using InvestmentTracker.Server.Data;
using InvestmentTracker.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Server.Services
{
    public class BondPaymentLoaderService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BondPaymentLoaderService> _logger;

        public BondPaymentLoaderService(IServiceScopeFactory scopeFactory, ILogger<BondPaymentLoaderService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task<(int coupons, int amortizations)> LoadAllAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var moex = scope.ServiceProvider.GetRequiredService<MoexService>();

            var bonds = await context.Securities
                .Where(s => s.AssetType.Name == "Облигация")
                .ToListAsync();

            int totalCoupons = 0;
            int totalAmorts = 0;

            foreach (var bond in bonds)
            {
                // 1. Удаляем ВСЕ будущие купоны и амортизации для этой облигации
                var oldEvents = await context.PaymentEvents
                    .Where(e => e.SecurityId == bond.Id && e.Date >= DateTime.Today &&
                               (e.Type == "Coupon" || e.Type == "Amortization"))
                    .ToListAsync();
                context.PaymentEvents.RemoveRange(oldEvents);

                // 2. Получаем актуальную информацию
                var info = await moex.GetBondPaymentInfoAsync(bond.Ticker);
                if (info == null) continue;

                // 3. Генерируем купоны
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
                            IsEstimated = !first   // первый купон – подтверждён, остальные – прогноз
                        });
                        totalCoupons++;
                        first = false;
                        date = date.AddMonths(monthsStep);
                    }
                }

                // 4. Амортизация (погашение) – используем FACEVALUE
                if (info.MaturityDate.HasValue && info.FaceValue.HasValue)
                {
                    context.PaymentEvents.Add(new PaymentEvent
                    {
                        Ticker = bond.Ticker,
                        SecurityId = bond.Id,
                        Date = info.MaturityDate.Value,
                        AmountPerUnit = info.FaceValue.Value,   // <-- правильный номинал
                        Currency = info.Currency,
                        Type = "Amortization",
                        IsEstimated = false
                    });
                    totalAmorts++;
                }
            }

            if (totalCoupons + totalAmorts > 0)
                await context.SaveChangesAsync();

            return (totalCoupons, totalAmorts);
        }
    }
}