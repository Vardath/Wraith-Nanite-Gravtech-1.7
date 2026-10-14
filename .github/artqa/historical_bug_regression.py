from pathlib import Path
import json
import subprocess
import sys

ROOT=Path(".")
REG=ROOT/"Docs"/"WNG-HISTORICAL-REGRESSIONS.json"
failures=[]
if not REG.exists():
    raise SystemExit("missing Docs/WNG-HISTORICAL-REGRESSIONS.json")

data=json.loads(REG.read_text(encoding="utf-8"))
if data.get("schema")!=1:
    failures.append(f"unsupported historical regression schema {data.get('schema')!r}")

regs=data.get("regressions") or []
ids=set()
test_to_bugs={}

def read(path):
    return path.read_text(encoding="utf-8",errors="ignore")

for reg in regs:
    rid=reg.get("id")
    title=reg.get("title") or rid
    if not rid:
        failures.append("regression entry missing id")
        continue
    if rid in ids:
        failures.append(f"duplicate regression id {rid}")
        continue
    ids.add(rid)

    tests=reg.get("tests") or []
    guards=reg.get("guards") or []
    if not tests:
        failures.append(f"{rid}: no permanent supporting test")
    if not guards:
        failures.append(f"{rid}: no exact historical guard")
    for test in tests:
        p=ROOT/test
        if not p.is_file():
            failures.append(f"{rid}: supporting test missing: {test}")
        else:
            test_to_bugs.setdefault(test,[]).append(rid)

    for guard in guards:
        rel=guard.get("path")
        if not rel:
            failures.append(f"{rid}: guard missing path")
            continue
        p=ROOT/rel
        if guard.get("exists") is True and not p.exists():
            failures.append(f"{rid}: required path missing: {rel}")
            continue
        if not p.exists():
            failures.append(f"{rid}: guarded path missing: {rel}")
            continue
        if p.is_dir():
            continue
        text=read(p)
        for token in guard.get("contains") or []:
            if token not in text:
                failures.append(f"{rid}: {rel} missing historical contract token {token!r}")
        for token in guard.get("not_contains") or []:
            if token in text:
                failures.append(f"{rid}: {rel} contains forbidden historical regression token {token!r}")
        for token,count in (guard.get("min_occurrences") or {}).items():
            actual=text.count(token)
            if actual < int(count):
                failures.append(f"{rid}: {rel} has {actual} occurrences of {token!r}; expected at least {count}")

print("=== D175 HISTORICAL BUG REGRESSION AUDIT ===")
print(" - permanent named regressions:",len(regs))
print(" - unique supporting test scripts:",len(test_to_bugs))
print(" - registry/schema/guard failures before execution:",len(failures))

if failures:
    print("FAILURES:")
    for f in failures:
        print(" -",f)
    raise SystemExit(1)

# Run each existing supporting audit once even if it protects multiple historical bugs.
for test in sorted(test_to_bugs):
    bug_ids=",".join(test_to_bugs[test])
    print(f"\nRUN {test}  protects={bug_ids}")
    proc=subprocess.run([sys.executable,test],cwd=ROOT,text=True,capture_output=True)
    if proc.stdout:
        print(proc.stdout.rstrip())
    if proc.stderr:
        print(proc.stderr.rstrip())
    if proc.returncode!=0:
        failures.append(f"{test} failed; historical bugs protected: {bug_ids}")

print("\nNAMED REGRESSIONS:")
for reg in regs:
    print(f" - {reg['id']}: {reg['title']}")

if failures:
    print("\nFAILURES:")
    for f in failures:
        print(" -",f)
    raise SystemExit(1)

print(f"PASS: all {len(regs)} named historical WNG regressions retain exact source/data guards and their supporting audits are green.")
