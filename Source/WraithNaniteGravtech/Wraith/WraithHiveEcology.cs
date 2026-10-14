using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Shared local-Hive helpers. This layer deliberately does not talk to WraithStrategicHungerRegistry:
    /// ordinary feeding stock inside a mature Hive is a local biological ecology, not a faction request.
    /// </summary>
    public static class WraithHiveEcologyUtility
    {
        public static bool IsWraithFaction(Faction faction)
        {
            // One authority for lineage identity. Do not duplicate DefName lists here: the old
            // WNG_WraithBrood/WNG_WraithExiles names survived one rebuild and silently excluded
            // Sable Brood/Pale Covenant from Mature-Hive behavior.
            return WraithLineageUtility.IsWraithLineage(faction);
        }

        public static bool IsWraith(Pawn pawn)
        {
            return pawn?.genes?.Xenotype?.defName == "WNG_Wraith";
        }

        public static bool IsKeeperOrQueen(Pawn pawn, Faction faction = null)
        {
            if (pawn == null || pawn.Dead || !pawn.Spawned || !IsWraith(pawn))
                return false;
            if (faction != null && pawn.Faction != faction)
                return false;

            string kind = pawn.kindDef?.defName;
            return kind == "WNG_WraithKeeper" || kind == "WNG_WraithQueen";
        }

        public static bool HasKeeperOrQueen(Map map, Faction faction)
        {
            return map != null && map.mapPawns.AllPawnsSpawned.Any(p => IsKeeperOrQueen(p, faction));
        }

        public static bool IsValidFeedingStock(Pawn pawn, Faction hostFaction = null)
        {
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.RaceProps == null)
                return false;
            if (!pawn.RaceProps.Humanlike || !pawn.RaceProps.IsFlesh || pawn.RaceProps.IsMechanoid || IsWraith(pawn))
                return false;
            if (HoffanSerumUtility.HasProtection(pawn))
                return false;
            if (hostFaction != null && (pawn.guest == null || !pawn.guest.IsPrisoner || pawn.guest.HostFaction != hostFaction))
                return false;
            return true;
        }

        public static int CountResource(Map map, ThingDef def)
        {
            if (map == null || def == null)
                return 0;

            int total = 0;
            foreach (Thing thing in map.listerThings.ThingsOfDef(def))
            {
                if (thing != null && !thing.Destroyed)
                    total += Math.Max(0, thing.stackCount);
            }
            return total;
        }

        public static bool TryConsumeResource(Map map, ThingDef def, int count)
        {
            if (count <= 0)
                return true;
            if (map == null || def == null || CountResource(map, def) < count)
                return false;

            int remaining = count;
            List<Thing> stacks = map.listerThings.ThingsOfDef(def)
                .Where(t => t != null && !t.Destroyed)
                .OrderBy(t => t.stackCount)
                .ToList();

            foreach (Thing stack in stacks)
            {
                if (remaining <= 0)
                    break;

                int take = Math.Min(remaining, stack.stackCount);
                if (take >= stack.stackCount)
                {
                    remaining -= stack.stackCount;
                    stack.Destroy(DestroyMode.Vanish);
                }
                else
                {
                    Thing split = stack.SplitOff(take);
                    remaining -= take;
                    split.Destroy(DestroyMode.Vanish);
                }
            }

            return remaining <= 0;
        }

        // Keep this established public signature stable for external WNG integrations.
        public static void SpawnResource(Map map, IntVec3 near, ThingDef def, int count)
        {
            TrySpawnResource(map, near, def, count);
        }

        internal static bool TrySpawnResource(Map map, IntVec3 near, ThingDef def, int count)
        {
            if (map == null || def == null || count <= 0)
                return false;

            int remaining = count;
            int stackLimit = Math.Max(1, def.stackLimit);
            while (remaining > 0)
            {
                int batch = Math.Min(remaining, stackLimit);
                Thing thing = ThingMaker.MakeThing(def);
                thing.stackCount = batch;

                bool placed = GenPlace.TryPlaceThing(thing, near, map, ThingPlaceMode.Near);
                if (!placed && near != map.Center)
                    placed = GenPlace.TryPlaceThing(thing, map.Center, map, ThingPlaceMode.Near);

                if (!placed)
                {
                    if (!thing.Destroyed && !thing.Spawned && thing.ParentHolder == null)
                        thing.Destroy(DestroyMode.Vanish);
                    return false;
                }

                remaining -= batch;
            }
            return true;
        }

        public static Gene_Resource_LifeForce FindChargedDonor(Map map, Faction faction, float minimumValue)
        {
            if (map == null || faction == null)
                return null;

            return map.mapPawns.AllPawnsSpawned
                .Where(p => p != null && !p.Dead && !p.Downed && p.Faction == faction && IsWraith(p))
                .Select(p => p.genes?.GetFirstGeneOfType<Gene_Resource_LifeForce>())
                .Where(g => g != null && g.Active && g.Value >= minimumValue)
                .OrderByDescending(g => g.Value)
                .FirstOrDefault();
        }

        public static List<Gene_Resource_LifeForce> HungryWraiths(Map map, Faction faction, float belowValue)
        {
            if (map == null || faction == null)
                return new List<Gene_Resource_LifeForce>();

            return map.mapPawns.AllPawnsSpawned
                .Where(p => p != null && !p.Dead && p.Faction == faction && IsWraith(p))
                .Select(p => p.genes?.GetFirstGeneOfType<Gene_Resource_LifeForce>())
                .Where(g => g != null && g.Active && !g.IsHibernating && g.Value < belowValue)
                .OrderBy(g => g.Value)
                .ToList();
        }

        public static void AdjustBiologicalAge(Pawn pawn, long yearsDelta)
        {
            const long ticksPerYear = 3600000L;
            if (pawn?.ageTracker == null || yearsDelta == 0L)
                return;

            long current = pawn.ageTracker.AgeBiologicalTicks;
            long next;
            try
            {
                checked { next = current + yearsDelta * ticksPerYear; }
            }
            catch (OverflowException)
            {
                next = yearsDelta > 0 ? long.MaxValue : 0L;
            }
            pawn.ageTracker.AgeBiologicalTicks = Math.Max(0L, next);
        }

        public static void RefreshHediff(Pawn pawn, string defName)
        {
            if (pawn?.health?.hediffSet == null || defName.NullOrEmpty())
                return;

            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
            if (def == null)
                return;

            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(def);
            if (existing != null)
                pawn.health.RemoveHediff(existing);
            pawn.health.AddHediff(def);
        }

        public static void EnsureHibernating(Pawn pawn, bool hibernating)
        {
            if (pawn?.health?.hediffSet == null)
                return;

            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail("WNG_WraithHibernating");
            if (def == null)
                return;

            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(def);
            if (hibernating && existing == null)
                pawn.health.AddHediff(def);
            else if (!hibernating && existing != null)
                pawn.health.RemoveHediff(existing);
        }

        public static void PutPawnInBed(Pawn pawn, Building_Bed bed, Faction prisonerHost = null)
        {
            if (pawn == null || pawn.Dead || bed == null || !bed.Spawned || pawn.Map != bed.Map || pawn.jobs == null)
                return;

            if (prisonerHost != null)
            {
                bed.ForOwnerType = BedOwnerType.Prisoner;
                if (pawn.guest != null && (!pawn.guest.IsPrisoner || pawn.guest.HostFaction != prisonerHost))
                    pawn.guest.SetGuestStatus(prisonerHost, GuestStatus.Prisoner);
            }

            if (pawn.jobs.curJob != null && pawn.jobs.curJob.def == JobDefOf.LayDown && pawn.jobs.curJob.targetA.Thing == bed)
                return;

            Job job = JobMaker.MakeJob(JobDefOf.LayDown, bed);
            job.expiryInterval = 60000;
            job.checkOverrideOnExpire = false;
            pawn.jobs.StartJob(job, JobCondition.InterruptForced, null, false, true, null, JobTag.Misc);
        }

        public static Building FindBuilding(Map map, Faction faction, ThingDef def, IntVec3 center, float radius)
        {
            if (map == null || faction == null || def == null)
                return null;
            float radiusSq = radius * radius;
            return map.listerThings.ThingsOfDef(def)
                .OfType<Building>()
                .Where(b => b != null && !b.Destroyed && b.Spawned && b.Faction == faction && b.Position.DistanceToSquared(center) <= radiusSq)
                .OrderBy(b => b.Position.DistanceToSquared(center))
                .FirstOrDefault();
        }

        public static Building SpawnBuildingNear(Map map, ThingDef def, IntVec3 near, Faction faction, int radius)
        {
            if (map == null || def == null || faction == null)
                return null;

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(near, Math.Max(1, radius), true).InRandomOrder())
            {
                CellRect rect = GenAdj.OccupiedRect(cell, Rot4.North, def.Size);
                bool valid = true;
                foreach (IntVec3 occupied in rect.Cells)
                {
                    if (!occupied.InBounds(map) || occupied.GetEdifice(map) != null || !occupied.Standable(map))
                    {
                        valid = false;
                        break;
                    }
                }
                if (!valid)
                    continue;

                Building building = ThingMaker.MakeThing(def) as Building;
                if (building == null)
                    return null;
                building.SetFaction(faction);
                GenSpawn.Spawn(building, cell, map, Rot4.North);
                return building;
            }
            return null;
        }
    }

    public sealed class CompProperties_WraithMatureHive : CompProperties
    {
        public int checkIntervalTicks = 250;
        public int localFeedCycleTicks = 120000;
        public int localFeedVictimAgeYears = 2;
        public float localFeedLifeForceGain = 0.12f;
        public float localFeedOnlyBelow = 0.85f;
        public int maxWraithsFedPerCycle = 3;

        public int replacementRetryTicks = 30000;
        public float replacementDonorMinimumLifeForce = 0.55f;
        public float replacementDonorLifeForceCost = 0.25f;
        public float newbornLifeForce = 0.35f;

        public int dormantWakeLossThreshold = 2;
        public float dormantWakeRadius = 18f;

        public int heartPulseTicks = 600;
        public int heartBiomassCost = 1;
        public int heartHealPerTarget = 7;
        public int heartMaxTargets = 4;
        public float heartRadius = 24f;

        public int startingBiomassMin = 480;
        public int startingBiomassMax = 620;

        public CompProperties_WraithMatureHive()
        {
            compClass = typeof(CompWraithMatureHive);
        }
    }

    /// <summary>
    /// Local mature-Hive ecology. It owns exact local pawns/references, a finite founding population,
    /// finite local feeding stock, real occupied hibernation pods, and resource-funded loss replacement.
    /// It never opens strategic faction feeding requests and never replenishes local captives for free.
    /// </summary>
    public sealed class CompWraithMatureHive : ThingComp
    {
        private bool initialized;
        private Faction hiveFaction;
        private Building growthChamber;
        private List<Pawn> populationMembers = new List<Pawn>();
        private List<Pawn> dormantMembers = new List<Pawn>();
        private List<Building_Bed> dormantPods = new List<Building_Bed>();
        private List<Pawn> feedingStock = new List<Pawn>();
        private List<Building_Bed> feedingNiches = new List<Building_Bed>();

        private int foundingPopulationCap;
        private int foundingActiveCount;
        private bool dormantCohortReleased;
        private int nextFeedTick = -1;
        private int nextReplacementAttemptTick = -1;
        private int nextHeartPulseTick = -1;
        private string activeReplacementKindDefName;
        private int replacementFinishTick = -1;
        private int replacementSerial;
        private int lastHeartTargetsHealed;

        public CompProperties_WraithMatureHive Props => (CompProperties_WraithMatureHive)props;

        public int LivingPopulation => populationMembers.Count(IsLivingHiveMember);
        public int LivingDormantPopulation => dormantCohortReleased ? 0 : dormantMembers.Count(IsLivingHiveMember);
        public int LivingFeedingStock => feedingStock.Count(p => WraithHiveEcologyUtility.IsValidFeedingStock(p, hiveFaction));
        public bool Initialized => initialized;
        private bool ReplacementActive => !activeReplacementKindDefName.NullOrEmpty() && replacementFinishTick >= 0;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (respawningAfterLoad || initialized || !parent.Spawned || parent.Map == null || parent.Faction == null)
                return;
            if (parent.Faction == Faction.OfPlayer || !WraithHiveEcologyUtility.IsWraithFaction(parent.Faction))
                return;

            InitializeFreshMatureHive();
        }

        private void InitializeFreshMatureHive()
        {
            Map map = parent.Map;
            Faction faction = parent.Faction;
            if (map == null || faction == null)
                return;

            hiveFaction = faction;
            populationMembers.Clear();
            dormantMembers.Clear();
            dormantPods.Clear();
            feedingStock.Clear();
            feedingNiches.Clear();

            ThingDef growthDef = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_WraithGrowthChamber");
            ThingDef podDef = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_WraithHibernationPod");
            ThingDef nicheDef = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_WraithFeedingNiche");
            ThingDef vaultDef = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_HiveDormancyVault");
            ThingDef biomassDef = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_Biomass");
            if (growthDef == null || podDef == null || nicheDef == null || vaultDef == null || biomassDef == null)
                return;

            growthChamber = WraithHiveEcologyUtility.FindBuilding(map, faction, growthDef, parent.Position, 30f)
                ?? WraithHiveEcologyUtility.SpawnBuildingNear(map, growthDef, parent.Position + new IntVec3(8, 0, 0), faction, 8);

            for (int i = 0; i < 4; i++)
            {
                Building_Bed pod = WraithHiveEcologyUtility.SpawnBuildingNear(
                    map,
                    podDef,
                    parent.Position + new IntVec3(-9 + i * 6, 0, -8),
                    faction,
                    5) as Building_Bed;
                if (pod != null)
                    dormantPods.Add(pod);
            }

            for (int i = 0; i < 2; i++)
            {
                Building_Bed niche = WraithHiveEcologyUtility.SpawnBuildingNear(
                    map,
                    nicheDef,
                    parent.Position + new IntVec3(-5 + i * 10, 0, 8),
                    faction,
                    6) as Building_Bed;
                if (niche != null)
                    feedingNiches.Add(niche);
            }

            for (int i = 0; i < 2; i++)
            {
                WraithHiveEcologyUtility.SpawnBuildingNear(
                    map,
                    vaultDef,
                    parent.Position + new IntVec3(-7 + i * 14, 0, -14),
                    faction,
                    6);
            }

            WraithHiveEcologyUtility.SpawnResource(
                map,
                parent.Position,
                biomassDef,
                Rand.RangeInclusive(Math.Max(0, Props.startingBiomassMin), Math.Max(Props.startingBiomassMin, Props.startingBiomassMax)));

            SpawnFoundingPopulation();
            SpawnOrdinaryHibernators();
            SpawnFiniteFeedingStock();

            foundingPopulationCap = LivingPopulation;
            foundingActiveCount = Math.Max(0, foundingPopulationCap - LivingDormantPopulation);
            dormantCohortReleased = LivingDormantPopulation == 0;

            int now = Find.TickManager?.TicksGame ?? 0;
            nextFeedTick = now + Math.Max(1, Props.localFeedCycleTicks);
            nextReplacementAttemptTick = now + Math.Max(1, Props.replacementRetryTicks);
            nextHeartPulseTick = now + Math.Max(1, Props.heartPulseTicks);
            initialized = foundingPopulationCap > 0;
        }

        private void SpawnFoundingPopulation()
        {
            string[] roster =
            {
                "WNG_WraithQueen",
                "WNG_WraithKeeper",
                "WNG_WraithCommander",
                "WNG_WraithWarrior",
                "WNG_WraithHunter",
                "WNG_WraithWarrior",
                "WNG_WraithHunter",
                "WNG_WraithWarrior"
            };

            List<Pawn> spawned = new List<Pawn>();
            foreach (string defName in roster)
            {
                PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(defName);
                if (kind == null)
                    continue;

                Pawn pawn = PawnGenerator.GeneratePawn(kind, hiveFaction);
                IntVec3 cell = CellFinder.RandomClosewalkCellNear(parent.Position, parent.Map, 18);
                GenSpawn.Spawn(pawn, cell, parent.Map);
                populationMembers.Add(pawn);
                spawned.Add(pawn);
            }

            if (spawned.Count > 0)
            {
                Lord lord = LordMaker.MakeNewLord(hiveFaction, new LordJob_DefendBase(hiveFaction, parent.Position, 90000), parent.Map);
                foreach (Pawn pawn in spawned)
                    lord.AddPawn(pawn);
            }
        }

        private void SpawnOrdinaryHibernators()
        {
            string[] roster = { "WNG_WraithHunter", "WNG_WraithWarrior", "WNG_WraithHunter" };
            int count = Math.Min(roster.Length, dormantPods.Count);
            for (int i = 0; i < count; i++)
            {
                PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(roster[i]);
                Building_Bed pod = dormantPods[i];
                if (kind == null || pod == null || !pod.Spawned)
                    continue;

                Pawn pawn = PawnGenerator.GeneratePawn(kind, hiveFaction);
                IntVec3 cell = CellFinder.RandomClosewalkCellNear(pod.InteractionCell, parent.Map, 3);
                GenSpawn.Spawn(pawn, cell, parent.Map);
                Gene_Resource_LifeForce resource = pawn.genes?.GetFirstGeneOfType<Gene_Resource_LifeForce>();
                if (resource != null)
                    resource.Value = Math.Min(resource.Max, 0.45f);
                WraithHiveEcologyUtility.EnsureHibernating(pawn, true);
                WraithHiveEcologyUtility.PutPawnInBed(pawn, pod);
                populationMembers.Add(pawn);
                dormantMembers.Add(pawn);
            }
        }

        private void SpawnFiniteFeedingStock()
        {
            PawnKindDef captiveKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("SpaceRefugee")
                ?? DefDatabase<PawnKindDef>.GetNamedSilentFail("Villager");
            if (captiveKind == null)
                return;

            foreach (Building_Bed niche in feedingNiches)
            {
                if (niche == null || !niche.Spawned)
                    continue;

                Pawn captive = PawnGenerator.GeneratePawn(captiveKind, null);
                if (captive == null || !WraithHiveEcologyUtility.IsValidFeedingStock(captive))
                {
                    captive?.Destroy(DestroyMode.Vanish);
                    continue;
                }

                IntVec3 cell = CellFinder.RandomClosewalkCellNear(niche.InteractionCell, parent.Map, 3);
                GenSpawn.Spawn(captive, cell, parent.Map);
                niche.ForOwnerType = BedOwnerType.Prisoner;
                if (captive.guest != null)
                    captive.guest.SetGuestStatus(hiveFaction, GuestStatus.Prisoner);
                WraithHiveEcologyUtility.PutPawnInBed(captive, niche, hiveFaction);
                feedingStock.Add(captive);
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!initialized || !parent.Spawned || parent.Map == null || hiveFaction == null || parent.Faction != hiveFaction)
                return;
            if (!parent.IsHashIntervalTick(Math.Max(1, Props.checkIntervalTicks)))
                return;

            MaintainDormantPopulation();
            MaintainFeedingStock();

            int now = Find.TickManager?.TicksGame ?? 0;
            if (now >= nextFeedTick)
            {
                RunLocalFeedingCycle();
                nextFeedTick = now + Math.Max(1, Props.localFeedCycleTicks);
            }

            if (now >= nextHeartPulseTick)
            {
                RunHeartRegenerationPulse();
                nextHeartPulseTick = now + Math.Max(1, Props.heartPulseTicks);
            }

            if (ReplacementActive)
            {
                if (!GrowthChamberUsable())
                    ClearReplacementCycle();
                else if (now >= replacementFinishTick)
                    CompleteReplacementCycle();
                return;
            }

            if (LivingPopulation < foundingPopulationCap && now >= nextReplacementAttemptTick)
            {
                nextReplacementAttemptTick = now + Math.Max(1, Props.replacementRetryTicks);
                TryStartReplacementCycle(now);
            }
        }

        private bool IsLivingHiveMember(Pawn pawn)
        {
            return pawn != null && !pawn.Dead && pawn.Faction == hiveFaction;
        }

        private void MaintainDormantPopulation()
        {
            if (dormantCohortReleased)
                return;

            int pairs = Math.Min(dormantMembers.Count, dormantPods.Count);
            bool podFailure = false;
            for (int i = 0; i < pairs; i++)
            {
                Pawn pawn = dormantMembers[i];
                Building_Bed pod = dormantPods[i];
                if (!IsLivingHiveMember(pawn) || !pawn.Spawned)
                    continue;
                if (pod == null || pod.Destroyed || !pod.Spawned)
                {
                    WraithHiveEcologyUtility.EnsureHibernating(pawn, false);
                    podFailure = true;
                    continue;
                }
                WraithHiveEcologyUtility.EnsureHibernating(pawn, true);
                WraithHiveEcologyUtility.PutPawnInBed(pawn, pod);
            }

            int activeLiving = Math.Max(0, LivingPopulation - LivingDormantPopulation);
            bool lossesRequireWake = activeLiving <= Math.Max(1, foundingActiveCount - Math.Max(1, Props.dormantWakeLossThreshold));
            bool playerNearby = PlayerNearDormantInfrastructure();
            if (podFailure || lossesRequireWake || playerNearby)
                ReleaseDormantCohort();
        }

        private bool PlayerNearDormantInfrastructure()
        {
            float radiusSq = Math.Max(1f, Props.dormantWakeRadius) * Math.Max(1f, Props.dormantWakeRadius);
            foreach (Pawn pawn in parent.Map.mapPawns.FreeColonistsSpawned)
            {
                if (pawn == null || pawn.Dead)
                    continue;
                if (pawn.Position.DistanceToSquared(parent.Position) <= radiusSq)
                    return true;
                foreach (Building_Bed pod in dormantPods)
                {
                    if (pod != null && pod.Spawned && pawn.Position.DistanceToSquared(pod.Position) <= radiusSq)
                        return true;
                }
            }
            return false;
        }

        private void ReleaseDormantCohort()
        {
            dormantCohortReleased = true;
            List<Pawn> released = new List<Pawn>();
            foreach (Pawn pawn in dormantMembers)
            {
                if (!IsLivingHiveMember(pawn) || !pawn.Spawned)
                    continue;
                WraithHiveEcologyUtility.EnsureHibernating(pawn, false);
                pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced);
                released.Add(pawn);
            }

            if (released.Count > 0)
            {
                Lord lord = LordMaker.MakeNewLord(hiveFaction, new LordJob_DefendBase(hiveFaction, parent.Position, 90000), parent.Map);
                foreach (Pawn pawn in released)
                    lord.AddPawn(pawn);
            }
        }

        private void MaintainFeedingStock()
        {
            int pairs = Math.Min(feedingStock.Count, feedingNiches.Count);
            for (int i = 0; i < pairs; i++)
            {
                Pawn captive = feedingStock[i];
                Building_Bed niche = feedingNiches[i];
                if (!WraithHiveEcologyUtility.IsValidFeedingStock(captive, hiveFaction) || niche == null || !niche.Spawned)
                    continue;
                WraithHiveEcologyUtility.PutPawnInBed(captive, niche, hiveFaction);
            }
        }

        private void RunLocalFeedingCycle()
        {
            if (!WraithHiveEcologyUtility.HasKeeperOrQueen(parent.Map, hiveFaction))
                return;

            List<Gene_Resource_LifeForce> hungry = WraithHiveEcologyUtility.HungryWraiths(parent.Map, hiveFaction, Props.localFeedOnlyBelow);
            if (hungry.Count == 0)
                return;

            Pawn captive = feedingStock.FirstOrDefault(p => WraithHiveEcologyUtility.IsValidFeedingStock(p, hiveFaction));
            if (captive == null || captive.ageTracker == null || captive.health?.hediffSet == null)
                return;

            int count = Math.Min(Math.Max(1, Props.maxWraithsFedPerCycle), hungry.Count);
            long oldAge = captive.ageTracker.AgeBiologicalTicks;
            HediffDef lifeDrainedDef = DefDatabase<HediffDef>.GetNamedSilentFail("WNG_LifeDrained");
            bool hadLifeDrained = lifeDrainedDef != null && captive.health.hediffSet.HasHediff(lifeDrainedDef);
            float[] oldLifeForce = new float[count];
            for (int i = 0; i < count; i++)
                oldLifeForce[i] = hungry[i].Value;

            try
            {
                // Local feeding is deliberately modest and never invokes the full-feed repeat-kill rule.
                WraithHiveEcologyUtility.AdjustBiologicalAge(captive, Math.Max(0, Props.localFeedVictimAgeYears));
                WraithHiveEcologyUtility.RefreshHediff(captive, "WNG_LifeDrained");
                if (lifeDrainedDef == null || !captive.health.hediffSet.HasHediff(lifeDrainedDef))
                    throw new InvalidOperationException("Mature Hive local feeding did not establish Life Drained.");

                for (int i = 0; i < count; i++)
                    hungry[i].AddLifeForce(Math.Max(0f, Props.localFeedLifeForceGain));
            }
            catch (Exception ex)
            {
                captive.ageTracker.AgeBiologicalTicks = oldAge;
                if (lifeDrainedDef != null)
                {
                    Hediff current = captive.health.hediffSet.GetFirstHediffOfDef(lifeDrainedDef);
                    if (!hadLifeDrained && current != null)
                        captive.health.RemoveHediff(current);
                    else if (hadLifeDrained && current == null)
                        captive.health.AddHediff(lifeDrainedDef);
                }
                for (int i = 0; i < count; i++)
                    hungry[i].Value = oldLifeForce[i];
                Log.Error("[WNG] Mature Hive local feeding failed and was rolled back: " + ex);
            }
        }

        private void RunHeartRegenerationPulse()
        {
            lastHeartTargetsHealed = 0;
            ThingDef biomass = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_Biomass");
            if (biomass == null || parent.Map == null)
                return;

            string[] regenerableDefs =
            {
                "WNG_WraithGrowthChamber",
                "WNG_WraithHibernationPod",
                "WNG_WraithFeedingNiche",
                "WNG_HiveDormancyVault"
            };

            float radiusSq = Math.Max(1f, Props.heartRadius) * Math.Max(1f, Props.heartRadius);
            List<Thing> damaged = new List<Thing>();
            foreach (string defName in regenerableDefs)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                if (def == null)
                    continue;
                damaged.AddRange(parent.Map.listerThings.ThingsOfDef(def)
                    .Where(t => t != null && !t.Destroyed && t.Faction == hiveFaction && t.HitPoints < t.MaxHitPoints && t.Position.DistanceToSquared(parent.Position) <= radiusSq));
            }

            damaged = damaged
                .OrderBy(t => t.HitPoints / (float)Math.Max(1, t.MaxHitPoints))
                .ThenBy(t => t.Position.DistanceToSquared(parent.Position))
                .ToList();
            if (damaged.Count == 0)
                return;

            int biomassCost = Math.Max(0, Props.heartBiomassCost);
            if (!WraithHiveEcologyUtility.TryConsumeResource(parent.Map, biomass, biomassCost))
                return;

            int limit = Math.Min(Math.Max(1, Props.heartMaxTargets), damaged.Count);
            for (int i = 0; i < limit; i++)
            {
                Thing target = damaged[i];
                int heal = Math.Min(Math.Max(1, Props.heartHealPerTarget), target.MaxHitPoints - target.HitPoints);
                if (heal <= 0)
                    continue;
                target.HitPoints += heal;
                lastHeartTargetsHealed++;
            }
        }

        private bool GrowthChamberUsable()
        {
            return growthChamber != null && !growthChamber.Destroyed && growthChamber.Spawned && growthChamber.Map == parent.Map && growthChamber.Faction == hiveFaction;
        }

        private void TryStartReplacementCycle(int now)
        {
            if (!GrowthChamberUsable() || !WraithHiveEcologyUtility.HasKeeperOrQueen(parent.Map, hiveFaction))
                return;

            string kindDefName;
            int biomassCost;
            int durationTicks;
            SelectReplacement(out kindDefName, out biomassCost, out durationTicks);

            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(kindDefName);
            ThingDef biomass = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_Biomass");
            Gene_Resource_LifeForce donor = WraithHiveEcologyUtility.FindChargedDonor(parent.Map, hiveFaction, Props.replacementDonorMinimumLifeForce);
            if (kind == null || biomass == null || donor == null)
                return;
            if (!WraithHiveEcologyUtility.TryConsumeResource(parent.Map, biomass, biomassCost))
                return;
            if (!donor.TrySpend(Math.Max(0f, Props.replacementDonorLifeForceCost)))
            {
                WraithHiveEcologyUtility.SpawnResource(parent.Map, growthChamber.Position, biomass, biomassCost);
                return;
            }

            activeReplacementKindDefName = kindDefName;
            replacementFinishTick = now + Math.Max(1, durationTicks);
        }

        private void SelectReplacement(out string kindDefName, out int biomassCost, out int durationTicks)
        {
            bool keeperAlive = populationMembers.Any(p => IsLivingHiveMember(p) && p.kindDef?.defName == "WNG_WraithKeeper");
            bool queenAlive = populationMembers.Any(p => IsLivingHiveMember(p) && p.kindDef?.defName == "WNG_WraithQueen");
            if (!keeperAlive && queenAlive)
            {
                kindDefName = "WNG_WraithKeeper";
                biomassCost = 280;
                durationTicks = 240000;
                return;
            }

            if ((replacementSerial++ & 1) == 0)
            {
                kindDefName = "WNG_WraithHunter";
                biomassCost = 160;
                durationTicks = 120000;
            }
            else
            {
                kindDefName = "WNG_WraithWarrior";
                biomassCost = 220;
                durationTicks = 180000;
            }
        }

        private void CompleteReplacementCycle()
        {
            if (!ReplacementActive || !GrowthChamberUsable() || LivingPopulation >= foundingPopulationCap)
            {
                ClearReplacementCycle();
                return;
            }

            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(activeReplacementKindDefName);
            if (kind == null)
            {
                ClearReplacementCycle();
                return;
            }

            Pawn clone = null;
            try
            {
                clone = PawnGenerator.GeneratePawn(kind, hiveFaction);
                Gene_Resource_LifeForce resource = clone.genes?.GetFirstGeneOfType<Gene_Resource_LifeForce>();
                if (resource != null)
                    resource.Value = Math.Min(resource.Max, Math.Max(0f, Props.newbornLifeForce));

                IntVec3 spawnCell = CellFinder.RandomClosewalkCellNear(growthChamber.InteractionCell, parent.Map, 5);
                GenSpawn.Spawn(clone, spawnCell, parent.Map);
                if (!clone.Spawned || clone.Map != parent.Map)
                    throw new InvalidOperationException("Mature Hive replacement did not reach the map.");

                populationMembers.Add(clone);
                ClearReplacementCycle();
                nextReplacementAttemptTick = (Find.TickManager?.TicksGame ?? 0) + Math.Max(1, Props.replacementRetryTicks);

                Lord lord = LordMaker.MakeNewLord(hiveFaction, new LordJob_DefendBase(hiveFaction, parent.Position, 90000), parent.Map);
                lord.AddPawn(clone);
            }
            catch (Exception ex)
            {
                // If the paid clone physically reached the map, keep and track it rather than creating
                // a free retry. Otherwise the paid cycle is lost; this is conservative and duplication-safe.
                bool committed = clone != null && clone.Spawned && clone.Map == parent.Map;
                if (committed && !populationMembers.Contains(clone))
                    populationMembers.Add(clone);
                if (committed)
                    ClearReplacementCycle();
                Log.Error("[WNG] Mature Hive replacement completion failed: " + ex);
            }
        }

        private void ClearReplacementCycle()
        {
            activeReplacementKindDefName = null;
            replacementFinishTick = -1;
        }

        public override string CompInspectStringExtra()
        {
            if (!initialized || parent.Map == null)
                return null;

            ThingDef biomass = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_Biomass");
            int biomassCount = WraithHiveEcologyUtility.CountResource(parent.Map, biomass);
            int active = Math.Max(0, LivingPopulation - LivingDormantPopulation);
            string replacement = ReplacementActive
                ? "Replacement gestation: " + activeReplacementKindDefName + ""
                : "Replacement gestation: idle";

            return "Hive population: " + active + " active / " + LivingDormantPopulation + " dormant / " + foundingPopulationCap + " founding cap\n" +
                   "Feeding stock: " + LivingFeedingStock + " exact captives\n" +
                   "Cultured biomass: " + biomassCount + "\n" +
                   replacement + "\n" +
                   "Last heart pulse targets: " + lastHeartTargetsHealed;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref initialized, "wngMatureHiveInitialized", false);
            Scribe_References.Look(ref hiveFaction, "wngMatureHiveFaction");
            Scribe_References.Look(ref growthChamber, "wngMatureHiveGrowthChamber");
            Scribe_Collections.Look(ref populationMembers, "wngMatureHivePopulation", LookMode.Reference);
            Scribe_Collections.Look(ref dormantMembers, "wngMatureHiveDormants", LookMode.Reference);
            Scribe_Collections.Look(ref dormantPods, "wngMatureHiveDormantPods", LookMode.Reference);
            Scribe_Collections.Look(ref feedingStock, "wngMatureHiveFeedingStock", LookMode.Reference);
            Scribe_Collections.Look(ref feedingNiches, "wngMatureHiveFeedingNiches", LookMode.Reference);
            Scribe_Values.Look(ref foundingPopulationCap, "wngMatureHiveFoundingCap", 0);
            Scribe_Values.Look(ref foundingActiveCount, "wngMatureHiveFoundingActive", 0);
            Scribe_Values.Look(ref dormantCohortReleased, "wngMatureHiveDormantsReleased", false);
            Scribe_Values.Look(ref nextFeedTick, "wngMatureHiveNextFeed", -1);
            Scribe_Values.Look(ref nextReplacementAttemptTick, "wngMatureHiveNextReplacementAttempt", -1);
            Scribe_Values.Look(ref nextHeartPulseTick, "wngMatureHiveNextHeartPulse", -1);
            Scribe_Values.Look(ref activeReplacementKindDefName, "wngMatureHiveReplacementKind");
            Scribe_Values.Look(ref replacementFinishTick, "wngMatureHiveReplacementFinish", -1);
            Scribe_Values.Look(ref replacementSerial, "wngMatureHiveReplacementSerial", 0);
            Scribe_Values.Look(ref lastHeartTargetsHealed, "wngMatureHiveLastHeartTargets", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (populationMembers == null) populationMembers = new List<Pawn>();
                if (dormantMembers == null) dormantMembers = new List<Pawn>();
                if (dormantPods == null) dormantPods = new List<Building_Bed>();
                if (feedingStock == null) feedingStock = new List<Pawn>();
                if (feedingNiches == null) feedingNiches = new List<Building_Bed>();
            }
        }
    }

    public sealed class CompProperties_WraithDormancyVault : CompProperties
    {
        public int dormantCount = 4;
        public float triggerRadius = 18f;
        public int awakenIntervalTicks = 180;
        public int awakenPerWave = 2;

        public CompProperties_WraithDormancyVault()
        {
            compClass = typeof(CompWraithDormancyVault);
        }
    }

    /// <summary>
    /// Separate finite combat reserve. Unlike ordinary hibernating population, the vault does not
    /// participate in demographic replacement and can never replenish its own remaining count.
    /// </summary>
    public sealed class CompWraithDormancyVault : ThingComp
    {
        private int remainingDormants = -1;
        private int nextAwakenTick = -1;
        private bool triggered;

        public CompProperties_WraithDormancyVault Props => (CompProperties_WraithDormancyVault)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (!respawningAfterLoad && remainingDormants < 0)
                remainingDormants = Math.Max(0, Props.dormantCount);
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned || parent.Map == null || parent.Faction == null || remainingDormants <= 0 || !parent.IsHashIntervalTick(60))
                return;
            if (!parent.Faction.HostileTo(Faction.OfPlayer))
                return;

            if (!triggered && ShouldAwaken())
            {
                triggered = true;
                nextAwakenTick = Find.TickManager?.TicksGame ?? 0;
            }
            if (!triggered || (Find.TickManager?.TicksGame ?? 0) < nextAwakenTick)
                return;

            AwakenWave();
            nextAwakenTick = (Find.TickManager?.TicksGame ?? 0) + Math.Max(1, Props.awakenIntervalTicks);
        }

        private bool ShouldAwaken()
        {
            if (parent.HitPoints < parent.MaxHitPoints)
                return true;

            float radiusSq = Math.Max(1f, Props.triggerRadius) * Math.Max(1f, Props.triggerRadius);
            return parent.Map.mapPawns.FreeColonistsSpawned.Any(p => p != null && !p.Dead && p.Position.DistanceToSquared(parent.Position) <= radiusSq);
        }

        private void AwakenWave()
        {
            PawnKindDef hunter = DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_WraithHunter");
            PawnKindDef warrior = DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_WraithWarrior");
            if (hunter == null || warrior == null)
                return;

            List<Pawn> awakened = new List<Pawn>();
            int count = Math.Min(Math.Max(1, Props.awakenPerWave), remainingDormants);
            for (int i = 0; i < count; i++)
            {
                PawnKindDef kind = ((remainingDormants + i) % 2 == 0) ? warrior : hunter;
                Pawn pawn = PawnGenerator.GeneratePawn(kind, parent.Faction);
                IntVec3 cell = CellFinder.RandomClosewalkCellNear(parent.Position, parent.Map, 5);
                GenSpawn.Spawn(pawn, cell, parent.Map);
                remainingDormants--;
                awakened.Add(pawn);
            }

            if (awakened.Count > 0)
            {
                Lord lord = LordMaker.MakeNewLord(parent.Faction, new LordJob_DefendBase(parent.Faction, parent.Position, 60000), parent.Map);
                foreach (Pawn pawn in awakened)
                    lord.AddPawn(pawn);
            }
        }

        public override string CompInspectStringExtra()
        {
            return remainingDormants < 0 ? null : "Dormant reserve: " + remainingDormants + (triggered ? " (awakening)" : " (sealed)");
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref remainingDormants, "wngDormancyRemaining", -1);
            Scribe_Values.Look(ref nextAwakenTick, "wngDormancyNextWake", -1);
            Scribe_Values.Look(ref triggered, "wngDormancyTriggered", false);
        }
    }
}
