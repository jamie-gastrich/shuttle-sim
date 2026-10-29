namespace ShuttleCore;

public sealed class Rack
{
    private readonly Dictionary<string, int> _laneOrdinals = new();
    private readonly Dictionary<string, string> _slotLanes  = new();
    private readonly Dictionary<string, string?> _palletsBySlot  = new();

    public Rack(int aisleCount, int lanesPerAisle, int slotsPerLane)
    {
        if (aisleCount < 1)
            throw new ArgumentOutOfRangeException(nameof(aisleCount), "Aisle count must be at least 1.");
        if (lanesPerAisle < 1)
            throw new ArgumentOutOfRangeException(nameof(lanesPerAisle), "Lanes per aisle must be at least 1.");
        if (slotsPerLane < 1)
            throw new ArgumentOutOfRangeException(nameof(slotsPerLane), "Slots per lane must be at least 1.");

        AisleCount = aisleCount;
        LanesPerAisle = lanesPerAisle;
        SlotsPerLane = slotsPerLane;

        var ordinal = 0;
        for (var aisle = 1; aisle <= AisleCount; aisle++)
        {
            for (var lane = 1; lane <= LanesPerAisle; lane++)
            {
                var laneId = $"A{aisle:00}-L{lane:00}";
                _laneOrdinals[laneId] = ordinal++;
                for (var slot = 1; slot <= SlotsPerLane; slot++)
                {
                    var slotId = $"{laneId}-S{slot:00}";
                    _slotLanes[slotId] = laneId;
                    _palletsBySlot[slotId] = null;
                }
            }
        }
    }

    public int AisleCount { get; }
    public int LanesPerAisle { get; }
    public int SlotsPerLane { get; }
    public IEnumerable<string> LaneId => _laneOrdinals.Keys;
    public IEnumerable<string> SlotId => _palletsBySlot.Keys;
    private readonly HashSet<string> _blockedLanes = new(StringComparer.Ordinal);

    public bool IsLane(string laneId) => _laneOrdinals.ContainsKey(laneId);
    public bool IsSlot(string slotId) => _palletsBySlot.ContainsKey(slotId);
    public bool IsSlotInLane(string slotId, string laneId) => 
        _slotLanes.TryGetValue(slotId, out var home) && home == laneId;

    public int DistanceInLanes(string fromLane, string toLane) =>
        Math.Abs(Ordinal(fromLane) - Ordinal(toLane));

    public bool IsOccupied(string slotId) => PalletAt(slotId) is not null;

    public string? PalletAt(string slotId) => _palletsBySlot.TryGetValue(slotId, out var pallet) 
        ? pallet 
        : throw new ArgumentException($"Slot '{slotId}' does not exist.");

    public void Place(string slotId, string palletId)
    {
        if (!_palletsBySlot.ContainsKey(slotId)) throw new ArgumentException($"unknown slot {slotId}");
        if (_palletsBySlot[slotId] is not null) throw new InvalidOperationException($"{slotId} is occupied");
        _palletsBySlot[slotId] = palletId;
    }

    public string Take(string slotId)
    {
        if (!_palletsBySlot.ContainsKey(slotId)) throw new ArgumentException($"unknown slot {slotId}");
        var pallet = _palletsBySlot[slotId] ?? throw new InvalidOperationException($"{slotId} is empty");
        _palletsBySlot[slotId] = null;
        return pallet;
    }

    private int Ordinal(string laneId) => _laneOrdinals.TryGetValue(laneId, out var ordinal)
        ? ordinal
        : throw new ArgumentException($"unknown lane {laneId}", nameof(laneId));

    public void MarkBlocked(string laneId)      
    {
        if (!_laneOrdinals.ContainsKey(laneId)) throw new ArgumentException($"unknown lane {laneId}");
        _blockedLanes.Add(laneId);
    }

    // Removes a lane from the blocked set. Mirrors MarkBlocked: unknown lanes are rejected.
    public void UnmarkBlocked(string laneId)
    {
        if (!_laneOrdinals.ContainsKey(laneId)) throw new ArgumentException($"unknown lane {laneId}", nameof(laneId));
        _blockedLanes.Remove(laneId);
    }

    // Blocked lanes whose ordinals fall between fromLane and toLane (inclusive).
    // Used by Shuttle to record which lane(s) caused a blocked-route fault.
    public IEnumerable<string> BlockedLanesOnRoute(string fromLane, string toLane)
    {
        var start = Math.Min(Ordinal(fromLane), Ordinal(toLane));
        var end = Math.Max(Ordinal(fromLane), Ordinal(toLane));
        foreach (var lane in _blockedLanes)
            if (Ordinal(lane) >= start && Ordinal(lane) <= end)
                yield return lane;
    }

    public bool IsBlocked(string laneId)
    {
        return _blockedLanes.Contains(laneId);
    }

    public bool IsRouteBlocked(string fromLane, string toLane)
    {
        return BlockedLanesOnRoute(fromLane, toLane).Any();
    }
}