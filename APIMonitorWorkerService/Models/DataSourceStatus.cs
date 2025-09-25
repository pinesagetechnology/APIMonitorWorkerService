using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APIMonitorWorkerService.Models
{
    public class DataSourceStatus
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public bool IsActive { get; set; }
        public DateTime? LastActivity { get; set; }
        public long FilesProcessed { get; set; }
        public string? LastError { get; set; }
        public DateTime? LastErrorAt { get; set; }
    }
}
