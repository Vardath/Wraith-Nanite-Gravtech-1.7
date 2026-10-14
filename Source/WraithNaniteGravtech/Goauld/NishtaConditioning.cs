using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Nish'ta is a biological conditioning organism, not a generic chemical high. The projectile
    /// deliberately reuses vanilla blind-smoke propagation for the visible gas cloud, then applies
    /// the WNG conditioning Hediff only to living humanlike pawns actually standing in that cloud.
    /// </summary>
    public sealed class Projectile_NishtaCanister : Projectile_Explosive
    {
        protected override void Explode()
        {
            Map map = Map;
            IntVec3 center = Position;
            Faction controller = Launcher?.Faction;
            float radius = def?.projectile?.explosionRadius ?? 0f;

            base.Explode();

            if (map == null || radius <= 0f)
                return;

            float radiusSq = radius * radius;
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || pawn.Position.DistanceToSquared(center) > radiusSq)
                    continue;
                if (map.gasGrid.DensityAt(pawn.Position, GasType.BlindSmoke) <= 0)
                    continue;

                NishtaUtility.TryCondition(pawn, controller);
            }
        }
    }

    /// <summary>
    /// Combat-Extended bridge for Nish'ta. Vanilla keeps the original WNG projectile subclass;
    /// CE replaces only the projectile class and adds this comp, so the active projectile framework
    /// owns flight while WNG still owns biological conditioning.
    /// </summary>
    public sealed class CompProperties_NishtaExposure : CompProperties
    {
        public CompProperties_NishtaExposure()
        {
            compClass = typeof(CompNishtaExposure);
        }
    }

    public sealed class CompNishtaExposure : ThingComp
    {
        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            IntVec3 center = parent?.PositionHeld ?? IntVec3.Invalid;
            float radius = parent?.def?.projectile?.explosionRadius ?? 0f;
            Faction controller = ResolveOptionalProjectileLauncherFaction(parent);
            bool landed = ResolveOptionalProjectileLanded(parent);

            base.PostDestroy(mode, previousMap);

            // Current CE creates the explosion (including postExplosionGasType) before its final
            // Destroy() call. Require CE's own landed flag plus actual BlindSmoke at the impact
            // area so interception/out-of-bounds cleanup cannot become a Nish'ta exposure.
            if (!landed || previousMap == null || !center.IsValid || radius <= 0f ||
                SumBlindSmoke(previousMap, center, radius) <= 0)
                return;

            previousMap.GetComponent<MapComponent_NishtaExposureQueue>()?.Enqueue(center, radius, controller);
        }

        private static Faction ResolveOptionalProjectileLauncherFaction(Thing projectile)
        {
            if (projectile == null)
                return null;

            // Combat Extended is optional and its ProjectileCE hierarchy does not inherit Verse.Projectile.
            // Read its public launcher field only by reflection so WNG retains no CE assembly dependency.
            try
            {
                System.Type type = projectile.GetType();
                while (type != null)
                {
                    System.Reflection.FieldInfo field = type.GetField(
                        "launcher",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    Thing launcher = field?.GetValue(projectile) as Thing;
                    if (launcher != null)
                        return launcher.Faction;
                    type = type.BaseType;
                }
            }
            catch (System.Exception ex)
            {
                Log.WarningOnce("[WNG] Nish'ta could not resolve an optional projectile launcher's faction: " + ex.Message, 731984221);
            }

            return null;
        }

        private static bool ResolveOptionalProjectileLanded(Thing projectile)
        {
            if (projectile == null)
                return false;
            try
            {
                System.Type type = projectile.GetType();
                while (type != null)
                {
                    System.Reflection.FieldInfo field = type.GetField(
                        "landed",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    if (field != null && field.FieldType == typeof(bool))
                        return (bool)field.GetValue(projectile);
                    type = type.BaseType;
                }
            }
            catch (System.Exception ex)
            {
                Log.WarningOnce("[WNG] Nish'ta could not resolve optional projectile impact state: " + ex.Message, 731984222);
            }
            return false;
        }

        internal static int SumBlindSmoke(Map map, IntVec3 center, float radius)
        {
            if (map == null || !center.IsValid || radius <= 0f)
                return 0;

            int total = 0;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, radius, true))
            {
                if (cell.InBounds(map))
                    total += map.gasGrid.DensityAt(cell, GasType.BlindSmoke);
            }
            return total;
        }
    }

    public sealed class NishtaExposureRecord : IExposable
    {
        public IntVec3 center;
        public float radius;
        public Faction controller;
        public int resolveTick;

        public NishtaExposureRecord() { }

        public NishtaExposureRecord(IntVec3 center, float radius, Faction controller, int resolveTick)
        {
            this.center = center;
            this.radius = radius;
            this.controller = controller;
            this.resolveTick = resolveTick;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref center, "center");
            Scribe_Values.Look(ref radius, "radius", 0f);
            Scribe_References.Look(ref controller, "controller");
            Scribe_Values.Look(ref resolveTick, "resolveTick", 0);
        }
    }

    public sealed class MapComponent_NishtaExposureQueue : MapComponent
    {
        private List<NishtaExposureRecord> pending = new List<NishtaExposureRecord>();

        public MapComponent_NishtaExposureQueue(Map map) : base(map) { }

        public void Enqueue(IntVec3 center, float radius, Faction controller)
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            pending.Add(new NishtaExposureRecord(center, radius, controller, now + 1));
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (pending == null || pending.Count == 0)
                return;

            int now = Find.TickManager?.TicksGame ?? 0;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                NishtaExposureRecord record = pending[i];
                if (record == null || record.resolveTick > now)
                    continue;

                pending.RemoveAt(i);
                Resolve(record);
            }
        }

        private void Resolve(NishtaExposureRecord record)
        {
            if (record == null || record.radius <= 0f || !record.center.IsValid || map == null)
                return;

            // PostDestroy queued only after CE reported a landed impact and actual Nish'ta
            // BlindSmoke existed at that impact area. Recheck the physical aerosol one tick later
            // before conditioning the exact Pawns standing in it.
            if (CompNishtaExposure.SumBlindSmoke(map, record.center, record.radius) <= 0)
                return;

            float radiusSq = record.radius * record.radius;
            List<Pawn> pawns = map.mapPawns.AllPawnsSpawned.ToList();
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || pawn.Position.DistanceToSquared(record.center) > radiusSq)
                    continue;
                if (map.gasGrid.DensityAt(pawn.Position, GasType.BlindSmoke) <= 0)
                    continue;

                NishtaUtility.TryCondition(pawn, record.controller);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref pending, "wngNishtaPendingExposures", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && pending == null)
                pending = new List<NishtaExposureRecord>();
        }
    }

    public static class NishtaUtility
    {
        private const string ConditioningDefName = "WNG_NishtaConditioning";
        private const string ResistanceDefName = "WNG_NishtaResistance";

        public static HediffDef ConditioningDef => DefDatabase<HediffDef>.GetNamedSilentFail(ConditioningDefName);
        public static HediffDef ResistanceDef => DefDatabase<HediffDef>.GetNamedSilentFail(ResistanceDefName);

        public static Hediff_NishtaConditioning Conditioning(Pawn pawn)
        {
            HediffDef def = ConditioningDef;
            return def == null ? null : pawn?.health?.hediffSet?.GetFirstHediffOfDef(def) as Hediff_NishtaConditioning;
        }

        public static bool HasResistance(Pawn pawn)
        {
            HediffDef def = ResistanceDef;
            return def != null && pawn?.health?.hediffSet?.GetFirstHediffOfDef(def) != null;
        }

        public static bool EligibleTarget(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.health?.hediffSet == null || pawn.RaceProps == null)
                return false;
            if (!pawn.RaceProps.Humanlike || !pawn.RaceProps.IsFlesh || pawn.RaceProps.IsMechanoid)
                return false;
            if (pawn.IsPrisoner || pawn.IsSlave || pawn.IsQuestLodger())
                return false;
            if (HasResistance(pawn) || Conditioning(pawn) != null)
                return false;

            // Nish'ta is a human/Jaffa neural-conditioning organism in Stargate lore. Do not make
            // it a universal mind-control solvent for Wraith or nanite-maintained human forms.
            string xenotype = pawn.genes?.Xenotype?.defName;
            if (xenotype == "WNG_Wraith" || xenotype == "WNG_NanitePrecursor" || xenotype == "WNG_HumanFormReplicator")
                return false;

            return true;
        }

        public static bool TryCondition(Pawn pawn, Faction controller)
        {
            HediffDef def = ConditioningDef;
            if (def == null || !EligibleTarget(pawn))
                return false;

            Hediff_NishtaConditioning hediff = HediffMaker.MakeHediff(def, pawn) as Hediff_NishtaConditioning;
            if (hediff == null)
                return false;

            hediff.Severity = 1f;
            pawn.health.AddHediff(hediff);
            hediff.InitializeControl(controller);
            return pawn.health.hediffSet.GetFirstHediffOfDef(def) == hediff;
        }

        public static bool RequestCure(Pawn pawn)
        {
            Hediff_NishtaConditioning hediff = Conditioning(pawn);
            if (hediff == null)
                return false;
            hediff.RequestCure();
            return true;
        }

        public static void GrantResistance(Pawn pawn)
        {
            HediffDef def = ResistanceDef;
            if (pawn == null || pawn.Dead || pawn.health?.hediffSet == null || def == null || HasResistance(pawn))
                return;

            Hediff resistance = HediffMaker.MakeHediff(def, pawn);
            resistance.Severity = 1f;
            pawn.health.AddHediff(resistance);
        }
    }

    /// <summary>
    /// Stores the exact pre-conditioning faction on the victim. The pawn itself remains the same
    /// pawn; Nish'ta does not create a proxy. Electrical cure marks the Hediff for normal health
    /// tracker removal so we do not mutate the Hediff list from inside the damage-notification loop.
    /// </summary>
    public sealed class Hediff_NishtaConditioning : HediffWithComps
    {
        private Faction priorFaction;
        private Faction controlFaction;
        private bool initialized;
        private bool cured;
        private bool priorFactionRestored;

        public Faction PriorFaction => priorFaction;
        public Faction ControlFaction => controlFaction;
        public bool Cured => cured;

        public void InitializeControl(Faction controller)
        {
            if (initialized || pawn == null)
                return;

            priorFaction = pawn.Faction;
            controlFaction = controller;
            initialized = true;

            if (controlFaction != null && pawn.Faction != controlFaction)
                pawn.SetFaction(controlFaction);
        }

        public void RequestCure()
        {
            cured = true;
            Severity = 0f;
        }

        public override void Notify_PawnPostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            base.Notify_PawnPostApplyDamage(dinfo, totalDamageDealt);
            if (pawn == null || pawn.Dead || dinfo.Def != DamageDefOf.EMP)
                return;

            // Electrical shock is the canonical Nish'ta deprogramming mechanism. Setting severity
            // to zero lets the health tracker remove us safely after its current notification pass.
            RequestCure();
        }

        public override void Notify_PawnDied(DamageInfo? dinfo, Hediff culprit = null)
        {
            RestorePriorFaction();
            base.Notify_PawnDied(dinfo, culprit);
        }

        public override void PostRemoved()
        {
            RestorePriorFaction();
            if (cured && pawn != null && !pawn.Dead)
                NishtaUtility.GrantResistance(pawn);
            base.PostRemoved();
        }

        private void RestorePriorFaction()
        {
            if (priorFactionRestored || !initialized || pawn == null)
                return;

            priorFactionRestored = true;

            // Do not clobber an unrelated faction transition that happened after conditioning.
            // If Nish'ta still owns the current faction, restore the exact saved pre-exposure state.
            if (pawn.Faction == controlFaction)
                pawn.SetFaction(priorFaction);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref priorFaction, "priorFaction");
            Scribe_References.Look(ref controlFaction, "controlFaction");
            Scribe_Values.Look(ref initialized, "initialized", false);
            Scribe_Values.Look(ref cured, "cured", false);
            Scribe_Values.Look(ref priorFactionRestored, "priorFactionRestored", false);
        }
    }

    public sealed class Recipe_NishtaDeprogrammingShock : Recipe_Surgery
    {
        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            Pawn pawn = thing as Pawn;
            if (pawn == null)
                return false;

            return NishtaUtility.Conditioning(pawn) != null && base.AvailableOnNow(thing, part);
        }

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            if (pawn == null || NishtaUtility.Conditioning(pawn) == null)
                return;

            if (billDoer != null && CheckSurgeryFail(billDoer, pawn, ingredients, part, bill))
                return;

            if (billDoer != null)
                TaleRecorder.RecordTale(TaleDefOf.DidSurgery, billDoer, pawn);

            if (NishtaUtility.RequestCure(pawn) && pawn.Faction == Faction.OfPlayer)
            {
                Messages.Message(
                    pawn.LabelShortCap + " has been electrically deprogrammed from Nish'ta conditioning. The killed organism leaves resistance to reinfection.",
                    pawn,
                    MessageTypeDefOf.PositiveEvent,
                    historical: false);
            }
        }
    }
}
