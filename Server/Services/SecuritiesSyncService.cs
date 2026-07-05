using System.Net.Http.Json;
using System.Text.Json;
using InvestmentTracker.Server.Data;
using InvestmentTracker.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Server.Services
{
    public class SecuritiesSyncService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SecuritiesSyncService> _logger;

        public SecuritiesSyncService(IServiceScopeFactory scopeFactory, ILogger<SecuritiesSyncService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Securities sync service starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SyncSecuritiesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error syncing securities");
                }

                // Раз в 24 часа
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
        }

        public async Task SyncSecuritiesAsync(CancellationToken stoppingToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var httpClient = scope.ServiceProvider.GetRequiredService<HttpClient>();

            var url = "https://iss.moex.com/iss/engines/stock/markets/shares/boards/TQBR/securities.json";
            using var stream = await httpClient.GetStreamAsync(url, stoppingToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: stoppingToken);

            var root = doc.RootElement;
            if (!root.TryGetProperty("securities", out var secBlock) ||
                !secBlock.TryGetProperty("data", out var data) ||
                data.GetArrayLength() == 0)
                return;

            var columns = secBlock.GetProperty("columns");
            int secidIdx = FindColumn(columns, "SECID");
            int nameIdx = FindColumn(columns, "SHORTNAME");
            int isinIdx = FindColumn(columns, "ISIN");

            if (secidIdx == -1 || nameIdx == -1) return;

            var assetType = await context.AssetTypes.FirstOrDefaultAsync(a => a.Name == "Акция", stoppingToken);
            if (assetType == null) return;

            foreach (var row in data.EnumerateArray())
            {
                var ticker = row[secidIdx].GetString();
                var name = row[nameIdx].GetString();
                var isin = isinIdx >= 0 ? row[isinIdx].GetString() : null;

                if (string.IsNullOrWhiteSpace(ticker)) continue;

                var exists = await context.Securities.AnyAsync(s => s.Ticker == ticker, stoppingToken);
                if (!exists)
                {
                    context.Securities.Add(new Security
                    {
                        Ticker = ticker,
                        Name = name ?? ticker,
                        Isin = isin,
                        AssetTypeId = assetType.Id
                    });
                }
            }

            await context.SaveChangesAsync(stoppingToken);
            _logger.LogInformation("Securities sync completed.");
        }

        private int FindColumn(JsonElement columns, string name)
        {
            for (int i = 0; i < columns.GetArrayLength(); i++)
                if (columns[i].GetString() == name) return i;
            return -1;
        }
    }
}