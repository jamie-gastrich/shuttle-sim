using FleetCore;

namespace FleetManager.Tests;

public static class TestDatabase
{
    public const string SentinelTaskId = "__fixture_probe__";

    public static string BuildConnectionString()
    {
        var configuredConnectionString = Environment.GetEnvironmentVariable("SHUTTLE_DB_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(configuredConnectionString))
            return configuredConnectionString;

        var builder = new Npgsql.NpgsqlConnectionStringBuilder
        {
            Host = Environment.GetEnvironmentVariable("SHUTTLE_DB_HOST") ?? "127.0.0.1",
            Username = Environment.GetEnvironmentVariable("SHUTTLE_DB_USER") ?? "shuttle",
            Password = Environment.GetEnvironmentVariable("SHUTTLE_DB_PASSWORD") ?? "shuttle",
            Database = Environment.GetEnvironmentVariable("SHUTTLE_DB_NAME") ?? "shuttle",
        };

        var configuredPort = Environment.GetEnvironmentVariable("DB_HOST_PORT");
        if (!string.IsNullOrWhiteSpace(configuredPort))
        {
            if (!int.TryParse(configuredPort, out var port))
                throw new InvalidOperationException("DB_HOST_PORT must be a valid integer.");

            builder.Port = port;
        }

        return builder.ConnectionString;
    }

    public static async Task ResetAsync(IFleetTaskRepository repository, CancellationToken cancellationToken)
    {
        await repository.GetAsync(SentinelTaskId, cancellationToken);

        await using var connection = new Npgsql.NpgsqlConnection(BuildConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new Npgsql.NpgsqlCommand("DELETE FROM fleet_tasks", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}