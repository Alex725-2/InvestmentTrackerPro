namespace InvestmentTracker.Server.Services
{
    public class BackupBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BackupBackgroundService> _logger;
        private readonly BackgroundJobStatusService _statusService;

        public BackupBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<BackupBackgroundService> logger,
            BackgroundJobStatusService statusService)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _statusService = statusService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Вычисляем время до следующего 4:00 МСК
                var mskZone = TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");
                var nowMsk = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, mskZone);
                var nextRun = nowMsk.Date.AddHours(4);
                if (nowMsk >= nextRun)
                    nextRun = nextRun.AddDays(1);

                var delay = nextRun - nowMsk;
                _logger.LogInformation("Next backup scheduled at {NextRun} MSK (in {Delay})", nextRun, delay);

                await Task.Delay(delay, stoppingToken);

                // Запускаем бэкап
                _statusService.SetRunning("backup");
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var backupService = scope.ServiceProvider.GetRequiredService<BackupService>();
                    var result = await backupService.CreateBackupAsync();
                    if (result != null)
                        _logger.LogInformation("Backup created: {FileName}, size {Size} bytes", result.FileName, result.SizeBytes);
                    else
                        _logger.LogWarning("Backup creation failed");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during scheduled backup");
                }
                finally
                {
                    _statusService.SetCompleted("backup");
                }
            }
        }
    }
}