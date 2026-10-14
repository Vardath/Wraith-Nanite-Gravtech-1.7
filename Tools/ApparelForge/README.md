# WNG ApparelForge

**ApparelForge does not create art.**

The previous ArtGen / ArtStudio approach is retired because procedural painting was
producing coloured shapes rather than professional costume illustration.

The production pipeline is now:

1. Stargate production costume/prop research.
2. Create three **finished master paintings** with a dedicated image/painting system:
   front/south, back/north, side/east. Minimum 1024 px, transparent background.
3. Use vanilla RimWorld apparel geometry as the silhouette authority.
4. ApparelForge projects the finished paintings onto Male/Female/Thin/Fat/Hulk
   variants and facings, clips to vanilla alpha, builds inventory art, and performs QA.
5. Inspect the contact sheet.
6. Only then commit live PNGs.

No historical WNG apparel is accepted. No polygon compositor, material shader,
procedural armour generator or old-clothing texture transfer exists in this pipeline.

The intended illustration tool is an external reference-capable image/painting model
(OpenArt is currently the preferred integration), with manual iteration until the
master paintings are genuinely production quality.
