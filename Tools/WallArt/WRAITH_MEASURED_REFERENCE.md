# HD Wraith Living Wall — 16-State Artwork and Geometry

**Current HD art pass (14 October 2026).** The installed Wraith settlement
atlas now uses **2048 × 2048 RGBA**, with **512 × 512 atlas cells**,
**64px gutters**, and **384 × 384 pixels per rendered 1 × 1 wall**.
Vanilla-format reference data below is in 192px sample units. Multiply
every baseline pixel coordinate by **2** to compare the installed artwork.
For example, an exposed edge inset is 18/384px rather than 9/192px.

The material source is the **user-approved Wraith isolated-wall master**
stored unchanged at
ArtSource/Walls/Wraith/Approved/WraithLivingWall_link00_approved.jpg.
Roof plates, north lip, living side armour, and front façade are sampled
from **different broad, high-resolution painted portions** of that original.
Prior stretching of a tiny purple/grey texture strip is discontinued.
The actual runtime atlas, Architect icon and legacy one-tile sprite are
regenerated from those sources, not from illustrative AI sprite sheets.

**Geometry fixed in physical map cells**: the same N=1, E=2, S=4, W=8
linked-wall states, CornerFiller, drawSize (1,1), native Wall/Rock joining,
and exact native silhouette are maintained. All eligible E/W and N/S joins
are checked for equal edge texels. Padding is edge-clamped. The separately
approved earlier masks are preserved in the source folder (not modified).

**Artistic acceptance:** This is a procedural assembly of original painted
materials, not 16 independently commissioned/hand-painted finished
masters. Offline QA and CI establish atlas geometry and pixel continuity;
they do **not** establish aesthetic player approval or live RimWorld
compatibility. Playtesting against installed vanilla Core 1.6 art is
still required.

**Review pixels:** ArtSource/Walls/Wraith/QA/HandPainted/
Wraith-Living-Wall-16-States-Review.png is a reduced real-asset contact
sheet; Wraith-Living-Wall-Room-Test.png is an assembled-layout simulation.

---

# Wraith Living Wall — 16-State Measured Visual Baseline

Status: full reference-fitted art installed in WNG `WNG_LivingWall_Atlas.png`; offline and native-geometry checks completed; live RimWorld 1.6 game placement not verified.

## Provenance

- Reference copied from `Helixien/ReBuild-DoorsAndCorners`, directory `.unity/Assets/Data/ReBuild.COTR.DoorsAndCorners/Textures/Things/Building/Linked/Wall/`, variants `Wall_Atlas_Smooth.png`, `Wall_Atlas_Bricks.png`, `Wall_Atlas_Planks.png`.
- These are ReBuild copies of familiar RimWorld Core wall artwork paths, **not** verified bitwise-identical to installed RimWorld Core 1.6 textures. Never claim a current installed Core comparison without one.
- The approved original Wraith 00–03 masters remain unmodified in `ArtSource/Walls/Wraith/Approved/`. All installed runtime states now use painting sampled from the high-resolution mask00 master, mapped to measured geometry. This is not 16 individually hand-painted source masters.

## Native atlas extraction

- 1024×1024 RGBA atlas; 4×4 256×256 cells, row `3 - floor(mask/4)`.
- 32 px inset on every atlas cell, giving 192×192 actual `MaterialAtlasPool` printed samples.
- Native connection bits N=1 E=2 S=4 W=8; unconnected sides inset 9px in the displayed sample, connected sides extend to sample boundary.
- Inside a non-south-connected wall, y0–8 outside, y9–21 top rim, y22–101 roof, y102–114 roof-to-facade transition, y115–181 facade, y182 boundary, y183–191 outside.
- Isolated wall around x55–136 is a broad central roof slab between angled left and right wall edges.

## Individual counterpart geometry

| Mask | Connected neighbours | Visible bounding box within 192px sample | Width×height | Sample area |
|:---:|:---|:---|:---:|---:|
| 00 | None | (9,9)–(183,183) | 174×174 | 82.13% |
| 01 | N | (9,0)–(183,183) | 174×183 | 86.38% |
| 02 | E | (9,9)–(192,183) | 183×174 | 86.38% |
| 03 | N+E | (9,0)–(192,183) | 183×183 | 90.84% |
| 04 | S | (9,9)–(183,192) | 174×183 | 86.38% |
| 05 | N+S | (9,0)–(183,192) | 174×192 | 90.63% |
| 06 | E+S | (9,9)–(192,192) | 183×183 | 90.84% |
| 07 | N+E+S | (9,0)–(192,192) | 183×192 | 95.31% |
| 08 | W | (0,9)–(183,183) | 183×174 | 86.38% |
| 09 | N+W | (0,0)–(183,183) | 183×183 | 90.84% |
| 10 | E+W | (0,9)–(192,183) | 192×174 | 90.63% |
| 11 | N+E+W | (0,0)–(192,183) | 192×183 | 95.31% |
| 12 | S+W | (0,9)–(183,192) | 183×183 | 90.84% |
| 13 | N+S+W | (0,0)–(183,192) | 183×192 | 95.31% |
| 14 | E+S+W | (0,9)–(192,192) | 192×183 | 95.31% |
| 15 | N+E+S+W | (0,0)–(192,192) | 192×192 | 100.00% |

## Art production / validation

- `Tools/WallArt/wng_true_wall.py` generates each mask with Wraith chitin sides, membrane roof material and deep darker front-face shading, with exposed borders only where the neighbouring link is absent.
- 64 allowed E/W mask combinations and 64 S/N mask combinations are byte checked for identical cardinal-edge samples.
- `Tools/WallArt/generate_wraith_settlement_wall.py` packs actual mod textures, architect icon and legacy sprite; `.github/workflows/wng-wraith-settlement-wall-art.yml` commits the generated result.
- `Tools/WallArt/validate_wall_art.py` asserts measured silhouettes, coverage, facades, atlas gutters and seams. D120 CI and the wall-generation CI passed on 14 October 2026.

## Live acceptance boundary

Check in an actual RimWorld 1.6 colony: solitary piece; 1/2/3/8-piece horizontal and vertical runs; L corners; 4-way and T junctions; roofs; room sealing; doors; walls touching vanilla brick/plank/smooth walls; wall lights; paint and damaged states. Offline assembled-room spritesheets do **not** prove real-game visual harmony.
