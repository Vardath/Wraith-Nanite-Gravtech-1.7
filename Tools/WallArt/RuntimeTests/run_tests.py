#!/usr/bin/env python3
"""Compile and exercise the actual WNG wall bridges without game binaries."""
import argparse
import html
from pathlib import Path
import re
import shutil
import subprocess
import tempfile

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]


def patch_class(source, name):
    """Keep the actual attribute/class body; discard unrelated game-dependent code."""
    declaration = source.index(f"internal static class {name}")
    start = source.rfind("[HarmonyPatch", 0, declaration)
    if start < 0:
        raise ValueError(f"Missing patch attribute for {name}")
    # Replace strings/comments by whitespace without changing offsets before scanning braces.
    clean = re.sub(r'//[^\n]*|/\*.*?\*/|"(?:\\.|[^"\\])*"',
                   lambda match: " " * len(match.group()), source, flags=re.S)
    opening = clean.index("{", declaration)
    depth = 1
    for end in range(opening + 1, len(clean)):
        depth += (clean[end] == "{") - (clean[end] == "}")
        if depth == 0:
            return source[start:end + 1]
    raise ValueError(f"Unclosed patch class {name}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default=shutil.which("dotnet"), help="Path to .NET 8 SDK dotnet executable")
    args = parser.parse_args()
    if not args.dotnet:
        parser.error(".NET 8 SDK is required; use --dotnet PATH if it is not on PATH")
    source = (ROOT / "Source/WraithNaniteGravtech/Gravships/GravshipFamilySystems.cs").read_text()
    classes = [patch_class(source, "WNGGravshipBakeIndoorHullMaskPatch")]
    with tempfile.TemporaryDirectory(prefix="wng-wall-bridge-tests-") as directory:
        target = Path(directory)
        for name in ("Program.cs", "Stubs.cs"):
            shutil.copyfile(HERE / name, target / name)
        (target / "ProductionBridges.cs").write_text(
            "using System.Collections.Generic;\nusing HarmonyLib;\nusing RimWorld;\n"
            "using UnityEngine;\nusing Verse;\nnamespace WraithNaniteGravtech {\n"
            + "\n".join(classes) + "\n}\n")
        layer = html.escape(str(ROOT / "Source/WraithNaniteGravtech/Gravships/SectionLayer_WNGFamilyGravshipHull.cs"))
        project = target / "RuntimeTests.csproj"
        project.write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>disable</ImplicitUsings><Nullable>disable</Nullable></PropertyGroup>
  <ItemGroup><Compile Include="{layer}" Link="ProductionHullLayer.cs" /></ItemGroup>
</Project>''')
        subprocess.run([args.dotnet, "run", "--project", str(project), "-c", "Release"], check=True)


if __name__ == "__main__":
    main()
