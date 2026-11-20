# Development environment

| Tool | Where it lives | Pinned by |
|---|---|---|
| .NET 10 SDK | machine-wide, Homebrew cask **`dotnet-sdk@10`** (`/usr/local/share/dotnet`) | `global.json` (10.0.x, newer patches allowed) |
| NuGet packages | shared cache `~/.nuget/packages` | project files; `nuget.config` limits sources to nuget.org |
| Git LFS | machine-wide, Homebrew `git-lfs`; hooks per repository | — |
| Python | Homebrew **`python@3.13`** + project venv `.venv` (`python3.13 -m venv .venv`) | `.python-version`, `tools/requirements.txt` |
| Unity 6 (from M2) | Unity Hub, editors side by side | `game/ProjectSettings/ProjectVersion.txt` |

None of this changes how your other Python or Node projects work: the .NET SDK adds only the `dotnet` command, the
LFS filter only acts on repos whose `.gitattributes` ask for it, and the venv uses the Homebrew Python you already have.

Background — how Pythons, PATH and venvs work on this Mac and why this setup was chosen:
[Game_knez — Python, PATH and venvs explained](https://claude.ai/artifact/48ZbdkrdFW5HAn3m43zJBp) (private doc).

## 1. Install the machine-wide prerequisites (once per Mac)

.NET 10 SDK — use exactly this cask. `dotnet-sdk` / `dotnet` give .NET 11, which does not satisfy `global.json`,
and the `dotnet@10` formula is keg-only (not on PATH). The cask runs Microsoft's installer and asks for your admin
password. It also installs the `dotnet-sdk` cask (.NET 11) as a dependency: both SDKs sit side by side in
`/usr/local/share/dotnet`, `global.json` keeps this repo on 10, and other folders get 11 by default.

```bash
brew install --cask dotnet-sdk@10
```

Git LFS — install, then enable it for your user (adds a `[filter "lfs"]` section to `~/.gitconfig`):

```bash
brew install git-lfs && git lfs install
```

Python 3.13 — already installed on this Mac; on a new machine:

```bash
brew install python@3.13
```

Open a new terminal afterwards so `dotnet` is on PATH.

## 2. Set up the repository

```bash
cd ~/Documents/projects/game_knez && bash tools/bootstrap.sh
```

It checks the SDK, Git LFS and Python, installs the LFS hooks for this repository, and creates `.venv`. Each missing
piece is reported with the exact fix. Re-run it any time; `bash tools/bootstrap.sh --check` only reports and changes
nothing.

### How the venv is created

```bash
python3.13 -m venv .venv
```

- **`python3.13`, not `python3`.** `python3` is a link Homebrew moves to its newest default (3.14 one day); a later
  re-run would then silently build the venv on a different version. The version comes from `.python-version`.
- **It survives `brew upgrade`.** The venv points at `/opt/homebrew/opt/python@3.13`, which Homebrew keeps stable
  across patch releases (3.13.7 → 3.13.8).
- **It breaks only if `python@3.13` is uninstalled** (or when Homebrew retires 3.13, around 2029). Keep it installed —
  the uv tool `crewai` runs on it too. If it ever breaks, re-run the bootstrap: it rebuilds `.venv` in seconds.

## 3. Every terminal

```bash
source .venv/bin/activate
```

`deactivate` leaves it. Only Python needs activating — `dotnet` and `git lfs` are always on PATH.
Install Python packages only inside the venv (`python -m pip install …` while it's active) and record them in
`tools/requirements.txt`. Right now the repo tools need only the standard library.

## Checks

```bash
bash tools/bootstrap.sh --check
```

```bash
dotnet --version && git lfs version && .venv/bin/python -c "import sys; print(sys.version, sys.base_prefix)"
```

`dotnet --version` inside the repo must print 10.0.x; the last line must show a path under `/opt/homebrew`.

## Editors

The SDK is in a standard location, so VS Code (C# Dev Kit) and Rider find it with no configuration. For Python in
VS Code, select the interpreter `.venv/bin/python`.

## Claude Code

`.claude/settings.json` has a SessionStart hook that puts `.venv/bin` first on Claude Code's PATH when the venv
exists, so `python3` in agent commands is the project venv's Python. The purity-check hook also uses
`.venv/bin/python` when present and falls back to `python3` (it needs only the standard library). Both take effect in
sessions started after the venv exists.

## .NET tools (formatters, analyzers, …)

Install them as **local tools** recorded in `.config/dotnet-tools.json`, never with `-g`:

```bash
dotnet new tool-manifest
```

```bash
dotnet tool install <tool-name>
```

## Unity (from milestone M2)

Install **Unity Hub** (a normal app). In Hub → Settings → Installs you can choose where editors go, e.g.
`~/Unity/Editors`; each editor version gets its own folder, so versions never conflict. The project pins its exact
editor version in `game/ProjectSettings/ProjectVersion.txt`, and Hub offers to install it when you open the project.
Unity ships its own compilers and runtime and does not use the .NET SDK.

## Removing the project setup

```bash
rm -rf .venv
```

The machine-wide tools are removed the usual Homebrew way (`brew uninstall --cask dotnet-sdk@10`,
`brew uninstall git-lfs`, plus `git lfs uninstall` to drop the `~/.gitconfig` section). Don't uninstall
`python@3.13` while `crewai` or other venvs still use it.
