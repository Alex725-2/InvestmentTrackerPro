namespace InvestmentTracker.Server.Services
{
    public class BackgroundJobStatusService
    {
        private readonly Dictionary<string, JobStatus> _jobs = new();

        public void SetRunning(string jobName)
        {
            lock (_jobs)
            {
                _jobs[jobName] = new JobStatus { IsRunning = true, LastStarted = DateTime.UtcNow };
            }
        }

        public void SetCompleted(string jobName)
        {
            lock (_jobs)
            {
                if (_jobs.TryGetValue(jobName, out var status))
                {
                    status.IsRunning = false;
                    status.LastCompleted = DateTime.UtcNow;
                }
                else
                {
                    _jobs[jobName] = new JobStatus { IsRunning = false, LastCompleted = DateTime.UtcNow };
                }
            }
        }

        public IReadOnlyDictionary<string, JobStatus> GetAllStatuses()
        {
            lock (_jobs)
            {
                return new Dictionary<string, JobStatus>(_jobs);
            }
        }

        public class JobStatus
        {
            public bool IsRunning { get; set; }
            public DateTime? LastStarted { get; set; }
            public DateTime? LastCompleted { get; set; }
        }
    }
}