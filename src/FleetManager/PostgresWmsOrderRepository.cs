namespace FleetManager;

using FleetCore;
using Npgsql;
using NpgsqlTypes;
using System.Threading;
using System.Threading.Tasks;

public sealed class PostgresWmsOrderRepository : IWmsOrderRepository
{
    private const string CreateTableSql = """
        CREATE TABLE IF NOT EXISTS wms_orders (
            order_id text PRIMARY KEY,
            kind text NOT NULL,
            pallet_id text NOT NULL,
            lane_id text NOT NULL,
            state text NOT NULL,
            received_at timestamptz NOT NULL
        )
        """;

    private const string SaveOrderSql = """
        INSERT INTO wms_orders (
            order_id, kind, pallet_id, lane_id, state, received_at
        ) VALUES (
            @order_id, @kind, @pallet_id, @lane_id, @state, @received_at
        )
        ON CONFLICT (order_id) DO UPDATE SET
            kind = EXCLUDED.kind,
            pallet_id = EXCLUDED.pallet_id,
            lane_id = EXCLUDED.lane_id,
            state = EXCLUDED.state,
            received_at = EXCLUDED.received_at
        """;

    private readonly string _connectionString;

    public PostgresWmsOrderRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public string ConnectionString => _connectionString;

    public async Task SaveAsync(WmsOrder order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureTableAsync(connection, cancellationToken);
        await SaveOrderAsync(connection, null, order, cancellationToken);
    }

    internal async Task SaveAsync(WmsOrder order, NpgsqlConnection connection, NpgsqlTransaction? transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(connection);
        await EnsureTableAsync(connection, cancellationToken);
        await SaveOrderAsync(connection, transaction, order, cancellationToken);
    }

    public async Task SaveAllAsync(IEnumerable<WmsOrder> orders, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orders);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureTableAsync(connection, cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var order in orders)
        {
            ArgumentNullException.ThrowIfNull(order);
            await SaveOrderAsync(connection, transaction, order, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IEnumerable<WmsOrder>> LoadAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureTableAsync(connection, cancellationToken);

        const string sql = """
            SELECT order_id, kind, pallet_id, lane_id, state, received_at
            FROM wms_orders
            ORDER BY received_at ASC, order_id ASC
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var orders = new List<WmsOrder>();
        while (await reader.ReadAsync(cancellationToken))
            orders.Add(ReadOrder(reader));

        return orders;
    }

    public async Task<WmsOrder?> GetAsync(string orderId, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureTableAsync(connection, cancellationToken);

        const string sql = """
            SELECT order_id, kind, pallet_id, lane_id, state, received_at
            FROM wms_orders
            WHERE order_id = @order_id
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("order_id", orderId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadOrder(reader) : null;
    }

    private static async Task EnsureTableAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(CreateTableSql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SaveOrderAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        WmsOrder order,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(SaveOrderSql, connection, transaction);
        command.Parameters.AddWithValue("order_id", order.OrderId);
        command.Parameters.AddWithValue("kind", order.Kind);
        command.Parameters.AddWithValue("pallet_id", order.PalletId);
        command.Parameters.AddWithValue("lane_id", order.LaneId);
        command.Parameters.AddWithValue("state", order.State);
        command.Parameters.AddWithValue("received_at", NpgsqlDbType.TimestampTz, order.ReceivedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static WmsOrder ReadOrder(NpgsqlDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetDateTime(5));
}