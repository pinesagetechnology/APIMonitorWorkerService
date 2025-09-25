namespace APIMonitorWorkerService.Models
{
    public class ApiPollerSettings
    {
        public Dictionary<string, string>? Headers { get; set; }
        public string? AuthenticationType { get; set; }
        public string? AuthenticationValue { get; set; }
        public int? RetryCount { get; set; }
        public int? RetryDelayMs { get; set; }
        public bool ParseJsonArray { get; set; } = true;
        public string? ItemIdField { get; set; }
        public string? TimestampField { get; set; }
        public string? DataField { get; set; }
    }
}
