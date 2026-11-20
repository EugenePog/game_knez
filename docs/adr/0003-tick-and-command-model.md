# 0003. Fixed tick and tick-stamped commands

Status: Accepted
Date: 2026-11-19

## Context
Combat is real time with pause; the concept asks for actions with durations so a turn-based mode can be added later,
and for a command queue that keeps multiplayer possible. The view (2D now, 3D later) must not drive the rules.

## Decision
- The sim advances in fixed ticks of 50 ms (20 ticks/s). `BattleSimulation.Step()` = one tick.
- All durations (actions, cooldowns, morale windows) are integers in ticks.
- The view owns real time: it calls `Step()` 0..n times per frame; pause means no calls; speed changes the rate.
- External input is only `ICommand` objects in a `CommandQueue`. A command enqueued at state tick T applies at the
  start of tick T+1, in enqueue order; invalid commands are rejected with an event.
- Systems run in the fixed order documented in `battle-sim.md` §9 and hold no state.
- Replay = scenario + seed + command log (+ hash checkpoints).

## Consequences
- Replays, headless runs and tests come for free; pause and speed control are trivial.
- Movement looks steppy at 20 Hz unless the view interpolates — it must.
- A turn-based mode becomes "step until the next decision point".
- Reaction latency is at most one tick (50 ms), acceptable for RTwP.

## Alternatives considered
- **Variable timestep tied to frame rate:** non-deterministic, no replays.
- **60 Hz tick:** 3× the CPU for little gain in a tactical game; can be revisited per M1 bench.
- **Event-driven (next-event) simulation:** elegant for turn-based, awkward for continuous steering.
