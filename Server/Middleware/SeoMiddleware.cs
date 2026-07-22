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

            // Проверяем, что запрос к /bond/{ticker}
            var path = context.Request.Path.Value;
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
                        var events = await db.PaymentEvents
                            .Where(p => p.SecurityId == bond.Id && p.Date >= DateTime.Today)
                            .OrderBy(p => p.Date)
                            .ToListAsync();

                        // Генерируем HTML, похожий на наш дизайн (подключаем Bootstrap)
                        var html = new StringBuilder();
                        html.AppendLine("<!DOCTYPE html>");
                        html.AppendLine("<html lang='ru'>");
                        html.AppendLine("<head>");
                        html.AppendLine("<meta charset='UTF-8'>");
                        html.AppendLine($"<meta name='description' content='Предстоящие выплаты купонов и амортизаций по облигации {bond.Name} (ISIN: {bond.Isin}). Календарь выплат, купоны, амортизации.'>");
                        html.AppendLine($"<meta name='keywords' content='выплаты по облигациям {bond.Name}, купоны по облигациям {bond.Isin}, амортизации по облигациям {bond.Name}, амортизации по облигациям {bond.Isin}, предстоящие выплаты по облигациям {bond.Name}, предстоящие выплаты по облигациям {bond.Isin}, следующие выплаты по облигациям {bond.Name}, следующие выплаты по облигациям {bond.Isin}, облигации {bond.Name} график, облигации {bond.Isin} график, инвестиции, календарь купонов, облигации федерального займа, ОФЗ, корпоративные облигации'>");
                        html.AppendLine("<title>Выплаты по облигации " + bond.Name + "</title>");
                        // Подключаем Bootstrap, чтобы дизайн был похож на приложение
                        html.AppendLine("<link href='https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/css/bootstrap.min.css' rel='stylesheet'>");
                        html.AppendLine("</head>");
                        html.AppendLine("<body class='container mt-4'>");
                        
                        html.AppendLine($"<h1>Облигация: {bond.Name} ({bond.Ticker})</h1>");
                        html.AppendLine($"<p><strong>ISIN:</strong> {bond.Isin ?? "—"}</p>");
                        html.AppendLine("<h3>Предстоящие выплаты</h3>");

                        if (events.Any())
                        {
                            html.AppendLine("<table class='table table-striped'>");
                            html.AppendLine("<thead><tr><th>Дата</th><th>Тип</th><th>Сумма на ед.</th><th>Валюта</th></tr></thead>");
                            html.AppendLine("<tbody>");
                            foreach (var e in events)
                            {
                                html.AppendLine($"<tr><td>{e.Date:dd.MM.yyyy}</td><td>{GetTypeName(e.Type)}</td><td>{e.AmountPerUnit:F2}</td><td>{e.Currency}</td></tr>");
                            }
                            html.AppendLine("</tbody></table>");
                        }
                        else
                        {
                            html.AppendLine("<p>Нет предстоящих выплат.</p>");
                        }

                        html.AppendLine("<p>Выплаты купонов по облигации " + bond.Name + " (" + bond.Isin + "), календарь купонов, купоны по облигации " + bond.Ticker + "</p>");
                        html.AppendLine("</div></body></html>");

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