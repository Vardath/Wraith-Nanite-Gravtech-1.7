using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_WraithGravEngineSeed : CompProperties
    {
        public string hediffDefName = "WNG_GravEngineSeedIncubation";
        public string corpseIncubationDefName = "WNG_CorpseGravEngineIncubation";

        public CompProperties_WraithGravEngineSeed()
        {
            compClass = typeof(CompWraithGravEngineSeed);
        }
    }

    public sealed class CompWraithGravEngineSeed : ThingComp
    {
        public CompProperties_WraithGravEngineSeed Props => (CompProperties_WraithGravEngineSeed)props;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
                yield return gizmo;
            if (parent == null || parent.Destroyed || !parent.Spawned || parent.Map == null)
                yield break;

            yield return new Command_Action
            {
                defaultLabel = "Implant grav-engine seed",
                defaultDesc = "Use this cultivated organ-seed on a biological humanlike host or corpse. The host is consumed after the configured incubation time and yields one real minified Wraith grav engine.",
                action = BeginTargeting
            };
        }

        private void BeginTargeting()
        {
            if (parent == null || !parent.Spawned || parent.Map == null)
                return;

            TargetingParameters parameters = new TargetingParameters
            {
                canTargetPawns = true,
                canTargetItems = true,
                canTargetBuildings = false,
                canTargetLocations = false,
                canTargetSelf = false
            };
            Find.Targeter.BeginTargeting(parameters, TryImplant);
        }

        private void TryImplant(LocalTargetInfo target)
        {
            if (parent == null || parent.Destroyed || !parent.Spawned || parent.Map == null)
                return;

            Corpse corpse = target.Thing as Corpse;
            if (corpse != null)
            {
                TryImplantCorpse(corpse);
                return;
            }

            Pawn host = target.Pawn;
            if (!ValidLivingHost(host, out string reason))
            {
                Messages.Message(reason, host ?? parent, MessageTypeDefOf.RejectInput, false);
                return;
            }

            HediffDef hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(Props.hediffDefName);
            if (hediffDef == null)
            {
                Messages.Message("The grav-engine incubation definition is missing.", parent, MessageTypeDefOf.RejectInput, false);
                return;
            }

            ThingDef seedDef = parent.def;
            Map sourceMap = parent.Map;
            IntVec3 sourceCell = parent.Position;
            if (!TryConsumeOneSeed())
            {
                Messages.Message("The grav-engine seed could not be consumed safely.", parent, MessageTypeDefOf.RejectInput, false);
                return;
            }

            try
            {
                Hediff_GravEngineSeedIncubation incubation = HediffMaker.MakeHediff(hediffDef, host) as Hediff_GravEngineSeedIncubation;
                if (incubation == null)
                {
                    RefundSeed(seedDef, sourceMap, sourceCell);
                    Messages.Message("The grav-engine seed could not establish a valid incubation; the seed was refunded.", host, MessageTypeDefOf.RejectInput, false);
                    return;
                }
                incubation.SetOutputFaction(Faction.OfPlayer);
                host.health.AddHediff(incubation);
            }
            catch (Exception ex)
            {
                RefundSeed(seedDef, sourceMap, sourceCell);
                Messages.Message("The grav-engine seed could not establish incubation; the seed was refunded: " + ex.Message, host, MessageTypeDefOf.RejectInput, false);
                return;
            }

            WraithLivingTechnologyUtility.BestEffortMessage(
                host.LabelShort + " is incubating a Wraith grav engine. The host will be consumed when the organ matures.",
                host,
                MessageTypeDefOf.NeutralEvent);
        }

        private void TryImplantCorpse(Corpse corpse)
        {
            if (!ValidCorpseHost(corpse, out string reason))
            {
                Messages.Message(reason, corpse ?? parent, MessageTypeDefOf.RejectInput, false);
                return;
            }

            ThingDef seedDef = parent.def;
            Map sourceMap = parent.Map;
            IntVec3 sourceCell = parent.Position;
            if (!TryConsumeOneSeed())
            {
                Messages.Message("The grav-engine seed could not be consumed safely.", parent, MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (!CorpseGravEngineIncubationUtility.TryBegin(corpse, Faction.OfPlayer, Props.corpseIncubationDefName, out Thing incubator, out reason))
            {
                RefundSeed(seedDef, sourceMap, sourceCell);
                Messages.Message(reason, corpse, MessageTypeDefOf.RejectInput, false);
                return;
            }

            WraithLivingTechnologyUtility.BestEffortMessage(
                "The corpse and grav-engine seed have become one incubating Wraith organ mass.",
                incubator,
                MessageTypeDefOf.NeutralEvent);
        }

        private bool ValidLivingHost(Pawn host, out string reason)
        {
            if (host == null || host.Destroyed || host.Dead || host.Map != parent.Map)
            {
                reason = "Select a living biological humanlike on the seed's map.";
                return false;
            }
            if (!WraithLivingTechnologyUtility.IsBiologicalHumanlike(host) || host.health == null)
            {
                reason = "A Wraith grav engine requires a biological humanlike host; mechanoids and nanite synthetics cannot support the organ.";
                return false;
            }

            HediffDef forge = DefDatabase<HediffDef>.GetNamedSilentFail("WNG_HostSeedIncubation");
            HediffDef engine = DefDatabase<HediffDef>.GetNamedSilentFail(Props.hediffDefName);
            if ((forge != null && host.health.hediffSet.HasHediff(forge)) ||
                (engine != null && host.health.hediffSet.HasHediff(engine)))
            {
                reason = "This host is already incubating Wraith living technology.";
                return false;
            }

            bool playerColonist = host.Faction == Faction.OfPlayer && !host.IsPrisoner && !host.IsSlave;
            bool prisonerOrSlave = host.IsPrisoner || host.IsSlave;
            bool hostileIncapacitated = host.Faction != null && host.Faction.HostileTo(Faction.OfPlayer) &&
                (host.Downed || host.stances?.stunner?.Stunned == true);
            if (!playerColonist && !prisonerOrSlave && !hostileIncapacitated)
            {
                reason = "Use a colonist, prisoner, slave, or an incapacitated hostile biological humanlike.";
                return false;
            }

            reason = null;
            return true;
        }

        private bool ValidCorpseHost(Corpse corpse, out string reason)
        {
            if (corpse == null || corpse.Destroyed || !corpse.Spawned || corpse.Map != parent.Map)
            {
                reason = "Select a biological humanlike corpse on the seed's map.";
                return false;
            }
            if (!WraithLivingTechnologyUtility.IsBiologicalHumanlike(corpse.InnerPawn))
            {
                reason = "A Wraith grav engine requires a biological humanlike corpse; mechanoids and nanite synthetics cannot support the organ.";
                return false;
            }
            reason = null;
            return true;
        }

        private bool TryConsumeOneSeed()
        {
            if (parent == null || parent.Destroyed || parent.stackCount <= 0)
                return false;
            try
            {
                Thing consumed = parent.SplitOff(1);
                if (consumed == null)
                    return false;
                if (!consumed.Destroyed)
                    consumed.Destroy(DestroyMode.Vanish);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void RefundSeed(ThingDef seedDef, Map map, IntVec3 cell)
        {
            if (seedDef == null || map == null)
                return;
            try
            {
                Thing refund = ThingMaker.MakeThing(seedDef);
                refund.stackCount = 1;
                if (!GenPlace.TryPlaceThing(refund, cell, map, ThingPlaceMode.Near))
                    WraithLivingTechnologyUtility.DestroyUnplaced(refund);
            }
            catch (Exception ex)
            {
                try { Log.Error("[WNG] Grav-engine seed refund failed after a rolled-back corpse transaction: " + ex.Message); } catch { }
            }
        }
    }

    public sealed class Hediff_GravEngineSeedIncubation : HediffWithComps
    {
        private const int CheckIntervalTicks = 250;
        private int startTick = -1;
        private int nextCheckTick = -1;
        private bool outputCommitted;
        private Faction outputFaction;

        public void SetOutputFaction(Faction faction) => outputFaction = faction;

        private float Progress
        {
            get
            {
                if (outputCommitted)
                    return 1f;
                int now = Find.TickManager?.TicksGame ?? 0;
                if (startTick < 0)
                    return 0f;
                return Mathf.Clamp01((now - startTick) / (float)WNGSettingsUtility.WraithGravEngineIncubationTicks);
            }
        }

        public override string LabelInBrackets => (Progress * 100f).ToString("0") + "%";
        public override string TipStringExtra => "Wraith grav-engine incubation: " + (Progress * 100f).ToString("0") + "%";

        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            int now = Find.TickManager?.TicksGame ?? 0;
            if (startTick < 0) startTick = now;
            if (nextCheckTick < 0) nextCheckTick = now + CheckIntervalTicks;
        }

        public override void Tick()
        {
            base.Tick();
            if (pawn == null || pawn.Destroyed)
                return;
            if (outputCommitted)
            {
                TryCleanupCommittedHost();
                return;
            }
            if (pawn.Dead || pawn.Map == null || !pawn.Spawned)
                return;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (startTick < 0) startTick = now;
            if (nextCheckTick < 0) nextCheckTick = now + CheckIntervalTicks;
            if (now < nextCheckTick)
                return;
            nextCheckTick = now + CheckIntervalTicks;

            if (now - startTick >= WNGSettingsUtility.WraithGravEngineIncubationTicks)
                TryComplete();
        }

        private void TryComplete()
        {
            if (outputCommitted || pawn == null || pawn.Destroyed || pawn.Dead || pawn.Map == null || !pawn.Spawned)
                return;

            if (!WraithLivingTechnologyUtility.TryPrepareMinifiedGravEngine(outputFaction, out MinifiedThing minified, out string reason))
            {
                Log.ErrorOnce("[WNG] Matured grav-engine host incubation could not prepare output; host remains authoritative and completion will retry: " + reason, pawn.thingIDNumber ^ 0x4B771);
                return;
            }
            if (!WraithLivingTechnologyUtility.TryPlacePrepared(minified, pawn.Position, pawn.Map, out reason))
            {
                WraithLivingTechnologyUtility.DestroyUnplaced(minified);
                Log.ErrorOnce("[WNG] Matured grav-engine host incubation could not place output; host remains authoritative and completion will retry: " + reason, pawn.thingIDNumber ^ 0x4B772);
                return;
            }

            outputCommitted = true;
            TryCleanupCommittedHost();
            WraithLivingTechnologyUtility.BestEffortMessage(
                "The host has been consumed. A minified Wraith grav engine has matured.",
                minified,
                MessageTypeDefOf.PositiveEvent);
        }

        private void TryCleanupCommittedHost()
        {
            if (pawn == null || pawn.Destroyed)
                return;
            try { pawn.Destroy(DestroyMode.Vanish); }
            catch (Exception ex)
            {
                try { Log.Warning("[WNG] Wraith grav-engine output committed but host cleanup failed; output will not be duplicated: " + ex.Message); } catch { }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref startTick, "wngGravEngineHostStartTick", -1);
            Scribe_Values.Look(ref nextCheckTick, "wngGravEngineHostNextCheckTick", -1);
            Scribe_Values.Look(ref outputCommitted, "wngGravEngineHostOutputCommitted", false);
            Scribe_References.Look(ref outputFaction, "wngGravEngineHostOutputFaction");
        }
    }

    public sealed class CompProperties_CorpseGravEngineIncubation : CompProperties
    {
        public CompProperties_CorpseGravEngineIncubation()
        {
            compClass = typeof(CompCorpseGravEngineIncubation);
        }
    }

    public sealed class CompCorpseGravEngineIncubation : ThingComp
    {
        private int ageTicks;
        private bool sourceConsumptionCommitted;
        private bool outputCommitted;
        private Faction outputFaction;

        internal void Prepare(Faction faction)
        {
            ageTicks = 0;
            sourceConsumptionCommitted = false;
            outputCommitted = false;
            outputFaction = faction;
        }

        internal void CommitSourceConsumption() => sourceConsumptionCommitted = true;

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (parent == null || parent.Destroyed || !parent.Spawned || parent.Map == null || !sourceConsumptionCommitted)
                return;
            if (outputCommitted)
            {
                TryCleanupCommittedParent();
                return;
            }

            ageTicks = ageTicks > int.MaxValue - 250 ? int.MaxValue : Math.Max(0, ageTicks) + 250;
            if (ageTicks >= WNGSettingsUtility.WraithGravEngineIncubationTicks)
                TryComplete();
        }

        private void TryComplete()
        {
            if (!sourceConsumptionCommitted || outputCommitted || parent == null || parent.Destroyed || !parent.Spawned || parent.Map == null)
                return;

            if (!WraithLivingTechnologyUtility.TryPrepareMinifiedGravEngine(outputFaction, out MinifiedThing minified, out string reason))
            {
                Log.ErrorOnce("[WNG] Matured corpse grav-engine incubation could not prepare output; incubation remains and will retry: " + reason, parent.thingIDNumber ^ 0x5B771);
                return;
            }
            if (!WraithLivingTechnologyUtility.TryPlacePrepared(minified, parent.Position, parent.Map, out reason))
            {
                WraithLivingTechnologyUtility.DestroyUnplaced(minified);
                Log.ErrorOnce("[WNG] Matured corpse grav-engine incubation could not place output; incubation remains and will retry: " + reason, parent.thingIDNumber ^ 0x5B772);
                return;
            }

            outputCommitted = true;
            TryCleanupCommittedParent();
            WraithLivingTechnologyUtility.BestEffortMessage(
                "The corpse incubation is complete. A minified Wraith grav engine has matured.",
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
                try { Log.Warning("[WNG] Wraith grav-engine output committed but residual incubation tissue cleanup failed; output will not be duplicated: " + ex.Message); } catch { }
            }
        }

        public override string CompInspectStringExtra()
        {
            if (!sourceConsumptionCommitted)
                return "Incubation: inert (source transaction incomplete)";
            if (outputCommitted)
                return "Incubation: complete (residual tissue cleanup pending)";
            float progress = Mathf.Clamp01(Math.Max(0, ageTicks) / (float)Math.Max(1, WNGSettingsUtility.WraithGravEngineIncubationTicks));
            return "Wraith grav-engine incubation: " + progress.ToStringPercent();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref ageTicks, "wngGravEngineCorpseAgeTicks", 0);
            Scribe_Values.Look(ref sourceConsumptionCommitted, "wngGravEngineCorpseSourceCommitted", false);
            Scribe_Values.Look(ref outputCommitted, "wngGravEngineCorpseOutputCommitted", false);
            Scribe_References.Look(ref outputFaction, "wngGravEngineCorpseOutputFaction");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                ageTicks = Math.Max(0, ageTicks);
        }
    }

    public static class CorpseGravEngineIncubationUtility
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
                reason = "The grav-engine corpse-incubation definition is missing.";
                return false;
            }

            Thing created;
            try { created = ThingMaker.MakeThing(incubatorDef); }
            catch (Exception ex)
            {
                reason = "The grav-engine incubation tissue could not be created: " + ex.Message;
                return false;
            }
            if (created == null)
            {
                reason = "The grav-engine incubation tissue could not be created.";
                return false;
            }

            CompCorpseGravEngineIncubation comp = created.TryGetComp<CompCorpseGravEngineIncubation>();
            if (comp == null)
            {
                WraithLivingTechnologyUtility.DestroyUnplaced(created);
                reason = "The grav-engine incubation tissue is missing its lifecycle component.";
                return false;
            }
            comp.Prepare(outputFaction);

            Map map = corpse.Map;
            IntVec3 sourceCell = corpse.Position;
            if (!GenPlace.TryPlaceThing(created, sourceCell, map, ThingPlaceMode.Near))
            {
                WraithLivingTechnologyUtility.DestroyUnplaced(created);
                reason = "There is no valid cell for the grav-engine incubation tissue.";
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
                try { Log.Warning("[WNG] Could not remove uncommitted grav-engine corpse-incubation residue: " + ex.Message); } catch { }
            }
        }
    }
}
