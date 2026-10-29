namespace FleetManager;

public interface IAlarmRepository
{
    Task SaveAsync(PostgresAlarmRepository.Alarm alarm, CancellationToken cancellationToken);
    Task<IReadOnlyList<PostgresAlarmRepository.Alarm>> LoadAllAsync(CancellationToken cancellationToken);
    Task<PostgresAlarmRepository.Alarm?> GetAsync(string shuttleId, string code, CancellationToken cancellationToken);
    Task<int> CountUnacknowledgedAsync(string shuttleId, CancellationToken cancellationToken);
    Task AcknowledgeAsync(string shuttleId, CancellationToken cancellationToken);
}
