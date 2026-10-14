# Goa'uld transporter ring animation — WNG 1.6 (2026-10-10)

## Visual design
The transport-ring structure is now a flush, round 3×3 floor plate when inactive. Its new texture is `Textures/Things/Building/Goauld/WNG_GoauldTransportRings.png`, a bespoke transparent 512×512 bronze/black naquadah platform with amber radial lighting.

The ring effect is `Textures/Things/Building/Goauld/WNG_GoauldRingEffect.png`, a separate transparent 512×512 segmented horizontal annulus with warm gold highlights and a restrained cyan inner emitter. Both assets are generated reproducibly by `.github/artgen/generate_goauld_ring_transport.py`, committed by `.github/workflows/wng-generate-goauld-rings-art.yml`. No chat image generation.

A successful physical transfer triggers five ring overlays at **both** exact sender and receiver. The overlays move sequentially up the screen/north from the floor to suggest rising and hovering rings in RimWorld's overhead projection, then retract. Timing: rise 65 ticks, hold 32 ticks, return 70 ticks, approximately 2.8 seconds total at normal speed. The effect includes a native lightning glow.

## Transaction safety
`CompGoauldTransportRings` still validates all source/destination conditions, plans landing cells, transfers the exact manifest transactionally and rolls back failure before committing. The animation starts **only after** the original successful manifest commit, so a missing texture, interrupted frame or visual exception cannot duplicate or lose pawns/items. Animation is transient; not persisted in saves, never invoked on a failed transfer, and imposes no new cooldown or cargo restrictions.

The ring building uses `MapMeshAndRealTime` so its `PostDraw` ring overlays update every frame while active. The permanent circular floor sprite remains a normal 3×3 pass-through structure. Texture lookup is fail-soft: if its animated sprite is missing, the transfer still works.

The presentation recreates the characteristic Stargate sequence as a 2D top-down game effect; it is not a literal 3D mesh or an animation physically surrounding and occluding full-size pawns. More ambitious pre-transport sequencing can be added later but should separately verify time-delayed transfers and save/load boundaries.

## Validation
GitHub's managed RimWorld 1.6 compile, static contracts, all 40 master release checks, packaged assets and release parity must pass. Live RimWorld testing is still required for camera layering, visual scale, and timing with normal game speed.
