from pathlib import Path
import subprocess
import sys
import time

ROOT = Path(".")
CRITICAL = [
    "bill_recipe_regression.py",
    "third_party_recipe_preservation.py",
    "patch_blast_radius.py",
    "harmony_blast_radius.py",
    "null_type_lifecycle.py",
    "def_inheritance.py",
    "cross_reference.py",
    "construction_god_mode.py",
    "architect_menu.py",
    "workgiver_worktype.py",
    "building_comp.py",
    "gravship_connectivity.py",
    "vehicle_shuttle_geometry.py",
    "pawn_generation.py",
    "apparel_equipment_retention.py",
    "weapon_orientation_rendering.py",
    "texture_graphic_family.py",
    "replicator_state_machine.py",
    "replicator_death_split.py",
    "human_form_asuran_copy.py",
    "wraith_feeding.py",
    "surgery.py",
    "health_gene.py",
    "faction_audit.py",
    "incident_quest.py",
    "worldgen.py",
    "save_load_migration.py",
    "removal_safety.py",
    "optional_mod_matrix.py",
    "load_order.py",
    "runtime_exception.py",
    "log_diff.py",
    "performance.py",
    "ui.py",
    "research_progression.py",
    "economy.py",
    "resource_conservation.py",
    "dev_mode_parity.py",
    "feature_manifest.py",
    "historical_bug_regression.py",
]
failures=[]
started=time.time()
print("=== D176 RELEASE GATE / MASTER STATIC AUDIT ===")
print("Critical audit scripts:",len(CRITICAL))
for idx,name in enumerate(CRITICAL,1):
    path=ROOT/".github"/"artqa"/name
    if not path.is_file():
        failures.append(f"missing critical audit script: {path}")
        print(f"[{idx}/{len(CRITICAL)}] MISSING {name}")
        continue
    print(f"\n[{idx}/{len(CRITICAL)}] RUN {name}")
    proc=subprocess.run([sys.executable,str(path)],cwd=ROOT,text=True,capture_output=True)
    if proc.stdout:
        print(proc.stdout.rstrip())
    if proc.stderr:
        print(proc.stderr.rstrip())
    if proc.returncode!=0:
        failures.append(f"{name} exit={proc.returncode}")
        print(f"MASTER_GATE_FAIL {name}")
        break
    print(f"MASTER_GATE_PASS {name}")

elapsed=time.time()-started
print(f"\nMASTER_GATE_ELAPSED_SECONDS {elapsed:.2f}")
print("MASTER_GATE_FAILURES",len(failures))
if failures:
    for failure in failures:
        print(" -",failure)
    raise SystemExit(1)
print("MASTER_STATIC_RELEASE_GATE GREEN")
print("NOTE: real-stack/manual live signoff is external evidence and is not claimed by CI.")
