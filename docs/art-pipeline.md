# Art pipeline (Unity battle view)

How the 2D battle view draws the designed art instead of bare discs and rectangles. Added 2026-11-26 at the user's
request (roadmap, "Art in the phase-1 view"). The drawings and their contract are in
[docs/design/art-style.md](design/art-style.md) and [docs/art-assets.md](art-assets.md); this page is the view side.

## Where the art lives

```
docs/design/art/paperdoll.js, objects.js   generators (people, buildings); unit-viewer.html shows them
tools/art/export_game_art.js               SVG → PNG with headless Chrome, writes the .meta files and the manifest
game/Assets/Knez/Art/Resources/
  Tokens/  tok_*.png    people from above, 200 px/m, pivot = body centre (0.5, 0.4), drawn facing north
  Map/     map_*.png    buildings and objects from above, 64 px/m, pivot = footprint centre
  Portraits/, Objects/  front and 3/4 views for UI cards (not used by the battle view yet)
  art-manifest.json     every sprite with what it shows (side, kit, experience, footprint, walls)
  palette.json          the art's named colours; the view takes the ground's grass from "map"
```

It is a `Resources` folder so the view loads the manifest and every sprite at run time by the manifest's own paths
(`Resources.Load<Sprite>("Tokens/tok_clan_axe-shield_0")`): no scene, prefab or Inspector wiring. Everything in
`Resources` ships with every build; move `Portraits/` and `Objects/` out when build size starts to matter. PNGs go
through Git LFS (`*.png` in `.gitattributes`); import settings (pixels per unit, pivot, mipmaps) are in the generated
`.meta` files and survive a re-export.

## Adding or changing art

1. Change the drawing in `docs/design/art/paperdoll.js` (people) or `objects.js` (buildings); check it on
   `docs/design/art/unit-viewer.html`.
2. `node tools/art/export_game_art.js` (needs Chrome; `CHROME=/path` if it isn't in the default place). It rewrites
   the PNGs and both JSON files and keeps existing `.meta` files, so GUIDs don't change.
3. Switch to Unity (it reimports on focus), then run the tests below.
4. Commit the PNGs with their `.meta` files: `git add -A game/Assets/Knez/Art` (LFS takes the PNGs by itself).

A new kit or side added to the exporter appears in the manifest and the view uses it with no code change. A new
building with `sim: "house"` joins the houses the view fits to walls; the objects that stand for plain obstacles and
fences are named in `WallArt.Rocks` and `WallArt.Fence`.

## How the view picks

All in `BattleView.Build`, from the plain-C# classes below; nothing is decided per frame except the state.

**Art side.** The sim only knows side numbers. `BattleView`'s **Side Art** list (Inspector) gives the art side per
side id: `clan`, `ruler` or `bandits`; default `clan`, `ruler`. Set side 1 to `bandits` for a bandit scenario.

**Token** (`FighterArt.Token`), the first that exists:

1. a Retainer by name: the roster's named man (`hrafn`, `vesti`) on his side in his kit;
2. a Retainer: a generic Retainer of his side in his kit;
3. an unnamed fighter of his side in his kit;
4. a Retainer: a generic Retainer in the drawn kit nearest to his;
5. an unnamed fighter in the nearest kit (same weapon first, then body armour, head cover, shield).

The kit beats the rank because the kit is what the player reads; the gold ring already marks a Retainer. Within the
kit: his experience if drawn for it, then variant `id % count`, so a squad's men look different. Experience shows on
the art only where the export draws it (Retainers: professional and elite); men of one side and kit share a look.

**State** (`FighterArt.State`), applied every frame:

| Sim | Token | Squad base disc |
| --- | --- | --- |
| steady, shaken, wavering, rallying | full colour | squad colour |
| routing | 65 % opaque | squad colour faded toward white (as before) |
| Down (fallen) | grey (the old fallen colour; Unity can't desaturate a sprite without a shader, so it is a dark tint), drawn under the living | hidden |
| fled, left the field | 30 % opaque, where he went | hidden |

Wounds keep the red dot (light small, heavy big); the art itself doesn't change for a wound (art-style.md).

**Facing.** The token is a child of the man's body, turned −90° (drawn facing north, the sim's 0° is east), so it
turns with him. The facing tick is hidden when a token is drawn.

**Overlays** stay as they were, by sorting order: ground −10, wall plot −5, selection halo −1, map art and battle-cry
disc 0, Retainer ring, routes and lone-Retainer lines 1, squad base disc and aim lines 2, token 3 (fallen and gone
tokens 2, under the living), wound dot and squad anchor 4, blow flash 5–6, morale bar 7–8. With a token on, the base disc is 1.25 × the
body so its colour shows as a rim (the ring and halo grow with it); the wound dot keeps its size.

**Walls** (`WallArt.Place`). Scenarios place bare rectangles, so the view chooses:

- a **house** wall gets the building (`sim: "house"`, one wall) whose footprint, turned 0° or 90° and scaled
  uniformly to fit inside the wall, covers most of it, scale 0.75–1.5, a slight preference for the drawn size;
- an **obstacle** gets the rocks (`boulders`), scale 0.5–2.5;
- an obstacle at least 4 times longer than thick is a **fence**: `palisade` lengths along it, scaled 0.5–1.5 so a
  whole number of them fills it.

The sprite is centred on the wall by the object's own wall offset, never stretched one way only. The wall's rectangle
stays under it as a faint plot (35 % of the old colour) so what the sim blocks is still visible. Any building may be
picked, so a 12 × 6 house can be drawn as the ruler's hall; scenarios that place catalogue objects by id would fix
that (not built).

**Fallbacks.** No manifest in `Resources`, or one that won't parse: a warning, and the whole battle uses the
primitives. A missing sprite: a warning once, and that man or wall stays a primitive. **Use Art** off on
`BattleView`: primitives only (the phase-1 look).

## Code

| File (`game/Assets/Knez/View/`) | What |
| --- | --- |
| `ArtCatalog`, `TokenArt`, `MapObjectArt` | the manifest and palette, parsed with `Knez.Sim.Content.Json` (plain C#) |
| `FighterArt`, `TokenState` | sim fighter → token path and look (plain C#) |
| `WallArt`, `WallSprite` | sim wall → map sprites placed (plain C#) |
| `ArtLibrary` | Unity side: loads the manifest, palette and sprites from `Resources`, caches per battle |
| `BattleView` | builds the tokens and wall art, applies the state each frame |

The plain-C# classes read sim state only and hold nothing between battles; `ArtLibrary` is made per
`BattleView.Build` (no statics, so Play mode without domain reload is safe).

## Tests

- `Tests/ArtTests.cs` — the mapping against the real manifest and PNGs: every content kit × experience × side has a
  token, Retainers by name and experience, variants, nearest kit, state table, every wall of `sandbox/villages`
  drawn, fence tiling, offsets. Plain C#: runs in Unity's EditMode and under .NET with the scratch test project the
  `unity-view` skill describes (compile `TokenArt`, `MapObjectArt`, `TokenState`, `ArtCatalog`, `FighterArt`,
  `WallSprite`, `WallArt` and `ArtTests.cs` with a `ContentFiles` stand-in).
- `Tests/BattleViewTests.cs` — in Unity only: `Build_Villages_DrawsArtForEveryManAndWall` (a token per man, a sprite
  per wall, loaded through `Resources`) and renders of `sandbox/villages` at ticks 0 and 1200 to `artifacts/unity/`.

## Seeing it

In the editor: select the object with `BattleRunner` and `BattleView` in `SampleScene`, set **Scenario** to
`scenarios/sandbox/villages.json`, press Play. **Use Art** and **Side Art** are on `BattleView`.
