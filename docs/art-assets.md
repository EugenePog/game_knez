# Art assets for the Unity view

Sprites and data the game can use, exported from the design drawings (`docs/design/art-style.md`, generators in
`docs/design/art/`). Added 2026-11-26 at the user's request. `BattleView` draws the tokens and map sprites (tokens
over the squad discs, sprites over the wall squares); how it picks them is in [art-pipeline.md](art-pipeline.md).

## Where and what

All under `game/Assets/Knez/Art/Resources/` (a Unity `Resources` folder: the manifest's paths load as they are with
`Resources.Load`, [art-pipeline.md](art-pipeline.md)). PNG with alpha (Git LFS), each with its `.meta`; importer settings are already in
the `.meta`, so the assets come in the right size and pivot. Sim metres are Unity units, X east, Y north
(`FixViewExtensions`).

| Folder | Content | Pixels per unit | Pivot | Mipmaps | Use |
| --- | --- | --- | --- | --- | --- |
| `Tokens/` | people seen from above, 1.28 × 1.6 m canvas | 200 | body centre (0.5, 0.4) | on | battle view, map |
| `Map/` | buildings and map objects from above | 64 | centre of the footprint (0.5, 0.5) | on | battle view, map |
| `Portraits/` | people from the front, 360 × 528 px | 100 | between the feet (0.5, 0.034) | off | UI: cards, recruitment |
| `Objects/` | buildings and objects in a 3/4 view | 100 | centre | off | UI: object cards |
| `art-manifest.json` | every sprite with its metadata (below) | | | | lookup |
| `palette.json` | named colours of the art (dyes, shield pigments, metals, map materials) | | | | HUD and UI colours |

Tokens are drawn facing north (+Y, sim facing 90°; facing is degrees counter-clockwise from east). Set the token's
z rotation to `facing − 90`. A token with a shield is a fighter; a token without one is a civilian.

## Names

| Pattern | What |
| --- | --- |
| `tok_{side}_{kit}_{v}`, `por_{side}_{kit}_0` | unnamed fighters: art side × kit from `content/defs/combat.json` × variant 0–3 (portrait for variant 0) |
| `tok_hero_{id}`, `por_hero_{id}` | Retainers: named (`hrafn`, `vesti`, `ruler-retainer`) and generic `hero_{side}_{kit}_{v}` |
| `tok_civ_{id}`, `por_civ_{id}` | civilians: design presets (`farmer`, `elder`, `housewife`, …) and generic `civ_{side}_{m\|f}_{v}` |
| `map_{id}`, `obj_{id}` | map objects: top-down sprite and 3/4 view |

Art sides are `clan` (the player: yellow ochre and soot shields), `ruler` (red ochre and chalk) and `bandits`
(unpainted wood). The sim knows only side numbers: which art side a sim side is comes from the scenario
(side 0 is the clan; side 1 is the ruler or bandits, by the enemy kind in `docs/design/enemies.md`).

## Manifest

`art-manifest.json` (`version` 1):

- `conventions` — the table above, as data.
- `sides` — per art side: `name`, `shield` colours, `pattern`, `militiaBand` (the stripe settlements paint on
  militia shields).
- `fighters[]` — `side`, `kit`, `role` (militia / warrior / retainer / bandit, how the man is dressed), `experience`,
  `variant`, `token`, `portrait` (variant 0 only). Pick by (art side, kit), then vary by fighter id, e.g.
  `variant = id % 4`, so a squad's men look different.
- `heroes[]` — `id`, `title`, `side`, `kit`, `experience`, `named`, `token`, `portrait`. Match a Retainer by name
  (lower case) first, else take a generic one of his side and kit.
- `civilians[]` — `id`, `side`, `sex`, `wealth` (0–2), `style` (women's dress), `preset`, `token`, `portrait`.
- `map[]` — `id`, `name`, `group`, `size` (footprint, m), `height` (m), `sim` (`house`, `obstacle`, `water`, `free`),
  `sprite`, `spriteSizeMetres`, `walls[]`, `view`, plus `where` and `note` from the design.

`walls[]` are the rectangles the object blocks, each `center` (m, relative to the sprite pivot, Y north), `size` (m)
and `kind` (`house` / `obstacle`, as `MapDef.walls`). For a scenario, an object placed with its pivot at `p` gives
the wall `from = p + center − size/2`, `to = p + center + size/2`. Turn objects only by multiples of 90° (walls are
axis-aligned): rotate the sprite and swap each wall's axes. Bridge and ford are `water` objects: their walls are
the water beside the deck, the ford has none; the sim has no water yet (open question in
`docs/design/open-questions.md`). Trees block only the trunk, except the spruce (whole crown).

Existing scenarios have houses of any size (`kind: "house"`). Until scenarios place catalogue objects, the view
picks the house sprite whose footprint fits best and centres it in the wall, rocks for obstacles and a palisade for
long thin ones (`WallArt`, [art-pipeline.md](art-pipeline.md)); sprites are never stretched non-uniformly.

## Regenerating

```bash
node tools/art/export_game_art.js
```

It rasterises the generators with headless Chrome (set `CHROME=/path` if it isn't in the default place), rewrites
the PNGs and the two JSON files, and keeps existing `.meta` files, so GUIDs and any importer changes made in Unity
survive. New files get a GUID derived from their path. To change a drawing, edit `docs/design/art/paperdoll.js`
(people) or `objects.js` (buildings), check it on the design page (`docs/design/art/unit-viewer.html`), then
re-export. Commit the PNGs (LFS) together with their `.meta` files.
