from pathlib import Path
import fnmatch
import json

ROOT=Path(".")
MANIFEST=ROOT/"Docs"/"WNG-FEATURE-MANIFEST.json"
SELF=".github/artqa/feature_manifest.py"
failures=[]
notes=[]

if not MANIFEST.exists():
    raise SystemExit("missing Docs/WNG-FEATURE-MANIFEST.json")

data=json.loads(MANIFEST.read_text(encoding="utf-8"))
if data.get("schema")!=1:
    failures.append(f"unsupported feature-manifest schema {data.get('schema')!r}")

features=data.get("features")
if not isinstance(features,list) or not features:
    failures.append("feature manifest has no features")
    features=[]

def matches(pattern):
    return sorted(
        p for p in ROOT.glob(pattern)
        if p.exists()
    )

ids=set()
claimed_source=set()
test_refs=set()
existing_features=0

for feature in features:
    fid=feature.get("id")
    title=feature.get("title") or fid
    if not fid:
        failures.append("feature entry missing id")
        continue
    if fid in ids:
        failures.append(f"duplicate feature id {fid}")
        continue
    ids.add(fid)

    detectors=feature.get("detectors") or []
    if not detectors:
        failures.append(f"{fid}: no existence detector")
        exists=False
    else:
        exists=any(matches(pat) for pat in detectors)

    if not exists:
        failures.append(f"{fid}: declared feature has no detector match (stale/missing feature)")
        continue
    existing_features+=1

    tests=feature.get("tests") or []
    if not tests:
        failures.append(f"{fid}: untested feature")
    for test in tests:
        if test==SELF:
            failures.append(f"{fid}: feature-manifest audit cannot be its own feature test")
        path=ROOT/test
        if not path.is_file():
            failures.append(f"{fid}: declared test missing: {test}")
        else:
            test_refs.add(test)

    defs=feature.get("required_defs") or []
    if not defs and not feature.get("defs_not_applicable"):
        failures.append(f"{fid}: required_defs empty without defs_not_applicable")
    for pat in defs:
        if not matches(pat):
            failures.append(f"{fid}: required Def path/glob matched nothing: {pat}")

    classes=feature.get("required_class_files") or []
    if not classes and not feature.get("classes_not_applicable"):
        failures.append(f"{fid}: required_class_files empty without classes_not_applicable")
    for pat in classes:
        found=matches(pat)
        if not found:
            failures.append(f"{fid}: required class file/glob matched nothing: {pat}")
        for p in found:
            if p.suffix!=".cs":
                failures.append(f"{fid}: class requirement is not C#: {p}")

    assets=feature.get("required_assets") or []
    if not assets and not feature.get("assets_not_applicable"):
        failures.append(f"{fid}: required_assets empty without assets_not_applicable")
    for pat in assets:
        if not matches(pat):
            failures.append(f"{fid}: required asset path/glob matched nothing: {pat}")

    source_globs=feature.get("source_globs") or []
    for pat in source_globs:
        found=[p for p in matches(pat) if p.is_file() and p.suffix==".cs"]
        if not found:
            failures.append(f"{fid}: source_glob matched no C# files: {pat}")
        for p in found:
            rel=p.relative_to(ROOT).as_posix()
            if "/Diagnostics/" not in rel and "/bin/" not in rel and "/obj/" not in rel:
                claimed_source.add(rel)

# Every production source file must belong to at least one feature entry.
production_source=set()
for p in (ROOT/"Source"/"WraithNaniteGravtech").rglob("*.cs"):
    rel=p.relative_to(ROOT).as_posix()
    if "/Diagnostics/" in rel or "/bin/" in rel or "/obj/" in rel:
        continue
    production_source.add(rel)

unclaimed=sorted(production_source-claimed_source)
for rel in unclaimed:
    failures.append(f"unmanifested production source feature/file: {rel}")

# Also prevent stale claims that are not production C#.
stale_claims=sorted(claimed_source-production_source)
for rel in stale_claims:
    failures.append(f"manifest claims non-production/missing source: {rel}")

# A test reference must be an audit/workflow, not a documentation placeholder.
for test in sorted(test_refs):
    if not (test.startswith(".github/artqa/") or test.startswith(".github/workflows/")):
        failures.append(f"non-executable/non-workflow feature test reference: {test}")

print("=== D174 FEATURE MANIFEST AUDIT ===")
print(f" - manifest features: {len(features)}")
print(f" - existing features: {existing_features}")
print(f" - unique test references: {len(test_refs)}")
print(f" - production C# files: {len(production_source)}")
print(f" - production C# files claimed by features: {len(claimed_source)}")
print(f" - unclaimed production C# files: {len(unclaimed)}")

print("\nFEATURES:")
for feature in features:
    print(
        f" - {feature.get('id')}: tests={len(feature.get('tests') or [])}; "
        f"source_globs={len(feature.get('source_globs') or [])}; "
        f"defs={len(feature.get('required_defs') or [])}; "
        f"classes={len(feature.get('required_class_files') or [])}; "
        f"assets={len(feature.get('required_assets') or [])}"
    )

if notes:
    print("\nNOTES:")
    for n in notes:
        print(" -",n)

if failures:
    print("\nFAILURES:")
    for f in failures:
        print(" -",f)
    raise SystemExit(1)

print("PASS: every declared major WNG feature exists, has machine-readable Def/class/asset requirements, has at least one real test, and every production C# file is claimed by the feature manifest.")
