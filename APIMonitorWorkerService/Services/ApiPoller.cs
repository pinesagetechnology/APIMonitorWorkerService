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
        private readonly HttpClient _httpClient;
        private readonly IRepository<APIDataSourceConfig> _repository;

        private Timer? _pollingTimer;
        private bool _isRunning = false;
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private DateTime _lastPollTime = DateTime.MinValue;
        private bool _disposed = false;

        public bool IsRunning => _isRunning;

        public ApiPoller(
            HttpClient httpClient,
            IServiceProvider serviceProvider,
            IConfigurationService configurationService,
            IRepository<APIDataSourceConfig> repository)
        {
            _httpClient = httpClient;
            _serviceProvider = serviceProvider;
            _repository = repository;
            _logger = serviceProvider.GetRequiredService<ILogger<ApiPoller>>();
        }

        public async Task StartAsync(APIDataSourceConfig config, Func<int, string, Task> _onError)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ApiPoller));
            
            ConfigureHttpClient(config);

            if (_isRunning) return;

            await _semaphore.WaitAsync();
            try
            {
                if (_isRunning) return;

                if (string.IsNullOrEmpty(config.ApiEndpoint))
                {
                    var error = "API endpoint is not configured";
                    await _onError(config.Id, error);
                    throw new InvalidOperationException(error);
                }

                var interval = TimeSpan.FromMinutes(config.PollingIntervalMinutes);
                _pollingTimer = new Timer(async _ => await PollApiAsync(config), null, TimeSpan.Zero, interval);
                _isRunning = true;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task StopAsync()
        {
            if (_disposed || !_isRunning) return;

            await _semaphore.WaitAsync();
            try
            {
                if (!_isRunning) return;

                _pollingTimer?.Dispose();
                _pollingTimer = null;
                _isRunning = false;

            }
            finally
            {
                _semaphore.Release();
            }
        }

        private void ConfigureHttpClient(APIDataSourceConfig config)
        {
            using var scope = _serviceProvider.CreateScope();
            var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();

            // Set timeout
            var timeoutSeconds = configService.GetValueAsync<int?>("Api.TimeoutSeconds").Result ?? 30;
            _httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            // Add API key if configured
            if (!string.IsNullOrEmpty(config.ApiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("X-API-Key", config.ApiKey);
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
                            _httpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse additional settings for {Name}", config.Name);
                }
            }

            // Set user agent
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "AzureGateway/1.0");
        }

        private async Task PollApiAsync(APIDataSourceConfig config)
        {
            if (!_isRunning) return;

            try
            {
                var response = await _httpClient.GetAsync(config.ApiEndpoint);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var contentType = response.Content.Headers.ContentType?.MediaType?.ToLower();

                await ProcessApiResponseAsync(config.Name, content, contentType);

                _lastPollTime = DateTime.UtcNow;

                if (config != null)
                {
                    config.LastProcessedAt = _lastPollTime;

                    await _repository.UpdateAsync(config);
                }
            }
            catch (HttpRequestException ex)
            {
                throw new HttpRequestException($"Error fetching API data: {ex.Message}", ex);
            }
            catch (TaskCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
            {
                throw new TaskCanceledException("API polling was canceled", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Unexpected error during API polling: {ex.Message}", ex);
            }
        }

        private async Task ProcessApiResponseAsync(string configName, string content, string? contentType)
        {
            // Determine how to process based on content type and response structure
            if (IsJsonContent(contentType))
            {
                await ProcessJsonResponseAsync(configName, content);
            }
            else
            {
                // Handle other content types or treat as raw data
                await ProcessRawResponseAsync(configName, content, contentType ?? "text/plain");
            }
        }

        private async Task ProcessJsonResponseAsync(string configName, string jsonContent)
        {
            using var document = JsonDocument.Parse(jsonContent);
            var root = document.RootElement;

            // Check if response contains an array of items or single item
            if (root.ValueKind == JsonValueKind.Array)
            {
                await ProcessJsonArrayAsync(configName, root);
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                // Check if object contains a data array
                if (root.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Array)
                {
                    await ProcessJsonArrayAsync(configName, dataElement);
                }
                else if (root.TryGetProperty("items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
                {
                    await ProcessJsonArrayAsync(configName, itemsElement);
                }
                else
                {
                    // Process single object
                    await ProcessSingleJsonItemAsync(configName, root);
                }
            }
        }

        private async Task ProcessJsonArrayAsync(string configName, JsonElement arrayElement)
        {
            var itemCount = 0;
            foreach (var item in arrayElement.EnumerateArray())
            {
                await ProcessSingleJsonItemAsync(configName, item);
                itemCount++;
            }
        }

        private async Task ProcessSingleJsonItemAsync(string configName, JsonElement item)
        {
            // Generate a unique filename for this JSON item
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            var itemId = ExtractItemId(item) ?? Guid.NewGuid().ToString("N")[..8];
            var fileName = $"api_data_{configName}_{timestamp}_{itemId}.json";

            // Serialize the item back to JSON
            var jsonString = JsonSerializer.Serialize(item, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await ProcessDataAsync(jsonString, fileName, FileType.Json);
        }

        private async Task ProcessRawResponseAsync(string configName, string content, string contentType)
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            var extension = GetFileExtensionFromContentType(contentType);
            var fileName = $"api_response_{configName}_{timestamp}.{extension}";
            var fileType = FileHelper.GetFileType(fileName);
            await ProcessDataAsync(content, fileName, fileType);
        }

        private async Task ProcessDataAsync(string content, string fileName, FileType fileType)
        {
            var tempDir = await GetTempDirectoryAsync();
            var tempFilePath = Path.Combine(tempDir, fileName);

            await File.WriteAllTextAsync(tempFilePath, content, Encoding.UTF8);

            var hash = await FileHelper.CalculateFileHashAsync(tempFilePath);
            var fileSize = new FileInfo(tempFilePath).Length;
        }

        private string? ExtractItemId(JsonElement item)
        {
            // Try common ID field names
            var idFields = new[] { "id", "Id", "ID", "identifier", "key", "uuid" };

            foreach (var field in idFields)
            {
                if (item.TryGetProperty(field, out var idElement))
                {
                    return idElement.ValueKind switch
                    {
                        JsonValueKind.String => idElement.GetString(),
                        JsonValueKind.Number => idElement.GetInt64().ToString(),
                        _ => null
                    };
                }
            }

            return null;
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

        private static bool IsJsonContent(string? contentType)
        {
            return contentType?.Contains("application/json") == true ||
                   contentType?.Contains("text/json") == true;
        }

        private async Task<string> GetTempDirectoryAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();

            var tempPath = await configService.GetValueAsync("Api.TempDirectory") ??
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
