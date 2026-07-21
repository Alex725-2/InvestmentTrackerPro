using InvestmentTracker.Server.Data;
using InvestmentTracker.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Server.Services
{
    public class DividendLoaderService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DividendLoaderService> _logger;

        public DividendLoaderService(IServiceScopeFactory scopeFactory, ILogger<DividendLoaderService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task<int> LoadDividendsForMonthAsync(int year, int month)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var moexService = scope.ServiceProvider.GetRequiredService<MoexService>();

            var securities = await context.Securities
                .Where(s => s.AssetType.Name == "Акция")
                .ToListAsync();

            int added = 0;
            foreach (var security in securities)
            {
                var dividends = await moexService.GetDividendsAsync(security.Ticker);
                foreach (var div in dividends)
                {
                    if (div.Date.Year == year && div.Date.Month == month)
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
                            added++;
                        }
                    }
                }
            }

            if (added > 0)
                await context.SaveChangesAsync();

            return added;
        }
    }
}