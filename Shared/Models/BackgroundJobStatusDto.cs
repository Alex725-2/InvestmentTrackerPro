namespace InvestmentTracker.Shared.Models
{
    public class BackgroundJobStatusDto
    {
        public bool IsRunning { get; set; }
        public DateTime? LastStarted { get; set; }
        public DateTime? LastCompleted { get; set; }
    }
}