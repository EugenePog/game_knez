---
name: playtest-notes
description: Record a playtest session of the skirmish prototype and track progress toward the phase-1 exit criterion ("the battle is still interesting the 20th time"). Use when the user says they played a battle, wants to log impressions, asks how many sessions are left, or wants to turn playtest feedback into the next tasks.
---

# Playtest notes

The phase-1 gate is subjective, so it needs a written trail. Each session gets one file; the index keeps the count.

## Never invent impressions

The user's feelings about the game are the data. Ask for them; quote them. You may add facts you can read
(commit, scenario, replay metrics, bugs reproduced), clearly marked as yours.

## Steps

1. Ask the user (one short message, skip what they already said):
   - which build/commit and scenario, which seed or replay file if saved;
   - result and roughly how long it took;
   - what was fun or tense (moments), what was boring, confusing or annoying;
   - did orders do what they expected; did morale changes make sense;
   - "Would you start another battle right now?" — 1 (no) to 5 (immediately);
   - bugs seen.
2. If a replay file exists, run `replay verify` and pull the summary metrics (see `battle-run`).
3. Write `docs/playtests/YYYY-MM-DD-NN.md` with the template below.
4. Update `docs/playtests/README.md`: add the row, recount sessions, note the trend of the "again" score.
5. Propose at most 3 next changes, each tied to a quoted observation, and ask which to do. Bugs become tasks;
   design changes need the user's yes.

## Session template

```markdown
# Playtest YYYY-MM-DD #NN

- Build: <commit> · Scenario: <file> · Seed/replay: <…>
- Result: <side won / lost>, <duration>
- Play again score: N/5
- Session number toward exit criterion: N

## Moments (user's words)
- …

## Boring / confusing
- …

## Orders and morale
- …

## Bugs
- …

## Metrics (from replay, added by agent)
| casualties P/E | routs | stuck fighter-ticks | duration |
|---|---|---|---|

## Hypotheses for next change
1. …
```

## Index (`docs/playtests/README.md`)

Create it on first use:

```markdown
# Playtests

Exit criterion for phase 1: the battle is still interesting the 20th time.

| # | Date | Build | Scenario | Again (1–5) | Main takeaway |
|---|---|---|---|---|---|
```

The criterion is met when the user says so; a sustained "again" score of 4–5 over the last several sessions is the
evidence to point at, not a substitute for asking.
