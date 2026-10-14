using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace WraithNaniteGravtech
{
    public static class WraithLivingTechnologyUtility
    {
        public const string BiologicalCultivationResearchDefName = "WNG_BiologicalCultivation";
        public const string LivingForgeDefName = "WNG_LivingForge";
        public const string GravEngineDefName = "WNG_WraithGravEngine";
        public const int IncubationDurationTicks = 60000;

        public static int LivingForgeIncubationTicks => WNGSettingsUtility.LivingForgeIncubationTicks;
        private static readonly string[] KnownNaniteXenotypes =
        {
            "WNG_NanitePrecursor",
            "WNG_HumanFormReplicator"
        };

        public static bool BiologicalCultivationAvailable(Pawn caster)
        {
            // The research gate is player progression. Non-player Wraith are not made dependent on
            // the player's research manager merely because they share the same biological gene.
            if (caster == null || caster.Faction != Faction.OfPlayer)
                return true;

            ResearchProjectDef research = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(BiologicalCultivationResearchDefName);
            return research != null && research.IsFinished;
        }

        public static bool IsBiologicalHumanlike(Pawn pawn)
        {
            if (pawn?.RaceProps == null || !pawn.RaceProps.Humanlike || !pawn.RaceProps.IsFlesh || pawn.RaceProps.IsMechanoid)
                return false;

            string xenotype = pawn.genes?.Xenotype?.defName;
            if (!xenotype.NullOrEmpty())
            {
                for (int i = 0; i < KnownNaniteXenotypes.Length; i++)
                {
                    if (xenotype == KnownNaniteXenotypes[i])
                        return false;
                }
            }
            return true;
        }

        public static bool IsValidLivingHost(Pawn caster, Pawn host, out string reason)
        {
            if (caster == null || host == null || host.Destroyed || host.Dead)
            {
                reason = "Select a living biological humanlike host.";
                return false;
            }
            if (host == caster)
            {
                reason = "A Wraith cannot cultivate a living forge inside itself.";
                return false;
            }
            if (caster.Map == null || host.Map != caster.Map)
            {
                reason = "The host must be on the same map as the Wraith.";
                return false;
            }
            if (!IsBiologicalHumanlike(host))
            {
                reason = "Living Forge cultivation requires a biological humanlike host; mechanoids and nanite synthetics cannot support the tissue.";
                return false;
            }
            if (host.health == null)
            {
                reason = "The selected host cannot support incubation.";
                return false;
            }

            HediffDef forgeIncubationDef = DefDatabase<HediffDef>.GetNamedSilentFail("WNG_HostSeedIncubation");
            HediffDef engineIncubationDef = DefDatabase<HediffDef>.GetNamedSilentFail("WNG_GravEngineSeedIncubation");
            if ((forgeIncubationDef != null && host.health.hediffSet.HasHediff(forgeIncubationDef)) ||
                (engineIncubationDef != null && host.health.hediffSet.HasHediff(engineIncubationDef)))
            {
                reason = "This host is already incubating Wraith living technology.";
                return false;
            }

            // Latest accepted design explicitly permits colonists, prisoners and slaves. Historical
            // hostile-host behavior is retained only for enemies already downed or stunned.
            bool playerColonist = host.Faction == Faction.OfPlayer && !host.IsPrisoner && !host.IsSlave;
            bool prisonerOrSlave = host.IsPrisoner || host.IsSlave;
            bool hostileIncapacitated = host.HostileTo(caster) &&
                (host.Downed || host.stances?.stunner?.Stunned == true);
            if (!playerColonist && !prisonerOrSlave && !hostileIncapacitated)
            {
                reason = "Use a colonist, prisoner, slave, or an incapacitated hostile biological humanlike.";
                return false;
            }

            reason = null;
            return true;
        }

        public static bool IsValidCorpseHost(Pawn caster, Corpse corpse, out string reason)
        {
            if (caster == null || corpse == null || corpse.Destroyed || !corpse.Spawned || corpse.Map == null || corpse.Map != caster.Map)
            {
                reason = "Select a biological humanlike corpse on the Wraith's map.";
                return false;
            }
            if (!IsBiologicalHumanlike(corpse.InnerPawn))
            {
                reason = "Living Forge cultivation requires a biological humanlike corpse; mechanoids and nanite synthetics cannot support the tissue.";
                return false;
            }
            reason = null;
            return true;
        }

        internal static bool TryPrepareMinifiedLivingForge(Faction outputFaction, out MinifiedThing minified, out string reason)
        {
            return TryPrepareMinifiedOutput(LivingForgeDefName, "Living Forge", outputFaction, out minified, out reason);
        }

        internal static bool TryPrepareMinifiedGravEngine(Faction outputFaction, out MinifiedThing minified, out string reason)
        {
            return TryPrepareMinifiedOutput(GravEngineDefName, "Wraith grav engine", outputFaction, out minified, out reason);
        }

        private static bool TryPrepareMinifiedOutput(string targetDefName, string displayName, Faction outputFaction, out MinifiedThing minified, out string reason)
        {
            minified = null;
            reason = null;
            ThingDef targetDef = DefDatabase<ThingDef>.GetNamedSilentFail(targetDefName);
            if (targetDef == null)
            {
                reason = "the " + displayName + " definition is missing";
                return false;
            }

            Thing output;
            try
            {
                output = ThingMaker.MakeThing(targetDef);
            }
            catch (Exception ex)
            {
                reason = "the " + displayName + " could not be constructed: " + ex.Message;
                return false;
            }
            if (output == null)
            {
                reason = "the " + displayName + " could not be constructed";
                return false;
            }

            try
            {
                if (outputFaction != null)
                    output.SetFactionDirect(outputFaction);

                ThingDef minifiedDef = targetDef.minifiedDef;
                if (minifiedDef == null)
                {
                    output.Destroy(DestroyMode.Vanish);
                    reason = "the " + displayName + " has no minified form";
                    return false;
                }

                MinifiedThing wrapper = ThingMaker.MakeThing(minifiedDef) as MinifiedThing;
                if (wrapper == null)
                {
                    output.Destroy(DestroyMode.Vanish);
                    reason = "the " + displayName + " minified definition did not create a MinifiedThing";
                    return false;
                }
                wrapper.InnerThing = output;
                minified = wrapper;
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    if (!output.Destroyed && !output.Spawned)
                        output.Destroy(DestroyMode.Vanish);
                }
                catch { }
                reason = "the " + displayName + " minified form could not be prepared: " + ex.Message;
                return false;
            }
        }

        internal static bool TryPlacePrepared(MinifiedThing minified, IntVec3 position, Map map, out string reason)
        {
            reason = null;
            if (minified == null || minified.Destroyed || map == null)
            {
                reason = "the prepared living-technology output or map is unavailable";
                return false;
            }
            try
            {
                if (!GenPlace.TryPlaceThing(minified, position, map, ThingPlaceMode.Near))
                {
                    reason = "no nearby cell can receive the matured living-technology output";
                    return false;
                }

                // Do not let a host-consumption transaction commit unless the prepared minified
                // building is demonstrably present on the expected map. This protects both the
                // Living Forge and seeded Wraith grav engine from a false-positive placement result.
                if (!minified.Spawned || minified.Map != map)
                {
                    reason = "the matured living-technology output did not remain spawned on the expected map";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                reason = "living-technology placement failed: " + ex.Message;
                return false;
            }
        }

        internal static void DestroyUnplaced(Thing thing)
        {
            if (thing == null || thing.Destroyed || thing.Spawned)
                return;
            try { thing.Destroy(DestroyMode.Vanish); }
            catch { }
        }

        internal static void BestEffortMessage(string text, Thing target, MessageTypeDef messageType)
        {
            try { Messages.Message(text, target, messageType, historical: false); }
            catch (Exception ex)
            {
                try { Log.Warning("[WNG] Living-technology transaction committed but presentation failed: " + ex.Message); }
                catch { }
            }
        }
    }

    public sealed class CompProperties_AbilityHostSeedGestation : CompProperties_AbilityEffect
    {
        public HediffDef incubationHediff;
        public string corpseIncubationDefName = "WNG_CorpseLivingForgeIncubation";

        public CompProperties_AbilityHostSeedGestation()
        {
            compClass = typeof(CompAbilityEffect_HostSeedGestation);
        }
    }

    public sealed class CompAbilityEffect_HostSeedGestation : CompAbilityEffect
    {
        public new CompProperties_AbilityHostSeedGestation Props => (CompProperties_AbilityHostSeedGestation)props;

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            Pawn caster = parent?.pawn;
            if (caster == null)
                return false;

            if (!WraithLivingTechnologyUtility.BiologicalCultivationAvailable(caster))
            {
                if (throwMessages)
                    Messages.Message("Biological Cultivation research is required before a player Wraith can grow a Living Forge.", caster, MessageTypeDefOf.RejectInput, false);
                return false;
            }

            Corpse corpse = target.Thing as Corpse;
            if (corpse != null)
            {
                if (!WraithLivingTechnologyUtility.IsValidCorpseHost(caster, corpse, out string corpseReason))
                {
                    if (throwMessages)
                        Messages.Message(corpseReason, corpse, MessageTypeDefOf.RejectInput, false);
                    return false;
                }
                return base.Valid(target, throwMessages);
            }

            if (!WraithLivingTechnologyUtility.IsValidLivingHost(caster, target.Pawn, out string reason))
            {
                if (throwMessages)
                    Messages.Message(reason, target.Pawn ?? caster, MessageTypeDefOf.RejectInput, false);
                return false;
            }
            if (Props.incubationHediff == null)
            {
                if (throwMessages)
                    Messages.Message("The Living Forge incubation definition is unavailable.", caster, MessageTypeDefOf.RejectInput, false);
                return false;
            }
            return base.Valid(target, throwMessages);
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            Pawn caster = parent?.pawn;
            if (caster == null || !Valid(target, false))
                return;

            Corpse corpse = target.Thing as Corpse;
            if (corpse != null)
            {
                if (!CorpseLivingForgeIncubationUtility.TryBegin(corpse, caster.Faction, Props.corpseIncubationDefName, out Thing incubator, out string reason))
                {
                    Messages.Message(reason, corpse, MessageTypeDefOf.RejectInput, false);
                    return;
                }
                base.Apply(target, dest);
                WraithLivingTechnologyUtility.BestEffortMessage(
                    "The corpse has been consumed into an incubating Wraith tissue mass. A Living Forge will emerge after one day.",
                    incubator,
                    MessageTypeDefOf.NeutralEvent);
                return;
            }

            Pawn host = target.Pawn;
            Hediff incubation = HediffMaker.MakeHediff(Props.incubationHediff, host);
            Hediff_HostSeedIncubation typed = incubation as Hediff_HostSeedIncubation;
            if (typed == null)
                return;
            typed.SetOutputFaction(caster.Faction);
            host.health.AddHediff(typed);
            base.Apply(target, dest);
            WraithLivingTechnologyUtility.BestEffortMessage(
                host.LabelShort + " is incubating a Living Forge. The host will die after one day if it survives the gestation; the corpse will remain and the incubation will be removed.",
                host,
                MessageTypeDefOf.NeutralEvent);
        }
    }

    public sealed class Hediff_HostSeedIncubation : HediffWithComps
    {
        private const int CheckIntervalTicks = 250;
        private int startTick = -1;
        private int nextCheckTick = -1;
        private bool outputCommitted;
        private Faction outputFaction;

        public void SetOutputFaction(Faction faction)
        {
            outputFaction = faction;
        }

        private float Progress
        {
            get
            {
                if (outputCommitted)
                    return 1f;
                int now = Find.TickManager?.TicksGame ?? 0;
                if (startTick < 0)
                    return 0f;
                return Mathf.Clamp01((now - startTick) / (float)WraithLivingTechnologyUtility.LivingForgeIncubationTicks);
            }
        }

        public override string LabelInBrackets => (Progress * 100f).ToString("0") + "%";
        public override string TipStringExtra => "Living Forge incubation: " + (Progress * 100f).ToString("0") + "%";

        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            int now = Find.TickManager?.TicksGame ?? 0;
            if (startTick < 0)
                startTick = now;
            if (nextCheckTick < 0)
                nextCheckTick = now + CheckIntervalTicks;
        }

        public override void Tick()
        {
            base.Tick();
            if (pawn == null || pawn.Destroyed)
                return;

            if (outputCommitted)
            {
                TryFinalizeCommittedHost();
                return;
            }

            // Historical contract: a living host must survive the gestation. Death before maturity
            // loses this implant; corpse cultivation is a separate fresh transaction.
            if (pawn.Dead || pawn.Map == null || !pawn.Spawned)
                return;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (startTick < 0)
                startTick = now;
            if (nextCheckTick < 0)
                nextCheckTick = now + CheckIntervalTicks;
            if (now < nextCheckTick)
                return;
            nextCheckTick = now + CheckIntervalTicks;

            if (now - startTick >= WraithLivingTechnologyUtility.LivingForgeIncubationTicks)
                TryComplete();
        }

        private void TryComplete()
        {
            if (outputCommitted || pawn == null || pawn.Destroyed || pawn.Dead || pawn.Map == null || !pawn.Spawned)
                return;

            if (!WraithLivingTechnologyUtility.TryPrepareMinifiedLivingForge(outputFaction, out MinifiedThing minified, out string reason))
            {
                Log.ErrorOnce("[WNG] Matured Living Forge host incubation could not prepare output; host remains authoritative and completion will retry: " + reason, pawn.thingIDNumber ^ 0x4A771);
                return;
            }

            if (!WraithLivingTechnologyUtility.TryPlacePrepared(minified, pawn.Position, pawn.Map, out reason))
            {
                WraithLivingTechnologyUtility.DestroyUnplaced(minified);
                Log.ErrorOnce("[WNG] Matured Living Forge host incubation could not place output; host remains authoritative and completion will retry: " + reason, pawn.thingIDNumber ^ 0x4A772);
                return;
            }

            // Commit the minified Forge first so a death/corpse cleanup exception can never erase the
            // matured output. Host finalization now uses RimWorld's normal death pipeline rather than
            // Destroy(Vanish), leaving a corpse behind.
            outputCommitted = true;
            TryFinalizeCommittedHost();
            WraithLivingTechnologyUtility.BestEffortMessage(
                "The one-day gestation is complete. The host has died and a minified Living Forge has emerged beside the corpse.",
                minified,
                MessageTypeDefOf.PositiveEvent);
        }

        private void TryFinalizeCommittedHost()
        {
            if (pawn == null || pawn.Destroyed)
                return;

            try
            {
                if (!pawn.Dead)
                    pawn.Kill(null);

                if (!pawn.Dead)
                {
                    Log.Warning("[WNG] Living Forge output committed but the host resisted forced death; finalization will retry.");
                    return;
                }

                // A dead Pawn remains the InnerPawn of its Corpse. Remove only the gestation marker
                // so the corpse is ordinary biological remains and cannot present stale incubation.
                Pawn corpsePawn = pawn.Corpse?.InnerPawn ?? pawn;
                Hediff incubation = corpsePawn.health?.hediffSet?.GetFirstHediffOfDef(def);
                if (incubation != null)
                    corpsePawn.health.RemoveHediff(incubation);
            }
            catch (Exception ex)
            {
                try { Log.Warning("[WNG] Living Forge output committed but host death/corpse cleanup failed; output will not be duplicated and finalization will retry: " + ex.Message); }
                catch { }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref startTick, "wngLivingForgeHostStartTick", -1);
            Scribe_Values.Look(ref nextCheckTick, "wngLivingForgeHostNextCheckTick", -1);
            Scribe_Values.Look(ref outputCommitted, "wngLivingForgeHostOutputCommitted", false);
            Scribe_References.Look(ref outputFaction, "wngLivingForgeHostOutputFaction");
        }
    }

    public sealed class CompProperties_CorpseLivingForgeIncubation : CompProperties
    {
        public int incubationTicks = WraithLivingTechnologyUtility.IncubationDurationTicks;

        public CompProperties_CorpseLivingForgeIncubation()
        {
            compClass = typeof(CompCorpseLivingForgeIncubation);
        }
    }

    public sealed class CompCorpseLivingForgeIncubation : ThingComp
    {
        private int ageTicks;
        private bool sourceConsumptionCommitted;
        private bool outputCommitted;
        private Faction outputFaction;

        public CompProperties_CorpseLivingForgeIncubation Props => (CompProperties_CorpseLivingForgeIncubation)props;

        internal void Prepare(Faction faction)
        {
            ageTicks = 0;
            sourceConsumptionCommitted = false;
            outputCommitted = false;
            outputFaction = faction;
        }

        internal void CommitSourceConsumption()
        {
            sourceConsumptionCommitted = true;
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (parent == null || parent.Destroyed || !parent.Spawned || parent.Map == null)
                return;
            if (!sourceConsumptionCommitted)
                return;
            if (outputCommitted)
            {
                TryCleanupCommittedParent();
                return;
            }

            ageTicks = ageTicks > int.MaxValue - 250 ? int.MaxValue : Math.Max(0, ageTicks) + 250;
            if (ageTicks >= Math.Max(250, WraithLivingTechnologyUtility.LivingForgeIncubationTicks))
                TryComplete();
        }

        private void TryComplete()
        {
            if (!sourceConsumptionCommitted || outputCommitted || parent == null || parent.Destroyed || !parent.Spawned || parent.Map == null)
                return;

            if (!WraithLivingTechnologyUtility.TryPrepareMinifiedLivingForge(outputFaction, out MinifiedThing minified, out string reason))
            {
                Log.ErrorOnce("[WNG] Matured corpse incubation could not prepare Living Forge output; incubation remains and will retry: " + reason, parent.thingIDNumber ^ 0x5A771);
                return;
            }
            if (!WraithLivingTechnologyUtility.TryPlacePrepared(minified, parent.Position, parent.Map, out reason))
            {
                WraithLivingTechnologyUtility.DestroyUnplaced(minified);
                Log.ErrorOnce("[WNG] Matured corpse incubation could not place Living Forge output; incubation remains and will retry: " + reason, parent.thingIDNumber ^ 0x5A772);
                return;
            }

            outputCommitted = true;
            TryCleanupCommittedParent();
            WraithLivingTechnologyUtility.BestEffortMessage(
                "The one-day corpse incubation is complete. A minified Living Forge has emerged.",
                minified,
                MessageTypeDefOf.PositiveEvent);
        }

        private void TryCleanupCommittedParent()
        {
            if (parent == null || parent.Destroyed)
                return;
            try { parent.Destroy(DestroyMode.Vanish); }
            catch (Exception ex)
            {
                try { Log.Warning("[WNG] Living Forge output committed but residual corpse-incubation mass cleanup failed; output will not be duplicated: " + ex.Message); }
                catch { }
            }
        }

        public override string CompInspectStringExtra()
        {
            if (!sourceConsumptionCommitted)
                return "Incubation: inert (source transaction incomplete)";
            if (outputCommitted)
                return "Incubation: complete (residual tissue cleanup pending)";
            float progress = Mathf.Clamp01(Math.Max(0, ageTicks) / (float)Math.Max(1, WraithLivingTechnologyUtility.LivingForgeIncubationTicks));
            return "Living Forge incubation: " + progress.ToStringPercent();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref ageTicks, "wngLivingForgeCorpseAgeTicks", 0);
            Scribe_Values.Look(ref sourceConsumptionCommitted, "wngLivingForgeCorpseSourceCommitted", false);
            Scribe_Values.Look(ref outputCommitted, "wngLivingForgeCorpseOutputCommitted", false);
            Scribe_References.Look(ref outputFaction, "wngLivingForgeCorpseOutputFaction");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                ageTicks = Math.Max(0, ageTicks);
        }
    }

    public static class CorpseLivingForgeIncubationUtility
    {
        public static bool TryBegin(Corpse corpse, Faction outputFaction, string incubatorDefName, out Thing incubator, out string reason)
        {
            incubator = null;
            if (corpse == null || corpse.Destroyed || !corpse.Spawned || corpse.Map == null)
            {
                reason = "The corpse must be spawned on the map.";
                return false;
            }

            ThingDef incubatorDef = DefDatabase<ThingDef>.GetNamedSilentFail(incubatorDefName);
            if (incubatorDef == null)
            {
                reason = "The Living Forge corpse-incubation definition is missing.";
                return false;
            }

            Thing created;
            try { created = ThingMaker.MakeThing(incubatorDef); }
            catch (Exception ex)
            {
                reason = "The corpse-incubation tissue could not be created: " + ex.Message;
                return false;
            }
            if (created == null)
            {
                reason = "The corpse-incubation tissue could not be created.";
                return false;
            }

            CompCorpseLivingForgeIncubation comp = created.TryGetComp<CompCorpseLivingForgeIncubation>();
            if (comp == null)
            {
                WraithLivingTechnologyUtility.DestroyUnplaced(created);
                reason = "The corpse-incubation tissue is missing its lifecycle component.";
                return false;
            }
            comp.Prepare(outputFaction);

            Map map = corpse.Map;
            IntVec3 sourceCell = corpse.Position;
            if (!GenPlace.TryPlaceThing(created, sourceCell, map, ThingPlaceMode.Near))
            {
                WraithLivingTechnologyUtility.DestroyUnplaced(created);
                reason = "There is no valid cell for the corpse-incubation tissue.";
                return false;
            }

            try { corpse.Destroy(DestroyMode.Vanish); }
            catch (Exception ex)
            {
                TryRollbackUncommittedIncubator(created);
                reason = "The corpse could not be consumed safely: " + ex.Message;
                return false;
            }
            if (!corpse.Destroyed)
            {
                TryRollbackUncommittedIncubator(created);
                reason = "The corpse could not be consumed safely.";
                return false;
            }

            comp.CommitSourceConsumption();
            incubator = created;
            reason = null;
            return true;
        }

        private static void TryRollbackUncommittedIncubator(Thing created)
        {
            if (created == null || created.Destroyed)
                return;
            try { created.Destroy(DestroyMode.Vanish); }
            catch (Exception ex)
            {
                // Even if physical rollback fails, sourceConsumptionCommitted remains false, so
                // this residue can never mature into a free Living Forge.
                try { Log.Warning("[WNG] Could not remove uncommitted corpse-incubation residue: " + ex.Message); }
                catch { }
            }
        }
    }
}
