# WNG Apparel Studio

This is the active WNG clothing/armour art generator.

It replaces the old shape/compositor pipeline.

## Hard rules

- Stargate production costume/prop research is Stage 1.
- Vanilla RimWorld apparel geometry is Stage 2 and remains the silhouette authority.
- Historical WNG apparel is inaccessible and forbidden.
- WNG ships/buildings may be used only as a quality benchmark outside this renderer.
- Component contours are **paint masks only**. They are not flat fills and are not visible geometry by themselves.
- Every material is painted at 1152×1152 with signed-distance volume, authored relief, normal-based light, AO/contact depth, material brushwork, wear, seams and closures, then reduced to 192×192.
- Hard-rubber and simulated-bone surfaces deliberately avoid bright perimeter outlines/rings.
- Visual contact-sheet inspection remains mandatory even after numeric QA passes.

The active engine is `Tools/ArtStudio/wng_apparel_studio.py`.
