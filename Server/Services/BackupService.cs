using InvestmentTracker.Server.Data;
using InvestmentTracker.Server.Models;
using InvestmentTracker.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Server.Services
{
    public class BackupService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BackupService> _logger;

        // Путь к папке с бэкапами (на проде это /opt/investment-tracker-pro/app-data/backups)
        private readonly string _backupFolder;

        public BackupService(IServiceScopeFactory scopeFactory, ILogger<BackupService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            // Определяем папку: в Development используем локальный путь, иначе продакшн-путь
            _backupFolder = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development"
                ? Path.Combine(Directory.GetCurrentDirectory(), "app-data", "backups")
                : "/opt/investment-tracker-pro/app-data/backups";
        }

        public async Task<DbBackupDto?> CreateBackupAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Определяем путь к файлу БД
            var dbPath = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development"
         ? Path.Combine(Directory.GetCurrentDirectory(), "investmenttracker-dev.db")   // <-- укажи точное имя твоего файла БД
         : "/opt/investment-tracker-pro/app-data/investmenttracker-pro.db";

            if (!File.Exists(dbPath))
            {
                _logger.LogError("Database file not found at {Path}", dbPath);
                return null;
            }

            // Создаём папку, если её нет
            Directory.CreateDirectory(_backupFolder);

            var fileName = $"backup_{DateTime.UtcNow:yyyyMMddHHmmss}.db";
            var destPath = Path.Combine(_backupFolder, fileName);

            // Копируем файл
            File.Copy(dbPath, destPath, true);

            var fileInfo = new FileInfo(destPath);
            var backup = new DbBackup
            {
                FileName = fileName,
                CreatedAt = DateTime.UtcNow,
                SizeBytes = fileInfo.Length
            };

            context.DbBackups.Add(backup);
            await context.SaveChangesAsync();

            return new DbBackupDto
            {
                Id = backup.Id,
                FileName = backup.FileName,
                CreatedAt = backup.CreatedAt,
                SizeBytes = backup.SizeBytes
            };
        }

        public async Task<List<DbBackupDto>> GetBackupsAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await context.DbBackups
                .OrderByDescending(b => b.CreatedAt)
                .Select(b => new DbBackupDto
                {
                    Id = b.Id,
                    FileName = b.FileName,
                    CreatedAt = b.CreatedAt,
                    SizeBytes = b.SizeBytes
                })
                .ToListAsync();
        }

        public async Task<bool> DeleteBackupAsync(int id)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var backup = await context.DbBackups.FindAsync(id);
            if (backup == null) return false;

            var filePath = Path.Combine(_backupFolder, backup.FileName);
            if (File.Exists(filePath))
                File.Delete(filePath);

            context.DbBackups.Remove(backup);
            await context.SaveChangesAsync();
            return true;
        }

        public async Task<(byte[]? FileBytes, string? FileName)> GetBackupFileAsync(int id)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var backup = await context.DbBackups.FindAsync(id);
            if (backup == null) return (null, null);

            var filePath = Path.Combine(_backupFolder, backup.FileName);
            if (!File.Exists(filePath)) return (null, null);

            return (File.ReadAllBytes(filePath), backup.FileName);
        }

        // Путь к файлу БД (используется фоновым сервисом)
        public string GetDatabaseFilePath()
        {
            return Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development"
                ? Path.Combine(Directory.GetCurrentDirectory(), "app-data", "investmenttracker-pro.db")
                : "/opt/investment-tracker-pro/app-data/investmenttracker-pro.db";
        }
    }
}