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

        // GET api/securities/bonds – возвращает все облигации
        [HttpGet("bonds")]
        [AllowAnonymous]
        public async Task<ActionResult<List<SecurityDto>>> GetBonds()
        {
            var bonds = await _context.Securities
                .Where(s => s.AssetType.Name == "Облигация")
                .OrderBy(s => s.Ticker)
                .Select(s => new SecurityDto
                {
                    Id = s.Id,
                    Ticker = s.Ticker,
                    Isin = s.Isin,
                    Name = s.Name,
                    AssetTypeId = s.AssetTypeId,
                    AssetTypeName = s.AssetType.Name,
                    NextCouponDate = s.NextCouponDate,
                    IssueSize = s.IssueSize,
                    FaceValue = s.FaceValue,
                    AccruedInterest = s.AccruedInterest,
                    Rating = s.Rating
                })
                .ToListAsync();

            return Ok(bonds);
        }

        // GET api/securities/ofz – возвращает только ОФЗ
        [HttpGet("ofz")]
        [AllowAnonymous]
        public async Task<ActionResult<List<SecurityDto>>> GetOfz()
        {
            var ofz = await _context.Securities
                .Where(s => s.AssetType.Name == "Облигация" && s.Name.StartsWith("ОФЗ"))
                .OrderBy(s => s.Ticker)
                .Select(s => new SecurityDto
                {
                    Id = s.Id,
                    Ticker = s.Ticker,
                    Isin = s.Isin,
                    Name = s.Name,
                    AssetTypeId = s.AssetTypeId,
                    AssetTypeName = s.AssetType.Name,
                    NextCouponDate = s.NextCouponDate,
                    IssueSize = s.IssueSize,
                    FaceValue = s.FaceValue,
                    AccruedInterest = s.AccruedInterest,
                    Rating = s.Rating
                })
                .ToListAsync();

            return Ok(ofz);
        }

        // GET api/securities/byTicker/{ticker} – получение одной бумаги по тикеру
        [HttpGet("byTicker/{ticker}")]
        [AllowAnonymous]
        public async Task<ActionResult<SecurityDto>> GetByTicker(string ticker)
        {
            var security = await _context.Securities
                .Include(s => s.AssetType)
                .FirstOrDefaultAsync(s => s.Ticker == ticker.ToUpper());

            if (security == null) return NotFound();

            return Ok(new SecurityDto
            {
                Id = security.Id,
                Ticker = security.Ticker,
                Isin = security.Isin,
                Name = security.Name,
                AssetTypeId = security.AssetTypeId,
                AssetTypeName = security.AssetType?.Name,
                NextCouponDate = security.NextCouponDate,
                IssueSize = security.IssueSize,
                FaceValue = security.FaceValue,
                AccruedInterest = security.AccruedInterest,
                Rating = security.Rating
            });
        }

        // GET api/securities – получить все бумаги
        [HttpGet]
        [AllowAnonymous]
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
                    AssetTypeName = s.AssetType.Name,
                    NextCouponDate = s.NextCouponDate,
                    IssueSize = s.IssueSize,
                    FaceValue = s.FaceValue,
                    AccruedInterest = s.AccruedInterest,
                    Rating = s.Rating
                })
                .ToListAsync();

            return Ok(securities);
        }

        // POST api/securities/refresh-types – обновить типы (админ)
        [HttpPost("refresh-types")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> RefreshSecurityTypes()
        {
            var moexService = HttpContext.RequestServices.GetRequiredService<Services.MoexService>();
            var securities = await _context.Securities.ToListAsync();
            int updated = 0;
            var errors = new List<string>();

            foreach (var sec in securities)
            {
                try
                {
                    var info = await moexService.LookupSecurityAsync(sec.Ticker);
                    if (info?.AssetTypeId != null && sec.AssetTypeId != info.AssetTypeId)
                    {
                        sec.AssetTypeId = info.AssetTypeId.Value;
                        updated++;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"{sec.Ticker}: {ex.Message}");
                }
            }

            if (updated > 0) await _context.SaveChangesAsync();
            return Ok(new { Updated = updated, Total = securities.Count, Errors = errors.Take(10) });
        }

        // GET api/securities/{id} – получить бумагу по ID
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
                AssetTypeName = security.AssetType.Name,
                NextCouponDate = security.NextCouponDate,
                IssueSize = security.IssueSize,
                FaceValue = security.FaceValue,
                AccruedInterest = security.AccruedInterest,
                Rating = security.Rating
            });
        }

        // GET api/securities/lookup – поиск бумаги на MOEX
        [HttpGet("lookup")]
        [AllowAnonymous]
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

            // MOEX не возвращает детали, поэтому поля останутся null
            return Ok(new SecurityDto
            {
                Ticker = info.Ticker,
                Isin = info.Isin,
                Name = info.Name,
                AssetTypeId = info.AssetTypeId ?? 0,
                NextCouponDate = null,
                IssueSize = null,
                FaceValue = null,
                AccruedInterest = null,
                Rating = null
            });
        }

        // GET api/securities/{id}/price – текущая цена
        [HttpGet("{id}/price")]
        [AllowAnonymous]
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

        // POST api/securities – добавить бумагу
        [HttpPost]
        public async Task<ActionResult<SecurityDto>> Create(SecurityDto dto)
        {
            dto.Ticker = dto.Ticker.Trim().ToUpperInvariant();

            var existing = await _context.Securities
                .FirstOrDefaultAsync(s => s.Ticker == dto.Ticker);
            if (existing != null)
            {
                return Conflict($"Security with ticker '{dto.Ticker}' already exists.");
            }

            // Проверяем, что AssetTypeId ссылается на существующий тип
            if (dto.AssetTypeId <= 0 || !await _context.AssetTypes.AnyAsync(a => a.Id == dto.AssetTypeId))
            {
                return BadRequest($"Невозможно определить тип бумаги. Пожалуйста, выберите тип вручную.");
            }

            var security = new Security
            {
                Ticker = dto.Ticker,
                Isin = dto.Isin,
                Name = dto.Name,
                AssetTypeId = dto.AssetTypeId,
                NextCouponDate = dto.NextCouponDate,
                IssueSize = dto.IssueSize,
                FaceValue = dto.FaceValue,
                AccruedInterest = dto.AccruedInterest,
                Rating = dto.Rating
            };

            _context.Securities.Add(security);
            await _context.SaveChangesAsync();

            dto.Id = security.Id;
            dto.AssetTypeName = (await _context.AssetTypes.FindAsync(security.AssetTypeId))?.Name;

            return CreatedAtAction(nameof(GetById), new { id = security.Id }, dto);
        }

        // DELETE api/securities/{id} – удалить бумагу
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
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