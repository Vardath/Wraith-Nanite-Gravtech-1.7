using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Keeps every WNG buildable in its native RimWorld functional Architect category,
    /// while adding non-destructive family views and Odyssey gravship duplication.
    ///
    /// This deliberately does not rewrite BuildableDef.designationCategory. A structure can
    /// therefore remain in Structure/Furniture/Power/etc. and also appear in the appropriate
    /// WNG family tab. Gravship-native WNG buildables are additionally copied into Odyssey's
    /// own gravship construction category.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class WNGArchitectRouting
    {
        private const string WraithCategory = "WNG_WraithArchitect";
        private const string AsuranCategory = "WNG_AsuranArchitect";
        private const string GoauldCategory = "WNG_GoauldArchitect";
        private const string OdysseyCategory = "Odyssey";

        private static readonly FieldInfo ResolvedDesignatorsField =
            typeof(DesignationCategoryDef).GetField(
                "resolvedDesignators",
                BindingFlags.Instance | BindingFlags.NonPublic);

        static WNGArchitectRouting()
        {
            LongEventHandler.ExecuteWhenFinished(Install);
        }

        private static void Install()
        {
            if (ResolvedDesignatorsField == null)
            {
                Log.Error("[WNG] Could not access DesignationCategoryDef.resolvedDesignators; family Architect duplication was not installed.");
                return;
            }

            DesignationCategoryDef wraith = DefDatabase<DesignationCategoryDef>.GetNamedSilentFail(WraithCategory);
            DesignationCategoryDef asuran = DefDatabase<DesignationCategoryDef>.GetNamedSilentFail(AsuranCategory);
            DesignationCategoryDef goauld = DefDatabase<DesignationCategoryDef>.GetNamedSilentFail(GoauldCategory);
            DesignationCategoryDef odyssey = ModsConfig.OdysseyActive
                ? DefDatabase<DesignationCategoryDef>.GetNamedSilentFail(OdysseyCategory)
                : null;

            if (wraith == null || asuran == null || goauld == null)
            {
                Log.Error("[WNG] One or more WNG family Architect categories are missing.");
                return;
            }

            List<BuildableDef> buildables = new List<BuildableDef>();
            buildables.AddRange(DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => d != null && d.defName != null && d.defName.StartsWith("WNG_", StringComparison.Ordinal) && d.designationCategory != null));
            buildables.AddRange(DefDatabase<TerrainDef>.AllDefsListForReading
                .Where(d => d != null && d.defName != null && d.defName.StartsWith("WNG_", StringComparison.Ordinal) && d.designationCategory != null));

            int nativeGodModeUpgrades = UpgradeNativeWNGDesignators(buildables);
            int familyCopies = 0;
            int odysseyCopies = 0;

            foreach (BuildableDef def in buildables)
            {
                DesignationCategoryDef family = FamilyCategoryFor(def, wraith, asuran, goauld);
                if (family != null && AddBuildDesignator(family, def))
                    familyCopies++;

                if (odyssey != null && IsWNGGravshipBuildable(def) && AddBuildDesignator(odyssey, def))
                    odysseyCopies++;
            }

            Log.Message($"[WNG] Architect routing installed: {nativeGodModeUpgrades} native WNG designators upgraded for explicit God Mode, {familyCopies} family entries and {odysseyCopies} Odyssey gravship entries; normal construction costs and blueprints remain unchanged.");
        }

        private static DesignationCategoryDef FamilyCategoryFor(
            BuildableDef def,
            DesignationCategoryDef wraith,
            DesignationCategoryDef asuran,
            DesignationCategoryDef goauld)
        {
            string name = def.defName ?? string.Empty;
            string artPath = ArtPathFor(def);

            if (ContainsAny(name, "Goauld", "Alkesh") ||
                ContainsAny(artPath, "/Goauld/", "Terrain/Goauld/"))
                return goauld;

            if (ContainsAny(name, "Wraith", "Hive", "Living", "Bioelectric") ||
                ContainsAny(artPath, "/Wraith/", "Terrain/Wraith/"))
                return wraith;

            if (ContainsAny(name, "Asuran", "Precursor", "HumanForm") ||
                ContainsAny(artPath, "/Precursor/", "Terrain/Precursor/"))
                return asuran;

            return null;
        }

        private static bool IsWNGGravshipBuildable(BuildableDef def)
        {
            string name = def.defName ?? string.Empty;

            if (name.Contains("Gravship", StringComparison.Ordinal) ||
                name.Contains("GravEngine", StringComparison.Ordinal) ||
                name.Contains("GravField", StringComparison.Ordinal))
                return true;

            TerrainAffordanceDef needed = def.terrainAffordanceNeeded;
            if (needed != null && needed.defName == "Substructure")
                return true;

            if (def is TerrainDef terrain && terrain.tags != null && terrain.tags.Contains("Substructure"))
                return true;

            return false;
        }

        private static int UpgradeNativeWNGDesignators(List<BuildableDef> buildables)
        {
            HashSet<BuildableDef> wanted = new HashSet<BuildableDef>(buildables);
            int upgraded = 0;

            foreach (DesignationCategoryDef category in DefDatabase<DesignationCategoryDef>.AllDefsListForReading)
            {
                List<Designator> resolved = GetResolvedDesignators(category);
                if (resolved == null)
                    continue;

                for (int i = 0; i < resolved.Count; i++)
                {
                    if (resolved[i] is Designator_Build build &&
                        wanted.Contains(build.PlacingDef) &&
                        !(resolved[i] is Designator_Build_WNGGodMode))
                    {
                        resolved[i] = new Designator_Build_WNGGodMode(build.PlacingDef);
                        upgraded++;
                    }
                }

                resolved.SortBy(d => d.Order);
            }

            return upgraded;
        }

        private static bool AddBuildDesignator(DesignationCategoryDef category, BuildableDef def)
        {
            List<Designator> resolved = GetResolvedDesignators(category);
            if (resolved == null)
                return false;

            if (resolved.Any(d => d is Designator_Build build && build.PlacingDef == def))
                return false;

            resolved.Add(new Designator_Build_WNGGodMode(def));
            resolved.SortBy(d => d.Order);
            return true;
        }

        private static List<Designator> GetResolvedDesignators(DesignationCategoryDef category)
        {
            List<Designator> resolved = ResolvedDesignatorsField.GetValue(category) as List<Designator>;
            if (resolved != null)
                return resolved;

            // The normal Def resolver should have populated this before ExecuteWhenFinished.
            // ResolveReferences is a safe fallback for unusual load-order interactions.
            category.ResolveReferences();
            return ResolvedDesignatorsField.GetValue(category) as List<Designator>;
        }

        private static string ArtPathFor(BuildableDef def)
        {
            if (def is ThingDef thing)
                return thing.graphicData?.texPath ?? thing.uiIconPath ?? string.Empty;
            if (def is TerrainDef terrain)
                return terrain.texturePath ?? string.Empty;
            return string.Empty;
        }

        private static bool ContainsAny(string value, params string[] tokens)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            foreach (string token in tokens)
            {
                if (value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// WNG Architect entries retain ordinary RimWorld construction semantics unless the player
    /// explicitly enables God Mode. In normal play this class delegates entirely to Designator_Build:
    /// real costs, blueprints, construction work, research and placement rules remain intact.
    ///
    /// When God Mode itself is enabled, WNG entries become available for direct test placement and
    /// use RimWorld's God Mode construction path. Merely enabling Dev Mode never activates this path.
    /// </summary>
    public sealed class Designator_Build_WNGGodMode : Designator_Build
    {
        public Designator_Build_WNGGodMode(BuildableDef entDef)
            : base(entDef)
        {
        }

        public override bool Visible => DebugSettings.godMode || base.Visible;

        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            if (!DebugSettings.godMode)
                return base.CanDesignateCell(c);

            if (Map == null || !c.InBounds(Map))
                return new AcceptanceReport("OutOfBounds".Translate());

            return AcceptanceReport.WasAccepted;
        }

        public override void DesignateSingleCell(IntVec3 c)
        {
            // Designator_Build already performs finished, no-cost placement while God Mode is
            // active. The override exists only so WNG-specific placement workers cannot prevent
            // deliberate developer test placement. We never toggle God Mode ourselves.
            base.DesignateSingleCell(c);
        }
    }

}
