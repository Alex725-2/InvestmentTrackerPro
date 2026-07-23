using InvestmentTracker.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Server.Services
{
    public class BondMaintenanceService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BondMaintenanceService> _logger;
        private readonly BackgroundJobStatusService _statusService;

        public BondMaintenanceService(
            IServiceScopeFactory scopeFactory,
            ILogger<BondMaintenanceService> logger,
            BackgroundJobStatusService statusService)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _statusService = statusService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Bond maintenance service starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                // Сообщаем светофору, что начался цикл обслуживания облигаций
                _statusService.SetRunning("bond-maintenance");

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
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                        await emailService.SendAsync("razrabotka_2010@mail.ru", "Ошибка обновления облигаций", ex.Message);
                    }
                    catch { }
                }
                finally
                {
                    // Завершили (даже если была ошибка)
                    _statusService.SetCompleted("bond-maintenance");
                }

                // Следующее обновление через 12 часов
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
        }
    }
}