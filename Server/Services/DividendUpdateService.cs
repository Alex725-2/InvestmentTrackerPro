using InvestmentTracker.Server.Services;

public class DividendUpdateService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DividendUpdateService> _logger;
    private readonly BackgroundJobStatusService _statusService;

    public DividendUpdateService(
        IServiceScopeFactory scopeFactory,
        ILogger<DividendUpdateService> logger,
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
            // Отмечаем, что началась фоновая загрузка
            _statusService.SetRunning("dividend-update");

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var loader = scope.ServiceProvider.GetRequiredService<DividendLoaderService>();
                await loader.LoadDividendsForMonthAsync(DateTime.Today.Year, DateTime.Today.Month);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in dividend update");
            }
            finally
            {
                // Завершили – снимаем флаг
                _statusService.SetCompleted("dividend-update");
            }

            await Task.Delay(TimeSpan.FromHours(12), stoppingToken);
        }
    }
}