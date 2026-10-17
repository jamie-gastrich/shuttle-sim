# Hints for Lesson 18: Operator-acknowledged recovery

**Tier 1 — nudge (read this first)**

- Look at how `MoveCommandMessage` and `CommandAckMessage` are structured. Copy the envelope pattern exactly (JsonPropertyName attributes match).
- For `Recover` on Shuttle, mirror `RaiseFault`: call `EnsureInOrder`, check state, set fields, update `_lastEventAt`. Don't touch Position/CarriedPalletId.
- Contract bump to v4 means new kinds + version constant change. Keep v3 unchanged.
- Recovery is command-like: reuse Command QoS in the MQTT mapping (map `MessageFamily.Recovery` to `_options.Qos.Command`).

**Tier 2 — direction (if you're stuck)**

- In `TopicScheme`, add the three methods: `RecoveryCommand`, `RecoveryAck`, `AllRecoveryAcks`. Follow the same pattern as `Command`/`Ack`/`AllAcks`.
- In `MessageFamily`, just add `Recovery` as a new enum member (order doesn't matter much).
- In `ToMqttQos`, add the case before the default throw (or after Alarm). Use Command QoS.
- For tests: in `StateTransitionTests`, test both the success case (from Fault) and rejection cases (from Idle). Also verify FaultReason becomes null.
- For the contract doc `v4.md`, mirror the structure of v3.md: version, envelope reference, then each new message kind with table and JSON example.

**Tier 3 — near-solution (structural only, no full test bodies)**

- `Recover`: 
  ```csharp
  public void Recover(DateTime now) {
      EnsureInOrder(now);
      if (_state != ShuttleState.Fault)
          throw new ShuttleRejectedException($"cannot recover while {_state.ToWireName()}");
      _state = ShuttleState.Idle;
      _faultReason = null;
      _lastEventAt = now;
  }
  ```
- `RecoveryCommandMessage`/`RecoveryAckMessage`: same shape as MoveCommand/CommandAck but with their own kinds.
- `ContractVersion.Current = 4` — this is the only version change.
- When adding recovery topics, make sure `AllRecoveryAcks` uses wildcard `+` like `AllAcks`.
- Don't forget to update `docs/contract/v4.md` — the lesson requires it exists with both message kinds documented.

**Notes:** This lesson focuses on building blocks (contract + shuttle transition), not wiring the full end-to-end recovery path in FleetServeLoop/ShuttleEndpoint. Keep changes minimal and scoped to the three concepts.
