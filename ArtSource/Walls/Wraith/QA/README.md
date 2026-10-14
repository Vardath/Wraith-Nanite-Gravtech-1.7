# Wraith Living Wall — Hand-painted replacement (RimWorld 1.6)

The **runtime mod asset** is `Textures/Things/Building/Wraith/Structure/WNG_LivingWall_Atlas.png`.
This is not a gallery mockup. `WNG_WraithLivingWall` already refers to it.

- Native texture: 1024×1024 PNG RGBA, sixteen 256px cells with 32px gutters.
- Native UV footprint: 192×192 per state on **one 1×1 map tile**; bit masks N=1, E=2, S=4, W=8.
- States: isolated, four endcaps, two straights, four corners, four T-junctions and a cross.
- Editable hand-drawn vector artwork: `ArtSource/Walls/Wraith/VectorMasters/WraithLivingWall_link00.svg` through `link15.svg`; rendered directly at 768px and downsampled losslessly to 192px.
- Rebuild using: `python Tools/WallArt/generate_wraith_settlement_wall.py --preview ArtSource/Walls/Wraith/QA/HandPainted`.
- The 48px-per-tile approximate vanilla-scale roof-and-junction layout is available at `HandPainted/Wraith-Living-Wall-Room-Test.png` (offline renderer simulation, NOT an in-game screenshot).
- Legacy wall standalone texture and Architect icon also updated from the isolated variant.
- Wraith gravship hull uses a separate atlas and remains unchanged.

Visual QA: broad wall-cap coverage is approximately 87.8% isolated and 100% fully linked; edge joins are painted and normalised across linked states; asymmetric claws cannot accidentally
produce opaque connectors in directions with no neighbour. Room topology, roof/door functionality,
wall linking and actual in-game resolution should also be inspected in RimWorld before final art signoff.

## Current material revision (supersedes the old vector and WebP sources)

Only `Tools/WallArt/generate_wraith_settlement_wall.py` generates the
installed living wall. Its material is continuously repeated across native
wall cells, with no central icon, framed scutes or special T-junction emblem.
The old SVG files in `VectorMasters/` and the old WebP are obsolete and
remain solely as historical record. The topological preview illustrates
connectivity, not a real-time in-game comparison with vanilla wall art.
