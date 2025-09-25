using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APIMonitorWorkerService.Services
{
    public static class ServiceLayerExtension
    {
        public static IServiceCollection RegisterServiceLayer(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped<IConfigurationService, ConfigurationService>();
            services.AddScoped<IDataSourceService, DataSourceService>();
            services.AddScoped<IApiPoller, ApiPoller>();
            services.AddHttpClient(); // Add HttpClient factory for proper lifecycle management
            return services;
        }
    }
}
