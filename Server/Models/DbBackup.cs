namespace InvestmentTracker.Server.Models
{
    public class DbBackup
    {
        public int Id { get; set; }
        public string FileName { get; set; } = string.Empty;   // только имя файла, без пути
        public DateTime CreatedAt { get; set; }
        public long SizeBytes { get; set; }
    }
}