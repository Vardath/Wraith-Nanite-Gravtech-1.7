from pathlib import Path
import argparse
import datetime as dt
import hashlib
import json

ROOT = Path(".")
DEFAULT_DLL = ROOT / "Assemblies" / "WraithNaniteGravtech.dll"
DEFAULT_MANIFEST = ROOT / "Assemblies" / "WraithNaniteGravtech.buildinfo.json"

def source_files():
    base = ROOT / "Source" / "WraithNaniteGravtech"
    files = []
    for p in base.rglob("*.cs"):
        if any(part in ("bin", "obj") for part in p.parts):
            continue
        files.append(p)
    project = base / "WraithNaniteGravtech.csproj"
    if project.exists():
        files.append(project)
    return sorted(set(files), key=lambda p: p.as_posix())

def source_fingerprint():
    h = hashlib.sha256()
    files = source_files()
    for p in files:
        rel = p.relative_to(ROOT).as_posix().encode("utf-8")
        data = p.read_bytes()
        h.update(len(rel).to_bytes(4, "big"))
        h.update(rel)
        h.update(len(data).to_bytes(8, "big"))
        h.update(data)
    return h.hexdigest(), files

def sha256(path):
    h = hashlib.sha256()
    with Path(path).open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()

def embedded_ascii_contains(path, value):
    return value.encode("ascii") in Path(path).read_bytes()

parser = argparse.ArgumentParser()
sub = parser.add_subparsers(dest="cmd", required=True)
sub.add_parser("fingerprint")

write = sub.add_parser("write")
write.add_argument("--dll", default=str(DEFAULT_DLL))
write.add_argument("--manifest", default=str(DEFAULT_MANIFEST))
write.add_argument("--source-commit", required=True)
write.add_argument("--built-utc", default=None)

verify = sub.add_parser("verify")
verify.add_argument("--dll", default=str(DEFAULT_DLL))
verify.add_argument("--manifest", default=str(DEFAULT_MANIFEST))
verify.add_argument("--expected-source-commit", default=None)
verify.add_argument("--require-embedded", action="store_true")

args = parser.parse_args()
fingerprint, files = source_fingerprint()

if args.cmd == "fingerprint":
    print(fingerprint)
    raise SystemExit(0)

dll = Path(args.dll)
manifest = Path(args.manifest)
if not dll.exists():
    raise SystemExit(f"Missing DLL: {dll}")

if args.cmd == "write":
    built_utc = args.built_utc or dt.datetime.now(dt.timezone.utc).replace(microsecond=0).isoformat()
    payload = {
        "schema": 1,
        "assembly": "WraithNaniteGravtech.dll",
        "source_commit": args.source_commit,
        "source_fingerprint_sha256": fingerprint,
        "source_file_count": len(files),
        "dll_sha256": sha256(dll),
        "dll_size": dll.stat().st_size,
        "built_utc": built_utc,
    }
    manifest.parent.mkdir(parents=True, exist_ok=True)
    manifest.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print("WNG_BUILDINFO_SOURCE_COMMIT", payload["source_commit"])
    print("WNG_BUILDINFO_SOURCE_SHA256", payload["source_fingerprint_sha256"])
    print("WNG_BUILDINFO_SOURCE_FILES", payload["source_file_count"])
    print("WNG_BUILDINFO_DLL_SHA256", payload["dll_sha256"])
    print("WNG_BUILDINFO_DLL_SIZE", payload["dll_size"])
    print("WNG_BUILDINFO_BUILT_UTC", payload["built_utc"])
    raise SystemExit(0)

if not manifest.exists():
    raise SystemExit(f"Missing build manifest: {manifest}")

data = json.loads(manifest.read_text(encoding="utf-8"))
errors = []
if data.get("schema") != 1:
    errors.append(f"unsupported buildinfo schema {data.get('schema')!r}")
if data.get("source_fingerprint_sha256") != fingerprint:
    errors.append(
        "stale DLL/source manifest: source fingerprint differs "
        f"(manifest={data.get('source_fingerprint_sha256')}, current={fingerprint})"
    )
if data.get("source_file_count") != len(files):
    errors.append(
        f"source file count differs (manifest={data.get('source_file_count')}, current={len(files)})"
    )
current_dll_sha = sha256(dll)
if data.get("dll_sha256") != current_dll_sha:
    errors.append(
        f"DLL hash differs (manifest={data.get('dll_sha256')}, current={current_dll_sha})"
    )
if data.get("dll_size") != dll.stat().st_size:
    errors.append(
        f"DLL size differs (manifest={data.get('dll_size')}, current={dll.stat().st_size})"
    )
if args.expected_source_commit and data.get("source_commit") != args.expected_source_commit:
    errors.append(
        f"source commit differs (manifest={data.get('source_commit')}, expected={args.expected_source_commit})"
    )
if not data.get("built_utc"):
    errors.append("build timestamp missing")
if args.require_embedded:
    commit = data.get("source_commit") or ""
    fp = data.get("source_fingerprint_sha256") or ""
    if not commit or not embedded_ascii_contains(dll, commit):
        errors.append("DLL does not contain embedded WNGSourceCommit metadata value")
    if not fp or not embedded_ascii_contains(dll, fp):
        errors.append("DLL does not contain embedded WNGSourceFingerprint metadata value")

print("WNG_PARITY_SOURCE_COMMIT", data.get("source_commit"))
print("WNG_PARITY_SOURCE_SHA256", fingerprint)
print("WNG_PARITY_SOURCE_FILES", len(files))
print("WNG_PARITY_DLL_SHA256", current_dll_sha)
print("WNG_PARITY_DLL_SIZE", dll.stat().st_size)
print("WNG_PARITY_BUILT_UTC", data.get("built_utc"))
print("WNG_PARITY_ERRORS", len(errors))
for error in errors:
    print("WNG_PARITY_ERROR", error)

if errors:
    raise SystemExit(1)
print("WNG_DLL_SOURCE_PARITY GREEN")
