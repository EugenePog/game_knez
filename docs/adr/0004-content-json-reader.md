# 0004. Content JSON is read by our own small reader

Status: Accepted
Date: 2026-11-20

## Context
Content (fighter and formation defs, scenarios) is JSON in `content/` (battle-sim.md D9). It is loaded by
`Knez.Sim.Content`, which must compile in plain .NET and inside Unity 6 (netstandard2.1, C# 9) and — like the sim —
must be deterministic: numbers such as `0.3` become `Fix` values that are part of every state hash, so they must
never pass through `double` (ADR-0002). Unity's IL2CPP strips code that is only reached by reflection, which is how
general-purpose JSON libraries map objects.

## Decision
`Knez.Sim.Content` contains its own JSON reader (`Knez.Sim.Content.Json`): it parses standard JSON (RFC 8259) into
an immutable tree and keeps every number as its source text. Loaders turn the tree into defs by hand:
- numbers are converted with integer arithmetic only (`ContentNumbers`: decimal text → `Fix`, rounded to the nearest
  1/65536, ties away from zero; exponents and more than 9 fractional digits are rejected);
- every object is read strictly: missing required members, unknown members (typos) and wrong types are errors that
  name the file, line, column and JSON path (`squads[1].formation`);
- no reflection, no external packages. File access stays outside (CLI, Unity): loaders take text;
- def constructors throw `ArgumentException(message)` without a parameter name, because CoreCLR and Mono format that
  suffix differently and the message ends up in front of a person editing JSON.

`Knez.Sim.Content` is a second Unity local package next to `Knez.Sim` and is covered by the purity check.

## Consequences
- Identical behaviour in .NET, Mono and IL2CPP; nothing to strip, no DLL version conflicts with the packages Unity
  ships (Newtonsoft, System.Text.Json).
- Exact, documented number parsing: `0.1` is always raw 6554.
- Error messages fit our content (path + position), which matters when agents and the designer edit JSON by hand.
- We maintain ~300 lines of parser plus tests, and each def needs a small hand-written loader. Content is small and
  changes slowly, so this stays cheap.
- Writing JSON (replay and frame files) is done by the CLI with System.Text.Json; Unity will need its own writer
  only if it saves replays (M2+).

## Alternatives considered
- **Newtonsoft.Json** (Unity ships `com.unity.nuget.newtonsoft-json`): mature, but object mapping relies on
  reflection (IL2CPP stripping, `link.xml`), and exact number handling needs `FloatParseHandling.Decimal` everywhere —
  easy to forget, and a single default (`double`) breaks determinism silently.
- **System.Text.Json** (NuGet package for netstandard2.1): `Utf8JsonReader` exposes raw number text, but in Unity it
  drags System.Memory, System.Buffers and friends that collide with Unity's own copies.
- **Unity JsonUtility**: Unity-only, so tests and the CLI couldn't use it; parses numbers as `float`.
