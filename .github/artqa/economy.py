from pathlib import Path
import re
import xml.etree.ElementTree as ET
from collections import defaultdict

ROOT=Path(".")
failures=[]
notes=[]

# ---------- XML inventory / inheritance ----------
docs=[]
named_thing_parents={}
thingdefs=[]
recipes=[]
all_wng_defs=set()

for base in (ROOT/"Defs", ROOT/"Compatibility"):
    if not base.exists():
        continue
    for path in sorted(base.rglob("*.xml")):
        raw=path.read_text(encoding="utf-8",errors="ignore")
        if "<Patch" in raw or "PatchOperation" in raw:
            continue
        try:
            root=ET.fromstring(raw)
        except ET.ParseError as exc:
            failures.append(f"{path}: XML parse failure: {exc}")
            continue
        docs.append((path,root))
        for node in list(root):
            name=(node.findtext("defName") or "").strip()
            if name.startswith("WNG_"):
                all_wng_defs.add(name)
            if node.tag=="ThingDef":
                if node.get("Name"):
                    named_thing_parents[node.get("Name")]=node
                if name:
                    thingdefs.append((path,node,name))
            elif node.tag=="RecipeDef" and name:
                recipes.append((path,node,name))

def chain(node):
    out=[node]
    seen=set()
    parent=node.get("ParentName")
    while parent and parent in named_thing_parents and parent not in seen:
        seen.add(parent)
        node=named_thing_parents[parent]
        out.append(node)
        parent=node.get("ParentName")
    return out

def inherited_elem(node,xpath):
    for cur in chain(node):
        elem=cur.find(xpath)
        if elem is None:
            continue
        if (elem.get("IsNull") or "").lower()=="true":
            return None
        return elem
    return None

def inherited_text(node,xpath):
    e=inherited_elem(node,xpath)
    return (e.text or "").strip() if e is not None and e.text else None

def positive_mapping(elem):
    if elem is None:
        return {}
    result={}
    for child in list(elem):
        txt=(child.text or "").strip()
        if not txt:
            continue
        try:
            value=float(txt)
        except ValueError:
            failures.append(f"non-numeric amount {child.tag}={txt!r}")
            continue
        result[child.tag]=value
    return result

# ---------- Craftable ThingDefs using recipeMaker ----------
craftable_items=[]
for path,node,name in thingdefs:
    if not name.startswith("WNG_"):
        continue
    maker=inherited_elem(node,"recipeMaker")
    if maker is None:
        continue

    craftable_items.append((path,node,name))
    work=(maker.findtext("workAmount") or "").strip()
    if work:
        try:
            if float(work)<=0:
                failures.append(f"{name}: recipeMaker workAmount={work} is non-positive ({path})")
        except ValueError:
            failures.append(f"{name}: recipeMaker workAmount={work!r} is invalid ({path})")
    else:
        # RimWorld generated recipes use the produced ThingDef's WorkToMake when
        # recipeMaker/workAmount is omitted. Treat that as the effective crafting work.
        work_to_make=inherited_text(node,"statBases/WorkToMake")
        if not work_to_make:
            failures.append(f"{name}: recipeMaker has neither workAmount nor effective WorkToMake ({path})")
        else:
            try:
                if float(work_to_make)<=0:
                    failures.append(f"{name}: effective WorkToMake={work_to_make} is non-positive ({path})")
                else:
                    notes.append(f"{name}: generated recipe work inherited from WorkToMake={work_to_make}")
            except ValueError:
                failures.append(f"{name}: effective WorkToMake={work_to_make!r} is invalid ({path})")

    costs=positive_mapping(inherited_elem(node,"costList"))
    if not costs or not any(v>0 for v in costs.values()):
        failures.append(f"{name}: craftable recipeMaker item has no positive effective costList ({path})")
    for material,count in costs.items():
        if count<=0:
            failures.append(f"{name}: costList material {material} has non-positive count {count} ({path})")
        if material.startswith("WNG_") and material not in all_wng_defs:
            failures.append(f"{name}: costList references missing WNG material {material} ({path})")

    pc=(maker.findtext("productCount") or "1").strip()
    try:
        if float(pc)<=0:
            failures.append(f"{name}: recipeMaker productCount={pc} is non-positive ({path})")
    except ValueError:
        failures.append(f"{name}: recipeMaker productCount={pc!r} is invalid ({path})")

# ---------- Explicit physical production RecipeDefs ----------
physical_recipes=[]
self_loops=[]

def ingredient_defs(li):
    out=[]
    for xp in ("filter/thingDefs/li","filter/allowedDefs/li"):
        out.extend((e.text or "").strip() for e in li.findall(xp) if (e.text or "").strip())
    return out

for path,node,name in recipes:
    if not name.startswith("WNG_"):
        continue

    products=node.find("products")
    surgery=(
        node.find("addsHediff") is not None
        or node.find("removesHediff") is not None
        or node.get("ParentName","").startswith("Surgery")
    )
    if products is None or surgery:
        continue

    product_map=positive_mapping(products)
    if not product_map:
        continue
    physical_recipes.append((path,node,name))

    work=(node.findtext("workAmount") or "").strip()
    if not work:
        failures.append(f"{name}: physical production recipe has no workAmount ({path})")
    else:
        try:
            if float(work)<=0:
                failures.append(f"{name}: workAmount={work} is non-positive ({path})")
        except ValueError:
            failures.append(f"{name}: workAmount={work!r} is invalid ({path})")

    for product,count in product_map.items():
        if count<=0:
            failures.append(f"{name}: product {product} has non-positive count {count} ({path})")
        if product.startswith("WNG_") and product not in all_wng_defs:
            failures.append(f"{name}: product references missing WNG ThingDef {product} ({path})")

    ingredient_nodes=node.findall("./ingredients/li")
    if not ingredient_nodes:
        failures.append(f"{name}: physical production recipe has no ingredients ({path})")
        continue

    exact_inputs=defaultdict(float)
    for idx,li in enumerate(ingredient_nodes,1):
        count_text=(li.findtext("count") or "").strip()
        if not count_text:
            failures.append(f"{name}: ingredient #{idx} has no count ({path})")
            continue
        try:
            count=float(count_text)
        except ValueError:
            failures.append(f"{name}: ingredient #{idx} count={count_text!r} is invalid ({path})")
            continue
        if count<=0:
            failures.append(f"{name}: ingredient #{idx} has non-positive count {count} ({path})")

        defs=ingredient_defs(li)
        cats=[(e.text or "").strip() for e in li.findall("filter/categories/li") if (e.text or "").strip()]
        if not defs and not cats:
            failures.append(f"{name}: ingredient #{idx} has no ThingDef/category filter ({path})")
        for d in defs:
            if d.startswith("WNG_") and d not in all_wng_defs:
                failures.append(f"{name}: ingredient #{idx} references missing WNG material {d} ({path})")
            exact_inputs[d]+=count

    fixed=node.find("fixedIngredientFilter")
    if fixed is not None:
        for e in fixed.findall(".//li"):
            d=(e.text or "").strip()
            if d.startswith("WNG_") and d not in all_wng_defs:
                failures.append(f"{name}: fixedIngredientFilter references missing WNG material {d} ({path})")

    # Direct same-Thing duplication is always suspicious here. More complex conservation is Audit 38.
    for product,pcount in product_map.items():
        if product in exact_inputs and pcount>=exact_inputs[product]:
            self_loops.append((name,product,exact_inputs[product],pcount,path))
            failures.append(
                f"{name}: direct same-item duplication loop {product}: consumes {exact_inputs[product]} and produces {pcount} ({path})"
            )

# ---------- Buildable physical economy delegates to D140 but inventory here ----------
buildables=[]
for path,node,name in thingdefs:
    if not name.startswith("WNG_"):
        continue
    if inherited_text(node,"designationCategory"):
        buildables.append((path,node,name))

# ---------- Runtime mutation sanity ----------
settings=ROOT/"Source"/"WraithNaniteGravtech/WNGSettings.cs"
stext=settings.read_text(encoding="utf-8",errors="ignore") if settings.exists() else ""
for field in ("workAmount","SetBaseCount","products[0].count"):
    if field in stext:
        notes.append("runtime settings mutates recipe economy field: "+field+"; live diagnostic checks effective values")

# Prevent broad zeroing of recipe economy at runtime.
source="\n".join(
    p.read_text(encoding="utf-8",errors="ignore")
    for p in (ROOT/"Source"/"WraithNaniteGravtech").rglob("*.cs")
    if "Diagnostics" not in p.parts
)
for label,pat in {
    "recipe work zeroing": r"\.workAmount\s*=\s*0(?:f|d)?\b",
    "ingredient zeroing": r"SetBaseCount\s*\(\s*0(?:f|d)?\s*\)",
    "product zeroing": r"\.count\s*=\s*0\s*;",
    "cost-list clearing": r"\.costList\s*\.\s*Clear\s*\(",
}.items():
    if re.search(pat,source):
        failures.append("runtime economy bypass detected: "+label)

print("=== D168 ECONOMY AUDIT ===")
print(f" - WNG ThingDefs inventoried: {sum(1 for _,_,n in thingdefs if n.startswith('WNG_'))}")
print(f" - WNG Architect buildables inventoried: {len(buildables)}")
print(f" - WNG recipeMaker craftables checked: {len(craftable_items)}")
print(f" - WNG explicit physical production recipes checked: {len(physical_recipes)}")
print(f" - direct same-item duplication loops: {len(self_loops)}")
print(f" - runtime economy mutation notes: {len(notes)}")

if notes:
    print("\nNOTES:")
    for n in notes:
        print(" -",n)

if failures:
    print("\nFAILURES:")
    for f in failures:
        print(" -",f)
    raise SystemExit(1)

print("PASS: WNG craftable items and explicit production recipes have positive work/material/product amounts, WNG material refs resolve, and no direct same-item duplication loop was found.")
print("NOTE: buildable costs/work are hard-gated by D140; broader transformation conservation/exploit analysis is Audit 38.")
