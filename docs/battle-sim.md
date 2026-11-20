# Battle simulation — technical design

Status: **draft v0.3** (2026-11-20, M1 step 3). Owner: Eugene. Scope: phase 1 skirmish; the same sim later drives the 3D view
and campaign battles. Code names follow the glossary in [AGENTS.md](../AGENTS.md#glossary-russian-design-term--code-name).

When code and this document disagree, fix one of them in the same change.

---

## 1. Goals

1. **Fun first:** commanding formations with pause must feel good, and the fight must not turn into mush.
2. **Deterministic:** scenario + seed + command log ⇒ identical state hash on every machine and runtime.
   This gives replays as bug reports, reproducible balance runs, A/B tests, and keeps multiplayer possible.
3. **Headless:** the whole battle runs in `dotnet test` and a CLI, with no Unity, so LLM agents can build and
   verify it.
4. **View-agnostic:** the 2D prototype, the replay viewer and the future 3D view read the same state.
5. **Turn-based ready:** actions have durations in ticks, so a turn-based mode can be added later
   (as Pathfinder: Kingmaker and Pillars of Eternity II did).

Non-goals for phase 1: campaign, persistence between battles, more than ~60 fighters, networking.

## 2. Decisions

| # | Decision | Status |
|---|---|---|
| D1 | Sim is pure C# without UnityEngine, tested with NUnit outside the editor — [ADR-0001](adr/0001-pure-csharp-sim.md) | Accepted (concept) |
| D2 | Fixed-point math (`Fix`, Q48.16 in `long`), no floating point in sim logic or state — [ADR-0002](adr/0002-fixed-point-math.md) | Accepted (veto before M1) |
| D3 | Fixed tick, 20 ticks/s; all durations in ticks; input only as tick-stamped commands — [ADR-0003](adr/0003-tick-and-command-model.md) | Accepted |
| D4 | Systems are stateless and run in a fixed order; all state is in `BattleState` | Accepted |
| D5 | Navigation: grid A* + our own steering/avoidance; no NavMesh or physics | Accepted (concept) |
| D6 | `Knez.Sim` targets `netstandard2.1`, C# 9; tests and tools target the current .NET LTS | Accepted |
| D7 | Unity consumes the sim as a local UPM package (`file:../../sim/src/Knez.Sim`) | Proposed — confirm in M2 |
| D8 | Agents verify behaviour through a headless CLI and an HTML replay viewer | Proposed — build in M0/M1 |
| D9 | Content (defs, scenarios) is JSON in `content/`, loaded outside the core by `Knez.Sim.Content` with our own strict reader — [ADR-0004](adr/0004-content-json-reader.md) | Accepted (M1 step 1) |

## 3. Architecture

```
 ┌──────────── view side (Unity 2D/3D, replay viewer) ─────────────┐
 │ input → Command          reads snapshot + events, interpolates   │
 └──────┬───────────────────────────────────────────▲──────────────┘
        │ Enqueue(cmd)                               │ IReadOnlyBattleState, SimEvent[]
 ┌──────▼──────────── Knez.Sim (pure C#) ────────────┴──────────────┐
 │ CommandQueue → BattleSimulation.Step() → systems in fixed order  │
 │                     │ reads/writes                               │
 │                 BattleState (all data) ── StateHasher, Snapshot  │
 └──────────────────────────────────────────────────────────────────┘
        ▲ ScenarioDef, content defs (built by Knez.Sim.Content from JSON)
```

Assemblies:

| Assembly | Target | References | Contents |
|---|---|---|---|
| `Knez.Sim` | netstandard2.1 | — | math, state, systems, commands, AI, hashing, replay model |
| `Knez.Sim.Content` | netstandard2.1 | Knez.Sim | own JSON reader → defs, scenario loading, exact decimal → `Fix` parsing; takes text, never files |
| `Knez.Sim.Tests` | net LTS | both + NUnit | unit, scenario, determinism tests |
| `Knez.Sim.Cli` | net LTS | both | `run`, `batch`, `bench`, `replay verify`, frame export |
| `Knez.Game.*` (Unity) | Unity | Knez.Sim(.Content) | view, input, UI; `Fix` → `float` conversion lives here |

## 4. Time model

- `TicksPerSecond = 20` (50 ms). `BattleState.Tick` counts completed ticks.
- `BattleSimulation.Step()` advances exactly one tick. The view owns real time: it accumulates frame time
  and calls `Step()` 0..n times per frame. Pause = no calls. Game speed ×0.5/×1/×2 = different call rate.
- The view interpolates between the previous and the current tick for smooth motion.
- Every action has a duration in ticks (e.g. a sword swing: windup 8, strike 2, recovery 10). A future
  turn-based mode advances the sim until the next *decision point* (a hero's action completes).

## 5. Numbers

- `Fix`: `readonly struct` over `long Raw`, 16 fractional bits (Q48.16, resolution ≈ 0.015 mm).
  Plain `long` multiplication is safe for game-range values (|a|,|b| < ~46 000); document any wider use.
- **Rounding:** `*` and `/` both truncate toward zero, so results are symmetric around zero — mirrored scenarios give
  mirrored numbers, and damped negative values decay to 0 like positive ones. (Decided 2026-11-20 before any
  movement code; changing it later changes every golden hash.)
- **Errors fail loudly:** division by zero always throws (explicit check — IL2CPP doesn't check integer division);
  Debug builds of `Knez.Sim` compile with overflow checking, so out-of-range math throws in tests; `FixMath.Sqrt`
  rejects values ≥ 2^32. Intended wraparound (RNG, hashing) is marked `unchecked`.
- `FixVec2.Length` works on the raw squares (full precision), so sub-millimetre vectors keep a non-zero length.
- `FixVec2` for positions/velocities (metres). Map axes: X east, Y north; the map spans (0, 0)–(width, height).
- `Angle` = 16-bit binary angle (65536 units per turn), wraps naturally. **0 = east (+X), counter-clockwise, 90° = north
  (+Y).** Degrees convert by rounding to the nearest unit, ties away from zero (`FromDegrees(30)` = 5461 units).
- `FixMath`: integer `Sqrt`; `Sin/Cos` from a committed 257-entry quarter-wave table
  (`Numerics/SineTable.cs`, generated once by `tools/gen_sine_table.py`) with linear interpolation — within 2/65536 of
  the true value, exact at multiples of 90°, exactly odd and mirror-symmetric; `Atan2` by binary search on the same
  table (exact on the axes and diagonals, exactly mirror-symmetric, within one unit elsewhere; (0, 0) → 0), `Clamp`.
  `FixVec2.WithLength`/`ClampLength` scale with 64-bit products instead of a rounded ratio, so a march step is exact. Tables are committed integer arrays — never computed with `double` at runtime.
- Percentages and probabilities: integers in permille (‰, 0..1000) or `Fix`.
- Content parses decimal text (`"1.25"`) into `Fix` with integer arithmetic, never via `double`: the nearest 1/65536,
  ties away from zero (`"0.1"` → raw 6554, `"-0.1"` → −6554); at most 9 decimals; exponents are rejected.
- Converting to `float` for rendering is done on the view side only.

## 6. Entities and state

- `EntityId`: `readonly struct` over `int`, allocated monotonically by `BattleState`, never reused; `0` = none.
- `BattleState` owns everything:
  - `Tick`, RNG stream states, the entity id counter;
  - `Map`: grid (cell size 0.5 m — to confirm in M1) with walkable, height level, cover and line-of-sight blockers;
  - `Sides` (player, enemy; later more — for now just `SideId` ints on squads and fighters), `Squads`, `Fighters`
    (heroes are fighters with a `HeroData` part);
  - spatial index for neighbour queries (rebuilt each tick, deterministic order);
  - per-tick `Events` buffer;
  - `Scenario`: an immutable reference to the `ScenarioDef` (map, starting squads, the defs they use), shared (not
    copied) by `Clone`; its `Fingerprint` (FNV-1a over everything battle-relevant, not the description) is hashed in the
    `Core` section. Fighters and squads reference their defs directly; defs are immutable (a test enforces readonly
    fields only).
- Scratch buffers live in `TickContext` (one per simulation), never in `BattleState` or system fields: they are not
  cloned or hashed, so two restored simulations can't share one.
- Storage: lists sorted by `EntityId`; lookup by binary search (an id → index map only if profiling asks for it).
  Systems iterate `state.FighterList` with `for` loops. Removed entities are marked dead and compacted at end of
  tick, preserving order.
- Fighter data groups: transform (position, velocity, facing), body (HP, wounds, fatigue), morale, combat
  (current action, cooldowns, target), squad membership and slot, def reference. Now (M1 step 3): id, def, side,
  position, velocity (m per tick), facing, squad id, slot index, alive.
- `Squad`: id, side, name, formation def, anchor (centre of the front rank), facing, order (`SquadOrder`: kind,
  target, arrival facing), velocity and spin (how far the anchor moved and the front turned this tick — they give each
  slot's own motion), member ids (sorted). Build the
  starting state with `BattleSetup.Create(scenario, seed)`: squads in scenario order, each followed by its fighters in
  slot order, every fighter on its slot facing with its squad.

## 7. Content definitions

Immutable classes in `Knez.Sim.Defs`, referenced directly by state (hashed by their string id; interning to ints only
if profiling asks): `FighterDef` (stats, radius, speed), `WeaponDef` (reach, timings, damage), `ArmorDef`, `ShieldDef`,
`FormationDef` (slot layout, spacing, stance modifiers), `AbilityDef`, `MapDef`, `ScenarioDef` (map, sides, squads,
spawn positions, victory conditions, seed defaults). Balance numbers live here, never as constants in systems. Each
def gets the fields its systems need when they arrive; M1 step 1 has:

| Def | Fields |
|---|---|
| `FighterDef` | `id`, `radius` (m), `walkSpeed`, `runSpeed` (m/s), `turnRate` (°/s) |
| `FormationDef` | `id`, `spacing` (m between neighbours), `rankSpacing` (m between ranks), `ranks` *or* `width`, `turnRate` (°/s) |
| `MapDef` | `width`, `height` (m) |
| `ScenarioDef` | `id`, `description`, `map`, `squads[]`: `name`, `side`, `fighter`, `count`, `formation`, `position` `[x, y]`, `facing` (degrees); optional `script[]`: `tick`, `order` (`"move"`), `squad` (name), `target` `[x, y]`, optional `facing` |

Agreed values (2026-11-20): walk 1.4 m/s, run 3.5 m/s; squad turn rate column 90°/s, line 45°/s, shield wall 30°/s.
Placeholders until the M1 bench: radius 0.3 m, fighter turn 360°/s, the formation spacings.

The **scenario script** is orders the scenario gives by itself at fixed ticks (benchmarks, tests, set pieces). In
tick order; each entry goes through the same validation as a player's `MoveSquad` at the start of its tick, before
queued commands, issued as the squad's own side, but is not written to the command log — the scenario (and its
fingerprint) already holds it. It exists because the CLI takes no commands and the S2–S4 benches need timed orders.
Sanity bounds also cover speeds (0.1..20 m/s) and turn rates (0..3600 °/s).

A scenario uses one def object per id (state hashes name defs by id); the fingerprint covers the defs its spawn
entries use. **Before M1 step 7** (`SetFormation`, later heroes), `ScenarioDef` must carry the battle's whole def
catalogue, fingerprint all of it, and commands must resolve def ids against `state.Scenario` — otherwise a def no spawn
used would be hashed nowhere. Sanity bounds (not balance): side 0..7, squad size 1..64, ranks/width 0..64.

Files: `content/defs/*.json` (objects with `fighters` / `formations` arrays, ids unique across files) and
`content/scenarios/**/*.json` (one scenario each, `id` = file name). `Knez.Sim.Content` reads them strictly
(ADR-0004): unknown or missing members, wrong types, unknown references and invalid values are errors located as
`file:line:column: json.path: message`. The CLI finds the defs in the nearest `defs/` folder above a scenario.

Formation slots (`FormationLayout`): numbered front rank first, left to right; the anchor is the centre of the front
rank; an incomplete rank is the rearmost and is centred. Width = `width`, or ⌈count / `ranks`⌉.

## 8. Commands

- `ICommand` with `SideId` and a type-specific payload; the tick and enqueue order live in `ScheduledCommand`.
  Phase 1 commands:
  `MoveSquad(squad, target, facing?)`, `SetFormation(squad, formation)`, `SetStance(squad, Hold|Advance|Pursue)`,
  `AttachHero(hero, squad|none)`, `HeroMove(hero, target)`, `HeroAttack(hero, target)`,
  `HeroUseAbility(hero, ability, target)`.
- A command enqueued while the state is at tick T is applied at the start of tick T+1. Ordering within a tick:
  enqueue sequence number.
- At its tick a command is first checked with `Validate(state)`, which gets no `TickContext` (no random draws, no
  events) and returns a `RejectReason`. Only a valid command is `Apply`-ed. An invalid one emits `CommandRejected`
  with the reason code (`unknown-entity`, `wrong-side`, … — stable strings for the UI) and changes nothing. Each
  command checks the issuing side's ownership itself.
- The command log + scenario + seed is the replay.
- `MoveSquad(side, squad, target, facing?)` (M1 step 3): rejected as `unknown-entity`, `wrong-side`, `not-alive` (no
  living member) or `off-map` (target outside the map). Applying it replaces the squad's order; without a facing the
  squad will end facing the direction from its anchor to the target (its current facing if the target is the anchor).

## 9. Tick pipeline (fixed order)

| # | System | Does |
|---|---|---|
| 0 | `BattleSimulation.Step()` | advance tick counter, clear events (not a system) |
| 1 | `CommandSystem` | apply this tick's commands |
| 2 | `AiSystem` | side → squad → fighter decisions; squads evaluated staggered (every 5 ticks, offset by id) |
| 3 | `OrderSystem` | squad orders → anchor goal, facing, formation slots, slot assignment |
| 4 | `PathSystem` | A* requests for squad anchors and detached fighters, per-tick budget, FIFO queue |
| 5 | `SteeringSystem` | desired velocity: slot seek/arrive, separation, obstacle avoidance |
| 6 | `MovementSystem` | integrate, resolve collisions (circle–circle, circle–grid), update facing |
| 7 | `TargetingSystem` | engagement and target choice in reach; tie-break by distance, then id |
| 8 | `CombatSystem` | advance actions (windup → strike → recovery), roll hits with `SimRng` |
| 9 | `DamageSystem` | damage, wounds, deaths |
| 10 | `MoraleSystem` | morale deltas, state transitions, rout and rally |
| 11 | `FatigueSystem` | fatigue gain/recovery from movement, combat, stance |
| 12 | `CleanupSystem` | compact dead, squad bookkeeping, victory check, end-of-tick events |
| 13 | `BattleSimulation.ComputeHash()` | state hash, called by tests and tools after a step (not a system) |

Systems 1–12 exist from M0 in `BattleSimulation.CreateDefaultSystems()`; all but `CommandSystem` are empty until
their milestone. Scratch buffers live in `TickContext`, never in system fields.

## 10. Movement and formations — the main risk (M1)

- A squad has an **anchor** (position + facing) that follows the A* path; the formation defines **slots**
  relative to the anchor. Fighters steer to their slots.
- **Move order (step 3, straight line until A* in step 6).** `OrderSystem` turns the squad's front toward the target at
  the formation's turn rate, in place, until the target is within 30° of the front (a fixed rule agreed 2026-11-20, not
  per formation); then the anchor marches straight
  at the walk speed of the slowest living member while the front finishes turning. Within 1 cm of the target the anchor
  stops there and turns to the order's facing; then the order is cleared (the squad holds). The squad pivots around its
  anchor, so a column's tail swings wide in an about-turn — revisit with wheeling in step 7.
- **Following the slot.** `SteeringSystem`: velocity = the slot's own motion this tick (from the anchor's velocity
  and spin) + a tenth of the lag the fighter had before (a 10-tick time constant; 1 m of lag closes to 2 cm in ~37
  ticks), capped at run speed. On a straight march a member never leaves its slot; a displaced one runs back. When the
  squad stands still and a member is within 2 cm of its slot, it steps exactly onto it, so formations settle instead of
  creeping. `MovementSystem` applies the velocity and turns the fighter at its turn rate: toward the squad's front while
  in formation — also when shuffling sideways or swinging round with a turning squad, whose outer slots move faster than
  walking — and toward its movement only when it moves faster than walking pace *relative to its own slot* (running to
  catch up). Both systems share the slot motion (`Movement/SlotTracking.cs`). Solver settings live in
  `Movement/MovementRules.cs`, not in content — tune them on the bench.
- A `MoveSquad` target within the 1 cm arrival distance keeps the squad's facing (a sub-centimetre offset has no
  meaningful direction).
- Slot assignment is stable (keep last assignment); after losses, reassign with a deterministic greedy
  min-distance pass in id order. Ranks fill from the back.
- Steering: arrive-at-slot + separation from neighbours + wall avoidance; engaged fighters are held in place
  (melee lock) and only shuffle to keep spacing.
- Collision: 2–3 relaxation passes in id order, push apart overlapping circles, clamp to walkable cells.
- If steering jams at 20 vs 30, upgrade to velocity obstacles (ORCA-like, fixed-point) — decide from the M1 bench.

Benchmark scenarios (in `content/scenarios/bench/`):

| Id | Scenario | Watch for |
|---|---|---|
| S1 | open-field clash, 20 vs 30 | jams, orbiting, overlap, line breaking apart |
| S2 | squad through a 3 m gate | deadlock at the choke |
| S3 | two squads crossing at 90° | interpenetration, oscillation |
| S4 | line wheels 90° | slot chaos, stragglers |
| S5 | shield wall holds vs a charge | wall integrity, push-through |

Metrics: *stuck fighter-ticks* (has a goal > 1 m away and speed < ε for > 2 s), mean slot error, overlap ratio
(pairs closer than r1 + r2 − 5 cm), tick time p50/p99.
M1 exit: no fighter stuck > 2 s in S1–S4, overlap < 1 % of fighter-ticks, p99 tick < 2 ms for 50 fighters on the
dev Mac, and it looks right in the replay viewer.

## 11. Combat

- Melee: reach from `WeaponDef`; a fighter in reach of an enemy is *engaged*. Attacks are actions with windup,
  strike and recovery ticks; the hit is resolved on the strike tick.
- Hit chance (‰) = base + attack − defence + modifiers: flank +, rear ++, shield wall frontal −, fatigue −,
  wounds −, morale state −. Exact values are design numbers — keep them in defs and tune in M3.
- Damage vs armour → HP loss and a wound roll: Light (small penalty), Heavy (big penalty, may leave the fight),
  Mortal (dies at end of battle unless treated — campaign later).
- Ranged in phase 1 only as hero abilities (e.g. throwing a spear); full archers later.

## 12. Morale (central system)

- Per-fighter morale 0..1000; squad cohesion modifies gains and losses.
- Inputs: recent casualties in the squad (sliding window), being attacked in flank or rear, fatigue, nearby
  friendly squads routing, enemy fear sources (`Fear` from the sorcerer), hero leadership (aura radius or
  attached to the squad), sworn brothers in the same squad (+) / rivals (−) — the last two from phase 2 data.
- States: `Steady → Shaken → Wavering → Routing`, and `Rallying` back. Thresholds with hysteresis.
- Routing fighters flee towards their map edge and ignore orders; they can rally near a hero when not engaged.
  A squad *breaks* when more than X % of it routs.
- Morale changes must be explainable in the UI: every delta carries a reason code for the tooltip and the log.

## 13. Heroes

A hero is a fighter with a `HeroData` part: abilities with cooldowns (ticks), leadership radius and strength,
optional attachment to a squad (leads it → cohesion and morale bonus). Direct control: move, attack target,
use ability. Phase 1: 2 heroes × 2–3 abilities (e.g. battle cry/rally, shield bash, spear throw).

## 14. Enemy AI

Utility + FSM, no neural nets, inside the sim and deterministic:
- **Side AI** (every ~1 s): score objectives per squad (attack weakest/nearest, flank, hold position, regroup).
- **Squad AI** FSM: `Idle, Advance, Engage, Hold, Regroup, Rout`; issues internal orders, not commands.
- **Fighter level**: target choice within the squad's engagement.
- Evaluation is staggered by id; ties broken by id; randomness only through the `ai` RNG stream.

## 15. RNG, hashing, snapshots, replays

- `SimRng`: PCG32. Separate streams per purpose (`combat`, `morale`, `ai`, `misc`), derived from the battle seed
  with SplitMix64, so a change in AI randomness doesn't shift combat rolls in A/B runs. RNG state is part of
  `BattleState`.
- `StateHasher`: FNV-1a 64 over fields in a canonical order, with per-section sub-hashes so a desync can be
  localised to a section. Sections now: `Core` (tick, seed, id counter, scenario fingerprint), `Rng`, `Squads` (id,
  side, name, formation id, anchor, facing, order kind/target/facing, velocity, spin, members), `Fighters` (id, def id,
  side, alive, position, velocity, facing, squad, slot); morale and others join when that state appears. Strings are
  hashed ordinally (length + UTF-16 units). Velocities and spin are recomputed every tick (stages 3 and 5) but hashed
  anyway: anything that reads them earlier in a tick (AI in M4, the view) sees last tick's values, so they are state, and
  they help localise a desync. `HashCoverageTests` checks that each field moves its own section.
- `Snapshot`: deep copy of `BattleState` — used for the view double buffer, restore-and-continue tests and
  later saves. Snapshot → restore → continue must produce the same hashes as an uninterrupted run.
- Replay file (JSON): `{ format, simVersion, scenario, seed, commands: [...], checkpoints: [{tick, hash}] }`.
  Golden replays live in `sim/tests/Golden/`; a rule change that alters outcomes regenerates them on purpose.
  Until replay files exist, the golden checks are `GoldenFinalHash` in `DeterminismTests` (seed 42, 300 ticks; its
  squads face between sine-table entries), `GoldenMarchHash` (a scripted march with an about-turn and an arrival
  facing, 400 ticks) and `ContentGoldenTests` (a test-owned JSON fixture: fingerprint and spawned state). `HashCoverageTests` checks that every state field moves its own hash section.

## 16. Events and the view contract

- Systems append `SimEvent`s (hit landed, fighter died, wound, morale state changed, command rejected, ability
  used, squad broke, battle ended) to the per-tick buffer; the view consumes them for VFX, sound, UI and log.
- The view reads `BattleState` through its public surface, which is read-only for state: fighter setters, the RNG
  streams and id allocation are `internal` (tests get them through `InternalsVisibleTo`). The view never calls
  systems and never writes state.
- Events hold one tick only: a view stepping several times per frame reads them after every `Step()`.
- If a command or system throws, the simulation is *faulted* (`IsFaulted`) and refuses further steps — its state no
  longer matches any replay. Restore a snapshot or restart.
- Input → `ICommand` → `CommandQueue`. Selection, camera and UI state are view-only.

## 17. Tools

- `Knez.Sim.Cli` (`sim/tools/Knez.Sim.Cli`). **Now (M1 step 2):** `run <scenario> [--seed N] [--ticks N]
  [--frames FILE] [--json]` loads the scenario and its defs, simulates it and prints the forces per side, the tick
  count and the final hash (JSON: `scenario`, `squads`, `fighters`, `finalHash` + section hashes, `frames`); exit codes
  0 ok, 1 simulation error, 2 bad arguments, bad content, or a frames file that can't be written (refused up front,
  including the scenario's own path, or failing mid-run). Recording is read-only: the final hash is the same with or
  without `--frames` (tested tick by tick on a moving battle). The list below is the target contract, built through M1:
  - `run <scenario> --seed N [--ticks N] [--out replay.json] [--frames frames.json]` → summary (winner, duration,
    casualties, morale breaks, final hash);
  - `batch <scenario> --seeds 1-200 [--json]` → aggregate metrics and win rate;
  - `bench <scenario> [--fighters N]` → tick time p50/p99;
  - `replay verify <replay.json>` → re-simulate and compare checkpoints.
- Frames file (`--frames`, written by `FrameRecorder`; this is the only copy of the spec), format `knez-frames` v1:
  ```
  { "format": "knez-frames", "version": 1, "scenario", "description", "seed" (string), "ticksPerSecond",
    "map": { "width", "height" },
    "squads":   [ { "id", "name", "side", "formation" } ],
    "fighters": [ { "id", "squad", "slot", "side", "def", "radius" } ],
    "frames":   [ { "tick", "hash", "squads": [[id, anchorX, anchorY, facing°, ordered 1|0, targetX, targetY, targetFacing°]],
                    "fighters": [[id, x, y, facing°, alive 1|0]], "events": [{ "type", … }] } ],
    "faulted":  { "tick", "error" } }   // only when the simulation threw
  ```
  - One frame for tick 0, then exactly one after every tick (players assume one frame per tick); metres to 0.1 mm,
    degrees (0 = east, counter-clockwise) to 0.01°; rows in id order; `hash` is the state's total hash.
  - `seed` is a string because JavaScript numbers lose 64-bit values above 2^53.
  - `events` holds that tick's events. `type` is the C# class name (renaming an event class changes the format);
    fields are written for the types in `FrameRecorder.DescribedEventTypes` — today `CommandRejected`: `command`,
    `side`, `reason`. A test fails when a `SimEvent` subclass has no entry there.
  - If the simulation throws, the file is still closed as valid JSON: the frames up to the last finished tick plus
    `faulted` (the tick that threw and the exception); the CLI exits 1.
  - Static tables describe the battle at tick 0. **Compatibility:** adding row columns, keys or event types keeps
    version 1 — readers ignore what they don't know; a version bump is only for breaking changes. When slots start
    changing (step 7), fighter rows gain `[squad, slot]` columns and the viewer prefers them over the static table.
- `tools/replay-viewer/`: static page (HTML + one script, no dependencies) that plays a frames file: map with a 1 m /
  5 m grid, fighters as circles in squad colours with a facing tick, squad anchors with facing arrows and names, dead
  fighters greyed, events of the current tick, hover details; play/pause/step/scrub, speed ×0.25–×4, zoom/pan,
  interpolation between ticks, a red note for a faulted run. URL:
  `?frames=<url>&tick=N&speed=2&play=1&labels=0&anchors=0&grid=0&ids=1`; a file can also be picked or dropped.
  `window.knezViewer` (`tick`, `seekTick(n)` — renders at once, `loaded`, `error`) lets agents drive it from the
  console. Pausing always lands on a whole tick. Move orders are drawn as a dashed line from the anchor to a ring at
  the target with a tick for the arrival facing; morale bars and event markers on the map arrive with their mechanics; at ×2/×4 the events panel shows only the tick on screen (collect skipped ticks when markers come).

## 18. Testing

| Kind | Where | Gate |
|---|---|---|
| Unit (per system/rule) | `sim/tests/.../<System>Tests.cs` | every change |
| Scenario (outcomes within ranges over N seeds) | `[Category("Scenario")]` | every change touching that mechanic |
| Determinism (two runs, snapshot/restore, golden replays) | `[Category("Determinism")]` | every sim change, CI |
| Purity | `tools/check_sim_purity.py` | hook + CI |
| Performance | `Knez.Sim.Cli bench` | tracked per milestone, not gating yet |
| Cross-runtime | same golden replays in a Unity EditMode test | from M2 |

## 19. Open questions

- Grid cell size (0.5 m vs 1 m) and fighter radius — decide from the M1 bench.
- Steering vs ORCA-like avoidance — decide from the M1 bench.
- Exact formation set for phase 1 (proposal: Line, ShieldWall, Column).
- Morale numbers, thresholds and rout rules — tune in M3 with playtests.
- Hero ability list for the prototype.
