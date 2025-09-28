using APIMonitorWorkerService.Models;
using Microsoft.EntityFrameworkCore;

namespace APIMonitorWorkerService.Data
{
    public class AppDbContext: DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options): base(options)
        {
        }

        public DbSet<APIDataSourceConfig> APIDataSourceConfigs { get; set; }

        public DbSet<Configuration> Configurations { get; set; }

        public DbSet<APIMonitorServiceHeartBeat> APIMonitorServiceHeartBeats { get; set; }
    }
}
