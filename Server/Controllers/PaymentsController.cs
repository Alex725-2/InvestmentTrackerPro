using InvestmentTracker.Server.Data;
using InvestmentTracker.Server.Models;
using InvestmentTracker.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace InvestmentTracker.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PaymentsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public PaymentsController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("upcoming")]
        [AllowAnonymous]
        public async Task<ActionResult<List<PaymentEventDto>>> GetUpcoming(
            [FromQuery] bool myOnly = false,
            [FromQuery] int count = 50)   // увеличим count для просмотра
        {
            var query = _context.PaymentEvents
                .Where(p => p.Date >= DateTime.UtcNow.Date.AddYears(-1))  // <-- за последний год
                .OrderByDescending(p => p.Date)  // покажем последние
                .AsQueryable();


        string? userId = null;
            if (myOnly && User.Identity?.IsAuthenticated == true)
                userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!string.IsNullOrEmpty(userId))
            {
                var userTickers = await _context.PortfolioItems
                    .Where(p => p.UserId == userId && p.Quantity > 0)
                    .Select(p => p.Security.Ticker)
                    .Distinct()
                    .ToListAsync();
                query = query.Where(p => userTickers.Contains(p.Ticker));
            }

            var events = await query
                .Take(count)
                .Select(e => new PaymentEventDto
                {
                    Id = e.Id,
                    Ticker = e.Ticker,
                    Date = e.Date,
                    AmountPerUnit = e.AmountPerUnit,
                    Currency = e.Currency,
                    Type = e.Type,
                    UserQuantity = null,
                    UserTotalAmount = null
                })
                .ToListAsync();

            // Дозаполняем пользовательские данные, если нужно
            if (!string.IsNullOrEmpty(userId) && events.Count > 0)
            {
                var tickers = events.Select(e => e.Ticker).Distinct().ToList();
                var userPositions = await _context.PortfolioItems
                    .Where(p => p.UserId == userId && tickers.Contains(p.Security.Ticker))
                    .GroupBy(p => p.Security.Ticker)
                    .Select(g => new { Ticker = g.Key, TotalQuantity = g.Sum(x => x.Quantity) })
                    .ToListAsync();

                foreach (var evt in events)
                {
                    var pos = userPositions.FirstOrDefault(p => p.Ticker == evt.Ticker);
                    if (pos != null)
                    {
                        evt.UserQuantity = pos.TotalQuantity;
                        evt.UserTotalAmount = pos.TotalQuantity * evt.AmountPerUnit;
                    }
                }
            }

            return Ok(events);
        }


        [HttpGet("force-update")]
        [AllowAnonymous]
        public async Task<IActionResult> ForceUpdate()
        {
            var moex = HttpContext.RequestServices.GetRequiredService<Services.MoexService>();
            var context = HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();

            var securities = await context.Securities
                .Where(s => s.AssetType.Name == "Акция")
                .ToListAsync();

            foreach (var security in securities)
            {
                var dividends = await moex.GetDividendsAsync(security.Ticker);
                foreach (var div in dividends)
                {
                    var exists = await context.PaymentEvents.AnyAsync(e =>
                        e.SecurityId == security.Id &&
                        e.Date == div.Date &&
                        e.Type == "Dividend");

                    if (!exists)
                    {
                        context.PaymentEvents.Add(new PaymentEvent
                        {
                            Ticker = security.Ticker,
                            SecurityId = security.Id,
                            Date = div.Date,
                            AmountPerUnit = div.Amount,
                            Currency = div.Currency,
                            Type = "Dividend"
                        });
                    }
                }
            }
            await context.SaveChangesAsync();
            return Ok($"Updated {securities.Count} securities");
        }
    }
}