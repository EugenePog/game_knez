# Roadmap

Principle: **prove the battle is fun before investing in art and campaign.** Each milestone ends with a checkpoint;
don't start the next one until it passes. Update the "Current" marker when a milestone closes.

**Current: M3 — Combat, wounds, morale, Retainers** (steps 1–5 built; steps 6–11 planned 2026-11-24).

## What the design changed for phase 1 (2026-11-24)

The game design in `docs/design/` (decision log D-001…D-026) settles how the battle should work beyond what M3–M5 had
planned. Only its battle parts are phase-1 work; the campaign (удел, economy, settlements, caravans) stays on paper
(AGENTS.md). D-021 puts every battle mechanic and the enemy's battle AI into phase 1, standoff and duel included, so
phase 1 grows by M5 and the 6–8 week estimate no longer holds.

| Decision | What it means for the skirmish | Where |
|---|---|---|
| D-015, D-010 | hero = дружинник = **Retainer**; дружина = **Retinue**; the player's chief is a separate character | M3.6, glossary |
| D-022, D-018 | no fighter types: **experience** and **kit** are independent; weapons differ within a squad; a weapon sets attack speed, reach and penetration | M3.9 |
| D-019, D-014, D-023, D-025 | Retainers lead from the front rank of any squad, leave it and join another, are under direct control; passive leadership grows with experience; one active ability from one of six traits, cooldown in seconds | M3.10–11 |
| D-024, D-020 | enemy behaviour = who it is (militia, the ruler's Retainers, bandits) + the leader's trait | M4 |
| D-009, D-021 | the standoff (morale pressure before contact, step forward, demand) and duels | M5 |
| D-026 | today's squad-vs-squad fight is the balance reference; targets for a 15–20 a side battle; every balance number in content defs | M3.7, M6 |

Superseded by the design: M3 step 6's "2–3 abilities per hero" (now leadership + one trait ability), M3 step 7's
"tune to about a minute" (D-026: today's squad fight is the reference), fatigue as a planned step (the design doesn't
ask for it — see open question 2), and M4's generic "advance, hold, flank, regroup" AI (now goals by enemy kind and
trait).

## Phase 1 — internal 2D skirmish vs AI

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
   ✅ 2026-11-20: 63 tests green (Debug; 62 in Release), golden final hash recorded; `Fighter` is minimal (id, side,
   position, alive). First `sim-reviewer` pass found 13 issues (1 major: public state write surface) — all fixed.
3. CI: GitHub Actions running build, tests and the purity check on every push.
   ✅ 2026-11-20: `.github/workflows/sim.yml` — Linux x64, Debug + Release tests, purity check, CLI smoke test.
   Doubles as the cross-architecture determinism check (golden hash recorded on ARM64 macOS).
4. CLI skeleton: `run` on an empty scenario prints tick count and final hash.
   ✅ 2026-11-20: `run [--seed N] [--ticks N] [--json]`, exit codes 0/1/2, 9 tests.

**Checkpoint:** `dotnet test` green on a clean clone; purity check clean; an agent took one small sim feature from
spec to merged commit using the `sim-feature` playbook and `sim-reviewer` without manual fixes. This checks how
the "you + LLM agents" loop works — write down what went wrong in `docs/playtests/` or the ADR log.

Checkpoint log (M1 step 1 as the feature, 2026-11-20): the playbook loop worked — test first, purity hook, reviewer,
fixes by the same agent, no manual fixes needed so far. What went wrong: hand-computed expectations in new tests (error
columns) were off and had to be corrected from real output; the reviewer caught real gaps the tests didn't (hash
sections not checked field by field, interpolated sine values not pinned, CoreCLR-only message trimming); and the
"M0 steps 3 and 4" commit left `.github/` out, so CI has **not run on GitHub yet** — "green on a clean clone" is
still open until the workflow is pushed and passes.

### M1. Movement and formations — main technical risk (weeks 2–4)
Grid map, A*, squad anchor + formation slots (Line, ShieldWall, Column), slot assignment, steering, separation,
collision resolution; benchmark scenarios S1–S5; CLI `--frames` + HTML replay viewer; `bench` command.

Steps — one change each, each ending with build/tests/purity green and a `sim-reviewer` pass:
1. Scenarios and squads: `Knez.Sim.Content` (own JSON reader, exact decimal → `Fix`), fighter/formation/map/scenario
   defs, `Angle` + sine table, squads spawned in their formation slots, content fingerprint in the hash,
   CLI `run <scenario>`. Doubles as the M0 checkpoint feature.
   ✅ 2026-11-20: ADR-0004; `content/scenarios/formations.json` (5 squads, 35 fighters); 232 tests (Debug), golden
   hashes re-recorded on purpose (state format grew). `sim-reviewer`: no blockers or majors, 5 minor findings + nits —
   all fixed except two deferred on purpose (full def catalogue → before step 7; `ToWorld` overload with a precomputed
   forward vector → step 8, when the bench says speed matters — 0.027 ms/tick for 35 fighters at step 3).
2. Frames and the replay viewer: CLI `--frames`, `tools/replay-viewer/` (circles, facing, squad colours, anchors), so
   every later step is checked by eye.
   ✅ 2026-11-20: `knez-frames` v1 (battle-sim.md §17), crash-safe and streamed; vanilla canvas viewer with playback,
   scrub, zoom/pan, hover, events and `window.knezViewer` for agents; checked in the browser pane on the formations
   scenario and on synthetic moving/faulted files. 246 tests. `sim-reviewer`: 1 major (a crash left an unreadable
   file) + minors — all fixed.
3. Moving a squad: `MoveSquad` command, the anchor walks straight to the target and turns, fighters seek and arrive at
   their slots, facing follows movement (`Atan2`). No collisions yet.
   Agreed 2026-11-20: walk 1.4 / run 3.5 m/s; turn rate column 90, line 45, shield wall 30 °/s; turn in place until
   within 30°, then march; no facing given → face the march direction.
   ✅ 2026-11-20: `Atan2`, `MoveSquad` (+ `off-map`), squad orders, slot following with the slot's own motion, facing
   rules; scenario `script` for timed orders (added here: the CLI takes no commands and the S2–S4 benches need it);
   `content/scenarios/march.json`; orders drawn in the viewer. 308 tests, a march golden. `sim-reviewer`: 1 major
   (outer fighters of a turning squad faced backwards) + minors — all fixed. Known look: squads pivot around the
   front-rank centre, so a column's tail swings wide in an about-turn → revisit with wheeling in step 7.
4. Crowding: spatial index, separation steering, circle–circle collision relaxation; overlap metric.
   Agreed 2026-11-20: everyone is solid (allies jostle past); squads march on (no waiting for stragglers); a standing
   squad gives way less (`holdWeight`, placeholder 3).
   ✅ 2026-11-20: separation, slide along bodies, relaxation by weight (6 passes), map edge (per pair, slots aimed at
   the edge), overlap metric in the CLI and viewer; `content/scenarios/crowding.json` and the test scenarios (crossing,
   a push four men deep, bodies against the edge) at 0 % overlap. No spatial index yet (all pairs; ~60 fighters max in
   phase 1) — revisit at step 8. Found while building: the slide must come after the run-speed cap, and "holding" must
   mean the man is near his slot, not just that his squad has no order. `sim-reviewer`: 3 majors (deep pushes, the map
   edge, a golden that only saw the settled end) — fixed. For the bench / M3: marchers stay blocked in front of a
   holding line; a push several squads deep beats `holdWeight` (~12 m); squads ordered onto clashing slots shuffle
   ~1 cm/tick forever.
   Decided 2026-11-21 after watching the replay: **allies open ranks** — a holding squad lets other squads of its side
   that are on orders pass through at 1/3 of walking speed (`allyPassSpeed`); enemies stay solid (they stack; fighting
   starts in M3). Follow-up decision after review: only men on orders pass, and a move order ends only when the squad is
   formed up (every man within 30 cm of his slot), so arriving squads finish passing and men shoved by enemies stay
   solid against allied reserves. A second review pass found the remaining holes, now closed: passing is remembered
   per man (`PassingThrough`, starts only when he means to walk into an allied holder, so a line shoved back into its
   reserve stays solid), the gentle parting of deep allied overlaps is limited to different squads (a deep push no
   longer collapses a squad's own ranks), and orders aimed so the formation would hang off the map are moved inward
   so they can end. Checked on 19 edge scenarios; `run` now also prints the share of fighter-ticks spent passing
   through allied ranks. This removes the blocked-marchers jam between allies; against enemies it stays until M3.
5. Terrain: walkable grid, circle–grid clamping, wall avoidance (decide the cell size).
   Decided 2026-11-21: walls are rectangles; an order into a wall goes to the nearest open ground; men whose slots are in
   a wall stand as close as they can. Built 2026-11-21: exact circle–rectangle collision and sliding, `walls.json`, a gate
   golden; the grid and cell size moved to step 6 (pathfinding). Reviewed by `sim-reviewer`: leaving a wall could land
   off the map or inside a neighbouring wall, and the formed-up check counted slots no body fits on (on a face, in a
   gap narrower than a body) and sent men to the far side of a palisade. Fixed: only open exits count, slots leave on
   the squad's side, formed-up skips slots without room; at most 64 walls, speeds at most 8 m/s and under
   radius + 5 cm per tick. 377 tests.
6. Pathfinding: A* for squad anchors with a per-tick budget, path following. First decision: the grid's cell size
   (0.5 m or 1 m), moved here from step 5.
   Decided 2026-11-21: 0.5 m cells; ground narrower than the formation costs 3× (prefer wide routes, squeeze when the
   detour is long); an unreachable target becomes the nearest reachable spot. Built 2026-11-21: clearance grid, A*
   with a 4 000-node budget per tick (first come first served), corner cutting and straightening, route following,
   routes in the frames and the viewer, `paths.json`. Goldens re-recorded: the hash now covers routes (open-ground
   motion verified unchanged); the gate trace also changes because the line now plans its way through. Reviewed by `sim-reviewer`: a route's first or last leg (anchor or target
   to the nearest cell centre) could cross a wall thinner than a cell, and the squad then never formed up. Fixed with an
   exact segment-through-wall check on those legs, plus a corner-room rule for diagonal steps; the grid is now built
   when the simulation is created. 396 tests.
   Changed 2026-11-21 after playing with it: no 3× cost — before a narrow gap the squad regroups into fewer files a
   little ahead of it, waits for its men, goes through and widens again, and the route search charges only the extra
   depth that costs; an unreachable target becomes the near side of the obstacle, not the geometrically nearest spot;
   every movement rule that shapes feel moved from code into `content/defs/movement.json`. Goldens re-recorded (the
   fingerprint now includes the movement rules; with the new fields left out, every trace but the gate's is unchanged,
   and the gate's line now regroups into 3 files). Reviewed again by `sim-reviewer`: narrowed slots could lie in walls
   (the route ran beside a gap's middle line, the squad widened with its rear still in the gap, the straight-line
   shortcut took squads into gaps full width), and narrowed files came out uneven. Fixed: narrowness is now the
   narrowest corridor across a cell (a wall alongside no longer counts), routes are shifted sideways to keep the
   formation clear (centred in tight gaps), widening waits for the rear, files are balanced, the squad regroups facing
   the gap, and the code's default movement rules now equal `movement.json` exactly (goldens re-recorded for that and
   for the gate). A third review found shifted runs could meet at a corner behind the squad (it pivoted about and
   back) and odd men unevenly spread for 9+ men; fixed (doubling-back waypoints dropped, odd men round-robin across
   ranks), with a seeded random-map sweep and a multi-start test pinning it. 435 tests.
7. Formations in motion: `SetFormation` (Line, ShieldWall, Column), wheeling, slot reassignment after losses.
   First: `ScenarioDef` carries and fingerprints the whole def catalogue (battle-sim.md §7).
   Decided 2026-11-21: wheel round the inner flank at route corners, face about for more than a quarter turn; re-form
   on the move at half pace; losses keep the front and thin the rear. Built 2026-11-21: formation catalogue in the
   scenario (fingerprinted), `SetFormation` (+ scripted `formation` orders, `unknown-formation`), `Frontage` and
   ranks closing from the back in `CleanupSystem`, nearest-man slot assignment on re-forming and facing about, tangent
   wheels at corners, `reformPace` in `movement.json`, squad/slot and formation columns in the frames,
   `content/scenarios/drill.json`, a drill golden. Other goldens re-recorded: the hash gained frontage, re-forming
   and the catalogue; with those left out every old golden matched (their squads never turn more than 30°).
   Reviewed by `sim-reviewer`: a wheel or face-about could carry the anchor into a wall (57 of 160 near-wall cases) and
   a face-about on arrival left the front a depth past the ordered point. Decided 2026-11-21: the front lands on the
   ordered point (the route runs a depth past it and the squad faces about there; a target just behind: face about and
   step back, `stepBackPace`). Fixed: wheels and face-abouts only on clear ground (0 of 160), `FitInside` uses the real
   frontage, single-rank squads skip a waypoint just behind them; tests for arrival, re-issue, step back, the wall
   repro, a right wheel, random losses and formation changes while waiting or regrouping. Goldens re-recorded for the
   fingerprint only (`stepBackPace`). 456 tests.
8. Bench: scenarios S1–S5, metrics, `bench` and `batch`; tune; decide steering vs ORCA; check the exit criteria.
   ✅ 2026-11-21: `content/scenarios/bench/` S1–S5, `MovementMeter` (stuck, engaged, slot error, overlap, passing,
   in walls), `batch` and `bench` in the CLI, `BenchTests` pinning the criteria. Reviewed by `sim-reviewer`: S1 wasn't
   a real clash and p99 measured JIT warm-up; rebuilt S1 head-on (files lined up), made S2–S5's second squads really
   meet, warm-up ≥ 1 s, engaged time and open orders reported beside stuck. Decided 2026-11-21: steering stays (no
   ORCA); marching enemies slipping round each other is left for M3; a squad in contact is engaged as a whole; fighting
   distance = body + 0.5 m (M3's default reach); body radius 0.3 m kept. Results: 0 stuck, 0.00 % overlap in all five;
   p99 0.063 ms for 50 fighters (S1). The leftover "for the bench" items are moved to M3 (pushing) and M2 playtests
   (column wheels, re-forming round obstacles). Remaining M1 criterion: "looks right in the viewer" — the user's call.

**Checkpoint:** M1 exit criteria in `battle-sim.md` §10 (no stuck > 2 s, overlap < 1 %, p99 < 2 ms for 50 fighters,
looks right in the viewer). If it fails, fix movement before anything else.

### M2. Playable 2D view (weeks 4–5)
Unity 6 project in `game/`, sim as local package, top-down 2D: circles, facing, selection, order arrows, morale bars,
pause/speed, issuing commands with the mouse. EditMode test that replays the golden replays inside Unity
(cross-runtime determinism).

Steps (planned 2026-11-21; each ends with the project compiling in batch mode and its tests green):

1. **Setup (the user, in the editor):** Unity Hub + Unity 6 LTS; a new 2D project created as `game/` in this repo;
   `game/Packages/manifest.json` gains `"com.knez.sim": "file:../../sim/src/Knez.Sim"` and
   `"com.knez.sim.content": "file:../../sim/src/Knez.Sim.Content"`; Unity's `.gitignore` entries; first commit with
   `.meta` files. Agent: check the packages compile in Unity (C# 9, nullable, no CS8632), fix what Unity rejects.
   ✅ 2026-11-22: Unity 6000.6.10f1 (Apple Silicon), 2D URP template; `Knez.Sim` and `Knez.Sim.Content` compile in
   Unity with no errors or warnings (no CS8632), their `.meta` files committed; generated `*.csproj`/`*.slnx` ignored.
2. **Assemblies and the sim loop:** `Knez.Game.View`, `Knez.Game.Input`, `Knez.Game.Tests` asmdefs; a `BattleRunner`
   MonoBehaviour that loads a scenario from StreamingAssets (content text → `DefLibrary.Parse` / `ScenarioLoader.Parse`),
   steps the sim at 20 ticks/s from `Update` with an accumulator, pause and ×0.5–×4 speed.
   ✅ 2026-11-22: `Assets/Knez/View` (`Knez.Game.View`: `SimClock` — 20 ticks/s, pause, ×0.25–×4, at most 8 ticks a
   frame and the backlog dropped after a hitch; `ContentFiles` — the repo's `content/` in the editor, StreamingAssets
   in builds; `BattleRunner` — loads a scenario, steps it from `Update`, stops on a sim fault) and `Assets/Knez/Tests`
   (`Knez.Game.Tests`, EditMode: the clock, and drill.json loading and running to the end inside Unity). 6/6 green in
   batch mode, no compile warnings. `Knez.Game.Input` waits for step 5.
3. **Cross-runtime determinism:** an EditMode test that runs the golden scenarios in Unity (Mono) and compares the
   golden hashes and traces with the dotnet values; later the same under IL2CPP.
   ✅ 2026-11-22: `sim/tests/Golden/content-traces.json` — every content scenario (11), seed 1, 600 ticks: per-tick hash
   trace and final hash. Checked by `ContentTraceGoldenTests` (.NET) and `CrossRuntimeGoldenTests` (Unity EditMode,
   Mono): all 11 match bit for bit. IL2CPP comes with the first player build.
4. **Drawing:** top-down 2D: walls, fighters as circles with a facing tick in squad colours, squad anchors and facing
   arrows, planned routes, interpolation between ticks (`Fix` → `float` only in the view).
   ✅ 2026-11-22: `BattleView` (walls, ground, fighters as discs in squad colours with a facing tick, grey when fallen,
   anchor bars along the front, routes ahead as lines; drawing runs one tick behind, interpolated by the clock's alpha,
   the runner raising `Ticked` after every tick), `FixViewExtensions`, `ShapeSprites` (runtime shapes, no art). The
   camera is fitted to the map. `BattleViewTests` render drill, walls and S1 to `artifacts/unity/*.png` in batch mode
   (with graphics); 22/22 Unity tests green.
5. **Input:** select squads (click, box), right-click to move (drag for facing), formation hotkeys (`SetFormation`),
   pause/speed keys, a camera that pans and zooms; every action becomes a command for the next tick.
   ✅ 2026-11-22: `Assets/Knez/Input` (`Knez.Game.Input`): `SquadPicker` (click within a body + 0.4 m, box select,
   own side only), `OrderBuilder` (click: move, facing the march; drag: face the drag; several squads side by side
   across the facing, 2 m apart), `BattleInput` (left click/drag select, Shift adds, Esc clears; right click/drag
   orders; 1/2/3 line/shield wall/column via `SetFormation`; Space pause, =/− speed; WASD/arrows/middle-drag pan, scroll
   zoom); the view highlights selected squads. `InputTests`: picking, gestures, and two squads ordered together
   arriving side by side. 26/26 Unity tests green.
6. **HUD:** squad list with formation and state, order arrows, rejected-command feedback; morale bars as placeholders
   until M3.
   ✅ 2026-11-22: `BattleHud` (immediate-mode GUI, no scene set-up): scenario, tick and time, speed or PAUSED; each
   squad's name, formation, men left of spawned and state (`HudText.Status`: holding, marching, planning a route,
   regrouping into N files, re-forming); a grey morale bar marked as M3's; refused orders in plain words naming the
   squad, fading after 4 s; a line of controls. `HudTextTests`; 28/28 Unity tests green.
   ✅ **M2 checkpoint met 2026-11-22:** the user moved squads, changed formations and paused in the editor; golden
   traces match in Unity. Found in that check and fixed: Unity's "fake null" objects (destroyed cached sprites across
   Play runs with domain reload off; an empty Inspector field) slipped past `??` — the view now uses Unity's `==`.

**Checkpoint:** you can move squads, change formations and pause; golden replays match in Unity.

### M3. Combat, wounds, morale, Retainers
Melee actions with timings, hit resolution, flanks/rear, shield wall, wounds, morale states, rout and rally; then
experience and kit apart (D-022) and Retainers with leadership and one trait ability each (D-019, D-023, D-025).
Morale reasons shown in the view and the battle log.

Steps 1–5 (planned 2026-11-22; decisions in battle-sim.md §11–§12):

1. ✅ **Combat content:** `content/defs/combat.json` — weapon types (reach, action ticks, wound rows), the hit matrix
   (weapon × experience), protection (shield, body armour, head cover), experience levels, warrior types; fighters gain
   weapon, armour, experience and type. Loaded, validated, fingerprinted.
2. ✅ **Engagement and melee lock:** reach, engaged men hold, engaged squads stop; the bench's "engaged" uses each man's
   weapon reach; withdrawing from melee only backward, facing the enemy, until clear by reach + 3 m (decided 2026-11-22).
3. ✅ **Attacks and blows:** action timings, target choice, the get-through roll (Combat RNG), flank/rear/shield wall.
4. ✅ **Wounds and falling:** Light/Heavy/Down, adding up, penalties; the fallen leave the fight (ranks close); targets
   that fell are cleared the same tick.
5. ✅ **Morale:** states with hysteresis, inputs (losses, flank/rear, routing neighbours, wounds), reason codes; rout
   and rally; a squad breaks.

Built after step 5 (2026-11-23…24, from playing in Unity): engaged men turn to face their enemy; **closing in** (rear
ranks step round their front men, a longer line wraps a shorter one); the shield wall **keeps its ranks**; sandbox
scenarios (`content/scenarios/sandbox/`); the user's weapon retune (sword 0.7 + 1.2 s, axe 0.9 + 1.5, spear 0.8 + 2.0
with 1.5 m reach, seax 0.5 + 0.8 — now also the code's built-in standard); the **soft wheel** (`turnLagTime` 4 s: men
lag behind turning slots and dress over ~4 s); **session files** — Unity writes every applied order to
`artifacts/sessions/` as a scenario that replays headless with the CLI (battle-sim.md §8). 669 tests.

Steps 6–11 (re-planned 2026-11-24 from `docs/design/`, see "What the design changed" above). Each step starts with its
battle-sim.md section — the contract — and the user's yes on the numbers that define feel; then test first, build,
`sim-reviewer`, and a look in Unity.

6. **Terms and the contract (docs only).** Hero → Retainer, Warband → Retinue in battle-sim.md, skills and code
   comments (D-015; the code has no hero type yet, so nothing is renamed in logic). battle-sim.md §6–§7 and §11 for
   D-022 (a fighter is a body + experience + kit; mixed squads; weapons set speed, reach and penetration) and
   ADR-0005 for that content-format change; §11's opening rewritten for D-026 (today's squad-vs-squad fight is the
   reference, no longer "tune to about a minute"); §13 and §14 replaced by short summaries of D-019/D-023/D-025 and
   D-024 that point to the steps below. Skills `unit-design` and `sim-feature` lose "warrior types" and "heroes".
   Done when: an agent following `sim-feature` finds every phase-1 rule's home in the spec, with open numbers marked
   as proposals.
   ✅ 2026-11-25: battle-sim.md v0.9 — Retainer terms; "Planned" contracts in §7 (body + experience + kit, ADR-0005,
   Proposed until step 9 starts), §8 (commands with rejection reasons), §11 (D-026 reference and targets, battle
   phases), §12 (standoff pressure), §13 (Retainers: place in a squad, leadership, six abilities, duel), §14 (enemy AI
   by kind and trait), §15 (each planned field's hash section). Skills `unit-design`, `game-design`, `sim-feature`,
   `economy-design` and two design agents updated; code comments only. `sim-reviewer`: no wrong claims about the
   built code beyond four slips (fixed); 3 gaps in the contracts — value-only state with hash sections, a Retainer's
   slot/ids/`StartCount`, re-pinning the veteran scenarios — closed in the spec. 669 tests green.
7. **Pin the reference fight (D-026), before anything changes balance.** Bench S6 (8 vs 8 head-on) and S7 (the same,
   flanked); `batch` reports the D-026 measures: battle length, how it ended (rout / annihilation / time), share of
   annihilations, win rate per side, losses at the break. Record today's numbers for S6, S7, `m3-*` and the sandbox
   over 50 seeds and pin them in a `Slow` test with tolerances, so later steps can't drift them unnoticed.
   Done when: the reference table is in battle-sim.md §11 and the test is green.
   ✅ 2026-11-25: `bench/s6-lines` (mirror image) and `bench/s7-flank` (its flankers strike ~3 s after the lock);
   `batch` prints the D-026 measures (`OutcomeMeter`, `OutcomeSummary` in the CLI). Seeds: more than 50 — at 50 a win
   rate carries ±7 points of noise — so S6 4000 (gated at ±4 points and inside the D-026 band 45–55 %), the other small
   fights 1000 (±8 on an even win share: catches a lurch, not a nudge), the sandbox 50 (a smoke check). Pinned in
   `sim/tests/Golden/balance-reference.json` by `BalanceReferenceTests`, tolerance 5 standard errors or a floor; Release
   only (15 s; Debug skips the pin and runs 4 seeds of each under its overflow checks). `m3-lines` is left out: it is the
   same battle as S6; `villages` has no script. Measured: S6 51.1 / 48.7 %, decided 22.2 s, a squad breaks at 25 % lost,
   no annihilations anywhere; S7 98.8 % and 6 s sooner, but its flanked line breaks at ~24 % lost — open question below
   §11's table; the sandbox is 74 % undecided at 3 minutes (no AI yet). `sim-reviewer`: meter, summary and parallel
   runs correct; the gate was looser than the docs said and a seed-count mismatch went inconclusive — both fixed, with
   the rest of its findings. 699 tests green in Release; in Debug 691 pass and the 9 pins skip.
8. **View and log for what exists** (was M3 step 7): blows, wounds and the fallen in Unity; real morale bars per squad
   and per man; why a squad broke, in words (reason codes → text); a scrolling battle log; a squad's composition.
   Done when: the user can tell from Unity alone why a squad broke.
   ✅ 2026-11-25 (the user played it: fine as built): `SquadBroke` carries its counts (`InFight`, `Running`; events
   aren't hashed, so no golden moved); view-side `BattleRecord` — a morale ledger per man by reason, and the battle log;
   each squad's panel shows who it started with, one morale cell per man, "N of M running · breaks at K", what cost it
   morale and, once broken, why in words; a morale bar over every man (B); the log (L); the hovered man's own account;
   clicks and the wheel over the HUD stay off the map. battle-sim.md §16. Unity batch tests not run (editor open):
   view logic tested under .NET (12 tests), view typechecked against Unity's assemblies. `sim-reviewer`: the sim
   change is right and moves nothing; fixed its view findings — the ledger takes a whole tick before re-form resets
   (a squad breaking nearby in the same tick was counted twice), the log hides only the men swept along by a break.
9. **Experience and kit apart (D-022, D-018).** `fighters.json` keeps bodies only (radius, speeds, turn rate);
   `combat.json` gains named **kits** (weapon, shield, body armour, head cover) and loses `warriorTypes` (the
   spearman's +30 becomes the spear's own defence; `mounted` waits for riders). Scenario squads list their men in
   groups — `{ "count": 3, "experience": "new", "kit": "spear-shield" }` — so one squad mixes weapons and experience.
   Weapons gain **penetration** (a share of body armour and helm ignored). Old ids map 1:1 (warrior → experienced +
   `sword-padded`, spearman → experienced + `spear-padded`, levy → new + `axe-shield`, veteran → elite +
   `sword-mail`); every scenario is rewritten and goldens re-recorded, verified first with penetration 0 (results
   equal step 7's reference). Unity HUD and the replay viewer show composition; session files written from then on
   keep working, older ones no longer load (ADR-0005).
   Decide first: penetration numbers; whether experience also shortens recovery (D-022 "buffs to the weapon's
   characteristics" — today it raises the hit chance and defence only); the phase-1 kit list.
   ✅ 2026-11-25. Decided by the user: penetration axe 300, spear 200, seax 100, sword 0 (the milder set); experience
   doesn't shorten recovery yet — measure "one level ahead" first (M6); seven kits, each named for all it holds and
   each with a round shield: `axe-shield`, `spear-shield`, `axe-shield-padded`, `spear-shield-padded`,
   `sword-shield-padded`, `sword-shield-mail`, `axe-shield-mail` (so the conversion is warrior → experienced +
   `sword-shield-padded`, spearman → experienced + `spear-shield-padded`, levy → new + `axe-shield`, veteran → elite +
   `sword-shield-mail`). Built: `BodyDef` (`bodies.json`), `KitDef`, `SquadMenDef`, weapon `penetration` and
   `defence`; squads list `men` groups; old-format files fail with a message saying where the parts went; frames carry
   `experience` and `kit`; the replay viewer's hover and the Unity HUD show them (a squad's groups, a man's kit and
   experience). Proof the conversion changed nothing: with penetration 0, every content scenario × 60 seeds × 60 s
   played identically, man by man. Penetration then moved `m3-spears` (spearmen 60 → 72 %), `m3-veterans`,
   `m3-rally` and the sandbox — re-pinned on purpose; S6, S7 and the rest are byte-identical in the reference. Goldens
   re-recorded: the state hash names body, experience and kit ids. ADR-0005 accepted.
10. **Retainers (D-019, D-014, D-023 passive).** A `Retainer` part on a fighter: trait, leadership. Professional and
    elite only for Retainers (content validation). Joins any squad of his side in its **front rank** and leaves it to
    stand alone (`AttachRetainer`, `DetachRetainer`); alone he is under direct control (move, attack a target).
    **Leadership** (passive, stronger with experience; proposal: his squad loses less morale and recovers faster
    while he is in it, his fall hits the squad hard, men rally near him). A lone Retainer's own morale rule. Slots,
    ids and `StartCount` on joining and leaving as battle-sim.md §13 proposes. Unity: Retainers drawn apart,
    selectable, attach by dragging onto a squad. Scenarios with elite squads (`m3-veterans`, `m3-rally`, the two
    sandbox ones) turn them into experienced men led by Retainers, and their step-7 reference is re-pinned on purpose.
    Decide first: leadership numbers per experience level; can a Retainer rout; does his fall alone break a squad.
    Done when: a squad with a Retainer measurably holds longer (batch), and the user moves Retainers between squads.
    ✅ Built 2026-11-26 (the user's play in Unity still to come). Decided by the user: leadership 100 / 150 / 200 ‰ for
    experienced / professional / elite (losses cut by that share, recovery faster by the same); a Retainer can rout,
    guarded by his leadership — in a squad he runs and rallies with it, alone he rallies where he stopped; his fall
    costs his squad as much as a mate's (`leaderFell` 1 — built at 2, "as two mates", and lowered after S8 below); he
    counts as one of its men for the break share. Built: `RetainerPart` (name, trait), `TraitDef` (`retainers.json`,
    ids only), experience `leadership` and `retainerOnly`, scenario `retainers[]`, the four commands and their script
    entries, `SquadMembership` (front-rank slot nearest him, `StartCount` ± 1), lone orders (he walks straight, sliding
    along walls), reason `LeaderFell`, frames `retainer`/`trait`, replay viewer gold ring. Unity: gold ring, drag onto a
    squad to join it or onto ground to leave and walk, click to select and right-click to walk / attack / join, X to
    leave; HUD names Retainers in their squad's panel and lists those standing alone. **Measured** — bench S8
    (`bench/s8-leader.json`: S6 with one experienced Retainer in the Hird line), 2000 seeds against S6's 50.3 %: with
    `leaderFell` 2 the led line won 45.9 % (his fall outweighed his leadership), with 1 52.1 %; a professional leader
    66 %, an elite one 82 % — mostly the experience ladder (open question 10), not leadership (open question 11).
    Battles without a Retainer play identically (18 scenarios × 60 seeds); `m3-veterans` (now 5 experienced + elite
    Hrafn, 93.6 %), `m3-rally`, the sandbox and S8 re-pinned on purpose. The sim review's fixes: a Retainer taken out of
    his squad while rallying rallies where he stands; a lone move ends only at the exact ordered facing; small pushes
    don't turn a Retainer standing idle; his lone attack is slowed by wounds; a side of Retainers alone counts in the
    victory check; a squad Retainers join at spawn must fit laid out for all its men; `m3/m3-retainers` puts lone
    orders into the cross-runtime goldens. Not built: men rallying sooner near him, his own route planning, allies'
    ranks opening for him, keeping him in the front rank when the squad re-slots.
11. **Trait abilities (D-023, D-025).** One active ability per Retainer from his trait; strength, duration, radius
    and a cooldown in seconds in content (`retainers.json`); a `UseAbility` command, an `AbilityUsed` event, buttons
    with cooldowns in Unity. Works in the standoff too (M5). First the three that only change numbers: **battle cry**
    (proud: own morale up, nearby enemies' down), **close ranks** (cautious: less damage from the front, slower),
    **stand to the death** (loyal: the squad's morale can't fall for a few seconds). Then **front-rank rush** (bold:
    the front rank strikes forward harder, then the formation is disordered for a while), **promise loot** (greedy:
    morale and push up for a while; the bigger share after the battle is campaign), **no quarter** (cruel: more of the
    fallen are killed — needs a killed/down tally in the battle result, which D-017 will count later — and enemy morale
    near the squad drops faster).
    Done when (D-026): in an equal fight each ability shifts the win rate noticeably but never decides it alone —
    measured with `battle-analyst` against step 7's reference.
    ✅ Built 2026-11-26 (the user's play in Unity still to come), as the user scoped it: all six abilities are defined
    (`AbilityDef` on each trait in `retainers.json`, so the AI can weigh them), and the three that only change numbers
    are built; promise loot, front-rank rush and no quarter are refused as `not-built` and come one by one after testing
    with the AI. The user's numbers: every cooldown 2 minutes; battle cry +100 to his side and −50 to enemies within
    15 m, at once; close ranks −30 ‰ off frontal blows at his squad and ×0.7 pace for 10 s; stand to the death no morale
    loss for 5 s; a lasting ability stays with the squad if he leaves. Built: `UseAbility` (refusals `not-built`,
    `cooling-down`, `not-in-squad`), `AbilityUsed` event, reasons `BattleCry`/`EnemyBattleCry`, the Retainer's ready tick
    and `Squad.Effects` (hashed), script order `ability`, session files, frames. Unity: Q or the HUD's button uses the
    selected Retainer's ability; the HUD shows each Retainer's ability and cooldown and the effects on each squad; the
    log names who used what; a battle cry shows its reach as a fading gold disc. **Measured** (benches S9–S11, S8 with
    one scripted use; 2000 seeds; S8 without it 52.1 %): battle cry ~60 %, close ranks ~60 %, stand to the death
    59–62 % depending on timing — each shifts an equal fight by 7–10 points and never decides it alone. Pinned in the
    reference (1000 seeds: 59.8 / 59.8 / 61.0 %). No battle without an ability use changed (every scenario × 60 seeds).

**Checkpoint:** a fight can be won by flanking, by breaking morale or by where a Retainer stands, not only by
attrition; the reference fight still measures as pinned.

### M4. Enemy AI (D-024)
Side utility AI + squad FSM inside the sim (deterministic, `Ai` RNG stream, staggered by id), driven by two layers:
**who it is** sets the goal of the fight, **the leader's trait** shifts decisions. No separate AI per enemy.

1. **Attack order and stances, for the player first.** Today a move order stops at its point and closing in reaches
   only 3 m, so a squad can't be told "attack that squad" (one reason the purple squad stood aside in the sandbox). An attack
   order on an enemy squad; stances Hold / Advance / Pursue; pursuit of routers and where it stops. The AI gives the
   same orders.
2. **AI frame:** the scenario says which side the AI plays and each enemy squad's kind and leader; side AI every ~1 s
   scores goals per squad; squad FSM (idle, advance, engage, hold, regroup, withdraw) issues internal orders;
   enemy Retainers use their abilities.
3. **Who it is — goals.** Militia defends its homes: stays near them, breaks early, never pursues. The ruler's
   Retainers break the enemy: keep formation, pursue, challenge to duels (the duel itself is M5). Bandits want loot:
   start from ambush, go for the baggage, leave once it's taken or losses are high. Needs minimal map objects in the
   scenario: a **homes** zone, a **baggage** point (taken by standing on it unopposed), an **ambush** start (enemies
   hidden until close), and **reinforcement** waves (militia gathering).
4. **The leader's trait.** Proud: accepts challenges, doesn't retreat. Cautious: drags the standoff, waits for
   reinforcements. Greedy: goes for the baggage, not the men. Loyal: holds to the end, refuses demands. Bold: strikes
   first, short standoff. Cruel: finishes the fallen. The standoff halves of these come with M5.

**Checkpoint:** the AI wins sometimes against a careless player and loses to a good plan; each enemy kind is
recognisable by how it fights (the user's call, on one scenario per kind).

### M5. Standoff and duel (D-009, D-021)
Spec request: `docs/design/standoff-duel.md`, "Запрос к симуляции". Bloodless power (D-006) is a fight of nerves
before contact.

1. **Battle phases.** A battle not started from an ambush opens with a standoff: squads form up and nobody strikes;
   it ends for everyone at the first blow (the player's attack order, the AI's decision), when a side leaves or when
   its squads break. State: the phase, who struck first. Decide first: what counts as the first blow (a man who walks
   into reach?).
2. **Pressure.** Within standoff distance (proposed 30 m) each man's morale moves every tick, without recovery. Down:
   the visible enemy strength near him (numbers, armour, experience), an enemy shield wall, an enemy step forward, his
   side's lost duel or refused challenge, enemy reinforcements. Holding: his own side's visible strength, a Retainer
   near him, militia near its homes, his side's won duel, reinforcements on the way. A squad that breaks leaves without
   a blow — a bloodless win. Numbers in content.
3. **Step forward and demand.** A step-forward order (presses both sides harder); a demand — phase 1 has only "leave
   the field" (pay and cargo are campaign) — and the AI's answer (accepts when its morale is low and the trait allows).
4. **Duel.** A Retainer challenges the enemy leader or one of his Retainers (militia never duel); the AI accepts by
   trait, strength and how the standoff goes (refusing a weak challenger costs nothing, refusing a strong one hurts its
   own side's morale); terms: first blood or Down; stake (phase 1: "you leave"); the armies stand in a ring and don't
   interfere (interfering is a heavy morale blow); the two fight automatically under normal combat rules (D-014);
   the result swings both sides' morale hard; one duel per standoff; the ruler's Retainers challenge too.
5. **View:** the standoff's state and whose nerve is going and why; challenge and demand buttons and the answer; the
   duel ring and its outcome in the log.

**Checkpoint (D-026):** a standoff lasts 20–60 s; with one side clearly stronger about half of the encounters end
without blood; a duel matters, but one strong Retainer doesn't decide everything; the standoff isn't boring (the
user's call).

### M6. Tuning and playtests (→ exit)
- D-026 targets measured with `battle-analyst` on 15–20 vs 15–20: a battle lasts 2–4 min of real time without
  pauses; it usually ends in a rout, total annihilation in fewer than 1 of 10; equal forces 45–55 %; one experience
  level ahead 65–70 %. Tuning only through content defs.
- 2–3 maps, one per enemy kind: a bandit ambush on a baggage train; a standoff in the field against the ruler's
  Retainers with militia gathering; an attack at a settlement whose militia stands by its homes (from
  `sandbox/villages`).
- Fatigue only if battles miss the length targets or reserves have no use without it (proposal — it was M3 step 6;
  the design doesn't ask for it).
- IL2CPP cross-runtime check with the first player build (left from M2 step 3).
- Playtest log (`playtest-notes`) toward 20 sessions.

**Exit criterion for phase 1:** the battle is still interesting the 20th time. Not shown publicly.

### Open questions for phase 1
Settle each before the step that needs it; the proposal is the default if the user agrees.

1. Is the player's chief (вождь, D-010) on the field in phase 1? Proposal: no — Retainers fight and duel; the chief
   comes with the campaign. (M3 step 10, M5 step 4)
2. Fatigue: drop from M3 and build only if tuning needs it? Proposal: yes. (M6)
3. Penetration numbers, and does experience also shorten recovery? **Answered 2026-11-25:** axe 300, spear 200, seax
   100, sword 0; recovery not yet — measure "one level ahead" first. (M3 step 9, done)
4. Phase-1 roster: how many Retainers per side? Proposal: the player 2–3 (2 squads + Retainers ≈ 15–20 men, D-026),
   the enemy leader + 1–2 Retainers. (M3 step 10)
5. Map objects for enemy goals — homes zone, baggage, ambush, reinforcement waves — kept minimal, no settlement
   simulation? Proposal: yes. (M4 step 3)
6. Militia kit "by chance" (D-018): random at battle start or listed in the scenario? Proposal: listed; random
   generation comes with the campaign. (M3 step 9: built as proposed — a scenario lists each group's kit)
7. Demand in phase 1: only "leave the field"? Proposal: yes. (M5 step 3)
8. Ranged weapons stay out of phase 1 (open-questions: deferred; idea — a second weapon for some militia and
   Retainers).
9. Flank and morale (found in M3 step 7). **Answered 2026-11-25** (battle-sim.md §11): sooner in time *and* with fewer
   attacker losses, chiefly because a flank or rear attack drains the squad's morale faster. Measured on 1000 seeds each
   (S7 = the flanked raider line; S6 = the equal head-on fight, which must stay even):

   | Variant | S7 first blow → decided | S7 flanked line breaks at | S7 attackers down | S6 wins | S6 breaks at |
   |---|---|---|---|---|---|
   | today (`flankBlow` 20, `rearBlow` 40, mates 5/10 per blow) | 9.6 s | 23.7 % lost | 3.7 % | 51 / 48 % | 24.9 % |
   | per-blow costs ×3 | 8.9 s | 19.6 % | 3.0 % | 51 / 49 % | 23.9 % |
   | per-blow costs ×5 | 8.5 s | 16.7 % | 2.8 % | 50 / 50 % | 22.7 % |
   | **flanked squad 25/s** (rear 50/s), per-blow as today | **7.0 s** | **11.9 %** | **2.1 %** | 51 / 49 % | 23.5 % |
   | flanked squad 50/s (rear 100/s) | 5.7 s | 6.5 % | 1.5 % | 52 / 48 % | 22.3 % |

   Per-blow costs stay weak however large, because most of the flankers' blows land from the front (the men they reach
   turn to face them). *Flanked squad* is a new rule (measured in a scratch copy, not built): while an enemy fights one
   of a squad's men from outside the squad's front — its formation's facing, front arc 60°, rear beyond 120° — every man
   of the squad in the fight loses morale each second, reasons `SquadFlanked` / `SquadTakenInRear` (they exist; today
   they mark per-blow costs). **Proposal:** build it at 25/s flank, 50/s rear (rates in `morale.json`), per-blow costs as
   today — a flanked squad breaks after about one man down instead of two, the flank ends the fight ~2.5 s sooner and
   costs the attackers ~40 % fewer men, the equal fight stays even. 50/s breaks squads almost on contact. If built: its
   own small step; S7, `m3-flank` and the sandbox re-pinned on purpose, S6 stays within its pin.
   **Deferred by the user 2026-11-25:** keep the option here, don't build it now; decide later (M6 tuning at the
   latest). The flank works today as measured in the first row.
10. The experience ladder is too steep (found in M3 step 9, 2026-11-26). D-026 wants one level ahead to win 65–70 %;
    measured on S6's lines in the same kit over 2000 seeds: experienced vs new 97 %, professional vs experienced 96 %,
    elite vs professional 93 %. A level adds +80 hit chance, +40 defence and +100–150 starting morale
    (`combat.json` `hitChance`, `experience`). Proposal: flatten it before Retainers (step 10 — professional and elite
    are theirs): measure variants with `battle-analyst` — e.g. +40 hit and +20 defence a level, morale steps of 50 —
    and pick the one where each step wins ~65–70 %, then re-pin `m3-veterans`, `m3-rally` and the sandbox. Answers
    the recovery question too: experience needs no further buff. (M3, before step 10, or M6) — Still open: the user
    chose to build Retainers first (2026-11-26). Step 10 shows it again: an elite Retainer's squad wins 82 % in S8
    chiefly through his hit chance.
11. How strong should leadership be? (found in M3 step 10, 2026-11-26) With his fall costing one mate, an experienced
    Retainer leading a line of 8 wins 52 % against an equal line (S8 vs S6's 50 %): leadership 100 ‰ barely shows, and
    a leader's worth comes from his experience level. Proposal: decide after question 10 — once the ladder is flatter,
    measure leadership 100/150/200 against stronger sets (e.g. 150/250/350) on S8 with `battle-analyst`, aiming at a
    leader worth roughly a level of experience to his squad. (M3 step 11 or M6)

## Phase 2 — public 3D skirmish
Same sim, 3D view in the style of Expeditions: Viking; Steam page, GIFs and videos, Steam Playtest with balance
branches, then a demo for Next Fest. Wishlist target 30–50k by Early Access.

## Phase 3 — campaign in Early Access
Prologue and Act I: escape, homestead by the town, first expedition of 3–4 battles, hiring, equipment, tribute,
save/load. The skirmish stays as a "free battle" mode.

Note 2026-11-24: the design redefines the campaign MVP as one map — the удел, the clan's first settlement on land
from the prince, with tribute due by the end of year N; no global map, caravans or expeditions in it (D-008, D-012;
`docs/design/README.md`). Rewrite this section when phase 1 exits.
