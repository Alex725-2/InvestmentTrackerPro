using InvestmentTracker.Server.Models;
using InvestmentTracker.Server.Services;
using InvestmentTracker.Shared.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace InvestmentTracker.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IConfiguration _configuration;

        public AuthController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IConfiguration configuration)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _configuration = configuration;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            var user = new ApplicationUser
            {
                UserName = request.Email,
                Email = request.Email,
                FullName = request.FullName
            };

            var result = await _userManager.CreateAsync(user, request.Password);

            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                return BadRequest(new AuthResponse { IsSuccess = false, Message = errors });
            }

            if (result.Succeeded)
            {
                if (result.Succeeded)
                {
                    // Проверяем настройку уведомлений
                    var settings = HttpContext.RequestServices.GetRequiredService<SettingsService>();
                    if (await settings.GetBoolAsync("SendNotificationAboutNewUser"))
                    {
                        var emailService = HttpContext.RequestServices.GetRequiredService<IEmailService>();
                        await emailService.SendAsync("razrabotka_2010@mail.ru",
                            "Новый пользователь",
                            $"Зарегистрировался: {user.Email} ({user.FullName})");
                    }
                    // ... остальной код (токен и т.д.)
                }
            }

            var token = await GenerateJwtToken(user);   // <-- было без await
            return Ok(new AuthResponse { IsSuccess = true, Token = token, Message = "Registration successful" });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var user = await _userManager.FindByEmailAsync(request.Email);
            if (user == null)
                return Unauthorized(new AuthResponse { IsSuccess = false, Message = "Invalid email or password" });

            var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, false);
            if (!result.Succeeded)
                return Unauthorized(new AuthResponse { IsSuccess = false, Message = "Invalid email or password" });

            var token = await GenerateJwtToken(user);   // <-- было без await
            user.LastLoginDate = DateTime.UtcNow;
            await _userManager.UpdateAsync(user);
            return Ok(new AuthResponse { IsSuccess = true, Token = token, Message = "Login successful" });
        }

        private async Task<string> GenerateJwtToken(ApplicationUser user)
        {
            var jwtSettings = _configuration.GetSection("Jwt");
            var key = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(jwtSettings["Key"]!));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(JwtRegisteredClaimNames.Email, user.Email ?? ""),
                new Claim("fullName", user.FullName ?? ""),
                new Claim(ClaimTypes.NameIdentifier, user.Id)
            };

            // Добавляем роли пользователя
            var roles = await _userManager.GetRolesAsync(user);
            claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

            var token = new JwtSecurityToken(
                issuer: jwtSettings["Issuer"],
                audience: jwtSettings["Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddDays(7),
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}