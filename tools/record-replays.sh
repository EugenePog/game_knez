#!/usr/bin/env bash
# Records a replay of each scenario in a folder and prints a replay-viewer link for each (battle-run skill).
# Usage: bash tools/record-replays.sh [scenario folder, default content/scenarios/m3] [seed, default 1] [seconds, default 150]
# Then serve the repo (python3 -u -m http.server 8765 --bind 127.0.0.1) and open the links.
set -euo pipefail
cd "$(dirname "$0")/.."
folder="${1:-content/scenarios/m3}"
seed="${2:-1}"
seconds="${3:-150}"
ticks=$((seconds * 20))
mkdir -p artifacts/replays
dotnet build sim/tools/Knez.Sim.Cli -c Release -v quiet -nologo >/dev/null
for file in "$folder"/*.json; do
  name="$(basename "$file" .json)"
  out="artifacts/replays/${name}-seed${seed}.frames.json"
  echo "== $name (seed $seed, ${seconds} s)"
  dotnet run --project sim/tools/Knez.Sim.Cli -c Release --no-build -- run "$file" --seed "$seed" --ticks "$ticks" --frames "$out" \
    | grep -E '^(combat|morale|result|sides):'
  echo "   http://127.0.0.1:8765/tools/replay-viewer/?frames=/$out"
done
