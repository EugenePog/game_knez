---
name: sim-feature
description: Implement or change anything inside the battle simulation (sim/src/Knez.Sim or sim/tests) — a tick system, a state field, a command, fixed-point math, RNG use, pathfinding, steering, formations, combat, wounds, morale, heroes, enemy AI. Use whenever a task touches sim/ or the user asks to add or change a battle mechanic. Covers where code goes, determinism rules, the tick order, the new-state checklist and the test-first loop.
---

# Implementing a sim feature

The sim is the game. It must stay pure C# (no Unity), deterministic, and testable with `dotnet test`.
Read `AGENTS.md` hard rules once per session; this playbook is the working loop.

## 0. Before writing code

1. Find the mechanic in `docs/battle-sim.md` (section numbers below) and the current milestone in `docs/roadmap.md`.
   If the task belongs to a later milestone or is out of phase-1 scope, say so and ask before building it.
2. If the spec is silent on a design number or rule that defines how the game *feels* (hit chance, morale
   thresholds, speeds), propose a value with a reason and ask — don't bury an invented rule in code.
3. Look at the existing code first: `ls sim/src/Knez.Sim`, `BattleSimulation.CreateDefaultSystems()`, the matching
   test folder. The API names below (`ISimSystem.Execute(state, context)`, `TickContext.Rng(stream)`,
   `BattleState`, `TestBattle`) are real since M0; if the code changes, update this skill in the same change.

## 1. Where things go

| Thing | Place | Rule |
|---|---|---|
| Data that changes during a battle | `BattleState` (or a part of it, e.g. `FighterMorale`) | the only mutable state |
| Rules that change data | a system in `sim/src/Knez.Sim/<Area>/<Name>System.cs` | stateless; reads/writes `BattleState` |
| External input | `sim/src/Knez.Sim/Commands/<Name>Command.cs` | `Validate(state)` → `RejectReason` (no RNG, no writes); `Apply(state, context)` only if valid |
| Tunable numbers | content defs in `sim/src/Knez.Sim/Defs/` (`FighterDef`, `FormationDef`, …), values in `content/defs/*.json` | no magic numbers in systems |
| Reading content | `sim/src/Knez.Sim.Content/` (`DefLibrary`, `ScenarioLoader`, `JsonObjectReader`) | text in, defs out; no files, no `double` |
| Rules that shape feel (thresholds, reaches, costs) | a content def too, e.g. `MovementDef` ← `content/defs/movement.json` (decided 2026-11-21) | never a constant in code |
| Numerical constants (precision, safety) | a `*Rules` static class next to the system, e.g. `Movement/MovementRules.cs` | named constants with a why; nothing that changes how the battle feels |
| Timed orders for tests/bench | the scenario `script` (battle-sim.md §7) | goes through the command's Validate/Apply; not logged |
| Things the view must react to | `SimEvent` subtype, emitted with `context.Emit(…)` | carry ids and reason codes |
| Scratch buffers | `TickContext` (internal properties) | never fields on systems or `BattleState` |
| Math | `Fix`, `FixVec2`, `Angle`, `FixMath` | no float/double anywhere in `sim/src` |
| Randomness | `ctx.Rng(RngStream.Combat)` etc. | one stream per purpose; never `System.Random` |

Tick order is fixed (battle-sim.md §9). A new system goes into that table **and** into the registration list in
`BattleSimulation` at the same position. Moving a system is a rule change: update the doc and expect golden
replays to change.

## 2. Test first

Write the failing NUnit test before the implementation:

```csharp
using Knez.Sim.Tests.Support;

namespace Knez.Sim.Tests.Movement;

[TestFixture]
public class MovementSystemTests
{
    [Test]
    public void Step_FighterWithGoal_ReachesItWithinTwoSeconds()
    {
        var sim = TestBattle.Create(seed: 7, wander: false); // 4 fighters, standard pipeline
        var fighter = sim.State.Fighters[0];
        sim.Enqueue(/* the command under test, e.g. a move order for fighter.Id */);

        sim.StepTicks(2 * BattleState.TicksPerSecond);

        Assert.That(fighter.Position, Is.EqualTo(/* expected FixVec2 */));
    }
}
```

- Helpers live in `sim/tests/Knez.Sim.Tests/Support/`: `TestBattle` (small battles), `NudgeCommand`,
  `RecordingCommand`, `RejectedCommand`, `WanderSystem`. Extend them rather than building ad-hoc setups.
- Tests may use modern C# (file-scoped namespaces, primary constructors); only `sim/src` is limited to C# 9.
- Test the rule in isolation (unit), then add a scenario-level assertion if the mechanic shows up in a battle.
- Scenario tests assert ranges over several seeds, not single lucky outcomes: `[Category("Scenario")]`.
- LINQ and lambdas are fine in tests; not in `sim/src` hot paths.

## 3. Implement under the rules

- Iterate fighters/squads in `EntityId` order: systems use `state.FighterList` with a `for` loop (the list is kept
  sorted). Break ties by id, never by list position of a `Dictionary`/`HashSet`.
- Integer ticks for every duration; `Fix` for every continuous quantity; permille ints for chances. `Fix` `*` and `/`
  truncate toward zero; Debug builds throw on overflow — a test that overflows found a real range problem.
- No allocations per fighter per tick: reuse scratch buffers owned by `TickContext`; no LINQ, closures, boxing,
  `params`, string formatting in hot paths.
- No exceptions for control flow: invalid input is a `RejectReason` from `Validate`. An exception during a tick faults
  the simulation for good, so throw only for programmer errors.
- State setters are `internal`: only code inside `Knez.Sim` (and tests) writes state; keep new setters internal too.
- Every morale/hit/damage change that the player could question carries a reason code in its event.

## 4. New state checklist

For every field you add to `BattleState` or its parts:

- [ ] initialised from the scenario/defs (not left at a default by accident)
- [ ] included in `StateHasher` in canonical order, in the right section sub-hash
- [ ] copied by the type's `Clone` (deep copy for reference types; `MemberwiseClone` shares lists and arrays)
- [ ] covered by the snapshot → restore → continue determinism test (usually automatic; check)
- [ ] listed in `StateCoverageTests.KnownFields` — that test fails on purpose until you do
- [ ] exposed read-only to the view if the view needs it

A field that isn't hashed is a desync nobody will catch.

For a new **def** field (defs are immutable and shared by every clone, so they are not part of the checklist above):

- [ ] get-only property set in the constructor, validated there (`ArgumentException`; the loader locates it)
- [ ] added to the def's `AddTo` (the scenario fingerprint) — otherwise two different contents share a hash
- [ ] read by the loader in `Knez.Sim.Content` and added to its allowed-members list; documented in battle-sim.md §7
- [ ] set in `content/defs/*.json` (a design number: propose a value with a reason and ask)

## 5. Verify

```bash
dotnet build sim/Knez.Sim.sln            # warning-free
dotnet test sim/Knez.Sim.sln             # all green
dotnet test sim/Knez.Sim.sln --filter Category=Determinism
python3 tools/check_sim_purity.py
```

If behaviour is visible on the battlefield, run a scenario and look at it (`battle-run` skill). If outcomes changed
on purpose, regenerate golden replays (`determinism` skill) and say why in the commit message.

## 6. Finish

1. Update `docs/battle-sim.md` if a rule, the tick order, a command or a data format changed. Hard-to-reverse
   decision → ADR.
2. Ask the `sim-reviewer` subagent to review the diff; fix what it finds or explain why not.
3. Commit with a scoped message: `sim: <what changed>`.
