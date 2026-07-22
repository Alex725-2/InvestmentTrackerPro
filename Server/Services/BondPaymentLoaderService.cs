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
                var info = await moex.GetBondPaymentInfoAsync(bond.Ticker);
                if (info == null) continue;

                // Рассчитываем будущие купоны
                if (info.NextCouponDate.HasValue && info.CouponValue.HasValue && info.CouponFrequency.HasValue)
                {
                    var date = info.NextCouponDate.Value;
                    var endDate = info.MaturityDate ?? date.AddYears(10); // если нет даты погашения, ограничим 10 годами
                    int monthsStep = 12 / info.CouponFrequency.Value;

                    while (date <= endDate)
                    {
                        if (!await context.PaymentEvents.AnyAsync(e =>
                            e.SecurityId == bond.Id && e.Date == date && e.Type == "Coupon"))
                        {
                            context.PaymentEvents.Add(new PaymentEvent
                            {
                                Ticker = bond.Ticker,
                                SecurityId = bond.Id,
                                Date = date,
                                AmountPerUnit = info.CouponValue.Value,
                                Currency = info.Currency,
                                Type = "Coupon"
                            });
                            totalCoupons++;
                        }
                        date = date.AddMonths(monthsStep);
                    }
                }

                // Амортизация (погашение)
                if (info.MaturityDate.HasValue && info.FaceValue.HasValue)
                {
                    if (!await context.PaymentEvents.AnyAsync(e =>
                        e.SecurityId == bond.Id && e.Date == info.MaturityDate.Value && e.Type == "Amortization"))
                    {
                        context.PaymentEvents.Add(new PaymentEvent
                        {
                            Ticker = bond.Ticker,
                            SecurityId = bond.Id,
                            Date = info.MaturityDate.Value,
                            AmountPerUnit = info.FaceValue.Value,
                            Currency = info.Currency,
                            Type = "Amortization"
                        });
                        totalAmorts++;
                    }
                }
            }

            if (totalCoupons + totalAmorts > 0)
                await context.SaveChangesAsync();

            return (totalCoupons, totalAmorts);
        }
    }
}