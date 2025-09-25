using System.ComponentModel.DataAnnotations;

namespace APIMonitorWorkerService.Models
{
    public class Configuration
    {
        [Key]
        [StringLength(100)]
        public string Key { get; set; } = string.Empty;

        [Required]
        public string Value { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [StringLength(50)]
        public string? Category { get; set; }

        public bool IsEncrypted { get; set; } = false;
    }
}
