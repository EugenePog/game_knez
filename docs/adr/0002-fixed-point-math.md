# 0002. Fixed-point math in the simulation

Status: Accepted (recommended by Claude on 2026-11-19; reverse before M1 starts if you disagree — it gets
expensive after movement code exists)
Date: 2026-11-19

## Context
Determinism is a core requirement: replays as bug reports, reproducible balance batches, A/B comparisons, and a
door left open for multiplayer. The same sim runs under CoreCLR (tests, CLI), Mono and IL2CPP (Unity), on ARM64
(the dev Mac) and x64 (most players). Floating-point results can differ across these: fused multiply-add
contraction, different `Math.Sin/Atan2` implementations, compiler reordering. A replay recorded on a player's
Windows PC must reproduce on the dev's Mac.

## Decision
- No `float`/`double` in sim state or logic. Use `Fix` (Q48.16 over `long`), `FixVec2`, `Angle` (16-bit binary
  angle) and `FixMath` (integer sqrt, table-based trig, integer atan2).
- Probabilities and percentages are integers in permille or `Fix`.
- Content is parsed from decimal text straight into `Fix` with integer arithmetic.
- `float` appears only on the view side for rendering.

## Consequences
- Bit-identical results across runtimes and CPUs; cheap hashes catch any divergence.
- Slightly more verbose math code; need a small, well-tested math library in M0/M1.
- Overflow is a new failure mode: keep values in game range (|x| < ~45 000 for products) and test edge cases.
- Enforced by the purity check (`float`, `double`, `Math.*` trig, float literals) and by cross-runtime golden
  replay tests from M2.

## Alternatives considered
- **Floats with care (same binary, strict settings):** works on one machine, but not across Mono/IL2CPP/CoreCLR and
  ARM/x64; desyncs would show up late and be hard to find.
- **Pure integers (millimetres, permille) everywhere:** deterministic, but steering and normalisation get awkward;
  `Fix` keeps the same guarantees with readable vector math.
- **Third-party fixed-point library:** possible later, but our needs are small and most libraries target
  newer C# than Unity's C# 9.
