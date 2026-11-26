# AGENTS.md — Game_knez

Instructions for every coding agent (Claude Code, Codex, Cursor, …) and for humans.
This file is the source of truth for how we work in this repository. Keep it short; details live in `docs/`.

## The project in one paragraph

Game_knez is an RPG-first **real-time-with-pause tactics** game inspired by the Viking-age Baltic (VIII–IX c.):
a clan that fled a raid settles next to a foreign trading town, and its retinue (дружина) opens new routes.
The player, the clan's chief, commands Retainers (дружинники — the named heroes) directly and 2–3 squads of 5–8
fighters through orders (formation, shield wall, hold, advance, pursue). Morale is the central combat system.
Engine: **Unity 6**, but the battle simulation is **pure C# outside Unity** and deterministic.

- Concept (Russian, snapshot): [docs/concept/concept-v2.md](docs/concept/concept-v2.md)
- Game design (Russian): [docs/design/](docs/design/README.md), decision log D-NNN in
  [docs/design/decisions.md](docs/design/decisions.md)
- Battle simulation spec: [docs/battle-sim.md](docs/battle-sim.md)
- Roadmap and current milestone: [docs/roadmap.md](docs/roadmap.md)
- Decision records: [docs/adr/](docs/adr/)
- Art assets (sprites, manifest, palette for the Unity view): [docs/art-assets.md](docs/art-assets.md); how the view
  draws them: [docs/art-pipeline.md](docs/art-pipeline.md)

## Current phase — read before writing code

**Phase 1: internal 2D skirmish vs AI.** One battle on a small map: 2 player squads + 2 Retainers vs 3 enemy
squads, top-down 2D view (token sprites over squad-coloured discs, order arrows, morale bars). Orders, 2–3
formations, morale, wounds, Retainers with leadership and a trait ability, the standoff and duels (D-021), enemy AI.
Exit criterion: *the battle is still interesting the 20th time.*

Until that criterion is met, **do not build**: campaign, global map, economy, settlement, relationships
beyond what a battle needs, 3D, Steam integration, save/load of campaigns, multiplayer. The 2D art pipeline is the
one exception, asked for by the user on 2026-11-26 (roadmap, "Art in the phase-1 view"): the battle view draws the
designed sprites from `docs/design/art/` ([docs/art-pipeline.md](docs/art-pipeline.md)); new art, animation, 3D
models or asset-store packages still need asking.
If a task seems to need one of these, stop and ask. Designing them on paper (`docs/design/`) is allowed; a design
doc is not a task to build it. Cut from the concept for good: magic, mass battles
(50–100 per side), Stronghold-style economy, multiple independent parties in MVP.

The first thing proven is the main technical risk: **formation movement and local collision avoidance at
20 vs 30 fighters** (milestone M1 in the roadmap).

## Repository layout

```
AGENTS.md  CLAUDE.md  README.md
docs/                 concept snapshot, battle-sim spec, roadmap, ADRs, playtest notes
  design/             game-design docs per system, design decision log (D-NNN), open questions
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
dotnet test sim/Knez.Sim.sln -c Release --filter FullyQualifiedName~BalanceReferenceTests   # balance reference, Release only
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

Names in `code` exist in the code or are fixed by a decision (D-NNN in `docs/design/decisions.md`); names in plain
text aren't in the code yet, and the roadmap step that builds them may still change them.

| Русский | Code | Note |
|---|---|---|
| дружина | `Retinue` | the chief's band of Retainers (D-015; formerly `Warband`) |
| дружинник, герой | `Retainer` | a named fighter with experience, a trait, leadership and one active ability; leads from a squad's front rank, can leave it and join another; under direct control in battle, own AI only on independent tasks (D-010, D-014, D-015, D-019, D-023). In code since M3 step 10: a `Fighter` with a `RetainerPart` (name, trait); alone he takes a `LoneOrder` |
| вождь (клана) | chief | the player's own character, not a Retainer (D-010) |
| отряд | `Squad` | 5–8 fighters, controlled by orders |
| боец | `Fighter` | any combatant; Retainers are fighters too |
| ополчение, ополченец | militia | settlement men who muster on alarm, one squad per settlement; experience new or experienced only (D-007, D-018); in content, men of `new`/`experienced` in kits such as `axe-shield` or `spear-shield` (the `levy` fighter type went in M3 step 9) |
| опыт | `Experience` | `new` / `experienced` / `professional` / `elite` (новичок / опытный / профессионал / элита); professional and elite only for Retainers (D-019); independent of kit (D-022) |
| снаряжение | kit | weapon, shield, body armour, head cover (`KitDef`, named by what it holds, e.g. `sword-shield-mail`); independent of experience, may differ within a squad (D-022, M3 step 9) |
| черта (характера) | `TraitDef` | proud, cautious, greedy, loyal, bold, cruel (гордый, осторожный, жадный, верный, напористый, жестокий): each gives one active ability and shifts AI decisions (D-025) |
| способность | `AbilityDef` | a Retainer's active ability from his trait, cooldown in seconds (D-023); used by `UseAbility`; a lasting one is an `AbilityEffect` on his squad (M3 step 11) |
| стояние | standoff | morale pressure before contact; nobody strikes until the first blow (D-009) |
| поединок / вызов / требование | duel / challenge / demand | a Retainer's one-on-one fight before the troops; the call to it; the terms put to the enemy (D-009) |
| правитель | ruler | the local ruler, main enemy of the удел; his warriors are his Retainers (D-007, D-020) |
| разбойники | bandits | enemy kind that fights for loot (D-024) |
| строй | `Formation` | slot layout for a squad |
| стена щитов | `ShieldWall` | formation + stance |
| приказ | `Order` | squad-level intent inside the sim |
| команда (ввод) | `Command` | external input stamped with a tick |
| держать / наступать / преследовать | `Hold` / `Advance` / `Pursue` | squad stances, `SquadStance` (M4 step 1): fight what comes / go at enemies within 15 m / also chase the runners |
| ИИ противника | `AiSystem`, `SquadAiState` | the enemy AI (M4 step 2): a scenario's `ai` sides; per squad `Idle`, `Advance` (round a flank), `Engage`, `Hold`, `Regroup`, `Withdraw`; rules in `AiDef` (`ai.json`) |
| уйти с поля | `Leaving`, `MoraleState.Left` | a squad marching off the field in order (the AI's withdrawal); its men leave alive at the edge — not a rout (`Fled`) |
| мораль | `Morale` | per fighter, 0..1000 |
| сплочённость | `Cohesion` | squad-level, from leadership and bonds |
| усталость | `Fatigue` | |
| ранение | `Wound` | Light / Heavy / Down (`WoundLevel`); Down = out of the fight, life or death settled after the battle |
| бегство | `Rout` | |
| лидерство | `Leadership` | a Retainer's passive morale bonus to his squad, stronger with experience (D-023): `ExperienceDef.Leadership` ‰ cuts its men's morale losses and speeds recovery (M3 step 10) |
| побратим / соперник | `SwornBrother` / `Rival` | relationship tags (morale modifiers) |
| колдун | `Sorcerer` | enemy leader; source of `Fear` (no real magic) |
| князь | `Knez` | the town's prince (campaign, later) |
| воевода | `Voivode` | Retainer left to run the homestead (campaign, later) |

## Setting and content rules

Real geography, fictional names and polities. Names are stylised after Old Norse, Baltic and Slavic speech of the
era but never copy real historical people or places; no modern ethnonyms, flags or symbols. Every people is shown
with respect. There is no real magic — the "sorcerer" is a charismatic leader whose power is belief and fear.
Store-facing text says "inspired by the Viking-age Baltic"; never "history of Rus".
