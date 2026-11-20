---
name: battle-run
description: Run battles headless and look at them — a single scenario with a seed, a batch over many seeds for win rates and metrics, a tick-time benchmark, or a replay in the HTML replay viewer via the browser pane. Use to check that movement, formations, combat or morale behave as intended, to compare balance before/after a change, to measure performance for the M1 criteria, or when the user asks "what does the battle look like".
---

# Running and watching battles headless

The Unity build can't be played by an agent. The CLI and the replay viewer are how you see the battle.
Contract: `docs/battle-sim.md` §17. If a command or flag doesn't exist yet, say so and offer to add it.

**Status (M1 step 1):** only `run <scenario> [--seed N] [--ticks N] [--json]` exists: it loads the scenario and
the defs from the nearest `defs/` folder above it, and prints forces per side, tick count and final hash (exit codes
0 / 1 sim error / 2 bad args or content — content errors are located as `file:line:col: json.path: message`). Nothing
moves yet. `--out`/`--frames`, `batch`, `bench`, `replay verify` and the replay viewer arrive in later M1 steps
(roadmap) — the sections below describe that target. CLI code lives in `sim/tools/Knez.Sim.Cli/` (`CliApp.cs`,
`ScenarioFiles.cs`); its tests in `sim/tests/Knez.Sim.Tests/Cli/`.

## Single run

```bash
dotnet run --project sim/tools/Knez.Sim.Cli -c Release -- run content/scenarios/<name>.json --seed 1 \
  --out artifacts/replays/<name>-s1.replay.json --frames artifacts/replays/<name>-s1.frames.json
```

Prints winner, duration (ticks and seconds), casualties per side, morale breaks/routs, final hash.

## Watch it

```bash
python3 -m http.server 8765 --directory .    # run in the background
```

Open `http://localhost:8765/tools/replay-viewer/?frames=/artifacts/replays/<name>-s1.frames.json` in the browser
pane. Use step/scrub to reach the interesting ticks; take screenshots at the moments that matter (first contact,
a wheel, a choke, the first rout). Describe what you see in terms of the M1/M3 criteria; don't claim behaviour you
didn't observe. Stop the server when done.

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

Metrics: win rate per side, mean/p90 duration, casualties, routs, stuck fighter-ticks, overlap ratio, mean slot error.

**A/B comparison:** run the same scenario and seed range on the baseline commit and on the change (use a git
worktree or stash for the baseline), then compare. Win-rate differences under ~5 points on 200 seeds are noise.
Long batches: hand them to the `battle-analyst` subagent so raw output stays out of the main context.

## Benchmark (M1 criterion: p99 < 2 ms per tick for 50 fighters on the dev Mac)

```bash
dotnet run --project sim/tools/Knez.Sim.Cli -c Release -- bench content/scenarios/bench/s1-open-clash.json --ticks 4000
```

Always Release, nothing else heavy running; report p50/p99 and the machine. Compare against the previous number
in `docs/roadmap.md` or the last report, not against a guess.

## Scenarios

`content/scenarios/*.json` — playable skirmishes; `content/scenarios/bench/` — the S1–S5 movement benchmarks
(battle-sim.md §10). A new mechanic usually deserves a small focused scenario; name it after what it tests. The file
format is in battle-sim.md §7; a scenario's `id` must equal its file name, and `RepoContentTests` loads every
scenario in the repo, so a broken one fails the tests.
