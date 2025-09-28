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
            _logger.LogInformation("🚀 APIMonitorWorkerService started at: {time}", DateTimeOffset.Now);

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

            _logger.LogInformation("🚀 Found {Count} datasources to process", datasourceList.Count());

            foreach (var datasource in datasourceList)
            {
                IServiceScope? scope = null;
                try
                {
                    _logger.LogInformation("🔧 Setting up ApiPoller for datasource: {Name} (Enabled: {Enabled}, Endpoint: {Endpoint})", 
                        datasource.Name, datasource.IsEnabled, datasource.ApiEndpoint);
                    
                    scope = _serviceProvider.CreateScope();
                    var poller = scope.ServiceProvider.GetRequiredService<IApiPoller>();
                    
                    if(datasource.IsEnabled == false)
                    {
                        _logger.LogInformation("⏸️ Datasource {Name} is disabled. Skipping...", datasource.Name);
                        continue;
                    }

                    _logger.LogInformation("▶️ Starting ApiPoller for {Name} with polling interval: {Interval} minutes", 
                        datasource.Name, datasource.PollingIntervalMinutes);

                    await poller.StartAsync(datasource, async (id, error) =>
                    {
                        _logger.LogError("💥 Watcher error for datasource {Id}: {Error}", id, error);
                        await Task.CompletedTask;
                    });

                    _activePollers.TryAdd(datasource.Name, (scope, poller));
                    scope = null; // Don't dispose if successfully added
                    
                    _logger.LogInformation("✅ Successfully added ApiPoller for {Name} to active pollers. Total active: {Count}", 
                        datasource.Name, _activePollers.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Failed to start poller for {Name}: {Message}", datasource.Name, ex.Message);
                    scope?.Dispose(); // Clean up scope if poller creation failed
                    continue;
                }
            }
            
            _logger.LogInformation("🎯 Initialization complete. Total active ApiPollers: {Count}", _activePollers.Count);

            while (!stoppingToken.IsCancellationRequested)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("🔄 Worker running at: {time} with {Count} active ApiPollers", 
                        DateTimeOffset.Now, _activePollers.Count);
                }

                await RefreshWatchersAsync();

                await HeartBeatUpdate();

                await Task.Delay(intervalSeconds * 1000, stoppingToken);
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

        private async Task RefreshWatchersAsync()
        {
            IEnumerable<APIDataSourceConfig> datasourceList;

            using (var scope = _serviceProvider.CreateScope())
            {
                var dataSourceService = scope.ServiceProvider.GetRequiredService<IDataSourceService>();
                datasourceList = await dataSourceService.GetAllDataSourcesAsync();
            }

            foreach (var datasource in datasourceList)
            {
                var itemToRefresh = _activePollers.FirstOrDefault(x => x.Key == datasource.Name);

                // Handle existing poller
                if (!string.IsNullOrEmpty(itemToRefresh.Key))
                {
                    if (datasource.IsRefreshing || datasource.IsEnabled == false)
                    {
                        _logger.LogInformation($"Stopping existing poller for datasource: {datasource.Name}");
                        try
                        {
                            await itemToRefresh.Value.Poller.StopAsync();
                        }
                        finally
                        {
                            itemToRefresh.Value.Scope.Dispose();
                        }
                        _activePollers.TryRemove(itemToRefresh.Key, out var _);
                    }
                    
                    // If datasource is disabled, don't restart it
                    if (datasource.IsEnabled == false)
                    {
                        continue;
                    }

                    if (datasource.IsRefreshing && datasource.IsEnabled)
                    {
                        _logger.LogInformation($"Refreshing API Poller: {datasource.ApiEndpoint} for datasource: {datasource.Name}");

                        IServiceScope? scope = null;
                        try
                        {
                            _logger.LogInformation($"Restarting to monitor API: {datasource.Name}");

                            scope = _serviceProvider.CreateScope();
                            var poller = scope.ServiceProvider.GetRequiredService<IApiPoller>();

                            await poller.StartAsync(datasource, async (id, error) =>
                            {
                                _logger.LogError("Watcher error for datasource {Id}: {Error}", id, error);
                                await Task.CompletedTask;
                            });

                            // Reset the refreshing flag for all data sources
                            datasource.IsRefreshing = false;
                            using (var updateScope = _serviceProvider.CreateScope())
                            {
                                var dataSourceService = updateScope.ServiceProvider.GetRequiredService<IDataSourceService>();
                                await dataSourceService.UpdateDataSourcesIsrefreshingFlagAsync(datasource);
                            }

                            _activePollers.TryAdd(datasource.Name, (scope, poller));
                            scope = null; // Don't dispose if successfully added
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Failed to start poller for {datasource.Name}");
                            scope?.Dispose(); // Clean up scope if poller creation failed
                            continue;
                        }
                    }
                }
                else
                {
                    // Handle new datasource (not currently running)
                    if (datasource.IsEnabled == true)
                    {
                        _logger.LogInformation($"Starting new poller for datasource: {datasource.Name}");
                        
                        IServiceScope? scope = null;
                        try
                        {
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
                            _logger.LogError(ex, $"Failed to start new poller for {datasource.Name}");
                            scope?.Dispose(); // Clean up scope if poller creation failed
                        }
                    }
                }
            }
        }

        private async Task HeartBeatUpdate()
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var heartbeatService = scope.ServiceProvider.GetRequiredService<IHeartbeatService>();
                await heartbeatService.Upsert();
            }
        }

        public override void Dispose()
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
