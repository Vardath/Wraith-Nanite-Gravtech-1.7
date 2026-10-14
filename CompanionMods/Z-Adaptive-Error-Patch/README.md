# Z Adaptive Error Patch

Private late-loading RimWorld 1.6 compatibility and error-cancellation layer for Vardath's personal modlist.

This mod is deliberately separate from Wraith & Nanite Gravtech and from upstream mods. It exists to absorb safe-to-repair incompatibilities in the active mod stack without editing each source mod directly.

## Current repair layers

- Guarded XML/schema/cross-reference cleanup in `Patches/AdaptiveKnownFixes.xml`.
- Runtime compatibility assembly in `Assemblies/ZAdaptiveRuntime.dll` for failures that cannot be corrected safely through Def XML alone.
- Independent per-mod startup profiler. RimDoctor is optional.

## Per-mod startup profiling

Z Adaptive installs its own startup instrumentation from its `Mod` constructor and does not reference the RimDoctor assembly at compile time.

It attributes measurable startup work to each active mod in these buckets:

- XML/Def file loading through `ModContentPack.LoadDefs`
- individual Def deserialization
- top-level `PatchOperation.Apply` cost
- `[StaticConstructorOnStartup]` execution time, attributed by assembly ownership

At the end of static-constructor startup it writes the slowest measured mods to `Player.log` even when RimDoctor is not installed.

If RimDoctor **is** installed, Z Adaptive detects `RimDoctor.ReportBuilder` by reflection and appends a `Z Adaptive per-mod startup cost` section to **Diagnostics -> Save report**. The table includes measured milliseconds by bucket, Def count, assembly count, and an on-demand installed-folder size/file-count scan so the report can identify both the largest installed mod and the highest measured startup cost.

The report explicitly separates attributed timings from RimWorld startup work that occurred before Z Adaptive could arm its profiler or that RimWorld performs as shared unified-XML/inheritance work. Unmeasured work is not falsely charged to a mod.

## Gravship rendering guards

The runtime layer currently addresses two verified RimWorld/VGE interactions:

1. **Vanilla Gravship Expanded chroma-key material misuse**
   - VGE assigns Odyssey's `GravshipChromaKey` screen-overlay material to `VGE_FakeTerrain` as a normal `Graphic_Single` material.
   - That shader deliberately lacks ordinary `_MainTex` / `_Color` properties.
   - Z Adaptive prevents the invalid chroma-key material from being inserted into the normal static texture atlas and replaces VGE's direct `Material.color` read in the current `LandingStructureBase.RenderAndSaveTexture` path with a guarded fallback.
   - Older VGE builds are still supported through the previous `LandingStructure.DoCapture` fallback target.

2. **RimWorld 1.6 static-atlas mip-count mismatch**
   - `StaticTextureAtlas.CalcRectsForAtlasNew` allocates mip levels from atlas width only.
   - `GenerateMipmapsWithCompute` later decides how many mip levels to populate from the larger of width and height.
   - Tall/non-square atlases can therefore attempt to write destination mip 2 when only mips 0 and 1 were allocated.
   - Z Adaptive corrects the empty atlas texture's mip allocation before the atlas is populated.

## Live-stack compatibility guards

3. **Vehicle Framework × GravTide early terrain-pathing race**
   - GravTide repaints tidal/foreshore terrain while a map is still being initialized.
   - Vehicle Framework's `TerrainGrid.DoTerrainChangedEffects` hook immediately calls `Vehicles.PathingHelper.RecalculatePerceivedPathCostAt`.
   - On the current stack this can run before `VehiclePathingSystem` has been added to the map, producing a repeated `NullReferenceException` from Vehicle Framework/SmashTools during GravTide terrain painting.
   - Z Adaptive now lets the refresh run normally once the vehicle pathing component exists, but skips only the premature refreshes before that component is available. Vehicle Framework then builds its path grids from the completed terrain during its normal initialization.

4. **Geological Landforms × GravTide Harmony arbitration**
   - Both mods patch lightning strikes and plant-growth tile lookup. GravTide uses bool-returning prefixes that can skip the vanilla methods, while Geological Landforms relies on its own lightning prefix and a `BuildFor` transpiler.
   - Lightning: Z Adaptive preserves both mods' native prefixes. It no longer removes Geological Landforms' patch or installs a second probabilistic filter; that would alter strike odds and trigger a destructive-patch warning. Any remaining upstream ordering conflict requires a verified version-specific fix.
   - For plant-growth calculation, Z Adaptive applies Geological Landforms' pocket-map source-tile substitution to both vanilla `MapPlantGrowthRateCalculator.BuildFor` and GravTide's override prefix, so GravTide cannot bypass the landform-safe tile lookup.
   - The upstream Geological Landforms patches remain installed; the pocket-map override receives an additional targeted transpiler without replacing the original.

5. **Auto Name Babies new-game collection mutation**
   - `AutoNameBabies.BabyNamer.NameUnnamedPlayerBabies` can throw `InvalidOperationException: Collection was modified; enumeration operation may not execute` from its `Game.FinalizeInit` postfix.
   - Naming is optional, so Z Adaptive suppresses only that exact collection-modified exception and allows new-game initialization to continue. Other exceptions from the mod are not swallowed.


6. **RimWorld 1.6 ideology deity grammar bridges**
   - `NameGenerator.GenerateName` can receive `r_deityType` requests with `memeConcept` supplied by generated meme grammar but no `memeConceptDef`, causing repeated unresolved deity-type grammar.
   - Z Adaptive adds `memeConceptDef -> [memeConcept]` only for `r_deityType`, only when `memeConceptDef` is absent, and only when `memeConcept` already exists.
   - Custom deity namers that expose `r_name` but are invoked through Ideology's `r_deityName` root are bridged to their own existing `r_name` output. No deity name is invented or replaced.

7. **Monolyn / Ultima ideology rule-pack inheritance**
   - `DeityMaker_Monolyn` is given the vanilla `NamerDeityGlobal` include when missing, supplying the expected `r_deityName` root while retaining Monolyn's own name components.
   - `NamerIdeoMonolyn` and `Ultima_NamerIdeoFellowship` are given the vanilla `NamerIdeoGlobal` include when missing. This supplies shared ideology grammar such as `hyphenPrefix` rather than hard-coding replacement names.

8. **Map Mode Framework 1.6 growing-period native crash**
   - Map Mode Framework asynchronously pre-caches the Growing Period overlay on a worker `Task`.
   - In the current Odyssey/Worldbuilder stack that worker reaches Burst-backed `PlanetLayer` tile geometry through seasonal-temperature calculation, which can terminate the native RimWorld process instead of producing a recoverable managed exception.
   - Z Adaptive leaves the Growing Period map mode available but forces only its `canCache` flag off, so the unsafe background pre-cache path is not started.

9. **Current-stack stale-reference and helper-race sanitation**
   - Optional `SOA_NuclearThruster` and `BMT_WoollySpider` references are now removed when only an abstract/non-resolvable source def exists.
   - Whitespace-corrupted `MNGravitySwitch` / `MNDownpourImpact` references are removed without touching correctly-authored sound references.
   - Colony Manager helper races from the live stack that advertise humanlike meat with a null `meatDef` are normalized to `hasMeat=false`.

All runtime patches are narrowly targeted and fail open: if a target class/method is absent because a mod was removed or updated, normal game behavior continues.

Validated startup-profiler CI build: workflow run `34085998839` — SUCCESS.

10. **Phaser charging-rack authored mass (2026-10-08)**
   - RimDoctor reported missing authored Mass on `DG_PhaserChargingRack`, `DG_TypeIPhaserChargingRack` and `DG_TypeIIIPhaserChargingRack` while all three are haulable.
   - `Patches/AdaptiveKnownFixes.xml` now adds a `Mass` stat of 10 kg to those three Defs only when they have no authored Mass. Existing Mass values are preserved.
   - This removes the specific missing-mass Def validation errors; the chosen fallback mass should be checked against the original mod's intended balance.

11. **Known-description whitespace normalization (2026-10-08)**
    - `CheatShelf10k`, its generated `Frame_CheatShelf10k`, and `RR_Biological_Exterminators` produced whitespace configuration errors in the latest diagnostic log.
    - The runtime checks these exact ThingDefs at the `ThingDef.ConfigErrors` boundary and trims only leading/trailing whitespace. It preserves the mod authors' original description wording, including generated frame descriptions that may not exist during XML patch loading.
    - In-game verification is still required: CI compiles code but cannot load the full 630-mod installation.

12. **Competing F9 default shortcut (2026-10-08)**
    - `MainTab_History` and `MainTab_CQFA_LevelSchedule` both advertised F9.
    - A guarded XML patch changes only the latter's authored `defaultKeyCode` from `F9` to `None`, keeping the vanilla History shortcut and Level Schedule's tab/button available.
    - Existing per-user key-binding overrides may still need to be reset in the game's key-binding settings.


## Public WNG 1.6 consolidation (2026-10-08)

The public Wraith-Nanite-Gravtech-1.6 repository's `CompanionMods/Z-Adaptive-Error-Patch` is now the distribution source of truth. It contains the complete standalone Z Adaptive 1.6 XML/runtime patch set, including the original hospital, reference, helper-race and worker guards and subsequent gravship, graphics, startup and compatibility fixes. Do not install the separately built standalone copy at the same time: both share package ID `vardath.adaptiveerrorpatch` and runtime Harmony IDs.

The 2026-10-08 Player-prev(5).log reports third-party XML patch failures in MorrowRim - Dunmer Lamp Pack, RimShips, Progression: Gravship, Rim-Elves and Mechanoid Mechanitor. These are emitted while each upstream mod applies its own patch operations; late-loading Z Adaptive cannot undo a patch failure already logged. No invented replacement Defs or silent exception suppression were added. A missing-target patch should instead be repaired at its owning mod's source against its current dependency Def names.

The same log contains InsectWorkEverything transpiler/target failures, stale Yayo settings data, optional facial animation Defs without their type, and a repeated NullReferenceException with no original stack in the supplied log. They remain unverified for a targeted runtime fix. World generation ends with ThreadAbortException, not a reliably attributable root-cause exception in this log. Verify the installed playable ZIP in-game before claiming resolved warnings.

13. **Graphic_Multi null path with supplied texture (2026-10-09)**
    - The current log reaches `Graphic_Multi.Init` and `ContentFinder.Get` with a null texture-path key.
    - The existing fallback skipped empty paths whenever the request also contained a direct texture. Unlike `Graphic_Single`, `Graphic_Multi` still resolves its directional textures from `req.path`.
    - The `Graphic_Multi.Init` prefix now enforces a nonempty path in that case, using the existing bundled four-direction placeholder only for the malformed request. Ordinary requests and direct-texture `Graphic_Single` requests retain their behavior.

14. **Empty AudioGrain clip paths and VGE shader identity (2026-10-09)**
    - An optional, guarded XML operation discards only `SoundDef` grain entries with a present but empty `clipPath` that otherwise request `AudioClip at ''`. Other grain entries remain intact.
    - The existing gravship chroma-key `_MainTex` / `_Color` guards now recognize the shader's exact `Custom/Gravship chroma key` identity as well as the material name. The offending material is still not treated as an ordinary texture.
    - These repairs address narrowly identified warning/error paths, **not** the separate native GPU startup crash or the untraced world-generation `ArgumentOutOfRangeException`.

The optional runtime guard still requires a newly built `ZAdaptiveRuntime.dll` and installation of the CI-produced companion ZIP; editing the XML/source in the repository does not update an already-installed game automatically.

15. **Ocean Floor 1.0.0 — stale work-speed StatDef references (2026-10-09)**
    - Examined the supplied Workshop RAR (ID 3815134598): **Ocean Floor**, `we1tall.depths`, 1.6 build.
    - Its `Patches/WaterPhysics.xml` targets `WorkSpeedGlobal`, whereas the same mod uses `GeneralLaborSpeed` for RimWorld 1.6 recipes. Its Captain/Manners hediff stages also contain five `WorkSpeedGlobal` factors.
    - Guarded, Ocean Floor-only XML operations attach `Depths.StatPart_WaterWork` to `GeneralLaborSpeed` (only if missing) and migrate the five exact hediff factors, preserving their numerical values (1.2, 0.9, 0.8, 1.12 and 0.93).
    - All operations require `GeneralLaborSpeed` to exist and `WorkSpeedGlobal` to be absent, avoiding changes on builds where the older stat is valid. Existing stat parts are preserved. Z Adaptive now explicitly loads after `we1tall.depths`.
    - The supplied player log confirms `[Depths] loaded`. This repair addresses a verifiable compatibility defect, **not a proven map-generation crash**. Ocean Floor's compiled assembly includes settlement-map-generator, starting-tile and gravship hooks overlapping other underwater mods, but the available logs do not isolate a fatal Depths call stack. Those runtime interactions have intentionally not been disabled or broadly suppressed.

16. **GravTide 2026.10.09 × Ocean Floor 1.0.0 — Ocean biome XML load-order collision (2026-10-09)**
    - Inspected the supplied GravTide archive (Workshop ID 3779600989), including its 237 parseable XML files and `GravTide.dll`. The archive's changelog independently reports a **2026-10-09** fix for new-game map generation with Vanilla Gravship Expanded and Dlc collaboration - Void universe; the supplied failed session logs are from 2026-10-08.
    - GravTide's `Patches/Biomes_Ocean.xml` always adds `baseWeatherCommonalities` and `fishTypes` to the vanilla Ocean biome. Ocean Floor's `Patches/OceanRig.xml` inserts fallback versions of the same two tags only when they are absent.
    - When Ocean Floor loads first, the combined patch results in **two** weather tables and **two** fish tables; this was reproduced from the supplied files by applying their patches in both orders. Reversing load order yields singletons.
    - `Patches/GravTideOceanFloor.xml` now removes **only** Ocean Floor's fallback weather and fish tables when the complete, exact GravTide variants coexist. It is gated on both mods' names, checks the authored values rather than load order, and does not alter GravTide's marine content, worldgen, pressure system, or either mod's compiled Harmony patches.
    - The supplied logs also show two Geological Landforms warnings naming GravTide's lightning and plant-growth prefixes. The existing Z Adaptive compatibility transpiler covers the plant-growth pocket-map tile path, and the existing Vehicle Framework early-map readiness guard remains. The lightning warning remains an upstream Harmony overlap, not proven to cause a crash. No blanket prefix suppression was added.
    - A native graphics-device crash and the asynchronous `ArgumentOutOfRangeException` remain unproven to originate in GravTide. Verify the newer GravTide binary and updated Z Adaptive in game.

17. **Geological Landforms 1.7.13.1 × GravTide 2026.10.09 — linked pocket-map lightning (2026-10-09)**
    - Reviewed the user's full Geological Landforms archive (Workshop 2773943594), including its `Lunar/Components/GeologicalLandforms.dll`, and the exact source of both mods' Harmony patches. GL's private `Patch_RimWorld_WeatherEvent_LightningStrike.FireEvent(Map)` prefix directly reads `map.TileInfo.hilliness`. GravTide creates seabed pocket maps whose `Map.Tile` is invalid and whose surface location is held in `PocketMapParent.sourceMap`.
    - When both mods are installed, Z Adaptive now patches only **GL's own prefix method**, resolving hilliness from a valid parent surface map on linked pocket maps. GL's native random rule remains unchanged: impassable surface world tiles pass 30% of automatic lightning checks; other tiles always pass. On all ordinary maps, GL's original prefix runs unchanged. GravTide's native `DoStrike` and `FireEvent` targeting patches are not replaced or unpatched.
    - No blanket suppression of lightning/weather exceptions or redirection of `Map.Tile` was added. The direct collision warning may still appear, because GL detects GravTide's independent short-circuiting prefix even though both are now retained.
    - The existing plant-growth compatibility transpiler remains for backward compatibility, but GravTide 2026.10.09 already calls `SeaPlace.SurfaceTileOf(map)` before writing the growth calculator's tile, so it does **not** depend on that transpiler for the supplied release.
    - This closes a source-identified invalid-tile risk; neither the earlier `ArgumentOutOfRangeException` nor the game's native GPU crash has yet been traced to this code. In-game validation remains necessary.

18. **Live 2026-10-09 logs — VGE invalid grav-engine lookup, Giddy-Up init, ideology role tooltip**
    - `Player(20261008-232806).log` pinpoints repeated `Invalid ThingRequest(group Undefined)` exceptions to `VanillaGravshipExpanded.GravEngineTracker.GetGravEngine_ListerThings(Map)`. Upstream source loops over grav-engine defs and sends `engine.minifiedDef` straight to `ListerThings.ThingsOfDef`; null/unlistable minified definitions can trigger that exception. The new VGE-only transpiler replaces **only this method's** `ThingsOfDef` calls with a guard that skips missing/Undefined requests while preserving legitimate engine searches and boarding AI. It does not suppress exceptions in other mod methods. The supplied report aggregates **34,346** such errors.
    - The Giddy-Up 2 `Pawn_HealthTracker.SetDead` postfix calls `ExtendedDataStorage.Singleton` during early world setup, even though its `MakeDowned` postfix guards against an uninitialized singleton. A prefix on the mod's postfix now skips only the optional dismount bookkeeping when the pawn is absent or Giddy-Up's world storage is not yet initialized. Normal deaths after setup continue through the original handler.
    - An Ideology UI failure at `Precept_Role.AllApparelRequirementLabels` (`Sequence contains no elements`) breaks `Page_ConfigureIdeo` tooltips repeatedly. A finalizer on `Precept_Role.GetTip` catches **only** that exact missing-apparel sequence error and displays a diagnostic tooltip; it does not rewrite ideology roles or apparel rules. The bad source role still needs a Def-level diagnosis.
    - World/map generation actually succeeded in the supplied session (69 steps, 0 faults). After the VGE pawn-AI error flood, saving failed with `OutOfMemoryException` and Windows error 1450, indicating process/system resource exhaustion. This patch may reduce the error flood but is **not** a substitute for checking page file/RAM use and testing whether large saves can complete.
    - The report attributes the save-memory failure to GravTide, but it contains no stack identifying GravTide as the allocation source. Disabling unrelated content mods or suppressing save exceptions is not justified.

19. **Geological Landforms + bundled Map Preview, GravTide, and Custom Quest Framework (2026-10-09)**
    - Verified against the user's full archives: Geological Landforms' `1.6/Lunar/Manifest.xml` requires bundled `MapPreview.dll` and `TerrainGraph.dll`. The Dubs Mint Minimap and Alien Worlds Framework archives contain no Map Preview assembly.
    - The publicly available Custom Quest Framework `QuestEditor_Library.Patch_MapGenerate.prefix` matching the logged signature **always returns true**. It may resize CQF custom maps but does not skip vanilla map generation. Map Preview's "destructive prefix" warning is therefore not demonstrated as an actual conflict for this source version. Removing the prefix would break CQF's custom scenarios, so the patch intentionally retains it.
    - Map Preview's separate RNG-state warning (431939 vs expected 1) concerns preview accuracy; resetting the global random state or silencing the warning without a verified reproducer could corrupt actual map generation. No speculative RNG adjustment is made.
    - Corrected Z Adaptive's GravTide growth-patch discovery to recognize the supplied binary's nested `GravTide.SeabedPlantGrowth+BuildFor_Patch` and the older flat class name. The present GravTide `Prefix` calls `SeaPlace.SurfaceTileOf(map)` to resolve seabed world tiles already. The runtime now verifies that **direct compiled call** before deciding to skip its redundant extra transpiler, reducing intervention with Geological Landforms while preserving the legacy fallback.
    - The preexisting linked-pocket-map lightning and Ocean Floor/GravTide ocean-table patches remain enabled. No log evidence currently implicates Dubs Mint Minimap or Alien Worlds Framework directly.

20. **2026-10-09 failed-worldgen log — null deity naming RulePackDef inside Z Adaptive grammar bridge**
    - During `WorldGenStep_Factions`, `IdeoFoundation_Deity.FillDeity` invoked `NameGenerator.GenerateName`; its Z Adaptive prefix called `GrammarRequest.HasRule`. The exact exception originated in `GrammarRequest.HasRule`'s predicate dereferencing an included `RulePackDef`, with `NullReferenceException`. This occurred before the separate `OutOfMemoryException` in `ModSummaryWindow.DrawContents`.
    - The grammar bridge now removes **only null entries** from the `GrammarRequest.Includes` list on the two deity-specific roots. All defined naming packs, authored rules, culture-specific names and other name-generation requests remain untouched. This avoids the demonstrated null-rule-pack dereference without swallowing worldgen failures.
    - The preceding `WorldFloodFiller.FloodFill` `ArgumentOutOfRangeException` in world reachability construction is a **different, unresolved** error. Its stack contains Alien Worlds Framework, Worldbuilder, Geological Landforms and RJW hooks but does not establish a root cause. The memory failure in the long-event `ModSummaryWindow` is similarly not evidence of an intentional or persisted `ModsConfig.xml` reset.
    - The player log confirms that RimWorld **completed startup with 630 mods**, 94,713 Defs and 747 assemblies. To verify the *persisted* active-mod list, back up and inspect `ModsConfig.xml` or a RimPy profile, neither of which is supplied in these diagnostics.

21. **2026-10-09 repeat failed worldgen and native Direct3D resource exhaustion**
    - Latest `Player(20261009-075100).log` shows `WorldReachability.InvalidateAllFields` / `WorldFloodFiller.FloodFill` throwing `ArgumentOutOfRangeException` during fresh world component construction. RJW and Geological Landforms appear as *postfixes* on the containing method; their presence in the stack does not identify them as the cause of the internal list indexing error.
    - The Player log then repeats an unexpanded `NullReferenceException` reference 2,593 times, while GravTide reports 57,236 strong-wind and 67,174 calm ocean tiles. Unity later fails 73 D3D11 buffer allocations plus two 1920x1080 render textures with HRESULT `0x8007000E`, and crashes natively. No managed `OutOfMemoryException` is recorded in this file, and available evidence cannot distinguish system commit, video memory, graphics driver, or other resource exhaustion. The high mod/asset count and worldgen work increase memory pressure but do not prove a specific mod leaks.
    - `ZAdaptiveRuntimeBootstrap` now logs startup and installs independent Harmony hooks under per-hook exception handling. One failed mod-specific patch can no longer prevent later unrelated runtime guards from loading.
    - Diagnostic-only worldgen hooks log managed-heap and process-private memory at the start of `WorldGenerator.GenerateWorld`, and at a failing `WorldReachability.InvalidateAllFields`. The latter finalizer **returns the original exception unchanged**: no tiles, pathing fields, factions or map-generation steps are silently repaired or skipped.
    - For next testing, compare a 5–10% coverage world with the original coverage. Confirm the first `[Z Adaptive] Runtime bootstrap active` message, check Task Manager's RAM/Commit and Dedicated GPU memory, and isolate custom planet mods on a temporary test profile if even tiny planets fail. Do not disable RJW based on its incidental postfix attribution. An actual world grid repair requires the offending tile/layer or reproducible mod combination.

22. **2026-10-09 evening play log — Kindred smelting definitions and [MAP] ideology initialization (2026-10-10)**
    - Based on `Player(20261009-205930).log` and `rimdoctor_session_20261009_195918.log`. This session completed world generation and reached gameplay; there is no logged `WorldFloodFiller` exception or native out-of-memory crash.
    - `Patches/KindredSmeltCompat.xml`: exactly 60 named Kindred ThingDefs have the reported invalid combination `smeltable=true` without smelt products or ingredient cost. Conditional, late-loading operations set `smeltable=false` for only those definitions without salvage inputs. Both explicit and inherited flags are covered. All actual smelt products, other Kindred items, and user-provided recipes remain unchanged. This avoids a broken zero-return smelt job rather than inventing recoverable materials.
    - `[MAP] Mechanoid Mechanitor` executes `GoodwillSituationManager.RecalculateAll` from its initial-ordinary-faction-relations postfix. The log's exact null stack reaches vanilla `GoodwillSituationWorker_SameIdeo.GetNaturalGoodwillOffset`. A gated prefix (only if this [MAP] scenario type is loaded) allows the original calculation when faction and ideology trackers are ready, but returns neutral zero if they do not exist yet. No valid goodwill offsets are replaced; this prevents a premature scenario initialization exception, but does not attempt to repair [MAP]'s separate failing Mechanoid ThinkTree XML insertion.
    - Neither supplied log contains a stack originating in WNG: **no WNG source change was justified**. Continuing errors include unrelated authors' missing `SOA_NuclearThruster`, `BMT_WoollySpider`, empty sound path, gravship chroma-key shader, missing Quarian graphics, `Imago_Metamorphosis` duplicate gene, and several optional XML integration patch failures. For these, mod ownership or exact source variant must be established before a stronger repair; do not substitute placeholder defs or silence missing-resource exceptions.
    - The Player log contains no `[Z Adaptive] Runtime bootstrap active` line despite a successful build of that version. If this persists after installing this build, confirm that **exactly one** compiled Z Adaptive mod folder is enabled and loads last. Prior XML compatibility fixes cannot be assumed active from the fact that the GitHub build passed.

## 2026-10-11 runtime compatibility repairs (Player-prev(7) / RimDoctor 00:10)

Source: `Source/ZAdaptiveRuntime/NightErrors20261011.cs`. These are independent, optional Harmony guards in the **consolidated** 1.6 companion. No WNG gameplay behavior is changed.

- **World settlement rendering:** A null texture key in `Settlement.get_Material` threw `ArgumentNullException` through `MaterialPool.MatFrom`, with Custom Base Framework/Legacy Worlds involved in the same getter. Only the `key`-null case receives a safe white placeholder material; valid settlement icons and unrelated errors remain unchanged. The originating bad faction/world-object texture definition should still be repaired upstream.
- **Vehicle Framework × Wizardry spawn:** `Vehicles.Patch_Construction.RegisterThingSpawned` threw `NullReferenceException` during Wizardry's `FlyingObject_Spinning.Impact`. Upstream code dereferences `newThing.def` before vanilla spawning rejects invalid input. An early `GenSpawn.Spawn` prefix now rejects **only null Thing inputs**; normal spawn requests are untouched. If the vehicle-framework prefix still throws for a *valid non-vehicle* thing, its own finalizer permits vanilla spawn to continue on a valid map. Vehicle spawns and other exception types remain under the framework. This is a compatibility recovery, not a substitute for upstream investigation.
- **A RimWorld of Magic attraction:** `Projectile_Attraction.Impact` threw `InvalidCastException` while converting an unsuitable object to a flying projectile. On that exception only, the failed attraction projectile is removed so it cannot throw on every subsequent tick. Successful attractions are unchanged; the defective effect is lost when its conversion fails.
- **Combat Extended restraint loop:** `JobGiver_Work.TryGiveJob` may return `CE_RestrainPrisonerToBed` repeatedly for the same prisoner and bed, producing '10 jobs in one tick'. Allow the first attempt, then decline identical assignments to the same worker for 180 ticks. Other jobs/targets still run. This suppresses runaway recursion but does not establish why that prisoner's restraint job is rejected; inspect the prisoner's sleeping spot, accessibility and restraints.
- **Deliberately not hidden:** RJW missing `InteractionDef` data, duplicate registered save-object IDs, absent Bun body graphics, and unresolved unrelated mod XML patches. The logs are insufficient to reconstruct these assets or saved objects safely. Do not blanket-suppress save/load exceptions or rewrite existing saves.

Testing: `.github/workflows/z-adaptive-companion-build.yml` compiles and packages the companion. In-game validation with the exact 626-mod setup and an existing save is still necessary; no automated test can prove the runtime crashes are fully resolved.

## 2026-10-11 RJW and Imago source-informed compatibility (supplied archives)

The supplied `rjw-master.rar` contains the RimWorld 1.6 RJW source. The observed
`SexInteraction..ctor(InteractionDef def)` null reference is reproducible as a code
path, not evidence that an XML InteractionDef is missing. Specifically,
`SexInteractionHelper.ChooseInteraction` returns `false` when no compatible
interactions exist, but `SexUtility.SelectSextype` ignores that result and
returns `SexProps` without an interaction. On the next access,
`SexProps.interaction` calls `new SexInteraction(null)` and the constructor
dereferences the null Def. This affects, for example, the masturbation job
for Bunlon and a Quickie job for Duster in the supplied Player log.

`Source/ZAdaptiveRuntime/RJWImagoCompatibility.cs` patches only
`rjw.JobGiver_Masturbate.TryGiveJob` and
`rjw.JobGiver_DoQuickie.TryGiveJob`. For those two actual job definitions,
it asks RJW's own `SexInteractionFinder.FindInteractions` whether the
candidate pair has any valid interaction. When none exists, the proposed
job is declined instead of running an invalid start/finish path. When
RJW is absent, methods cannot be resolved, or the compatibility probe
fails, normal selection remains unchanged. This handles *newly generated*
jobs; an existing saved/active invalid job may still need to end.

The supplied Imago 1.6 archive (`3816071711.rar`, package `Cain.Imago`)
contains just one `Imago_Metamorphosis` definition, and its
`LoadFolders.xml` correctly selects `Common` and `1.6`. That archive
alone does not explain the log's duplicate gene registration.
`Patches/ImagoDuplicateGeneGuard.xml` conservatively removes only
second-and-later concrete GeneDef nodes with that exact name **if**
duplicates coexist in the aggregated XML. The first gene, its class,
cocoon, adult forms, and xenotype references remain intact. If a different
mod adds the duplicate only at runtime, this XML guard cannot address
that cause; verify against a new startup log before calling the issue fixed.

These are changes to the maintained 1.6 companion, not modifications to
the two supplied original archives. They require the current 1.6 companion
assembly to be rebuilt by the companion CI workflow and verified in-game.

## 2026-10-11 Kindred — Vampires of the Rim update (workshop 3813009890)

Inspected the supplied updated RAR archive directly (RimWorld 1.6,
package `tanyavonhart.kindred`) and cross-checked the 2026-10-10 logs.
The Society of Leopold, Order of Hermes, and Sabbat intentionally attack
vampire colonies; **do not** neutralize their factions or disable raids.

Three known current XML defects remain: `Kindred_Mage_AdeptFlame` inherits
`300~900` weapon money from `Kindred_MageAdept`, while
`Kindred_Mage_TheurgeMind` and `Kindred_Mage_TheurgeAccord` inherit
`500~1200` from `Kindred_MageTheurge`. In the reported actual game load,
their eligible EltexStaff costs at least 2000, causing the three pawn-kind
configuration errors and possible weaponless enemy magi.

`Patches/KindredMageBudgetCompat.xml` adds `2050~2900` only for these
three exact pawn kinds. Guards handle absent child overrides and old
inherited values that have already been expanded. An updated upstream
budget outside those old values is left untouched. Their psycast abilities,
combat power, faction relations, and equipment tags are unchanged.

`About/About.xml` now explicitly loads after `tanyavonhart.kindred`.
The older `local.kindred` entry remains for installs that still use it,
preserving the earlier `KindredSmeltCompat.xml` repairs and other fixes.

Archive XML parses cleanly with no duplicate `(Def type, defName)` keys
in its scanned Def XML. The apparent absent image paths for gore and
sculpture are legitimate `Graphic_Random` directories with individual
variant PNGs, so no art-path rewrite was applied. The prior runtime
warnings about Kindred classes owning Unity static fields could not be
proved to still occur on this updated DLL without a fresh game log;
they are not suppressed. This is a targeted compatibility correction,
not a verified fix for unrelated runtime exceptions.

## 2026-10-11 nine supplied mod archives — source-grounded follow-up

Inspected the actual user-supplied 1.6 RAR content for Capture Expansion
(`3787736781`), Bun Race (`2108324996`), Custom Base Framework
(`3813689040`), Lord of the Rims - Wizardry (Continued)
(`3551419391`), Vehicle Framework (`3014915404`), Mimic Slime -
Deliria (`3636189401`), Humanoid Alien Races (`839005762`),
Dark Ages: Crypts and Tombs (`2963826335`), and Star Wars: The Force
Standalone (`3557220601`). The scanned XML in those archives parsed
without structural errors or duplicated concrete Def names within each
1.6 content selection; that does *not* prove their interaction is safe.

**Capture Expansion 1.6 confirmed code defect:** Its
`WorkGiver_Warden_RestrainPrisonerToBed.JobOnThing` verifies that a
bed is reservable but does **not** check the prisoner's reservation. Its
`JobDriver_RestrainPrisonerToBed.TryMakePreToilReservations` calls
`Prisoner.ClearAllReservations()` before reserving the prisoner and bed.
This can invalidate another job's pawn reservation when multiple wardens
target the same captive, consistent with the logged
`CE_RestrainPrisonerToBed` ten-jobs-in-one-tick storm.

`Source/ZAdaptiveRuntime/CaptureExpansionReservations.cs` patches only
these two optional mod methods when their exact classes exist. It
declines new restraint jobs when the prisoner is already reserved and
claims prisoner/bed reservations *without erasing unrelated claims*.
The earlier `NightErrors20261011` same-job retry throttle is retained
as an additional guard. Ordinary prisoner jobs, restraints, releases,
and non-Capture-Expansion mods are unchanged.

**Bun Race:** The original package *does include* female/male body
textures under `Textures/Bun/Bodies/` and defines
`alienRace/graphicPaths/body=Bun/Bodies/`. The logged attempt to
resolve the bare directory rather than a body-type suffix is therefore
not proof of missing source textures. Big and Small / HAR renderer
selection is a possible contributor; without the specific pawn/bodyType
and a new trace, avoid replacing all Bun visuals or changing gene/body
type restrictions.

**Other archive checks:** Custom Base Framework has no isolated XML
error explaining the null settlement material; the previously
compiled targeted settlement guard remains. The Wizardry/Vehicle
Framework null-spawn guard from `NightErrors20261011` remains. Mimic
Slime ability load-ID collisions occur alongside a duplicated Pawn,
so no ability IDs or serialized pawns were altered. HAR's one
`ResolveReferences` NRE does not identify which race Def triggered
it. The `/lastUser` reference warnings on the Dark Ages skeleton
statue and Force Darkside Web scroll cannot be attributed to code in
either archive; both have unrelated Defs and neither defines a
`lastUser` serializer. No blanket save-game rewrite or suppression
of missing references was added.

All fixes require runtime verification with the updated complete mod
list. Copy the save before testing; the duplicate Pawn Joyce registration
is not resolved by a mod XML patch.

## 2026-10-11 A RimWorld of Magic + Lord of the Rims Wizardry collision (uploaded workshop archive 1201382956)

The user-supplied `1201382956.rar` is **A RimWorld of Magic** by Torann
(`torann.arimworldofmagic`), including active `v1.6` content.
The independent supplied 1.6 Lord of the Rims – Wizardry (Continued)
archive (`3551419391`, `zal.lotrwizardry`) includes a completely different
concrete `ThingDef` named `FlyingObject_Spinning`. This is a genuine
cross-mod collision, not merely a suspected Vehicle Framework problem:

- Torann: `FlyingObject_Spinning` -> `TorannMagic.FlyingObject_Spinning`,
  defined under `v1.6/Defs/AbilityDefs/Abilities_Base.xml`.
- Wizardry: `FlyingObject_Spinning` ->
  `Wizardry.FlyingObject_Spinning`, defined under
  `1.6/Defs/AbilityDefs/LotRW_Abilities_Base.xml`.

Torann's `Projectile_Attraction.LaunchFlyingObect` makes the explicit
`(FlyingObject_Spinning)GenSpawn.Spawn(TorannMagicDefOf.FlyingObject_Spinning, ...)`
cast. Several other Torann abilities use `ThingDef.Named("FlyingObject_Spinning")`
with the same cast. When Wizardry's incompatible definition wins name
resolution, Torann's cast throws `InvalidCastException`, exactly matching
the log's stack `TorannMagic.Projectile_Attraction.LaunchFlyingObect`.

**Root-cause prevention:** `Patches/RimworldOfMagicWizardrySpinDefCollision.xml`
runs *only when both exact 1.6 definitions coexist*. It retains Torann's
original `defName` and XML parent `Name`, renaming only the Wizardry
projectile's `defName` and `Name` to
`LotRW_FlyingObject_Spinning`. `Source/ZAdaptiveRuntime/WizardrySpinDefBinding.cs`
then reassigns the compiled `Wizardry.WizardryDefOf.FlyingObject_Spinning`
field to the unique Wizardry ThingDef by reflection. The wizardry
projectile retains its own original ThingClass, graphic and damage
data, and Torann's spells again resolve to Torann's original class.
The XML and C# repairs are guarded; if either upstream mod changes its
definition, no blind override is attempted. Z Adaptive now requests
`loadAfter` for both mods.

The existing specific fallback in `NightErrors20261011.cs`, which prevents
an invalid Attraction projectile from flooding the log, remains as a
last-resort crash guard. The earlier Vehicle Framework non-vehicle-spawn
guard remains independently useful for Wizardry's flying objects.
A successful CI build validates assembly structure, **not** runtime spell
behavior, load-time DefOf binding, or pre-existing serialized projectiles.
Make a copy of the save before testing. Existing flying-object instances
saved under the previously shared name may need to finish or be cleaned
up safely; this patch does not rewrite saved objects.

## 2026-10-11 Vanilla Expanded Framework and [MAP] Mechanoid Mechanitor (supplied source archives)

Examined user-supplied archives 2023507013 (Vanilla Expanded Framework,
`OskarPotocki.VanillaFactionsExpanded.Core`) and 3811415429
([MAP] Mechanoid Mechanitor 1.6, `xyzf.mechanoidmechanitor`,
version 1.0.10). All extracted XML parsed; no duplicate concrete
`(DefType, defName)` keys were found in either package's 1.6 defs.

**[MAP] recreation ThinkTree:** `1.6/Patches/Recreation/MechanoidMechanitorRecreationThinkTree.xml`
unconditionally inserts
`MAP_MechanoidMechanitor.JobGiver_MechanoidMechanitorRecreation` just
before an *exact* `JobGiver_WanderColony[maxDanger="None"]` nested under
the player-controlled `Mechanoid` Idle branch. The Player-prev(7) log
confirms this specific XPath is missing in the actual 626-mod stack.
This is a fragile compatibility assumption; it need not mean MAP's job
giver or all Mechanoid work is invalid.

**2026-10-14 correction:** the second-chance patch `Patches/MechanoidMechanitorRecreationCompat.xml`
was **removed**, not merely disabled. Its XPath tested for a remaining Mechanoid
Idle child but never checked that the external MAP job-giver *class existed*.
After MAP was removed from the active installation, this XML inserted a dead
`MAP_MechanoidMechanitor.JobGiver_MechanoidMechanitorRecreation` class into
vanilla `Mechanoid`. The resulting `Mechanoid.xml` deserialization exception
removed the shared `Mechanoid` ThinkTreeDef and prevented lifters and WNG's
hauling-capable Child's Toy from selecting jobs. No job giver is injected now.
If MAP is reinstalled later, its own recreation mod patch must be repaired
against the installed Mechanoid Idle tree, with the MAP assembly definitely loaded.
The optional loadAfter entry is harmless if MAP is not installed.

**2026-10-14 job continuity guards:** `Source/ZAdaptiveRuntime/JobContinuity20261014.cs`
handles two *specific* recorded failures while leaving normal work, hunting,
think trees and save records unchanged:

- The observed `JobDriver_Wait.CheckForAutoAttack` NullReferenceException
  can interrupt Wait and even its error-recovery job. A finalizer skips only
  the failing optional auto-attack check, with a one-time warning; every
  successful auto-attack and every other exception remains unchanged.
- TaranMagicFramework's optional `JobAbility+Job_ExposeData.Postfix` threw
  during PostLoadInit on vanilla `SelfShutdown` and `HaulToCell` jobs.
  A finalizer handles only NullReferenceException on these exact two job defs
  in `LoadSaveMode.PostLoadInit`, retaining the base game's loaded job;
  optional Taran-specific metadata may still not recover. Other save and
  Taran jobs/errors are unmodified.

**Not established as fixed:** the removed SMART Hunters save components,
a possible hunting-mod interaction, and the prisoner's competing sleeping-spot
reservation still need validation on a backed-up save. The given logs do not
show a vanilla `WorkGiver_HunterHunt` failure. Do not blindly patch it.
Z Adaptive's source changes require a new DLL build and reinstall of the
bundled companion mod. Removing the harmful XML alone requires replacing the
installed companion patch folder and restarting RimWorld.

**VEF/MVCF duplicate save IDs:** VEF bundles MVCF, so RimDoctor labels
duplicated `MVCF.ManagedVerb` registrations as VEF, but the same
source log repeats deep-saved genes, hediffs, and verbs from many other
mods associated with the same pawn. These are *consistent with* a
pawn or object graph deep-saved more than once, **not proof** of a
specific VEF/MVCF defect. The archive contains no safe XML fix for
that save-ownership problem. No save/load patch, ID rewrite, deletion
of managed verbs, or blanket exception suppression has been applied.
Diagnosis requires a copy of the affected save and a fresh load/save
stack from the updated mod list. This is still an unresolved
save-integrity risk.

## 2026-10-11 RimShips source audit and five-item crash follow-up

Uploaded `3689132119.rar` is RimShips 1.6,
`NeuralCat.RimShips`, with 371 archive entries, C# source, XML
defs, and the assembly `Seaship.dll`. It directly confirms:

1. **RimShips minified grav engine.** `Defs/Things_Buildings/ShipCore.xml`
   defines `RimShipCore` inheriting `GravshipComponentBase` without
   a custom `minifiedDef`. This accounts for the reported
   `RimShipCore doesn't have a custom minified def` warning.
   RimShips' own `Patches/Multifloors/Multifloors.xml` demonstrates
   a correct separate `MinifiedThing` wrapper and `minifiedDef` link
   for another grav engine. The guarded
   `Patches/RimShipsMinifiedEngineCompat.xml` adds
   `ZAdaptive_MinifiedRimShipCore` and links it **only when**
   RimShipCore has no dedicated minified def / still uses generic
   MinifiedThing. Existing custom minified defs, building stats,
   ship travel, engines and ship movement code are unchanged.

2. **RimShips/Avaloi mismatch.** RimShips' `Patches/Genes.xml`
   unconditionally references `LTS_Hydragon`, `LTS_Mucorins`,
   and `LTS_Nexarii` when a mod with display name
   `Det's Xenotypes - Avaloi` is loaded. The log shows
   `LTS_Hydragon` absent, aborting that three-part patch.
   `Patches/RimShipsAvaloiGeneCompat.xml` restores the
   appropriate existing RimShips genes **only for target xenotypes
   actually present**, with per-gene duplicate protection.
   No missing Avaloi xenotype is created and the earlier failure
   message may still be logged by upstream.

3. **RJW invalid interactions.** The prior 1.6 source-backed
   `RJWImagoCompatibility.cs` now includes the optional
   `JobGiver_JoinInBed.TryGiveJob` path alongside Quickie and
   masturbation. The new
   `Source/ZAdaptiveRuntime/RJWExistingInvalidJobsGuard.cs`
   also checks an already generated/saved `JoinInBed`, `Quickie`,
   or `RJW_Masturbate` job at pre-toil reservation time, declining
   only those with no available interaction or missing partner.
   It uses RJW's actual `SexInteractionFinder.FindInteractions`;
   no fictitious sex act is inserted and other RJW jobs are not
   modified. Jobs already mid-toil when loading may still need to
   end; the guards are intentionally not a save migration.

4. **Save serialization risk still open.** The supplied log shows
   `Thing_Human11361` / Joyce duplicated, alongside the same
   pawn's MVCF verbs, RJW organs, RimTalk, Deep Colony, Mimic
   Slime, WNG hediffs, abilities, genes and injuries.
   This strongly suggests a duplicated pawn/object graph but does
   not establish which mod owns the erroneous `Scribe_Deep.Look`
   or whether multiple references are deep-saving the same pawn.
   RimDoctor also reports `InvalidCastException` while saving
   `Map-0-PlayerHome` and references to discarded pawns.
   We did **not** rewrite XML save IDs, delete pawns/genes/hediffs,
   patch `Scribe_Deep`, suppress exceptions or modify MVCF.
   A backed-up `.rws` and a fresh *full* save-time stack trace
   are required before making a save-repair patch.

5. **Bun Race / Big and Small / HAR rendering open.** The uploaded
   Bun Race 1.5 content (loaded under 1.6) explicitly restricts
   valid adult body types to Female, while supplying distinct
   male and female graphic variants and baby/child sprites.
   The logged missing root path `Bun/Bodies/` is consistent with
   a renderer selecting a body type outside that valid set.
   Neither stripping Bun's original body types nor globally
   changing HAR was considered safe with no Big and Small
   framework assembly/source supplied. No textures were
   fabricated or replaced. Inspect updated Big and Small/HAR
   versions or send a new pawn-specific render trace.

The remaining upstream XML errors for Progression: Gravship,
Rim-Elves and MorrowRim are likewise not corrected by speculative
fallbacks without the owning 1.6 source/actual target defs.
Existing compatible fixes, including the MAP mechanoid recreation
fallback, remain untouched.

This is a compiled compatibility update, **not** a claim that
save integrity, Bun race rendering, or all XML warnings are
resolved in the live 626-mod game.

## 2026-10-11 eleven further source archives, remaining errors

Inspected **eleven** user-supplied workshop archives directly, reading
their 1.6 XML, load folders and available source. XML parser validation:
all 1,959 extracted XML files across these archives parsed (351 Big and
Small Framework, 371 Big and Small Genes, 1 Better Compatibility Body
Texture, 10 Progression Gravship, 32 Moon Sugar & Skooma, 5 Dunmer
Lamp Pack, 17 Mashed's Lamps Pack 1, 4 Passive Birthsigns, 3 Ashlander
Yurts, 1,104 Mashed's Ashlands, and 61 Rim-Elves).

### New narrow fallbacks (runtime verification required)

- `Patches/RimElvesIntegratedGenesCompat.xml`: The original Rim-Elves
  `1.6/Patches/Gene_Patch.xml` begins with an *inverted*
  `PatchOperationTest` for `KindInstinct/exclusionTags`. Because
  `Integrated Genes` (`turnovus.biotech.integratedgenes`) is
  installed and known to add `exclusionTags`, that test can fail
  despite a valid gene. Existing tags are not removed. Add only
  `SuperiorityComplex` and `Fanaticism` when each is missing,
  creating the container only when absent, and only when
  `KindInstinct` exists. Never fabricate a missing gene.
- `Patches/ProgressionGravshipFuelOptimizerCompat.xml`:
  `ferny.ProgressionGravship` 1.6 (3522893553), conditional VGE
  Chapter 1 XML blindly replaces
  `ThingDef[FuelOptimizer]/researchPrerequisites` and the exact
  node is missing in the logged stack. If `FuelOptimizer` **exists**
  but the prerequisite element is absent, and
  `Ferny_AstrofuelOptimizer` also exists, add the intended
  `researchPrerequisites` only to that Def. If the entire
  FuelOptimizer ThingDef is absent or moved, do nothing until the
  active VGE Chapter 1 definition is inspected.
- `Patches/DunmerLanternStyleCompat.xml`: The source
  `MorrowRim - Dunmer Lamp Pack` 1.6
  `Patches/DunmerStylesPatch.xml` nests FindMod(Ideology) and
  FindMod(ESCP - Dunmer / MorrowRim - Dunmer Styles) around a
  brittle multi-addition sequence. The reported error is an outer
  operation failure and does not identify which inner target failed.
  Eight independently guarded operations restore its four intended
  style-group/designator entries if the corresponding ESCP
  StyleCategoryDef and list exists, creating only missing lists and
  preventing duplicate entries. Missing StyleCategoryDefs, unrelated
  lamp defs or non-existent designator classes are not fabricated.
  Source for ESCP - Dunmer or Dunmer Styles is still needed to verify
  each target, and original upstream error logs may persist.

### Bun Race graphics (not patched)

Bun Race 1.6 (`spankyh.bunrace.core`) has
`alienRace/generalSettings/alienPartGenerator/bodyTypes` restricted
to Female, Baby and Child despite both `Male_Naked_*` and
`Female_Naked_*` images being packaged in its
`Textures/Bun/Bodies`. Male adult Buns normally render using a
Female torso with a gender-specific overlay. Reports for Big and Small
Framework dated April 2026 reproduce the exact
`Multi(initPath=Bun/Bodies/)` failure and describe a male-body
selection clash with HAR. B&S's 1.6 sources include an optional HAR
integration and VEF integration, while the uploaded Better
Compatibility Body Texture pack provides B&S skins rather than Bun
files. A safe repair should bind a B&S/HAR rendering hook for only Bun
pawns, but a new complete trace and version-specific compiled
renderer method are needed before doing that. Do NOT invent textures
or rewrite Bun's allowed bodyTypes based on the warning alone.

### Still open: save serialization

The older session shows pawn Joyce / Thing_Human11361 registered
twice, then duplicate MVCF verbs, RimTalk, Deep Colony, Mimic Slime
abilities, RJW hediffs, WNG obscuring hediffs and genes. An
`Exception while saving Map-0-PlayerHome: InvalidCastException`
is marked VEF by RimDoctor, but the abbreviated RimDoctor line does
not expose the original stack or establish which owner deep-saved
the Pawn twice. None of the eleven archives is an affected `.rws`
save. No changes to `Scribe_Deep`, save IDs or Pawn collections
were made. Need a backed-up `.rws` with Joyce, fresh Player.log,
and fresh RimDoctor session/report from the latest compiled build.
Deep Colony, RimTalk, Mimic Slime Ability Expended, and Integrated
Genes are useful additional sources if the save identifies them.

### Source identification

- 2925432336: Big and Small - Framework
- 2920751126: Big and Small - Genes & More
- 3563140976: Big and Small - Better Compatibility Body Texture
- 3522893553: Progression: Gravship
- 3244726208: MorrowRim - Moon Sugar and Skooma
- 3654943384: MorrowRim - Dunmer Lamp Pack
- 3244646697: MorrowRim - Mashed's Lamps Pack 1
- 3244646911: MorrowRim - Passive Birthsigns
- 2778937658: Morrowrim Ashlander Yurts
- 3109835541: Mashed's Ashlands
- 3028702598: Rim-Elves

The current batch does not justify broader patches to unrelated
weapons, research, gene serialization or biome changes.

## 2026-10-11 13:12 RimDoctor: post-recovery restraints and map safety

The recovered Dinithera 661289 colony successfully loaded. In the new
626-mod report, the dominant active error is repeated
`CE_RestrainPrisonerToBed` jobs (multiple wardens, prisoner targets and
sleeping spots; 10+ starts per tick). The earlier guard inspected the
workgiver's enumerated `Thing`, which is not guaranteed to be the
prisoner. Source now checks the actual issued job targets (A=Pawn,
B=sleeping spot), declines only invalid/unreservable same-map
assignments, and retains the no-ClearAllReservations driver patch.
Explicit 0/2, 1/2, or 2/2 hook-install diagnostics and the existing
JobGiver_Work retry-throttle install message make stale DLLs/signature
changes visible in the next full Player.log. Neither unrelated work
jobs nor the prisoner's current lay-down job is rewritten.

`Patches/MechControlMeatCompat.xml` addresses the specific
`HL_Milian_FloatUnit_MechControl` helper mech race reported by Colony
Manager Redux: adds/changes `hasMeat=false` only when that race
has no meatDef. Other animals and mechs are untouched.

Outstanding issues are deliberately not hidden: SaiyanMod/Taran Magic
`AbilityClassKI.OnLevelGained` during cross-reference resolution,
GravTide_DivingAnimalConstant think-tree duplicate key -2 and
index error, The Force /lastUser loading, an Ocean Floor graphic
loaded from a worker thread, and Jurassic Rimworld's pawn-group
exception all need the actual owning source or more targeted stack
data for safe fixes. Existing late XML fallbacks do not erase
earlier upstream patch-operation warnings. Map/saving paths and
the recovered colony .rws were not modified. Download/install the
new Actions-built Z Adaptive ZIP; source-only GitHub edits do not
update a previously installed runtime DLL. Verify the emitted
Capture Expansion guard-install lines in the next Player.log.

## 2026-10-11 source audit: GravTide, Ocean Floor, Saiyans, Jurassic (four archives)

Directly inspected 3779600989 (GravTide 2026.10.09, including its full
source), 3815134598 (Ocean Floor 1.6), 2768874080 (The Saiyans 1.6),
and 3541468886 (Jurassic Rimworld Continued 1.6); compared to
Player(20261011-031448).log and RimDoctor 13:12 report.

**Jurassic trader-carrier failure (guarded runtime fix):** The original
Jurassic carrier worker source at
`emipa606/JurassicRimworld/Source/JurassicRimworld/PawnGroupKindWorker_Trader_GenerateCarriers.cs`
enumerates *every* pack animal and calls `x.race.tradeTags.Any` without
checking if `tradeTags` is null. The live NRE stack enters precisely
`GenCollection.Any` from that filter. The new priority-first patch on
`PawnGroupKindWorker_Trader.GenerateCarriers` converts only null
pack-animal trade-tag lists into empty lists. It does not invent
carrier trade tags. If Jurassic's own filter still throws specifically
while selecting a candidate, its Prefix receives a narrowly checked
fallback to vanilla carrier generation. Other exceptions remain visible;
Jurassic caravans with eligible dinosaurs retain original behavior.

**Ocean Floor off-main-thread Figurehead_C graphic (guarded preload):**
All Figurehead_B/C/D PNGs exist in the uploaded source archive, and
the live error comes from `GraphicDatabase.GetInner` refusing an
uncached Graphic_Single on a worker thread. A mod-conditional main-thread
preload at startup / Game.FinalizeInit caches the actual variant
graphics. No texture is fabricated or substituted. Actual custom
shader/size cache-key variations may require further investigation,
so validation in the 626-mod stack is mandatory.

**GravTide think-tree bug (confirmed, deliberately not patched):**
`Defs/ThinkTreeDefs/ThinkTree_DivingAnimal.xml` creates
`GravTide_DivingAnimalConstant` with a `ThinkNode_Subtree` copying
`AnimalConstant`. The live stack enters
`ThinkNode_Subtree.DeepCopy` (out-of-range child index) during
ResolveReferences and leaves two nodes with unresolved key -2.
Removing the subtree would remove normal sea-otter animal AI; replacing
the tree with a global Animal_PreWander job would modify AI of unrelated
animals. Neither is safe without a verified replacement tree and test.

**Saiyan/Taran Magic load issue (not patched):** The supplied Saiyans
package includes 1.6 definitions and DLLs, not source for
`AbilityClassKI.OnLevelGained` or Taran's base class. The live
exception occurs as `HediffSet.DirtyCache` triggers progression inside
`Scribe` cross-reference resolution. Skipping OnLevelGained or
suppressing its NRE at load can leave an incomplete ability state.
Need matching TaranMagicFramework implementation and a targeted
postload re-initialization design, not an exception suppressor.

No save XML, pawn records, map generation, WNG gameplay or other
compatibility patches were changed. GitHub Actions builds the new
runtime DLL; source-only changes do not affect an installed copy.

## 2026-10-11: Taran Magic Framework fifth archive and Saiyan load-phase repair

Archive `3527502876.rar` contains Taran Magic Framework's 1.6 DLL and
XML definitions. The matching upstream 1.6 C# source is public in
`Taranchuk/TaranMagicFramework/1.6/Source/AbilityClass.cs`.
`AbilityClass.OnLevelGained()` immediately invokes
`compAbilities.RecheckAbilities()`, which is unsafe before
`CompAbilities.PostLoadInit` has restored its association.
In Player(20261011-031448).log, the exception is reached through
`SaiyanMod.DirtyCache_Patch.Postfix` -> `GainXP` -> `SetLevel` ->
`SaiyanMod.AbilityClassKI.OnLevelGained` -> Taran's base
`OnLevelGained`, specifically while RimWorld runs
`CrossRefHandler.ResolveAllCrossReferences()`.

`SaiyanLoadPhaseGuard.cs` patches only that optional Saiyan mod's
own DirtyCache postfix and does not execute it while
`Scribe.mode != LoadSaveMode.Inactive`. Unlike a broad exception
finalizer, this avoids levelling incomplete abilities and leaves the
whole vanilla HediffSet/DirtCache function and other mods' postfixes
intact. Ordinary in-game Saiyan health and XP effects are unchanged.
Live confirmation is still required; no save data was modified.

## 2026-10-11: CE repeat-job throttle signature repair (after 15:08 warning)

The October 11 Player and RimDoctor logs explicitly reported
`[Z Adaptive] CE restraint repeat-job throttle target was not found.`
The earlier implementation attempted to patch
`RimWorld.JobGiver_Work.TryGiveJob`, which does **not** exist in
RimWorld 1.6. Decompiled vanilla 1.6 `JobGiver_Work` derives from
`ThinkNode` and instead overrides
`TryIssueJobPackage(Pawn, JobIssueParams)` returning `ThinkResult`.
The compatibility postfix now uses that precise overload and returns
`ThinkResult.NoJob` only for repeated *identical*
`CE_RestrainPrisonerToBed` target tuples within 180 ticks.
Other jobs and initial restraint attempts retain vanilla behavior.

The dedicated Capture Expansion target-reservation guard was already
implemented separately. Its class constructor is now explicitly run
through a guarded startup installer to ensure a clear diagnostic
message about presence or absence of the optional mod hooks.
Neither change touches Scribe save loading or game-map serialization.
The long wait *after selecting a colony save* is therefore not
established as caused by this runtime job throttle.


## 2026-10-12: hunting triage and missing Custom Quest Framework item entries

Evidence: Player(20261011-223244).log, RimDoctor session
20261012_065010 and report 20261012_082237, RimWorld 1.6.4871,
625 active mods.

### Hunting investigation — no unverified AI override

No hunting workgiver or hunt-job exception was emitted in the supplied
logs. The loaded stack includes **SMART Hunters**
(`idzigzag.smarthunters`), **Hunters Use Melee!**
(`mlie.huntersusemelee`) and **Auto Hunt** (`snues.autohunt`).
The SMART Hunters author's compatibility note states that it takes
over hunting job assignment: *designation-only* Auto Hunt is compatible,
but mods that alter how hunters choose targets/behave will not operate
normally while SMART Hunters is active.

Recommended isolation sequence, **not an automatic patch**:
1. Make a backed-up save. In SMART Hunters settings choose **Prepare
   save for mod removal** before disabling it, if the test reaches that step.
   Never remove SMART Hunters during an ongoing group hunt.
2. Leave Auto Hunt active, disable Hunters Use Melee, restart and test
   a vanilla-ranged hunter against manually designated harmless prey.
3. Check hunting work priority, job restrictions, allowed area, an
   ordinary ranged weapon and whether higher-priority work is consuming
   all time. If hunting still fails, use SMART Hunters' safe-removal
   procedure and retry with Auto Hunt and vanilla hunting logic.
4. If vanilla hunts work but SMART Hunters hunts do not, retain the
   game log and test SMART Hunters without other hunting behaviour mods.

We have **not** replaced SMART Hunters AI, globally disabled
`WorkGiver_HunterHunt`, overridden pawn jobs, or suppressed hunting
exceptions. The present diagnostics do not prove that the mod
interaction is the exclusive cause of inactive hunting.

### Actual new XML repair: CQF optional item spawn references

Custom Quest Framework attempts to resolve
`QuestEditor_Library.ThingData` entries pointing to non-existent
`RSH_BloodWallTorch` and `RSH_Stairs` ThingDefs. The new
`Patches/CustomQuestMissingThingDataCompat.xml` checks that each
*concrete* ThingDef is absent and removes only the corresponding
unresolvable ThingData entry. A defined item or unrelated quest item
is never removed. Z Adaptive is ordered after `QF_Patch` to process
the actual optional quest data. This repairs invalid references;
the originally intended item-spawning quest content still requires
its own missing dependency or an upstream content update.
The XML and XPath parse and have been exercised against a small
representative Def tree; the full 625-mod stack is **not** available
for live confirmation.

### Other diagnostics and triage

- Existing Z Adaptive fixes already target
  `SOA_NuclearThruster`, `BMT_WoollySpider`,
  `Imago_Metamorphosis` duplicates, and upstream XML compatibility.
  Their warning lines are still present; do not duplicate the rules
  or assert that all references are resolved without fresh startup logs.
- The active **RimPy Mod Manager Database** produces an empty
  load-folders error. It is not required as an in-game content mod
  to use the external RimPy manager; remove it from the *active game*
  mod list for a diagnostic test.
- **Space Services** repeatedly fails to patch an inherited
  `JobDriver_SitFacingBuilding.TryMakePreToilReservations` method.
  This requires a Space Services-specific source fix or temporary
  removal, not a global Harmony exception filter.
- **[MAP] Mechanoid Mechanitor (1.6)** is now absent from the user's
  installed stack. The unsafe optional late ThinkTree injection has been
  removed after it caused the vanilla Mechanoid tree to fail loading.
  Its original recreation XPath and grower-sow IL-pattern warning remain
  upstream compatibility matters if MAP is installed again.
- The absent FacialAnimation.HeadTypeDef, Yayo's moved settings class,
  malformed optional psytrainer references and inactive RimPy
  database are independent issues and not demonstrated hunting causes.
- RimDoctor's **HIGH** count includes heavily patched methods
  without corresponding runtime faults. It must not be interpreted
  as 72 proven crashes.
- Save-load references involving `/lastUser`, old orphaned Lords or
  parent save-ID registration remain unresolved; no save serialization
  hooks were broadened.

No active mods were disabled in the repository. Modlist reductions
must be performed in the user's local save/profile, with a backup and
save-safe procedure.
