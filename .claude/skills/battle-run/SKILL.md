---
name: battle-run
description: Run battles headless and look at them — a single scenario with a seed, a batch over many seeds for win rates and metrics, a tick-time benchmark, or a replay in the HTML replay viewer via the browser pane. Use to check that movement, formations, combat or morale behave as intended, to compare balance before/after a change, to measure performance for the M1 criteria, or when the user asks "what does the battle look like".
---

# Running and watching battles headless

The Unity build can't be played by an agent. The CLI and the replay viewer are how you see the battle.
Contract: `docs/battle-sim.md` §17. If a command or flag doesn't exist yet, say so and offer to add it.

**Status (M1 step 2):** `run <scenario> [--seed N] [--ticks N] [--frames FILE] [--json]` and the replay viewer
exist. `run` loads the scenario and the defs from the nearest `defs/` folder above it, and prints forces per side, tick
count and final hash (exit codes 0 / 1 sim error / 2 bad args, content or frames path — content errors are located as
`file:line:col: json.path: message`). Squads move on `MoveSquad` orders; the CLI can't issue commands, so a scenario's
`script` gives timed orders (battle-sim.md §7) — `content/scenarios/march.json` is the example, and the viewer draws
each order as a dashed line to a ring with the arrival facing. Bodies are solid (step 4): `run` prints the overlap
metric (`crowding:` line; M1 target < 1 % of fighter-ticks) and the viewer rings overlapping fighters in red —
allies passing through a holding ally's ranks are allowed and not ringed (decided 2026-11-21);
`content/scenarios/crowding.json` shows a crossing, allies squeezing through a holding line, an enemy block and the
map edge. After changing `tools/replay-viewer/` files, the page may keep running the old script: `http.server` sends no
cache headers, and in the browser pane Cmd+Shift+R didn't help (M3 step 9). Refresh the cached copy from the page —
`await fetch('/tools/replay-viewer/viewer.js', {cache: 'reload'})` with the javascript tool — then navigate to the
viewer again. `batch` and `bench` exist since M1 step 8;
`--out` (replay files) and `replay verify` arrive later — those parts below describe the target. CLI code lives in
`sim/tools/Knez.Sim.Cli/` (`CliApp.cs`, `ScenarioFiles.cs`, `FrameRecorder.cs`); its tests in
`sim/tests/Knez.Sim.Tests/Cli/`. Frames format and viewer options: battle-sim.md §17.

## Single run

```bash
dotnet run --project sim/tools/Knez.Sim.Cli -c Release -- run content/scenarios/<name>.json --seed 1 \
  --frames artifacts/replays/<name>-s1.frames.json          # --out <replay.json> arrives later in M1
```

Prints winner, duration (ticks and seconds), casualties per side, morale breaks/routs, final hash.

## Watch it

Serve the repo root on `127.0.0.1:8765`: `preview_start` with the `replay-viewer` config in `.claude/launch.json`.
If that server stays "starting" (seen on macOS: the app-launched Python never binds, likely a pending folder-access
prompt for ~/Documents), run `python3 -u -m http.server 8765 --bind 127.0.0.1` from the repo root as a background
shell task instead, and open the page with `preview_start` + `url`.

Open `http://127.0.0.1:8765/tools/replay-viewer/?frames=/artifacts/replays/<name>-s1.frames.json&tick=120` in the
browser pane (`tick=` jumps straight to a moment; `ids=1` labels fighters when zoomed in). Step with ←/→ (Shift = 20
ticks), Space plays, the mouse wheel zooms, drag pans, F fits. From the console, `knezViewer.seekTick(n)` jumps and
renders at once, `knezViewer.tick` reads the position, and `knezViewer.error` is set if loading failed (don't wait on
`loaded` forever). A hidden browser pane runs no animation frames: playback stalls there, so seek instead of playing,
and take a screenshot to see the canvas. A run that crashed still leaves a readable file — the viewer shows
"Faulted in tick N" — so look at the ticks just before it. Hovering a fighter shows its id, squad, slot,
position and facing in the side panel, and from M3 his morale, its state and his wound. M3 marks: a bar under each
man shows morale (green steady, yellow shaken, orange wavering, red about to run), a red dot a wound (bigger = heavy),
white with a coloured ring a running man, a dashed ring a rallying one; the squad panel shows average morale, how many
run and BROKEN, the Tick panel the battle's result. The Morale toggle (or `morale=0`) hides the bars. Take screenshots at the moments that matter (first contact, a wheel, a choke,
the first rout). Describe what you see in terms of the M1/M3 criteria; don't claim behaviour you didn't observe. Stop
the server when done.

What to look for:
- **Movement:** fighters stuck or orbiting, lines tearing apart, interpenetration, stragglers after a wheel,
  deadlock at a choke.
- **Combat:** fighters swinging out of reach, whole squads idle next to enemies, unlimited push-through.
- **Morale:** routs without visible cause, never routing, whole army breaking at once, no rally.

## Batch (balance and metrics)

```bash
dotnet run --project sim/tools/Knez.Sim.Cli -c Release -- batch content/scenarios/<name>.json --seeds 1-200 --json \
  > artifacts/reports/<name>-<label>.json
```

Movement metrics (M1): overlap (mean and worst run), stuck (worst run's fighters stuck > 2 s, longest spell), mean
slot error, distinct outcomes. For a fight, the D-026 balance measures (M3 step 7, battle-sim.md §17), each taken up to
the decision: wins per side, how runs ended (rout / annihilation / nobody left / undecided at the limit), when they
were decided, losses when a squad first broke, each side's men down, blows that got through. Give fights time to end:
`--ticks 3600` (3 minutes). `run` prints one seed's result, morale breaks and blows.

**The balance reference** (battle-sim.md §11): S6 `bench/s6-lines` (8 vs 8 head-on) and S7 `bench/s7-flank` (the same
plus a flanking squad), with the `m3-*` and sandbox fights, pinned in `sim/tests/Golden/balance-reference.json` and
checked by `BalanceReferenceTests` in Release only (`dotnet test sim/Knez.Sim.sln -c Release --filter
FullyQualifiedName~BalanceReferenceTests`). Compare a balance change against that table; when the change is on purpose,
re-record (`--filter Name=Record_WritesTheReferenceFile`, also `-c Release`) and update the §11 table.

**A/B comparison:** run the same scenario and seed range on the baseline commit and on the change (use a git
worktree or stash for the baseline), then compare. A win rate measured on n seeds has a standard error of about
50/√n points, and the difference of two such runs √2 times that: under ~10 points on 200 seeds, or ~4.5 on 1000, is
noise. `batch` steps every run to `--ticks` (only the test stops at the decision): 1000 seeds of S6 at `--ticks 3600`
take ~15 s in Release, S7 ~28 s, the sandbox ~30 s per 50 seeds.
Long batches: hand them to the `battle-analyst` subagent so raw output stays out of the main context.

## Benchmark (M1 criterion: p99 < 2 ms per tick for 50 fighters on the dev Mac)

```bash
dotnet run --project sim/tools/Knez.Sim.Cli -c Release -- bench content/scenarios/bench/s1-clash.json --ticks 900 --runs 5
```

Always Release, nothing else heavy running; report p50/p99 and the machine. Compare against the previous number
in `docs/roadmap.md` or the last report, not against a guess.

## A battle played in Unity

When Play stops, Unity writes the battle as a scenario with every applied order in its `script` to
`artifacts/sessions/<scenario>-seed<N>-<yyyyMMdd-HHmmss>.json` and logs the exact replay command to the Console. Run it
with the same seed and the tick count from the file's description, record frames, and watch it in the viewer:

```bash
dotnet run --project sim/tools/Knez.Sim.Cli -c Release -- run artifacts/sessions/villages-seed1-20261123-153000.json --seed 1 --ticks 6500 --frames artifacts/replays/villages-session.frames.json
```

The defs come from today's `content/defs/`, so a session recorded before a content change no longer replays exactly.

## Scenarios

`content/scenarios/*.json` — playable skirmishes; `content/scenarios/bench/` — the S1–S5 movement benchmarks
(battle-sim.md §10); `content/scenarios/m3/` — combat and morale setups (lines, flank, shield wall, spears, veterans,
rally, withdraw). `bash tools/record-replays.sh [folder] [seed] [seconds]` records a replay of every scenario in a
folder (default the M3 ones, seed 1, 150 s), prints each run's combat, morale and result lines, and a viewer link.
`run` prints the same lines (`combat:`, `morale:`, `result:`, `sides:`). A new scenario must also get an entry in
`sim/tests/Golden/content-traces.json` (the cross-runtime goldens list every content scenario). A new mechanic usually deserves a small focused scenario; name it after what it tests. The file
format is in battle-sim.md §7; a scenario's `id` must equal its file name, and `RepoContentTests` loads every
scenario in the repo, so a broken one fails the tests.
