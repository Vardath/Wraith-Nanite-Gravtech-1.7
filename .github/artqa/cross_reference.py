from pathlib import Path
import re
import xml.etree.ElementTree as ET
from collections import Counter, defaultdict

ROOT = Path(".")
SRC = ROOT / "Source" / "WraithNaniteGravtech"
failures = []
notes = []

# ---------- Build authoritative WNG Def inventory ----------
def_names = defaultdict(set)
def_locations = {}
defs_by_xml_type = defaultdict(set)
xml_files = []

for base in (ROOT / "Defs", ROOT / "Compatibility"):
    if not base.exists():
        continue
    for path in base.rglob("*.xml"):
        raw = path.read_text(encoding="utf-8", errors="ignore")
        if "<Patch" in raw or "PatchOperation" in raw:
            continue
        try:
            root = ET.fromstring(raw)
        except ET.ParseError as exc:
            failures.append(f"{path}: XML parse failure: {exc}")
            continue

        xml_files.append((path, root))
        for node in list(root):
            name = (node.findtext("defName") or "").strip()
            if not name:
                continue
            key = (node.tag, name)
            if key in def_locations:
                failures.append(
                    f"duplicate {node.tag} defName {name}: {def_locations[key]} and {path}"
                )
            def_locations[key] = path
            def_names[name].add(node.tag)
            defs_by_xml_type[node.tag].add(name)

# ---------- Build production C# class inventory ----------
cs_files = [p for p in SRC.rglob("*.cs") if "Diagnostics" not in p.parts]
declared_classes = set()
class_files = {}

for path in cs_files:
    text = path.read_text(encoding="utf-8", errors="ignore")
    # WNG uses ordinary namespace blocks; collect all class/struct/interface/enum names
    # under the nearest namespace declaration in the file.
    namespaces = re.findall(r"\bnamespace\s+([A-Za-z_][A-Za-z0-9_.]*)", text)
    namespace = namespaces[0] if namespaces else ""
    for kind, name in re.findall(
        r"\b(class|struct|interface|enum)\s+([A-Za-z_][A-Za-z0-9_]*)", text
    ):
        fq = f"{namespace}.{name}" if namespace else name
        declared_classes.add(fq)
        class_files[fq] = path

# ---------- XML custom-class references ----------
class_ref_fields = {
    "thingClass", "workerClass", "driverClass", "compClass", "hediffClass",
    "geneClass", "incidentClass", "worker", "jobClass", "verbClass",
    "placeWorkerClass", "designatorClass", "modExtensionClass",
}
class_refs = []

for path, root in xml_files:
    for elem in root.iter():
        # Class="WraithNaniteGravtech.X" style
        cls = (elem.get("Class") or "").strip()
        if cls.startswith("WraithNaniteGravtech."):
            class_refs.append((path, f"@Class on <{elem.tag}>", cls))

        # <workerClass>WraithNaniteGravtech.X</workerClass> style
        text = (elem.text or "").strip()
        if elem.tag in class_ref_fields and text.startswith("WraithNaniteGravtech."):
            class_refs.append((path, elem.tag, text))

        # Custom Def root tags can themselves be class names.
        if elem.tag.startswith("WraithNaniteGravtech."):
            class_refs.append((path, "custom XML root/type", elem.tag))

for path, field, cls in class_refs:
    if cls not in declared_classes:
        failures.append(f"{path}: unresolved WNG class reference {cls} ({field})")

# ---------- XML WNG Def references ----------
# Restrict to fields that semantically hold a Def name. This avoids treating labels,
# pattern IDs, sound text, tags, save keys, etc. as Def references.
ref_tags = {
    "thingDef", "thingDefName", "pawnKind", "pawnKindDef", "factionDef",
    "researchPrerequisite", "researchProject", "hediff", "hediffDef",
    "gene", "geneDef", "xenotype", "xenotypeDef", "ability", "abilityDef",
    "jobDef", "workType", "workTypeDef", "workGiver", "workGiverDef",
    "terrain", "terrainDef", "sound", "soundDef", "fleck", "fleckDef",
    "thought", "thoughtDef", "incident", "incidentDef", "sitePart", "sitePartDef",
    "traderKind", "traderKindDef", "recipe", "recipeDef", "defaultProjectile",
    "projectileWhenLoaded", "leavingDef", "incomingDef", "skyfaller",
    "minifiedDef", "entityDef", "race", "bodyType", "weaponDef",
    "apparelDef", "damageDef", "statDef", "chemical", "drugCategory",
    "designationCategory", "category", "componentTypeDef", "fuelFilter",
}

# Generic list parents whose <li> entries are Def names.
list_ref_parents = {
    "thingDefs", "recipeUsers", "researchPrerequisites", "thingCategories",
    "affectedDamageDefs", "requiredResearch", "researchProjects", "hediffs",
    "genes", "abilities", "siteParts", "pawnKinds", "factionDefs",
    "terrains", "sounds",
}

expected_type_by_tag = {
    "thingDef": "ThingDef", "thingDefName": "ThingDef",
    "pawnKind": "PawnKindDef", "pawnKindDef": "PawnKindDef",
    "factionDef": "FactionDef",
    "researchPrerequisite": "ResearchProjectDef", "researchProject": "ResearchProjectDef",
    "hediff": "HediffDef", "hediffDef": "HediffDef",
    "gene": "GeneDef", "geneDef": "GeneDef",
    "xenotype": "XenotypeDef", "xenotypeDef": "XenotypeDef",
    "ability": "AbilityDef", "abilityDef": "AbilityDef",
    "jobDef": "JobDef", "workType": "WorkTypeDef", "workTypeDef": "WorkTypeDef",
    "workGiver": "WorkGiverDef", "workGiverDef": "WorkGiverDef",
    "terrain": "TerrainDef", "terrainDef": "TerrainDef",
    "sound": "SoundDef", "soundDef": "SoundDef",
    "fleck": "FleckDef", "fleckDef": "FleckDef",
    "thought": "ThoughtDef", "thoughtDef": "ThoughtDef",
    "incident": "IncidentDef", "incidentDef": "IncidentDef",
    "sitePart": "SitePartDef", "sitePartDef": "SitePartDef",
    "traderKind": "TraderKindDef", "traderKindDef": "TraderKindDef",
    "recipe": "RecipeDef", "recipeDef": "RecipeDef",
    "defaultProjectile": "ThingDef", "projectileWhenLoaded": "ThingDef",
    "leavingDef": "ThingDef", "incomingDef": "ThingDef", "skyfaller": "ThingDef",
    "minifiedDef": "ThingDef", "entityDef": "ThingDef", "race": "ThingDef",
    "weaponDef": "ThingDef", "apparelDef": "ThingDef",
    "damageDef": "DamageDef", "statDef": "StatDef", "chemical": "ChemicalDef",
    "designationCategory": "DesignationCategoryDef",
}

expected_type_by_list_parent = {
    "thingDefs": "ThingDef",
    "recipeUsers": "ThingDef",
    "researchPrerequisites": "ResearchProjectDef",
    "requiredResearch": "ResearchProjectDef",
    "researchProjects": "ResearchProjectDef",
    "thingCategories": "ThingCategoryDef",
    "affectedDamageDefs": "DamageDef",
    "hediffs": "HediffDef",
    "genes": "GeneDef",
    "abilities": "AbilityDef",
    "siteParts": "SitePartDef",
    "pawnKinds": "PawnKindDef",
    "factionDefs": "FactionDef",
    "terrains": "TerrainDef",
    "sounds": "SoundDef",
}

xml_wng_refs = []
for path, root in xml_files:
    parent_map = {child: parent for parent in root.iter() for child in parent}
    for elem in root.iter():
        value = (elem.text or "").strip()
        if not value.startswith("WNG_"):
            continue

        parent = parent_map.get(elem)
        parent_tag = parent.tag if parent is not None else ""

        is_ref = elem.tag in ref_tags or (elem.tag == "li" and parent_tag in list_ref_parents)
        if not is_ref:
            continue

        xml_wng_refs.append((path, elem.tag, parent_tag, value))
        expected_type = expected_type_by_tag.get(elem.tag)
        if elem.tag == "li":
            expected_type = expected_type_by_list_parent.get(parent_tag)

        if value not in def_names:
            failures.append(
                f"{path}: unresolved WNG Def reference {value} in <{elem.tag}>"
            )
        elif expected_type and value not in defs_by_xml_type.get(expected_type, set()):
            failures.append(
                f"{path}: {value} referenced as {expected_type} in <{elem.tag}> "
                f"but defined as {sorted(def_names[value])}"
            )

# ---------- Typed DefDatabase lookups in production C# ----------
lookup_re = re.compile(
    r"DefDatabase\s*<\s*([A-Za-z_][A-Za-z0-9_.]*)\s*>"
    r"\s*\.\s*GetNamed(?:SilentFail)?\s*\(\s*\"(WNG_[^\"]+)\""
)

typed_lookup_count = 0
for path in cs_files:
    text = path.read_text(encoding="utf-8", errors="ignore")
    for def_type, name in lookup_re.findall(text):
        typed_lookup_count += 1
        if name not in def_names:
            failures.append(
                f"{path}: DefDatabase<{def_type}> lookup references missing def {name}"
            )
            continue

        simple_type = def_type.split(".")[-1]
        strict = {
            "ThingDef","RecipeDef","HediffDef","GeneDef","XenotypeDef",
            "PawnKindDef","FactionDef","ResearchProjectDef","AbilityDef",
            "JobDef","WorkGiverDef","WorkTypeDef","TerrainDef","FleckDef",
            "SoundDef","ThoughtDef","IncidentDef","SitePartDef","TraderKindDef",
            "DamageDef","StatDef","ChemicalDef","ThingCategoryDef",
            "DesignationCategoryDef",
        }
        if simple_type in strict and name not in defs_by_xml_type.get(simple_type, set()):
            failures.append(
                f"{path}: {name} looked up as {simple_type} but is defined as "
                f"{sorted(def_names[name])}"
            )

# ---------- [DefOf] field names ----------
def_of_fields = []
for path in cs_files:
    text = path.read_text(encoding="utf-8", errors="ignore")
    for m in re.finditer(
        r"\[\s*DefOf\s*\][\s\S]{0,3000}?class\s+[A-Za-z_][A-Za-z0-9_]*\s*\{([\s\S]*?)\n\}",
        text
    ):
        body = m.group(1)
        for type_name, field in re.findall(
            r"public\s+static\s+([A-Za-z_][A-Za-z0-9_.]*)\s+(WNG_[A-Za-z0-9_]+)\s*;",
            body
        ):
            def_of_fields.append((path, type_name, field))
            if field not in def_names:
                failures.append(
                    f"{path}: [DefOf] field {field} ({type_name}) has no matching XML def"
                )
            else:
                simple_type = type_name.split(".")[-1]
                if simple_type in defs_by_xml_type and field not in defs_by_xml_type[simple_type]:
                    failures.append(
                        f"{path}: [DefOf] field {field} declared as {simple_type} "
                        f"but defined as {sorted(def_names[field])}"
                    )

# ---------- Literal WNG def-name strings in common *DefName fields ----------
# These are runtime references stored as strings rather than DefDatabase lookups.
string_name_re = re.compile(
    r"(?:const\s+string|static\s+readonly\s+string|string)\s+"
    r"([A-Za-z_][A-Za-z0-9_]*(?:DefName|KindDefName|RecipeDefName|JobDefName|HediffDefName|FactionDefName))"
    r"\s*=\s*\"(WNG_[^\"]+)\""
)
string_ref_count = 0
for path in cs_files:
    text = path.read_text(encoding="utf-8", errors="ignore")
    for field, name in string_name_re.findall(text):
        string_ref_count += 1
        if name not in def_names:
            failures.append(
                f"{path}: string Def reference {field}={name} has no matching XML def"
            )

print("=== D139 CROSS-REFERENCE AUDIT ===")
print(f" - Parsed XML files: {len(xml_files)}")
print(f" - Unique def names inventoried: {len(def_names)}")
print(f" - Typed defs inventoried: {len(def_locations)}")
print(f" - Production C# files scanned: {len(cs_files)}")
print(f" - WNG classes declared: {len(declared_classes)}")
print(f" - XML WNG class references checked: {len(class_refs)}")
print(f" - XML WNG Def references checked: {len(xml_wng_refs)}")
print(f" - Typed DefDatabase WNG lookups checked: {typed_lookup_count}")
print(f" - [DefOf] WNG fields checked: {len(def_of_fields)}")
print(f" - String *DefName WNG references checked: {string_ref_count}")

if failures:
    print("\nFAILURES:")
    for failure in failures:
        print(" -", failure)
    raise SystemExit(1)

print("PASS: WNG XML/class/runtime Def references resolve to current source and Def inventory.")
