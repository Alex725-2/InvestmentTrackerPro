using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using InvestmentTracker.Server.Data;
using System.Text;

namespace InvestmentTracker.Server.Controllers
{
    [Route("sitemap.xml")]
    [AllowAnonymous]
    public class SitemapController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public SitemapController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetSitemap()
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");

            // Главная страница
            sb.AppendLine($"  <url><loc>{baseUrl}/</loc></url>");
            // Календарь
            sb.AppendLine($"  <url><loc>{baseUrl}/calendar</loc></url>");

            // Все облигации
            var bonds = await _context.Securities
                .Where(s => s.AssetType.Name == "Облигация")
                .Select(s => s.Ticker)
                .ToListAsync();
            foreach (var ticker in bonds)
            {
                sb.AppendLine($"  <url><loc>{baseUrl}/bond/{ticker}</loc></url>");
            }

            sb.AppendLine("</urlset>");

            return Content(sb.ToString(), "text/xml; charset=utf-8");
        }
    }
}