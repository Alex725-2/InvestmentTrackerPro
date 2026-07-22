using InvestmentTracker.Server.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace InvestmentTracker.Server.Controllers
{
    [Route("bond")]
    [AllowAnonymous]
    public class BondController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public BondController(ApplicationDbContext context)
        {
            _context = context;
        }

        //[HttpGet("{ticker}")]
        //public async Task<IActionResult> GetBondPage(string ticker)
        //{
        //    var bond = await _context.Securities
        //        .FirstOrDefaultAsync(s => s.Ticker == ticker);

        //    if (bond == null) return NotFound();

        //    var events = await _context.PaymentEvents
        //        .Where(p => p.SecurityId == bond.Id && p.Date >= DateTime.Today)
        //        .OrderBy(p => p.Date)
        //        .ToListAsync();

        //    var sb = new StringBuilder();
        //    sb.AppendLine("<!DOCTYPE html>");
        //    sb.AppendLine("<html lang='ru'>");
        //    sb.AppendLine("<head><meta charset='UTF-8'><title>Выплаты по облигации " + bond.Name + "</title></head>");
        //    sb.AppendLine("<body>");
        //    sb.AppendLine($"<h1>Выплаты по облигации {bond.Name} ({bond.Ticker})</h1>");
        //    sb.AppendLine($"<p>ISIN: {bond.Isin}</p>");
        //    sb.AppendLine("<table border='1'><tr><th>Дата</th><th>Тип</th><th>Сумма на ед.</th><th>Валюта</th></tr>");

        //    foreach (var e in events)
        //    {
        //        sb.AppendLine($"<tr><td>{e.Date.ToString("dd.MM.yyyy")}</td><td>{GetTypeName(e.Type)}</td><td>{e.AmountPerUnit.ToString("F2")}</td><td>{e.Currency}</td></tr>");
        //    }

        //    sb.AppendLine("</table>");
        //    sb.AppendLine("<p>Выплаты купонов по облигации " + bond.Name + " (" + bond.Isin + "), календарь купонов, купоны по облигации " + bond.Ticker + "</p>");
        //    sb.AppendLine("</body></html>");

        //    return Content(sb.ToString(), "text/html; charset=utf-8");
        //}

        private string GetTypeName(string type) => type switch
        {
            "Dividend" => "Дивиденд",
            "Coupon" => "Купон",
            "Amortization" => "Амортизация",
            _ => type
        };
    }
}