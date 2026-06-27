using InvestmentTracker.Server.Models;
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

        public AdminController(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        [HttpGet("users")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<List<UserDto>>> GetUsers()
        {
            var users = await _userManager.Users.Select(u => new UserDto
            {
                Id = u.Id,
                Email = u.Email ?? string.Empty,
                FullName = u.FullName ?? string.Empty,
                LastLoginDate = u.LastLoginDate
            }).ToListAsync();

            return Ok(users);
        }
    }
}