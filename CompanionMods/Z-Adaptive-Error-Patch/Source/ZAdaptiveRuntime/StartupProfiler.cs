using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using HarmonyLib;
using Verse;

namespace ZAdaptiveRuntime
{
    public sealed class ZAdaptiveProfilerMod : Mod
    {
        public ZAdaptiveProfilerMod(ModContentPack content) : base(content)
        {
            ZAdaptiveEarlyFixes.Install();
            StartupProfiler.Install();
        }
    }

    /// <summary>
    /// Def validation and key-binding compatibility must install from the Mod
    /// constructor, before implied Defs and config checks. The regular
    /// [StaticConstructorOnStartup] bootstrap executes too late for these.
    /// </summary>
    public static class ZAdaptiveEarlyFixes
    {
        private static bool installed;
        private static bool keyBindingWarningLogged;

        public static void Install()
        {
            if (installed)
                return;
            installed = true;

            try
            {
                var harmony = new Harmony("vardath.adaptiveerrorpatch.early");
                MethodInfo validate = AccessTools.Method(typeof(ThingDef), "ConfigErrors");
                MethodInfo whitespacePrefix = AccessTools.Method(typeof(ZAdaptiveEarlyFixes), nameof(KnownDescriptionWhitespacePrefix));
                if (validate != null && whitespacePrefix != null)
                    harmony.Patch(validate, prefix: new HarmonyMethod(whitespacePrefix));
                else
                    Log.Warning("[Z Adaptive] Early ThingDef.ConfigErrors whitespace guard could not attach.");

                // Key bindings for main tabs can be implied *after* the XML patch
                // stage. Set only the conflicting optional default before
                // KeyPrefs reads user overrides. Never overwrite custom bindings.
                MethodInfo keyPrefsInit = AccessTools.Method("Verse.KeyPrefs:Init") ??
                                          AccessTools.Method("RimWorld.KeyPrefs:Init");
                MethodInfo keyPrefix = AccessTools.Method(typeof(ZAdaptiveEarlyFixes), nameof(KeyPrefsInitPrefix));
                if (keyPrefsInit != null && keyPrefix != null)
                    harmony.Patch(keyPrefsInit, prefix: new HarmonyMethod(keyPrefix));
                else
                    Log.Warning("[Z Adaptive] Optional Level Schedule key preference guard could not attach.");
            }
            catch (Exception ex)
            {
                Log.Warning("[Z Adaptive] Early compatibility fix installation failed: " + ex);
            }
        }

        private static void KnownDescriptionWhitespacePrefix(ThingDef __instance)
        {
            if (__instance == null || string.IsNullOrEmpty(__instance.description))
                return;

            switch (__instance.defName)
            {
                case "CheatShelf10k":
                case "Frame_CheatShelf10k":
                case "RR_Biological_Exterminators":
                    __instance.description = __instance.description.Trim();
                    break;
            }
        }

        private static void KeyPrefsInitPrefix()
        {
            try
            {
                // Access these members reflectively because the key binding
                // storage moved between RimWorld versions and mod builds.
                Type bindingType = AccessTools.TypeByName("Verse.KeyBindingDef") ??
                                   AccessTools.TypeByName("RimWorld.KeyBindingDef");
                if (bindingType == null)
                    return;

                Type databaseType = typeof(DefDatabase<>).MakeGenericType(bindingType);
                MethodInfo lookup = AccessTools.Method(databaseType, "GetNamedSilentFail", new[] { typeof(string) });
                object binding = lookup?.Invoke(null, new object[] { "MainTab_CQFA_LevelSchedule" });
                FieldInfo defaultKeyField = AccessTools.Field(bindingType, "defaultKeyCode");
                if (binding == null || defaultKeyField == null)
                    return;

                object current = defaultKeyField.GetValue(binding);
                if (current is UnityEngine.KeyCode key && key == UnityEngine.KeyCode.F9)
                    defaultKeyField.SetValue(binding, UnityEngine.KeyCode.None);
            }
            catch (Exception ex)
            {
                if (keyBindingWarningLogged)
                    return;
                keyBindingWarningLogged = true;
                Log.Warning("[Z Adaptive] Could not normalize optional Level Schedule default key: " + ex);
            }
        }
    }

    public static class StartupProfiler
    {
        public sealed class Cost
        {
            public string Name;
            public string PackageId;
            public long LoadDefsTicks;
            public long DefParseTicks;
            public long PatchTicks;
            public long StaticCtorTicks;
            public int ParsedDefs;
            public int PatchOps;
            public int StaticCtors;
            public int DefCount;
            public int AssemblyCount;
            public long InstalledBytes = -1;
            public int InstalledFiles = -1;
            public ModContentPack Pack;

            public double LoadDefsMs => TicksToMs(LoadDefsTicks);
            public double DefParseMs => TicksToMs(DefParseTicks);
            public double PatchMs => TicksToMs(PatchTicks);
            public double StaticCtorMs => TicksToMs(StaticCtorTicks);
            public double MeasuredMs => LoadDefsMs + DefParseMs + PatchMs + StaticCtorMs;
        }

        public sealed class TimerState
        {
            public long Start;
            public Cost Cost;
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<ModContentPack, Cost> ByPack = new Dictionary<ModContentPack, Cost>();
        private static readonly Dictionary<Assembly, Cost> ByAssembly = new Dictionary<Assembly, Cost>();
        private static readonly Dictionary<PatchOperation, Cost> PatchOwner = new Dictionary<PatchOperation, Cost>();
        private static readonly HashSet<MethodBase> PatchedPatchApplyMethods = new HashSet<MethodBase>();
        private static bool installed;
        private static bool startupFinished;
        private static bool sizesScanned;
        private static long installTimestamp;

        public static void Install()
        {
            lock (Gate)
            {
                if (installed) return;
                installed = true;
                installTimestamp = Stopwatch.GetTimestamp();
            }

            try
            {
                RefreshMaps();
                var harmony = new Harmony("vardath.adaptiveerrorpatch.startupprofiler");

                PatchSimple(harmony, AccessTools.Method(typeof(ModContentPack), "LoadDefs"), nameof(LoadDefsPrefix), nameof(LoadDefsPostfix));
                PatchSimple(harmony, AccessTools.Method(typeof(DirectXmlToObjectNew), "DefFromNodeNew"), nameof(DefParsePrefix), nameof(DefParsePostfix));
                PatchSimple(harmony, AccessTools.Method(typeof(DirectXmlLoader), "DefFromNode"), nameof(DefParsePrefix), nameof(DefParsePostfix));

                PatchPatchOperations(harmony);
                PatchStaticConstructors(harmony);
                PatchRimDoctorIfPresent(harmony);

                Log.Message("[Z Adaptive] Per-mod startup profiler armed. RimDoctor integration is optional; standalone summary will be written to Player.log.");
            }
            catch (Exception ex)
            {
                Log.Warning("[Z Adaptive] Startup profiler failed to arm safely: " + ex);
            }
        }

        private static void PatchSimple(Harmony harmony, MethodInfo target, string prefixName, string postfixName)
        {
            if (target == null) return;
            MethodInfo prefix = AccessTools.Method(typeof(StartupProfiler), prefixName);
            MethodInfo postfix = AccessTools.Method(typeof(StartupProfiler), postfixName);
            harmony.Patch(target,
                prefix: prefix == null ? null : new HarmonyMethod(prefix),
                postfix: postfix == null ? null : new HarmonyMethod(postfix));
        }

        private static void RefreshMaps()
        {
            lock (Gate)
            {
                var mods = LoadedModManager.RunningModsListForReading;
                if (mods == null) return;

                foreach (ModContentPack pack in mods)
                {
                    if (pack == null) continue;
                    if (!ByPack.TryGetValue(pack, out Cost cost))
                    {
                        cost = new Cost
                        {
                            Name = pack.Name ?? "(unknown)",
                            PackageId = pack.PackageIdPlayerFacing ?? "(unknown)",
                            Pack = pack
                        };
                        ByPack.Add(pack, cost);
                    }

                    try
                    {
                        cost.DefCount = pack.AllDefs?.Count() ?? 0;
                    }
                    catch { }

                    try
                    {
                        var assemblies = pack.assemblies?.loadedAssemblies;
                        cost.AssemblyCount = assemblies?.Count ?? 0;
                        if (assemblies != null)
                        {
                            foreach (Assembly assembly in assemblies)
                                if (assembly != null) ByAssembly[assembly] = cost;
                        }
                    }
                    catch { }

                    try
                    {
                        if (pack.Patches != null)
                        {
                            foreach (PatchOperation op in pack.Patches)
                                if (op != null) PatchOwner[op] = cost;
                        }
                    }
                    catch { }
                }
            }
        }

        public static void LoadDefsPrefix(ModContentPack __instance, out TimerState __state)
        {
            __state = NewState(__instance);
        }

        public static void LoadDefsPostfix(TimerState __state)
        {
            if (__state?.Cost == null) return;
            AddTicks(__state.Cost, Stopwatch.GetTimestamp() - __state.Start, Bucket.LoadDefs);
        }

        public static void DefParsePrefix(object[] __args, out TimerState __state)
        {
            __state = null;
            if (__args == null) return;
            LoadableXmlAsset asset = __args.OfType<LoadableXmlAsset>().FirstOrDefault();
            ModContentPack pack = asset?.mod;
            if (pack == null) return;
            __state = NewState(pack);
        }

        public static void DefParsePostfix(TimerState __state)
        {
            if (__state?.Cost == null) return;
            lock (Gate) __state.Cost.ParsedDefs++;
            AddTicks(__state.Cost, Stopwatch.GetTimestamp() - __state.Start, Bucket.DefParse);
        }

        private static TimerState NewState(ModContentPack pack)
        {
            if (pack == null) return null;
            lock (Gate)
            {
                if (!ByPack.TryGetValue(pack, out Cost cost))
                {
                    RefreshMaps();
                    ByPack.TryGetValue(pack, out cost);
                }
                return cost == null ? null : new TimerState { Start = Stopwatch.GetTimestamp(), Cost = cost };
            }
        }

        private enum Bucket { LoadDefs, DefParse, Patch, StaticCtor }

        private static void AddTicks(Cost cost, long ticks, Bucket bucket)
        {
            if (cost == null || ticks < 0) return;
            lock (Gate)
            {
                switch (bucket)
                {
                    case Bucket.LoadDefs: cost.LoadDefsTicks += ticks; break;
                    case Bucket.DefParse: cost.DefParseTicks += ticks; break;
                    case Bucket.Patch: cost.PatchTicks += ticks; break;
                    case Bucket.StaticCtor: cost.StaticCtorTicks += ticks; break;
                }
            }
        }

        private static void PatchPatchOperations(Harmony harmony)
        {
            RefreshMaps();
            IEnumerable<Type> types;
            try { types = GenTypes.AllTypes.Where(t => t != null && !t.IsAbstract && typeof(PatchOperation).IsAssignableFrom(t)).ToList(); }
            catch { return; }

            foreach (Type type in types)
            {
                MethodInfo method = AccessTools.DeclaredMethod(type, "Apply", new[] { typeof(XmlDocument) });
                if (method == null || method.IsAbstract || !PatchedPatchApplyMethods.Add(method)) continue;
                try
                {
                    harmony.Patch(method,
                        prefix: new HarmonyMethod(AccessTools.Method(typeof(StartupProfiler), nameof(PatchApplyPrefix))),
                        postfix: new HarmonyMethod(AccessTools.Method(typeof(StartupProfiler), nameof(PatchApplyPostfix))));
                }
                catch { }
            }
        }

        public static void PatchApplyPrefix(PatchOperation __instance, out TimerState __state)
        {
            __state = null;
            if (__instance == null) return;
            lock (Gate)
            {
                if (!PatchOwner.TryGetValue(__instance, out Cost cost)) return;
                cost.PatchOps++;
                __state = new TimerState { Start = Stopwatch.GetTimestamp(), Cost = cost };
            }
        }

        public static void PatchApplyPostfix(TimerState __state)
        {
            if (__state?.Cost == null) return;
            AddTicks(__state.Cost, Stopwatch.GetTimestamp() - __state.Start, Bucket.Patch);
        }

        private static void PatchStaticConstructors(Harmony harmony)
        {
            MethodInfo callAll = AccessTools.Method(typeof(StaticConstructorOnStartupUtility), nameof(StaticConstructorOnStartupUtility.CallAll));
            if (callAll == null) return;
            harmony.Patch(callAll,
                transpiler: new HarmonyMethod(AccessTools.Method(typeof(StartupProfiler), nameof(CallAllTranspiler))),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(StartupProfiler), nameof(StaticConstructorsFinished))));
        }

        public static IEnumerable<CodeInstruction> CallAllTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo original = AccessTools.Method(typeof(RuntimeHelpers), nameof(RuntimeHelpers.RunClassConstructor), new[] { typeof(RuntimeTypeHandle) });
            MethodInfo replacement = AccessTools.Method(typeof(StartupProfiler), nameof(RunClassConstructorTimed));
            foreach (CodeInstruction instruction in instructions)
            {
                if (original != null && replacement != null && instruction.Calls(original))
                {
                    var copy = new CodeInstruction(OpCodes.Call, replacement);
                    copy.labels.AddRange(instruction.labels);
                    copy.blocks.AddRange(instruction.blocks);
                    yield return copy;
                }
                else yield return instruction;
            }
        }

        public static void RunClassConstructorTimed(RuntimeTypeHandle handle)
        {
            Type type = Type.GetTypeFromHandle(handle);
            Cost cost = null;
            if (type != null)
            {
                lock (Gate) ByAssembly.TryGetValue(type.Assembly, out cost);
            }

            long start = Stopwatch.GetTimestamp();
            try
            {
                RuntimeHelpers.RunClassConstructor(handle);
            }
            finally
            {
                if (cost != null)
                {
                    lock (Gate) cost.StaticCtors++;
                    AddTicks(cost, Stopwatch.GetTimestamp() - start, Bucket.StaticCtor);
                }
            }
        }

        public static void StaticConstructorsFinished()
        {
            startupFinished = true;
            RefreshMaps();
            try
            {
                string summary = BuildPlainSummary(15);
                Log.Message("[Z Adaptive] Per-mod startup cost summary (measured work after Z Adaptive profiler initialization):\n" + summary);
            }
            catch (Exception ex)
            {
                Log.Warning("[Z Adaptive] Could not write startup summary: " + ex.Message);
            }
        }

        private static void PatchRimDoctorIfPresent(Harmony harmony)
        {
            Type reportBuilder = AccessTools.TypeByName("RimDoctor.ReportBuilder");
            MethodInfo build = reportBuilder == null ? null : AccessTools.Method(reportBuilder, "Build", Type.EmptyTypes);
            if (build == null)
            {
                Log.Message("[Z Adaptive] RimDoctor not detected. Startup profiler remains active and will report to Player.log only.");
                return;
            }

            harmony.Patch(build, postfix: new HarmonyMethod(AccessTools.Method(typeof(StartupProfiler), nameof(RimDoctorBuildPostfix))));
            Log.Message("[Z Adaptive] RimDoctor detected; per-mod startup cost table will be appended to Diagnostics -> Save report output.");
        }

        public static void RimDoctorBuildPostfix(ref string __result)
        {
            try
            {
                __result = (__result ?? string.Empty) + "\n\n" + BuildMarkdownReport();
            }
            catch (Exception ex)
            {
                __result = (__result ?? string.Empty) + "\n\n## Z Adaptive startup profiler\n_(failed to build profiler section: " + ex.Message + ")_\n";
            }
        }

        private static string BuildMarkdownReport()
        {
            RefreshMaps();
            ScanInstalledSizesIfNeeded();
            List<Cost> rows;
            lock (Gate) rows = ByPack.Values.OrderByDescending(c => c.MeasuredMs).ToList();

            var sb = new StringBuilder();
            sb.AppendLine("## Z Adaptive per-mod startup cost");
            sb.AppendLine();
            sb.AppendLine("Measured independently by Z Adaptive; RimDoctor is not required for collection. The table is appended here only when RimDoctor is present.");
            sb.AppendLine();
            sb.AppendLine("**Important:** this is measured startup work that Z Adaptive can attribute after its Mod class is instantiated: XML/Def file loading, Def deserialization, top-level patch application, and `[StaticConstructorOnStartup]` execution. RimWorld content/assembly loading that occurred before Z Adaptive could install its profiler, plus shared unified-XML/inheritance work, is not falsely assigned to individual mods.");
            sb.AppendLine();
            sb.AppendLine("| # | Mod | Measured ms | XML load | Def parse | Patches | Static ctors | Defs | Assemblies | Installed MiB | Files |");
            sb.AppendLine("|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            int rank = 1;
            foreach (Cost c in rows)
            {
                string name = (c.Name ?? "(unknown)").Replace("|", "\\|");
                string size = c.InstalledBytes < 0 ? "?" : (c.InstalledBytes / 1048576d).ToString("0.0");
                string files = c.InstalledFiles < 0 ? "?" : c.InstalledFiles.ToString();
                sb.AppendLine($"| {rank++} | {name} | {c.MeasuredMs:0.0} | {c.LoadDefsMs:0.0} | {c.DefParseMs:0.0} | {c.PatchMs:0.0} | {c.StaticCtorMs:0.0} | {c.DefCount} | {c.AssemblyCount} | {size} | {files} |");
            }
            sb.AppendLine();

            Cost biggest = rows.Where(c => c.InstalledBytes >= 0).OrderByDescending(c => c.InstalledBytes).FirstOrDefault();
            Cost slowest = rows.FirstOrDefault();
            if (slowest != null) sb.AppendLine($"- Highest **measured attributable startup cost**: **{slowest.Name}** — {slowest.MeasuredMs:0.0} ms.");
            if (biggest != null) sb.AppendLine($"- Largest installed mod folder: **{biggest.Name}** — {biggest.InstalledBytes / 1048576d:0.0} MiB across {biggest.InstalledFiles} files.");
            sb.AppendLine($"- Z Adaptive profiler install-to-static-constructor-completion span: {(startupFinished ? TicksToMs(Stopwatch.GetTimestamp() - installTimestamp).ToString("0.0") : "still collecting")} ms.");
            return sb.ToString();
        }

        private static string BuildPlainSummary(int count)
        {
            List<Cost> rows;
            lock (Gate) rows = ByPack.Values.OrderByDescending(c => c.MeasuredMs).Take(count).ToList();
            var sb = new StringBuilder();
            int i = 1;
            foreach (Cost c in rows)
            {
                sb.AppendLine($"{i++,2}. {c.Name}: {c.MeasuredMs:0.0} ms (XML {c.LoadDefsMs:0.0}, defs {c.DefParseMs:0.0}, patches {c.PatchMs:0.0}, static {c.StaticCtorMs:0.0})");
            }
            return sb.ToString().TrimEnd();
        }

        private static void ScanInstalledSizesIfNeeded()
        {
            lock (Gate)
            {
                if (sizesScanned) return;
                sizesScanned = true;
            }

            List<Cost> rows;
            lock (Gate) rows = ByPack.Values.ToList();
            foreach (Cost c in rows)
            {
                try
                {
                    object rootDir = AccessTools.Property(c.Pack.GetType(), "RootDir")?.GetValue(c.Pack, null);
                    string path = null;
                    if (rootDir is DirectoryInfo di) path = di.FullName;
                    else path = AccessTools.Property(rootDir?.GetType(), "FullName")?.GetValue(rootDir, null) as string;
                    if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) continue;

                    long bytes = 0;
                    int files = 0;
                    foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            bytes += new FileInfo(file).Length;
                            files++;
                        }
                        catch { }
                    }
                    c.InstalledBytes = bytes;
                    c.InstalledFiles = files;
                }
                catch { }
            }
        }

        private static double TicksToMs(long ticks)
        {
            return ticks * 1000d / Stopwatch.Frequency;
        }
    }
}
