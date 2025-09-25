using APIMonitorWorkerService.Data;
using APIMonitorWorkerService.Models;

namespace APIMonitorWorkerService.Services
{
    public interface IDataSourceService
    {
        Task<IEnumerable<APIDataSourceConfig>> GetAllDataSourcesAsync();
        Task<APIDataSourceConfig> GetDataSourcesByNameAsync(string name);
        Task UpdateDataSourcesIsrefreshingFlagAsync(APIDataSourceConfig fileDataSourceConfig);
    }

    public class DataSourceService : IDataSourceService
    {
        private readonly IRepository<APIDataSourceConfig> _repository;
        private readonly ILogger<DataSourceService> _logger;

        public DataSourceService(IRepository<APIDataSourceConfig> repository, ILogger<DataSourceService> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        public async Task<IEnumerable<APIDataSourceConfig>> GetAllDataSourcesAsync()
        {
            try
            {
                return await _repository.GetAllAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error occurred while fetching data sources {ex.Message}");

                return Enumerable.Empty<APIDataSourceConfig>();
            }
        }

        public async Task<APIDataSourceConfig> GetDataSourcesByNameAsync(string name)
        {
            try
            {
                _logger.LogInformation("Fetching all data sources");

                var datasourceConfig = await _repository.FindAsync(x => x.Name == name);

                return datasourceConfig.FirstOrDefault() ?? new APIDataSourceConfig();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error occurred while fetching data sources {ex.Message}");

                return new APIDataSourceConfig();
            }
        }

        public async Task UpdateDataSourcesIsrefreshingFlagAsync(APIDataSourceConfig fileDataSourceConfig)
        {
            _logger.LogInformation("Fetching all data sources");

            await _repository.UpdateAsync(fileDataSourceConfig);

            _logger.LogInformation($"Updated data source {fileDataSourceConfig.Name} with isRefreshing flag set to {fileDataSourceConfig.IsRefreshing}");
        }
    }
}
