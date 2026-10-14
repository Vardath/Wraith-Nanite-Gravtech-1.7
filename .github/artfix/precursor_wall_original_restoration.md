# Historical Precursor settlement-wall image repair

Superseded on 12 October 2026 by the authored full-width family wall art.
`Tools/WallArt/assemble_wall_art.mjs` now derives this gallery fallback from the
isolated native mask0 sampled tile (192x192). When the reviewed master exists at
`ArtSource/Walls/Precursor/master.png`, the old repair script exits without
rewriting the authored fallback. The description below records the earlier fix.

The original art-gallery image `Textures/Things/Building/Precursor/Structure/WNG_PrecursorWall.png`
was a 256×256 RGBA single wall panel. The lower part of the PNG had coloured
garbage rows beginning at y=211, including a full-width opaque band at y=244.
This was an image-pixel defect, not a `ThingDef` or gravship-linking bug.

`.github/artfix/repair_precursor_wall_original.py` preserves rows 0–210
exactly, reconstructs damaged rows 211–255 by vertical reflection of the
corresponding intact upper-frame rows, verifies the transparent margins and
PNG integrity, and writes only that existing source image.

The following remain unchanged by the image repair:
- Precursor in-game linked wall 16-variant atlas `WNG_PrecursorHull_Atlas.png`
- Precursor single-panel Architect icon `Textures/UI/WNG/Build/WNG_PrecursorWall.png`
- Wraith wall original texture, linked wall atlas and Architect icon
- Doors, wall snap points, linked render flags, code, saves and assemblies

The WNG art gallery exposes the repaired original under the **Buildings &
Structures** collection. The UI & Icons entry remains a separate file.

This note also records the image repair so the next release build can be
verified against the fixed source PNG.
