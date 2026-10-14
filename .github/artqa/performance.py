from pathlib import Path
import re

ROOT=Path(".")
SRC=ROOT/"Source"/"WraithNaniteGravtech"
failures=[]
notes=[]

files=[p for p in SRC.rglob("*.cs") if "Diagnostics" not in p.parts]

method_re=re.compile(
    r"(?P<prefix>(?:public|protected|private|internal)\s+(?:static\s+)?(?:override\s+|virtual\s+)?)"
    r"(?P<ret>[A-Za-z0-9_<>,\.\[\]\?]+)\s+"
    r"(?P<name>MapComponentTick|CompTick|CompTickRare)\s*\([^)]*\)\s*\{",
    re.M,
)

def method_body(text,start):
    brace=text.find("{",start)
    if brace<0: return None
    depth=0
    for i in range(brace,len(text)):
        ch=text[i]
        if ch=="{": depth+=1
        elif ch=="}":
            depth-=1
            if depth==0: return text[brace+1:i]
    return None

def throttled(name,body):
    if name=="CompTickRare":
        return True
    markers=(
        "IsHashIntervalTick(",
        "TicksGame %",
        "TicksAbs %",
        "nextTick",
        "nextCheckTick",
        "nextScanTick",
        "nextEvaluationTick",
        "nextUpdateTick",
        "nextRefreshTick",
        "lastCheckTick",
        "lastScanTick",
        "RareTickInterval",
    )
    if any(m in body for m in markers):
        return True
    # Common local-tick cadence guard, e.g. "if (now % ScanIntervalTicks != 0) return;".
    if re.search(r"\b(?:now|tick|ticks|currentTick)\s*%\s*[A-Za-z0-9_]+", body):
        return True
    return False

tick_methods=[]
heavy=[]
for path in files:
    text=path.read_text(encoding="utf-8",errors="ignore")
    for m in method_re.finditer(text):
        name=m.group("name")
        body=method_body(text,m.end()-1)
        if body is None:
            failures.append(f"{path}: could not parse {name} body")
            continue
        is_throttled=throttled(name,body)
        tick_methods.append((path,name,is_throttled,body))

        full_map=[
            token for token in (
                "AllThings","AllPawnsSpawned","AllPawns","mapPawns.",
                "listerThings.AllThings","Find.Maps","AllMaps"
            ) if token in body
        ]
        linq=[
            token for token in (
                ".Where(", ".Select(", ".OrderBy(", ".OrderByDescending(",
                ".ToList(", ".ToArray(", ".GroupBy(", ".Distinct(", ".Count("
            ) if token in body
        ]
        reflection=[
            token for token in (
                "System.Reflection","BindingFlags","GetMethod(","GetField(",
                "GetProperty(","MakeGenericMethod(","Activator.CreateInstance",
                "AccessTools."
            ) if token in body
        ]
        defdb=len(re.findall(r"DefDatabase\s*<",body))
        nested_loop=bool(re.search(r"foreach\s*\([^)]*\)[\s\S]{0,1200}foreach\s*\(",body))

        score=0
        if full_map: score+=2
        if len(linq)>=2: score+=1
        if reflection: score+=3
        if defdb: score+=2
        if nested_loop: score+=2

        if score:
            heavy.append((path,name,is_throttled,score,full_map,linq,reflection,defdb,nested_loop))

        # Hard failures are only for work that executes every normal tick.
        if not is_throttled:
            if reflection:
                failures.append(f"{path}: {name} performs reflection every tick: {', '.join(reflection)}")
            if defdb>=2:
                failures.append(f"{path}: {name} performs {defdb} DefDatabase lookups every tick")
            if full_map and len(linq)>=2:
                failures.append(f"{path}: {name} combines full-map scan with repeated LINQ every tick: map={full_map}, linq={linq}")
            if full_map and nested_loop:
                failures.append(f"{path}: {name} combines full-map scan with nested iteration every tick")

print("=== D165 PERFORMANCE AUDIT ===")
print(f" - production C# files scanned: {len(files)}")
print(f" - tick-heavy methods inventoried: {len(tick_methods)}")
print(f" - throttled/rare tick methods: {sum(1 for _,_,t,_ in tick_methods if t)}")
print(f" - direct every-tick methods: {sum(1 for _,_,t,_ in tick_methods if not t)}")
print(f" - methods containing reviewed heavy-work markers: {len(heavy)}")

if heavy:
    print("\nHEAVY-WORK INVENTORY:")
    for path,name,is_throttled,score,full_map,linq,reflection,defdb,nested in heavy:
        bits=[]
        if full_map: bits.append("map="+",".join(full_map))
        if linq: bits.append("linq="+",".join(linq))
        if reflection: bits.append("reflection="+",".join(reflection))
        if defdb: bits.append(f"DefDatabase={defdb}")
        if nested: bits.append("nested-loop")
        print(f" - {path}: {name}: {'throttled/rare' if is_throttled else 'EVERY-TICK'}: score={score}: " + "; ".join(bits))

if failures:
    print("\nFAILURES:")
    for f in failures:
        print(" -",f)
    raise SystemExit(1)

print("PASS: no unthrottled WNG tick method combines the audited high-cost patterns above the hard-gate thresholds.")
print("NOTE: live 1/10/50/100 Replicator-drone scaling samples remain required for real-stack timing.")
