---
name: determinism
description: Verify or debug determinism of the battle simulation — run determinism tests, investigate a hash mismatch or desync, a replay that no longer reproduces, a test that passes or fails randomly, or a difference between dotnet and Unity results; regenerate golden replays after an intentional rule change. Use whenever "hash", "desync", "replay mismatch", "flaky sim test" or "golden" comes up.
---

# Determinism: verify and debug

Contract: scenario + seed + command log ⇒ the same state hash at every tick, on every run, OS, CPU and runtime.
Anything else is a bug, even if the battle "looks the same".

## Quick verification

```bash
python3 tools/check_sim_purity.py
dotnet test sim/Knez.Sim.sln --filter Category=Determinism
```

The determinism suite contains (add to it when you add a subsystem):
1. **Twin run** — the same scenario and commands simulated twice in one process ⇒ identical hash every tick.
2. **Fresh process** — hashes recorded by one process match a second process (catches randomised string hashes,
   static state, uninitialised fields).
3. **Snapshot/restore** — snapshot at tick N, restore, continue ⇒ same hashes as the uninterrupted run.
4. **Golden replays** — `sim/tests/Golden/*.replay.json` re-simulated ⇒ every checkpoint hash matches. Until replay
   files exist, the golden check is the `GoldenFinalHash` constant in `DeterminismTests`; an intended outcome change
   updates it, with the reason in the commit message.
5. **Cross-runtime** (from M2) — the same golden replays in a Unity EditMode test.

## Debugging a mismatch

1. **Reproduce headless.** Get the replay (from a test, a player, or `--out`). Run
   `dotnet run --project sim/tools/Knez.Sim.Cli -- replay verify <file> --hash-every 1`. It reports the first
   divergent checkpoint.
2. **Find the first divergent tick.** If checkpoints are sparse, re-run with `--hash-every 1`, or bisect: the first
   tick where the hash differs is the one to study. Ignore later ticks — divergence snowballs.
3. **Find the section.** `StateHasher` keeps per-section sub-hashes (fighters, squads, morale, combat, RNG, …).
   Print them for the bad tick from both runs (`--dump-sections <tick>`). The first section that differs points to
   the system that wrote it.
4. **Find the system.** Hash after each system for that tick (`--hash-per-system <tick>`). The first system whose
   output differs is the culprit; diff the dumped state of that section.
5. **Name the cause** (most likely first):
   - iteration over `Dictionary`/`HashSet` or sorting without a full tie-break (`OrderBy(distance)` without `ThenBy(id)`)
   - a field missing from `StateHasher` or `Snapshot` (state diverges silently, then shows up elsewhere)
   - RNG drawn from the wrong stream, or a conditional extra draw that differs between runs
   - `float`/`double` sneaking in (a helper, a content loader leaking doubles, a `Math.*` call)
   - mutable `static` state or a cache surviving between battles in one process
   - `GetHashCode()` of strings/objects used for ordering or ids
   - reading wall-clock time, environment, culture-specific parsing (`double.Parse` without invariant culture)
   - overflow in `Fix` multiply at extreme values (different but deterministic — still a bug)
6. **Fix and pin it.** Add a test that fails without the fix (often a twin-run of a minimal scenario). Then run the
   whole determinism suite.

## Flaky sim tests

A sim test that sometimes fails is a determinism bug until proven otherwise. Check: test order dependence (shared
static state), seeds taken from time, parallel test execution touching shared objects. Never "fix" by retrying.

## Golden replays after an intentional change

When a rule change alters outcomes, golden replays must be regenerated on purpose:

```bash
dotnet run --project sim/tools/Knez.Sim.Cli -- golden regenerate sim/tests/Golden
git diff --stat sim/tests/Golden
```

`sim/tests/Golden/balance-reference.json` (M3 step 7) is not a hash golden: it pins balance measures over many
seeds with tolerances. Re-recorded traces with the balance reference still green mean the change reshuffled outcomes
without moving balance; re-record the reference itself only when balance changes on purpose (battle-sim.md §11).

Only regenerate after you understand *why* outcomes changed. The commit message says it, e.g.
`sim: flank bonus +100‰ — golden replays regenerated (hit rolls change)`. A change that should be
outcome-neutral (refactor, perf) must leave golden replays untouched; if they change, it's a bug.

## If the CLI flags above don't exist yet

They are the intended contract (battle-sim.md §15, §17). If a flag is missing when you need it, add it to
`Knez.Sim.Cli` as part of the fix — it will be needed again.
