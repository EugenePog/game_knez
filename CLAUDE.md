@AGENTS.md

# Claude Code notes

Everything in AGENTS.md applies. This part covers only what is specific to Claude Code.

## Skills (`.claude/skills/`)

| Skill | Use when |
|---|---|
| `sim-feature` | adding or changing anything in `sim/src` or `sim/tests` |
| `determinism` | checking determinism, a hash mismatch, a desync, golden replays |
| `battle-run` | running scenarios headless, batches, benchmarks, viewing a replay |
| `unity-view` | anything in `game/` (Unity view, input, batch-mode tests) |
| `playtest-notes` | recording a playtest session toward the "20th battle" exit criterion |

## Subagents (`.claude/agents/`)

| Agent | Use when |
|---|---|
| `sim-reviewer` | after any change to `sim/`, before committing — read-only review for determinism and architecture |
| `sim-test-writer` | writing tests from the spec independently of the implementation |
| `battle-analyst` | long batch runs, balance A/B comparisons, perf benchmarks — keeps raw output out of the main context |

## Toolchain in your shell

`dotnet` and `git lfs` are machine-wide. A SessionStart hook puts `.venv/bin` first on PATH when the venv exists, so
`python3` is the project venv's Python (Homebrew 3.13). If something is missing, run `bash tools/bootstrap.sh --check` and
relay its `fix:` lines to the user — installing the .NET cask needs their admin password, so never try it yourself.
In a session that started before `.venv` existed, use `.venv/bin/python` explicitly.

## Purity hook

`.claude/settings.json` also runs `tools/check_sim_purity.py --hook` after every Edit/Write. It only looks at `.cs`
files under `sim/src` and reports violations back to you. Fix them; don't suppress with
`// sim-purity: allow` unless the line is genuinely safe, and say why in that comment.

## Seeing the battle

You can't play the Unity build. To see behaviour, run a scenario with the CLI (`--frames`), serve the repo
(`replay-viewer` in `.claude/launch.json`, or `python3 -u -m http.server 8765 --bind 127.0.0.1` from a shell if that
one never starts) and open `tools/replay-viewer/` in the browser pane. Screenshots of the viewer are the
way to check formations, jams and routs (see `battle-run`).

## Concept document

The live concept is a Claude Docs doc ("Game_knez — Концепция v2",
https://claude.ai/artifact/5L55AzFUYe6c72fx1Gwvfj). `docs/concept/concept-v2.md` is a snapshot of it for agents
that can't reach it. If the user says the concept changed, re-read the doc and refresh the snapshot.
