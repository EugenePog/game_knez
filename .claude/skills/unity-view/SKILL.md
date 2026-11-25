---
name: unity-view
description: Work in the Unity 6 project in game/ — the 2D top-down view (circles, order arrows, morale bars), input that issues sim commands, selection, camera, pause and speed controls, UI, assembly definitions, the local-package link to sim/src, and running Unity tests in batch mode. Use for any change under game/, for setting the Unity project up (milestone M2), or when the user asks how the view should talk to the sim.
---

# Unity view work

Unity renders the battle and turns input into commands. It never owns game state (ADR-0001).

## Project shape (M2)

```
game/
  Packages/manifest.json        "com.knez.sim": "file:../../sim/src/Knez.Sim",
                                "com.knez.sim.content": "file:../../sim/src/Knez.Sim.Content"
  Assets/Knez/
    View/   Knez.Game.View.asmdef      → references Knez.Sim
    Input/  Knez.Game.Input.asmdef     → references Knez.Sim, Knez.Game.View
    Tests/  Knez.Game.Tests.asmdef     → EditMode tests (golden replays in Unity)
```

`sim/src/Knez.Sim` carries `package.json` and `Knez.Sim.asmdef` (`"noEngineReferences": true`) so Unity compiles
the same sources, plus `csc.rsp` (`-nullable:enable`); `sim/src/Knez.Sim.Content` is a second package the same way
(depends on `com.knez.sim`). The view loads content text itself (e.g. `TextAsset`/StreamingAssets) and passes it to
`DefLibrary.Parse` / `ScenarioLoader.Parse` — content code never opens files. On first import, confirm Unity honours that file — no CS8632
nullable warnings from Knez.Sim; if it doesn't, add `#nullable enable` to the files instead. Unity can't see
`internal` members of Knez.Sim: the view uses only the public, read-only surface. Unity writes `.meta` files into that folder — commit them. Build output of `dotnet` must not land
inside the package folder (`Directory.Build.props` redirects it to `sim/artifacts/`).

## Rules

1. **Read, don't own.** The view reads `IReadOnlyBattleState`/snapshots and `SimEvent`s. GameObjects hold only
   view caches keyed by `EntityId` (sprite, label, previous position).
2. **Input = commands.** Clicks and hotkeys build `ICommand`s and enqueue them through `BattleRunner.Enqueue` (not
   `Simulation.Enqueue`), so they land in the session file written when Play stops (`artifacts/sessions/`, see
   `battle-run`). Selection, camera and UI state stay in the view.
3. **Real time lives here.** A `BattleRunner` MonoBehaviour accumulates `Time.unscaledDeltaTime`, calls `Step()`
   while the accumulator ≥ 50 ms × speed, and interpolates positions with `alpha = accumulator / tickDuration`.
   Pause = stop stepping; commands are still accepted while paused.
4. **`Fix` → `float` conversion happens in the view** (`FixViewExtensions.ToFloat()`), never in `sim/src`.
5. **No gameplay in Unity:** no NavMesh, no Rigidbody/Collider for fighters, no animation-driven timing.
6. **Phase 1 look:** primitives only — circles, facing ticks, squad colours, order arrows, morale bars, a battle log.
   No art, no asset-store packages without asking.
7. **What the view remembers lives in `BattleRecord`** (M3 step 8): `SquadTally` (who started where, blows),
   `MoraleLedger` (per man, morale moved by reason since the start or his squad's last re-form) and `BattleLog` (the
   battle in words), all plain C# fed from events after each tick; the words come from `HudText`. Keep logic there,
   out of `MonoBehaviour`s, so it can be tested without the editor (below). battle-sim.md §16 says what the view
   explains.
8. **Input over the HUD isn't for the map:** `BattleInput` asks `BattleHud.Covers(screenPoint)` before starting a
   click or zooming with the wheel. A new IMGUI panel adds its rect to the HUD's panel list.

## Unity's null

Never use `??`, `??=` or `?.` on `UnityEngine.Object` references (components, sprites, materials, cameras): a
destroyed object, or an empty Inspector field in the editor, is a "fake null" that only Unity's `==` sees as null.
This project enters Play mode without a domain reload, so statics survive between runs while the objects they cache
are destroyed — re-create them behind `if (x == null)`. (Found in the M2 check: sprites vanished on the second Play,
and input never ran because the camera field stayed "None".)

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

### Typecheck while the editor is open

Batch mode needs the editor closed. To check that view code compiles while the user has Unity open, build a throwaway
`netstandard2.1` / C# 9 project in the scratchpad that compiles `game/Assets/Knez/{View,Input,Tests}/*.cs`, references
Unity's own NUnit (`game/Library/PackageCache/com.unity.ext.nunit@*/net472/unity-custom/nunit.framework.dll` — it is
NUnit 3.5: no `Assert.Multiple`, so never use it in `game/` tests; a newer NUnit package hides that) and
`game/Library/ScriptAssemblies/Unity.InputSystem.dll`, references `sim/src/Knez.Sim` and `sim/src/Knez.Sim.Content` as projects, and
references every DLL in `/Applications/Unity/Hub/Editor/<version>/Unity.app/Contents/Resources/Scripting/Managed/UnityEngine/`.
Add `CS0649` to `NoWarn` (Unity fills `[SerializeField]` fields; its compiler doesn't flag them). Set
`<Nullable>disable</Nullable>` and `TreatWarningsAsErrors`: Unity compiles with nullable off, so a `?` annotation in a
file without `#nullable enable` warns (CS8632) — every `game/` file that uses `?` on a reference type needs the directive. It proves the code
compiles, not that it behaves — the user's Play (or batch mode later) is still the test.

### Testing view logic while the editor is open

The plain-C# view classes (`HudText`, `SquadTally`, `MoraleLedger`, `BattleLog`, `BattleRecord`, …) and their EditMode
tests run under .NET too: a throwaway `net10.0` test project in the scratchpad that compiles those files and the test
files (`<Compile Include=…>`), references `sim/src/Knez.Sim` and `Knez.Sim.Content` as projects and NUnit 3.14 (the
3.x API, like Unity's 3.5), and a small `ContentFiles` stand-in in namespace `Knez.Game.View` whose `DefaultRoot()`
returns the repo's `content/` (the real one asks `UnityEngine.Application`). It proves the logic; the typecheck above
proves the MonoBehaviours compile; Play (or batch mode once the editor is closed) is still the test of the rest.

## Cross-runtime determinism (M2 checkpoint)

`Knez.Game.Tests` contains `CrossRuntimeGoldenTests`: it runs every entry of `sim/tests/Golden/content-traces.json`
(each content scenario, its per-tick hash trace and final hash) under Unity's runtime; `ContentTraceGoldenTests` checks
the same file under .NET. Replay files (`*.replay.json`, with commands) join it when the CLI writes them. If it fails while `dotnet test` passes, follow the
`determinism` skill — it is almost always a floating-point or ordering leak.
