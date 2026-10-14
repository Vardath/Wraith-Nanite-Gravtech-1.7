Run the focused wall rendering bridge tests with .NET 8:

```sh
python Tools/WallArt/RuntimeTests/run_tests.py
```

Pass `--dotnet /path/to/dotnet` when the SDK is outside PATH. No game binaries or NuGet packages are required.

The harness compiles the current production hull layer and extracts the actual cinematic bake Harmony bridge from `GravshipFamilySystems.cs`. Recording engine stand-ins exercise all three hull families and eight angled variants, relative geometry, UVs, tint, mask shader and queue, roof/foundation gates, opposing-family precedence, material batching, and preservation of vanilla bake results. Wall painting, shadows, blueprints and intersections use RimWorld's unmodified native linked renderer.

These tests verify bridge behavior without Unity. Also build the complete mod against its configured RimWorld 1.6 reference package and inspect in-game rendering before making claims about live game behavior.
