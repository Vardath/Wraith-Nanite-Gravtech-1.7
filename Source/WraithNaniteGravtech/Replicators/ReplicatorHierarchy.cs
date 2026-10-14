using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_ReplicatorStepAudio : CompProperties
    {
        public SoundDef stepElectric;
        public SoundDef stepMetalA;
        public SoundDef stepMetalB;

        public CompProperties_ReplicatorStepAudio()
        {
            compClass = typeof(CompReplicatorStepAudio);
        }
    }

    /// <summary>
    /// Presentation-only block-form movement audio. A cue is emitted only when the exact pawn
    /// enters a new map cell while its native pather is moving; spawning, teleporting and
    /// human-form Replicators do not use this component.
    /// </summary>
    public sealed class CompReplicatorStepAudio : ThingComp
    {
        private IntVec3 lastPosition = IntVec3.Invalid;
        private int stepIndex;

        private CompProperties_ReplicatorStepAudio Props => (CompProperties_ReplicatorStepAudio)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            lastPosition = parent?.Position ?? IntVec3.Invalid;
            stepIndex = parent == null ? 0 : Math.Abs(parent.thingIDNumber % 3);
        }

        public override void CompTick()
        {
            base.CompTick();

            Pawn pawn = parent as Pawn;
            if (pawn == null || !pawn.Spawned || pawn.Map == null || pawn.Dead)
            {
                lastPosition = IntVec3.Invalid;
                return;
            }

            IntVec3 current = pawn.Position;
            bool changedCell = lastPosition.IsValid && current != lastPosition;
            lastPosition = current;

            if (!changedCell || pawn.Downed || pawn.pather == null || !pawn.pather.Moving || current.Fogged(pawn.Map))
                return;

            SoundDef sound = stepIndex == 0 ? Props.stepElectric : stepIndex == 1 ? Props.stepMetalA : Props.stepMetalB;
            stepIndex = (stepIndex + 1) % 3;

            try
            {
                sound?.PlayOneShot(new TargetInfo(current, pawn.Map));
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Replicator step sound failed: " + ex.Message);
            }
        }
    }

    public sealed class CompProperties_ReplicatorHierarchy : CompProperties
    {
        public string upgradePawnKind;
        public string splitChildPawnKind;
        public int unitsRequired = 2;
        public int splitCount = 2;
        public int assemblyCheckTicks = 2500;
        public int splitRecombineDelayTicks = 2500;
        public float assemblyRadius = 7f;

        public CompProperties_ReplicatorHierarchy()
        {
            compClass = typeof(CompReplicatorHierarchy);
        }
    }

    /// <summary>
    /// Clean WNGv1 physical hierarchy only.
    ///
    /// Upward recombination is intentionally a non-death transaction: the replacement form is
    /// generated and placed first, then the exact source pawns are consumed with Vanish. Genuine
    /// pawn death emits configured lower-tier children through Notify_Killed; KillFinalize remains
    /// a fallback for direct destruction paths that bypass Pawn.Kill.
    ///
    /// Learned adaptation and controller-domain identity transfer at the explicit target-preparation
    /// point. Different controller domains may never recombine merely because their faction matches.
    /// Future authority implementations extend the shared domain component rather than adding a
    /// second parallel hierarchy/control framework.
    /// </summary>
    public sealed class CompReplicatorHierarchy : ThingComp
    {
        private int nextAssemblyTick;
        private int recombinationLockedUntilTick;
        private bool splitEmitted;
        private IntVec3 lastKnownPosition = IntVec3.Invalid;
        private Faction lastKnownFaction;

        private CompProperties_ReplicatorHierarchy Props => (CompProperties_ReplicatorHierarchy)props;

        public int RecombinationLockedUntilTick => recombinationLockedUntilTick;

        public void LockRecombination(int ticks)
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            recombinationLockedUntilTick = Math.Max(recombinationLockedUntilTick, now + Math.Max(0, ticks));
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            CacheIdentity();

            if (!respawningAfterLoad && nextAssemblyTick <= 0)
                ScheduleNextAssembly();
        }

        public override void CompTick()
        {
            base.CompTick();

            Pawn pawn = parent as Pawn;
            if (pawn == null || !pawn.Spawned || pawn.Dead)
                return;

            CacheIdentity();

            if (ReplicatorInterferenceUtility.IsEmpDisrupted(pawn) ||
                TemporaryAsuranIntrusionUtility.IsCommandSuppressed(pawn))
                return;

            if (ReplicatorContainmentUtility.IsContained(pawn.Map, pawn.Position))
                return;

            if (string.IsNullOrEmpty(Props.upgradePawnKind))
                return;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (nextAssemblyTick <= 0)
                ScheduleNextAssembly();

            if (now < nextAssemblyTick || now < recombinationLockedUntilTick)
                return;

            ScheduleNextAssembly();
            TryRecombine(pawn, now);
        }

        private void CacheIdentity()
        {
            Pawn pawn = parent as Pawn;
            if (pawn == null)
                return;

            if (pawn.Spawned)
                lastKnownPosition = pawn.Position;
            if (pawn.Faction != null)
                lastKnownFaction = pawn.Faction;
        }

        private void ScheduleNextAssembly()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            int baseInterval = Math.Max(250, Props.assemblyCheckTicks);
            Pawn pawn = parent as Pawn;
            float coordinationFactor = pawn?.Map?.GetComponent<MapComponent_ReplicatorCoordination>()?.AssemblyFactorFor(pawn) ?? 1f;
            float postureFactor = pawn?.Map?.GetComponent<MapComponent_ReplicatorSwarmBehavior>()?.AssemblyFactorFor(pawn) ?? 1f;
            float intervalFactor = Math.Max(0.05f, coordinationFactor * postureFactor);
            int interval = Math.Max(250, (int)Math.Round(baseInterval * intervalFactor));
            int staggerWindow = Math.Min(250, interval);
            int stagger = parent == null ? 0 : Math.Abs(parent.thingIDNumber % Math.Max(1, staggerWindow));
            nextAssemblyTick = now + interval + stagger;
        }

        private bool IsEligibleDonor(Pawn candidate, Pawn leader, int now)
        {
            if (candidate == null || candidate.Destroyed || candidate.Dead || candidate.Downed || !candidate.Spawned)
                return false;
            if (candidate.def != leader.def || candidate.Faction != leader.Faction)
                return false;
            if (!ReplicatorDomainUtility.SameDomain(candidate, leader))
                return false;
            if (candidate.Position.DistanceToSquared(leader.Position) > Props.assemblyRadius * Props.assemblyRadius)
                return false;
            if (ReplicatorContainmentUtility.IsContained(candidate.Map, candidate.Position))
                return false;
            if (ReplicatorInterferenceUtility.IsEmpDisrupted(candidate) ||
                TemporaryAsuranIntrusionUtility.IsCommandSuppressed(candidate))
                return false;

            CompReplicatorHierarchy hierarchy = candidate.TryGetComp<CompReplicatorHierarchy>();
            if (hierarchy == null || hierarchy.Props.upgradePawnKind != Props.upgradePawnKind)
                return false;
            if (now < hierarchy.recombinationLockedUntilTick)
                return false;

            return true;
        }

        private void TryRecombine(Pawn pawn, int now)
        {
            if (pawn.Map == null || pawn.Faction == null || pawn.Downed)
                return;

            // Ordinary player block control is still not defined. The only player-faction exception
            // here is a real Temporary-Asuran override, whose exact prior state is already recorded.
            if (pawn.Faction == Faction.OfPlayer && !TemporaryAsuranIntrusionUtility.IsTemporarilyOverridden(pawn))
                return;

            int required = Math.Max(2, Props.unitsRequired);
            List<Pawn> donors = new List<Pawn>(required);
            IReadOnlyList<Pawn> spawned = pawn.Map.mapPawns.AllPawnsSpawned;

            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn candidate = spawned[i];
                if (IsEligibleDonor(candidate, pawn, now))
                    donors.Add(candidate);
            }

            if (donors.Count < required)
                return;

            donors.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));
            if (donors[0] != pawn)
                return;

            if (donors.Count > required)
                donors.RemoveRange(required, donors.Count - required);

            PawnKindDef upgradedKind = DefDatabase<PawnKindDef>.GetNamedSilentFail(Props.upgradePawnKind);
            if (upgradedKind == null)
            {
                Log.ErrorOnce("[WNG] Missing Replicator hierarchy upgrade PawnKindDef: " + Props.upgradePawnKind,
                    Props.upgradePawnKind.GetHashCode());
                return;
            }

            Pawn upgraded = null;
            try
            {
                upgraded = PawnGenerator.GeneratePawn(upgradedKind, pawn.Faction);

                if (!ReplicatorDomainUtility.AllSameDomain(donors) ||
                    (!TemporaryAsuranIntrusionUtility.TransformationStatesCompatible(donors) ||
                     !ReplicatorSovereignControlUtility.TransformationStatesCompatible(donors)))
                {
                    if (!upgraded.Destroyed)
                        upgraded.Destroy(DestroyMode.Vanish);
                    return;
                }

                ReplicatorDomainUtility.CopyDomain(pawn, upgraded);
                TemporaryAsuranIntrusionUtility.CopyMergedState(donors, upgraded);
                ReplicatorSovereignControlUtility.CopyState(pawn, upgraded);
                upgraded.TryGetComp<CompReplicatorAdaptation>()?.MergeFrom(donors);
                upgraded.TryGetComp<CompReplicatorMaterialProfile>()?.MergeFrom(donors);

                if (!GenPlace.TryPlaceThing(
                        upgraded,
                        pawn.Position,
                        pawn.Map,
                        ThingPlaceMode.Near,
                        null,
                        cell => !ReplicatorContainmentUtility.IsContained(pawn.Map, cell)))
                {
                    if (!upgraded.Destroyed)
                        upgraded.Destroy(DestroyMode.Vanish);
                    return;
                }

                // Replacement exists before any source is consumed. Vanish deliberately bypasses both
                // native killed leavings and this component's genuine-death split path.
                for (int i = 0; i < donors.Count; i++)
                {
                    Pawn donor = donors[i];
                    if (donor != null && !donor.Destroyed)
                        donor.Destroy(DestroyMode.Vanish);
                }

                // The upgraded body and source-consumption transaction are already committed.
                PlaySoundFailSoft("WNG_ReplicatorAssembly", upgraded, upgraded.Map, upgraded.Position);
                PlayRecombinationVfxFailSoft(upgraded);
            }
            catch (Exception ex)
            {
                if (upgraded != null && !upgraded.Destroyed)
                    upgraded.Destroy(DestroyMode.Vanish);
                Log.Error("[WNG] Replicator upward recombination failed: " + ex);
            }
        }

        public override void Notify_Killed(Map prevMap, DamageInfo? dinfo = null)
        {
            // Pawns normally die without immediately destroying their Thing instance, so PostDestroy
            // is too late/unreliable for the hierarchy split. This hook fires for ordinary weapons,
            // projectiles and explosive/missile kills and therefore owns the genuine-death split.
            CacheIdentity();
            TryEmitDeathSplit(prevMap);
            PlaySoundFailSoft("WNG_ReplicatorMatterFall", parent, prevMap, lastKnownPosition);
            base.Notify_Killed(prevMap, dinfo);
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            // Fallback only for direct KillFinalize destruction paths. splitEmitted prevents a
            // duplicate if Notify_Killed already committed the lower-tier children.
            if (mode == DestroyMode.KillFinalize && !splitEmitted)
            {
                CacheIdentity();
                TryEmitDeathSplit(previousMap);
                PlaySoundFailSoft("WNG_ReplicatorMatterFall", parent, previousMap, lastKnownPosition);
            }

            base.PostDestroy(mode, previousMap);
        }

        private void TryEmitDeathSplit(Map map)
        {
            if (splitEmitted || string.IsNullOrEmpty(Props.splitChildPawnKind) || map == null)
                return;

            splitEmitted = true;

            Pawn parentPawn = parent as Pawn;
            int inheritedEmpUntil = parentPawn?.TryGetComp<CompReplicatorInterference>()?.EmpDisruptedUntilTick ?? 0;
            CompReplicatorAdaptation inheritedAdaptation = parentPawn?.TryGetComp<CompReplicatorAdaptation>();
            CompReplicatorMaterialProfile inheritedMaterial = parentPawn?.TryGetComp<CompReplicatorMaterialProfile>();
            CompReplicatorDomain inheritedDomain = parentPawn?.TryGetComp<CompReplicatorDomain>();
            Faction faction = parentPawn?.Faction ?? lastKnownFaction;
            if (faction == null)
            {
                Log.Warning("[WNG] Replicator death split could not recover faction for " + parent?.def?.defName + ".");
                return;
            }

            IntVec3 origin = lastKnownPosition;
            if (!origin.IsValid || !origin.InBounds(map))
            {
                Log.Warning("[WNG] Replicator death split could not recover a valid map position for " + parent?.def?.defName + ".");
                return;
            }

            PawnKindDef childKind = DefDatabase<PawnKindDef>.GetNamedSilentFail(Props.splitChildPawnKind);
            if (childKind == null)
            {
                Log.Error("[WNG] Missing Replicator split PawnKindDef: " + Props.splitChildPawnKind);
                return;
            }

            int count = Math.Max(1, Props.splitCount);
            List<Pawn> committedChildren = new List<Pawn>(count);
            for (int i = 0; i < count; i++)
            {
                Pawn child = null;
                try
                {
                    child = PawnGenerator.GeneratePawn(childKind, faction);
                    if (!GenPlace.TryPlaceThing(child, origin, map, ThingPlaceMode.Near))
                    {
                        if (!child.Destroyed)
                            child.Destroy(DestroyMode.Vanish);
                        Log.Warning("[WNG] Could not place a split-born Replicator child for " + parent?.def?.defName + ".");
                        continue;
                    }

                    child.TryGetComp<CompReplicatorDomain>()?.CopyFrom(inheritedDomain);
                    TemporaryAsuranIntrusionUtility.CopyState(parentPawn, child);
                    ReplicatorSovereignControlUtility.CopyState(parentPawn, child);
                    child.TryGetComp<CompReplicatorHierarchy>()?.LockRecombination(Props.splitRecombineDelayTicks);
                    child.TryGetComp<CompReplicatorAdaptation>()?.InheritFrom(inheritedAdaptation);
                    child.TryGetComp<CompReplicatorMaterialProfile>()?.InheritFrom(inheritedMaterial);
                    child.TryGetComp<CompReplicatorInterference>()?.InheritEmpDisruptionUntil(
                        inheritedEmpUntil,
                        preservePhysicalStun: true);
                    committedChildren.Add(child);
                }
                catch (Exception ex)
                {
                    if (child != null && !child.Destroyed)
                        child.Destroy(DestroyMode.Vanish);
                    Log.Error("[WNG] Replicator genuine-death split failed for " + parent?.def?.defName + ": " + ex);
                }
            }

            // Presentation begins only after every successfully configured child above is a real
            // map pawn. Failed child placements are excluded and never gain a decorative proxy.
            if (committedChildren.Count > 0)
                PlaySplitVfxFailSoft(map, origin, committedChildren);
        }


        private static void PlayRecombinationVfxFailSoft(Pawn upgraded)
        {
            try
            {
                if (upgraded == null || !upgraded.Spawned || upgraded.Map == null)
                    return;

                // Bright convergence at the replacement body plus dense electrical chatter.
                FleckMaker.ThrowLightningGlow(upgraded.DrawPos, upgraded.Map, 2.2f);
                for (int i = 0; i < 7; i++)
                    FleckMaker.ThrowMicroSparks(upgraded.DrawPos, upgraded.Map);
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Replicator recombination VFX failed after commit: " + ex.Message);
            }
        }

        private static void PlaySplitVfxFailSoft(Map map, IntVec3 origin, List<Pawn> committedChildren)
        {
            try
            {
                if (map == null || !origin.IsValid || !origin.InBounds(map) || committedChildren == null)
                    return;

                // The destroyed form visibly disassembles at its last real cell, then each exact
                // split-born child receives its own electrical materialization chatter.
                FleckMaker.ThrowLightningGlow(origin.ToVector3Shifted(), map, 1.35f);
                for (int i = 0; i < 4; i++)
                    FleckMaker.ThrowMicroSparks(origin.ToVector3Shifted(), map);

                foreach (Pawn child in committedChildren)
                {
                    if (child == null || !child.Spawned || child.Map != map)
                        continue;
                    FleckMaker.ThrowMicroSparks(child.DrawPos, map);
                    FleckMaker.ThrowMicroSparks(child.DrawPos, map);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Replicator split VFX failed after commit: " + ex.Message);
            }
        }

        private static void PlaySoundFailSoft(string defName, Thing target, Map fallbackMap, IntVec3 fallbackPosition)
        {
            try
            {
                Map map = target?.Map ?? fallbackMap;
                IntVec3 position = target != null && target.Spawned ? target.Position : fallbackPosition;
                if (map != null && position.IsValid && position.InBounds(map))
                    DefDatabase<SoundDef>.GetNamedSilentFail(defName)?.PlayOneShot(new TargetInfo(position, map));
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Replicator presentation sound failed: " + ex.Message);
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref nextAssemblyTick, "wngHierarchyNextAssemblyTick", 0);
            Scribe_Values.Look(ref recombinationLockedUntilTick, "wngHierarchyRecombinationLockedUntil", 0);
            Scribe_Values.Look(ref splitEmitted, "wngHierarchySplitEmitted", false);
            Scribe_Values.Look(ref lastKnownPosition, "wngHierarchyLastKnownPosition", IntVec3.Invalid);
            Scribe_References.Look(ref lastKnownFaction, "wngHierarchyLastKnownFaction");
        }
    }
}
