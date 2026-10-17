# Z Adaptive Error Patch — RimWorld 1.7 Waylith port

The public source is mirrored into this WNG 1.7 repository as
`CompanionMods/Z-Adaptive-Error-Patch`.

The 1.7 runtime compiles with .NET 10 and Lib.Harmony 3.0.0-rc.2,
against Krafs.Rimworld.Ref 1.7.4977-alpha.1.
The 1.6 runtime continues to compile against net48, Harmony 2.3.3,
and Krafs.Rimworld.Ref 1.6.4871 using the
`ZAdaptiveRuntime16.csproj` build entry point.

The installable mod is built by `Tools/build_dual_version.py`.
It contains `LoadFolders.xml` with separate `v1.6` and `v1.7`
entries; one compiled assembly for each engine, never loaded together.
All original 1.6 XML compatibility patches are preserved under
`1.6/Patches` and **excluded from 1.7**. No old XML patches are
implicitly considered Waylith-safe.

The C# runtime compatibility hooks have been retargeted and compile
against 1.7; optional hook installers check for missing target methods,
but successful compilation does not prove that an upstream mod has
the same behaviour at runtime. Treat the 1.7 port as experimental
until real Waylith play-testing confirms its behaviour.

In particular, 1.6-specific targeted fixes for other mods cannot
be assumed valid or necessary in 1.7. The packaged `1.7/Patches`
allowlist contains only `KindredSmeltCompat17.xml`, conditionally disabling
zero-output smelting on named Kindred ThingDefs confirmed by the
2026-10-17 RimWorld 1.7 Player.log. It does not invent salvage materials.
All other older XML repairs remain quarantined in `1.6/Patches`.
The old GravTide diving-animal think-tree defect is still not silently
patched or suppressed.

Do **not** install the source companion directory directly. Install
the generated `Z-Adaptive-Error-Patch-1.6-and-1.7.zip` instead,
next to the Wraith-Nanite-Gravtech mod. Do not install a second copy
of the old Z Adaptive because it has the same package ID.
