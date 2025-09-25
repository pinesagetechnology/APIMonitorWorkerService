namespace APIMonitorWorkerService.Models
{
    public class ApiData
    {
        public string Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string Type { get; set; }
        public object Data { get; set; }
    }

    public class ApiPollingStatus
    {
        public bool IsRunning { get; set; }
        public DateTime StartedAt { get; set; }
        public int ActiveApiPollers { get; set; }
        public long TotalItemsProcessed { get; set; }
        public DateTime? LastActivity { get; set; }
        //public List<DataSourceStatus> DataSources { get; set; } = new();
    }
}
