using InvestmentTracker.Server.Data;
using InvestmentTracker.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Server.Services
{
    public class BondPaymentLoaderService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BondPaymentLoaderService> _logger;
        private readonly BackgroundJobStatusService _statusService;

        public BondPaymentLoaderService(
            IServiceScopeFactory scopeFactory,
            ILogger<BondPaymentLoaderService> logger,
            BackgroundJobStatusService statusService)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _statusService = statusService;
        }

        public async Task<(int coupons, int amortizations)> LoadAllAsync()
        {
            _statusService.SetRunning("bond-payment-update");

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var moex = scope.ServiceProvider.GetRequiredService<MoexService>();

                var bonds = await context.Securities
                    .Where(s => s.AssetType.Name == "Облигация")
                    .ToListAsync();

                int totalCoupons = 0;
                int totalAmorts = 0; // сюда теперь входят и Amortization, и Redemption

                foreach (var bond in bonds)
                {
                    // 1. Удаляем ВСЕ будущие купоны и амортизации для этой облигации
                    var oldEvents = await context.PaymentEvents
                        .Where(e => e.SecurityId == bond.Id && e.Date >= DateTime.Today &&
                                   (e.Type == "Coupon" || e.Type == "Amortization" || e.Type == "Redemption"))
                        .ToListAsync();
                    context.PaymentEvents.RemoveRange(oldEvents);

                    // 2. Получаем актуальные параметры облигации
                    var info = await moex.GetBondPaymentInfoAsync(bond.Ticker);
                    if (info == null) continue;

                    // 3. Генерируем купоны (без изменений)
                    if (info.NextCouponDate.HasValue && info.CouponValue.HasValue && info.CouponFrequency.HasValue)
                    {
                        var date = info.NextCouponDate.Value;
                        var endDate = info.MaturityDate ?? date.AddYears(10);
                        int monthsStep = 12 / info.CouponFrequency.Value;
                        bool first = true;
                        while (date <= endDate)
                        {
                            context.PaymentEvents.Add(new PaymentEvent
                            {
                                Ticker = bond.Ticker,
                                SecurityId = bond.Id,
                                Date = date,
                                AmountPerUnit = info.CouponValue.Value,
                                Currency = info.Currency,
                                Type = "Coupon",
                                IsEstimated = !first
                            });
                            totalCoupons++;
                            first = false;
                            date = date.AddMonths(monthsStep);
                        }
                    }

                    // 4. Амортизации / Погашение
                    var amorts = await moex.GetAmortizationsAsync(bond.Ticker);
                    bool hasRealAmorts = amorts.Any();

                    if (hasRealAmorts)
                    {
                        // Есть частичные амортизации – обрабатываем их
                        foreach (var am in amorts)
                        {
                            // Определяем, является ли эта амортизация последней (погашение всей суммы)
                            bool isRedemption = info.MaturityDate.HasValue &&
                                                am.Date == info.MaturityDate.Value &&
                                                am.Amount == info.FaceValue;

                            string type = isRedemption ? "Redemption" : "Amortization";
                            context.PaymentEvents.Add(new PaymentEvent
                            {
                                Ticker = bond.Ticker,
                                SecurityId = bond.Id,
                                Date = am.Date,
                                AmountPerUnit = am.Amount,
                                Currency = am.Currency,
                                Type = type,
                                IsEstimated = false
                            });
                            totalAmorts++;
                        }
                    }
                    else if (info.MaturityDate.HasValue && info.FaceValue.HasValue)
                    {
                        // Нет частичных амортизаций – создаём одно событие погашения
                        context.PaymentEvents.Add(new PaymentEvent
                        {
                            Ticker = bond.Ticker,
                            SecurityId = bond.Id,
                            Date = info.MaturityDate.Value,
                            AmountPerUnit = info.FaceValue.Value,
                            Currency = info.Currency,
                            Type = "Redemption",   // <-- теперь это погашение
                            IsEstimated = false
                        });
                        totalAmorts++;
                    }
                }

                if (totalCoupons + totalAmorts > 0)
                    await context.SaveChangesAsync();

                return (totalCoupons, totalAmorts);
            }
            finally
            {
                _statusService.SetCompleted("bond-payment-update");
            }
        }
    }
}