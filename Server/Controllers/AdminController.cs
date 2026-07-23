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
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IServiceProvider _serviceProvider;

        public AdminController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IServiceProvider serviceProvider)
        {
            _context = context;
            _userManager = userManager;
            _serviceProvider = serviceProvider;
        }

        // ==================== ПОЛЬЗОВАТЕЛИ ====================
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

        // ==================== НАСТРОЙКИ ====================
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

        // ==================== ТЕСТОВАЯ ТАБЛИЦА (проверка миграций) ====================
        [HttpGet("test-records")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<List<TestRecordDto>>> GetTestRecords()
        {
            var records = await _context.TestRecords
                .Select(r => new TestRecordDto { Id = r.Id, Name = r.Name })
                .ToListAsync();
            return Ok(records);
        }

        // ==================== МИГРАЦИИ: фиксация истории ====================
        [HttpPost("fix-migrations")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> FixMigrations()
        {
            // Создаём таблицу истории, если её ещё нет
            await _context.Database.ExecuteSqlRawAsync(
                "CREATE TABLE IF NOT EXISTS __EFMigrationsHistory (MigrationId TEXT PRIMARY KEY, ProductVersion TEXT);"
            );
            // Вставляем записи о выполненных миграциях (имена бери из своих файлов миграций)
            await _context.Database.ExecuteSqlRawAsync(
                "INSERT OR IGNORE INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20260723104024_InitialCreate', '8.0.0');"
            );
            //await _context.Database.ExecuteSqlRawAsync(
            //    "INSERT OR IGNORE INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20260723114044_AddTestRecord', '8.0.0');"
            //);
            return Ok("История миграций исправлена.");
        }

        [HttpGet("migration-history")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<List<MigrationHistoryDto>>> GetMigrationHistory()
        {
            var connection = _context.Database.GetDbConnection();
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT MigrationId, ProductVersion FROM __EFMigrationsHistory";
            var reader = await command.ExecuteReaderAsync();
            var result = new List<MigrationHistoryDto>();
            while (await reader.ReadAsync())
            {
                result.Add(new MigrationHistoryDto
                {
                    MigrationId = reader.GetString(0),
                    ProductVersion = reader.GetString(1)
                });
            }
            await connection.CloseAsync();
            return Ok(result);
        }

        // ==================== ЗАГРУЗКА ДАННЫХ ====================
        [HttpPost("load-bonds")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> LoadBonds()
        {
            var loader = _serviceProvider.GetRequiredService<BondLoaderService>();
            int added = await loader.LoadBondsAsync();
            return Ok(new { Added = added });
        }

        [HttpPost("load-bond-payments")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> LoadBondPayments()
        {
            var loader = _serviceProvider.GetRequiredService<BondPaymentLoaderService>();
            var result = await loader.LoadAllAsync();
            return Ok(new { CouponsAdded = result.coupons, AmortizationsAdded = result.amortizations });
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

        [HttpPost("clear-payments")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ClearPayments()
        {
            var all = await _context.PaymentEvents.ToListAsync();
            _context.PaymentEvents.RemoveRange(all);
            await _context.SaveChangesAsync();
            return Ok(new { Deleted = all.Count });
        }

        // ==================== ДИАГНОСТИКА ====================
        [HttpPost("debug-bond")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DebugBond([FromBody] DebugBondRequest request)
        {
            var moex = _serviceProvider.GetRequiredService<MoexService>();
            var bond = await _context.Securities.FirstOrDefaultAsync(s => s.Ticker == request.Ticker);
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

        [HttpPost("debug-bond-by-isin")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DebugBondByIsin([FromBody] DebugBondRequest request)
        {
            var moex = _serviceProvider.GetRequiredService<MoexService>();
            var bond = await _context.Securities.FirstOrDefaultAsync(s => s.Isin == request.Isin || s.Ticker == request.Isin);
            if (bond == null)
                return BadRequest($"Бумага с ISIN/Ticker '{request.Isin}' не найдена в справочнике.");

            var info = await moex.GetBondPaymentInfoAsync(bond.Ticker);
            if (info == null)
                return Ok(new { Message = "Не удалось получить параметры облигации с MOEX." });

            // Удаляем старые будущие купоны/амортизации для этой бумаги
            var oldEvents = await _context.PaymentEvents
                .Where(e => e.SecurityId == bond.Id && e.Date >= DateTime.Today &&
                           (e.Type == "Coupon" || e.Type == "Amortization"))
                .ToListAsync();
            _context.PaymentEvents.RemoveRange(oldEvents);

            int coupons = 0, amorts = 0;

            if (info.NextCouponDate.HasValue && info.CouponValue.HasValue && info.CouponFrequency.HasValue)
            {
                var date = info.NextCouponDate.Value;
                var endDate = info.MaturityDate ?? date.AddYears(10);
                int monthsStep = 12 / info.CouponFrequency.Value;
                bool first = true;
                while (date <= endDate)
                {
                    _context.PaymentEvents.Add(new PaymentEvent
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

            if (info.MaturityDate.HasValue && info.FaceValue.HasValue)
            {
                _context.PaymentEvents.Add(new PaymentEvent
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

            await _context.SaveChangesAsync();

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

        // ==================== ТЕСТОВАЯ ПОЧТА ====================
        [HttpPost("test-email")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> TestEmail()
        {
            var emailService = _serviceProvider.GetRequiredService<IEmailService>();
            await emailService.SendAsync("razrabotka_2010@mail.ru", "Тестовое уведомление", "Это тестовое сообщение от Investment Tracker.");
            return Ok(new { message = "Тестовое письмо отправлено" });
        }

        // ==================== СИНХРОНИЗАЦИЯ ====================
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

        // ==================== DTO ====================
        public class DebugBondRequest
        {
            public string Ticker { get; set; } = string.Empty;
            public string Isin { get; set; } = string.Empty;
        }
    }
}