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

        public AdminController(UserManager<ApplicationUser> userManager, IServiceProvider serviceProvider)
        {
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