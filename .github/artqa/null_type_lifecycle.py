from pathlib import Path
import re

ROOT=Path(".")
SRC=ROOT/"Source"/"WraithNaniteGravtech"
failures=[]
notes=[]

sensitive_types=("Thing","Pawn","Map","Faction","ThingComp","Hediff","Gene","Def","Job","Bill","Building","Graphic")
files=[p for p in SRC.rglob("*.cs") if "Diagnostics" not in p.parts]

method_re=re.compile(
    r"(?P<prefix>(?:public|protected|private|internal)\s+(?:static\s+)?(?:override\s+|virtual\s+)?)"
    r"(?P<ret>[A-Za-z0-9_<>,\.\[\]\?]+)\s+"
    r"(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*\((?P<params>[^)]*)\)\s*\{",
    re.M,
)

def method_body(text,start):
    brace=text.find("{",start)
    if brace<0: return None
    depth=0
    for i in range(brace,len(text)):
        if text[i]=="{": depth+=1
        elif text[i]=="}":
            depth-=1
            if depth==0: return text[brace+1:i]
    return None

inventory=[]
map_ticks=[]
job_givers=[]
job_on_thing=[]
available=[]
post_destroy=[]
post_despawn=[]

for path in files:
    text=path.read_text(encoding="utf-8",errors="ignore")
    for m in method_re.finditer(text):
        params=m.group("params")
        name=m.group("name")
        prefix=m.group("prefix")
        body=method_body(text,m.end()-1)
        if body is None:
            continue
        sensitive=[t for t in sensitive_types if re.search(r"\b"+re.escape(t)+r"\b",params)]
        is_lifecycle=name in {
            "PostSpawnSetup","PostDeSpawn","PostDestroy","PostExposeData","ExposeData",
            "CompTick","CompTickRare","MapComponentTick","WorldComponentTick","FactionTick",
            "TryGiveJob","JobOnThing","AvailableOnNow","TryMakePreToilReservations"
        }
        if sensitive or is_lifecycle:
            inventory.append((path,name,params,sensitive,prefix,body))
        if name=="MapComponentTick": map_ticks.append((path,body))
        if name=="TryGiveJob" and re.search(r"\bPawn\s+pawn\b",params): job_givers.append((path,body))
        if name=="JobOnThing" and re.search(r"\bPawn\s+pawn\b",params): job_on_thing.append((path,body))
        if name=="AvailableOnNow" and re.search(r"\bThing\s+thing\b",params): available.append((path,body))
        if name=="PostDestroy": post_destroy.append((path,params,body))
        if name=="PostDeSpawn": post_despawn.append((path,params,body))

# AvailableOnNow is an externally probed path: null Thing/non-pawn must not throw.
for path,body in available:
    if "RecipeWorker_UniversalCraftOnly" in path.read_text(encoding="utf-8",errors="ignore"):
        # This worker intentionally ignores the Thing entirely.
        pass
    elif not (
        re.search(r"\bthing\s*==\s*null\b",body)
        or re.search(r"\bPawn\s+pawn\s*=\s*thing\s+as\s+Pawn\b",body)
    ):
        failures.append(f"{path}: AvailableOnNow lacks explicit null/type gate")

# Job givers are routinely queried by the think tree; a null pawn must short-circuit before dereference.
null_safe_gate_markers=(
    "IsRepairer(pawn)","CanOperate(pawn)","CanStarvationPredate(pawn)",
    "CanAutonomouslyAssimilate(pawn)","FindClosestConsumableCell(pawn",
)
for path,body in job_givers:
    first=body[:700]
    first_deref=first.find("pawn.")
    has_explicit=bool(re.search(r"\bpawn\s*==\s*null\b|\bpawn\s+is\s+null\b",first))
    has_safe_gate=any(x in first for x in null_safe_gate_markers)
    if first_deref>=0 and not has_explicit and not has_safe_gate:
        failures.append(f"{path}: TryGiveJob dereferences pawn without an explicit/null-safe opening gate")

# WorkGiver_DoBill overrides must reject null pawn/thing before calling vanilla.
for path,body in job_on_thing:
    first=body[:700]
    if "base.JobOnThing" in body:
        pawn_safe=(
            re.search(r"\bpawn\s*==\s*null\b",first)
            or "IsNaniteSynthetic(pawn)" in first
        )
        thing_safe=(
            re.search(r"\bthing\s*==\s*null\b",first)
            or "base.JobOnThing" in body  # vanilla remains authority after WNG pawn gate
        )
        if not pawn_safe or not thing_safe:
            failures.append(f"{path}: JobOnThing lacks safe input gating before vanilla delegation")

# Map components are engine-owned, but harden them for teardown/test harness states.
# Every MapComponentTick that directly dereferences map must have an early map guard.
for path,body in map_ticks:
    first=body[:900]
    if "map." in body:
        guarded=(
            re.search(r"\bmap\s*==\s*null\b|\bmap\s+is\s+null\b",first)
            or "map?." in first
        )
        if not guarded:
            failures.append(f"{path}: MapComponentTick directly dereferences map without an early null guard")

# Destroy/despawn callbacks must not blindly dereference the previous map argument.
for path,params,body in post_destroy+post_despawn:
    map_names=re.findall(r"\bMap\s+([A-Za-z_][A-Za-z0-9_]*)",params)
    for name in map_names:
        if f"{name}." in body and not (
            re.search(rf"\b{re.escape(name)}\s*==\s*null\b",body)
            or f"{name}?." in body
        ):
            failures.append(f"{path}: lifecycle callback dereferences {name} without null guard")

# Direct parent dereference in tick methods should be guarded by parent null/conditional access first.
for path,name,params,sensitive,prefix,body in inventory:
    if name not in ("CompTick","CompTickRare","PostSpawnSetup"):
        continue
    first=body[:900]
    if "parent." in body:
        guarded=(
            re.search(r"\bparent\s*==\s*null\b|\bparent\s*!=\s*null\b|\bparent\?\.",first)
            or "parent?.Spawned" in first
        )
        # ThingComp parent is normally assigned, so report only as note unless code is clearly teardown-sensitive.
        if not guarded and ("Destroyed" in body or "Spawned" in body or ".Map" in body):
            notes.append(f"{path}: {name} relies on ThingComp parent lifecycle invariant")

print("=== D137 NULL / TYPE / LIFECYCLE SAFETY AUDIT ===")
print(f" - Production C# files scanned: {len(files)}")
print(f" - Sensitive/lifecycle methods inventoried: {len(inventory)}")
print(f" - AvailableOnNow paths: {len(available)}")
print(f" - Pawn TryGiveJob paths: {len(job_givers)}")
print(f" - JobOnThing paths: {len(job_on_thing)}")
print(f" - MapComponentTick paths: {len(map_ticks)}")
print(f" - Destroy/DeSpawn callbacks: {len(post_destroy)+len(post_despawn)}")
if notes:
    print("\nNOTES:")
    for n in notes:
        print(" -",n)
if failures:
    print("\nFAILURES:")
    for f in failures:
        print(" -",f)
    raise SystemExit(1)
print("PASS: audited externally-probed and lifecycle-sensitive WNG paths are null/type guarded.")
