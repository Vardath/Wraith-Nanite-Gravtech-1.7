## Wraith living-wall high-definition upgrade (14 October 2026)

The Wraith settlement wall is the **2× exception** to the original family
wall texture format: its in-mod `WNG_LivingWall_Atlas.png` is
**2048×2048 RGBA**, containing sixteen **512px** atlas cells with
**64px padding** around a **384×384** displayed tile. It still occupies
**one** RimWorld map square, with exactly the 9/192-scale open-edge setback
(18 pixels at this resolution). The other family hull atlases remain
unchanged at 1024px.

`wng_true_wall.py` uses the already-approved high-resolution original Wraith
paintings in `ArtSource/Walls/Wraith/Approved/` as primary material art. The
earlier build sampled tiny strips and blurred them across whole squares;
the 2× pass now crops broad, detailed sections of the original painted
roof armour, organic side ribs, north lip and darker front wall face.
Visible bone bevels occur only on exposed edges and never at linked joins.

The mod texture, legacy wall icon, Architect icon and four-by-four
contact sheet are generated together. Native `CornerFiller`, `Wall`/`Rock`
linking, room sealing, roof support, door replacement, 1×1 footprint
and Wraith regeneration are unchanged. The full 16-state set and all
compatible cardinal port combinations are checked for geometry and seam
pixels. The review sheet is
`ArtSource/Walls/Wraith/QA/HandPainted/Wraith-Living-Wall-16-States-Review.png`.
A game runtime appearance test is still necessary; CI does not simulate
lighting, doors or mixed vanilla/Wraith placement.

---

# WNG walls for RimWorld 1.6

The five functional walls use three material families but four linked atlases.
Wraith settlement art is independent from the Wraith gravship hull so each wall
can be redesigned and reviewed individually.

| Family | ThingDefs | Runtime atlas |
| --- | --- | --- |
| Wraith settlement | `WNG_WraithLivingWall` | `Wraith/Structure/WNG_LivingWall_Atlas` |
| Wraith gravship | `WNG_OrganicGravshipHull` | `Wraith/Gravship/WNG_OrganicHull_Atlas` |
| Asuran / Precursor | `WNG_PrecursorWall`, `WNG_PrecursorGravshipHull` | `Precursor/Gravship/WNG_PrecursorHull_Atlas` |
| Goa'uld | `WNG_GoauldGravshipHull` | `Goauld/Gravship/WNG_GoauldHull_Atlas` |

Atlas paths are under `Textures/Things/Building`. Wall lumens are attached lights,
and `WNG_ReplicatorStrippedWallHusk` is pass-through ruin scenery; neither is a
functional wall and neither belongs in the linked atlas pipeline.

## Geometry and native hooks

Each 1024×1024 RGBA atlas contains sixteen 256-pixel cells. RimWorld's
`MaterialAtlasPool` samples the central 192×192 pixels of each cell, inset by
32 pixels. Masks use N=1, E=2, S=4, W=8; PNG row is `3 - mask // 4`.
The native renderer prints that sampled area on an exact 1×1 map cell.

Gravship/Precursor/Goa'uld walls still retain their previous geometry (isolated
87.9% sampled coverage). The new Wraith living settlement wall has a broad 1x1 roof-facing cap,
with chitin bevels only on exposed boundaries and no frame at joined edges. Isolated Wraith mask0 measures approximately 85.9% opaque coverage,
and fully joined mask15 fills the complete sampled tile. The roof/cap is
continuous across horizontal, vertical, L, T, and cross connections.
A wall is rendered as a true 1x1 building, not a freestanding sprite.

All walls retain `Wall` inheritance, `CornerFiller`, Wall/Rock flags, native
roof/room/pathing/attachment/replacement behavior, damage marks and painting.
The two fixed-material settlement walls explicitly seal vacuum without a
stuff-dependent airtight query. Existing hull materials, recipes, abilities,
stats, research and placement workers are preserved.

Pieces 00, 01, 02 and 03 have independently approved masters under `ArtSource/Walls/Wraith/Approved/`. The generator uses the original high-resolution paintings for masks 00 and 01, and preserves approved native 192×192 RGBA masks 02 and 03 without scaling or repainting. The high-resolution source painting for mask 03 is also stored as `WraithLivingWall_link03_original_master.png`; its native source has the north and east connected edges and the south and west exposed edges. Masks 04–15 retain their existing generated artwork until separately approved. The Architect menu icon continues to use isolated piece 00. North/south seam matching with the unfinished adjacent masks remains a visual QA item.

The current Wraith settlement material is generated exclusively by
`Tools/WallArt/generate_wraith_settlement_wall.py`. The same muted
chitin/membrane material fills the roof cap on all sixteen masks. Only exposed
edges receive a short raised chitin lip and a recessed, shaded wall face.
Connected edges have no decorative frame. Each variant has precisely the
same interior material so straight runs, intersections and mixed
`Wall`/`Rock` connections read as continuous architecture.

The earlier `paint_wraith_chitin.py`, `VectorMasters/` SVGs and the
low-resolution WebP are rejected historical experiments. They are NOT
inputs to the current game textures. Do not restore their scutes, central
emblems or framed panels. Native atlas topology and the build-menu/legacy
graphics are regenerated in the actual WNG mod. Offline topology previews
are not live RimWorld evidence, so a player-side comparison with adjacent
vanilla built walls is still required. No native Wall.Print, link flags,
roof, room, material, blueprint, door or Odyssey placement logic was
modified by the artwork replacement.

Twenty-four full/partial hull overlays use the native 2×2 corner coordinates,
with different silhouettes for a present or absent diagonal neighbor. The
family layer keeps Odyssey's map masks and also appends its indoor corner
meshes to the native launch/landing bake.

## Rebuild and verify

For the independently authored Wraith settlement artwork, run
`python Tools/WallArt/generate_wraith_settlement_wall.py` first (Pillow and NumPy). It generates the PNG atlas and in-game single-tile Architect/legacy
icons. Use `--preview ArtSource/Walls/Wraith/QA/HandPainted` to render an
actual multi-wall test layout without opening the game.
The Wraith gravship hull atlas and angled corners remain untouched by that pass.
The original gravship material also has its own isolated-tile Architect icon,
`UI/WNG/Build/WNG_WraithGravshipHull`, so changing the settlement wall does not
silently change gravship build-menu artwork.

The older family-hull atlas generator still uses Node.js and ImageMagick (`magick`, or ImageMagick 6 `convert`) are needed to pack
the committed masters. Pillow and NumPy are needed for validation.

```sh
python Tools/WallArt/generate_wraith_settlement_wall.py --preview ArtSource/Walls/Wraith/QA/HandPainted
python Tools/WallArt/validate_wall_art.py
python Tools/WallArt/validate_wall_art.py --json wall-evidence.json
```

`--icons-only` and `--corners-only` support the older narrow workflows. The
authoritative masters live in `ArtSource/Walls`; the packer derives masks and
bevels without inventing replacement painting. Menu icons and legacy single
wall textures show the isolated mask, not an atlas sheet or four-way cross.

`--preview DIRECTORY` renders isolated tiles and a small wall topology at
gameplay scale for visual inspection. These are renderer simulations, not live
RimWorld screenshots. Check horizontal/vertical runs, ends, L/T/cross junctions,
thick intersections, and full versus partial angled hulls after future changes.

The focused validator checks all five defs, all 64 sampled atlas states,
compatible edge pairs, native linking and the painted Wraith silhouettes,
24 angled sprites, family icons,
padding, and the rendering hooks. Structural checks supplement visual review;
they do not claim a live save-load or launch test.

The isolated cinematic bake bridge also has a runnable harness:
`python Tools/WallArt/RuntimeTests/run_tests.py --dotnet /path/to/dotnet`.
It compiles the actual hull-layer source against small fixtures and tests native
geometry, masks, relative coordinates and mixed-family precedence. A production
Release build against the project's RimWorld 1.6 reference package is still
required before shipping the assembly.


## Wraith full 16-state reference-fitted pass — 14 October 2026

The living settlement wall now uses Tools/WallArt/wng_true_wall.py, called by
generate_wraith_settlement_wall.py. This preserves original authored
WraithLivingWall_link00..03_approved images and uses the authentic
high-resolution approved isolated wall only as the Wraith material,
not as a repeated standalone object at every link. The four original
approved paintings remain in ArtSource unchanged, but all 16 installed
runtime states are now fitted/constructed Wraith architecture.

REFERENCE PROVENANCE: A published ReBuild source copy of the Core Wall
Smooth, Planks and Bricks atlases. These three copies show identical
structural silhouettes and RGBA extents. They are not asserted to be
byte-identical to a fresh installed RimWorld 1.6 Core package.

MEASURED GEOMETRY: 1024x1024 atlas, 4x4 cells of 256px,
inset 32px to 192px native printed samples; 9px transparent margin
for each cardinal direction WITHOUT a linked neighbour.
The measured unlinked north cap runs from y9 to y21.
Roof surface: y22..101. Facade transition: y102..114.
The south-facing vertical wall face: y115..181, not a shallow 20px rim.
Unlinked west/east bevel faces are approximately 46px wide across
the roof surface. All link masks follow N=1 E=2 S=4 W=8.

The current pass is geometry-verified and has 64 E/W and 64 S/N
mask pairing checks in its generator, plus full-mod native atlas validation.
Offline room previews are not live RimWorld screenshots.
The result remains PENDING IN-GAME VISUAL ACCEPTANCE (roof, doors,
real vanilla adjacency, paint and scale at colony camera zoom).
No gravship or Asuran/Goa'uld runtime atlases changed.
