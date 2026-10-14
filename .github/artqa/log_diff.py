from pathlib import Path
import hashlib
import json
import re
import sys

ROOT = Path(".")
SRC = ROOT / "Source" / "WraithNaniteGravtech"
BASELINE = ROOT / ".github" / "artqa" / "baselines" / "wng_log_callsite_baseline.json"
failures = []

files = [
    p for p in SRC.rglob("*.cs")
    if "Diagnostics" not in p.parts
    and "bin" not in p.parts
    and "obj" not in p.parts
]

call_re = re.compile(r"\bLog\.(Warning|ErrorOnce|Error)\s*\(")
literal_re = re.compile(r'(?:\$@|@\$|\$|@)?"((?:\\.|[^"\\])*)"')

def stable_prefix(text, start):
    window = text[start:start + 1400]
    m = literal_re.search(window)
    if not m:
        return "<dynamic>"
    value = m.group(1)
    value = value.replace("\\n", " ").replace("\\r", " ").replace("\\t", " ")
    value = re.sub(r"\{[^{}]*\}", "{*}", value)
    value = re.sub(r"\s+", " ", value).strip()
    return value[:220] if value else "<empty>"

signatures = []
counts = {"Warning": 0, "Error": 0, "ErrorOnce": 0}
for path in sorted(files):
    text = path.read_text(encoding="utf-8", errors="ignore")
    rel = path.as_posix()
    for m in call_re.finditer(text):
        level = m.group(1)
        counts[level] += 1
        signatures.append(f"{level}|{rel}|{stable_prefix(text, m.end())}")

signatures.sort()
payload = "\n".join(signatures).encode("utf-8")
digest = hashlib.sha256(payload).hexdigest()

current = {
    "schema": 1,
    "description": "Static WNG production Log.Warning/Log.Error/Log.ErrorOnce callsite signature.",
    "production_cs_files": len(files),
    "signature_count": len(signatures),
    "counts": counts,
    "sha256": digest,
}

print("=== D164 LOG-DIFF AUDIT ===")
print(f" - production C# files scanned: {len(files)}")
print(f" - warning/error callsite signatures: {len(signatures)}")
print(f" - Warning calls: {counts['Warning']}")
print(f" - Error calls: {counts['Error']}")
print(f" - ErrorOnce calls: {counts['ErrorOnce']}")
print(f" - current static signature sha256: {digest}")

if not BASELINE.exists():
    print("\nBASELINE MISSING. Suggested baseline JSON:")
    print(json.dumps(current, indent=2, sort_keys=True))
    raise SystemExit(2)

try:
    baseline = json.loads(BASELINE.read_text(encoding="utf-8"))
except Exception as exc:
    print(f"FAIL: could not parse baseline {BASELINE}: {exc}")
    raise SystemExit(1)

for key in ("schema", "production_cs_files", "signature_count", "counts", "sha256"):
    if baseline.get(key) != current.get(key):
        failures.append(f"static log signature field changed: {key}: baseline={baseline.get(key)!r} current={current.get(key)!r}")

# In-game Audit 33 monitor retired after audit completion; static baseline remains enforced.

if failures:
    print("\nFAILURES:")
    for failure in failures:
        print(" -", failure)
    print("\nCurrent baseline replacement candidate:")
    print(json.dumps(current, indent=2, sort_keys=True))
    raise SystemExit(1)

print("PASS: static WNG warning/error callsite signature matches the checked baseline.")
print("NOTE: runtime/startup log monitoring is no longer shipped in the mod; static baseline remains enforced in CI.")
