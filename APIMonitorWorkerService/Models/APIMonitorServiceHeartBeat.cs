using System.ComponentModel.DataAnnotations;

namespace APIMonitorWorkerService.Models
{
    public class APIMonitorServiceHeartBeat
    {
        [Key]
        public int Id { get; set; }

        public DateTime? LastRun { get; set; }
    }
}
