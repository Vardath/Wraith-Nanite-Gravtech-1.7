from pathlib import Path
import re
import xml.etree.ElementTree as ET
from collections import defaultdict, deque

ROOT=Path(".")
RDIR=ROOT/"Defs"/"ResearchProjectDefs"
SRC=ROOT/"Source"/"WraithNaniteGravtech"
failures=[]
notes=[]

# Parse all XML once.
xml_docs=[]
def_names=set()
def_types=defaultdict(set)
for base in (ROOT/"Defs", ROOT/"Compatibility", ROOT/"Patches"):
    if not base.exists():
        continue
    for path in sorted(base.rglob("*.xml")):
        try:
            root=ET.parse(path).getroot()
        except Exception as exc:
            failures.append(f"{path}: XML parse failure: {exc}")
            continue
        xml_docs.append((path,root))
        for node in root.iter():
            dn=(node.findtext("defName") or "").strip()
            if dn:
                def_names.add(dn)
                def_types[dn].add(node.tag)

research={}
research_paths={}
for path,root in xml_docs:
    if "ResearchProjectDefs" not in path.parts:
        continue
    for node in root.findall(".//ResearchProjectDef"):
        name=(node.findtext("defName") or "").strip()
        if not name.startswith("WNG_"):
            continue
        if name in research:
            failures.append(f"duplicate WNG ResearchProjectDef {name}: {research_paths[name]} and {path}")
            continue
        research[name]=node
        research_paths[name]=path

if not research:
    failures.append("no WNG ResearchProjectDefs found")

prereqs={}
required_analyzed={}
custom_unlock_text={}
for name,node in research.items():
    label=(node.findtext("label") or "").strip()
    desc=(node.findtext("description") or "").strip()
    if not label:
        failures.append(f"{name}: missing research label")
    if not desc:
        failures.append(f"{name}: missing research description")

    base_cost=float((node.findtext("baseCost") or "0").strip() or 0)
    knowledge_cost=float((node.findtext("knowledgeCost") or "0").strip() or 0)
    if base_cost <= 0 and knowledge_cost <= 0:
        failures.append(f"{name}: research has neither positive baseCost nor knowledgeCost")

    ps=[]
    pnode=node.find("prerequisites")
    if pnode is not None:
        for li in pnode.findall("li"):
            value=(li.text or "").strip()
            if value:
                ps.append(value)
    prereqs[name]=ps

    ra=[]
    ranode=node.find("requiredAnalyzed")
    if ranode is not None:
        for li in ranode.findall("li"):
            value=(li.text or "").strip()
            if value:
                ra.append(value)
                if value.startswith("WNG_") and value not in def_names:
                    failures.append(f"{name}: requiredAnalyzed references missing WNG Def {value}")
    required_analyzed[name]=ra

    cunlock=node.find("customUnlockTexts")
    custom_unlock_text[name]=bool(cunlock is not None and any((li.text or "").strip() for li in cunlock.findall("li")))

# Internal research references must resolve.
for name,ps in prereqs.items():
    for p in ps:
        if p.startswith("WNG_") and p not in research:
            failures.append(f"{name}: prerequisite references missing WNG research {p}")

# Cycle detection over WNG-only edges.
state={}
stack=[]
cycles=[]
def visit(n):
    state[n]=1
    stack.append(n)
    for p in prereqs.get(n,[]):
        if p not in research:
            continue
        if state.get(p,0)==0:
            visit(p)
        elif state.get(p)==1:
            try:
                idx=stack.index(p)
                cyc=stack[idx:]+[p]
            except ValueError:
                cyc=[p,n,p]
            cycles.append(cyc)
    stack.pop()
    state[n]=2

for name in sorted(research):
    if state.get(name,0)==0:
        visit(name)

seen_cycles=set()
for cyc in cycles:
    key=tuple(cyc)
    if key not in seen_cycles:
        seen_cycles.add(key)
        failures.append("research cycle: "+" -> ".join(cyc))

# Reachability from projects whose WNG prerequisites are empty/reachable.
reachable=set()
changed=True
while changed:
    changed=False
    for name in research:
        internal=[p for p in prereqs[name] if p in research]
        if name not in reachable and all(p in reachable for p in internal):
            reachable.add(name)
            changed=True
for name in sorted(set(research)-reachable):
    failures.append(f"{name}: not reachable through the WNG prerequisite graph")

# Collect direct unlock consumers from XML outside ResearchProjectDefs.
xml_consumers=defaultdict(list)
all_wng_research_refs=[]
for path,root in xml_docs:
    if "ResearchProjectDefs" in path.parts:
        continue
    for elem in root.iter():
        if elem.tag in ("researchPrerequisite","researchPrerequisites"):
            values=[]
            if elem.text and elem.text.strip():
                values.append(elem.text.strip())
            for li in elem.findall("li"):
                if li.text and li.text.strip():
                    values.append(li.text.strip())
            for value in values:
                if value.startswith("WNG_"):
                    all_wng_research_refs.append((path,elem.tag,value))
                    if value not in research:
                        failures.append(f"{path}: {elem.tag} references missing WNG research {value}")
                    else:
                        xml_consumers[value].append((path,elem.tag))

# C# feature consumers: literal research def names outside diagnostics.
source_files=[p for p in SRC.rglob("*.cs") if "Diagnostics" not in p.parts]
source_text={p:p.read_text(encoding="utf-8",errors="ignore") for p in source_files}
code_consumers=defaultdict(list)
for name in research:
    quoted=re.compile(r'["]'+re.escape(name)+r'["]')
    for path,text in source_text.items():
        if quoted.search(text):
            code_consumers[name].append(path)

# Downstream research edges.
downstream=defaultdict(list)
for child,ps in prereqs.items():
    for p in ps:
        if p in research:
            downstream[p].append(child)

# Every project needs some reason to exist: content unlock, runtime feature, explicit custom unlock,
# or a downstream research project.
dead=[]
for name in sorted(research):
    if not xml_consumers[name] and not code_consumers[name] and not custom_unlock_text[name] and not downstream[name]:
        dead.append(name)
        failures.append(f"{name}: dead research branch; no buildable/recipe/runtime/custom unlock and no downstream project")

# requiredAnalyzed refs are additionally inventoried for live acquisition testing.
analyzed_refs=sorted({x for values in required_analyzed.values() for x in values if x.startswith("WNG_")})
analyzed_reference_counts={}
all_nonresearch_text="\n".join(
    ET.tostring(root,encoding="unicode")
    for path,root in xml_docs if "ResearchProjectDefs" not in path.parts
)+"\n"+"\n".join(source_text.values())
for ref in analyzed_refs:
    analyzed_reference_counts[ref]=len(re.findall(re.escape(ref),all_nonresearch_text))
    if analyzed_reference_counts[ref]==0:
        notes.append(f"requiredAnalyzed {ref} resolves as a Def but has no obvious non-research source/XML reference; live acquisition must prove reachability")

# Leaf projects should have a direct content/runtime/custom unlock, not merely terminate silently.
leaves=[n for n in research if not downstream[n]]
for name in sorted(leaves):
    if not xml_consumers[name] and not code_consumers[name] and not custom_unlock_text[name]:
        failures.append(f"{name}: terminal research leaf has no direct content/runtime/custom unlock")

roots=[n for n in research if not any(p in research for p in prereqs[n])]
external_prereq_refs=sorted({p for values in prereqs.values() for p in values if p not in research})

print("=== D167 RESEARCH PROGRESSION AUDIT ===")
print(f" - WNG ResearchProjectDefs: {len(research)}")
print(f" - WNG graph roots: {len(roots)}")
print(f" - WNG graph leaves: {len(leaves)}")
print(f" - WNG projects reachable in internal graph: {len(reachable)} / {len(research)}")
print(f" - internal prerequisite edges: {sum(sum(1 for p in ps if p in research) for ps in prereqs.values())}")
print(f" - external/vanilla prerequisite refs: {len(external_prereq_refs)}")
print(f" - XML buildable/recipe/etc. WNG research references: {len(all_wng_research_refs)}")
print(f" - projects with XML unlock consumers: {sum(1 for n in research if xml_consumers[n])}")
print(f" - projects with C# runtime consumers: {sum(1 for n in research if code_consumers[n])}")
print(f" - projects with custom unlock text: {sum(1 for n in research if custom_unlock_text[n])}")
print(f" - WNG requiredAnalyzed Defs: {len(analyzed_refs)}")
print(f" - dead branches: {len(dead)}")

if analyzed_refs:
    print("\nREQUIRED-ANALYZED INVENTORY:")
    for ref in analyzed_refs:
        owners=[n for n in research if ref in required_analyzed[n]]
        print(f" - {ref}: projects={','.join(owners)}; non-research references={analyzed_reference_counts[ref]}")

if notes:
    print("\nNOTES:")
    for n in notes:
        print(" -",n)

if failures:
    print("\nFAILURES:")
    for f in failures:
        print(" -",f)
    raise SystemExit(1)

print("PASS: WNG research graph is acyclic/reachable, research-gated content resolves, analyzed gates resolve, and no dead terminal branch was found.")
print("NOTE: fresh-colony acquisition of requiredAnalyzed artifacts and real unlock behavior remain live requirements.")
