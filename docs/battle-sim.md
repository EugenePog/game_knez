# Battle simulation — technical design

Status: **draft v0.11** (2026-11-25, M3 step 9: a fighter is a body, an experience level and a kit, §7; step 7: the
balance reference measured and pinned, §11; step 6: the game design's terms and the contracts for M3 steps 10–11, M4
and M5). Owner: Eugene. Scope: phase 1 skirmish; the same sim later drives the 3D view
and campaign battles. Code names follow the glossary in [AGENTS.md](../AGENTS.md#glossary-russian-design-term--code-name);
design decisions D-NNN are in [docs/design/decisions.md](design/decisions.md).

When code and this document disagree, fix one of them in the same change. Parts marked **Planned** describe what a
later roadmap step builds — the contract that step starts from; their numbers are proposals until the user agrees.

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
  turn-based mode advances the sim until the next *decision point* (a Retainer's action completes).

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
  - `Map`: the scenario's map (walls). Its route grid (`Movement/NavGrid.cs`: 0.5 m cells, decided 2026-11-21, each
    with its clearance — the exact distance from its centre to the nearest wall or map edge) is derived from it, so it
    lives in `TickContext`, built when the simulation is created or restored (maps with walls), never hashed or cloned. Height, cover and line of sight come with combat;
  - `Sides` (player, enemy; later more — for now just `SideId` ints on squads and fighters), `Squads`, `Fighters`
    (Retainers — дружинники, the named heroes — are fighters with a `Retainer` part, §13; D-015 renamed this
    spec's `HeroData`);
  - spatial index for neighbour queries (rebuilt each tick, deterministic order) — *not built yet*: phase 1 battles have
    at most ~60 fighters, so crowding checks all pairs in id order (~0.15 ms/tick for 35); add the index if the M1 bench
    asks for it;
  - per-tick `Events` buffer;
  - `Scenario`: an immutable reference to the `ScenarioDef` (map, starting squads, the defs they use), shared (not
    copied) by `Clone`; its `Fingerprint` (FNV-1a over everything battle-relevant, not the description) is hashed in the
    `Core` section. Fighters and squads reference their defs directly; defs are immutable (a test enforces readonly
    fields only).
- Scratch buffers live in `TickContext` (one per simulation), never in `BattleState` or system fields: they are not
  cloned or hashed, so two restored simulations can't share one.
- Storage: lists sorted by `EntityId`; lookup by binary search (an id → index map only if profiling asks for it).
  Systems iterate `state.FighterList` with `for` loops. Fallen fighters are marked dead and stay in the list (for
  the view); they leave their squad at the end of the tick.
- Fighter data groups: transform (position, velocity, facing), body (wounds — no hit points, §11; fatigue only if
  M6 tuning asks for it), morale, combat (current action, cooldowns, target), squad membership and slot, and the
  defs he is made of — body, experience and kit (M3 step 9, §7). Now: id, body, experience, kit, side,
  position, velocity (m per tick), facing, squad id, slot index, alive, `PassingThrough` (step 4), `TurnLag` (soft wheel, 2026-11-23: how far behind his slot the squad's turn left him), `Target` (M3 step 2:
  the enemy he is in melee with, or none), `Action` + `ActionTicks` (M3 step 3: idle, windup or recovery, ticks left), `Wound` (M3 step 4: none, light,
  heavy, down; down = fallen, alive false), `Morale`, `MoraleState` (step 5). A fighter who falls leaves his squad (squad id
  none, slot −1) at the end of the tick and stays in the list, dead, for the view.
- `Squad`: id, side, name, formation def, anchor (centre of the front rank), facing, order (`SquadOrder`: kind,
  target, arrival facing, whether that facing is "the way it marched"), route (`Path`: waypoints, the last one the
  target; `PathFiles`: what it does at each — regroup into k files, widen, or nothing; `Frontage`: files across its front,
  set when it forms up; `Reforming`: changing formation, at reduced pace; `PathIndex`: the one it heads
  for; `PathRequestTick`: waiting for a route since that tick, or −1), `Files` (narrowed to this many files for a gap,
  0 = its own width), `RegroupSince` (waiting for its men to regroup since that tick, or −1), `Withdrawing` (M3 step 2:
  stepping back out of melee, facing the enemy, until clear), `StartCount`, `Broken` (step 5), velocity and spin (how far the anchor moved and the front turned this tick — they give each
  slot's own motion), member ids (sorted). Build the
  starting state with `BattleSetup.Create(scenario, seed)`: squads in scenario order, each followed by its fighters in
  slot order, every fighter on its slot facing with its squad.

## 7. Content definitions

Immutable classes in `Knez.Sim.Defs`, referenced directly by state (hashed by their string id; interning to ints only
if profiling asks): `BodyDef` (radius, speeds, turn rate), `WeaponDef` (reach, timings, wounds, penetration, defence),
`ProtectionDef` (shields, body armour, head covers), `ExperienceDef`, `KitDef` (weapon, shield, body armour, head cover;
M3 step 9), `CombatDef` (the combat rules),
`FormationDef` (slot layout, spacing, stance modifiers), trait defs (`retainers.json`, M3 step 11, §13), `MapDef`, `ScenarioDef` (map, sides, squads,
spawn positions, victory conditions, seed defaults). Balance numbers live here, never as constants in systems. Each
def gets the fields its systems need when they arrive; M1 step 1 has:

| Def | Fields |
|---|---|
| `BodyDef` (`bodies.json`, array `bodies`; M3 step 9, was `FighterDef`) | `id`, `radius` (m), `walkSpeed`, `runSpeed` (m/s, at most 8, and under radius + 5 cm per tick so nobody steps through a 10 cm wall), `turnRate` (°/s) — the body only. One body, `man`, until riders come |
| `CombatDef` (`combat.json`, M3 step 1, numbers in §11) | `weapons[]`: `id`, `reach` (m, gap between bodies), `windup`, `recovery` (s, whole ticks), `wounds` {`light`, `heavy`, `down`} (‰, sum 1000), `penetration` (‰ of the defender's body armour and head cover its blows ignore; shields aren't affected), `defence` (‰ off blows at the man who carries it); `shields[]`, `bodyArmour[]`, `headCover[]`: `id`, `protection` (‰); `experience[]` (lowest first): `id`, `defence` (‰), `morale` (start, 0..1000); `kits[]`: `id`, `weapon`, `shield`, `bodyArmour`, `headCover` (ids from these lists, all required); `hitChance` {weapon: {experience level: ‰}} — every cell required, keyed by id; `hitLimits` {`min`, `max`}; `position` {`frontArc`, `rearArc` (°), `flank`, `rear`, `shieldFromFlank`, `shieldFromRear`, `shieldWall` (‰)}; `strikeArc` (°); `wounded` {`lightPenalty`, `heavyPenalty` (‰), `lightPace`, `heavyPace` (fractions)}. Catalogues keep file order; at most one def file holds it; code-built scenarios use `CombatDef.Standard`, which a test keeps equal to the file (by fingerprint). A scenario is refused when a squad's kit or experience level isn't its combat rules' own |
| `FormationDef` | `id`, `spacing` (m between neighbours), `rankSpacing` (m between ranks), `ranks` *or* `width`, `turnRate` (°/s), `holdWeight` (how much harder men holding their ground are to push; moving men weigh 1), `allyPassSpeed` (fraction of walking speed at which allies squeeze through this formation while it holds), optional `shieldWall` (M3: frontal blows at men with shields get through less) |
| `MovementDef` (`movement.json`, decided 2026-11-21: tunables in content, not code) | `pivotTolerance` (° off the heading a squad still marches at, 30), `slotCatchUpTime` (s, 0.5), `personalSpace` (m, 0.1), `separationTime` (s, 0.25), `holdingReach` (m, 1.5), `formedUpReach` (m, 0.3), `collisionPasses` (6), `allyPartingDepth` (m, 0.25), `pathBudget` (search nodes per tick, 4 000), `regroupLead` (m, 1), `regroupMaxWait` (s, 5), `regroupCost` (route metres per metre of extra depth, 1), `reformPace` (fraction of the pace while changing formation, 0.5), `stepBackPace` (fraction of the pace when stepping back onto a target just behind, and when withdrawing from melee, 0.5), `withdrawClearance` (m beyond each man's weapon reach a withdrawing squad keeps facing the enemy, 3; decided 2026-11-22), `closeInRange` (m, edge to edge: in a squad standing in melee a man not yet fighting steps toward an enemy this near, 3; asked for 2026-11-23, the number a proposal; 0 = off), `turnLagTime` (s: the soft wheel — the lag a turn leaves closes over this, 4; asked for 2026-11-23 as 3–5 s; 0 = men swing rigidly with their slots). Times are rounded to the nearest whole tick (0.05 s). At most one def file holds it; scenarios built in code use `MovementDef.Standard`, which a test keeps equal to the file. A scenario is refused when `personalSpace` doesn't fit between two of its men side by side or rank behind rank |
| `MoraleDef` (`morale.json`, M3 step 5, numbers in §12) | thresholds `shaken`, `wavering`, `routing`, `hysteresis`; `lossWeight` (× 1000 / start size per fallen squadmate); costs `ownLightWound`, `ownHeavyWound`, `mateHeavyWound`, `flankBlow`, `rearBlow`, `mateFlankBlow`, `mateRearBlow`, `mateRouts`, `allySquadBroke`, `enemySquadBroke` (a gain), `nearby` (m); `recoveryPerSecond`, `safeDistance` (m), `rallyAt`; `breakShare`, `reformShare` (‰); `waveringHitPenalty` (‰). At most one def file holds it; code uses `MoraleDef.Standard`, kept equal by fingerprint. A scenario is refused when a squad's experience starts below `routing` |
| `MapDef` | `width`, `height` (m, at most 250: 500 route cells), optional `walls[]` (at most 64): `from` `[x, y]`, `to` `[x, y]` (opposite corners of an axis-aligned rectangle, ≥ 0.1 m thick, on the map) |
| `ScenarioDef` | `id`, `description`, `map`, `squads[]`: `name`, `side`, `body`, `men[]` (`count`, `experience`, `kit`; M3 step 9), `formation`, `position` `[x, y]`, `facing` (degrees); optional `script[]`: `tick`, `order`, `squad` (name), then for `"move"` `target` `[x, y]` and optional `facing`, for `"formation"` `formation` (an id from the catalogue). Built from content it also carries the whole formation catalogue (`Formations`) |

Agreed values (2026-11-20): walk 1.4 m/s, run 3.5 m/s; squad turn rate column 90°/s, line 45°/s, shield wall 30°/s;
a standing squad gives way less than one on the move; (2026-11-21) a holding squad opens its ranks to its own side,
allies squeezing through at 1/3 of walking speed (`allyPassSpeed` 0.333333333). The M1 bench gave no
reason to change radius 0.3 m (the body's collision size, kept — decided 2026-11-21), fighter turn 360°/s, the
formation spacings or `holdWeight` 3 (a holding man takes a quarter of a push against a moving one); they stay
tunable in content. Fighting distance is separate from the body and from personal space (decided 2026-11-21): two
men are in contact when their bodies are within 0.5 m — in M1 it only defines "engaged" for the bench; it becomes the
default weapon reach in M3.

The **scenario script** is orders the scenario gives by itself at fixed ticks (benchmarks, tests, set pieces). In
tick order; each entry goes through the same validation as a player's `MoveSquad` at the start of its tick, before
queued commands, issued as the squad's own side, but is not written to the command log — the scenario (and its
fingerprint) already holds it. It exists because the CLI takes no commands and the S2–S4 benches need timed orders.
Sanity bounds also cover speeds (0.1..8 m/s) and turn rates (0..3600 °/s).

A scenario uses one def object per id (state hashes name defs by id). Since M1 step 7 it carries the formation
catalogue (`ScenarioDef.Formations`: every formation in the content's def files, or the spawns' own when built in code;
each spawn's formation must be the catalogue's def of its id), fingerprints all of it, and `SetFormation` resolves ids
against it — so a formation no spawn uses is still hashed. The kit catalogue is carried and fingerprinted the same
way, inside the combat rules (M3 step 9), so a later rule that changes a man's kit resolves it there. Sanity bounds (not balance): side 0..7, squad size 1..64, ranks/width 0..64.

**A fighter is a body, an experience level and a kit (M3 step 9, built 2026-11-25; D-022, D-018; ADR-0005).** There
are no fighter types: what a man fights with and how well he fights are set apart, and one squad may mix both. The
old `warrior`/`spearman`/`levy`/`veteran` fighter types, their default kit and the `warriorTypes` axis are gone.

- **Bodies** (`bodies.json`): size, speeds, turn rate. One, `man`.
- **Kits** (`combat.json` `kits[]`), named by what they hold — a militiaman and a Retainer may share one. Decided by
  the user 2026-11-25: seven, every one with a round shield.

  | kit | weapon | body armour | head cover | for |
  |---|---|---|---|---|
  | `axe-shield` | axe | none | none | militia |
  | `spear-shield` | spear | none | none | militia |
  | `axe-shield-padded` | axe | padded | cap | militia, better kit |
  | `spear-shield-padded` | spear | padded | cap | militia, better kit |
  | `sword-shield-padded` | sword | padded | cap | raiders, warriors |
  | `sword-shield-mail` | sword | mail | helm | Retainers |
  | `axe-shield-mail` | axe | mail | helm | Retainers |

- **Weapons** gain `penetration` (decided by the user 2026-11-25: axe 300, spear 200, seax 100, sword 0 — the axe is
  the armour-breaker) and `defence` (the spear's 30, "keeps the enemy at reach", which was the spearman warrior
  type's). Shields aren't affected by penetration.
- **Experience** is unchanged (`id`, `defence`, `morale`; the hit matrix weapon × experience is the weapon buff D-022
  asks for). Whether experience also shortens recovery: **not yet** (the user, 2026-11-25) — measure "one level ahead"
  against D-026's 65–70 % first (M6). Step 10 adds `retainerOnly` (true for professional and elite, D-019; a squad man at
  such a level is an error at `squads[i].men[j].experience`) and `leadership` (§13).
- **Squads** name one `body` for their men (a Retainer who joins keeps his own — a squad marches at its slowest man's
  pace anyway) and list `men[]`: `count`, `experience`, `kit`. The squad's size is the sum, 1..64. Slots are filled in
  `men` order, front rank first, so listing shields first and spears last puts the spears behind. Errors are located
  at the group: `squads[0].men[1].kit: unknown kit …`. A file with the old `fighters` or a squad with `fighter` gets an
  error saying where the parts went.
- **Hit chance** (§11): the defender's armour counts as `(bodyArmour + headCover) × (1000 − p) / 1000`, truncated, where
  `p` is the attacker's weapon's penetration; his own weapon's `defence` takes the warrior type's place.
- **State:** `Fighter` holds `Body`, `Experience` and `Kit`; the `Fighters` hash section hashes the three ids where it
  hashed the def id. Frames: the static fighter table's `def` holds the body id, and `experience` and `kit` are added
  keys (version 1 stays); the replay viewer's hover shows them. The Unity HUD lists each squad's groups ("3 new: spear
  · round shield · no armour · bare head").
- **Converted 1:1:** `warrior` → experienced + `sword-shield-padded`; `spearman` → experienced + `spear-shield-padded`;
  `levy` → new + `axe-shield`; `veteran` → elite + `sword-shield-mail`. With penetration 0 every content scenario
  played exactly as before — every seed of 60 for a minute: end, winner, blows and every man's position, facing,
  morale and wound — then penetration moved only `m3-spears`, `m3-rally`, `m3-veterans` and the sandbox (§11 table).
- **Open, later:** should a man's kit choose his rank when slots are reassigned (spears behind shields)? Today's
  nearest-man reassignment ignores kit.

Files: `content/defs/*.json` (objects with `bodies` / `formations` arrays and the `movement`, `combat`, `morale`
rules, ids unique across files) and
`content/scenarios/**/*.json` (one scenario each, `id` = file name). `Knez.Sim.Content` reads them strictly
(ADR-0004): unknown or missing members, wrong types, unknown references and invalid values are errors located as
`file:line:column: json.path: message`. The CLI finds the defs in the nearest `defs/` folder above a scenario.

Formation slots (`FormationLayout`): numbered front rank first, left to right; the anchor is the centre of the front
rank; an incomplete rank is the rearmost and is centred. Width = `width`, or ⌈count / `ranks`⌉.

## 8. Commands

- `ICommand` with `SideId` and a type-specific payload; the tick and enqueue order live in `ScheduledCommand`.
  Phase 1 commands — built: `MoveSquad(squad, target, facing?)`, `SetFormation(squad, formation)`. **Planned:**
  - M4 step 1: `AttackSquad(squad, enemy squad)` (go at it and fight it — today a move order stops at its point),
    `SetStance(squad, Hold|Advance|Pursue)`;
  - M3 step 10 (§13): `AttachRetainer(retainer, squad)`, `DetachRetainer(retainer)`, `MoveRetainer(retainer, target,
    facing?)`, `RetainerAttack(retainer, enemy fighter)` — a Retainer standing alone is under direct control (D-014).
    Rejections (proposal): `unknown-entity`, `wrong-side` (also attaching to the other side's squad), `not-alive`
    (down or fled), `routing`, `squad-broken`, `squad-full` (64 men), `in-squad` / `not-in-squad` (move or detach
    orders that don't fit where he stands);
  - M3 step 11 (§13): `UseAbility(retainer)` — every trait ability acts round him or on his squad, so it takes no
    target; a squad ability used by a Retainer standing alone is rejected as `not-in-squad`, and one still cooling down
    as `cooling-down` (proposal);
  - M5 (§12, §13): `StepForward(squad)`, `Demand(side, terms)` (phase 1: only "leave the field"),
    `Challenge(retainer, enemy, terms)`.
  The AI (§14) issues the same orders inside the sim, not as commands.
- A command enqueued while the state is at tick T is applied at the start of tick T+1. Ordering within a tick:
  enqueue sequence number.
- At its tick a command is first checked with `Validate(state)`, which gets no `TickContext` (no random draws, no
  events) and returns a `RejectReason`. Only a valid command is `Apply`-ed. An invalid one emits `CommandRejected`
  with the reason code (`unknown-entity`, `wrong-side`, … — stable strings for the UI) and changes nothing. Each
  command checks the issuing side's ownership itself.
- The command log + scenario + seed is the replay.
- **Session files (Unity).** Until replay files exist (§15), the Unity view keeps the log as a scenario:
  `SessionRecorder` (Knez.Sim.Content) notes every command the player enqueues, keeps those the sim applied (a
  rejected one changed nothing) and, when Play stops, `BattleRunner` writes the scenario with them added to its
  `script` to `artifacts/sessions/<scenario>-seed<N>-<time>.json`. A scenario's own entries of a tick stay ahead of
  the recorded ones, as in the sim. Positions are written with up to 9 decimals and facings in degrees chosen so they
  read back to the exact raw values, so `run <session> --seed N` replays the battle: same fighters, squads and RNG
  (the core hash differs, because the scenario's id and script are part of it). The CLI finds `content/defs/` above
  a scenario outside `content/`.
- `SetFormation(side, squad, formation)` (M1 step 7): rejected as `unknown-entity`, `wrong-side`, `not-alive` (no
  members left) or `unknown-formation` (not in the scenario's catalogue). Applying it re-forms the squad on the move (§10).
- `MoveSquad(side, squad, target, facing?)` (M1 step 3): rejected as `unknown-entity`, `wrong-side`, `not-alive` (no
  living member) or `off-map` (target outside the map). Applying it replaces the squad's order; without a facing the
  squad will end facing the direction from its anchor to the target (its current facing if the target is the anchor).
  The order lasts until the squad is **formed up** at the target: anchor there, facing reached, and every living member
  within 30 cm of his slot (decided 2026-11-21). A squad blocked short of its slots keeps its order. A target that
  would put part of the formation (at the arrival facing) off the map is moved inward until it all fits — otherwise
  slots past an edge are pulled onto the same spot and the squad can never form up. A target inside a wall then moves
  to the nearest open ground (M1 step 5, `Terrain.Constrain`, §10). Since step 6 the squad follows a route (§10): straight
  when the line has room for its frontage, otherwise planned by `PathSystem` this tick, marching from the next. A
  target no route reaches becomes the near side of whatever is in the way, and without an ordered facing the squad ends
  facing along the route's last leg (decided 2026-11-21).

## 9. Tick pipeline (fixed order)

| # | System | Does |
|---|---|---|
| 0 | `BattleSimulation.Step()` | advance tick counter, clear events (not a system) |
| 1 | `CommandSystem` | apply this tick's commands |
| 2 | `AiSystem` | side → squad → fighter decisions; squads evaluated staggered (every 5 ticks, offset by id) |
| 3 | `OrderSystem` | squad orders → anchor goal, facing, formation slots, slot assignment |
| 4 | `PathSystem` | A* for squads waiting for a route, first come first served (request tick, then id), 4 000 search nodes per tick; Retainers standing alone later (M3 step 10) |
| 5 | `SteeringSystem` | turn lag (soft wheel, §10), then desired velocity: slot seek/arrive, separation, obstacle avoidance; routers flee; closing in (§11) |
| 6 | `MovementSystem` | integrate, resolve collisions (circle–circle, circle–wall), update facing (engaged men face their target, M3) |
| 7 | `TargetingSystem` | engagement and target choice in reach; tie-break by distance, then id |
| 8 | `CombatSystem` | advance actions (windup → strike → recovery), roll hits with `SimRng` |
| 9 | `DamageSystem` | damage, wounds, deaths |
| 10 | `MoraleSystem` | morale deltas, state transitions, rout and rally |
| 11 | `FatigueSystem` | empty: fatigue is built only if M6 tuning asks for it (the design doesn't; roadmap open question 2) |
| 12 | `CleanupSystem` | the fallen and the fled leave their squads and ranks close (step 7); the victory check (M3 step 5) |
| 13 | `BattleSimulation.ComputeHash()` | state hash, called by tests and tools after a step (not a system) |

Systems 1–12 exist from M0 in `BattleSimulation.CreateDefaultSystems()`; all but `CommandSystem` are empty until
their milestone. Scratch buffers live in `TickContext`, never in system fields.

## 10. Movement and formations — the main risk (M1)

- A squad has an **anchor** (position + facing) that follows its route; the formation defines **slots**
  relative to the anchor. Fighters steer to their slots.
- **Routes (step 6, decided 2026-11-21).** `MoveSquad` gives the squad its route at once when it stands in its own
  width and the straight line to the target has room for the formation all the way (every 25 cm sample at least half
  the frontage plus a body radius, less 23 cm). Otherwise the squad waits (`PathRequestTick`) and `PathSystem` runs A*
  over the 0.5 m grid (`Movement/PathPlanner.cs`): 8-connected, octile costs (10 per cell, 14 diagonally). A cell a man
  fits in (clearance ≥ the widest member's radius) is passable. A diagonal step needs both side cells passable and
  enough room at the corner they share that no post there can sit on the step. The first leg starts at the anchor and
  the last ends at the target, anywhere in their cells, so those two are checked exactly against the walls
  (`Terrain.CrossesWall`): a thin wall can run through a cell.
- **Regrouping for narrow gaps (decided 2026-11-21, replacing a 3× cost for narrow ground).** Each cell knows its
  corridor (`NavGrid.Corridor`): its narrowest cross-section, wall to wall (or map edge), over four directions (E–W,
  N–S and both diagonals; rays from the cell centre, capped at 32 m). A gap is narrow whichever way it is crossed, while
  a single wall beside or behind a squad never makes ground narrow, since the open side keeps every cross-section wide.
  Where the corridor is narrower than the formation's frontage, the squad narrows to as many files as fit. The route
  search charges only the extra waiting: each metre the squad grows deeper (its rear goes through that much later)
  costs `regroupCost` metres of route (1 by default). So it squeezes through a gap when a detour is longer than the
  depth it gains, and goes round otherwise. On the route, `regroupLead` (1 m) before each narrow stretch the squad stops
  and regroups (`Squad.Files`): each old rank splits left to right into bands as even as possible (the odd men go to the bands
  round-robin across ranks, so the new files differ by one man at most), and within a new file men stand by their old rank,
  inner files first — the middle of the front stays in front and the flanks fall in behind
  (`FormationLayout.SlotOffset(…, files)`, `NarrowedRanks`). It turns to face the gap while the men fall in, waits until
  formed up (at most `regroupMaxWait`, 5 s), marches through and widens back (no wait) once its narrowed depth plus
  `regroupLead` is past the stretch; stretches too close to widen between count as one, narrowed to the tightest. It
  widens at the target only if its rear is through by then and the target has room for its frontage at the arrival
  facing; otherwise it ends narrowed. A squad given a new order while narrowed counts its own spot as narrow ground, so
  it widens only once its rear is clear. Regroup and widen waypoints (`Squad.PathFiles`) are never straightened away.
  Slot reassignment after losses is still step 7.
- **Route shape and limits.** The cell chain is split into straight runs, and each run is shifted sideways so the
  formation (in the width it marches there) keeps clear of walls on both sides with up to 25 cm to spare — by as little
  as that takes, the first run preferring the line the squad already stands on; in a gap that leaves no spare room
  that is its middle line, and where no shift fits, the middle of the run's tightest cross-section. Shifted runs meet
  where their lines cross; a waypoint that would then turn the squad back on itself next to a short leg is dropped
  (unless marked or the shortcut would cross a wall), before and after straightening. If any shifted leg would cross a wall, the whole route is laid unshifted (the legs the search
  walked). Waypoints: the anchor, the corners, the first and last cell, the marked cells and the target; unshifted
  corners in open ground are then straightened where the straight stretch has about as much room as the cells it
  skips (25 cm samples, no less than their room minus 23 cm, capped at the frontage, never under 25 cm, so no stretch
  hops a wall between samples). A target no route reaches (walled in, or across a wall with no gap) becomes the near
  side of whatever is in the way (decided 2026-11-21): walking the straight line back from the target toward the anchor,
  the first cell the squad can reach and stand in — not the geometrically nearest reachable spot, which may lie round
  the far side. The order's target becomes that spot; a target in the anchor's own cell but behind a thin wall leaves the
  squad where it is. Ties everywhere break by fixed neighbour order and cell index. Searches run to the end within the
  tick they start; once `pathBudget` (4 000) nodes are spent (laying the route out isn't counted) the rest wait for the
  next tick. Measured: 0.22 ms for a 4 400-node search on a 60 × 40 m map, 1.9 ms for a 23 000-node flood of a
  120 × 100 m one — splitting searches over ticks waits for bigger maps. The grid (clearance, a corner value and 8 rays per cell) is built
  when the simulation is created or restored, only for maps with walls: about 0.47 s in Release (5.4 s in Debug) for a
  250 × 250 m map with 64 walls, negligible at phase-1 sizes; restoring could reuse a built grid later. Known (left for M2 playtests): on a short diagonal leg
  into a gap's mouth one man's slot can graze the wall's corner for a few ticks (sideways room is sampled at cell
  centres); no re-planning yet, so a squad shoved off its route marches on to the next waypoint.
- **Move order (step 3; along the route since step 6; turning since step 7).** `OrderSystem` marches the anchor at the
  walk speed of the slowest living member toward the next waypoint, finishing turns of up to 30° (`pivotTolerance`)
  about the anchor while marching. A sharper turn (decided 2026-11-21):
  - **more than a quarter turn: face about.** Every man turns round and the rear rank becomes the front: the anchor
    moves to where the rear rank's centre was and each new place goes to the nearest man (`SlotAssignment.Nearest`), so
    nobody walks far and a column's tail no longer swings round. It is skipped when the waypoint would still lie behind
    the squad afterwards (closer than the squad is deep) — then it pivots in place as before; an unmarked waypoint that
    close behind is passed over instead.
  - **30°–90°: wheel round the inner flank.** The man at the inner end of the front rank marks time and the outer flank
    swings round, no faster than the formation's turn rate nor than the outer flank can run; the anchor moves on the arc.
    At a route corner the wheel starts half·tan(θ/2) before it (half = half the front), so the arc is tangent to both legs
    and the squad comes out on the next leg; regroup and widen points are reached first, not cut. A leg shorter than the
    front (the wheel would overshoot it) is turned in place instead.
  Within 1 cm of the target the anchor stops there and turns to the order's facing about the anchor. When the order's
  facing is more than a quarter turn from the last leg, the front rank still lands on the ordered point (decided
  2026-11-21): the route runs one squad-depth past the target (where the ground allows), the squad faces about there,
  and its new front stands on the point; failing that it faces about at the target and steps back onto it. A target
  behind the squad and closer than its depth: it faces about and steps back (`stepBackPace`, half pace), never pivots.
  Wheels and face-abouts happen only on clear ground — the anchor after the turn clear of walls, its arc not crossing
  one, and the way on open — otherwise the squad pivots in place as before. The order is cleared once every living member stands within 30 cm of his slot (formed up; `formedUpReach` in
  `movement.json`), and the squad holds — after a turn that waits for the men to dress (soft wheel, below). Deep formations still wheel as one shape: a column's tail sweeps wide at a 90° route
  corner (follow-the-leader columns: not hit by the bench; left for M2 playtests).
- **Formation changes (step 7, decided 2026-11-21).** `SetFormation` switches the squad to another formation of the
  catalogue: its frontage is set for its living men, each new slot (front first) goes to the nearest man, and it keeps
  its order — a move order is re-planned for the new shape — marching at `reformPace` (half) until formed up, then at
  full pace. Narrowing for a gap (`Files`) is kept when it is still narrower than the new front.
- **Losses (step 7, decided 2026-11-21: keep the front, thin the rear).** At the end of the tick (`CleanupSystem`) the
  fallen leave their squad and the ranks close from the back (`SlotAssignment.CloseRanks`): each gap, front rank
  first, is taken by the nearest man (sideways) of the nearest rank behind that still has men, whose place then becomes
  the gap, until the gaps are all in the rear rank, which the layout centres. The front keeps its `Frontage` until fewer
  men are left than its files. Ties go to the lower id.
- **Soft wheel (asked for 2026-11-23: "flanks and rear ranks turn like a brick").** Men are not carried round with
  their slots when the squad turns — wheeling on the march, pivoting, or turning in place to the order's facing. Each
  tick, before following, the part of a man's slot motion that comes from the squad's spin is added the other way round
  to his `Fighter.TurnLag`, so his target (slot + lag) stays where the swing left it. In a wheel round the inner
  flank (recognised by the anchor landing exactly where swinging it about the inner front end puts it) all the slot's
  motion is the swing: the pivot man marks time, the outer flank trails the most. Any other turn is about the anchor
  (pivoting in place, or the last 30° of a corner turned on the march), so there the inner flank leads as much as the
  outer trails. Then the lag shrinks by 3/T of itself (to ~5 % over T = `turnLagTime`, 4 s) but by at least 1 m per
  T, so a lag of a few metres ends in about T, and is dropped under 1 cm. He follows slot + lag as below, so the line
  turns ragged, then dresses over the next seconds. A man given a new slot (face-about, formation change, stepping up
  into a gap) drops his lag: the new slot was picked from where he stands. The lag pulls targets a little toward the
  pivot while turning (the line closes up by up to a third of its spacing); crowding keeps bodies apart. Running men, broken squads and men without a squad have no lag. Holding,
  stuck and slot-error measures use slot + lag (where he means to be); formed up uses the slot itself, so a move
  order ends only once the ranks have dressed. `turnLagTime` 0 restores the rigid turn (checked: with it, and the lag left out of the hash, the goldens recorded before were reproduced).
- **Following the slot.** `SteeringSystem`: velocity = the slot's own motion this tick (from the anchor's velocity
  and spin, plus the turn lag's change) + a tenth of the lag the fighter had before (a 10-tick time constant; 1 m of lag closes to 2 cm in ~37
  ticks), capped at run speed. On a straight march a member never leaves its slot; a displaced one runs back. When the
  squad stands still and a member is within 2 cm of its slot, it steps exactly onto it, so formations settle instead of
  creeping. `MovementSystem` applies the velocity and turns the fighter at its turn rate: toward the squad's front while
  in formation — also when shuffling sideways or swinging round with a turning squad, whose outer slots move faster than
  walking — and toward its movement only when it moves faster than walking pace *relative to its own slot* (running to
  catch up). Both systems share the slot motion (`Movement/SlotTracking.cs`). Every movement rule that shapes how the battle
  feels is content: `content/defs/movement.json` (`MovementDef`, §7) — tune it there and on the bench.
  `Movement/MovementRules.cs` keeps only numerical constants that keep the maths exact (1 cm arrival, 2 cm snap, 25 cm
  route samples and the 23 cm shortcut slack), as do `NavGrid` (0.5 m cells) and `Terrain` (3 push-out rounds).
- A `MoveSquad` target within the 1 cm arrival distance keeps the squad's facing (a sub-centimetre offset has no
  meaningful direction).
- **Crowding (step 4).** Every living fighter is solid to every other, allies included (agreed 2026-11-20: crossing
  squads jostle past each other) — with one exception, decided 2026-11-21: **allies open ranks.** A man holding his
  ground lets a man of *another squad of his side* who is *passing through on orders* go through him: that pair
  ignores each other (no separation, slide or collision), and while their bodies overlap the passing man's own stride
  is at most the holder formation's `allyPassSpeed` (1/3) of his walking pace (pushes from his own rank can still carry
  him a little faster; walking alongside without touching costs nothing). Only men on orders pass (decided
  2026-11-21), and only on purpose: `Fighter.PassingThrough` (hashed state) starts when an ordered man, within personal
  space of an allied holder, *means to walk into him* — his formation-following velocity this tick heads toward that
  holder — and lasts while his body still overlaps allied holders. So a line still on orders but shoved backwards into
  its reserve stays solid against it; a man shoved off his slot without orders is *loose* (neither holding nor ordered)
  and solid to everyone; men of one squad never pass through each other; squad-less men (Retainers standing alone, M3 step 10) don't pass yet. An
  order lasts until the squad is formed up, so an arriving squad keeps passing until its men stand on their slots. Men
  of different allied squads more than 25 cm inside each other (they were passing through until an order changed) are
  parted ~1.7 cm per pass, and steering stops shoving them. **Enemies stay solid**: they stack into each other, and
  fighting starts there (M3). Roles per fighter and tick: `Movement/CrowdRole` (Loose, Holding, Ordered). Squads march on: the anchor never waits for men who fall behind; they run to catch
  up. In a push the share goes by weight — a man *holding his ground* weighs his formation's `holdWeight`, anyone on
  the move weighs 1. He holds his ground only while his squad has no order and he stands within 1.5 m of his slot (slot + turn lag, §10);
  a man still trying to reach a distant slot is on the move (otherwise a squad whose anchor arrived while its men were
  blocked short of their slots would shove an enemy line at full weight and bulldoze it). Three mechanisms, all over
  all pairs in id order (`Movement/Crowding.cs`):
  - *separation* (steering): inside 10 cm of personal space a man eases away by a fifth of the intrusion per tick,
    the lighter man taking the larger share;
  - *slide* (steering, after the run-speed cap): the part of his velocity heading into a neighbour inside personal
    space is cut by that neighbour's weight share — half between equals (they jostle), three quarters for a mover
    against a holder (he mostly slides along the line). Without it, sustained pressure left 5–8 cm overlaps;
  - *collision relaxation* (movement): up to 6 passes push overlapping pairs apart by their overlap, shared by weight,
    stopping early once nothing overlaps (a push travels one link per pass against id order; 3 passes left pushes four
    men deep overlapping). Two bodies on exactly the same spot part along X, the lower id west. Both bodies of a pushed
    pair are put back inside the map and out of walls at once (clamping only at the end pushed men at the edge back
    into their neighbours), and a slot past the map edge is aimed at the edge, so nobody leans into it.
  Facing follows the intended velocity, not the shove. A marching squad that meets a holding line bends it (≈0.9 m
  at weight 3) and stays blocked in front of it — a jam the M1 bench (stuck metric) and M3 combat (engaged fighters
  hold) will address. `MovementMetrics.OverlappingFighters` is the overlap metric below.
- **Walls (step 5, decided 2026-11-21).** Walls are axis-aligned rectangles (`map.walls: [{ "from": [x, y], "to": [x, y] }]`,
  ≥ 0.1 m thick, on the map); bodies collide with them exactly and slide round their corners (`Movement/Terrain.cs`).
  `Terrain.Constrain` (map edge + walls) replaces the edge-only clamp everywhere: the per-pair and final collision
  clamps, order targets, slot targets. A body partly in a wall is pushed straight away from it; a centre inside a wall
  (or exactly on its outline) leaves by one of the four sides, counting only sides where a body fits on the map and
  whose exit is inside no other wall (a wall can be flush with the edge or abut another). Bodies and order targets
  take the nearest such side, so an order aimed into a wall goes to the nearest open ground. Slot targets take the
  side on the shortest way to the squad's anchor (`Terrain.ConstrainToward`): a man whose slot falls in a wall stands
  as close as he can on his squad's side, not across a palisade from it, and counts as holding when he's there. The
  squad is formed up without men whose slots have no room for a body (`Constrain` moves the slot: past the edge, in or
  against a wall, in a gap narrower than a body), since several of them may be sent to the same spot. Steering
  removes the part of a man's velocity heading into a wall within personal space. Spawns in or against a wall are
  content errors. Routes round walls came in step 6 (above). Known: a formation ordered so tight to a wall that its men
  with in-wall slots have no room on their squad's side (a line whose front rank is 0.5 m from a 1 m palisade; 0.6 m
  forms up) shoves its own front rank and never forms up (re-forming around obstacles: left for M2 playtests).
- **Known, not covered by the bench — moved to M3 (2026-11-21); the melee lock (M3 step 2) now stops contact pushes:** a
  push several squads deep beats `holdWeight` — three lines marching through a
  holding line shove it back ~12 m (design question: may a mass of movers drive a holding line back? default: yes until
  M3's melee lock stops "march on" pushing), and a front line crushed between such a push and its own reserve overlaps
  heavily (~20 % of fighter-ticks, visible in the metric); squads ordered onto clashing slots (two formations
  overlapping the same ground) never settle — their men keep shuffling ~1 cm per tick; when an order changes while
  allies are deep inside each other (or a squad stops on clashing allied slots), the last 25 cm of overlap clears in one
  tick (jumps up to ~0.3 m measured).
- Slot assignment is stable: it changes only when men fall (ranks close from the back) and when the squad re-forms or
  faces about (nearest man per slot, front first) — see above.
- Steering: arrive-at-slot + separation from neighbours + wall avoidance; engaged fighters are held in place
  (melee lock) and only shuffle to keep spacing — except that, closing in (§11), men of a squad in melee step toward
  the enemy until well inside their reach.
- Collision: relaxation passes in id order (6 since step 4), push apart overlapping circles, clamp to the map and out
  of walls (`Terrain.Constrain`).
- Steering, not velocity obstacles: the M1 bench showed no jams at 20 vs 30 — only engaged presses, which stand by design until M3 (decided 2026-11-21).

Benchmark scenarios (in `content/scenarios/bench/`, M1 step 8):

| Id | Scenario | Watch for |
|---|---|---|
| S1 `s1-clash` | open-field clash, 20 vs 30 (two squads of 10 vs three) | jams, orbiting, overlap, line breaking apart |
| S2 `s2-gate` | a line of 10 and a column of 8 through a 3 m gate | deadlock at the choke |
| S3 `s3-crossing` | two lines of 10 crossing at 90°, a column following | interpenetration, oscillation |
| S4 `s4-wheel` | a line and a shield wall wheel a quarter turn; a column wheels round a building | slot chaos, stragglers |
| S5 `s5-shieldwall` | a holding shield wall of 10 charged by a line of 10 and a column of 8 | wall integrity, push-through |

Metrics (`Movement/MovementMeter.cs`, read-only, used by the CLI and tests): *engaged* — a squad any of whose men is
in melee (`Fighter.IsEngaged`: an enemy within his weapon's reach, M3 step 2; until then bodies within 0.5 m); the whole
squad counts, its rear ranks held by the fight in front (decided 2026-11-21); *stuck* — a member of a squad that isn't engaged, more than 1 m from his slot and
moving under 1 cm a tick (0.2 m/s), for more than 2 s; *longest spell more than 1 m from a slot*, moving or not
(orbiting shows here); mean and worst slot error; overlap ratio (fighter-ticks in which a fighter is closer than
r1 + r2 − 5 cm to another, `MovementMetrics.OverlappingFighters`; allies allowed through each other's ranks don't
count); passing and in-walls ratios; the share of members' ticks engaged and the orders still open at the end (so an
engaged press can't hide a jam); tick time p50/p99 (`bench`).
M1 exit: no fighter stuck > 2 s in S1–S4, overlap < 1 % of fighter-ticks, p99 tick < 2 ms for 50 fighters on the
dev Mac, and it looks right in the replay viewer.

**Results (2026-11-21, Apple M4, Release):** `run <file> --ticks 900 --json` and `bench <file> --ticks 900 --runs 5`
(after a 1 s warm-up; without it p99 measured the JIT, ~9× higher). Orders finish by ticks ~300–480, so p50 includes
idle ticks.

| | fighters | stuck > 2 s | overlap | engaged | open orders | slot error mean / worst | longest > 1 m off slot | tick p50 / p99 / max |
|---|---|---|---|---|---|---|---|---|
| S1 | 50 | 0 | 0.00 % | 32.5 % | 2 | 1.43 / 5.30 m | 33.4 s | 0.042 / 0.063 / 0.073 ms |
| S2 | 18 | 0 | 0.00 % | 0 % | 0 | 0.043 / 2.52 m | 0.7 s | 0.004 / 0.017 / 0.071 ms |
| S3 | 28 | 0 | 0.00 % | 0 % | 0 | 0.068 / 3.56 m | 4.8 s | 0.007 / 0.036 / 0.044 ms |
| S4 | 28 | 0 | 0.00 % | 0 % | 0 | 0.004 / 1.08 m | 0.1 s | 0.008 / 0.012 / 0.085 ms |
| S5 | 28 | 0 | 0.00 % | 13.8 % | 0 | 0.053 / 7.29 m | 7.8 s | 0.008 / 0.029 / 0.035 ms |

**After the melee lock (M3 step 2, 2026-11-22, before combat, morale and closing in; the table above is M1's):** S1 — 0 stuck, 0 % overlap, engaged 60.9 %,
4 orders open: both head-on pairs lock (the shield wall and the raider flank no longer slip through each other),
p99 0.032 ms; S5 — the attackers stop at the wall and lock, engaged 74.7 %, 2 orders open; S2–S4 unchanged.

In M1, S1's head-on pair (files lined up) pressed and stood for the rest of the run — engaged, their two orders open
by design until combat; its rear ranks are what the slot error and the 33 s spell measured. The measurable criteria hold
with a wide margin (`BenchTests` pins stuck, overlap and "no open order outside an engaged press" for S1–S4, and S1's
press); "looks right" is the user's call in the viewer. **Decided 2026-11-21: steering stays** — no jams or overlap
that velocity obstacles (ORCA) would fix. **Fixed in M3 step 2:** marching enemies that met off-line slipped round each
other like allies jostling past (S1's shield wall and flank; S5's charge worked through the shield wall, 7.3 m worst
displacement); the melee lock now stops both squads at contact.

## 11. Combat

**Decided 2026-11-22 (M3):** morale ends most fights (a squad usually breaks after losing a quarter to a third of its
men, much sooner when hit in flank or rear); a man who falls is *out of the fight* (wounded or dead is settled after
the battle — men who went down die unless treated, campaign later); there are **no hit points**: every blow that gets
through leaves a wound. The numbers below were confirmed by the user on 2026-11-22 (weapon timings retuned 2026-11-23)
and live in `content/defs/combat.json` for tuning.

**Flank and rear, clarified by the user 2026-11-25.** "Much sooner" means: a flank or rear attack drains the attacked
squad's morale faster, and more blows land in the same time, so it breaks sooner and the attackers lose fewer men. The
flank's better chance to hit applies to men already fighting their front with their side or back to the flankers; men
not yet fighting turn to face the flankers. The sim already does the second part (every man faces his nearest enemy
in reach: in S7, 71 % of the flankers' blows land from the front; keeping a man's target while it stays in reach was
measured and changes nothing — the flank blows only move between the two attacking squads). The first part is weak
today: the morale cost comes per flank or rear blow, and the flanked line still breaks at ~24 % lost against 25 %
head-on. The measured options (a per-second "flanked squad" morale cost among them) are kept in the roadmap,
phase-1 open questions 9; **deferred by the user 2026-11-25**, nothing built.

**Balance reference (D-026, 2026-11-24).** Today's squad-vs-squad fight, in today's settings, *is* balanced: it is the
reference new mechanics are tuned from, and it replaces the 2026-11-22 aim of "an equal 8 vs 8 lasts about a minute".
Every number that moves balance lives in content defs — experience buffs, weapon stats, ability strength and
cooldowns, standoff distance and pressure, morale thresholds — never in code.

**The reference, measured (M3 step 7, 2026-11-25; re-pinned for weapon penetration in M3 step 9, the same day).** Each scenario runs for seeds 1–n (4000 for S6, 1000 for the
other small fights, 50 for the sandbox) to its decision or 3 minutes; "decided at" counts from the start, so it
includes the march into contact (about 6.5 s in S6). No run of any of them ended in annihilation, so that column is
left out.

| Scenario (seeds) | Side 0 wins | Side 1 wins | Undecided at 3 min | Decided at | Lost when a squad broke | Down at the end, side 0 / 1 | Blows that got through |
|---|---|---|---|---|---|---|---|
| **S6** `bench/s6-lines`: 8 vs 8 warriors head-on, mirror images (4000) | 51.1 % | 48.7 % | 0 | 22.2 s | 24.9 % | 15.3 / 15.6 % | 12.2 % |
| **S7** `bench/s7-flank`: S6 + 8 Hird flankers striking the raiders' end ~3 s after the lock (1000) | 98.8 % | 1.2 % | 0 | 16.1 s | 23.8 % | 3.7 / 23.5 % | 15.4 % |
| `m3-flank`: 8 hold + 6 flankers at 7 s vs 8 charging (1000) | 95.8 % | 4.2 % | 0 | 21.9 s | 23.7 % | 4.7 / 23.2 % | 15.4 % |
| `m3-spears`: 8 spearmen hold vs 8 warriors charging (1000) | 72.0 % | 28.0 % | 0 | 28.3 s | 25.8 % | 9.8 / 20.8 % | 10.3 % |
| `m3-shieldwall`: a wall of 8 holds vs a line of 10 (1000) | 50.0 % | 50.0 % | 0 | 26.1 s | 22.0 % | 15.0 / 14.8 % | 11.9 % |
| `m3-withdraw`: two walls of 8; side 0 steps back at 10 s (1000) | 50.9 % | 48.6 % | 0.2 % | 70.1 s | 25.4 % | 15.9 / 16.4 % | 4.2 % |
| `m3-veterans`: 6 elite in mail hold vs 12 new levies (1000) | 99.4 % | 0.6 % | 0 | 16.7 s | 15.0 % | 2.9 / 14.8 % | 24.1 % |
| `m3-rally`: 8 new levies hold vs 6 elite charging (1000) | 0.6 % | 99.4 % | 0 | 14.4 s | 17.9 % | 17.7 / 2.2 % | 26.0 % |
| `sandbox/villages-assault`: 5 squads a side, side 0 attacks (50; a smoke check) | 16 % | 14 % | 70 % | 104 s | 20.0 % | 14.9 / 13.4 % | 9.5 % |

Penetration (§7, axe 300, spear 200) moved the four rows where axes or spears meet armour and nothing else: spearmen
holding against swordsmen in padded coats now win 72 % instead of 60 %; the levies' axes against mail and helm and the
sandbox moved within a few points. What it says against the targets below: **equal forces win equally** (S6 51/49, and in 0.3 % neither side was left
standing; a wall of 8 against a line of 10 and two walls are even too); **fights end by rout**, never annihilation; an
even fight breaks a squad when it has lost **a quarter of its men** (25 %: 2 of 8), an outclassed one sooner
(15–18 %). A second squad on the flank (S7) wins almost always and ends the fight 6 s sooner by wounding faster (15.4 %
of blows get through, against 12.2 %); the flanked raiders still break at ~24 % lost, so the flank's morale cost
barely shows in the losses at the break — **open question** for the user (roadmap, phase-1 open questions 9): is
"much sooner when hit in flank or rear" (2026-11-22, above) sooner in time, which holds, or with fewer losses, which
doesn't yet? An equal 8 vs 8 lasts 15.7 s from the first blow (22 s with the march) — far from 2–4 minutes, which is a whole battle of 15–20 a side with manoeuvre, a standoff and AI (M4–M6),
not one squad's fight. The sandbox mostly runs out of time because nobody has AI yet: a squad that wins its fight just
stands. *One experience level ahead* (65–70 %), measured once step 9 set experience and kit apart (2026-11-26; S6's
lines in the same `sword-shield-padded`, 2000 seeds each): experienced beats new **97 %**, professional beats experienced
**96 %**, elite beats professional **93 %** — far above the target. Each step of the ladder adds 80 to the hit chance,
40 to defence and 100–150 to starting morale, and the three compound. Open question for the user (roadmap, phase-1
open questions 10); nothing retuned.

The numbers are pinned in `sim/tests/Golden/balance-reference.json`, checked by `BalanceReferenceTests` in Release
builds (Debug is several times slower; there it only runs the first 4 seeds of each under its overflow checks). Each
measure may move 5 of its standard errors (a share's error is never taken below its value at 3 runs in n, so "none in
1000" still allows a few) or a floor (time 1 s, loss shares 2 points); S6 must also keep each side inside the D-026
band, 45–55 %. Reshuffling which seed wins passes. How big a shift fails: on S6's win share ±4 points, on the other
even fights' ±8 at 1000 seeds — a lurch, not a nudge; compare a balance change with `batch` over 1000 seeds or more as
well (`battle-run`). The sandbox row is a smoke check: at 50 seeds its tolerances are ±20–30 points. A step that
changes balance on purpose (M3 steps 10–11) re-records the file with `Record_WritesTheReferenceFile` and this table,
and says why. To see one: `batch content/scenarios/bench/s6-lines.json --seeds 1-4000 --ticks 3600` (§17; ~1 min).

Targets for a whole phase-1 battle of 15–20 a side (M6):

| Measure | Target |
|---|---|
| battle length | 2–4 min of real time without pauses |
| standoff length (§12) | 20–60 s |
| how it ends | usually one side routs; total annihilation in fewer than 1 battle of 10 |
| standoff with one side clearly stronger | no blood in about half the encounters |
| equal forces | 45–55 % wins each side |
| one experience level ahead | 65–70 % wins |
| a Retainer's ability | shifts the outcome noticeably, never decides it alone |

- **Engagement and melee lock (M3 step 2, built):** after movement (`TargetingSystem`, stage 7) every living man takes as
  `Target` the nearest living enemy whose body is within his weapon's reach of his own (edge to edge), ties by the lower
  id; with a target he is *engaged*. Any direction counts; where a blow may come from is step 3's business. Engaged men
  hold their ground — they only ease out of personal space — and weigh their formation's hold weight in a push. A squad
  with an engaged man stops: it doesn't march, turn or regroup, and its order waits until contact ends (the order then
  carries on, e.g. after the enemy breaks). Marching enemies no longer slip through each other (the M1 known issue).
  **Getting out (decided 2026-11-22): only backward, slowly** — when the way on lies more than a quarter turn behind the
  squad's front (straight to the target when it is behind with a clear way), it steps toward it at `stepBackPace` (0.5)
  of its pace, still facing the enemy, and its men follow their slots out of reach; an order ahead or sideways waits. It
  stays `Withdrawing` — stepping back, facing the enemy — until no enemy is within each man's reach + `withdrawClearance`
  (3 m, `movement.json`); then it marches on as usual, facing about if the way on is behind. An enemy that keeps
  following never sees its back: the withdrawal then ends at the target, still facing it. Route marks passed while
  withdrawing narrow or widen the squad without stopping; a regroup wait starts over once out of contact. Engaged men
  weigh their formation's hold weight whatever their squad is doing. The bench's "engaged" squad (stuck excluded) is one
  with an engaged man. Spearmen engage from 1.5 m, so a spear line halts before a sword line reaches it.
- **Closing in (asked for 2026-11-23):** in a squad standing in melee (a man engaged and not running; the squad
  neither marching, turning nor stepping back — waiting for a route counts as standing), every man not yet fighting
  steps at walking pace toward the nearest enemy *in the fight* (engaged, or of a squad in melee — closing in never
  starts a second fight) and not running whose body is within `closeInRange` (3 m) of his own, stopping half his reach
  short of touching; an engaged man likewise keeps stepping in until he is half his reach from his enemy, so the press
  doesn't push him in and out of reach. Both only while he stands within `closeInRange` of his own slot: nobody chases a
  withdrawing enemy or wanders off. More men fight than the first pairs: rear ranks step round their front men and a
  longer line wraps round a shorter one's ends. **A formation that keeps its ranks** (`keepsRanks` in
  `formations.json`, decided 2026-11-23 for the shield wall) doesn't close in at all: its men stay in their places and
  only those in reach fight. The 8-man wall against 10 raiders (`m3-shieldwall`) wins half of 1000 seeds since the
  2026-11-23 weapon retune (§11, the reference table; the 7-in-10 from 10 seeds before it is gone); in S1 it is often
  swept away when the Hird line beside it breaks. A man closing in may walk into a holding allied squad and pass
  through it like any man on orders.
- **Attacks (M3 step 3, built):** a blow is a windup, the strike, then a recovery (`CombatSystem`, stage 8, men in id
  order). An idle man starts a windup as soon as his `Target` is within reach and within his **strike arc** (60° either
  side of his facing); **engaged men turn to face their enemy** at their own turn rate (decided 2026-11-23; movement,
  stage 6), so a man taken in the flank or rear turns to meet it and loses blows while he turns. On the strike tick he
  strikes his current target if he still can — else the blow is lost — rolling the chance on the `Combat` RNG stream; a
  `BlowStruck` event carries attacker, defender, sector, chance and whether it got through. Then he recovers; a new
  windup can start the tick the recovery ends, so a cycle is windup + recovery. **Sectors** (decided 2026-11-23),
  measured from the defender's facing to the attacker: front within 60°, flank to 120°, rear beyond (`frontArc`,
  `rearArc`). The shield-wall bonus needs a shield, the formation's `shieldWall` flag and a frontal blow. What a blow
  that gets through does is step 4. Reach is the gap between the two bodies. Arcs are converted to angle units by
  rounding, so a boundary angle (exactly 60°) counts as inside. Men stepping back out of melee keep facing and striking
  their enemy while he is in reach. Proposed (windup + recovery), retuned by the user 2026-11-23: sword 0.7 + 1.2 s, reach
  0.5 m; axe 0.9 + 1.5 s, 0.5 m; spear 0.8 + 2.0 s, 1.5 m (still fights from the second rank: 1.25 m rank spacing);
  seax 0.5 + 0.8 s, 0.3 m.
- **Does it get through (‰):** *base* from the matrix **weapon type × attacker experience**, minus the defender's
  protection, plus position, clamped to 20..950. The roll uses the `Combat` RNG stream.

  | base ‰ | new | experienced | professional | elite |
  |---|---|---|---|---|
  | sword | 420 | 500 | 580 | 660 |
  | axe | 400 | 480 | 560 | 640 |
  | spear | 380 | 460 | 540 | 620 |
  | seax | 340 | 420 | 500 | 580 |

  | defender, subtract ‰ | values |
  |---|---|
  | shield | none 0 · round 220 (frontal; half from the flank, none from the rear) |
  | body armour | none 0 · padded 70 · mail 140 |
  | head cover | none 0 · cap 30 · helm 60 |
  | experience | new 0 · experienced 40 · professional 80 · elite 120 |
  | penetration (attacker's weapon) | his armour and head cover count only (1000 − p) ‰: sword 0 · seax 100 · spear 200 · axe 300 (M3 step 9) |
  | his weapon's defence | spear 30 (keeps the enemy at reach) · others 0 (M3 step 9; was the spearman warrior type) |
  | position | flank +100 to the attacker · rear +200 · shield wall, frontal −80 more |
  | wounds | the *attacker's* own wounds: Light −40 · Heavy −120 |

  Integer permille throughout: a share (the shield from the flank, `protection × share / 1000`) truncates toward zero;
  everything is summed, and the clamp comes last.

  An experienced swordsman against an experienced man with round shield, padded coat and cap: 500 − 220 − 70 − 30 −
  40 = 140 ‰ per blow, a blow every 1.9 s (0.7 + 1.2).
- **Wounds (M3 step 4, built):** a blow that gets through rolls its severity from the weapon's row — sword Light 650 / Heavy 280 / Down
  70, axe 500 / 330 / 170, spear 600 / 300 / 100, seax 720 / 240 / 40 (‰). Wounds add up: a second Light makes it
  Heavy, a Heavy on a Light leaves it Heavy; any wound on a Heavy puts the man down. Light: −40 to his blows, −10 %
  pace; Heavy: −120, −30 % pace, and a morale hit to his squad (step 5). A squad marches at its slowest man, so the
  wounded slow it. Every blow of a tick is struck (stage 8) before any wounds (`DamageSystem`, stage 9, in the order
  struck; the severity roll uses the `Combat` stream), so two men striking on the same tick both land whatever their
  ids. A man who goes down is out of the fight at once (alive false, no target, no blow); anyone targeting him loses
  the target that tick, and cleanup (stage 12) takes him out of his squad. A `Wounded` event carries who, by whom, how
  the blow landed and his wound now.
- **Kit and experience** (M3 step 9, §7): each group of a squad's men names its experience level and a named kit
  (weapon, shield, body armour, head cover), so one squad may mix both; the weapon sets reach, timings, wounds,
  penetration and its own defence.
- The reference fight (above) is the bench scenarios S6 (8 vs 8 head-on) and S7 (the same, flanked by a second
  squad), with the `m3-*` and sandbox fights beside them (M3 step 7, built). Don't edit S6 or S7 to play: a change there
  moves the reference.
- No ranged weapons in phase 1 (deferred; the idea is a second weapon for some militia and Retainers, not a separate
  kind of fighter). None of the trait abilities (§13) is ranged.
- **Planned — battle phases (M5; D-009, D-021).** A battle that doesn't open from an ambush opens with a *standoff*:
  squads form up and nobody strikes (§12, "Standoff"). The standoff ends for everyone at the first blow — the
  player's attack order or the AI's decision (open: does a man who walks into reach count?) — or when a side leaves
  the field or its squads break. State: `BattleState.Phase` (standoff, battle), who struck first. A scenario may start
  in the battle phase (an ambush, M4 step 3).

## 12. Morale (central system)

Built in M3 step 5 (`Morale/MoraleSystem.cs`, stage 10); rules in `content/defs/morale.json` (`MoraleDef`). Decided
by the user 2026-11-23: start by experience; a squad breaks after losing about a quarter; it breaks when more than half
its men rout; routers rally once safe. Other numbers are proposals, tuned on the bench.

- **Morale** is 0..1000 per man; he starts with his experience level's (`combat.json` experience `morale`: new 600,
  experienced 750, professional 850, elite 950) and never recovers above it.
- **Costs** (safety is judged once per tick, before anyone's state changes), each with a reason code (`MoraleChanged` event): a fallen squadmate costs every man of the squad
  `lossWeight` (2) × 1000 / the squad's starting size (200 in a squad of 10); his own wound 50 light / 150 heavy; a
  squadmate's heavy wound 20; a blow at him from the flank 20, from the rear 40, and at a squadmate 5 / 10 (also when it
  felled him; not when the squadmate was already running — decided 2026-11-23: he is chased, not his squad attacked); a squadmate
  starting to rout 30; an allied squad breaking within 20 m 150; an enemy squad breaking within 20 m gives +100.
- **Recovery:** a man with no enemy (in the fight, not routing) within 10 m regains 20 a second, spread exactly over
  the ticks, up to his start.
- **States** (`MoraleState`, `MoraleStateChanged` event): `Steady` ≥ 600 > `Shaken` ≥ 400 > `Wavering` ≥ 200 >
  `Routing`; down at once, up only 50 above a threshold. A wavering man's blows get 40 ‰ less. A routing man drops his
  blow and target, strikes no one, is no one's lock, ignores orders and runs for his side's map edge (the edge away from the enemy:
  the main direction from the enemies' start to his side's) at run speed — round the nearer end of a wall in his way; reaching it he has
  *fled* (out of the fight, alive). Running enemies threaten no one: they don't stop recovery or rallying and don't
  hold back a withdrawing squad, though men in reach still strike at their backs. Once safe with
  morale ≥ 350 he *rallies*: back in his place (within 1.5 m of his slot, counting his turn lag, §10) he is wavering, shaken or steady by his
  morale. Routing men don't count for their squad's lock, pace or forming up.
- **Squads:** a squad *breaks* (`SquadBroke`) when more than half its men in the fight rout: all of them rout (their
  morale unchanged, so men swept along rally sooner), its order is dropped and it refuses orders (`squad-broken`).
  The event carries the counts that decided it (M3 step 8, so the view can say why): `InFight`, its living men, and
  `Running`, those already running when it broke (the rest were swept along); on re-forming, those still running. Events
  aren't hashed, so this changes no golden.
  Rallied men of a broken squad stand where they stopped; when more than half are back the squad *re-forms* there
  (anchor at their centre, fitted onto the map and clear of walls like a move order's target; places to the nearest
  men). Only squad members rally (a Retainer standing alone needs his own rule, M3 step 10).
- **Victory** (cleanup, stage 12): once at most one side has men in the fight who aren't routing, the battle is
  decided (`BattleState.Winner`, `BattleEnded` event) — at once, even if a broken squad might re-form (decided
  2026-11-23); the sim keeps running for the view.
- **Next:** a Retainer's leadership and rallying near him (M3 step 10, §13); trait abilities that move morale (step
  11); standoff pressure (M5, below). **Later:** squad cohesion, fear from the sorcerer, sworn brothers and rivals
  (phase 2 data); fatigue only if M6 tuning asks for it.
- **Tuning note (2026-11-23, measured 2026-11-25):** an equal head-on fight of two lines of 8 (S6) breaks one when it
  has lost 25 % (2 men), 15.7 s after the first blow on average. Since D-026 that is the reference, not a fault (§11).
- **Planned — standoff (M5; D-009, D-021; `docs/design/standoff-duel.md`).** While the battle is in its standoff
  phase (§11) nobody strikes, and morale moves *before* contact. Each tick, a man with an enemy within the standoff
  distance (proposal: 30 m) has his morale changed, and recovery doesn't run for him:

  | presses down | holds |
  |---|---|
  | the visible enemy strength near him: numbers, armour, experience | his own side's visible strength near him |
  | an enemy shield wall (more than a crowd) | a Retainer in his squad or near him (leadership) |
  | an enemy step forward (`StepForward`) | militia: its homes near (M4 step 3) |
  | his side lost a duel or its leader refused a challenge (§13) | his side won a duel |
  | enemy reinforcements arrived | his own reinforcements on the way |

  A squad that breaks in the standoff leaves the field without a blow — a bloodless win (the victory check as above).
  `StepForward` presses both sides harder. A `Demand` (phase 1: "leave the field") is answered by the AI (§14): it
  leaves when its morale is already low and its leader's trait allows. Formulas and numbers (`morale.json` or a new
  `standoff` def) are M5's first decision; target lengths in §11's table.

## 13. Retainers

**Planned — M3 steps 10–11; the duel in M5.** Design: D-010, D-014, D-015, D-019, D-023, D-025
(`docs/design/heroes.md`). A Retainer (дружинник — the named hero) is the only fighter with a trait. In the campaign
he also has his own AI for independent tasks; in battle the player controls him directly (D-014). He is a `Fighter`
with a `Retainer` part (D-015 renamed this spec's `HeroData`). The player's own character, the chief (D-010), is not
on the field in phase 1 (proposal, roadmap open question 1).

- **Who (step 10):** experience experienced, professional or elite — professional and elite only for Retainers
  (`retainerOnly`, §7) — and better kit than militia (content). One trait of six (D-025). A scenario lists them apart
  from squads, as named people: `retainers[]`: `name`, `side`, `trait`, `experience`, `kit`, `body`, and either
  `squad` (the squad he starts in) or `position` + `facing` (standing alone). Their ids come after every squad's
  men, in `retainers[]` order. Converting today's elite squads (`m3-veterans`, `m3-rally`, `sandbox/villages`,
  `villages-assault`) to experienced men led by Retainers changes those battles, so step 10 re-pins their step-7
  reference on purpose and says so.
- **His place (D-019):** he joins any squad of his side in its **front rank** and can leave it to stand alone. In a
  squad he counts as one of its men for slots, pace, the melee lock and forming up. Slots (proposal): joining — at
  spawn or by `AttachRetainer` — lays the formation out for one more man (a `ranks` formation may widen); he takes
  the front-rank slot nearest him (at spawn: nearest the anchor; ties to the lower slot index), then every other man
  takes the nearest free slot (in id order, ties to the lower index), as on re-forming (§10); everyone's turn lag
  resets. Leaving lays it out for one fewer, the same way. `StartCount` (which sets loss weight and the break share,
  §12) grows by one when he joins and shrinks by one when he leaves alive, so a squad's losses are counted against the
  men it has. Alone he takes his own orders (move, attack an enemy fighter, §8) and plans his own route
  (`PathSystem`), and allies' ranks don't open for him yet (§10).
- **Leadership (passive, D-023 — "a Retainer in a squad raises its morale, the more the more experienced"):** how is
  step 10's first decision. Proposal: while he is in a squad and in the fight, its men lose less morale and recover
  faster, by his experience level's `leadership` (§7); routing men near him rally sooner (§12); his fall costs his
  squad morale (a reason code of its own).
- **State (planned, hashed):** the `Retainer` part holds values only — trait id, the tick his ability is ready
  again, the tick its effect ends — because `Fighter.Clone()` copies members and would share a mutable object between
  a snapshot and the live state. Effects expire by comparing with `Tick`, so no stage counts them down. See §15.
- **Ability (active, step 11, D-023, D-025):** one, from his trait; `UseAbility(retainer)`, no target; strength,
  duration, radius and a cooldown in seconds in content (`retainers.json`, `traits[]`); usable in the standoff too;
  an `AbilityUsed` event.

  | trait | ability | in the sim |
  |---|---|---|
  | proud | battle cry | morale up for his own men near him, down for enemies near him |
  | cautious | close ranks | his squad takes less from the front and marches slower, for a while |
  | greedy | promise loot | his squad's morale and push up for a while (the bigger share after the battle is campaign) |
  | loyal | stand to the death | his squad's morale can't fall for a few seconds |
  | bold | front-rank rush | the front rank steps forward and strikes harder; then the squad is disordered for a while |
  | cruel | no quarter | his squad finishes the fallen (more of them killed, not just down), and enemies near it lose morale faster |

  Order: the three that only change numbers (battle cry, close ranks, stand to the death) first. No quarter needs the
  battle's result to tell killed from down; that tally is what D-017 counts in the campaign.
- **Duel (M5; D-009, D-014, `docs/design/standoff-duel.md`):** during the standoff a Retainer may `Challenge` the
  enemy's leader or one of his Retainers (militia never duel), with terms — to first blood or to `Down` — and a
  stake (phase 1: "your side leaves the field"). The AI answers (§14): refusing a clearly weaker challenger costs
  nothing, refusing a stronger one costs its own side morale. Accepted: both armies hold in a ring and don't strike
  the two; the duelists fight automatically under the normal combat rules (D-014), each striking only the other;
  anyone who interferes costs his side heavy morale. The end — the first wound or a man down — swings both sides'
  morale hard (the loser's side often breaks at once). One duel per standoff; a new challenge only after a new
  approach. State: the duel (both ids, terms, since which tick).
- **Decide first:** step 10 — leadership numbers per experience level; can a Retainer rout; does his fall alone
  break a squad; does he count in his squad's break share. Step 11 — each ability's numbers, and how "finished" is
  decided for no quarter. M5 — the duel's morale swings.

## 14. Enemy AI

**Planned — M4** (D-024, D-020; `docs/design/enemies.md`). Utility + FSM, no neural nets, inside the sim and
deterministic. Two layers decide, not a separate AI per enemy: **who it is** sets the goal of its fight, **its
leader's trait** shifts its decisions.

| who | goal | how it fights |
|---|---|---|
| militia | defend its homes | stays near them, breaks early, never pursues |
| the ruler's Retainers | break the enemy | keeps formation, pursues, challenges to duels |
| bandits | loot | attacks from ambush; leaves once the loot is taken or its losses are high |

| leader's trait | shifts decisions |
|---|---|
| proud | accepts challenges, doesn't retreat |
| cautious | drags the standoff out, waits for reinforcements |
| greedy | goes for the baggage, not the men |
| loyal | holds to the end, refuses demands |
| bold | strikes first, keeps the standoff short |
| cruel | finishes the fallen |

- **Scenario (M4 steps 2–3):** the side the AI plays; each AI squad's `kind` and its leader's trait (from a Retainer
  in it, or the side's leader — open). Minimal map objects, no settlement simulation (proposal): a `homes` zone, a
  `baggage` point (taken by standing on it unopposed for a while), an ambush start (the battle opens in the battle
  phase, §11), and `reinforcements` (squads that enter at a tick or on a trigger).
- **Orders first (M4 step 1):** `AttackSquad` and stances `Hold` / `Advance` / `Pursue` (§8) for the player; the AI
  gives the same orders inside the sim, not as commands.
- **Side AI** (every ~1 s): scores goals per squad from its kind and trait — attack the weakest or nearest, flank,
  hold, go for the baggage, regroup, leave the field. In the standoff (M5): when to strike first, whether to accept a
  challenge or a demand, when to challenge.
- **Squad AI** FSM: `Idle, Advance, Engage, Hold, Regroup, Withdraw` (routing is morale's, §12, not a choice);
  issues internal orders. AI Retainers use their abilities.
- **Fighter level**: target choice within the squad's engagement (`TargetingSystem`, §11).
- Evaluation is staggered by id; ties broken by id; randomness only through the `ai` RNG stream.

## 15. RNG, hashing, snapshots, replays

- `SimRng`: PCG32. Separate streams per purpose (`combat`, `morale`, `ai`, `misc`), derived from the battle seed
  with SplitMix64, so a change in AI randomness doesn't shift combat rolls in A/B runs. RNG state is part of
  `BattleState`.
- `StateHasher`: FNV-1a 64 over fields in a canonical order, with per-section sub-hashes so a desync can be
  localised to a section. Sections now: `Core` (tick, seed, id counter, scenario fingerprint), `Rng`, `Squads` (id,
  side, name, formation id, anchor, facing, order kind/target/facing, route, files, frontage, regrouping, reforming,
  withdrawing, start count, broken, velocity, spin, members), `Fighters` (id, body, experience and kit ids (M3 step 9), side, alive, position, velocity, facing, squad, slot,
  passing through, turn lag, target, action, action ticks, wound, morale, morale state); `Core` also has the winner.
  **Planned fields and their sections:** `Fighters` — the Retainer part: trait id, ability-ready tick, effect-end tick (steps 10–11); killed (no quarter, step 11).
  `Squads` — stance, attack target (M4 step 1); AI kind, leader trait, FSM state and its since-tick (M4 step 2).
  `Core` — battle phase and the side that struck first (M5); the duel: both ids, terms, start tick (M5); baggage
  holder and since-tick, reinforcement waves already sent (M4 step 3). Each is snapshotted by value; anything held in
  a class gets a deep copy in `Clone`. Strings are
  hashed ordinally (length + UTF-16 units). Velocities and spin are recomputed every tick (stages 3 and 5) but hashed
  anyway: anything that reads them earlier in a tick (AI in M4, the view) sees last tick's values, so they are state, and
  they help localise a desync. `HashCoverageTests` checks that each field moves its own section.
- `Snapshot`: deep copy of `BattleState` — used for the view double buffer, restore-and-continue tests and
  later saves. Snapshot → restore → continue must produce the same hashes as an uninterrupted run.
- Replay file (JSON): `{ format, simVersion, scenario, seed, commands: [...], checkpoints: [{tick, hash}] }`.
  Golden replays live in `sim/tests/Golden/`; a rule change that alters outcomes regenerates them on purpose.
  Until replay files exist, the golden checks are `GoldenFinalHash` in `DeterminismTests` (seed 42, 300 ticks; its
  squads face between sine-table entries), `GoldenMarchHash` (a scripted march with an about-turn and an arrival
  facing, 400 ticks), `GoldenCrossingTrace` (every tick's hash of two lines jostling through each other, folded into one
  value — the crossing settles exactly on its slots afterwards, so an end-state hash wouldn't notice a crowding change),
  `GoldenAlliesThroughTrace` (the same for a line squeezing through an allied holding line), `GoldenGateTrace` (the
  same for a line of 8 regrouping into 3 files for a 3 m gate, going through and widening again), `GoldenDrillTrace`
  (the same for a wheel, a face-about, line → column → line on the march and ranks closing after a loss) and `ContentGoldenTests` (a test-owned JSON fixture: fingerprint and spawned state). `HashCoverageTests` checks that every state field moves its own hash section. Cross-runtime (M2 step 3): `sim/tests/Golden/content-traces.json`
  lists every content scenario with its per-tick hash trace and final hash (seed 1, 600 ticks); .NET
  (`ContentTraceGoldenTests`) and Unity EditMode (`CrossRuntimeGoldenTests`, Mono) check the same file, and a new
  scenario must be added to it (a test enforces that).

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
- **What the Unity view explains (M3 step 8).** Everything is built from events and the read-only state, view-side
  (`Knez.Game.View.BattleRecord`): which squad each man started in (the fallen and the fled leave their squad in the
  sim), blows per squad, and a *morale ledger* — per man, his morale when his account opened (the battle's start, or
  his squad's last re-form) and the sum of `MoraleChanged` deltas by reason since; recovery away from enemies isn't
  reported reason by reason, so it is the remainder (now − start − changes). The ledger takes a whole tick's changes
  before it opens new accounts for a squad that re-formed in it, and accounts are read against morale at the end of
  the tick. From it:
  - a squad's panel: who it started with, how its men are hurt, its average morale, one cell per man in his morale
    state's colour, "N of M running · breaks at K" (the break share of the men in the fight, §12), and what has cost it
    morale ("Morale 750 → 520: mates fell −250, own wounds −30") — once broken, why it broke, in words, from the
    event's own counts: "5 of 7 men in the fight were running — more than half — so the other 2 ran with them. 1 of 8
    down. Morale 750 → 170: mates fell −250, …" (or "All 6 men left in the fight were running." when nobody was
    swept along);
  - a battle log: the first blow, wounds and falls, men running (not those swept along by a break: the break's line
    counts them), rallying, back in place and fled, squads breaking (with the why) and re-forming ("4 of 6 back"),
    refused orders, the result. Shaken and wavering aren't logged (the bars show them), nor blows turned aside;
  - a morale bar over every man in the fight, filled to his morale, in his state's colour; the man under the mouse
    with his own account.
  Times in the view's text use a decimal point whatever the machine's locale.

## 17. Tools

- `Knez.Sim.Cli` (`sim/tools/Knez.Sim.Cli`). **Now (M1 step 2):** `run <scenario> [--seed N] [--ticks N]
  [--frames FILE] [--json]` loads the scenario and its defs, simulates it and prints the forces per side, the tick
  count and the final hash (JSON: `scenario`, `squads`, `fighters`, `finalHash` + section hashes, `frames`); exit codes
  0 ok, 1 simulation error, 2 bad arguments, bad content, or a frames file that can't be written (refused up front,
  including the scenario's own path, or failing mid-run). Recording is read-only: the final hash is the same with or
  without `--frames` (tested tick by tick on a moving battle). `run` also reports the M1 overlap metric: the share of
  fighter-ticks with a fighter pressed more than 5 cm into someone, and the worst tick (JSON `overlap.ratio`,
  `overlap.worstTickFighters`) and, separately, the share of fighter-ticks spent passing through allied ranks
  (`overlap.passingRatio`), so an allowed overlap can't hide a failure, and the share with a fighter more than 5 cm
  inside a wall (`overlap.inWallsRatio`; printed as "inside walls"); since step 8 also stuck fighters (`stuck`: fighters
  stuck > 2 s, longest spell, fighter-ticks past 2 s) and slot error (`slotError.mean`, `.worst`). The replay viewer
  draws the map's walls and each squad's planned route.
  - `batch <scenario> [--seeds A-B] [--ticks N] [--json]` (step 8): the scenario once per seed (default 1–20); prints
    mean and worst-run overlap, the worst run's stuck fighters and longest spell, mean slot error, and how many
    distinct final hashes. For a fight (a run with a blow or a decision; M3 step 7) also the D-026 measures, each taken
    up to the decision (`OutcomeMeter`, `OutcomeSummary`; JSON `battle`, null for a drill): wins per side, nobody
    and undecided; how runs ended — rout (the losers have living men, all running or gone), annihilation (all of them
    down), nobody left, or undecided at the `--ticks` limit; when they were decided (mean, standard error, 10/50/90 %)
    and the first blow to the decision; each squad's first break with the share of its starting men down then (also per
    side); the share of each side's men down; blows and the share that got through. Give a fight time to end: `--ticks 3600`.
  - `bench <scenario> [--runs N] [--ticks N] [--json]` (step 8): one warm-up run, then N timed runs (default 5); every
    tick timed alone; p50, p99, max and mean, and the fighter count.
  - Still to come: `run --out replay.json` and `replay verify <replay.json>` (re-simulate and compare checkpoints);
    winner, casualties and morale breaks in `run` with combat.
- Frames file (`--frames`, written by `FrameRecorder`; this is the only copy of the spec), format `knez-frames` v1:
  ```
  { "format": "knez-frames", "version": 1, "scenario", "description", "seed" (string), "ticksPerSecond",
    "map": { "width", "height", "walls": [[minX, minY, maxX, maxY]] },
    "squads":   [ { "id", "name", "side", "formation" } ],
    "fighters": [ { "id", "squad", "slot", "side", "def", "radius", "experience", "kit" } ],  // def: his body (M3 step 9; his fighter type before)
    "frames":   [ { "tick", "hash", "squads": [[id, anchorX, anchorY, facing°, ordered 1|0, targetX, targetY, targetFacing°, formation, files]],
                    "paths": [[squad, x, y, x, y, …]],   // route still ahead: next waypoint … target (step 6)
                    "fighters": [[id, x, y, facing°, alive 1|0, holdsGround 1|0, passingThrough 1|0, squad, slot]], "events": [{ "type", … }] } ],
    "faulted":  { "tick", "error" } }   // only when the simulation threw
  ```
  - One frame for tick 0, then exactly one after every tick (players assume one frame per tick); metres to 0.1 mm,
    degrees (0 = east, counter-clockwise) to 0.01°; rows in id order; `hash` is the state's total hash.
  - `seed` is a string because JavaScript numbers lose 64-bit values above 2^53.
  - `events` holds that tick's events. `type` is the C# class name (renaming an event class changes the format);
    fields are written for the types in `FrameRecorder.DescribedEventTypes` — `CommandRejected`: `command`,
    `side`, `reason`; `BlowStruck` (M3): `attacker`, `defender` (fighter ids), `sector` (`front`/`flank`/`rear`),
    `chance` (‰), `landed`; `Wounded` (M3): `fighter`, `by`, `blow` (how it landed), `now` (his wound after it;
    `down` = he fell); `MoraleChanged`: `fighter`, `reason`, `delta`, `now`; `MoraleStateChanged`: `fighter`,
    `from`, `to`; `SquadBroke`: `squad`, `reformed`, `inFight`, `running` (M3 step 8); `BattleEnded`: `winner`. Fighter rows end (from M3) with morale,
    morale state and wound. A test fails when a `SimEvent` subclass has no entry there.
  - If the simulation throws, the file is still closed as valid JSON: the frames up to the last finished tick plus
    `faulted` (the tick that threw and the exception); the CLI exits 1.
  - Static tables describe the battle at tick 0. **Compatibility:** adding row columns, keys or event types keeps
    version 1 — readers ignore what they don't know; a version bump is only for breaking changes. Since step 7 squad
    rows carry the current formation id and narrowing, and fighter rows the current squad (0 once fallen) and slot; the
    viewer shows them in the squad list and the hover panel, and falls back to the static tables for older files.
- `tools/replay-viewer/`: static page (HTML + one script, no dependencies) that plays a frames file: map with a 1 m /
  5 m grid, fighters as circles in squad colours with a facing tick, squad anchors with facing arrows and names, dead
  fighters greyed, events of the current tick, hover details; play/pause/step/scrub, speed ×0.25–×4, zoom/pan,
  interpolation between ticks, a red note for a faulted run. URL:
  `?frames=<url>&tick=N&speed=2&play=1&labels=0&anchors=0&grid=0&ids=1&overlaps=0`; a file can also be picked or
  dropped. A red ring marks fighters pressed more than 5 cm into someone (the overlap metric, computed in the viewer;
  allies passing through a holding ally's ranks are allowed and not ringed). Hover shows whether a man holds his ground.
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

- (Fighter radius: 0.3 m body, kept; fighting distance body + 0.5 m; grid cells 0.5 m — all decided 2026-11-21.)
- (Steering vs ORCA: steering, decided 2026-11-21 from the M1 bench.)
- Exact formation set for phase 1 (proposal: Line, ShieldWall, Column).
- Morale numbers, thresholds and rout rules — tune in M3 with playtests.
- (Retainer abilities: one per trait, six traits — D-023, D-025, §13.)
- Phase-1 questions with the step that settles each: roadmap, "Open questions for phase 1".
