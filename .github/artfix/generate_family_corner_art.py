#!/usr/bin/env python3
"""Regenerate native family hull corners with the reviewed wall-art assembler.

The retired centered-strip generator had no native quadrant coverage. Keep this
entrypoint for the existing workflow while using the same source masters and
geometry as the committed family wall art.
"""
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[2]
ASSEMBLER = ROOT / "Tools/WallArt/assemble_wall_art.mjs"
if not ASSEMBLER.exists():
    raise FileNotFoundError(f"Reviewed wall-art assembler is missing: {ASSEMBLER}")
subprocess.run(["node", str(ASSEMBLER), "--corners-only"], cwd=ROOT, check=True)
