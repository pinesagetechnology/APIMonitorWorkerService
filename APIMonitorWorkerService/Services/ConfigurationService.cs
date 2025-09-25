using APIMonitorWorkerService.Data;
using APIMonitorWorkerService.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APIMonitorWorkerService.Services
{
    public interface IConfigurationService
    {
        Task<string?> GetValueAsync(string key);
        Task<T> GetValueAsync<T>(string key);
        Task<Dictionary<string, string>> GetCategoryAsync(string category);
        Task DeleteAsync(string key);
        Task<IEnumerable<Configuration>> GetAllAsync();
    }

    public class ConfigurationService : IConfigurationService
    {
        private readonly IRepository<Configuration> _repository;
        private readonly ILogger<ConfigurationService> _logger;

        public ConfigurationService(IRepository<Configuration> repository, ILogger<ConfigurationService> logger)
        {
            _logger = logger;
            _repository = repository;
        }

        public async Task DeleteAsync(string key)
        {
            _logger.LogInformation("Deleting configuration key: {Key}", key);

            if (string.IsNullOrEmpty(key))
            {
                _logger.LogError("Configuration key cannot be null or empty");
                throw new ArgumentNullException("Configuration key cannot be null or empty", nameof(key));
            }

            var config = await _repository.GetByKeyAsync<string>(key);

            if (config != null)
            {
                await _repository.DeleteAsync(config);
                _logger.LogInformation("Successfully deleted configuration key: {Key}", key);
            }
            else
            {
                _logger.LogWarning("Attempted to delete non-existent configuration key: {Key}", key);
            }
        }

        public async Task<IEnumerable<Configuration>> GetAllAsync()
        {
            _logger.LogDebug("Getting all configuration values");

            var configs = await _repository.GetAllAsync();

            _logger.LogDebug($"Retrieved {configs.Count()} configuration values");

            return configs
                .OrderBy(c => c.Category)
                .ThenBy(c => c.Key)
                .ToList();
        }

        public async Task<Dictionary<string, string>> GetCategoryAsync(string category)
        {
            _logger.LogDebug("Getting all configuration values for category: {Category}", category);

            if (string.IsNullOrEmpty(category))
            {
                _logger.LogError("Configuration category cannot be null or empty");
                throw new ArgumentNullException("Configuration category cannot be null or empty", nameof(category));
            }

            var configs = await _repository.FindAsync(c => c.Category == category);

            var result = configs.ToDictionary(c => c.Key, c => c.Value);

            _logger.LogDebug("Found {Count} configuration values for category {Category}", result.Count, category);

            return result;
        }

        public async Task<string?> GetValueAsync(string key)
        {
            _logger.LogDebug("Getting configuration value for key: {Key}", key);
            var config = await _repository.GetByKeyAsync<string>(key);
            //var config = await context.Configuration.FindAsync(key);

            if (config != null)
            {
                _logger.LogDebug("Found configuration value for key {Key}: {Value}", key, config.Value);
                return config.Value;
            }
            else
            {
                _logger.LogDebug("Configuration key {Key} not found", key);
                return null;
            }
        }

        public async Task<T?> GetValueAsync<T>(string key)
        {
            _logger.LogDebug("Getting typed configuration value for key: {Key}, type: {Type}", key, typeof(T));

            var config = await _repository.GetByKeyAsync<string>(key);

            if (config == null || string.IsNullOrEmpty(config.Value))
            {
                _logger.LogDebug("Configuration value for key {Key} is null or empty, returning default", key);
                return default(T);
            }

            try
            {
                T result = (T)Convert.ChangeType(config.Value, typeof(T));

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error converting configuration value for key {Key} to type {Type}", key, typeof(T));
                return default(T);
            }
        }
    }
}
