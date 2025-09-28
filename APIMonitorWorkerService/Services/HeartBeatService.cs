using APIMonitorWorkerService.Data;
using APIMonitorWorkerService.Models;

namespace APIMonitorWorkerService.Services
{
    public interface IHeartbeatService
    {
        Task Upsert();
    }

    public class HeartBeatService : IHeartbeatService
    {
        private readonly IRepository<APIMonitorServiceHeartBeat> _repository;

        public HeartBeatService(IRepository<APIMonitorServiceHeartBeat> repository)
        {
            _repository = repository;
        }

        public async Task Upsert()
        {
            var heartBeat = await _repository.GetByIdAsync(1);

            if (heartBeat == null)
            {
                heartBeat = new APIMonitorServiceHeartBeat
                {
                    Id = 1,
                    LastRun = DateTime.UtcNow
                };
                await _repository.AddAsync(heartBeat);
            }
            else
            {
                heartBeat.LastRun = DateTime.UtcNow;
                await _repository.UpdateAsync(heartBeat);
            }
        }
    }
}
