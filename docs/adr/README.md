# Architecture decision records

One file per hard-to-reverse decision: `NNNN-short-title.md`, numbered in order. Never rewrite an accepted ADR;
supersede it with a new one and link both ways.

| ADR | Title | Status |
|---|---|---|
| [0001](0001-pure-csharp-sim.md) | Battle simulation is pure C# outside Unity | Accepted |
| [0002](0002-fixed-point-math.md) | Fixed-point math in the simulation | Accepted |
| [0003](0003-tick-and-command-model.md) | Fixed tick and tick-stamped commands | Accepted |
| [0004](0004-content-json-reader.md) | Content JSON is read by our own small reader | Accepted |
| [0005](0005-fighter-body-experience-kit.md) | A fighter is a body, an experience level and a kit | Accepted |

## Template

```markdown
# NNNN. Title

Status: Proposed | Accepted | Superseded by NNNN
Date: YYYY-MM-DD

## Context
What forces the decision; constraints from the concept or earlier ADRs.

## Decision
What we do, stated so an agent can follow it.

## Consequences
What gets easier, what gets harder, what we must now enforce (tests, checks).

## Alternatives considered
Each with the reason it lost.
```
