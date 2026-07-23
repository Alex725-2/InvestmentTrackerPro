using Microsoft.EntityFrameworkCore;
using InvestmentTracker.Server.Data;
using System.Text;

namespace InvestmentTracker.Server.Middleware
{
    public class SeoMiddleware
    {
        private readonly RequestDelegate _next;

        public SeoMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Проверяем, является ли клиент поисковым ботом
            var userAgent = context.Request.Headers["User-Agent"].ToString().ToLower();
            bool isBot = userAgent.Contains("googlebot") || userAgent.Contains("yandexbot") ||
                         userAgent.Contains("bingbot") || userAgent.Contains("duckduckbot");

            var path = context.Request.Path.Value;

            // Обработка /calendar для ботов
            if (isBot && path != null && path.StartsWith("/calendar"))
            {
                using var scope = context.RequestServices.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // Получаем ближайшие 100 будущих выплат (или за текущий месяц)
                var from = DateTime.Today;
                var events = await db.PaymentEvents
                    .Where(p => p.Date >= from)
                    .OrderBy(p => p.Date)
                    .Take(100)
                    .ToListAsync();

                var html = new StringBuilder();
                html.AppendLine("<!DOCTYPE html><html lang='ru'><head><meta charset='UTF-8'>");
                html.AppendLine("<meta name='description' content='Календарь выплат дивидендов, купонов и амортизаций по облигациям. Ближайшие выплаты, купоны, амортизации.'>");
                html.AppendLine("<meta name='keywords' content='календарь выплат, купоны, амортизации, дивиденды, облигации, инвестиции, календарь инвестора'>");
                html.AppendLine("<title>Календарь выплат по облигациям</title>");
                html.AppendLine("<link href='https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/css/bootstrap.min.css' rel='stylesheet'></head><body class='container mt-4'>");
                html.AppendLine("<h1>Календарь выплат</h1>");
                html.AppendLine("<p>Ближайшие выплаты по облигациям, купоны и амортизации.</p>");

                if (events.Any())
                {
                    html.AppendLine("<table class='table table-striped'><thead><tr><th>Дата</th><th>Тикер</th><th>Тип</th><th>Сумма на ед.</th><th>Валюта</th></tr></thead><tbody>");
                    foreach (var e in events)
                    {
                        html.AppendLine($"<tr><td>{e.Date:dd.MM.yyyy}</td><td>{e.Ticker}</td><td>{GetTypeName(e.Type)}</td><td>{e.AmountPerUnit:F2}</td><td>{e.Currency}</td></tr>");
                    }
                    html.AppendLine("</tbody></table>");
                }
                else
                {
                    html.AppendLine("<p>Нет предстоящих выплат.</p>");
                }

                html.AppendLine("<p><a href='/'>На главную</a></p>");
                html.AppendLine("</body></html>");

                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync(html.ToString());
                return;
            }

            // Существующая обработка /bond/{ticker} для ботов
            if (isBot && path != null && path.StartsWith("/bond/"))
            {
                var ticker = path.Substring("/bond/".Length);
                if (!string.IsNullOrEmpty(ticker))
                {
                    using var scope = context.RequestServices.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                    var bond = await db.Securities.FirstOrDefaultAsync(s => s.Ticker == ticker);
                    if (bond != null)
                    {
                        var bondEvents = await db.PaymentEvents
                            .Where(p => p.SecurityId == bond.Id && p.Date >= DateTime.Today)
                            .OrderBy(p => p.Date)
                            .ToListAsync();

                        var html = new StringBuilder();
                        html.AppendLine("<!DOCTYPE html><html lang='ru'><head><meta charset='UTF-8'>");
                        html.AppendLine($"<meta name='description' content='Выплаты по облигации {bond.Name} (ISIN: {bond.Isin}). Календарь купонов, амортизации.'>");
                        html.AppendLine($"<meta name='keywords' content='выплаты по облигациям {bond.Name}, купоны по облигациям {bond.Isin}, амортизации по облигациям {bond.Name}, предстоящие выплаты {bond.Isin}, следующие выплаты {bond.Name}, облигации {bond.Name} график, облигации {bond.Isin} график'>");
                        html.AppendLine($"<title>Выплаты по облигации {bond.Name} ({bond.Ticker})</title>");
                        html.AppendLine("<link href='https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/css/bootstrap.min.css' rel='stylesheet'></head><body class='container mt-4'>");
                        html.AppendLine($"<h1>{bond.Name} ({bond.Ticker})</h1>");
                        if (!string.IsNullOrEmpty(bond.Isin)) html.AppendLine($"<p>ISIN: {bond.Isin}</p>");
                        html.AppendLine("<table class='table table-striped'><thead><tr><th>Дата</th><th>Тип</th><th>Сумма на ед.</th><th>Валюта</th></tr></thead><tbody>");
                        foreach (var e in bondEvents)
                        {
                            html.AppendLine($"<tr><td>{e.Date:dd.MM.yyyy}</td><td>{GetTypeName(e.Type)}</td><td>{e.AmountPerUnit:F2}</td><td>{e.Currency}</td></tr>");
                        }
                        html.AppendLine("</tbody></table>");
                        html.AppendLine("<p><a href='/calendar'>Календарь выплат</a></p>");
                        html.AppendLine("</body></html>");

                        context.Response.ContentType = "text/html; charset=utf-8";
                        await context.Response.WriteAsync(html.ToString());
                        return;
                    }
                }
            }

            await _next(context);
        }

        private string GetTypeName(string type) => type switch
        {
            "Dividend" => "Дивиденд",
            "Coupon" => "Купон",
            "Amortization" => "Амортизация",
            _ => type
        };
    }
}