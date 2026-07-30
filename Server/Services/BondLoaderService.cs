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
        private readonly BackgroundJobStatusService _statusService;

        public BondLoaderService(IServiceScopeFactory scopeFactory, ILogger<BondLoaderService> logger, BackgroundJobStatusService statusService)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _statusService = statusService;
        }

        public async Task<int> LoadBondsAsync(string board = "TQCB,TQOB")
        {
            _statusService.SetRunning("load-bonds");
            var boards = board.Split(',', StringSplitOptions.RemoveEmptyEntries);
            int added = 0;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var httpClient = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("Moex");
                var moexService = scope.ServiceProvider.GetRequiredService<MoexService>();

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

                            var exists = await context.Securities.AnyAsync(s => s.Ticker == ticker);
                            if (!exists)
                            {
                                var security = new Security
                                {
                                    Ticker = ticker!,
                                    Name = name!,
                                    Isin = isin,
                                    AssetTypeId = 2   // Облигация
                                };

                                // Сразу пытаемся заполнить детали (купон, номинал, объём)
                                try
                                {
                                    await FillBondDetailsInline(moexService, security);
                                }
                                catch { /* не критично, бумага добавится и без деталей */ }

                                context.Securities.Add(security);
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
            }
            finally
            {
                _statusService.SetCompleted("load-bonds");
            }

            return added;
        }

        // Вспомогательный метод, чтобы не дублировать логику парсинга
        private static async Task FillBondDetailsInline(MoexService moexService, Security bond)
        {
            try
            {
                var url = $"https://iss.moex.com/iss/securities/{bond.Ticker.ToUpper()}.json";
                using var httpClient = new HttpClient();
                using var stream = await httpClient.GetStreamAsync(url);
                using var doc = await JsonDocument.ParseAsync(stream);
                var root = doc.RootElement;

                if (root.TryGetProperty("description", out var desc) && desc.TryGetProperty("data", out var data))
                {
                    foreach (var row in data.EnumerateArray())
                    {
                        var name = row[0].GetString();
                        var value = row[2].GetString();
                        switch (name)
                        {
                            case "COUPONDATE":
                                if (DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                                        System.Globalization.DateTimeStyles.None, out var cd))
                                    bond.NextCouponDate = cd;
                                break;
                            case "ISSUESIZE":
                                if (long.TryParse(value, out var issueSize))
                                    bond.IssueSize = issueSize;
                                break;
                            case "FACEVALUE":
                                if (decimal.TryParse(value, System.Globalization.NumberStyles.Any,
                                        System.Globalization.CultureInfo.InvariantCulture, out var fv))
                                    bond.FaceValue = fv;
                                break;
                        }
                    }
                }
            }
            catch
            {
                // молча пропускаем
            }
        }

        private int FindColumnIndex(JsonElement columns, string name)
        {
            for (int i = 0; i < columns.GetArrayLength(); i++)
                if (columns[i].GetString() == name) return i;
            return -1;
        }
    }
}