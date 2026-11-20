# Roadmap

Principle: **prove the battle is fun before investing in art and campaign.** Each milestone ends with a checkpoint;
don't start the next one until it passes. Update the "Current" marker when a milestone closes.

**Current: M0 — Foundation.**

## Phase 1 — internal 2D skirmish vs AI (6–8 weeks)

### M0. Foundation (weeks 1–2)
1. Repository: Git + Git LFS, layout from `AGENTS.md`, `AGENTS.md`/`CLAUDE.md`, `docs/battle-sim.md`, ADRs,
   skills and subagents, purity check hook, `tools/bootstrap.sh` (checks .NET 10, Git LFS and Python 3.13, creates `.venv`).
   ✅ docs, agent setup and bootstrap — installing `dotnet-sdk@10` and git-lfs, then running the bootstrap, is pending.
2. Sim skeleton in `sim/`: solution and projects (`Knez.Sim`, `Knez.Sim.Tests`), `Directory.Build.props`
   (C# 9, nullable, warnings as errors, artifacts output), Unity package files (`package.json`, `.asmdef`).
   `EntityId`, `Fix`/`FixVec2` basics, `SimRng` (PCG32 + streams), `BattleState`, `ICommand` + `CommandQueue`,
   `BattleSimulation.Step()` with the empty systems of the tick pipeline, `StateHasher`, `Snapshot`.
   Tests: RNG known-answer, `Fix` arithmetic, determinism (same seed + commands twice ⇒ same hash sequence;
   snapshot/restore ⇒ same continuation).
3. CI: GitHub Actions running build, tests and the purity check on every push.
4. CLI skeleton: `run` on an empty scenario prints tick count and final hash.

**Checkpoint:** `dotnet test` green on a clean clone; purity check clean; an agent took one small sim feature from
spec to merged commit using the `sim-feature` playbook and `sim-reviewer` without manual fixes. This checks how
the "you + LLM agents" loop works — write down what went wrong in `docs/playtests/` or the ADR log.

### M1. Movement and formations — main technical risk (weeks 2–4)
Grid map, A*, squad anchor + formation slots (Line, ShieldWall, Column), slot assignment, steering, separation,
collision resolution; benchmark scenarios S1–S5; CLI `--frames` + HTML replay viewer; `bench` command.

**Checkpoint:** M1 exit criteria in `battle-sim.md` §10 (no stuck > 2 s, overlap < 1 %, p99 < 2 ms for 50 fighters,
looks right in the viewer). If it fails, fix movement before anything else.

### M2. Playable 2D view (weeks 4–5)
Unity 6 project in `game/`, sim as local package, top-down 2D: circles, facing, selection, order arrows, morale bars,
pause/speed, issuing commands with the mouse. EditMode test that replays the golden replays inside Unity
(cross-runtime determinism).

**Checkpoint:** you can move squads, change formations and pause; golden replays match in Unity.

### M3. Combat, wounds, morale (weeks 5–6)
Melee actions with timings, hit resolution, flanks/rear, shield wall, wounds, fatigue, morale states, rout and
rally, hero leadership and 2–3 abilities per hero. Morale reasons shown in tooltips and the battle log.

**Checkpoint:** a fight can be won by flanking or by breaking morale, not only by attrition.

### M4. Enemy AI (weeks 6–7)
Side utility AI + squad FSM; 3 enemy squads that advance, hold, flank and regroup.

**Checkpoint:** the AI wins sometimes against a careless player and loses to a good plan.

### M5. Tuning and playtests (week 7 → exit)
Playtest log (`playtest-notes` skill), balance batches (`battle-analyst`), 2–3 map variants.

**Exit criterion for phase 1:** the battle is still interesting the 20th time. Not shown publicly.

## Phase 2 — public 3D skirmish
Same sim, 3D view in the style of Expeditions: Viking; Steam page, GIFs and videos, Steam Playtest with balance
branches, then a demo for Next Fest. Wishlist target 30–50k by Early Access.

## Phase 3 — campaign in Early Access
Prologue and Act I: escape, homestead by the town, first expedition of 3–4 battles, hiring, equipment, tribute,
save/load. The skirmish stays as a "free battle" mode.
