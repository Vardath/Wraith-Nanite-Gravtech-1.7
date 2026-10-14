# WNG Apparel ArtGen — Costume-First Architecture

The permanent apparel generator is **not** a generic armour-panel compositor.

## Enforced order

1. `StargateReferencePass`
2. `RimWorldImplementationPass`
3. `SynthesisPass`
   - costume construction
   - material painting
   - seams / closures / wear
4. Visual and RimWorld QA
5. Live mod PNGs

## Costume Blueprint v2 + Raster Painter v3

New clothing and uniform work should use:

```json
"renderer": "costume_blueprint_v2"
```

A profile supplies named Stargate materials and a semantic garment blueprint for south,
north and east. Pieces must be real clothing/construction concepts such as **lapel,
collar, yoke, vest, epaulette, cuff, waistband, sash, placket, corset, coat tail,
cuirass or guard**. Anonymous `panel`, `plate`, `polygon` and generic geometric
piece roles are rejected by profile validation.

The vanilla RimWorld alpha remains the silhouette authority. The blueprint only
describes construction *inside* that silhouette.

## Material model

Materials are profile-defined using shadow/mid/high response plus a material kind.
The generator supports cloth/woven uniform fabric, leather, reptile leather,
silk/satin and high-technology fabrics without reducing faction identity to colour.

Broad form, authored fold guides, seams, stitching, closures and wear create the
surface. Random noise is deliberately subordinate and cannot define the garment
structure.

## Historical WNG apparel — forbidden

Historical WNG clothing or armour may **not** be used as a synthesis input at all:
no pixel transfer, grayscale relief, texture transfer, palette transfer, silhouette
transfer, or repainting. Profile validation rejects non-empty historical-apparel
inputs and the legacy history loader throws if called.

Continuity comes from Stargate production references and lore. WNG ships/buildings
may remain **quality benchmarks only**; they do not donate clothing geometry or texture.

## Garment QA

Costume Blueprint v2 rejects:
- one oversized overlay that hides the base garment;
- excessive semantic pieces that become panel fragmentation;
- missing garment-construction seams;
- anonymous panel/plate geometry;
- undefined materials;
- invalid RimWorld silhouette output.

Automated QA is still only a gate. The 192 px contact sheet must be visually
inspected before an item is accepted.

### Professional raster rule

All costume_blueprint_v2 profiles now require `painting_mode: raster_brush_v3`. Semantic masks only locate materials. Visible art must come from sculpted raster light/form, hand-painted material breakup, contact depth, authored relief, seams and wear. A flat mask + colour fill is invalid.
