using InvestmentTracker.Shared.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace InvestmentTracker.Server.Services
{
    public class MoexService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<MoexService> _logger;

        public MoexService(HttpClient httpClient, ILogger<MoexService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<List<(DateTime Date, decimal Amount, string Currency)>> GetCouponsAsync(string ticker)
        {
            var result = new List<(DateTime, decimal, string)>();
            try
            {
                var url = $"https://iss.moex.com/iss/securities/{ticker.ToUpper()}/coupons.json";
                _logger.LogInformation("Fetching coupons for {Ticker}, URL: {Url}", ticker, url);
                using var stream = await _httpClient.GetStreamAsync(url);
                using var doc = await JsonDocument.ParseAsync(stream);

                var root = doc.RootElement;
                if (!root.TryGetProperty("coupons", out var coupons))
                {
                    _logger.LogWarning("No 'coupons' block for {Ticker}", ticker);
                    return result;
                }

                if (!coupons.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
                {
                    _logger.LogInformation("No coupon data for {Ticker}", ticker);
                    return result;
                }

                var columns = coupons.GetProperty("columns");
                int dateIdx = FindColumnIndex(columns, "coupondate");
                int valueIdx = FindColumnIndex(columns, "value");
                int currencyIdx = FindColumnIndex(columns, "currencyid");

                if (dateIdx == -1 || valueIdx == -1)
                {
                    _logger.LogWarning("Required columns not found for {Ticker}", ticker);
                    return result;
                }

                foreach (var row in data.EnumerateArray())
                {
                    var dateStr = row[dateIdx].GetString();
                    if (string.IsNullOrWhiteSpace(dateStr)) continue;
                    if (!DateTime.TryParse(dateStr, out var date)) continue;

                    if (row[valueIdx].ValueKind == JsonValueKind.Null)
                    {
                        _logger.LogDebug("Null value for coupon on {Date} for {Ticker}", dateStr, ticker);
                        continue;
                    }

                    decimal amount = 0;
                    if (row[valueIdx].ValueKind == JsonValueKind.Number)
                        amount = row[valueIdx].GetDecimal();
                    else if (row[valueIdx].ValueKind == JsonValueKind.String && decimal.TryParse(row[valueIdx].GetString(), out var parsed))
                        amount = parsed;
                    else
                        continue;

                    var currency = currencyIdx >= 0 ? row[currencyIdx].GetString() ?? "RUB" : "RUB";
                    result.Add((date, amount, currency));
                }

                _logger.LogInformation("Found {Count} coupons for {Ticker}", result.Count, ticker);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error fetching coupons for {Ticker}", ticker);
            }
            return result;
        }

        public async Task<List<(DateTime Date, decimal Amount, string Currency)>> GetAmortizationsAsync(string ticker)
        {
            var result = new List<(DateTime, decimal, string)>();
            try
            {
                var url = $"https://iss.moex.com/iss/securities/{ticker.ToUpper()}/amortizations.json";
                using var stream = await _httpClient.GetStreamAsync(url);
                using var doc = await JsonDocument.ParseAsync(stream);

                var root = doc.RootElement;
                if (!root.TryGetProperty("amortizations", out var amorts) ||
                    !amorts.TryGetProperty("data", out var data) ||
                    data.GetArrayLength() == 0)
                {
                    _logger.LogInformation("No amortization data for {Ticker}", ticker);
                    return result;
                }

                var columns = amorts.GetProperty("columns");
                int dateIdx = FindColumnIndex(columns, "amortdate");
                int valueIdx = FindColumnIndex(columns, "value");
                int currencyIdx = FindColumnIndex(columns, "currencyid");

                if (dateIdx == -1 || valueIdx == -1)
                {
                    _logger.LogWarning("Required columns not found for amortizations of {Ticker}", ticker);
                    return result;
                }

                foreach (var row in data.EnumerateArray())
                {
                    var dateStr = row[dateIdx].GetString();
                    if (string.IsNullOrWhiteSpace(dateStr)) continue;
                    if (!DateTime.TryParse(dateStr, out var date)) continue;

                    // Пропускаем, если значение null
                    if (row[valueIdx].ValueKind == JsonValueKind.Null)
                        continue;

                    decimal amount = 0;
                    if (row[valueIdx].ValueKind == JsonValueKind.Number)
                        amount = row[valueIdx].GetDecimal();
                    else if (row[valueIdx].ValueKind == JsonValueKind.String &&
                             decimal.TryParse(row[valueIdx].GetString(), out var parsed))
                        amount = parsed;
                    else
                        continue;

                    var currency = currencyIdx >= 0 ? row[currencyIdx].GetString() ?? "RUB" : "RUB";
                    result.Add((date, amount, currency));
                }
                _logger.LogInformation("Found {Count} amortizations for {Ticker}", result.Count, ticker);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error fetching amortizations for {Ticker}", ticker);
            }
            return result;
        }

        // Получить дату и сумму ближайшего купона из description
        public async Task<(DateTime? Date, decimal? Amount, string Currency)> GetNextCouponFromDescriptionAsync(string ticker)
        {
            try
            {
                var url = $"https://iss.moex.com/iss/securities/{ticker.ToUpper()}/coupons.json";
                using var stream = await _httpClient.GetStreamAsync(url);
                using var doc = await JsonDocument.ParseAsync(stream);
                var root = doc.RootElement;

                if (root.TryGetProperty("description", out var desc) &&
                    desc.TryGetProperty("data", out var data))
                {
                    foreach (var row in data.EnumerateArray())
                    {
                        var name = row[0].GetString();
                        var value = row[2].GetString();
                        if (name == "COUPONDATE" && DateTime.TryParse(value, out var coupDate) && coupDate >= DateTime.Today)
                        {
                            // Нашли дату купона, теперь ищем сумму купона
                            decimal? amount = null;
                            string currency = "RUB";
                            foreach (var inner in data.EnumerateArray())
                            {
                                if (inner[0].GetString() == "COUPONVALUE" && decimal.TryParse(inner[2].GetString(), out var amt))
                                {
                                    amount = amt;
                                    break;
                                }
                            }
                            return (coupDate, amount, currency);
                        }
                    }
                }
            }
            catch { /* игнорируем */ }
            return (null, null, "RUB");
        }

        // Получить дату погашения и номинал из description
        public async Task<(DateTime? Date, decimal? Amount, string Currency)> GetNextAmortizationFromDescriptionAsync(string ticker)
        {
            try
            {
                var url = $"https://iss.moex.com/iss/securities/{ticker.ToUpper()}/amortizations.json";
                using var stream = await _httpClient.GetStreamAsync(url);
                using var doc = await JsonDocument.ParseAsync(stream);
                var root = doc.RootElement;

                if (root.TryGetProperty("description", out var desc) &&
                    desc.TryGetProperty("data", out var data))
                {
                    DateTime? matDate = null;
                    decimal? faceValue = null;
                    string currency = "RUB";

                    foreach (var row in data.EnumerateArray())
                    {
                        var name = row[0].GetString();
                        var value = row[2].GetString();
                        if (name == "MATDATE" && DateTime.TryParse(value, out var d) && d >= DateTime.Today)
                            matDate = d;
                        else if (name == "FACEVALUE" && decimal.TryParse(value, out var fv))
                            faceValue = fv;
                    }
                    if (matDate.HasValue)
                        return (matDate, faceValue, currency);
                }
            }
            catch { /* игнорируем */ }
            return (null, null, "RUB");
        }

        // --------------------------------------------------
        // УНИВЕРСАЛЬНЫЙ ПОИСК БУМАГИ ПО ТИКЕРУ (АКЦИИ, ОБЛИГАЦИИ, ПИФЫ)
        // --------------------------------------------------
        public async Task<SecurityInfo?> LookupSecurityAsync(string ticker)
        {
            // Пробуем акции (TQBR), но смотрим на SECTYPE (может быть ПИФ/ETF)
            var info = await GetSecurityInfoFromMarketAsync(ticker, "shares", "TQBR");
            if (info != null) return info;

            // Облигации
            info = await GetSecurityInfoFromMarketAsync(ticker, "bonds", "TQCB");
            if (info != null) return info;
            info = await GetSecurityInfoFromMarketAsync(ticker, "bonds", "TQOB");
            if (info != null) return info;

            // Паи ПИФов (специализированный борд)
            info = await GetSecurityInfoFromMarketAsync(ticker, "shares", "TQTF");
            if (info != null) return info;
            _logger.LogWarning("LookupSecurityAsync: ticker={Ticker} not found on any market", ticker);
            return null;
        }

        private async Task<SecurityInfo?> GetSecurityInfoFromMarketAsync(string ticker, string market, string board)
        {
            try
            {
                var url = $"https://iss.moex.com/iss/engines/stock/markets/{market}/boards/{board}/securities/{ticker.ToUpper()}.json";
                using var stream = await _httpClient.GetStreamAsync(url);
                using var doc = await JsonDocument.ParseAsync(stream);

                var root = doc.RootElement;
                if (!root.TryGetProperty("securities", out var securitiesBlock))
                    return null;

                if (!securitiesBlock.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
                    return null;

                var columns = securitiesBlock.GetProperty("columns");
                int secidIdx = FindColumnIndex(columns, "SECID");
                int isinIdx = FindColumnIndex(columns, "ISIN");
                int nameIdx = FindColumnIndex(columns, "SHORTNAME");
                int secTypeIdx = FindColumnIndex(columns, "SECTYPE"); // важно для ETF

                if (secidIdx == -1 || nameIdx == -1) return null;

                var row = data[0];
                string? secid = row[secidIdx].GetString();
                string? isin = isinIdx >= 0 ? row[isinIdx].GetString() : null;
                string? name = row[nameIdx].GetString();
                string? secType = secTypeIdx >= 0 ? row[secTypeIdx].GetString() : null;
                _logger.LogInformation("Ticker={Ticker}, market={Market}, board={Board}, SECTYPE={SecType}",
    ticker, market, board, secType);
                int? assetTypeIdlog = MapSecTypeToAssetTypeId(secType) ?? (market == "bonds" ? 2 : null);
                _logger.LogInformation("Computed AssetTypeId={AssetTypeIdlog}", assetTypeIdlog);

                if (string.IsNullOrWhiteSpace(secid) || string.IsNullOrWhiteSpace(name))
                    return null;

                // Определяем тип по SECTYPE в первую очередь
                int? assetTypeId = MapSecTypeToAssetTypeId(secType);

                // Если по SECTYPE не определили, пробуем угадать по рынку (запасной вариант)
                if (assetTypeId == null)
                {
                    assetTypeId = market switch
                    {
                        "bonds" => 2,
                        "shares" => board == "TQTF" ? 3 : 1, // если TQTF – паи, иначе акция (но это неточно)
                        _ => null
                    };
                }

                return new SecurityInfo
                {
                    Ticker = secid,
                    Isin = isin,
                    Name = name,
                    AssetTypeId = assetTypeId
                };
            }
            catch
            {
                return null;
            }
        }

        // МАППИНГ ТИПОВ (добавлен ETF)
        private int? MapSecTypeToAssetTypeId(string? secType)
        {
            return secType switch
            {
                "1" => 1, // Акция
                "2" => 2, // Облигация
                "9" => 3, // ПИФ
                "E" => 4, // ETF
                "J" => 4, // ETF (альтернативный код)
                _ => null
            };
        }

        // --------------------------------------------------
        // ПОИСК БУМАГИ ПО ISIN (С АЛЬТЕРНАТИВНЫМ ПОДХОДОМ)
        // --------------------------------------------------
        public async Task<SecurityInfo?> GetSecurityInfoByIsinAsync(string isin)
        {
            try
            {
                // Способ 1: прямой запрос https://iss.moex.com/iss/securities/{isin}.json
                var url1 = $"https://iss.moex.com/iss/securities/{isin}.json";
                using var stream1 = await _httpClient.GetStreamAsync(url1);
                using var doc1 = await JsonDocument.ParseAsync(stream1);
                var root1 = doc1.RootElement;

                // Пробуем получить данные из блока description (он может быть пустым)
                if (root1.TryGetProperty("description", out var desc) &&
                    desc.TryGetProperty("data", out var data1) &&
                    data1.GetArrayLength() > 0)
                {
                    var secInfo = ParseDescription(data1, desc.GetProperty("columns"));
                    if (secInfo != null && !string.IsNullOrWhiteSpace(secInfo.Name))
                        return secInfo; // если имя есть — возвращаем

                    // Если имя пустое, пробуем добрать по тикеру
                    if (secInfo != null && !string.IsNullOrWhiteSpace(secInfo.Ticker))
                    {
                        var lookupInfo = await LookupSecurityAsync(secInfo.Ticker);
                        if (lookupInfo != null) return lookupInfo;
                    }
                }

                // Способ 2: если description пуст, используем поиск через q=
                var url2 = $"https://iss.moex.com/iss/securities.json?q={isin}";
                using var stream2 = await _httpClient.GetStreamAsync(url2);
                using var doc2 = await JsonDocument.ParseAsync(stream2);
                var root2 = doc2.RootElement;

                if (root2.TryGetProperty("securities", out var secBlock) &&
                    secBlock.TryGetProperty("data", out var data2) &&
                    data2.GetArrayLength() > 0)
                {
                    var secInfo = ParseSecuritiesSearchResult(data2, secBlock.GetProperty("columns"));
                    if (secInfo != null && !string.IsNullOrWhiteSpace(secInfo.Name))
                        return secInfo;

                    // Если имя пустое, добираем по тикеру
                    if (secInfo != null && !string.IsNullOrWhiteSpace(secInfo.Ticker))
                    {
                        var lookupInfo = await LookupSecurityAsync(secInfo.Ticker);
                        if (lookupInfo != null) return lookupInfo;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error fetching security by ISIN {Isin}", isin);
            }
            return null;
        }

        // Парсинг блока description
        private SecurityInfo? ParseDescription(JsonElement data, JsonElement columns)
        {
            var cols = columns.EnumerateArray().Select(c => c.GetString()).ToArray();
            int secidIdx = Array.IndexOf(cols, "secid");
            int isinIdx = Array.IndexOf(cols, "isin");
            int nameIdx = Array.IndexOf(cols, "name");
            int groupIdx = Array.IndexOf(cols, "group");

            if (secidIdx == -1 || nameIdx == -1) return null;

            var row = data[0];
            string? secid = row[secidIdx].GetString();
            string? isinOut = isinIdx >= 0 ? row[isinIdx].GetString() : null;
            string? name = row[nameIdx].GetString();
            string? group = groupIdx >= 0 ? row[groupIdx].GetString() : null;

            return CreateSecurityInfo(secid, isinOut, name, group);
        }

        // Парсинг результата поиска (securities.json?q=)
        private SecurityInfo? ParseSecuritiesSearchResult(JsonElement data, JsonElement columns)
        {
            var cols = columns.EnumerateArray().Select(c => c.GetString()).ToArray();
            int secidIdx = Array.IndexOf(cols, "secid");
            int isinIdx = Array.IndexOf(cols, "isin");
            int nameIdx = Array.IndexOf(cols, "name");
            int groupIdx = Array.IndexOf(cols, "group");

            if (secidIdx == -1 || nameIdx == -1) return null;

            // Берём первый подходящий результат
            var row = data[0];
            string? secid = row[secidIdx].GetString();
            string? isinOut = isinIdx >= 0 ? row[isinIdx].GetString() : null;
            string? name = row[nameIdx].GetString();
            string? group = groupIdx >= 0 ? row[groupIdx].GetString() : null;

            return CreateSecurityInfo(secid, isinOut, name, group);
        }

        // Фабрика SecurityInfo
        private SecurityInfo? CreateSecurityInfo(string? secid, string? isin, string? name, string? group)
        {
            if (string.IsNullOrWhiteSpace(secid) || string.IsNullOrWhiteSpace(name))
                return null;

            int? assetTypeId = group switch
            {
                "stock_shares" => 1,
                "stock_bonds" => 2,
                "stock_ppif" => 3,
                _ => null
            };

            return new SecurityInfo
            {
                Ticker = secid,
                Isin = isin,
                Name = name,
                AssetTypeId = assetTypeId
            };
        }

      
        public async Task<decimal?> GetCurrentPriceAsync(string ticker)
        {
            // Пробуем все рынки по очереди
            var price = await GetPriceFromBoardAsync(ticker, "shares", "TQBR"); // Акции
            if (price.HasValue) return price;

            price = await GetPriceFromBoardAsync(ticker, "bonds", "TQCB"); // Облигации
            if (price.HasValue) return price;

            price = await GetPriceFromBoardAsync(ticker, "shares", "TQTF"); // ПИФы
            if (price.HasValue) return price;


            price = await GetPriceFromBoardAsync(ticker, "bonds", "TQOB");
            return price;
        }

        private async Task<decimal?> GetPriceFromBoardAsync(string ticker, string market, string board)
        {
            try
            {
                var url = $"https://iss.moex.com/iss/engines/stock/markets/{market}/boards/{board}/securities/{ticker.ToUpper()}.json";
                using var stream = await _httpClient.GetStreamAsync(url);
                using var doc = await JsonDocument.ParseAsync(stream);

                var root = doc.RootElement;
                if (!root.TryGetProperty("marketdata", out var marketdata))
                    return null;

                if (!marketdata.TryGetProperty("columns", out var columns) ||
                    !marketdata.TryGetProperty("data", out var data) ||
                    data.GetArrayLength() == 0)
                    return null;

                int lastIdx = -1;
                for (int i = 0; i < columns.GetArrayLength(); i++)
                {
                    if (columns[i].GetString() == "LAST")
                    {
                        lastIdx = i;
                        break;
                    }
                }
                if (lastIdx == -1) return null;

                var row = data[0];
                if (row.GetArrayLength() <= lastIdx) return null;

                var lastElement = row[lastIdx];
                if (lastElement.ValueKind == JsonValueKind.Number && lastElement.TryGetDecimal(out var price))
                    return price;
                if (lastElement.ValueKind == JsonValueKind.String && decimal.TryParse(lastElement.GetString(), out var parsedPrice))
                    return parsedPrice;

                return null;
            }
            catch
            {
                return null;
            }
        }

        // --------------------------------------------------
        // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
        // --------------------------------------------------
        private int FindColumnIndex(JsonElement columns, string name)
        {
            for (int i = 0; i < columns.GetArrayLength(); i++)
            {
                if (columns[i].GetString() == name)
                    return i;
            }
            return -1;
        }

        private int? FindColumnIndexCaseInsensitive(string[] columns, string name)
        {
            for (int i = 0; i < columns.Length; i++)
            {
                if (string.Equals(columns[i], name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private decimal? ParseDecimal(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number)
                return element.GetDecimal();
            if (element.ValueKind == JsonValueKind.String && decimal.TryParse(element.GetString(), out var parsed))
                return parsed;
            return null;
        }

        // --------------------------------------------------
        // ОСТАЛЬНЫЕ МЕТОДЫ (БЕЗ ИЗМЕНЕНИЙ)
        // --------------------------------------------------
        public async Task<SecurityInfo?> GetSecurityInfoAsync(string tickerOrIsin)
        {
            // Оставлен для обратной совместимости, может использоваться в старом коде.
            // Лучше использовать LookupSecurityAsync или GetSecurityInfoByIsinAsync.
            return await LookupSecurityAsync(tickerOrIsin);
        }

        public async Task<decimal?> GetChangePercentSafeAsync(string ticker)
        {
            try
            {
                var url = $"https://iss.moex.com/iss/engines/stock/markets/shares/boards/TQBR/securities/{ticker.ToUpper()}.json";
                using var stream = await _httpClient.GetStreamAsync(url);
                using var doc = await JsonDocument.ParseAsync(stream);

                var root = doc.RootElement;
                if (!root.TryGetProperty("marketdata", out var md) ||
                    !md.TryGetProperty("columns", out var columns) ||
                    !md.TryGetProperty("data", out var data) ||
                    data.GetArrayLength() == 0)
                    return null;

                var cols = columns.EnumerateArray().Select(c => c.GetString()).ToArray();
                int idx = -1;
                for (int i = 0; i < cols.Length; i++)
                {
                    if (string.Equals(cols[i], "LASTCHANGEPRCNT", StringComparison.OrdinalIgnoreCase))
                    {
                        idx = i;
                        break;
                    }
                }
                if (idx == -1) return null;

                var value = data[0][idx];
                if (value.ValueKind == JsonValueKind.Number)
                    return value.GetDecimal();
                if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), out var parsed))
                    return parsed;
                return null;
            }
            catch
            {
                return null;
            }
        }

        public async Task<IndexInfo?> GetIndexInfoAsync(string ticker)
        {
            try
            {
                var url = $"https://iss.moex.com/iss/engines/stock/markets/index/securities/{ticker.ToUpper()}.json";
                using var stream = await _httpClient.GetStreamAsync(url);
                using var doc = await JsonDocument.ParseAsync(stream);

                var root = doc.RootElement;
                decimal? value = null;
                decimal? changePct = null;

                if (root.TryGetProperty("marketdata", out var md) &&
                    md.TryGetProperty("columns", out var mdCols) &&
                    md.TryGetProperty("data", out var mdData) &&
                    mdData.GetArrayLength() > 0)
                {
                    var mdColumnsList = mdCols.EnumerateArray().Select(c => c.GetString()).ToArray();
                    int? valueIdx = null;
                    foreach (var name in new[] { "LAST", "LCLOSEPRICE", "CURRENTVALUE" })
                    {
                        valueIdx = FindColumnIndexCaseInsensitive(mdColumnsList, name);
                        if (valueIdx >= 0) break;
                    }
                    if (valueIdx >= 0 && mdData[0].GetArrayLength() > valueIdx)
                    {
                        value = ParseDecimal(mdData[0][valueIdx.Value]);
                    }

                    int? chgIdx = null;
                    foreach (var name in new[] { "LASTCHANGEPRCNT", "CHANGE", "LASTCHANGE" })
                    {
                        chgIdx = FindColumnIndexCaseInsensitive(mdColumnsList, name);
                        if (chgIdx >= 0) break;
                    }
                    if (chgIdx >= 0 && mdData[0].GetArrayLength() > chgIdx)
                    {
                        changePct = ParseDecimal(mdData[0][chgIdx.Value]);
                    }
                }

                if (!value.HasValue && root.TryGetProperty("securities", out var sec) &&
                    sec.TryGetProperty("columns", out var secCols) &&
                    sec.TryGetProperty("data", out var secData) &&
                    secData.GetArrayLength() > 0)
                {
                    var secColumnsList = secCols.EnumerateArray().Select(c => c.GetString()).ToArray();
                    int? valueIdx = null;
                    foreach (var name in new[] { "CURRENTVALUE", "LAST", "CLOSE" })
                    {
                        valueIdx = FindColumnIndexCaseInsensitive(secColumnsList, name);
                        if (valueIdx >= 0) break;
                    }
                    if (valueIdx >= 0 && secData[0].GetArrayLength() > valueIdx)
                    {
                        value = ParseDecimal(secData[0][valueIdx.Value]);
                    }
                }

                if (value.HasValue)
                    return new IndexInfo { Value = value, ChangePct = changePct };

                _logger.LogWarning("Could not find index value for {Ticker}", ticker);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting index info for {Ticker}", ticker);
                return null;
            }
        }

        public async Task<MarketStatus?> GetMarketStatusAsync()
        {
            try
            {
                var url = "https://iss.moex.com/iss/engines/stock/markets/shares/boards/TQBR/securities/SBER.json";
                using var stream = await _httpClient.GetStreamAsync(url);
                using var doc = await JsonDocument.ParseAsync(stream);

                var root = doc.RootElement;
                if (!root.TryGetProperty("marketdata", out var md) ||
                    !md.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
                    return new MarketStatus { Status = "Нет данных", CloseTime = "—" };

                var columns = md.GetProperty("columns");
                int statusIdx = FindColumnIndex(columns, "TRADINGSTATUS");
                var row = data[0];
                string? status = statusIdx >= 0 ? row[statusIdx].GetString() : null;

                string closeTime = "—";
                if (status == "T")
                {
                    var moscowTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Russian Standard Time");
                    if (moscowTime.Hour >= 10 && moscowTime.Hour < 18 || (moscowTime.Hour == 18 && moscowTime.Minute < 50))
                        closeTime = "18:50 МСК (осн. сессия)";
                    else if (moscowTime.Hour >= 19 && moscowTime.Hour < 23 || (moscowTime.Hour == 23 && moscowTime.Minute < 50))
                        closeTime = "23:50 МСК (вечерняя)";
                    else if (moscowTime.Hour >= 18 && moscowTime.Hour < 19)
                        closeTime = "18:50 МСК (осн.) / 23:50 МСК (веч.)";
                    else
                        closeTime = "23:50 МСК (вечерняя)";
                }

                return new MarketStatus
                {
                    Status = status switch { "T" => "Торги открыты", "C" => "Торги закрыты", _ => status ?? "—" },
                    CloseTime = closeTime
                };
            }
            catch { return new MarketStatus { Status = "—", CloseTime = "—" }; }
        }

        public async Task<MoverDto?> GetStockMoverAsync(string ticker)
        {
            try
            {
                var url = $"https://iss.moex.com/iss/engines/stock/markets/shares/boards/TQBR/securities/{ticker.ToUpper()}.json";
                using var stream = await _httpClient.GetStreamAsync(url);
                using var doc = await JsonDocument.ParseAsync(stream);

                var root = doc.RootElement;
                if (!root.TryGetProperty("marketdata", out var md) ||
                    !md.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
                    return null;

                var columns = md.GetProperty("columns");
                int lastIdx = FindColumnIndex(columns, "LAST");
                int chgIdx = FindColumnIndex(columns, "LASTCHANGEPRCNT");

                var row = data[0];
                decimal? price = lastIdx >= 0 && row[lastIdx].ValueKind == JsonValueKind.Number ? row[lastIdx].GetDecimal() : null;
                decimal? change = chgIdx >= 0 && row[chgIdx].ValueKind == JsonValueKind.Number ? row[chgIdx].GetDecimal() : null;

                return new MoverDto { Ticker = ticker, Price = price, ChangePct = change };
            }
            catch { return null; }
        }

        public async Task<List<(DateTime Date, decimal Amount, string Currency)>> GetDividendsAsync(string ticker)
        {
            var result = new List<(DateTime, decimal, string)>();
            try
            {
                var url = $"https://iss.moex.com/iss/securities/{ticker.ToUpper()}/dividends.json";
                using var stream = await _httpClient.GetStreamAsync(url);
                using var doc = await JsonDocument.ParseAsync(stream);

                var root = doc.RootElement;
                if (!root.TryGetProperty("dividends", out var divs) ||
                    !divs.TryGetProperty("data", out var data) ||
                    data.GetArrayLength() == 0)
                    return result;

                var columns = divs.GetProperty("columns");
                int dateIdx = FindColumnIndex(columns, "registryclosedate");
                int valueIdx = FindColumnIndex(columns, "value");
                int currencyIdx = FindColumnIndex(columns, "currencyid");

                if (dateIdx == -1 || valueIdx == -1) return result;

                foreach (var row in data.EnumerateArray())
                {
                    var dateStr = row[dateIdx].GetString();
                    if (string.IsNullOrWhiteSpace(dateStr)) continue;
                    if (!DateTime.TryParse(dateStr, out var date)) continue;

                    var amount = row[valueIdx].GetDecimal();
                    var currency = currencyIdx >= 0 ? row[currencyIdx].GetString() ?? "RUB" : "RUB";
                    result.Add((date, amount, currency));
                }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Error fetching dividends for {Ticker}", ticker); }
            return result;
        }

        // Вложенные классы
        public class SecurityInfo
        {
            public string Ticker { get; set; } = string.Empty;
            public string? Isin { get; set; }
            public string Name { get; set; } = string.Empty;
            public int? AssetTypeId { get; set; }
        }

        public class IndexInfo
        {
            public decimal? Value { get; set; }
            public decimal? ChangePct { get; set; }
        }

        public class MarketStatus
        {
            public string Status { get; set; } = string.Empty;
            public string CloseTime { get; set; } = string.Empty;
        }

        public class BondPaymentInfo
        {
            public DateTime? NextCouponDate { get; set; }
            public decimal? CouponValue { get; set; }
            public int? CouponFrequency { get; set; } // выплат в год
            public DateTime? MaturityDate { get; set; }
            public decimal? FaceValue { get; set; }
            public string Currency { get; set; } = "RUB";
        }

        public async Task<BondPaymentInfo?> GetBondPaymentInfoAsync(string ticker)
        {
            try
            {
                var url = $"https://iss.moex.com/iss/securities/{ticker.ToUpper()}/coupons.json";
                using var stream = await _httpClient.GetStreamAsync(url);
                using var doc = await JsonDocument.ParseAsync(stream);
                var root = doc.RootElement;

                if (!root.TryGetProperty("description", out var desc) ||
                    !desc.TryGetProperty("data", out var data))
                    return null;

                var info = new BondPaymentInfo();

                foreach (var row in data.EnumerateArray())
                {
                    var name = row[0].GetString();
                    var value = row[2].GetString();
                    switch (name)
                    {
                        case "COUPONDATE":
                            if (DateTime.TryParse(value, out var cd) && cd >= DateTime.Today)
                                info.NextCouponDate = cd;
                            break;
                        case "COUPONVALUE":
                            if (decimal.TryParse(value, out var cv))
                                info.CouponValue = cv;
                            break;
                        case "COUPONFREQUENCY":
                            if (int.TryParse(value, out var freq))
                                info.CouponFrequency = freq;
                            break;
                        case "MATDATE":
                            if (DateTime.TryParse(value, out var md) && md >= DateTime.Today)
                                info.MaturityDate = md;
                            break;
                        case "FACEVALUE":
                            if (decimal.TryParse(value, out var fv))
                                info.FaceValue = fv;
                            break;
                        case "FACEUNIT":
                            info.Currency = value ?? "RUB";
                            break;
                    }
                }

                if (info.NextCouponDate.HasValue && info.CouponValue.HasValue && info.CouponFrequency.HasValue)
                    return info;
                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}