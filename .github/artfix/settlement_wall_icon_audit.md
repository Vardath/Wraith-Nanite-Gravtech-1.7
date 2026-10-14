# Settlement wall linked-art audit (12 October 2026)

The Precursor wall `WNG_PrecursorWall` and Wraith living wall
`WNG_WraithLivingWall` use the same 16-state linked atlases as their
respective family gravship hulls. RimWorld's `MaterialAtlasPool` samples
an indexed sub-rectangle of the material from `Graphic_Linked`.
Removing the atlas breaks corner, run and door-adjacency artwork even if
the replacement is a visually attractive single tile.

The in-map graphics must therefore retain:
- Precursor `Things/Building/Precursor/Gravship/WNG_PrecursorHull_Atlas`
- Wraith `Things/Building/Wraith/Gravship/WNG_OrganicHull_Atlas`
- `Graphic_Single`, `CornerFiller` and Wall/Rock link flags

Before this fix, the XML incorrectly nested `uiIconPath` under
`graphicData`; `uiIconPath` is a property of `BuildableDef`.
The former Precursor icon PNG also had visibly corrupted bottom rows,
while the Wraith fallback artwork was itself a 4x4 atlas, not one icon.
`.github/artfix/generate_settlement_wall_icons.py` delegates to
`Tools/WallArt/assemble_wall_art.mjs --icons-only`. It uses the sampled
192x192 isolated mask0 tile for each family icon, including the Goa'uld hull.
The standalone Precursor/Wraith gallery fallbacks use that same sampled tile.

The new `D121 Settlement Family Validation` asserts paths, correct XML
nesting, icon existence and single-tile PNG dimensions.
`Tools/WallArt/validate_wall_art.py`, run by D120, also checks complete native
tile coverage, all 16 mask orientations, compatible edge seams, clamp gutters,
native corner silhouettes, isolated icons and inherited wall hooks.

Expected QA: each wall icon shows one complete capped tile. Connected map tiles
fill the native 1x1 space and retain `CornerFiller` connections to walls, rocks
and family doors. The new 1024x1024 family atlases preserve each family's design.
