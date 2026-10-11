namespace FleetManager;

using FleetCore;
using Npgsql;
using NpgsqlTypes;
using System.Threading;
using System.Threading.Tasks;

public sealed class PostgresFleetTaskRepository : IFleetTaskRepository
{
    private const string CreateTableSql = """
        CREATE TABLE IF NOT EXISTS fleet_tasks (
            task_id text PRIMARY KEY,
            to_lane text NOT NULL,
            created_at timestamptz NOT NULL,
            state text NOT NULL,
            assigned_shuttle_id text NULL,
            failure_reason text NULL
        )
        """;

    private const string SaveTaskSql = """
        INSERT INTO fleet_tasks (
            task_id, to_lane, created_at, state, assigned_shuttle_id, failure_reason
        ) VALUES (
            @task_id, @to_lane, @created_at, @state, @assigned_shuttle_id, @failure_reason
        )
        ON CONFLICT (task_id) DO UPDATE SET
            to_lane = EXCLUDED.to_lane,
            created_at = EXCLUDED.created_at,
            state = EXCLUDED.state,
            assigned_shuttle_id = EXCLUDED.assigned_shuttle_id,
            failure_reason = EXCLUDED.failure_reason
        """;

    private readonly string _connectionString;

    public PostgresFleetTaskRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task SaveAsync(FleetTask task, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureTableAsync(connection, cancellationToken);
        await SaveTaskAsync(connection, null, task, cancellationToken);
    }

    public async Task SaveAllAsync(IEnumerable<FleetTask> tasks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureTableAsync(connection, cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var task in tasks)
        {
            ArgumentNullException.ThrowIfNull(task);
            await SaveTaskAsync(connection, transaction, task, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IEnumerable<FleetTask>> LoadAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureTableAsync(connection, cancellationToken);

        const string sql = """
            SELECT task_id, to_lane, created_at, state, assigned_shuttle_id, failure_reason
            FROM fleet_tasks
            ORDER BY created_at ASC, task_id ASC
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var tasks = new List<FleetTask>();
        while (await reader.ReadAsync(cancellationToken))
            tasks.Add(ReadTask(reader));

        return tasks;
    }

    public async Task<FleetTask?> GetAsync(string taskId, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureTableAsync(connection, cancellationToken);

        const string sql = """
            SELECT task_id, to_lane, created_at, state, assigned_shuttle_id, failure_reason
            FROM fleet_tasks
            WHERE task_id = @task_id
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("task_id", taskId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadTask(reader) : null;
    }

    private static async Task EnsureTableAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(CreateTableSql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SaveTaskAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        FleetTask task,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(SaveTaskSql, connection, transaction);
        command.Parameters.AddWithValue("task_id", task.TaskId);
        command.Parameters.AddWithValue("to_lane", task.ToLane);
        command.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, task.CreatedAt);
        command.Parameters.AddWithValue("state", task.State.ToWireName());
        command.Parameters.AddWithValue(
            "assigned_shuttle_id",
            NpgsqlDbType.Text,
            (object?)task.AssignedShuttleId ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "failure_reason",
            NpgsqlDbType.Text,
            (object?)task.FailureReason ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static FleetTask ReadTask(NpgsqlDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetDateTime(2),
        TaskStateWire.FromWireName(reader.GetString(3)),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        null);
}