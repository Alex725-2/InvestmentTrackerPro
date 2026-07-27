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
        public IActionResult GetSitemap()
        {
            var baseUrl = $"https://{Request.Host.Value}";
            var urls = new List<string>
    {
        $"{baseUrl}/",
        $"{baseUrl}/calendar",
        $"{baseUrl}/bonds",
        $"{baseUrl}/ofz"
    };

            var context = HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
            var bonds = context.Securities
                .Where(s => s.AssetType.Name == "Облигация")
                .Select(s => s.Ticker)
                .ToList();

            urls.AddRange(bonds.Select(ticker => $"{baseUrl}/bond/{ticker}"));

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
            foreach (var url in urls)
            {
                sb.AppendLine("  <url>");
                sb.AppendLine($"    <loc>{url}</loc>");
                sb.AppendLine($"    <lastmod>{DateTime.UtcNow:yyyy-MM-dd}</lastmod>");
                sb.AppendLine("  </url>");
            }
            sb.AppendLine("</urlset>");

            return Content(sb.ToString(), "application/xml; charset=utf-8");
        }
    }
}