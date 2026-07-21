using InvestmentTracker.Server.Data;
using InvestmentTracker.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Server.Services
{
    public class DividendUpdateService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DividendUpdateService> _logger;

        public DividendUpdateService(IServiceScopeFactory scopeFactory, ILogger<DividendUpdateService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Dividend update service starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var moex = scope.ServiceProvider.GetRequiredService<MoexService>();

                    var securities = await context.Securities
                        .Where(s => s.AssetType.Name == "Акция")
                        .ToListAsync(stoppingToken);

                    foreach (var security in securities)
                    {
                        if (stoppingToken.IsCancellationRequested) break;

                        var dividends = await moex.GetDividendsAsync(security.Ticker);
                        foreach (var div in dividends)
                        {
                            // Проверяем, есть ли уже такое событие
                            var exists = await context.PaymentEvents.AnyAsync(e =>
                                e.SecurityId == security.Id &&
                                e.Date == div.Date &&
                                e.Type == "Dividend", stoppingToken);

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

                    await context.SaveChangesAsync(stoppingToken);
                    _logger.LogInformation("Dividend update completed.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in dividend update.");
                }

                // Следующее обновление через 24 часа
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
                //await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        public async Task<int> LoadDividendsForMonthSyncAsync(int year, int month)
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