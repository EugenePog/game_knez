#!/usr/bin/env bash
# Game_knez setup check and project venv.
#
# The .NET 10 SDK, Git LFS and Python 3.13 are machine-wide Homebrew installs (docs/dev-setup.md).
# This script checks them, enables the Git LFS hooks for this repository, and creates .venv with
# Homebrew's Python using the version-pinned name:  python3.13 -m venv .venv
#
#   bash tools/bootstrap.sh           check, then set up LFS hooks and .venv
#   bash tools/bootstrap.sh --check   check and report only; change nothing
#
# Safe to re-run. Exit code 1 if something still needs your action.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
DOTNET_MAJOR=10
PYTHON_VERSION="$(tr -d '[:space:]' <"$ROOT/.python-version" 2>/dev/null || true)"
PYTHON_VERSION="${PYTHON_VERSION:-3.13}"
CHECK_ONLY=0
[ "${1:-}" = "--check" ] && CHECK_ONLY=1

missing=0
step() { printf '\n==> %s\n' "$*"; }
ok() { printf '    ok: %s\n' "$*"; }
need() {
  printf '    MISSING: %s\n    fix:     %s\n' "$1" "$2"
  missing=$((missing + 1))
}

# ---------------------------------------------------------------------------------------------------
step ".NET $DOTNET_MAJOR SDK (machine-wide, Homebrew cask dotnet-sdk@$DOTNET_MAJOR)"
if ! command -v dotnet >/dev/null 2>&1; then
  need "dotnet is not on PATH" \
    "brew install --cask dotnet-sdk@$DOTNET_MAJOR   (asks for your admin password; then open a new terminal)"
elif ! dotnet --list-sdks 2>/dev/null | grep "^$DOTNET_MAJOR\." >/dev/null; then
  need ".NET $DOTNET_MAJOR SDK is not installed (found: $(dotnet --list-sdks 2>/dev/null | awk '{print $1}' | paste -sd ' ' -))" \
    "brew install --cask dotnet-sdk@$DOTNET_MAJOR"
elif ! sdk="$(cd "$ROOT" && dotnet --version 2>/dev/null)"; then
  need "no installed SDK satisfies global.json" "brew upgrade --cask dotnet-sdk@$DOTNET_MAJOR"
else
  ok "SDK $sdk selected by global.json ($(command -v dotnet))"
fi

# ---------------------------------------------------------------------------------------------------
step "Git LFS (machine-wide, Homebrew git-lfs)"
hooks_dir="$(cd "$ROOT" && git rev-parse --git-path hooks)"
case "$hooks_dir" in /*) ;; *) hooks_dir="$ROOT/$hooks_dir" ;; esac
if ! git lfs version >/dev/null 2>&1; then
  need "git-lfs is not installed" "brew install git-lfs && git lfs install"
elif [ -z "$(git -C "$ROOT" config --get filter.lfs.process || true)" ]; then
  need "Git LFS filters are not configured for your user" \
    "git lfs install   (once per machine; adds [filter \"lfs\"] to ~/.gitconfig)"
else
  if [ "$CHECK_ONLY" = 0 ]; then
    git -C "$ROOT" lfs update >/dev/null # hooks for this repository only
  fi
  if grep "git lfs" "$hooks_dir/pre-push" >/dev/null 2>&1; then
    ok "$(git lfs version | cut -d' ' -f1), hooks installed in $hooks_dir"
  else
    need "Git LFS hooks are not installed in this repository" "bash tools/bootstrap.sh"
  fi
fi

# ---------------------------------------------------------------------------------------------------
step "Python $PYTHON_VERSION venv in .venv (Homebrew python@$PYTHON_VERSION, python$PYTHON_VERSION -m venv)"
BREW_PREFIX="$(brew --prefix 2>/dev/null || echo /opt/homebrew)"
PY_NAME="python$PYTHON_VERSION" # the pinned name: python3 will move to a newer version over time
is_brew() { case "$1" in "$BREW_PREFIX"/*) return 0 ;; *) return 1 ;; esac; }

# True when .venv exists, runs Python $PYTHON_VERSION, and its base interpreter is Homebrew's.
venv_ok() {
  [ -x "$ROOT/.venv/bin/python" ] || return 1
  local info
  info="$("$ROOT/.venv/bin/python" -c 'import sys; print("%d.%d %s" % (sys.version_info[0], sys.version_info[1], sys.base_prefix))' 2>/dev/null)" || return 1
  [ "${info%% *}" = "$PYTHON_VERSION" ] && is_brew "${info#* }"
}

py_path="$(command -v "$PY_NAME" 2>/dev/null || true)"
if [ -z "$py_path" ]; then
  need "$PY_NAME is not on PATH" "brew install python@$PYTHON_VERSION"
elif ! is_brew "$py_path"; then
  need "$PY_NAME resolves to $py_path, not Homebrew's" \
    "brew install python@$PYTHON_VERSION, and keep $BREW_PREFIX/bin ahead of other Python folders in PATH"
elif venv_ok; then
  ok ".venv → $("$ROOT/.venv/bin/python" -c 'import sys; print(sys.base_prefix)')"
elif [ "$CHECK_ONLY" = 1 ]; then
  need ".venv is missing or not on Homebrew Python $PYTHON_VERSION" "bash tools/bootstrap.sh"
else
  [ -e "$ROOT/.venv" ] && echo "    rebuilding .venv (missing, broken or on another interpreter)"
  "$py_path" -m venv --clear "$ROOT/.venv"
  if venv_ok; then
    ok ".venv → $("$ROOT/.venv/bin/python" -c 'import sys; print(sys.base_prefix)')"
  else
    need "the venv was created but is not on Homebrew Python $PYTHON_VERSION" "check: $py_path -c 'import sys; print(sys.base_prefix)'"
  fi
fi

if [ "$CHECK_ONLY" = 0 ] && venv_ok && grep -Ev '^[[:space:]]*(#|$)' "$ROOT/tools/requirements.txt" >/dev/null 2>&1; then
  "$ROOT/.venv/bin/python" -m pip install --quiet --disable-pip-version-check -r "$ROOT/tools/requirements.txt"
fi

# ---------------------------------------------------------------------------------------------------
step "Summary"
if [ "$missing" -gt 0 ]; then
  echo "    $missing item(s) need your action — follow the 'fix:' lines above, then re-run: bash tools/bootstrap.sh"
  exit 1
fi
if [ "$CHECK_ONLY" = 1 ]; then
  echo "    Everything is in place."
else
  echo "    All set. Activate the venv in a terminal with: source .venv/bin/activate"
fi
