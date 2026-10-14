"""Regenerate dedicated wall icons from native isolated mask0 sampled tiles.

The shared assembler owns UV cropping and every family icon. The legacy mask15
crop showed a four-way junction and could overwrite the isolated wall icons.
"""
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[2]
ASSEMBLER = ROOT / "Tools/WallArt/assemble_wall_art.mjs"
if not ASSEMBLER.exists():
    raise FileNotFoundError(f"Reviewed wall-art assembler is missing: {ASSEMBLER}")
subprocess.run(["node", str(ASSEMBLER), "--icons-only"], cwd=ROOT, check=True)
