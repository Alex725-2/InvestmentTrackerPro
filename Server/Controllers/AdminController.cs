using InvestmentTracker.Server.Data;
using InvestmentTracker.Server.Models;
using InvestmentTracker.Server.Services;
using InvestmentTracker.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AdminController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        [HttpGet("test-records")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<List<TestRecordDto>>> GetTestRecords()
        {
            var records = await _context.TestRecords
                .Select(r => new TestRecordDto { Id = r.Id, Name = r.Name })
                .ToListAsync();
            return Ok(records);
        }

        [HttpPost("fix-migrations")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> FixMigrations()
        {
            var context = _serviceProvider.GetRequiredService<ApplicationDbContext>();
            await context.Database.ExecuteSqlRawAsync(
                "CREATE TABLE IF NOT EXISTS __EFMigrationsHistory (MigrationId TEXT PRIMARY KEY, ProductVersion TEXT);"
            );
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20260723104024_InitialCreate', '8.0.0');"
            );
            return Ok("История миграций исправлена.");
        }

        [HttpPost("clear-payments")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ClearPayments()
        {
            var context = _serviceProvider.GetRequiredService<ApplicationDbContext>();
            var all = await context.PaymentEvents.ToListAsync();
            context.PaymentEvents.RemoveRange(all);
            await context.SaveChangesAsync();
            return Ok(new { Deleted = all.Count });
        }

        [HttpPost("debug-bond-by-isin")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DebugBondByIsin([FromBody] DebugBondRequest request)
        {
            var moex = _serviceProvider.GetRequiredService<MoexService>();
            var context = _serviceProvider.GetRequiredService<ApplicationDbContext>();

            // Ищем бумагу по ISIN
            var bond = await context.Securities.FirstOrDefaultAsync(s => s.Isin == request.Isin || s.Ticker == request.Isin);
            if (bond == null)
                return BadRequest($"Бумага с ISIN/Ticker '{request.Isin}' не найдена в справочнике.");

            var info = await moex.GetBondPaymentInfoAsync(bond.Ticker);
            if (info == null)
                return Ok(new { Message = "Не удалось получить параметры облигации с MOEX." });

            // Удаляем старые будущие купоны/амортизации для этой бумаги
            var oldEvents = await context.PaymentEvents
                .Where(e => e.SecurityId == bond.Id && e.Date >= DateTime.Today &&
                           (e.Type == "Coupon" || e.Type == "Amortization"))
                .ToListAsync();
            context.PaymentEvents.RemoveRange(oldEvents);

            int coupons = 0, amorts = 0;

            // Генерируем купоны
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
                    coupons++;
                    first = false;
                    date = date.AddMonths(monthsStep);
                }
            }

            // Генерируем амортизацию
            if (info.MaturityDate.HasValue && info.FaceValue.HasValue)
            {
                context.PaymentEvents.Add(new PaymentEvent
                {
                    Ticker = bond.Ticker,
                    SecurityId = bond.Id,
                    Date = info.MaturityDate.Value,
                    AmountPerUnit = info.FaceValue.Value,
                    Currency = info.Currency,
                    Type = "Amortization",
                    IsEstimated = false
                });
                amorts++;
            }

            await context.SaveChangesAsync();

            return Ok(new
            {
                Bond = bond.Ticker,
                CouponsAdded = coupons,
                AmortizationsAdded = amorts,
                NextCouponDate = info.NextCouponDate?.ToString("yyyy-MM-dd"),
                CouponValue = info.CouponValue,
                Frequency = info.CouponFrequency,
                MaturityDate = info.MaturityDate?.ToString("yyyy-MM-dd"),
                FaceValue = info.FaceValue
            });
        }
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IServiceProvider _serviceProvider;

        [HttpGet("settings")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetSetting([FromQuery] string code)
        {
            var setting = await _serviceProvider.GetRequiredService<SettingsService>().GetBoolAsync(code);
            return Ok(setting.ToString());
        }

        [HttpPut("settings")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateSetting([FromQuery] string code, [FromQuery] bool enabled)
        {
            await _serviceProvider.GetRequiredService<SettingsService>().SetBoolAsync(code, enabled);
            return NoContent();
        }

        public AdminController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IServiceProvider serviceProvider)
        {
            _context = context;
            _userManager = userManager;
            _serviceProvider = serviceProvider;
        }

        [HttpGet("users")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<List<UserDto>>> GetUsers()
        {
            var users = await _userManager.Users
                .OrderBy(u => u.Email)
                .Select(u => new UserDto
                {
                    Id = u.Id,
                    Email = u.Email ?? string.Empty,
                    FullName = u.FullName ?? string.Empty,
                    LastLoginDate = u.LastLoginDate
                })
                .ToListAsync();

            return Ok(users);
        }

        [HttpGet("sync-securities")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> SyncSecurities()
        {
            var syncService = _serviceProvider.GetService<SecuritiesSyncService>();
            if (syncService == null)
                return BadRequest("SecuritiesSyncService не зарегистрирован. Синхронизация недоступна.");

            await syncService.SyncSecuritiesAsync();
            return Ok("Sync completed");
        }

        [HttpPost("load-dividends")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> LoadDividends([FromBody] LoadDividendsRequest request)
        {
            if (request.Year <= 0 || request.Month < 1 || request.Month > 12)
                return BadRequest("Некорректные год или месяц.");

            var loader = _serviceProvider.GetRequiredService<DividendLoaderService>();
            int added = await loader.LoadDividendsForMonthAsync(request.Year, request.Month);

            return Ok(new { Added = added, Month = request.Month, Year = request.Year });
        }

        [HttpPost("load-bond-payments")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> LoadBondPayments()
        {
            var loader = _serviceProvider.GetRequiredService<BondPaymentLoaderService>();
            var result = await loader.LoadAllAsync();
            return Ok(new { CouponsAdded = result.coupons, AmortizationsAdded = result.amortizations });
        }

        [HttpPost("load-bonds")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> LoadBonds()
        {
            var loader = _serviceProvider.GetRequiredService<BondLoaderService>();
            int added = await loader.LoadBondsAsync(); // по умолчанию TQCB и TQOB
            return Ok(new { Added = added });
        }

        [HttpPost("debug-bond")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DebugBond([FromBody] DebugBondRequest request)
        {
            var moex = _serviceProvider.GetRequiredService<MoexService>();
            var context = _serviceProvider.GetRequiredService<ApplicationDbContext>();

            var bond = await context.Securities.FirstOrDefaultAsync(s => s.Ticker == request.Ticker);
            if (bond == null)
                return BadRequest($"Облигация {request.Ticker} не найдена в справочнике.");

            var coupons = await moex.GetCouponsAsync(request.Ticker);
            var amorts = await moex.GetAmortizationsAsync(request.Ticker);

            return Ok(new
            {
                Bond = bond.Ticker,
                CouponCount = coupons.Count,
                AmortCount = amorts.Count,
                SampleCoupon = coupons.Take(3).Select(c => new { c.Date, c.Amount, c.Currency }),
                SampleAmort = amorts.Take(3).Select(a => new { a.Date, a.Amount, a.Currency })
            });
        }

        // DTO для запроса
        public class DebugBondRequest
        {
            public string Ticker { get; set; } = string.Empty;
            public string Isin { get; set; } = string.Empty;
        }

        [HttpPost("test-email")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> TestEmail()
        {
            var emailService = _serviceProvider.GetRequiredService<IEmailService>();
            await emailService.SendAsync("razrabotka_2010@mail.ru", "Тестовое уведомление", "Это тестовое сообщение от Investment Tracker.");
            return Ok(new { message = "Тестовое письмо отправлено" });
        }
    }
}