using InvestmentTracker.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Server.Services
{
    public class SettingsService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public SettingsService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public async Task<bool> GetBoolAsync(string code)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var setting = await context.AppSettings.FirstOrDefaultAsync(s => s.Code == code);
            return setting?.Enabled ?? false;
        }

        public async Task SetBoolAsync(string code, bool enabled)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var setting = await context.AppSettings.FirstOrDefaultAsync(s => s.Code == code);
            if (setting != null)
            {
                setting.Enabled = enabled;
                await context.SaveChangesAsync();
            }
        }
    }
}