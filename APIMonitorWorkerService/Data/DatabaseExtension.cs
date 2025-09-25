using Microsoft.EntityFrameworkCore;

namespace APIMonitorWorkerService.Data
{
    public static class DataLayerExtension
    {
        public static IServiceCollection RegisterDataLayer(this IServiceCollection services, IConfiguration configuration)
        {
            var rawConnection = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("DefaultConnection not found");

            // If Data Source is a relative path, resolve it under AppContext.BaseDirectory
            var connectionString = rawConnection;
            try
            {
                const string key = "Data Source=";
                var idx = rawConnection.IndexOf(key, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    var pathStart = idx + key.Length;
                    var path = rawConnection.Substring(pathStart).Trim();
                    // Strip trailing options if any
                    var semiIdx = path.IndexOf(';');
                    if (semiIdx >= 0)
                    {
                        path = path.Substring(0, semiIdx);
                    }

                    if (!Path.IsPathRooted(path))
                    {
                        var absolutePath = Path.Combine(AppContext.BaseDirectory, path);
                        connectionString = rawConnection.Replace(path, absolutePath);
                    }
                }
            }
            catch { }

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseSqlite(connectionString);
                options.EnableSensitiveDataLogging(false);
                options.EnableDetailedErrors(true);
            });

            services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

            return services;
        }
    }
}
