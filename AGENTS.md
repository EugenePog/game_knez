# AGENTS.md — Game_knez

Instructions for every coding agent (Claude Code, Codex, Cursor, …) and for humans.
This file is the source of truth for how we work in this repository. Keep it short; details live in `docs/`.

## The project in one paragraph

Game_knez is an RPG-first **real-time-with-pause tactics** game inspired by the Viking-age Baltic (VIII–IX c.):
a clan that fled a raid settles next to a foreign trading town, and its warband (дружина) opens new routes.
The player commands 3–5 heroes directly and 2–3 squads of 5–8 fighters through orders (formation, shield wall,
hold, advance, pursue). Morale is the central combat system. Engine: **Unity 6**, but the battle simulation is
**pure C# outside Unity** and deterministic.

- Concept (Russian, snapshot): [docs/concept/concept-v2.md](docs/concept/concept-v2.md)
- Battle simulation spec: [docs/battle-sim.md](docs/battle-sim.md)
- Roadmap and current milestone: [docs/roadmap.md](docs/roadmap.md)
- Decision records: [docs/adr/](docs/adr/)

## Current phase — read before writing code

**Phase 1: internal 2D skirmish vs AI (6–8 weeks).** One battle on a small map: 2 player squads + 2 heroes
vs 3 enemy squads, top-down 2D view (circles, order arrows, morale bars). Orders, 2–3 formations, morale,
wounds, enemy AI. Exit criterion: *the battle is still interesting the 20th time.*

Until that criterion is met, **do not build**: campaign, global map, economy, settlement, relationships
beyond what a battle needs, 3D, art pipeline, Steam integration, save/load of campaigns, multiplayer.
If a task seems to need one of these, stop and ask. Cut from the concept for good: magic, mass battles
(50–100 per side), Stronghold-style economy, multiple independent parties in MVP.

The first thing proven is the main technical risk: **formation movement and local collision avoidance at
20 vs 30 fighters** (milestone M1 in the roadmap).

## Repository layout

```
AGENTS.md  CLAUDE.md  README.md
docs/                 concept snapshot, battle-sim spec, roadmap, ADRs, playtest notes
sim/                  pure C# simulation (no UnityEngine) — the heart of the game
  Knez.Sim.sln
  src/Knez.Sim/       core sim library: netstandard2.1, C# 9, also a Unity local package
  src/Knez.Sim.Content/   JSON text → defs and scenarios (own strict reader, ADR-0004); also a Unity package
  tests/Knez.Sim.Tests/   NUnit tests, run with `dotnet test` (no Unity needed)
  tools/Knez.Sim.Cli/     headless battle runner: run / batch / bench / replay verify
content/              data: fighter/weapon/formation defs, battle scenarios (JSON)
tools/                repo scripts: check_sim_purity.py, replay-viewer/ (HTML canvas)
game/                 Unity 6 project (view + input only); references sim as a local package
artifacts/            build output, replays, reports — gitignored
```

Folders appear as milestones reach them; `sim/` arrives in M0, `game/` in M2.

## Hard rules (never break these)

1. **`sim/src` never references Unity.** No `UnityEngine`, `UnityEditor`, no Unity packages. It must build and
   test with plain `dotnet`.
2. **The sim is deterministic.** Same scenario + seed + command log ⇒ bit-identical state hash on every run,
   OS, CPU and runtime (CoreCLR, Mono, IL2CPP). In `sim/src`:
   - no `float`/`double`/`System.Math` trig — use `Fix`, `FixVec2`, `FixMath` ([ADR-0002](docs/adr/0002-fixed-point-math.md));
   - no `System.Random` — use `SimRng` streams from the tick context;
   - no wall clock (`DateTime`, `Stopwatch`), threads, tasks, `async`, I/O, `Guid.NewGuid`, `GetHashCode()` of runtime objects;
   - never iterate `Dictionary`/`HashSet` for logic — iterate lists sorted by `EntityId`; ties broken by id;
   - no mutable `static` state.
   `tools/check_sim_purity.py` enforces most of this in all of `sim/src` (it also runs as a Claude Code hook and in CI).
3. **All battle state lives in `BattleState`.** Systems are stateless; GameObjects and view code hold no game
   state, only caches keyed by `EntityId`.
4. **Input enters only as commands** (`ICommand` stamped with a tick) through the command queue. AI writes orders
   inside the sim; the view never mutates state.
5. **Time is ticks**, not seconds or animations: 20 ticks/s, action durations in ticks. Pause = the view stops
   stepping the sim.
6. **Navigation, collisions and combat logic are ours, in C#.** No Unity NavMesh, no Unity physics in gameplay.
   Height, cover and line of sight are data in the sim so a 3D view can later read the same state.
7. Any new state field that affects the future must be **hashed and snapshotted** (see `sim-feature` checklist).

## Build and test

Toolchain ([docs/dev-setup.md](docs/dev-setup.md)): .NET 10 SDK machine-wide (Homebrew cask `dotnet-sdk@10`, pinned
by `global.json`); Git LFS machine-wide; Python 3.13 from Homebrew (`python@3.13`) with the project venv `.venv`
(`python3.13 -m venv .venv`); Unity 6 LTS via Unity Hub from M2. `bash tools/bootstrap.sh --check` reports what's
missing. Don't install or upgrade SDKs or global dotnet tools (`dotnet tool install -g`) — use local tools. Python
packages go into `.venv` (`.venv/bin/python -m pip install …`) and are listed in `tools/requirements.txt`; never
`pip install` outside the venv.

```bash
dotnet build sim/Knez.Sim.sln
dotnet test sim/Knez.Sim.sln
dotnet test sim/Knez.Sim.sln --filter Category=Determinism
python3 tools/check_sim_purity.py
dotnet run --project sim/tools/Knez.Sim.Cli -c Release -- run content/scenarios/formations.json --seed 1
```

A change to `sim/` is done only when the build is warning-free, all tests pass, and the purity check is clean.
GitHub Actions (`.github/workflows/sim.yml`) runs the same checks on Linux x64 for every push touching `sim/`; a
failing golden hash there but not locally is a cross-platform determinism bug.

## C# conventions

- `sim/src`: **C# 9 only** (Unity 6 constraint): no file-scoped namespaces, global usings, `record struct`,
  `required`, raw strings. `Nullable` enabled. `TreatWarningsAsErrors` on.
- Naming: `PascalCase` types/methods/properties, `_camelCase` private fields, `camelCase` locals/parameters.
  One public type per file; file name = type name. Namespaces mirror folders (`Knez.Sim.Movement`).
- Hot paths (anything called per fighter per tick): no LINQ, no lambdas capturing locals, no boxing, no
  allocations — preallocate and reuse buffers in `BattleState`/scratch structures.
- Balance numbers live in content defs, not as constants inside systems.
- Comments explain *why*. Public sim APIs get a one-line `///` summary.
- Tests: NUnit, `MethodUnderTest_Condition_Expected`, tag with `[Category("Determinism")]`, `[Category("Scenario")]`
  or `[Category("Slow")]` where relevant.

## How agents work here

1. **Read first**: this file, the relevant section of `docs/battle-sim.md`, the current milestone in `docs/roadmap.md`.
2. **Small, verifiable steps.** One mechanic per change. For sim work write the failing test first.
3. **Verify, don't assume**: run the build, tests and purity check; for visible behaviour run a scenario headless
   and look at the replay (see the `battle-run` playbook).
4. **Docs follow code**: if you change a rule, the tick order or a data format, update `docs/battle-sim.md` in the
   same change. Hard-to-reverse decisions get an ADR (`docs/adr/NNNN-title.md`, template in `docs/adr/README.md`).
5. **Ask instead of guessing** on game-design questions (numbers that define feel, new mechanics, scope). Propose a
   default with a reason; don't silently invent design.
6. **Commits**: imperative, scoped prefix — `sim: add SimRng PCG32 streams`, `docs: ...`, `game: ...`, `tools: ...`,
   `content: ...`. Regenerated golden replays must say why in the message. Never commit `artifacts/`.
7. Unity: always commit `.meta` files; don't hand-edit scene/prefab YAML beyond trivial fixes — ask the human to
   do editor work and describe the steps.

Playbooks for common tasks are in `.claude/skills/*/SKILL.md` (`sim-feature`, `determinism`, `battle-run`,
`unity-view`, `playtest-notes`). Any agent may read and follow them.

## Glossary (Russian design term → code name)

| Русский | Code | Note |
|---|---|---|
| дружина | `Warband` | the player's whole force |
| отряд | `Squad` | 5–8 fighters, controlled by orders |
| боец | `Fighter` | any combatant; heroes are fighters too |
| герой | `Hero` | component on a fighter: abilities, leadership |
| именной дружинник | named retainer | veteran promoted to a named character (later) |
| строй | `Formation` | slot layout for a squad |
| стена щитов | `ShieldWall` | formation + stance |
| приказ | `Order` | squad-level intent inside the sim |
| команда (ввод) | `Command` | external input stamped with a tick |
| держать / наступать / преследовать | `Hold` / `Advance` / `Pursue` | squad stances |
| мораль | `Morale` | per fighter, 0..1000 |
| сплочённость | `Cohesion` | squad-level, from leadership and bonds |
| усталость | `Fatigue` | |
| ранение | `Wound` | Light / Heavy / Mortal |
| бегство | `Rout` | |
| лидерство | `Leadership` | hero aura / attached leader |
| побратим / соперник | `SwornBrother` / `Rival` | relationship tags (morale modifiers) |
| колдун | `Sorcerer` | enemy leader; source of `Fear` (no real magic) |
| князь | `Knez` | the town's prince (campaign, later) |
| воевода | `Voivode` | hero left to run the homestead (campaign, later) |

## Setting and content rules

Real geography, fictional names and polities. Names are stylised after Old Norse, Baltic and Slavic speech of the
era but never copy real historical people or places; no modern ethnonyms, flags or symbols. Every people is shown
with respect. There is no real magic — the "sorcerer" is a charismatic leader whose power is belief and fear.
Store-facing text says "inspired by the Viking-age Baltic"; never "history of Rus".
