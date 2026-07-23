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
            [FromQuery] int count = 500,
            [FromQuery] int? year = null,
            [FromQuery] int? month = null,
            [FromQuery] string? ticker = null,
            [FromQuery] bool includePast = false)
        {
            var query = _context.PaymentEvents.AsQueryable();

            // Фильтр по конкретному тикеру (используется на странице облигации)
            if (!string.IsNullOrEmpty(ticker))
            {
                query = query.Where(p => p.Ticker == ticker);
            }

            // Фильтр по дате
            if (!includePast)
            {
                if (year.HasValue && month.HasValue)
                {
                    var from = new DateTime(year.Value, month.Value, 1);
                    var to = from.AddMonths(1);
                    query = query.Where(p => p.Date >= from && p.Date < to);
                }
                else
                {
                    // По умолчанию — только будущие события
                    query = query.Where(p => p.Date >= DateTime.UtcNow.Date);
                }
            }
            else
            {
                // Когда includePast = true, можем ограничить годом/месяцем, если они заданы
                if (year.HasValue && month.HasValue)
                {
                    var from = new DateTime(year.Value, month.Value, 1);
                    var to = from.AddMonths(1);
                    query = query.Where(p => p.Date >= from && p.Date < to);
                }
                // Иначе возвращаем все события (включая прошлые)
            }

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
                .Include(p => p.Security)
                .Take(count)
                .Select(e => new PaymentEventDto
                {
                    Id = e.Id,
                    Ticker = e.Ticker,
                    Date = e.Date,
                    AmountPerUnit = e.AmountPerUnit,
                    Currency = e.Currency,
                    Type = e.Type,
                    SecurityName = e.Security.Name,
                    UserQuantity = null,
                    UserTotalAmount = null,
                    IsEstimated = e.IsEstimated
                })
                .ToListAsync();

            // Дозаполняем пользовательские данные
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
    }
}