using Contracts;
using ShuttleCore;

namespace FleetManager;

public sealed class ShuttleRegistry
{
    private readonly Dictionary<string, ShuttleSnapshot> _shuttles = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public void Record(ShuttleStatus status)
    {
        lock (_gate)
        {
            _shuttles[status.ShuttleId] = new ShuttleSnapshot(
                status.ShuttleId, status.State, status.Position, status.Timestamp);
        }
    }

    public bool IsIdle(string shuttleId)
    {
        lock (_gate)
        {
            // A shuttle we have never heard from is not idle: we do not give work
            // to a shuttle we know nothing about.
            return _shuttles.TryGetValue(shuttleId, out var shuttle)
                && shuttle.State == ShuttleState.Idle.ToWireName();
        }
    }

    public bool TryGet(string shuttleId, out ShuttleSnapshot? snapshot)
    {
        lock (_gate)
        {
            return _shuttles.TryGetValue(shuttleId, out snapshot);
        }
    }

    public string? PositionOf(string shuttleId)
    {
        lock (_gate)
        {
            return _shuttles.TryGetValue(shuttleId, out var shuttle) ? shuttle.Position : null;
        }
    }

    public IReadOnlyList<string> FreeShuttles()
    {
        lock (_gate)
        {
            // Id order makes the same shuttle win the same race every time, so the
            // behaviour is easy to explain and easy to test.
            return _shuttles.Values
                .Where(shuttle => shuttle.State == ShuttleState.Idle.ToWireName())
                .Select(shuttle => shuttle.ShuttleId)
                .OrderBy(shuttleId => shuttleId, StringComparer.Ordinal)
                .ToList();
        }
    }

    public IReadOnlyList<ShuttleSnapshot> Snapshot()
    {
        lock (_gate)
        {
            // Copy under the lock so a status update cannot change the list
            // while the dashboard (or a test) enumerates it.
            return _shuttles.Values
                .OrderBy(shuttle => shuttle.ShuttleId, StringComparer.Ordinal)
                .ToList();
        }
    }

    public IReadOnlyList<string> ShuttleIds()
    {
        lock (_gate)
        {
            return _shuttles.Keys
                .OrderBy(shuttleId => shuttleId, StringComparer.Ordinal)
                .ToList();
        }
    }
}

public sealed record ShuttleSnapshot(
    string ShuttleId, string State, string Position, DateTime SeenAt);