using System.ComponentModel.DataAnnotations;

namespace APIMonitorWorkerService.Models
{
    public class APIDataSourceConfig
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        public bool IsEnabled { get; set; } = true;

        public bool IsRefreshing { get; set; } = true;

        [StringLength(500)]
        public string? TempFolderPath { get; set; }

        [StringLength(500)]
        public string? ApiEndpoint { get; set; }

        [StringLength(200)]
        public string? ApiKey { get; set; }

        public int PollingIntervalMinutes { get; set; } = 1;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? LastProcessedAt { get; set; }

        [StringLength(1000)]
        public string? AdditionalSettings { get; set; } // JSON for extra configs
    }
}
