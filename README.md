# Game_knez

RPG-first real-time-with-pause tactics inspired by the Viking-age Baltic. Unity 6 view over a deterministic,
pure C# battle simulation.

Status: **Phase 1, M0 — Foundation.** See [docs/roadmap.md](docs/roadmap.md).

## Start here

- [AGENTS.md](AGENTS.md) — rules for agents and humans: scope, architecture, hard rules, commands, glossary
- [docs/concept/concept-v2.md](docs/concept/concept-v2.md) — game concept (Russian)
- [docs/battle-sim.md](docs/battle-sim.md) — battle simulation design
- [docs/adr/](docs/adr/) — decision records

## Setup

Machine-wide via Homebrew: .NET 10 SDK, Git LFS, Python 3.13. Project-local: a venv (`.venv`) on that Python.
Details: [docs/dev-setup.md](docs/dev-setup.md).

```bash
brew install --cask dotnet-sdk@10
```

```bash
brew install git-lfs python@3.13 && git lfs install
```

```bash
bash tools/bootstrap.sh
```

```bash
source .venv/bin/activate
```

Unity Hub + Unity 6 LTS are needed from milestone M2 (Unity view); the simulation builds and tests without Unity.

```bash
dotnet test sim/Knez.Sim.sln
python3 tools/check_sim_purity.py
```
