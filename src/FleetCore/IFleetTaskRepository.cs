namespace FleetCore;

public interface IFleetTaskRepository
{
    Task SaveAsync(FleetTask task, CancellationToken  cancellationToken);
    Task SaveAllAsync(IEnumerable<FleetTask> tasks, CancellationToken  cancellationToken);
    Task<IEnumerable<FleetTask>> LoadAllAsync(CancellationToken  cancellationToken);
    Task<FleetTask?> GetAsync(string taskId, CancellationToken  cancellationToken);
}