using APIMonitorWorkerService.Models;
using APIMonitorWorkerService.Services;
using System.Collections.Concurrent;

namespace APIMonitorWorkerService
{
    public class Worker : BackgroundService, IDisposable
    {
        private readonly ILogger<Worker> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly ConcurrentDictionary<string, (IServiceScope Scope, IApiPoller Poller)> _activePollers = new();
        private bool _disposed = false;

        public Worker(ILogger<Worker> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Worker started at: {time}", DateTimeOffset.Now);

            int intervalSeconds = 5;
            using (var scope = _serviceProvider.CreateScope())
            {
                var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
                intervalSeconds = await configService.GetValueAsync<int>(Constants.ProcessingIntervalSeconds);
            }

            _logger.LogInformation($"Fetch processing interval seconds: {intervalSeconds}");

            IEnumerable<APIDataSourceConfig> datasourceList;
            using (var scope = _serviceProvider.CreateScope())
            {
                var dataSourceService = scope.ServiceProvider.GetRequiredService<IDataSourceService>();
                datasourceList = await dataSourceService.GetAllDataSourcesAsync();
            }

            foreach (var datasource in datasourceList)
            {
                IServiceScope? scope = null;
                try
                {
                    _logger.LogInformation($"Starting to monitor API: {datasource.Name}");
                    scope = _serviceProvider.CreateScope();
                    var poller = scope.ServiceProvider.GetRequiredService<IApiPoller>();
                    
                    await poller.StartAsync(datasource, async (id, error) =>
                    {
                        _logger.LogError("Watcher error for datasource {Id}: {Error}", id, error);
                        await Task.CompletedTask;
                    });

                    _activePollers.TryAdd(datasource.Name, (scope, poller));
                    scope = null; // Don't dispose if successfully added
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to start poller for {datasource.Name}");
                    scope?.Dispose(); // Clean up scope if poller creation failed
                }
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                }

                await Task.Delay(1000, stoppingToken);
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping worker and cleaning up pollers...");
            
            // Stop all active pollers
            var stopTasks = _activePollers.Values.Select(async kvp =>
            {
                try
                {
                    await kvp.Poller.StopAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error stopping poller");
                }
            });

            await Task.WhenAll(stopTasks);

            // Dispose all scopes
            foreach (var kvp in _activePollers.Values)
            {
                try
                {
                    kvp.Scope?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing scope");
                }
            }
            _activePollers.Clear();

            await base.StopAsync(cancellationToken);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    StopAsync(CancellationToken.None).Wait();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during disposal");
                }
                finally
                {
                    _disposed = true;
                }
            }
        }
    }
}
