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
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IServiceProvider _serviceProvider;

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

        [HttpPost("load-bonds")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> LoadBonds()
        {
            var loader = _serviceProvider.GetRequiredService<BondLoaderService>();
            int added = await loader.LoadBondsAsync(); // по умолчанию TQCB и TQOB
            return Ok(new { Added = added });
        }
    }
}