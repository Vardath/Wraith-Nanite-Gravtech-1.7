using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_ReplicatorThreatResponse : CompProperties
    {
        public int selfDefenseTicks = 900;
        public int swarmAlertTicks = 1800;
        public float swarmResponseChance = 0.24f;
        public float swarmResponseRadius = 40f;

        public CompProperties_ReplicatorThreatResponse()
        {
            compClass = typeof(CompReplicatorThreatResponse);
        }
    }

    /// <summary>
    /// Personal attack memory for an autonomous block Replicator. This does not turn the pawn into a
    /// generic enemy-seeking raider; it only remembers a real pawn aggressor for a bounded period and
    /// publishes a bounded same-domain retaliation alert to the map component.
    /// </summary>
    public sealed class CompReplicatorThreatResponse : ThingComp
    {
        private Pawn lastAggressor;
        private int selfDefenseUntilTick;

        private CompProperties_ReplicatorThreatResponse Props =>
            (CompProperties_ReplicatorThreatResponse)props;

        public override void PostPostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            base.PostPostApplyDamage(dinfo, totalDamageDealt);

            Pawn victim = parent as Pawn;
            Pawn attacker = dinfo.Instigator as Pawn;
            if (victim == null || attacker == null || victim.Dead || !victim.Spawned || victim.Map == null || attacker == victim)
                return;
            if (victim.Faction == null || victim.Faction == Faction.OfPlayer || attacker.Faction == victim.Faction)
                return;
            if (ReplicatorInterferenceUtility.IsEmpDisrupted(victim))
                return;

            int now = Find.TickManager?.TicksGame ?? 0;
            lastAggressor = attacker;
            selfDefenseUntilTick = SafeFutureTick(now, Math.Max(60, Props.selfDefenseTicks));

            victim.Map.GetComponent<MapComponent_ReplicatorThreatResponse>()?.RegisterAttack(
                victim,
                attacker,
                Math.Max(60, Props.swarmAlertTicks),
                Math.Max(0f, Math.Min(1f, Props.swarmResponseChance)),
                Math.Max(1f, Props.swarmResponseRadius));

            if (totalDamageDealt > 0f)
                victim.Map.GetComponent<MapComponent_ReplicatorSwarmBehavior>()?.RegisterDefensePressure(victim, attacker);
        }

        public bool TryGetSelfDefenseTarget(Pawn responder, out Pawn target)
        {
            target = null;
            if (responder == null || responder.Dead || !responder.Spawned || responder.Faction == Faction.OfPlayer)
                return false;
            if (ReplicatorInterferenceUtility.IsEmpDisrupted(responder))
                return false;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (now > selfDefenseUntilTick || lastAggressor == null || lastAggressor.Dead || !lastAggressor.Spawned || lastAggressor.Map != responder.Map)
                return false;
            if (lastAggressor.Faction == responder.Faction || !responder.CanReach(lastAggressor, PathEndMode.Touch, Danger.Deadly))
                return false;

            target = lastAggressor;
            return true;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref lastAggressor, "wngReplicatorLastAggressor");
            Scribe_Values.Look(ref selfDefenseUntilTick, "wngReplicatorSelfDefenseUntil", 0);
        }

        internal static int SafeFutureTick(int now, int delay)
        {
            long result = (long)Math.Max(0, now) + Math.Max(0, delay);
            return result >= int.MaxValue ? int.MaxValue : (int)result;
        }
    }

    public sealed class ReplicatorThreatAlert : IExposable
    {
        public string domainId;
        public ReplicatorControlAuthority authority = ReplicatorControlAuthority.AutonomousSwarm;
        public Pawn aggressor;
        public IntVec3 attackCell = IntVec3.Invalid;
        public int untilTick;
        public int serial;
        public float responseChance;
        public float responseRadiusSq;

        public void ExposeData()
        {
            Scribe_Values.Look(ref domainId, "domainId");
            Scribe_Values.Look(ref authority, "authority", ReplicatorControlAuthority.AutonomousSwarm);
            Scribe_References.Look(ref aggressor, "aggressor");
            Scribe_Values.Look(ref attackCell, "attackCell", IntVec3.Invalid);
            Scribe_Values.Look(ref untilTick, "untilTick", 0);
            Scribe_Values.Look(ref serial, "serial", 0);
            Scribe_Values.Look(ref responseChance, "responseChance", 0f);
            Scribe_Values.Look(ref responseRadiusSq, "responseRadiusSq", 0f);
        }
    }

    /// <summary>
    /// Save-persistent bounded retaliation memory. Free autonomous Replicators share the swarm
    /// warning channel even when they were spawned with separate local domain IDs. Queen/Asuran/
    /// sovereign-controlled blocks remain isolated to their exact controller domain.
    /// </summary>
    public sealed class MapComponent_ReplicatorThreatResponse : MapComponent
    {
        private List<ReplicatorThreatAlert> alerts = new List<ReplicatorThreatAlert>();
        private int nextSerial;

        public MapComponent_ReplicatorThreatResponse(Map map) : base(map)
        {
        }

        public void RegisterAttack(Pawn victim, Pawn attacker, int durationTicks, float responseChance, float responseRadius)
        {
            if (victim == null || attacker == null || victim.Map != map || attacker.Map != map || victim.Faction == null)
                return;

            CompReplicatorDomain victimDomain = ReplicatorDomainUtility.Domain(victim);
            string domainId = victimDomain?.DomainId;
            if (victimDomain == null || string.IsNullOrEmpty(domainId))
                return;

            ReplicatorThreatAlert alert = null;
            for (int i = 0; i < alerts.Count; i++)
            {
                ReplicatorThreatAlert candidate = alerts[i];
                if (candidate == null)
                    continue;

                bool sameAlertDomain =
                    victimDomain.Authority == ReplicatorControlAuthority.AutonomousSwarm
                        ? candidate.authority == ReplicatorControlAuthority.AutonomousSwarm
                        : candidate.authority == victimDomain.Authority &&
                          candidate.domainId == domainId;
                if (sameAlertDomain)
                {
                    alert = candidate;
                    break;
                }
            }

            if (alert == null)
            {
                alert = new ReplicatorThreatAlert { domainId = domainId };
                alerts.Add(alert);
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            alert.domainId = domainId;
            alert.authority = victimDomain.Authority;
            alert.aggressor = attacker;
            alert.attackCell = victim.Position;
            alert.untilTick = CompReplicatorThreatResponse.SafeFutureTick(now, durationTicks);
            alert.responseChance = responseChance;
            alert.responseRadiusSq = responseRadius * responseRadius;
            unchecked { alert.serial = ++nextSerial; }
        }

        public bool TryGetRetaliationTarget(Pawn responder, out Pawn target)
        {
            target = null;
            if (responder == null || responder.Dead || !responder.Spawned || responder.Map != map || responder.Faction == null || responder.Faction == Faction.OfPlayer)
                return false;
            if (ReplicatorInterferenceUtility.IsEmpDisrupted(responder))
                return false;

            int now = Find.TickManager?.TicksGame ?? 0;
            CompReplicatorDomain responderDomain = ReplicatorDomainUtility.Domain(responder);
            string responderDomainId = responderDomain?.DomainId;
            if (responderDomain == null || string.IsNullOrEmpty(responderDomainId))
                return false;
            for (int i = alerts.Count - 1; i >= 0; i--)
            {
                ReplicatorThreatAlert alert = alerts[i];
                if (alert == null || now > alert.untilTick || alert.aggressor == null || alert.aggressor.Dead || !alert.aggressor.Spawned || alert.aggressor.Map != map)
                {
                    alerts.RemoveAt(i);
                    continue;
                }
                bool matchingControl =
                    responderDomain.Authority == ReplicatorControlAuthority.AutonomousSwarm
                        ? alert.authority == ReplicatorControlAuthority.AutonomousSwarm
                        : alert.authority == responderDomain.Authority &&
                          alert.domainId == responderDomainId;
                if (!matchingControl || alert.aggressor.Faction == responder.Faction)
                    continue;
                if (responder.Position.DistanceToSquared(alert.attackCell) > alert.responseRadiusSq)
                    continue;

                int hash = responder.thingIDNumber;
                unchecked { hash = (hash * 397) ^ (alert.serial * 7919); }
                int threshold = (int)Math.Round(Math.Max(0f, Math.Min(1f, alert.responseChance)) * 10000f);
                if ((hash & 0x7fffffff) % 10000 >= threshold)
                    return false;
                if (!responder.CanReach(alert.aggressor, PathEndMode.Touch, Danger.Deadly))
                    return false;

                target = alert.aggressor;
                return true;
            }
            return false;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref alerts, "wngReplicatorThreatAlerts", LookMode.Deep);
            Scribe_Values.Look(ref nextSerial, "wngReplicatorThreatNextSerial", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && alerts == null)
                alerts = new List<ReplicatorThreatAlert>();
        }
    }

    public sealed class JobGiver_ReplicatorDefense : ThinkNode_JobGiver
    {
        // Controlled swarm bodies use this same think tree as autonomous ones.
        // Historically the player-faction early return left genuine Queen / neural
        // lattice controlled units entirely unable to protect colonists.
        private static readonly Dictionary<int, int> nextFailedColonyGuardScan = new Dictionary<int, int>();

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map == null)
                return null;
            if (ReplicatorInterferenceUtility.IsEmpDisrupted(pawn))
                return null;

            if (pawn.Faction == Faction.OfPlayer)
            {
                // Faction membership alone does not equal control. Only active,
                // signal-valid sovereign authority allows colony defense.
                if (!ReplicatorSovereignControlUtility.SignalOperationalForCurrentDomain(pawn))
                    return null;
                Pawn invader = FindThreatToColonists(pawn);
                return invader == null ? null : MakeDefenseJob(pawn, invader, 450);
            }

            Pawn aggressor;
            CompReplicatorThreatResponse personal = pawn.TryGetComp<CompReplicatorThreatResponse>();
            if (personal?.TryGetSelfDefenseTarget(pawn, out aggressor) == true)
                return MakeDefenseJob(pawn, aggressor, 900);

            MapComponent_ReplicatorThreatResponse swarm = pawn.Map.GetComponent<MapComponent_ReplicatorThreatResponse>();
            if (swarm?.TryGetRetaliationTarget(pawn, out aggressor) == true)
                return MakeDefenseJob(pawn, aggressor, 720);

            return null;
        }

        private static Pawn FindThreatToColonists(Pawn responder)
        {
            if (responder?.Map == null || Faction.OfPlayer == null)
                return null;
            int now = Find.TickManager?.TicksGame ?? 0;
            int retryTick;
            if (nextFailedColonyGuardScan.TryGetValue(responder.thingIDNumber, out retryTick) &&
                now >= 0 && now < retryTick)
                return null;

            Map map = responder.Map;
            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            IReadOnlyList<Pawn> all = map.mapPawns.AllPawnsSpawned;
            Pawn closest = null;
            float distanceSq = 25f * 25f;
            if (colonists != null && colonists.Count > 0)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    Pawn enemy = all[i];
                    if (enemy == null || !enemy.Spawned || enemy.Dead || enemy.Downed ||
                        enemy.Faction == null || !enemy.Faction.HostileTo(Faction.OfPlayer))
                        continue;
                    float dist = responder.Position.DistanceToSquared(enemy.Position);
                    if (dist >= distanceSq)
                        continue;
                    bool nearColonist = false;
                    for (int j = 0; j < colonists.Count; j++)
                    {
                        Pawn colonist = colonists[j];
                        if (colonist != null && colonist.Spawned && !colonist.Dead &&
                            colonist.Position.DistanceToSquared(enemy.Position) <= 100f)
                        {
                            nearColonist = true;
                            break;
                        }
                    }
                    if (!nearColonist)
                        continue;
                    closest = enemy;
                    distanceSq = dist;
                }
            }

            if (closest != null && responder.CanReach(closest, PathEndMode.Touch, Danger.Deadly))
            {
                nextFailedColonyGuardScan.Remove(responder.thingIDNumber);
                return closest;
            }
            if (nextFailedColonyGuardScan.Count > 2048)
                nextFailedColonyGuardScan.Clear();
            nextFailedColonyGuardScan[responder.thingIDNumber] = now + 120;
            return null;
        }

        private static Job MakeDefenseJob(Pawn pawn, Pawn target, int expiryTicks)
        {
            // A real confrontation with an active native personal barrier is AntiShield evidence.
            // The adaptation utility throttles/shared-records the sample, so repeated ThinkTree
            // evaluation cannot inflate the lineage history.
            if (ReplicatorAdaptationUtility.HasActiveNativePersonalShield(target))
                ReplicatorAdaptationUtility.ShareShieldEngagement(pawn);

            if (pawn?.equipment?.Primary != null && pawn.equipment.Primary.def.IsRangedWeapon)
            {
                Job ranged = JobMaker.MakeJob(JobDefOf.AttackStatic, target);
                ranged.expiryInterval = expiryTicks;
                ranged.maxNumStaticAttacks = 3;
                ranged.endIfCantShootTargetFromCurPos = true;
                return ranged;
            }

            Job melee = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
            melee.expiryInterval = expiryTicks;
            return melee;
        }
    }
}
