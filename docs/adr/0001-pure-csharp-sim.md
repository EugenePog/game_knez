# 0001. Battle simulation is pure C# outside Unity

Status: Accepted
Date: 2026-11-19

## Context
The concept fixes Unity 6 as the engine but starts with a 2D prototype and moves to 3D later. Development is solo
with LLM agents, which work best with code they can build and test from the command line. The 3D view must read
the same battle without rewriting combat.

## Decision
All battle rules and state live in `Knez.Sim` (`sim/src/Knez.Sim`), a plain C# library with no reference to
`UnityEngine`/`UnityEditor`. It is tested with NUnit through `dotnet test`. Unity code only renders state and turns
input into commands. GameObjects hold no game state.

## Consequences
- Agents can implement and verify mechanics without the editor.
- The 2D prototype, replay viewer and 3D view share one sim.
- We write our own navigation, collision and spatial queries (no NavMesh, no physics in gameplay).
- `Knez.Sim` is limited to what Unity 6 compiles: `netstandard2.1`, C# 9.
- Enforced by `tools/check_sim_purity.py` (hook + CI) and the `sim-reviewer` agent.

## Alternatives considered
- **Gameplay in MonoBehaviours / Unity ECS:** fastest to start, but untestable outside the editor, binds the
  prototype to the final view, and breaks the 2D → 3D path.
- **A separate engine for the prototype:** throws the work away at the 3D switch.
