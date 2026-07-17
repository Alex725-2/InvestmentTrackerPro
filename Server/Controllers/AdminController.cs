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
    }
}