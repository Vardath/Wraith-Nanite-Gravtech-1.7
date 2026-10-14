from pathlib import Path
import collections
import xml.etree.ElementTree as ET

ROOT=Path(".")
failures=[]
notes=[]
counts=collections.Counter()
empty_replacements=[]
items=[]

# Lists/properties where replacement can silently erase inherited vanilla/mod content.
critical_lists={
    "recipeUsers","comps","verbs","tools","thingCategories","weaponTags",
    "costList","ingredients","researchPrerequisites","placeWorkers","stuffCategories",
    "parts","affordances","statBases","apparel","graphicData","modExtensions",
    "inspectorTabs","buildingTags","tradeTags","weaponClasses","bodyPartGroups",
}

for base in (ROOT/"Defs", ROOT/"Compatibility"):
    if not base.exists():
        continue
    for path in base.rglob("*.xml"):
        raw=path.read_text(encoding="utf-8",errors="ignore")
        if "<Patch" in raw or "PatchOperation" in raw:
            continue
        try:
            root=ET.fromstring(raw)
        except ET.ParseError:
            continue

        # Only inspect actual defs; inherited list replacement in patch XML is a different audit.
        for node in list(root):
            defname=(node.findtext("defName") or "").strip()
            parent=(node.get("ParentName") or "").strip()
            abstract=(node.get("Abstract") or "").lower()=="true"
            wng_owned = (
                not defname
                or defname.startswith("WNG_")
                or defname.endswith("_WNG_Kassa")
            )
            for elem in node.iter():
                if (elem.get("Inherit") or "").lower()!="false":
                    continue
                tag=elem.tag
                counts[tag]+=1
                child_count=len(list(elem))
                text=(elem.text or "").strip()
                items.append((path,defname,parent,abstract,tag,child_count,text))

                if tag in critical_lists and child_count==0 and not text:
                    empty_replacements.append((path,defname,parent,abstract,tag))

                # recipeUsers replacement is especially dangerous: it may only narrow WNG-owned
                # production onto WNG benches. It must never erase an external bench collection.
                if tag=="recipeUsers":
                    users=[(x.text or "").strip() for x in list(elem) if (x.text or "").strip()]
                    external=[u for u in users if not u.startswith("WNG_")]
                    if external:
                        failures.append(
                            f"{path}: {defname} recipeUsers Inherit=False contains external user(s) {external}"
                        )
                    if not users:
                        failures.append(f"{path}: {defname} recipeUsers Inherit=False is empty")

                # Cost/research/ingredient replacement should never silently produce an empty gate.
                if tag in {"costList","ingredients","researchPrerequisites","placeWorkers","thingCategories"}:
                    if child_count==0 and not text:
                        failures.append(f"{path}: {defname} {tag} Inherit=False is empty")

                # Empty stuffCategories is safe only when stuff construction is explicitly disabled.
                if tag=="stuffCategories" and child_count==0 and not text:
                    cost_stuff=(node.findtext("costStuffCount") or "").strip()
                    if cost_stuff not in ("0","0.0"):
                        failures.append(
                            f"{path}: {defname} clears stuffCategories without costStuffCount=0"
                        )

                # A graphicData replacement must provide a concrete texture path.
                if tag=="graphicData":
                    if elem.find("texPath") is None:
                        failures.append(f"{path}: {defname} graphicData Inherit=False lacks texPath")

                # Scenario parts replacement is permitted only on WNG-owned scenarios.
                if tag=="parts" and not defname.startswith("WNG_"):
                    failures.append(f"{path}: external scenario {defname} replaces inherited parts")

                # WNG should never replace inherited collections on a non-WNG concrete def.
                if defname and not wng_owned:
                    # Parent/base helper defs without defName are excluded; concrete foreign defs are not.
                    failures.append(
                        f"{path}: non-WNG def {defname} uses {tag} Inherit=False"
                    )

# Empty replacement policy: comps/weaponTags may intentionally clear inherited behavior.
# Require that those cases stay confined to WNG defs and report them explicitly for review.
for path,defname,parent,abstract,tag in empty_replacements:
    if tag in {"comps","weaponTags","stuffCategories"}:
        wng_owned = (
            not defname
            or defname.startswith("WNG_")
            or defname.endswith("_WNG_Kassa")
        )
        if not wng_owned:
            failures.append(f"{path}: external def {defname} clears inherited {tag}")
        else:
            notes.append(f"reviewed-empty {tag}: {defname} <- {parent or '<none>'}")
    elif tag not in {"recipeUsers","costList","ingredients","researchPrerequisites","placeWorkers","thingCategories"}:
        # Unknown empty replacement is review-required rather than silently accepted.
        failures.append(f"{path}: {defname} empties inherited {tag}; explicit review required")

print("=== D138 DEF INHERITANCE AUDIT ===")
print(f" - Inherit=False occurrences: {len(items)}")
print(" - By element: "+", ".join(f"{k}={v}" for k,v in sorted(counts.items())))
print(f" - Explicit empty replacements: {len(empty_replacements)}")
if notes:
    print("\nREVIEWED EMPTY REPLACEMENTS:")
    for n in notes:
        print(" -",n)
if failures:
    print("\nFAILURES:")
    for f in failures:
        print(" -",f)
    raise SystemExit(1)
print("PASS: inherited-list replacement is confined to WNG defs and validated for destructive empty/external cases.")
