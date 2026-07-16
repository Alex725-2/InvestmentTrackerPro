using InvestmentTracker.Server.Data;
using InvestmentTracker.Server.Models;
using InvestmentTracker.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static InvestmentTracker.Server.Services.MoexService;

namespace InvestmentTracker.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SecuritiesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public SecuritiesController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        [AllowAnonymous]   // гости и все могут видеть список бумаг
        public async Task<ActionResult<List<SecurityDto>>> GetAll()
        {
            var securities = await _context.Securities
                .Include(s => s.AssetType)
                .Select(s => new SecurityDto
                {
                    Id = s.Id,
                    Ticker = s.Ticker,
                    Isin = s.Isin,
                    Name = s.Name,
                    AssetTypeId = s.AssetTypeId,
                    AssetTypeName = s.AssetType.Name
                })
                .ToListAsync();

            return Ok(securities);
        }

        [HttpGet("{id}")]
        [AllowAnonymous]
        public async Task<ActionResult<SecurityDto>> GetById(int id)
        {
            var security = await _context.Securities
                .Include(s => s.AssetType)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (security == null) return NotFound();

            return Ok(new SecurityDto
            {
                Id = security.Id,
                Ticker = security.Ticker,
                Isin = security.Isin,
                Name = security.Name,
                AssetTypeId = security.AssetTypeId,
                AssetTypeName = security.AssetType.Name
            });
        }
        

        [HttpGet("lookup")]
        [AllowAnonymous]   // поиск тикера нужен всем
        public async Task<ActionResult<SecurityDto?>> Lookup([FromQuery] string? ticker, [FromQuery] string? isin)
        {
            if (string.IsNullOrWhiteSpace(ticker) && string.IsNullOrWhiteSpace(isin))
                return BadRequest("Specify ticker or isin");

            var moexService = HttpContext.RequestServices.GetRequiredService<Services.MoexService>();
            SecurityInfo? info = null;

            if (!string.IsNullOrWhiteSpace(isin))
            {
                info = await moexService.GetSecurityInfoByIsinAsync(isin);
            }
            else if (!string.IsNullOrWhiteSpace(ticker))
            {
                info = await moexService.LookupSecurityAsync(ticker);
            }

            if (info == null)
                return NotFound();

            return Ok(new SecurityDto
            {
                Ticker = info.Ticker,
                Isin = info.Isin,
                Name = info.Name,
                AssetTypeId = info.AssetTypeId ?? 0
            });
        }

        [HttpGet("{id}/price")]
        [AllowAnonymous]   // получение текущей цены нужно всем
        public async Task<ActionResult<decimal?>> GetCurrentPrice(int id)
        {
            var security = await _context.Securities.FindAsync(id);
            if (security == null) return NotFound();

            var latestQuote = await _context.Quotes
                .Where(q => q.SecurityId == id)
                .OrderByDescending(q => q.Date)
                .FirstOrDefaultAsync();

            if (latestQuote != null && latestQuote.Date > DateTime.UtcNow.AddMinutes(-15))
                return Ok(latestQuote.Price);

            var moexService = HttpContext.RequestServices.GetRequiredService<Services.MoexService>();
            var price = await moexService.GetCurrentPriceAsync(security.Ticker);
            if (price.HasValue)
            {
                _context.Quotes.Add(new Quote
                {
                    SecurityId = id,
                    Date = DateTime.UtcNow,
                    Price = price.Value,
                    Source = "MOEX_ISS"
                });
                await _context.SaveChangesAsync();
                return Ok(price);
            }
            return Ok((decimal?)null);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]   // только админ может добавить бумагу
        public async Task<ActionResult<SecurityDto>> Create(SecurityDto dto)
        {
            dto.Ticker = dto.Ticker.Trim().ToUpperInvariant();

            var existing = await _context.Securities
                .FirstOrDefaultAsync(s => s.Ticker == dto.Ticker);
            if (existing != null)
            {
                return Conflict($"Security with ticker '{dto.Ticker}' already exists.");
            }

            var security = new Security
            {
                Ticker = dto.Ticker,
                Isin = dto.Isin,
                Name = dto.Name,
                AssetTypeId = dto.AssetTypeId
            };

            _context.Securities.Add(security);
            await _context.SaveChangesAsync();

            dto.Id = security.Id;
            dto.AssetTypeName = (await _context.AssetTypes.FindAsync(dto.AssetTypeId))?.Name;

            return CreatedAtAction(nameof(GetById), new { id = security.Id }, dto);
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]   // только админ может удалять
        public async Task<IActionResult> Delete(int id)
        {
            var security = await _context.Securities.FindAsync(id);
            if (security == null) return NotFound();

            var inUse = await _context.Transactions.AnyAsync(t => t.SecurityId == id)
                     || await _context.PortfolioItems.AnyAsync(p => p.SecurityId == id);
            if (inUse)
            {
                return BadRequest("Security is used in transactions or portfolio and cannot be deleted.");
            }

            _context.Securities.Remove(security);
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}