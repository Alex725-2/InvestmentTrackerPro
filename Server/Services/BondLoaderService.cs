using InvestmentTracker.Server.Data;
using InvestmentTracker.Server.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace InvestmentTracker.Server.Services
{
    public class BondLoaderService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BondLoaderService> _logger;

        public BondLoaderService(IServiceScopeFactory scopeFactory, ILogger<BondLoaderService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task<int> LoadBondsAsync(string board = "TQCB,TQOB")
        {
            int added = 0;
            var boards = board.Split(',', StringSplitOptions.RemoveEmptyEntries);

            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var httpClient = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("Moex");

            foreach (var b in boards)
            {
                var url = $"https://iss.moex.com/iss/engines/stock/markets/bonds/boards/{b}/securities.json" +
                          "?iss.only=securities&securities.columns=SECID,SHORTNAME,ISIN";

                try
                {
                    using var stream = await httpClient.GetStreamAsync(url);
                    using var doc = await JsonDocument.ParseAsync(stream);

                    var root = doc.RootElement;
                    if (!root.TryGetProperty("securities", out var securitiesBlock))
                        continue;

                    if (!securitiesBlock.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
                        continue;

                    var columns = securitiesBlock.GetProperty("columns");
                    int secidIdx = FindColumnIndex(columns, "SECID");
                    int nameIdx = FindColumnIndex(columns, "SHORTNAME");
                    int isinIdx = FindColumnIndex(columns, "ISIN");

                    if (secidIdx == -1 || nameIdx == -1) continue;

                    foreach (var row in data.EnumerateArray())
                    {
                        var ticker = row[secidIdx].GetString();
                        var name = row[nameIdx].GetString();
                        var isin = isinIdx >= 0 ? row[isinIdx].GetString() : null;

                        if (string.IsNullOrWhiteSpace(ticker) || string.IsNullOrWhiteSpace(name))
                            continue;

                        // Проверяем, нет ли уже такой бумаги
                        var exists = await context.Securities.AnyAsync(s => s.Ticker == ticker);
                        if (!exists)
                        {
                            context.Securities.Add(new Security
                            {
                                Ticker = ticker!,
                                Name = name!,
                                Isin = isin,
                                AssetTypeId = 2   // Облигация
                            });
                            added++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка загрузки облигаций с доски {Board}", b);
                }
            }

            if (added > 0)
                await context.SaveChangesAsync();

            return added;
        }

        private int FindColumnIndex(JsonElement columns, string name)
        {
            for (int i = 0; i < columns.GetArrayLength(); i++)
                if (columns[i].GetString() == name) return i;
            return -1;
        }
    }
}