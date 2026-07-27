namespace InvestmentTracker.Shared.Models
{
    public class DbBackupDto
    {
        public int Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public long SizeBytes { get; set; }
    }
}