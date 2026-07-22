using InvestmentTracker.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Server.Services
{
    public class BondMaintenanceService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BondMaintenanceService> _logger;

        public BondMaintenanceService(IServiceScopeFactory scopeFactory, ILogger<BondMaintenanceService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Bond maintenance service starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var loader = scope.ServiceProvider.GetRequiredService<BondPaymentLoaderService>();
                    var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                    var (coupons, amortizations) = await loader.LoadAllAsync();

                    var bondsCount = await context.Securities.CountAsync(s => s.AssetType.Name == "Облигация");
                    var subject = "Обновление облигаций";
                    var body = $"Добавлено купонов: {coupons}, амортизаций: {amortizations}. Всего облигаций: {bondsCount}.";

                    await emailService.SendAsync("razrabotka_2010@mail.ru", subject, body);
                    _logger.LogInformation("Bond maintenance completed. {Body}", body);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in bond maintenance.");
                    // Попытка отправить уведомление об ошибке
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                        await emailService.SendAsync("razrabotka_2010@mail.ru", "Ошибка обновления облигаций", ex.Message);
                    }
                    catch { }
                }

                // Следующее обновление через 24 часа
                await Task.Delay(TimeSpan.FromHours(12), stoppingToken);
            }
        }
    }
}