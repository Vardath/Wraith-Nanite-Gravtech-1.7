# RimWorld 1.6 wall catalogue and WNG art parity audit

## Reference scope

This is a **whole wall-family audit**, not an audit of one wood/stone sprite.
RimWorld's native graphics are *16-link atlases of material appearances*.
The vanilla source art is not copied into WNG. Original files are in the
Ludeon-published art-source pack; available paths and game design behaviour
were checked against the vanilla asset index, RimWorld modding reference and
the existing WNG native-hook tests.

References:
- Ludeon art source via https://rimworldwiki.com/wiki/Modding_Tutorials/Textures
- https://gist.github.com/lbmaian/317dc5a1a300842d3151fe3d0f8ba789
- https://rimworldwiki.com/wiki/Wall
- https://github.com/purpleorangegames/JustWalls (comparison of custom wall appearance sets, not vanilla source)

### Native catalogue considered

| Family | Native art path/name | What changes |
| --- | --- | --- |
| Constructed plank | `Things/Building/Linked/Wall/Wall_Atlas_Planks` | Wood-like longitudinal planks, inherited construction/material colour |
| Constructed masonry | `.../Wall/Wall_Atlas_Bricks` | Stone-block course seams and material tint |
| Constructed smooth | `.../Wall/Wall_Atlas_Smooth` | Metal/precious smooth appearance, broad plane with edge chamfer |
| Natural rough rock | `Things/Building/Linked/Rock_Atlas` | Irregular rock mass, links to rock and walls |
| Flecked rough rock | `.../RockFlecked_Atlas` | Rough face/detail changes rather than another independent wall definition |
| Smoothed natural rock | `.../RockSmooth_Atlas` | Sculpted transition into smooth solid rock |
| Sandbags (comparison only) | `.../Sandbags_Atlas` | Links like a barrier, *not* a 1x1 roof-holding wall |
| Odyssey hull corners | native `SectionLayer_GravshipHull` | Separate angled 2x2 overlays, not handled by the standard 16-state straight wall atlas |

Material tint/appearance is a separate axis from connection topology. A single
appearance includes isolated, terminating, straight, elbow, tee, cross and bulk
connected graphics. Distinct material appearances can share one native wall
definition without requiring the same painted surface for every material.

**Limits:** The official art-source texture pack itself has not been imported
into this mod, and this document is not a byte-for-byte pixel survey of every
DLC wall. It describes identifiable 1.6-native wall graphics, their contract,
and the WNG implementation inspected in the public repository.

## Native wall contract preserved

- `ParentName=Wall`: fill 1, impassable, roof support, room sealing, floor covering,
  wind/light blocking, edge shadows, wall attachment supports and wall replacement.
- 1x1 graphic, no draw offset, native `CornerFiller`, Wall and Rock link flags.
- 16 atlas tiles; N=1 E=2 S=4 W=8 with original native MaterialAtlasPool UV rows.
- Connected edge pixels agree, including transitions from ends into corners,
  tees, four-way intersections and solid thicker masses.
- Paint, wall damage graphics, health and weather/atmosphere behaviour remain
  governed by game definitions, not an extra per-frame Harmony drawing patch.
- Gravship angled exterior sections remain handled by the Odyssey-compatible
  WNG family layer; a straight-wall recolour cannot simulate that geometry.

## Replacement order and verified source links

1. **WNG_WraithLivingWall** (completed art pass): independent
   `Textures/Things/Building/Wraith/Structure/WNG_LivingWall_Atlas.png`.
   Topology is authored as chitin ribs, recessed plum membrane and limited
   green growth seams. It no longer renders a picture-frame square border on
   every tile. Explicit ends, axial runs, L/T junctions, crossroads, and thicker
   fully linked regions receive different morphology.
2. **WNG_OrganicGravshipHull** (pending): Wraith living gravship hull and
   Odyssey-compatible angled corner overlay source. Its currently approved
   image assets must remain unchanged until this phase.
3. **WNG_PrecursorWall** (pending): Asuran settlement nanite composite.
4. **WNG_PrecursorGravshipHull** (pending): Asuran spacecraft structure and corners.
5. **WNG_GoauldGravshipHull** (pending): Goa'uld Ha'tak naquadah armor and corners.

Wall lumens are wall-mounted *attachments*, and the stripped Replicator wall
husk is ruin scenery; neither is a roof-holding structural wall.

## QA evidence and limits

- `python Tools/WallArt/generate_wraith_settlement_wall.py --preview ArtSource/Walls/Wraith/QA/settlement-topology.png`
  produces a real assembled colony-scale wall example, not sixteen
  disconnected sprites. The topology preview is a simulated wall renderer,
  **not a live RimWorld screenshot**.
- `python Tools/WallArt/validate_wall_art.py` tests every 16-state atlas UV,
  full cell coverage, native `CornerFiller`, all compatible join pairs,
  padding, the actual XML definitions and the separate angled hull layers.
- `python Tools/WallArt/RuntimeTests/run_tests.py` covers native hull overlays.
- In-game acceptance still requires a placement test: a long corridor, a
  closed room with doors, corners, tees, crosspieces, a thick 3x3 mass, roof
  support, wall attachments, painted pieces and damage stages.
