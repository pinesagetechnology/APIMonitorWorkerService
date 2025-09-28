using APIMonitorWorkerService.Data;
using APIMonitorWorkerService.Models;
using APIMonitorWorkerService.Utility;
using System.Text;
using System.Text.Json;

namespace APIMonitorWorkerService.Services
{
    public interface IApiPoller
    {
        Task StartAsync(APIDataSourceConfig config, Func<int, string, Task> _onError);
        Task StopAsync();
        bool IsRunning { get; }
    }

    public class ApiPoller : IApiPoller, IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ApiPoller> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IRepository<APIDataSourceConfig> _repository;

        private Timer? _pollingTimer;
        private bool _isRunning = false;
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private DateTime _lastPollTime = DateTime.MinValue;
        private bool _disposed = false;
        private APIDataSourceConfig? _currentConfig;

        public bool IsRunning => _isRunning;

        public ApiPoller(
            IHttpClientFactory httpClientFactory,
            IServiceProvider serviceProvider,
            IConfigurationService configurationService,
            IRepository<APIDataSourceConfig> repository)
        {
            _httpClientFactory = httpClientFactory;
            _serviceProvider = serviceProvider;
            _repository = repository;
            _logger = serviceProvider.GetRequiredService<ILogger<ApiPoller>>();
        }

        public async Task StartAsync(APIDataSourceConfig config, Func<int, string, Task> _onError)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ApiPoller));
            
            if (_isRunning) 
            {
                _logger.LogDebug("ApiPoller for {Name} is already running, skipping start", config.Name);
                return;
            }

            await _semaphore.WaitAsync();
            try
            {
                if (_isRunning) 
                {
                    _logger.LogDebug("ApiPoller for {Name} is already running (double-check), skipping start", config.Name);
                    return;
                }

                if(config.IsEnabled)
                {
                    if (string.IsNullOrEmpty(config.ApiEndpoint))
                    {
                        var error = "API endpoint is not configured";
                        _logger.LogError("Cannot start ApiPoller for {Name}: {Error}", config.Name, error);
                        await _onError(config.Id, error);
                        throw new InvalidOperationException(error);
                    }
                    
                    _currentConfig = config; // Store config for use in timer callback
                    var interval = TimeSpan.FromMinutes(config.PollingIntervalMinutes);
                    
                    _logger.LogInformation("Starting ApiPoller for {Name} with interval {Interval} minutes, endpoint: {Endpoint}", 
                        config.Name, config.PollingIntervalMinutes, config.ApiEndpoint);
                    
                    _pollingTimer = new Timer(async _ => await PollApiAsync(), null, TimeSpan.Zero, interval);
                    _isRunning = true;
                    
                    _logger.LogInformation("✅ ApiPoller timer started successfully for {Name} - first poll will happen immediately", config.Name);
                }
                else
                {
                    _logger.LogInformation("ApiPoller for {Name} is disabled, skipping start", config.Name);
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task StopAsync()
        {
            if (_disposed || !_isRunning) 
            {
                _logger.LogDebug("ApiPoller for {Name} is not running or already disposed, skipping stop", _currentConfig?.Name ?? "Unknown");
                return;
            }

            _logger.LogInformation("Stopping ApiPoller for {Name}", _currentConfig?.Name ?? "Unknown");

            await _semaphore.WaitAsync();
            try
            {
                if (!_isRunning) 
                {
                    _logger.LogDebug("ApiPoller for {Name} is not running (double-check), skipping stop", _currentConfig?.Name ?? "Unknown");
                    return;
                }

                _pollingTimer?.Dispose();
                _pollingTimer = null;
                _isRunning = false;

                _logger.LogInformation("✅ ApiPoller stopped successfully for {Name}", _currentConfig?.Name ?? "Unknown");
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private HttpClient CreateConfiguredHttpClient(APIDataSourceConfig config)
        {
            var httpClient = _httpClientFactory.CreateClient();
            
            using var scope = _serviceProvider.CreateScope();
            var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();

            // Set timeout
            var timeoutSeconds = configService.GetValueAsync<int?>("Api.TimeoutSeconds").Result ?? 30;
            httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            // Add API key if configured
            if (!string.IsNullOrEmpty(config.ApiKey))
            {
                httpClient.DefaultRequestHeaders.Add("X-API-Key", config.ApiKey);
            }

            // Add custom headers from additional settings
            if (!string.IsNullOrEmpty(config.AdditionalSettings))
            {
                try
                {
                    var settings = JsonSerializer.Deserialize<ApiPollerSettings>(config.AdditionalSettings);
                    if (settings?.Headers != null)
                    {
                        foreach (var header in settings.Headers)
                        {
                            httpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse additional settings for {Name}", config.Name);
                }
            }

            // Set user agent
            httpClient.DefaultRequestHeaders.Add("User-Agent", "AzureGateway/1.0");
            
            return httpClient;
        }

        private async Task PollApiAsync()
        {
            var configName = _currentConfig?.Name ?? "Unknown";
            
            _logger.LogInformation("🔄 Timer callback triggered for {Name} at {Time}", configName, DateTime.UtcNow);
            
            // Check if the instance has been disposed
            if (_disposed || !_isRunning || _currentConfig == null) 
            {
                _logger.LogWarning("⚠️ PollApiAsync called but ApiPoller is disposed/stopped/null for {Name}", configName);
                // If disposed, stop the timer
                if (_disposed && _pollingTimer != null)
                {
                    _logger.LogInformation("Stopping timer for disposed ApiPoller {Name}", configName);
                    _pollingTimer.Dispose();
                    _pollingTimer = null;
                }
                return;
            }

            _logger.LogInformation("📡 Starting API call for {Name} to {Endpoint}", configName, _currentConfig.ApiEndpoint);

            HttpClient? httpClient = null;
            try
            {
                // Create a fresh HttpClient for each request to avoid disposal issues
                httpClient = CreateConfiguredHttpClient(_currentConfig);
                _logger.LogDebug("Created HttpClient for {Name}", configName);
                
                var startTime = DateTime.UtcNow;
                var response = await httpClient.GetAsync(_currentConfig.ApiEndpoint);
                var responseTime = DateTime.UtcNow - startTime;
                
                _logger.LogInformation("📊 API response received for {Name} - Status: {StatusCode}, ResponseTime: {ResponseTime}ms", 
                    configName, response.StatusCode, responseTime.TotalMilliseconds);
                
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var contentType = response.Content.Headers.ContentType?.MediaType?.ToLower();
                var contentLength = content.Length;

                _logger.LogInformation("📄 API response content for {Name} - ContentType: {ContentType}, Length: {Length} characters", 
                    configName, contentType, contentLength);

                await ProcessApiResponseAsync(configName, content, contentType);

                _lastPollTime = DateTime.UtcNow;

                // Update the last processed time
                _currentConfig.LastProcessedAt = _lastPollTime;
                await _repository.UpdateAsync(_currentConfig);
                
                _logger.LogInformation("✅ Successfully processed API response for {Name}, next poll in {Interval} minutes", 
                    configName, _currentConfig.PollingIntervalMinutes);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "❌ HTTP error fetching API data for {Name}: {Message}", configName, ex.Message);
            }
            catch (TaskCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "⏹️ API polling was canceled for {Name}", configName);
            }
            catch (ObjectDisposedException ex)
            {
                _logger.LogWarning(ex, "🗑️ ApiPoller was disposed during polling for {Name}", configName);
                // Stop the timer since we're disposed
                _pollingTimer?.Dispose();
                _pollingTimer = null;
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Unexpected error during API polling for {Name}: {Message}", configName, ex.Message);
            }
            finally
            {
                // Dispose the HttpClient to prevent connection leaks
                httpClient?.Dispose();
                _logger.LogDebug("Disposed HttpClient for {Name}", configName);
            }
        }

        private async Task ProcessApiResponseAsync(string configName, string content, string? contentType)
        {
            if (_disposed) return; // Don't process if disposed
            
            // Save the response as-is in a single file
            await ProcessRawResponseAsync(configName, content, contentType ?? "application/json");
        }


        private async Task ProcessRawResponseAsync(string configName, string content, string contentType)
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            var extension = GetFileExtensionFromContentType(contentType);
            var fileName = $"api_response_{configName}_{timestamp}.{extension}";
            var fileType = FileHelper.GetFileType(fileName);
            
            _logger.LogInformation("💾 Saving API response to file: {FileName} (ContentType: {ContentType}, Length: {Length} chars)", 
                fileName, contentType, content.Length);
            
            await ProcessDataAsync(content, fileName, fileType);
        }

        private async Task ProcessDataAsync(string content, string fileName, FileType fileType)
        {
            if (_disposed || _currentConfig == null) return; // Don't process if disposed

            var tempDir = GetTempDirectoryAsync(_currentConfig.TempFolderPath);

            var tempFilePath = Path.Combine(tempDir, fileName);

            await File.WriteAllTextAsync(tempFilePath, content, Encoding.UTF8);
        }

        private static string GetFileExtensionFromContentType(string contentType)
        {
            return contentType.ToLower() switch
            {
                "application/json" => "json",
                "text/plain" => "txt",
                "text/csv" => "csv",
                "application/xml" or "text/xml" => "xml",
                "image/jpeg" => "jpg",
                "image/png" => "png",
                _ => "data"
            };
        }

        private string GetTempDirectoryAsync(string tempFolderPath)
        {
            var tempPath = tempFolderPath ??
                           Path.Combine(Path.GetTempPath(), "azure-gateway", "api-data");

            if (!Directory.Exists(tempPath))
            {
                Directory.CreateDirectory(tempPath);
            }

            return tempPath;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    StopAsync().Wait();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error stopping ApiPoller during disposal");
                }
                finally
                {
                    _pollingTimer?.Dispose();
                    _semaphore?.Dispose();
                    _disposed = true;
                }
            }
        }
    }
}
