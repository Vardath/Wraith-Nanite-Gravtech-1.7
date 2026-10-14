from pathlib import Path
import re
import xml.etree.ElementTree as ET
from collections import Counter, defaultdict

ROOT=Path(".")
TEX=ROOT/"Textures"
failures=[]
notes=[]

xml_paths=[]
for base in (ROOT/"Defs", ROOT/"Compatibility", ROOT/"Patches"):
    if base.exists():
        xml_paths.extend(sorted(base.rglob("*.xml")))

docs=[]
for path in xml_paths:
    try:
        docs.append((path,ET.parse(path).getroot()))
    except Exception as exc:
        failures.append(f"{path}: XML parse failure: {exc}")

def is_wng_ref(ref):
    ref=(ref or "").strip()
    return bool(ref) and (
        ref.startswith("UI/WNG/")
        or "WNG_" in ref
        or ref.startswith("WraithNaniteGravtech/")
    )

def exact(ref):
    return TEX/(ref+".png")

def family(ref):
    base=TEX/ref
    if not base.parent.exists():
        return []
    return sorted(base.parent.glob(base.name+"*.png"))

def directional(ref):
    base=TEX/ref
    return {d:(Path(str(base)+f"_{d}.png")).exists() for d in ("north","east","south","west")}

graphic_counts=Counter()
local_graphics=0
multi_graphics=0
single_graphics=0
linked_graphics=0
ui_refs=set()
worn_refs=set()
mask_refs=set()
damage_refs=set()
minified=0

# Validate XML art references.
for path,root in docs:
    for owner in root.iter():
        owner_name=(owner.findtext("defName") or owner.get("Name") or owner.tag).strip()

        if owner.tag=="ThingDef" and owner_name.startswith("WNG_") and (owner.findtext("minifiedDef") or "").strip():
            minified+=1

        for gd in owner.findall(".//graphicData"):
            ref=(gd.findtext("texPath") or "").strip()
            if not ref or not is_wng_ref(ref):
                continue

            local_graphics+=1
            gclass=(gd.findtext("graphicClass") or "Graphic_Single").strip()
            graphic_counts[gclass]+=1

            if gclass=="Graphic_Multi":
                multi_graphics+=1
                dirs=directional(ref)
                # Standard RimWorld Graphic_Multi should have N/E/S. West may be independently
                # authored or derived/mirrored by engine/art pipeline, so N/E/S are the hard floor.
                for d in ("north","east","south"):
                    if not dirs[d]:
                        failures.append(f"{path}: {owner_name} Graphic_Multi missing {d} texture for {ref}")
                if not dirs["west"]:
                    notes.append(f"{path}: {owner_name} Graphic_Multi has no explicit west texture for {ref} (east-mirror path allowed)")
            elif gclass=="Graphic_Single":
                single_graphics+=1
                if not exact(ref).exists():
                    failures.append(f"{path}: {owner_name} Graphic_Single missing exact texture {ref}.png")
            elif "Graphic_Linked" in gclass:
                linked_graphics+=1
                if not exact(ref).exists():
                    failures.append(f"{path}: {owner_name} linked graphic missing atlas/base texture {ref}.png")
            elif gclass=="Graphic_Random":
                if not family(ref):
                    failures.append(f"{path}: {owner_name} Graphic_Random has no texture family for {ref}")
            else:
                # Custom classes still need at least an exact base or a family rooted at texPath.
                if not exact(ref).exists() and not family(ref):
                    failures.append(f"{path}: {owner_name} {gclass} has no texture/base family for {ref}")

            # Explicit masks in GraphicData.
            for tag in ("maskPath","texPathMask"):
                m=(gd.findtext(tag) or "").strip()
                if m and is_wng_ref(m):
                    mask_refs.add(m)
                    if not exact(m).exists():
                        failures.append(f"{path}: {owner_name} missing explicit mask {m}.png")

            # Any custom damage graphic path nested under GraphicData must resolve.
            for e in gd.iter():
                if e.tag in ("damageTexPath","damagedTexPath","destroyedTexPath") and e.text:
                    dref=e.text.strip()
                    if is_wng_ref(dref):
                        damage_refs.add(dref)
                        if not exact(dref).exists() and not family(dref):
                            failures.append(f"{path}: {owner_name} missing damage graphic {dref}")

        # Other texture-bearing XML fields.
        for tag in ("uiIconPath","iconPath","factionIconPath","backgroundPath","uiIcon"):
            for e in owner.findall(".//"+tag):
                ref=(e.text or "").strip()
                if not is_wng_ref(ref):
                    continue
                ui_refs.add(ref)
                if not exact(ref).exists():
                    failures.append(f"{path}: {owner_name} missing {tag} {ref}.png")

        for e in owner.findall(".//wornGraphicPath"):
            ref=(e.text or "").strip()
            if not is_wng_ref(ref):
                continue
            worn_refs.add(ref)
            fam=family(ref)
            if not fam:
                failures.append(f"{path}: {owner_name} wornGraphicPath has no files for {ref}")
                continue
            names=[p.stem.lower() for p in fam]
            # Worn apparel uses N/E/S overlays; body-type/gender prefixes are allowed.
            for d in ("north","east","south"):
                if not any(n.endswith("_"+d) for n in names):
                    failures.append(f"{path}: {owner_name} wornGraphicPath {ref} lacks any {d} worn overlay")

        # Terrain/local texturePath fields are also part of visible WNG graphic families.
        for e in owner.findall(".//texturePath"):
            ref=(e.text or "").strip()
            if is_wng_ref(ref) and not exact(ref).exists() and not family(ref):
                failures.append(f"{path}: {owner_name} missing texturePath {ref}")

# Literal C# Texture2D ContentFinder paths must exist.
content_refs=set()
pat=re.compile(r'ContentFinder\s*<\s*Texture2D\s*>\s*\.\s*Get\s*\(\s*"([^"]+)"')
for p in (ROOT/"Source"/"WraithNaniteGravtech").rglob("*.cs"):
    text=p.read_text(encoding="utf-8",errors="ignore")
    for ref in pat.findall(text):
        if not is_wng_ref(ref):
            continue
        content_refs.add(ref)
        if not exact(ref).exists():
            failures.append(f"{p}: ContentFinder<Texture2D> missing literal texture {ref}.png")

# All WNG UI PNGs should be valid file names and non-empty. Full PNG decoding is already
# covered by the final-art and release-package integrity audits.
ui_pngs=sorted((TEX/"UI"/"WNG").rglob("*.png")) if (TEX/"UI"/"WNG").exists() else []
for p in ui_pngs:
    if p.stat().st_size<=0:
        failures.append(f"empty UI PNG: {p}")

# Standard MinifiedThing uses the source thing's own graphic; ensure every explicitly minifiable
# WNG ThingDef with local explicit art already passed a graphic check above. Record the population.
# This audit does not demand a separate minified sprite where RimWorld generates one natively.

print("=== D149 TEXTURE / GRAPHIC FAMILY AUDIT ===")
print(f" - XML files scanned: {len(xml_paths)}")
print(f" - WNG/local graphicData refs checked: {local_graphics}")
print(f" - Graphic_Multi refs: {multi_graphics}")
print(f" - Graphic_Single refs: {single_graphics}")
print(f" - Linked/custom linked refs: {linked_graphics}")
print(f" - UI/icon refs checked: {len(ui_refs)}")
print(f" - Worn graphic families checked: {len(worn_refs)}")
print(f" - Explicit mask refs checked: {len(mask_refs)}")
print(f" - Explicit damage graphic refs checked: {len(damage_refs)}")
print(f" - C# ContentFinder WNG texture refs checked: {len(content_refs)}")
print(f" - WNG UI PNGs inventoried: {len(ui_pngs)}")
print(f" - Explicitly minifiable WNG ThingDefs inventoried: {minified}")

if notes:
    print("\nNOTES:")
    for n in notes[:100]:
        print(" -",n)
    if len(notes)>100:
        print(f" - ... {len(notes)-100} more notes")

if failures:
    print("\nFAILURES:")
    for f in failures:
        print(" -",f)
    raise SystemExit(1)

print("PASS: WNG texture paths, Graphic_Multi direction families, icons, worn graphics, masks and literal UI texture lookups resolve structurally.")
