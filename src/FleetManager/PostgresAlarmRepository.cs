using Npgsql;

namespace FleetManager;

public sealed class PostgresAlarmRepository
{
    private readonly string _connectionString;

    public PostgresAlarmRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public sealed record Alarm(
        string ShuttleId, string Severity, string Code, DateTime RaisedAt,
        DateTime? AcknowledgedAt = null);

    public async Task SaveAsync(Alarm alarm, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO alarms (shuttle_id, code, severity, raised_at, acknowledged_at)
            VALUES (@shuttleId, @code, @severity, @raisedAt, @acknowledgedAt)
            ON CONFLICT (shuttle_id, code)
            DO UPDATE SET acknowledged_at = @acknowledgedAt, raised_at = GREATEST(alarms.raised_at, EXCLUDED.raised_at);";
        command.Parameters.AddWithValue("@shuttleId", alarm.ShuttleId);
        command.Parameters.AddWithValue("@code", alarm.Code);
        command.Parameters.AddWithValue("@severity", alarm.Severity);
        command.Parameters.AddWithValue("@raisedAt", alarm.RaisedAt);
        command.Parameters.AddWithValue("@acknowledgedAt", (object?)alarm.AcknowledgedAt ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Alarm>> LoadAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT shuttle_id, code, severity, raised_at, acknowledged_at FROM alarms";
        var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var list = new List<Alarm>();
        while (reader.Read())
        {
            var a = new Alarm(
                reader.GetString(reader.GetOrdinal("shuttle_id")),
                reader.GetString(reader.GetOrdinal("severity")),
                reader.GetString(reader.GetOrdinal("code")),
                reader.GetDateTime(reader.GetOrdinal("raised_at")));
            if (!reader.IsDBNull(reader.GetOrdinal("acknowledged_at")))
                a = a with { AcknowledgedAt = reader.GetDateTime(reader.GetOrdinal("acknowledged_at")) };
            list.Add(a);
        }
        return list;
    }

    public async Task<Alarm?> GetAsync(string shuttleId, string code, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT shuttle_id, code, severity, raised_at, acknowledged_at FROM alarms WHERE shuttle_id = @shuttleId AND code = @code";
        cmd.Parameters.AddWithValue("@shuttleId", shuttleId);
        cmd.Parameters.AddWithValue("@code", code);
        var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!reader.Read()) return null;
        var a = new Alarm(
            reader.GetString(reader.GetOrdinal("shuttle_id")),
            reader.GetString(reader.GetOrdinal("severity")),
            reader.GetString(reader.GetOrdinal("code")),
            reader.GetDateTime(reader.GetOrdinal("raised_at")));
        if (!reader.IsDBNull(reader.GetOrdinal("acknowledged_at")))
            a = a with { AcknowledgedAt = reader.GetDateTime(reader.GetOrdinal("acknowledged_at")) };
        return a;
    }

    public async Task<int> CountUnacknowledgedAsync(string shuttleId, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM alarms WHERE shuttle_id = @shuttleId AND acknowledged_at IS NULL";
        cmd.Parameters.AddWithValue("@shuttleId", shuttleId);
        return (int)(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    public async Task AcknowledgeAsync(string shuttleId, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE alarms SET acknowledged_at = NOW() WHERE shuttle_id = @shuttleId AND acknowledged_at IS NULL";
        command.Parameters.AddWithValue("@shuttleId", shuttleId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}