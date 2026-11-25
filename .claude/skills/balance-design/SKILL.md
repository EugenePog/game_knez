---
name: balance-design
description: Set balance targets and check balance for Game_knez — battle balance (win rates, battle length, casualties, when squads break, unit value vs cost, counters, morale pacing) measured on the real sim, and campaign balance (income vs costs, progression, difficulty curve) modelled numerically. Use for "is X too strong", "what should the numbers be", difficulty, tuning proposals or comparing variants. Never edits defs — it writes measured change proposals. Builds on game-design.
---

# Balance design

Follow `game-design` for boundaries and workflow. Doc: `docs/design/balance.md` — targets, measurements, proposals.

## Targets first

Balance is "results match intent". Write the intent as measurable targets **before** tuning and get the user's yes:

| Area | Example target (proposal — the user sets real ones) |
|---|---|
| Skirmish length | 2–4 min of battle time per fight |
| Player win rate, sensible orders | 60–75 % on the phase-1 skirmish |
| Player win rate, no orders at all | ≤ 25 % (orders must matter) |
| How battles end | most end by a rout, not by everyone Down |
| Squad break | after ~25 % losses (morale lossWeight decided 2026-11-23) |
| Unit value | measured power ratio within ±20 % of the cost ratio |
| Dominance | no single formation/type wins every matchup |
| Act I economy | tribute payable on time with 2 of 3 good major choices |

## Battle balance — measure, don't guess

- Use the `battle-analyst` agent: batches over ≥ 200 seeds, A/B against a baseline, variants as scenario copies under
  `artifacts/experiments/` (never in `content/`). It reports tables; you interpret them against the targets.
- Ask it for the metrics the targets need (win rate, duration, casualties by wound level, routs, time to first break).
  If the CLI lacks a metric, note it as a tooling request — don't work around it.
- Matchup grid: type vs type and formation vs formation in equal-cost fights; compare with the `unit-design` value
  scale.
- Win-rate differences under ~10 points at 200 seeds (~4.5 at 1000) are noise.
- The measured reference (D-026) is the table in `docs/battle-sim.md` §11 (S6, S7, `m3-*`, sandbox, M3 step 7):
  compare against it, not against memory.
- Look at a few notable seeds in the replay viewer (`battle-run`) — numbers can hide a degenerate shape (one squad
  doing all the work, endless shuffling, routs that never rally).

## Campaign balance — model

Use the `economy-modeler` agent: season-by-season simulation of strategy archetypes with stated assumptions,
sensitivity to the 3–5 most important numbers, and a check of the act I pressure curve. A model only ranks options and
finds broken loops; it doesn't prove fun.

## Change proposals (what this skill outputs)

| Def / doc | Field | Now | Proposed | Why (target) | Measured effect | Decision |
|---|---|---|---|---|---|---|

Rules: one lever at a time when possible; prefer changing a placeholder over an agreed number; numbers that define feel
need the user's yes and a D-NNN entry. Accepted rows go to a coding session (they change golden hashes and the def's
`description`).

## Playtests beat models

The phase-1 gate is subjective ("interesting the 20th time"). When a target and a playtest impression disagree, the
impression wins and the target gets rewritten. Link to `docs/playtests/` sessions where relevant.
