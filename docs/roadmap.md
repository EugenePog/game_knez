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
8. Bench: scenarios S1–S5, metrics, `bench` and `batch`; tune; decide steering vs ORCA; check the exit criteria.

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
