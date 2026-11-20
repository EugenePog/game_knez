#!/usr/bin/env python3
"""Determinism and purity guard for the battle simulation (sim/src).

Catches the mistakes that break ADR-0001 (no Unity in the sim) and ADR-0002 (no floating point,
no hidden nondeterminism). It is a fast text check, not a proof: the sim-reviewer agent and the
determinism tests cover what a regex cannot see.

Usage:
  python3 tools/check_sim_purity.py            scan every .cs file under sim/src
  python3 tools/check_sim_purity.py FILE...    scan the given .cs files
  python3 tools/check_sim_purity.py --hook     Claude Code PostToolUse hook: reads the tool call as JSON
                                               on stdin and checks the edited file if it is under sim/src

Exit codes: 0 clean; 1 violations (CLI); 2 violations (hook — stderr is shown to the agent).
Allow one line on purpose with a trailing comment:  // sim-purity: allow <reason>
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SIM_SRC = ROOT / "sim" / "src"
ALLOW_MARK = "sim-purity: allow"

RULES = [
    ("unity", re.compile(r"\bUnity(Engine|Editor)\b"),
     "Unity API in the sim; Knez.Sim must build without Unity (ADR-0001)"),
    ("float", re.compile(r"\b(float|double)\b"),
     "floating point in the sim; use Fix / FixVec2 (ADR-0002)"),
    ("float-literal", re.compile(r"(?<![\w.])\d+\.\d+(?:[eE][+-]?\d+)?[fFdDmM]?\b|(?<![\w.])\d+[fFdD]\b"),
     "floating-point literal; use an int, Fix.FromInt, Fix.Ratio or content data"),
    ("float-math", re.compile(r"\bMathF\b|\bMath\.(?:Sin|Cos|Tan|Asin|Acos|Atan2?|Sinh|Cosh|Tanh|Sqrt|Cbrt|Pow|Exp"
                              r"|Log2|Log10|Log|Round|Floor|Ceiling|Truncate|FusedMultiplyAdd|ScaleB)\b|\busing\s+static\s+System\.Math"),
     "floating-point math; use FixMath"),
    ("random", re.compile(r"\bSystem\.Random\b|\bnew\s+Random\s*\(|\bRandom\.Shared\b"),
     "System.Random; draw from a SimRng stream in the tick context"),
    ("clock", re.compile(r"\b(?:DateTime|DateTimeOffset|Stopwatch|TimeProvider)\b|\bEnvironment\.TickCount"),
     "wall-clock time; sim time is BattleState.Tick"),
    ("guid", re.compile(r"\bGuid\.NewGuid\b"),
     "random GUID; ids come from BattleState's EntityId counter"),
    ("concurrency", re.compile(r"\b(?:Thread|ThreadPool|Parallel|Task|ValueTask|Interlocked)\b\s*[.<(]"
                               r"|\basync\b|\bawait\b|\block\s*\(|\bvolatile\b|\[ThreadStatic\]"),
     "concurrency; the sim is single-threaded"),
    ("hashcode", re.compile(r"\.GetHashCode\s*\("),
     "runtime hash codes are not stable between runs (string hashes are randomised); use StateHasher or explicit keys"),
    ("io", re.compile(r"\bConsole\.|\bSystem\.IO\b|\bFile\.|\bDirectory\.|\bEnvironment\.GetEnvironmentVariable"),
     "I/O or environment access in the sim; loading and saving live outside Knez.Sim"),
    ("unordered-iteration", re.compile(r"\bforeach\s*\([^)]*\bin\b[^)]*(?:\.Keys\b|\.Values\b|Dictionary|HashSet)"),
     "iterating an unordered collection; iterate a list sorted by EntityId"),
    ("static-mutable", re.compile(
        r"^\s*(?:(?:public|private|internal|protected|new|unsafe)\s+)*static\s+"
        r"(?!readonly\b|class\b|partial\b|void\b|extern\b|implicit\b|explicit\b|async\b)"
        r"[\w<>\[\],.? ]+?\s+\w+\s*(?:=(?![>=])|;)"),
     "mutable static field; all state belongs in BattleState"),
]


def strip_comments_and_strings(source: str) -> str:
    """Blank out comments and string/char literal contents, keeping line structure."""
    out = []
    i, n = 0, len(source)
    while i < n:
        c = source[i]
        nxt = source[i + 1] if i + 1 < n else ""
        if c == "/" and nxt == "/":
            while i < n and source[i] != "\n":
                i += 1
            continue
        if c == "/" and nxt == "*":
            i += 2
            while i < n and not (source[i] == "*" and i + 1 < n and source[i + 1] == "/"):
                out.append("\n" if source[i] == "\n" else " ")
                i += 1
            i += 2
            continue
        if c == "@" and nxt == '"' or (c in "$@" and nxt in "$@" and i + 2 < n and source[i + 2] == '"'):
            # verbatim string: "" is an escaped quote
            i = source.index('"', i) + 1
            out.append('""')
            while i < n:
                if source[i] == '"':
                    if i + 1 < n and source[i + 1] == '"':
                        i += 2
                        continue
                    i += 1
                    break
                if source[i] == "\n":
                    out.append("\n")
                i += 1
            continue
        if c == '"' or (c == "$" and nxt == '"'):
            i = source.index('"', i) + 1
            out.append('""')
            while i < n and source[i] != '"' and source[i] != "\n":
                i += 2 if source[i] == "\\" else 1
            i += 1
            continue
        if c == "'":
            j = i + 1
            while j < n and source[j] != "'" and source[j] != "\n":
                j += 2 if source[j] == "\\" else 1
            out.append("' '")
            i = j + 1
            continue
        out.append(c)
        i += 1
    return "".join(out)


def check_file(path: Path) -> list:
    try:
        raw = path.read_text(encoding="utf-8-sig")
    except (OSError, UnicodeDecodeError) as exc:
        return [f"{path}: cannot read ({exc})"]
    raw_lines = raw.splitlines()
    code_lines = strip_comments_and_strings(raw).splitlines()
    problems = []
    for lineno, code in enumerate(code_lines, start=1):
        original = raw_lines[lineno - 1] if lineno - 1 < len(raw_lines) else ""
        if ALLOW_MARK in original:
            continue
        for rule_id, pattern, message in RULES:
            if rule_id == "static-mutable" and "operator" in code:
                continue
            if pattern.search(code):
                rel = path.relative_to(ROOT) if path.is_relative_to(ROOT) else path
                problems.append(f"{rel}:{lineno}: [{rule_id}] {message}\n    {original.strip()}")
    return problems


def is_sim_source(path: Path) -> bool:
    return path.suffix == ".cs" and path.is_relative_to(SIM_SRC) and "artifacts" not in path.parts


def run_hook() -> int:
    try:
        payload = json.load(sys.stdin)
    except json.JSONDecodeError:
        return 0
    tool_input = payload.get("tool_input") or {}
    file_path = tool_input.get("file_path") or (payload.get("tool_response") or {}).get("filePath")
    if not file_path:
        return 0
    path = Path(file_path).resolve()
    if not is_sim_source(path) or not path.exists():
        return 0
    problems = check_file(path)
    if not problems:
        return 0
    print("Sim purity check failed (see AGENTS.md hard rules, docs/adr/0002):", file=sys.stderr)
    print("\n".join(problems), file=sys.stderr)
    print("Fix the code, or mark a genuinely safe line with '// sim-purity: allow <reason>'.", file=sys.stderr)
    return 2


def run_cli(args: list) -> int:
    if args:
        files = [Path(a).resolve() for a in args if a.endswith(".cs")]
    elif SIM_SRC.exists():
        files = sorted(p for p in SIM_SRC.rglob("*.cs") if is_sim_source(p.resolve()))
    else:
        print(f"{SIM_SRC.relative_to(ROOT)} does not exist yet; nothing to check.")
        return 0
    problems = []
    for f in files:
        problems.extend(check_file(f))
    if problems:
        print("\n".join(problems))
        print(f"\n{len(problems)} purity problem(s) in {len(files)} file(s).")
        return 1
    print(f"Sim purity check: {len(files)} file(s) clean.")
    return 0


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "--hook":
        sys.exit(run_hook())
    sys.exit(run_cli(sys.argv[1:]))
