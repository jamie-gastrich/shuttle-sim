namespace ShuttleCore;

public sealed class Shuttle
{
    private readonly Rack _rack;
    private readonly ShuttleTiming _timing;
    private ShuttleState _state = ShuttleState.Idle;
    private string _position;
    private string? _carriedPalletId;
    private string? _faultReason;
    private DateTime? _lastEventAt;
    private DateTime _moveStartedAt;
    private string _moveToLane = string.Empty;
    private DateTime _phaseStartedAt;
    private string _phaseSlot = string.Empty;

    public Shuttle(string shuttleId, Rack rack, string startLane, ShuttleTiming timing)
    {
        if (string.IsNullOrWhiteSpace(shuttleId)) throw new ArgumentException("id required", nameof(shuttleId));
        if (!rack.IsLane(startLane)) throw new ArgumentException($"unknown lane {startLane}", nameof(startLane));

        ShuttleId = shuttleId;
        _rack = rack;
        _timing = timing;
        _position = startLane;
    }

    public string ShuttleId { get; }
    public ShuttleState State => _state;
    public string Position => _position;
    public string? CarriedPalletId => _carriedPalletId;
    public string? FaultReason => _faultReason;

    public void StartMove(string toLane, DateTime now)
    {
        EnsureInOrder(now);
        EnsureState(ShuttleState.Idle, "start a move");
        if (!_rack.IsLane(toLane)) throw new ShuttleRejectedException($"unknown lane {toLane}");
        _moveToLane = toLane;
        _moveStartedAt = now;
        _state = ShuttleState.Moving;
        _lastEventAt = now;
    }

    public void CompleteMove(DateTime now)
    {
        EnsureInOrder(now);
        EnsureState(ShuttleState.Moving, "complete a move");
        var travel = TravelTime(_position, _moveToLane);
        if (now - _moveStartedAt < travel)
            throw new ShuttleRejectedException($"travel to {_moveToLane} has not elapsed");
        _position = _moveToLane;
        _state = ShuttleState.Idle;
        _lastEventAt = now;
    }

    public void StartLoad(string slotId, DateTime now)
    {
        EnsureInOrder(now);
        EnsureState(ShuttleState.Idle, "start loading");
        EnsureSlotInLane(slotId);
        if (_carriedPalletId is not null) throw new ShuttleRejectedException("already carrying a pallet");
        if (!_rack.IsOccupied(slotId)) throw new ShuttleRejectedException($"slot {slotId} holds no pallet");
        _phaseSlot = slotId;
        _phaseStartedAt = now;
        _state = ShuttleState.Loading;
        _lastEventAt = now;
    }

    public void CompleteLoad(DateTime now)
    {
        EnsureInOrder(now);
        EnsureState(ShuttleState.Loading, "complete loading");
        if (now - _phaseStartedAt < _timing.Load) throw new ShuttleRejectedException("load not elapsed");
        _carriedPalletId = _rack.Take(_phaseSlot);
        _state = ShuttleState.Idle;
        _lastEventAt = now;
    }

    public void StartUnload(string slotId, DateTime now)
    {
        EnsureInOrder(now);
        EnsureState(ShuttleState.Idle, "start unloading");
        EnsureSlotInLane(slotId);
        if (_carriedPalletId is null) throw new ShuttleRejectedException("not carrying a pallet");
        if (_rack.IsOccupied(slotId)) throw new ShuttleRejectedException($"slot {slotId} is occupied");
        _phaseSlot = slotId;
        _phaseStartedAt = now;
        _state = ShuttleState.Unloading;
        _lastEventAt = now;
    }

    public void CompleteUnload(DateTime now)
    {
        EnsureInOrder(now);
        EnsureState(ShuttleState.Unloading, "complete unloading");
        if (now - _phaseStartedAt < _timing.Unload) throw new ShuttleRejectedException("unload not elapsed");
        _rack.Place(_phaseSlot, _carriedPalletId!);
        _carriedPalletId = null;
        _state = ShuttleState.Idle;
        _lastEventAt = now;
    }

    public void RaiseFault(string reason, DateTime now)
    {
        EnsureInOrder(now);
        if (_state == ShuttleState.Fault) throw new ShuttleRejectedException("already faulted");
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("reason required", nameof(reason));
        _faultReason = reason;
        _state = ShuttleState.Fault;
        _lastEventAt = now;
    }

    private void EnsureInOrder(DateTime now)
    {
        if (_lastEventAt is DateTime previous && now < previous)
            throw new ShuttleRejectedException($"clock went backwards: {now:O} < {previous:O}");
    }

    private void EnsureState(ShuttleState expected, string what)
    {
        if (_state != expected)
            throw new ShuttleRejectedException($"cannot {what} while {_state.ToWireName()}");
    }

    private void EnsureSlotInLane(string slotId)
    {
        if (!_rack.IsSlot(slotId)) throw new ShuttleRejectedException($"unknown slot {slotId}");
        if (!_rack.IsSlotInLane(slotId, _position))
            throw new ShuttleRejectedException($"slot {slotId} is not in lane {_position}");
    }

    private TimeSpan TravelTime(string fromLane, string toLane) =>
        TimeSpan.FromTicks(_rack.DistanceInLanes(fromLane, toLane) * _timing.MovePerLane.Ticks);
}