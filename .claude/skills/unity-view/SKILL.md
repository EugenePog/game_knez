---
name: unity-view
description: Work in the Unity 6 project in game/ — the 2D top-down view (circles, order arrows, morale bars), input that issues sim commands, selection, camera, pause and speed controls, UI, assembly definitions, the local-package link to sim/src, and running Unity tests in batch mode. Use for any change under game/, for setting the Unity project up (milestone M2), or when the user asks how the view should talk to the sim.
---

# Unity view work

Unity renders the battle and turns input into commands. It never owns game state (ADR-0001).

## Project shape (M2)

```
game/
  Packages/manifest.json        "com.knez.sim": "file:../../sim/src/Knez.Sim"
  Assets/Knez/
    View/   Knez.Game.View.asmdef      → references Knez.Sim
    Input/  Knez.Game.Input.asmdef     → references Knez.Sim, Knez.Game.View
    Tests/  Knez.Game.Tests.asmdef     → EditMode tests (golden replays in Unity)
```

`sim/src/Knez.Sim` carries `package.json` and `Knez.Sim.asmdef` (`"noEngineReferences": true`) so Unity compiles
the same sources, plus `csc.rsp` (`-nullable:enable`). On first import, confirm Unity honours that file — no CS8632
nullable warnings from Knez.Sim; if it doesn't, add `#nullable enable` to the files instead. Unity can't see
`internal` members of Knez.Sim: the view uses only the public, read-only surface. Unity writes `.meta` files into that folder — commit them. Build output of `dotnet` must not land
inside the package folder (`Directory.Build.props` redirects it to `sim/artifacts/`).

## Rules

1. **Read, don't own.** The view reads `IReadOnlyBattleState`/snapshots and `SimEvent`s. GameObjects hold only
   view caches keyed by `EntityId` (sprite, label, previous position).
2. **Input = commands.** Clicks and hotkeys build `ICommand`s and enqueue them. Selection, camera and UI state stay in
   the view.
3. **Real time lives here.** A `BattleRunner` MonoBehaviour accumulates `Time.unscaledDeltaTime`, calls `Step()`
   while the accumulator ≥ 50 ms × speed, and interpolates positions with `alpha = accumulator / tickDuration`.
   Pause = stop stepping; commands are still accepted while paused.
4. **`Fix` → `float` conversion happens in the view** (`FixViewExtensions.ToFloat()`), never in `sim/src`.
5. **No gameplay in Unity:** no NavMesh, no Rigidbody/Collider for fighters, no animation-driven timing.
6. **Phase 1 look:** primitives only — circles, facing ticks, squad colours, order arrows, morale bars, a battle log.
   No art, no asset-store packages without asking.

## Editor work is the human's

Agents write C# and asmdefs. Creating scenes, prefabs, project settings and package installs is done in the editor:
describe the exact steps for the user instead of hand-editing YAML. Trivial YAML fixes (a missing reference GUID) are
fine if you explain them. Always commit `.meta` files; binary assets go through Git LFS (`.gitattributes`).

## Batch mode (tests, compile check)

Unity must not have the project open (the project lock makes batch mode fail).

```bash
UNITY="/Applications/Unity/Hub/Editor/<version>/Unity.app/Contents/MacOS/Unity"
"$UNITY" -batchmode -nographics -projectPath game -runTests -testPlatform EditMode \
  -testResults artifacts/unity/editmode.xml -logFile artifacts/unity/editmode.log
```

Find `<version>` with `ls /Applications/Unity/Hub/Editor/`. Read the XML for results and the log for compile
errors. Run in the background — the first import takes minutes.

## Cross-runtime determinism (M2 checkpoint)

`Knez.Game.Tests` contains an EditMode test that loads every `sim/tests/Golden/*.replay.json`, re-simulates it
under Unity's runtime and compares checkpoint hashes. If it fails while `dotnet test` passes, follow the
`determinism` skill — it is almost always a floating-point or ordering leak.
