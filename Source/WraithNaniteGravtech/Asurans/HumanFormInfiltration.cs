using System;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Def-owned tuning for one cover variant. Cover is only the visible social allegiance of
    /// the exact operative pawn. The synthetic body, genes, memories, injuries and all other
    /// pawn identity remain on that same object throughout concealment and reveal.
    /// </summary>
    public sealed class HumanFormInfiltrationSettingsExtension : DefModExtension
    {
        public string coverFactionDefName;
        public bool useFactionlessCover;
        public int maximumCoverTicks = 30000;
        public int targetObservationTicks = 2000;
        public int closeInspectionTicks = 720;
        public float closeInspectionRadius = 3f;
        public int inspectionSkillThreshold = 6;
        public int suspiciousWitnessTicks = 600;
        public float suspiciousWitnessRadius = 8f;
        public int patternArchiveExposureTicks = 1000;
        public float patternArchiveSecurityRadius = 12f;
    }

    internal static class HumanFormInfiltrationUtility
    {
        public const string HostileFactionDefName = "WNG_PrecursorCollective";
        public const string HumanFormKindDefName = "WNG_HumanFormInfiltrator";

        public static bool IsUnderCover(Pawn pawn)
        {
            return pawn?.Map != null
                && pawn.Map.GetComponent<MapComponent_HumanFormInfiltration>()?.IsUnderCover(pawn) == true;
        }

        public static float InjurySeverity(Pawn pawn)
        {
            return pawn?.health?.hediffSet?.hediffs
                ?.OfType<Hediff_Injury>()
                .Where(injury => injury != null && injury.Severity > 0f)
                .Sum(injury => injury.Severity) ?? 0f;
        }

        public static HumanFormInfiltrationSettingsExtension SettingsFor(string incidentDefName)
        {
            IncidentDef incident = string.IsNullOrEmpty(incidentDefName)
                ? null
                : DefDatabase<IncidentDef>.GetNamedSilentFail(incidentDefName);
            return incident?.GetModExtension<HumanFormInfiltrationSettingsExtension>()
                ?? new HumanFormInfiltrationSettingsExtension();
        }
    }

    /// <summary>
    /// Spawns one exact human-form Replicator operative. The primary incident uses a non-hostile
    /// Quiet Lattice faction cover; the secondary incident uses a factionless traveller cover.
    /// Reveal never replaces the pawn: it restores that object's true Lattice Collective allegiance.
    /// </summary>
    public sealed class IncidentWorker_HumanFormInfiltration : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (!(parms.target is Map map) || !map.IsPlayerHome)
                return false;

            MapComponent_HumanFormInfiltration component = map.GetComponent<MapComponent_HumanFormInfiltration>();
            if (component == null || component.HasActiveMission)
                return false;

            FactionDef hostileDef = DefDatabase<FactionDef>.GetNamedSilentFail(HumanFormInfiltrationUtility.HostileFactionDefName);
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(HumanFormInfiltrationUtility.HumanFormKindDefName);
            Faction hostileFaction = hostileDef == null ? null : Find.FactionManager.FirstFactionOfDef(hostileDef);
            if (hostileFaction == null || kind == null)
                return false;

            HumanFormInfiltrationSettingsExtension settings = def.GetModExtension<HumanFormInfiltrationSettingsExtension>();
            if (settings != null && !settings.useFactionlessCover)
            {
                Faction coverFaction = ResolveCoverFaction(settings);
                if (coverFaction == null || coverFaction.HostileTo(Faction.OfPlayer))
                    return false;
            }

            return base.CanFireNowSub(parms);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            if (!(parms.target is Map map) || !map.IsPlayerHome)
                return false;

            MapComponent_HumanFormInfiltration component = map.GetComponent<MapComponent_HumanFormInfiltration>();
            if (component == null || component.HasActiveMission)
                return false;

            FactionDef hostileDef = DefDatabase<FactionDef>.GetNamedSilentFail(HumanFormInfiltrationUtility.HostileFactionDefName);
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(HumanFormInfiltrationUtility.HumanFormKindDefName);
            Faction hostileFaction = hostileDef == null ? null : Find.FactionManager.FirstFactionOfDef(hostileDef);
            if (hostileFaction == null || kind == null)
                return false;

            HumanFormInfiltrationSettingsExtension settings = def.GetModExtension<HumanFormInfiltrationSettingsExtension>()
                ?? new HumanFormInfiltrationSettingsExtension();
            Faction coverFaction = settings.useFactionlessCover ? null : ResolveCoverFaction(settings);
            if (!settings.useFactionlessCover && (coverFaction == null || coverFaction.HostileTo(Faction.OfPlayer)))
                return false;

            // Quiet-Lattice cover is generated natively as a Quiet-Lattice pawn. Factionless cover
            // is generated under the true faction and immediately masks only the visible faction.
            Pawn pawn = PawnGenerator.GeneratePawn(kind, coverFaction ?? hostileFaction);
            if (pawn == null)
                return false;

            if (settings.useFactionlessCover && pawn.Faction != null)
                pawn.SetFaction(null);
            RemoveVisibleWeapon(pawn);

            if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 entryCell, map, 0f))
                entryCell = CellFinder.RandomEdgeCell(map);

            try
            {
                GenSpawn.Spawn(pawn, entryCell, map);
            }
            catch (Exception ex)
            {
                if (!pawn.Destroyed)
                    pawn.Destroy(DestroyMode.Vanish);
                Log.Warning("[WNG] Human-form infiltration failed before spawn commit: " + ex.Message);
                return false;
            }

            if (!component.TryBeginMission(pawn, def.defName))
            {
                if (!pawn.Destroyed)
                    pawn.Destroy(DestroyMode.Vanish);
                return false;
            }

            // Exact spawned pawn + registered mission is the incident commit. Letter/UI failures
            // cannot make Storyteller retry and duplicate an already-present covert operative.
            try
            {
                SendStandardLetter(def.letterLabel, def.letterText, def.letterDef, parms, pawn);
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Human-form infiltration committed but arrival presentation failed: " + ex.Message);
            }
            return true;
        }

        private static Faction ResolveCoverFaction(HumanFormInfiltrationSettingsExtension settings)
        {
            if (settings == null || string.IsNullOrEmpty(settings.coverFactionDefName))
                return null;
            FactionDef coverDef = DefDatabase<FactionDef>.GetNamedSilentFail(settings.coverFactionDefName);
            return coverDef == null ? null : Find.FactionManager.FirstFactionOfDef(coverDef);
        }

        private static void RemoveVisibleWeapon(Pawn pawn)
        {
            ThingWithComps primary = pawn?.equipment?.Primary;
            if (primary != null && !primary.Destroyed)
                primary.Destroy(DestroyMode.Vanish);
        }
    }

    /// <summary>
    /// One map-scoped covert mission at a time. Detection is intentionally independent from the
    /// ordinary Collective Link and Neural Interface. Pattern Archive authentication is not used
    /// here until that later progression layer actually exists in clean WNGv1.
    /// </summary>
    public sealed class MapComponent_HumanFormInfiltration : MapComponent
    {
        private Pawn agent;
        private Thing scoutTarget;
        private string incidentDefName;
        private int covertTicks;
        private int observationTicks;
        private int inspectionTicks;
        private int suspiciousWitnessTicks;
        private int archiveExposureTicks;
        private int nextMoveOrderTick;
        private float initialInjurySeverity;
        private bool revealed;

        public MapComponent_HumanFormInfiltration(Map map) : base(map) { }

        public bool HasActiveMission => agent != null && !agent.Dead && agent.Spawned;

        public bool IsUnderCover(Pawn pawn)
        {
            return pawn != null && pawn == agent && !revealed && !pawn.Dead && pawn.Spawned;
        }

        public bool TryBeginMission(Pawn pawn, string sourceIncidentDefName)
        {
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map != map || HasActiveMission)
                return false;

            agent = pawn;
            scoutTarget = null;
            incidentDefName = sourceIncidentDefName;
            covertTicks = 0;
            observationTicks = 0;
            inspectionTicks = 0;
            suspiciousWitnessTicks = 0;
            archiveExposureTicks = 0;
            nextMoveOrderTick = 0;
            initialInjurySeverity = HumanFormInfiltrationUtility.InjurySeverity(pawn);
            revealed = false;
            return true;
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (agent == null)
                return;

            if (agent.Dead)
            {
                if (!revealed)
                    ExposePostmortem();
                else
                    ClearMission();
                return;
            }

            if (!agent.Spawned || agent.Map != map)
            {
                ClearMission();
                return;
            }

            if (revealed)
                return;

            if (agent.Downed)
            {
                Reveal("The visitor's nanite masking collapsed when the synthetic body was disabled.");
                return;
            }
            if (agent.IsPrisonerOfColony)
            {
                Reveal("Restraint and close examination exposed hostile Lattice Collective control keys beneath the visitor's cover identity.");
                return;
            }
            if (HumanFormInfiltrationUtility.InjurySeverity(agent) > initialInjurySeverity + 0.05f)
            {
                Reveal("Physical trauma destabilized the visitor's surface mimicry and exposed a hostile nanite command lattice.");
                return;
            }
            if (AsuranCollectiveUtility.IsDisrupted(agent))
            {
                Reveal("EMP disruption collapsed the visitor's coordinated masking and exposed a Lattice Collective infiltrator.");
                return;
            }

            if (!agent.IsHashIntervalTick(60))
                return;

            HumanFormInfiltrationSettingsExtension settings = HumanFormInfiltrationUtility.SettingsFor(incidentDefName);
            covertTicks += 60;

            UpdateCloseInspection(settings);
            if (inspectionTicks >= Math.Max(60, settings.closeInspectionTicks))
            {
                Reveal("A sustained close inspection found synthetic nanite structures and hostile Lattice Collective command signatures beneath the cover identity.");
                return;
            }

            UpdatePatternArchiveAuthentication(settings);
            if (archiveExposureTicks >= Math.Max(60, settings.patternArchiveExposureTicks))
            {
                Reveal("A powered Asuran Pattern Archive authenticated the visitor's lattice traffic and exposed hostile Lattice Collective command keys beneath the cover identity.");
                return;
            }

            EnsureScoutTarget();
            UpdateScouting(settings);

            if (suspiciousWitnessTicks >= Math.Max(60, settings.suspiciousWitnessTicks))
            {
                Reveal("Colonists caught the visitor repeatedly loitering around sensitive infrastructure and exposed the hostile lattice hidden beneath the cover identity.");
                return;
            }

            if (observationTicks >= Math.Max(60, settings.targetObservationTicks))
            {
                Reveal("The visitor completed a covert survey of colony infrastructure and dropped the disguise to attack the mapped target.");
                return;
            }

            if (covertTicks >= Math.Max(6000, settings.maximumCoverTicks))
                Reveal("The visitor's cover window ended as a concealed Lattice uplink activated. The operative is now openly hostile.");
        }

        private void UpdateCloseInspection(HumanFormInfiltrationSettingsExtension settings)
        {
            float radius = Math.Max(1f, settings.closeInspectionRadius);
            float radiusSquared = radius * radius;
            int threshold = Math.Max(0, settings.inspectionSkillThreshold);

            bool inspected = map.mapPawns.AllPawnsSpawned.Any(pawn =>
                pawn != null
                && !pawn.Dead
                && !pawn.Downed
                && pawn.Faction == Faction.OfPlayer
                && pawn.RaceProps != null
                && pawn.RaceProps.Humanlike
                && pawn.Drafted
                && pawn.Position.DistanceToSquared(agent.Position) <= radiusSquared
                && HasInspectionSkill(pawn, threshold));

            inspectionTicks = inspected
                ? inspectionTicks + 60
                : Math.Max(0, inspectionTicks - 30);
        }

        private static bool HasInspectionSkill(Pawn pawn, int threshold)
        {
            if (pawn?.skills == null)
                return false;
            int medicine = pawn.skills.GetSkill(SkillDefOf.Medicine)?.Level ?? 0;
            int intellectual = pawn.skills.GetSkill(SkillDefOf.Intellectual)?.Level ?? 0;
            return Math.Max(medicine, intellectual) >= threshold;
        }


        private void UpdatePatternArchiveAuthentication(HumanFormInfiltrationSettingsExtension settings)
        {
            float radius = Math.Max(1f, settings.patternArchiveSecurityRadius);
            bool authenticated = AsuranCollectiveUtility.HasPoweredArchive(map, Faction.OfPlayer, agent.Position, radius);
            archiveExposureTicks = authenticated
                ? archiveExposureTicks + 60
                : Math.Max(0, archiveExposureTicks - 30);
        }

        private void UpdateScouting(HumanFormInfiltrationSettingsExtension settings)
        {
            if (!(scoutTarget is Building building) || !IsValidScoutTarget(building))
            {
                observationTicks = Math.Max(0, observationTicks - 30);
                suspiciousWitnessTicks = Math.Max(0, suspiciousWitnessTicks - 30);
                return;
            }

            if (agent.Position.DistanceToSquared(building.Position) <= 25)
            {
                observationTicks += 60;
                float witnessRadius = Math.Max(1f, settings.suspiciousWitnessRadius);
                float witnessRadiusSquared = witnessRadius * witnessRadius;
                bool witnessed = map.mapPawns.AllPawnsSpawned.Any(pawn =>
                    pawn != null
                    && !pawn.Dead
                    && !pawn.Downed
                    && pawn.Faction == Faction.OfPlayer
                    && pawn.Position.DistanceToSquared(agent.Position) <= witnessRadiusSquared);
                suspiciousWitnessTicks = witnessed
                    ? suspiciousWitnessTicks + 60
                    : Math.Max(0, suspiciousWitnessTicks - 30);
            }
            else
            {
                observationTicks = Math.Max(0, observationTicks - 30);
                suspiciousWitnessTicks = Math.Max(0, suspiciousWitnessTicks - 30);
                OrderScoutMovement();
            }
        }

        private void EnsureScoutTarget()
        {
            if (scoutTarget is Building current && IsValidScoutTarget(current))
                return;

            scoutTarget = map.listerThings.AllThings
                .OfType<Building>()
                .Where(IsValidScoutTarget)
                .OrderByDescending(ScoutValue)
                .ThenBy(building => building.Position.DistanceToSquared(agent.Position))
                .FirstOrDefault();
            observationTicks = 0;
            suspiciousWitnessTicks = 0;
        }

        private bool IsValidScoutTarget(Building building)
        {
            if (building == null || building.Destroyed || !building.Spawned || building.Faction != Faction.OfPlayer)
                return false;
            if (map.areaManager?.Home == null || !map.areaManager.Home[building.Position])
                return false;
            return agent.CanReach(building, PathEndMode.Touch, Danger.Some);
        }

        private static float ScoutValue(Building building)
        {
            if (building == null)
                return 0f;
            string defName = (building.def?.defName ?? string.Empty).ToLowerInvariant();
            float score = Math.Max(0f, building.MarketValue) * 0.025f;
            if (building.TryGetComp<CompPowerTrader>() != null)
                score += 30f;
            if (building is Building_WorkTable)
                score += 22f;
            foreach (string token in new[] { "research", "comms", "reactor", "battery", "shield", "turret", "archive", "fabricator", "grav", "stargate", "console" })
                if (defName.Contains(token))
                    score += 35f;
            return score;
        }

        private void OrderScoutMovement()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (agent?.jobs == null || scoutTarget == null || now < nextMoveOrderTick)
                return;

            nextMoveOrderTick = SafeFutureTick(now, 900);
            IntVec3 observationCell = CellFinder.RandomClosewalkCellNear(scoutTarget.Position, map, 4);
            if (!observationCell.IsValid || !agent.CanReach(observationCell, PathEndMode.OnCell, Danger.Some))
                return;

            Job job = new Job(JobDefOf.Goto, observationCell)
            {
                expiryInterval = 1500,
                checkOverrideOnExpire = true
            };
            agent.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        private void Reveal(string reason)
        {
            if (revealed || agent == null || agent.Dead || !agent.Spawned)
                return;

            FactionDef hostileDef = DefDatabase<FactionDef>.GetNamedSilentFail(HumanFormInfiltrationUtility.HostileFactionDefName);
            Faction hostileFaction = hostileDef == null ? null : Find.FactionManager.FirstFactionOfDef(hostileDef);
            if (hostileFaction == null)
                return;

            // Faction restoration is the functional reveal commit. Persist revealed only after the
            // exact pawn successfully changes allegiance; otherwise future ticks may retry safely.
            try
            {
                if (agent.Faction != hostileFaction)
                    agent.SetFaction(hostileFaction);
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Human-form infiltrator reveal could not restore hostile faction; state retained for retry: " + ex.Message);
                return;
            }
            revealed = true;

            try
            {
                Find.LetterStack.ReceiveLetter(
                    "Human-form infiltrator exposed",
                    reason + "\n\nThe same pawn has reverted to its true Lattice Collective allegiance. No replacement pawn was created. Until dedicated precursor combat equipment is rebuilt, the infiltrator retains its current physical body and attacks with whatever capabilities that body actually has.",
                    LetterDefOf.ThreatBig,
                    agent);
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Human-form infiltrator reveal committed but letter presentation failed: " + ex.Message);
            }

            if (agent.jobs != null)
            {
                Thing target = scoutTarget != null && !scoutTarget.Destroyed && scoutTarget.Spawned
                    ? scoutTarget
                    : FindNearestPlayerPawn();
                if (target != null)
                {
                    Job attack = new Job(JobDefOf.AttackStatic, target)
                    {
                        expiryInterval = 2400,
                        maxNumStaticAttacks = 4,
                        endIfCantShootTargetFromCurPos = false
                    };
                    agent.jobs.TryTakeOrderedJob(attack, JobTag.Misc);
                }
            }
        }

        private Pawn FindNearestPlayerPawn()
        {
            return map.mapPawns.AllPawnsSpawned
                .Where(pawn => pawn != null && !pawn.Dead && pawn.Faction == Faction.OfPlayer)
                .OrderBy(pawn => pawn.Position.DistanceToSquared(agent.Position))
                .FirstOrDefault();
        }

        private void ExposePostmortem()
        {
            Pawn deadAgent = agent;
            ClearMission();
            try
            {
                Find.LetterStack.ReceiveLetter(
                    "Covert lattice exposed postmortem",
                    "The visitor's death exposed a concealed human-form Replicator lattice keyed to the hostile Lattice Collective. Future operatives can also be exposed through injury, EMP disruption, restraint, deliberate close inspection, or by catching them surveying sensitive colony infrastructure.",
                    LetterDefOf.PositiveEvent,
                    new TargetInfo(map.Center, map));
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Human-form infiltrator postmortem mission clear committed but presentation failed: " + ex.Message);
            }
        }

        private static int SafeFutureTick(int now, int delay)
        {
            long result = (long)Math.Max(0, now) + Math.Max(0, delay);
            return result >= int.MaxValue ? int.MaxValue : (int)result;
        }

        private void ClearMission()
        {
            agent = null;
            scoutTarget = null;
            incidentDefName = null;
            covertTicks = 0;
            observationTicks = 0;
            inspectionTicks = 0;
            suspiciousWitnessTicks = 0;
            archiveExposureTicks = 0;
            nextMoveOrderTick = 0;
            initialInjurySeverity = 0f;
            revealed = false;
        }

        private void NormalizeLoadedState()
        {
            if (agent == null)
            {
                ClearMission();
                return;
            }

            HumanFormInfiltrationSettingsExtension settings = HumanFormInfiltrationUtility.SettingsFor(incidentDefName);
            covertTicks = Math.Max(0, Math.Min(Math.Max(6000, settings.maximumCoverTicks), covertTicks));
            observationTicks = Math.Max(0, Math.Min(Math.Max(60, settings.targetObservationTicks), observationTicks));
            inspectionTicks = Math.Max(0, Math.Min(Math.Max(60, settings.closeInspectionTicks), inspectionTicks));
            suspiciousWitnessTicks = Math.Max(0, Math.Min(Math.Max(60, settings.suspiciousWitnessTicks), suspiciousWitnessTicks));
            archiveExposureTicks = Math.Max(0, Math.Min(Math.Max(60, settings.patternArchiveExposureTicks), archiveExposureTicks));
            if (nextMoveOrderTick < 0)
                nextMoveOrderTick = 0;
            initialInjurySeverity = Math.Max(0f, initialInjurySeverity);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref agent, "wngHumanFormCovertAgent");
            Scribe_References.Look(ref scoutTarget, "wngHumanFormCovertScoutTarget");
            Scribe_Values.Look(ref incidentDefName, "wngHumanFormCovertIncidentDef", null);
            Scribe_Values.Look(ref covertTicks, "wngHumanFormCovertTicks", 0);
            Scribe_Values.Look(ref observationTicks, "wngHumanFormObservationTicks", 0);
            Scribe_Values.Look(ref inspectionTicks, "wngHumanFormInspectionTicks", 0);
            Scribe_Values.Look(ref suspiciousWitnessTicks, "wngHumanFormSuspiciousWitnessTicks", 0);
            Scribe_Values.Look(ref archiveExposureTicks, "wngHumanFormArchiveExposureTicks", 0);
            Scribe_Values.Look(ref nextMoveOrderTick, "wngHumanFormNextMoveOrderTick", 0);
            Scribe_Values.Look(ref initialInjurySeverity, "wngHumanFormInitialInjurySeverity", 0f);
            Scribe_Values.Look(ref revealed, "wngHumanFormCoverRevealed", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                NormalizeLoadedState();
        }
    }
}
