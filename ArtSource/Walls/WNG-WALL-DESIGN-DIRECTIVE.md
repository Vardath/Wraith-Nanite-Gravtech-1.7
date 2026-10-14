# WNG WALL ART — ACCEPTANCE DIRECTIVE (all five functional walls)

## Why this file exists
This directive records the user's repeated corrections and the failures visible in WNG
gallery screenshots 367, 368, and 369 (12 October 2026), plus earlier discussions
from September and October 2026. The user requires *professionally hand-authored,
Stargate-themed wall artwork* that actually behaves and looks like vanilla
RimWorld 1.6 walls. Wall art must be committed into WNG's real mod
Textures folder, **not** delivered as conceptual images in chat. Replace one
wall at a time. Do not repeat the previous failures for the other four walls.

### Non-negotiable distinction: source pixels vs visible dimensions
A 1024x1024 RGBA linked atlas with sixteen 256x256 cells is merely one valid
*texture encoding*. It is NOT proof the wall is the size of a vanilla wall.
Current Wraith's mask-0 printable image occupies only about 105 of the 192 px
sampled width (x approximately 51..155). The screenshot shows a miniature
standing ornament/rail, not a full-scale constructed wall. This fails.

The target is the visible proportions/coverage, top-cap/face thickness and
spacing of actual RimWorld 1.6 built walls. Each structural piece must use
the native 1x1 game-world footprint and exhibit a substantial broad top
surface and shaded/chamfered wall-facing edges, with appropriately
contiguous mass at neighbour joins. A full texture square is not a
substitute for proper geometry, and an ornamental thin bar is not a wall.

## Required physical reference (before drawing any replacement)
Inspect the actual **current** Core wall library and Odyssey gravship hull
art/renderer (where applicable), including at minimum:
- Wall_Atlas_Bricks, Wall_Atlas_Planks, Wall_Atlas_Smooth, metal/smooth
  appearances, natural rock and smoothed-rock connections.
- Native Wall ThingDef inheritance, Wall vs Rock linkFlags, CornerFiller
  atlas sampling, constant-UV diagonal fillers, blueprint/placement styles.
- Straight runs, solitary pieces, all four termini, four L corners, all
  T-junctions and four-way crosses; mixtures with stone walls, rock and
  real doors.
- Roof-bearing behaviour, wall lights/fixtures at face-aligned
  offsets, shutters/doors, gravship walls linking to same-family hull,
  supporting doors and the appropriate thruster interfaces.
- Odyssey exterior hull angled / chamfered 2x2 overlays as **separate**
  graphics from the standard straight 1x1 wall atlas.

Don't claim a true visual comparison to all current vanilla assets unless the
actual art has been obtained and examined. Source baseline:
https://rimworldwiki.com/wiki/Modding_Tutorials/Textures (includes official
Ludeon art-source link). Use the 1.6 installed Core/Odyssey files if available.

## Visual target
The whole wall reads as **a continuous building**, not 16 unconnected
collectible icons, tiled medallions, pipes, fence rails, or miniatures.
Top cap occupies the same apparent map-cell footprint as nearby vanilla wall,
with an angled/chamfered rise to a short shaded wall-face. Connected edges flow
without gaps, crossings have coherent body mass and corners do not turn into
oversized ornaments. Scale and lighting are consistent across all 16 states.
High-quality intentional hand-painted visual language, crisp at native
colony zoom. Restrained faction motifs decorate structural mass; decorative
ribs, glow and plates never replace the structural slab.

### Family art (distinct, not a universal reskin)
1. WNG_WraithLivingWall — grown bone/chitin armour, purple living membrane,
   organic ribs/tendons, restrained green bioluminescence. Normal settlement
   wall, not spacecraft hull.
2. WNG_OrganicGravshipHull — massive Wraith living-hull tissue with
   strengthened organic contours; proper Odyssey corners.
3. WNG_PrecursorWall — finely segmented pale Ancient/Asuran composite,
   engineered geometry with restrained cyan seams and subtle bronze tracery.
4. WNG_PrecursorGravshipHull — engineered Asuran nanite/Ancient hull with
   Odyssey external chamfers, not a giant bordered square.
5. WNG_GoauldGravshipHull — naquadah reinforcement, dark armour and bronze
   / amber restrained Goa'uld geometric accents; Odyssey corners.

Wall lumens are *attachments* with their own directional artwork, not
structural walls. Replicator stripped-wall husk is scenery.

## Prohibited failure modes (observed in previous attempts)
- Generate a concept sheet, downscale it to 288x288 lossy WebP, crop tiny
  objects, upscale back to 192px printed tiles: this destroys quality.
- Design an entire wall system by cropping/rotating a few unrelated
  freestanding sprite shapes. It creates pipes/fences and mismatched scale.
- Decorate the outside of the same dark square 16 different ways.
- Confuse wide-open transparency or full-image opacity with a verified
  vanilla-equivalent structural silhouette.
- Depend on one isolated preview or the art gallery as proof of multi-wall
  behaviour.
- Relax validation thresholds to pass an asset after a visual failure.
- Let an unrelated generator/CI art-fix overwrite approved output.
- Conflate passing GitHub checks with accurate live in-game appearance.
- Replace both settlement and gravship visual assets as one batch.

## Production sequence — enforce on EACH wall, starting again with #1
1. **Reference**. Acquire/inspect actual vanilla corresponding atlas(es),
   and compare 1x1 rasterized wall footprints/ratios at the same scale.
   Identify how vanilla handles top cap, cliff/bevel, front face, link masks,
   thick corners, missing neighbour endings, materials and shadow.
2. **Art master**. Paint high-resolution, editable original WNG-specific
   layer sources using those templates. Preserve originals losslessly;
   no tiny WebP/interpolated enlargements. Hand-paint the wall *as a
   physical built surface*, first a correct isolated wall and ordinary
   E/W + N/S two-cell run, then corners and more complex masks.
3. **Native atlas assembly**. Fit 16 masks into tested RimWorld UV atlas
   format; validate actual sampled region, border pixels, gutter padding,
   connected seams and diagonal CornerFiller. Preserve native Wall and
   substructure logic, and same-family compatibility as before.
4. **Side-by-side QA**. Render a vanilla wall and candidate WNG wall
   on the *same scale grid* including 1/2/3/8-tile runs, ends, L/T/cross,
   thick clusters, doors and wall fixtures. Compare relative wall mass,
   shading, contact footprint and camera zoom. Inspect full-sized crops,
   not only thumbnails or an icon gallery.
5. **Runtime compatibility**. Test actual in-game walls, roof support,
   room sealing/airtightness, door snapping, rot/placement, attachment
   placement, blueprints, replacement, painting, and gravship hull
   exterior chamfers/landing/launch where relevant. If not possible,
   report explicitly as **not live tested**.
6. **Commit just the approved single family** to real mod `Textures`,
   retain source, inspect result in Gallery **and game**, then proceed
   to next family only after visual and functional success.

## Automated checks must measure the RIGHT things
Use the matched vanilla reference as the acceptance target rather than
a hardcoded percentage invented for the new artwork. Check physical occupied
bbox, central structural-mass coverage, alpha coverage, apparent wall
thickness, neighbour connected UV edges, 16 ordered states, and
CornerFiller gap-freeness. Check source art size/resolution too. A wrong
or tiny sprite must fail **even when atlas dimensions and link masks pass**.
Report the measured mismatch; do not weaken the test to bless it.

## Status — Wraith settlement redesign, 12 October 2026

Older failed art (screenshot 369): a 288x288 lossy WebP was used to make tiny
floating ornaments, about 55% of map-cell width. That art is **rejected** and
not used by the current runtime atlas.

CURRENT WRAITH SETTLEMENT PASS (third artwork revision, not yet player-approved):
- Editable hand-authored SVG structural masters:
  `ArtSource/Walls/Wraith/VectorMasters/WraithLivingWall_link00.svg`
  through `WraithLivingWall_link15.svg`; source assembly is in
  `Tools/WallArt/generate_wraith_settlement_wall.py` and hand-composed
  chitin plating is in `Tools/WallArt/paint_wraith_chitin.py`.
- The latest revision uses four interlocking chitin armour plates, dark
  membrane folds and highlighted calcified ribs on an already full-sized slab.
  The bright green living junction nodes do not recur on plain straight walls.
- Each wall is drawn as a full-size organic chitin/membrane slab, with visible
  roof-facing cap, exposed bevels and downward shadowed sides, zero borders
  between connected tiles. No freestanding sprite-sheet fragments.
- The 16 SVG states are first rendered at 768px before resampling to the
  native 192px UV sample. Packed in a real 1024px linked atlas.
- Runtime atlas actually replaced:
  `Textures/Things/Building/Wraith/Structure/WNG_LivingWall_Atlas.png`;
  the vanilla wall XML inheritance, linkType and 1x1 draw size are intact.
- Isolated mask0 has ~87.9% visible coverage; mask15 100%. No half-tile
  width miniature. Fits native 1x1 footprint, links to ordinary walls/rock
  under existing Wall/Rock link flags.
- Preview layout:
  `ArtSource/Walls/Wraith/QA/HandPainted/Wraith-Living-Wall-Room-Test.png`
  is a renderer test, NOT an in-game screenshot.
- Strict validator requires >=85.5% isolated coverage and >=99.5%
  structural core, all-connected >=99.5%; no suppression of failures.
- Wall-art + native hull bridge and settlement-family GitHub validators PASS.
  Actual live RimWorld artistic review/room use still UNVERIFIED.
- 12 October third revision: art generator, D120 linked-wall/gravship validator
  and D121 settlement-family validator passed. Other hull atlases were
  checked by blob SHA and remain unchanged.

**DO NOT CALL THE WALL ARTISTICALLY APPROVED** without seeing the result
in-game at vanilla zoom beside a constructed vanilla wall, verifying cap
height/shape, doors, roof, attachments and scale. Do not proceed to wall #2
until that review. Preserve the above assets in later edits.

## Revision 4 — 12 October 2026 — vanilla-like material pass

The first three artistic revisions failed user review: ornament, miniature
sprite, then a framed centre motif on each tile. These were not acceptable
vanilla walls.

Step 1 has been redone in the **actual 1.6 mod**. The authoritative material
recipe is now `Tools/WallArt/generate_wraith_settlement_wall.py`; it writes
`Textures/Things/Building/Wraith/Structure/WNG_LivingWall_Atlas.png`,
`Textures/Things/Building/Wraith/Structure/WNG_LivingWall.png` and
`Textures/UI/WNG/Build/WNG_WraithLivingWall.png`.

The prior `paint_wraith_chitin.py` and `VectorMasters/` assets are
SUPERSEDED HISTORICAL WORK, not inputs to the current art. Do not revive
the central emblem or framed plaques. No concept image in chat is an asset.

Current target: native full-size vanilla wall body and linking with one
continuous understated Wraith chitin/membrane material on every tile.
Exposed faces have short dark bevels; connected sides have none.
All sixteen masks use the *same material in their central 128x128 region*.
The strict validator asserts this to reject recurring oversized emblems.
Isolated ~85.9% / four-way linked 100% coverage. Exact 192px native
sample within 256px cells of a 1024px RGBA atlas, 1x1 draw footprint.
No model transformations, runtime intercepts, door, roof, regenerative
comp, biomass, parent Wall, Wall/Rock linkFlags or gravship mechanics changed.

Known verification boundary: structural CI and offline assembled-wall preview
are not equivalent to an artist-approved, live RimWorld colony comparison.
Wraith-to-vanilla linking inherits the same Wall/Rock flags and CornerFiller
path, but has not been visually tested against the installed game.
Only wall #1 has been worked on this revision. Do not start #2 until
user accepts #1 in game.
