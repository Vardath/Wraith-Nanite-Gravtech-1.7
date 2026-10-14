using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace WraithNaniteGravtech
{
    public static class SovereignLatticeUtility
    {
        private const string LatticeDefName = "WNG_SovereignNeuralLattice";
        private const string AbilityDefName = "WNG_SovereignLatticeDirective";
        private const string HumanEmpDefName = "WNG_NaniteEMPDisruption";
        private const string SummonAbilityDefName = "WNG_ReplicatorSovereignSummon";
        private const string ReleaseSummonedAbilityDefName = "WNG_ReplicatorSovereignReleaseSummoned";

        public static HediffDef LatticeDef => DefDatabase<HediffDef>.GetNamedSilentFail(LatticeDefName);
        public static AbilityDef DirectiveDef => DefDatabase<AbilityDef>.GetNamedSilentFail(AbilityDefName);
        public static AbilityDef SummonDef => DefDatabase<AbilityDef>.GetNamedSilentFail(SummonAbilityDefName);
        public static AbilityDef ReleaseSummonedDef => DefDatabase<AbilityDef>.GetNamedSilentFail(ReleaseSummonedAbilityDefName);
        public static int MaxControlledBlocks => WNGSettingsUtility.SovereignLatticeControlCap;
        public static string DomainId(Pawn bearer) => bearer == null ? null : "sovereign-lattice:" + bearer.thingIDNumber;

        public static bool HasInstalledLattice(Pawn pawn)
        {
            HediffDef def = LatticeDef;
            return pawn != null && !pawn.Dead && !ReplicatorQueenUtility.IsExactQueen(pawn) &&
                   def != null && pawn.health?.hediffSet?.GetFirstHediffOfDef(def) != null;
        }

        public static bool BearerEmpDisrupted(Pawn pawn)
        {
            if (pawn == null)
                return true;
            HediffDef disruption = DefDatabase<HediffDef>.GetNamedSilentFail(HumanEmpDefName);
            return disruption != null && pawn.health?.hediffSet?.GetFirstHediffOfDef(disruption) != null;
        }

        public static bool SignalAvailable(Pawn bearer)
        {
            if (!HasInstalledLattice(bearer) || bearer.Faction == null || BearerEmpDisrupted(bearer))
                return false;
            return !bearer.Spawned || !ReplicatorContainmentUtility.IsContained(bearer.Map, bearer.Position);
        }

        public static IEnumerable<Pawn> ControlledBlocks(Pawn bearer)
        {
            if (bearer == null)
                yield break;

            HashSet<Pawn> yielded = new HashSet<Pawn>();
            if (Find.Maps != null)
            {
                foreach (Map map in Find.Maps)
                {
                    IReadOnlyList<Pawn> pawns = map?.mapPawns?.AllPawnsSpawned;
                    if (pawns == null)
                        continue;
                    for (int i = 0; i < pawns.Count; i++)
                    {
                        Pawn pawn = pawns[i];
                        CompReplicatorSovereignState state = pawn?.TryGetComp<CompReplicatorSovereignState>();
                        if (state?.HasRecord == true && state.ControllerPawn == bearer &&
                            state.ControlAuthority == ReplicatorControlAuthority.SovereignNeuralLattice &&
                            yielded.Add(pawn))
                            yield return pawn;
                    }
                }
            }

            if (Find.WorldObjects?.Caravans != null)
            {
                foreach (Caravan caravan in Find.WorldObjects.Caravans)
                {
                    List<Pawn> pawns = caravan?.PawnsListForReading;
                    if (pawns == null)
                        continue;
                    for (int i = 0; i < pawns.Count; i++)
                    {
                        Pawn pawn = pawns[i];
                        CompReplicatorSovereignState state = pawn?.TryGetComp<CompReplicatorSovereignState>();
                        if (state?.HasRecord == true && state.ControllerPawn == bearer &&
                            state.ControlAuthority == ReplicatorControlAuthority.SovereignNeuralLattice &&
                            yielded.Add(pawn))
                            yield return pawn;
                    }
                }
            }
        }

        public static int ControlledCount(Pawn bearer)
        {
            return ControlledBlocks(bearer).Count();
        }

        public static void ReleaseAll(Pawn bearer)
        {
            List<Pawn> blocks = ControlledBlocks(bearer).Distinct().ToList();
            for (int i = 0; i < blocks.Count; i++)
                blocks[i]?.TryGetComp<CompReplicatorSovereignState>()?.TryReleaseToExactPriorState();
        }
    }

    public sealed class Hediff_SovereignNeuralLattice : Hediff_AddedPart
    {
        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            if (pawn == null || ReplicatorQueenUtility.IsExactQueen(pawn))
                return;
            GainIfMissing(SovereignLatticeUtility.DirectiveDef);
            GainIfMissing(SovereignLatticeUtility.SummonDef);
            GainIfMissing(SovereignLatticeUtility.ReleaseSummonedDef);
        }

        public override void PostRemoved()
        {
            Pawn bearer = pawn;
            SovereignLatticeUtility.ReleaseAll(bearer);
            RemoveIfPresent(bearer, SovereignLatticeUtility.DirectiveDef);
            RemoveIfPresent(bearer, SovereignLatticeUtility.SummonDef);
            RemoveIfPresent(bearer, SovereignLatticeUtility.ReleaseSummonedDef);
            base.PostRemoved();
        }

        private void GainIfMissing(AbilityDef ability)
        {
            if (ability != null && pawn?.abilities != null && pawn.abilities.GetAbility(ability) == null)
                pawn.abilities.GainAbility(ability);
        }

        private static void RemoveIfPresent(Pawn bearer, AbilityDef ability)
        {
            if (ability != null && bearer?.abilities != null && bearer.abilities.GetAbility(ability) != null)
                bearer.abilities.RemoveAbility(ability);
        }
    }

    public sealed class CompProperties_AbilitySovereignLatticeDirective : CompProperties_AbilityEffect
    {
        public CompProperties_AbilitySovereignLatticeDirective()
        {
            compClass = typeof(CompAbilityEffect_SovereignLatticeDirective);
        }
    }

    public sealed class CompAbilityEffect_SovereignLatticeDirective : CompAbilityEffect
    {
        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            Pawn bearer = parent.pawn;
            Pawn block = target.Pawn;
            if (!SovereignLatticeUtility.SignalAvailable(bearer) || bearer == null || !bearer.Spawned)
            {
                if (throwMessages)
                    Messages.Message("The Sovereign Neural Lattice is absent, disrupted, contained or otherwise unable to project control.", bearer, MessageTypeDefOf.RejectInput, false);
                return false;
            }
            if (block == null || block.Dead || !block.Spawned || block.Map != bearer.Map ||
                !ReplicatorAssimilationUtility.IsBlockReplicator(block))
            {
                if (throwMessages)
                    Messages.Message("The lattice can target only a living physical block Replicator on the bearer's map.", bearer, MessageTypeDefOf.RejectInput, false);
                return false;
            }
            if (ReplicatorInterferenceUtility.IsEmpDisrupted(block) ||
                ReplicatorContainmentUtility.IsContained(block.Map, block.Position))
            {
                if (throwMessages)
                    Messages.Message("EMP disruption or active containment is blocking lattice acquisition.", block, MessageTypeDefOf.RejectInput, false);
                return false;
            }
            if (block.TryGetComp<CompReplicatorTemporaryAsuranState>()?.HasOverrideRecord == true)
            {
                if (throwMessages)
                    Messages.Message("A temporary Asuran intrusion must resolve before the Sovereign Neural Lattice can change this block.", block, MessageTypeDefOf.RejectInput, false);
                return false;
            }

            CompReplicatorSovereignState state = block.TryGetComp<CompReplicatorSovereignState>();
            if (state == null)
                return false;

            bool alreadyMine = state.HasRecord && state.ControllerPawn == bearer &&
                               state.ControlAuthority == ReplicatorControlAuthority.SovereignNeuralLattice;
            if (!alreadyMine && state.HasRecord)
            {
                if (throwMessages)
                    Messages.Message("This block already belongs to another persistent sovereign authority.", block, MessageTypeDefOf.RejectInput, false);
                return false;
            }

            if (!alreadyMine && SovereignLatticeUtility.ControlledCount(bearer) >= SovereignLatticeUtility.MaxControlledBlocks)
            {
                if (throwMessages)
                    Messages.Message("This Sovereign Neural Lattice is already maintaining its configured maximum of " +
                                     SovereignLatticeUtility.MaxControlledBlocks + " block Replicators.", bearer, MessageTypeDefOf.RejectInput, false);
                return false;
            }
            return base.Valid(target, throwMessages);
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Pawn bearer = parent.pawn;
            Pawn block = target.Pawn;
            CompReplicatorSovereignState state = block?.TryGetComp<CompReplicatorSovereignState>();
            if (bearer == null || block == null || state == null)
                return;

            bool alreadyMine = state.HasRecord && state.ControllerPawn == bearer &&
                               state.ControlAuthority == ReplicatorControlAuthority.SovereignNeuralLattice;
            bool success;
            string message;
            if (alreadyMine)
            {
                success = state.TryReleaseToExactPriorState();
                message = success
                    ? block.LabelShortCap + " has been released from the Sovereign Neural Lattice and restored to its exact prior controller state."
                    : "The Sovereign Neural Lattice could not safely release " + block.LabelShortCap + ".";
            }
            else
            {
                if (SovereignLatticeUtility.ControlledCount(bearer) >= SovereignLatticeUtility.MaxControlledBlocks)
                    return;
                success = state.TryAssignSovereignNeuralLattice(bearer);
                message = success
                    ? block.LabelShortCap + " has entered " + bearer.LabelShortCap + "'s Sovereign Neural Lattice domain."
                    : "The Sovereign Neural Lattice could not acquire " + block.LabelShortCap + ".";
            }

            if (bearer.Faction == Faction.OfPlayer)
                Messages.Message(message, block, success ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput, false);
        }
    }

    public sealed class Recipe_InstallSovereignNeuralLattice : Recipe_InstallArtificialBodyPart
    {
        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            Pawn pawn = thing as Pawn;
            if (pawn == null)
                return false;

            return !ReplicatorQueenUtility.IsExactQueen(pawn) &&
                   !SovereignLatticeUtility.HasInstalledLattice(pawn) &&
                   base.AvailableOnNow(thing, part);
        }
    }

    public sealed class Recipe_RemoveSovereignNeuralLattice : Recipe_Surgery
    {
        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            Pawn pawn = thing as Pawn;
            if (pawn == null)
                return false;

            HediffDef def = SovereignLatticeUtility.LatticeDef;
            return def != null &&
                   pawn.health?.hediffSet?.GetFirstHediffOfDef(def) != null &&
                   base.AvailableOnNow(thing, part);
        }

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            HediffDef def = SovereignLatticeUtility.LatticeDef;
            Hediff hediff = def == null ? null : pawn?.health?.hediffSet?.GetFirstHediffOfDef(def);
            if (hediff == null)
                return;
            if (CheckSurgeryFail(billDoer, pawn, ingredients, part, bill))
                return;

            pawn.health.RemoveHediff(hediff);

            ThingDef itemDef = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_SovereignNeuralLattice");
            if (itemDef == null)
                return;
            Thing recovered = ThingMaker.MakeThing(itemDef);
            Map map = pawn.MapHeld;
            IntVec3 cell = billDoer?.PositionHeld ?? pawn.PositionHeld;
            if (map == null || !GenPlace.TryPlaceThing(recovered, cell, map, ThingPlaceMode.Near))
            {
                if (!recovered.Destroyed)
                    recovered.Destroy(DestroyMode.Vanish);
            }
        }
    }

    public sealed class CompProperties_ReplicatorCoreFragmentSalvage : CompProperties
    {
        public float dropChance;

        public CompProperties_ReplicatorCoreFragmentSalvage()
        {
            compClass = typeof(CompReplicatorCoreFragmentSalvage);
        }
    }

    public sealed class CompReplicatorCoreFragmentSalvage : ThingComp
    {
        private IntVec3 lastKnownPosition = IntVec3.Invalid;
        private bool rolled;

        private CompProperties_ReplicatorCoreFragmentSalvage Props => (CompProperties_ReplicatorCoreFragmentSalvage)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (parent.Spawned)
                lastKnownPosition = parent.Position;
        }

        public override void CompTick()
        {
            base.CompTick();
            if (parent.Spawned)
                lastKnownPosition = parent.Position;
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            if (mode == DestroyMode.KillFinalize && !rolled)
            {
                rolled = true;
                TryDrop(previousMap);
            }
            base.PostDestroy(mode, previousMap);
        }

        private void TryDrop(Map map)
        {
            if (map == null || !lastKnownPosition.IsValid || !lastKnownPosition.InBounds(map))
                return;
            float chance = Math.Max(0f, Math.Min(1f, Props.dropChance));
            if (!Rand.Chance(chance))
                return;

            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_ReplicatorCoreFragment");
            if (def == null)
                return;
            Thing fragment = ThingMaker.MakeThing(def);
            if (!GenPlace.TryPlaceThing(fragment, lastKnownPosition, map, ThingPlaceMode.Near) && !fragment.Destroyed)
                fragment.Destroy(DestroyMode.Vanish);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref lastKnownPosition, "wngCoreFragmentLastKnownPosition", IntVec3.Invalid);
            Scribe_Values.Look(ref rolled, "wngCoreFragmentRolled", false);
        }
    }

    public sealed class QuestNode_Root_SovereignLatticeRecovery : QuestNode
    {
        protected override bool TestRunInt(Slate slate)
        {
            if (Find.Maps == null || !Find.Maps.Any(m => m != null && m.IsPlayerHome))
                return false;
            SitePartDef part = DefDatabase<SitePartDef>.GetNamedSilentFail("WNG_SovereignLatticeCache");
            if (part == null)
                return false;
            if (Find.QuestManager?.QuestsListForReading != null &&
                Find.QuestManager.QuestsListForReading.Any(q => q != null && (q.State == QuestState.Ongoing || q.State == QuestState.NotYetAccepted) &&
                    q.PartsListForReading.OfType<QuestPart_SovereignLatticeRecovery>().Any()))
                return false;
            return !Find.WorldObjects.AllWorldObjects.OfType<Site>()
                .Any(site => site?.parts != null && site.parts.Any(p => p?.def == part));
        }

        protected override void RunInt()
        {
            string accepted = QuestGenUtility.HardcodedSignalWithQuestID("Accepted");
            QuestGen.quest.AddPart(new QuestPart_SovereignLatticeRecovery { inSignal = accepted, inSignalEnable = accepted });
        }
    }

    public sealed class QuestPart_SovereignLatticeRecovery : QuestPartActivable
    {
        public string inSignal;
        private Site cacheSite;
        private bool started;
        private bool resolved;

        public override string DescriptionPart
        {
            get
            {
                if (resolved)
                    return "Sovereign Neural Lattice cache: reached.";
                if (!started)
                    return "Sovereign Neural Lattice cache: accept the lead.";
                return cacheSite == null ? "Sovereign Neural Lattice cache: locating signal." : "Sovereign Neural Lattice cache: travel to the marked site.";
            }
        }

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (started || resolved || signal.tag != inSignal)
                return;
            started = true;
            if (!TryCreateSite())
            {
                resolved = true;
                quest.End(QuestEndOutcome.Fail);
            }
        }

        public override void QuestPartTick()
        {
            base.QuestPartTick();
            if (!started || resolved)
                return;
            if (cacheSite == null)
            {
                resolved = true;
                quest.End(QuestEndOutcome.Fail);
                return;
            }
            if (cacheSite.HasMap)
            {
                resolved = true;
                quest.End(QuestEndOutcome.Success);
            }
        }

        private bool TryCreateSite()
        {
            SitePartDef part = DefDatabase<SitePartDef>.GetNamedSilentFail("WNG_SovereignLatticeCache");
            if (part == null)
                return false;
            PlanetTile tile;
            if (!TileFinder.TryFindNewSiteTile(out tile, 8, 28, allowCaravans: false))
                return false;

            float points = Math.Max(300f, StorytellerUtility.DefaultSiteThreatPointsNow() * 0.75f);
            Site site = SiteMaker.MakeSite(part, tile, faction: null, ifHostileThenMustRemainHostile: false, threatPoints: points);
            if (site == null)
                return false;
            site.customLabel = "Severed Command-Lattice Cache";
            try
            {
                Find.WorldObjects.Add(site);
                cacheSite = site;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignal, "wngSovereignLatticeQuestSignal");
            Scribe_References.Look(ref cacheSite, "wngSovereignLatticeQuestSite");
            Scribe_Values.Look(ref started, "wngSovereignLatticeQuestStarted", false);
            Scribe_Values.Look(ref resolved, "wngSovereignLatticeQuestResolved", false);
        }
    }

    public sealed class SitePartWorker_SovereignLatticeCache : SitePartWorker
    {
        public override void PostMapGenerate(Map map)
        {
            base.PostMapGenerate(map);
            if (map == null)
                return;
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_SovereignNeuralLattice");
            if (def == null)
                return;
            Thing lattice = ThingMaker.MakeThing(def);
            if (!GenPlace.TryPlaceThing(lattice, map.Center, map, ThingPlaceMode.Near) && !lattice.Destroyed)
                lattice.Destroy(DestroyMode.Vanish);
        }
    }
}
