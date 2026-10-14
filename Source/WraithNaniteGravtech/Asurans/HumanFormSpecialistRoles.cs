using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace WraithNaniteGravtech
{
    public enum HumanFormSpecialistRole
    {
        Unassigned = 0,
        Engineer = 1,
        Infiltrator = 2,
        Soldier = 3,
        Coordinator = 4,
        Commander = 5
    }

    public static class HumanFormSpecialistRoleUtility
    {
        private const string CoordinationDefName = "WNG_HumanFormCoordination";

        private static readonly string[] RoleDefNames =
        {
            null,
            "WNG_HumanFormRoleEngineer",
            "WNG_HumanFormRoleInfiltrator",
            "WNG_HumanFormRoleSoldier",
            "WNG_HumanFormRoleCoordinator",
            "WNG_HumanFormRoleCommander"
        };

        public static bool IsEligible(Pawn pawn)
        {
            if (pawn == null ||
                pawn.Dead ||
                pawn.def != ThingDefOf.Human ||
                !AsuranCollectiveUtility.IsNaniteSynthetic(pawn) ||
                !AsuranCollectiveUtility.IsLinked(pawn) ||
                ReplicatorQueenUtility.IsExactQueen(pawn))
            {
                return false;
            }

            return pawn.kindDef?.defName != "WNG_ReplicatorQueenChild";
        }

        public static HumanFormSpecialistRole RoleOf(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null)
                return HumanFormSpecialistRole.Unassigned;

            for (int i = 1; i < RoleDefNames.Length; i++)
            {
                HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(RoleDefNames[i]);
                if (def != null && pawn.health.hediffSet.GetFirstHediffOfDef(def) != null)
                    return (HumanFormSpecialistRole)i;
            }

            return HumanFormSpecialistRole.Unassigned;
        }

        public static HumanFormSpecialistRole EnsureRole(Pawn pawn)
        {
            if (!IsEligible(pawn))
                return HumanFormSpecialistRole.Unassigned;

            HumanFormSpecialistRole current = RoleOf(pawn);
            if (current != HumanFormSpecialistRole.Unassigned)
                return current;

            HumanFormSpecialistRole chosen = ChooseRole(pawn);
            RestoreRole(pawn, chosen);
            return chosen;
        }

        public static void RestoreRole(Pawn pawn, HumanFormSpecialistRole role)
        {
            if (pawn?.health?.hediffSet == null ||
                role == HumanFormSpecialistRole.Unassigned ||
                (int)role <= 0 ||
                (int)role >= RoleDefNames.Length)
            {
                return;
            }

            string wantedName = RoleDefNames[(int)role];
            for (int i = 1; i < RoleDefNames.Length; i++)
            {
                HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(RoleDefNames[i]);
                if (def == null)
                    continue;

                Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(def);
                if (existing != null && RoleDefNames[i] != wantedName)
                    pawn.health.RemoveHediff(existing);
            }

            HediffDef wanted = DefDatabase<HediffDef>.GetNamedSilentFail(wantedName);
            if (wanted != null && pawn.health.hediffSet.GetFirstHediffOfDef(wanted) == null)
                pawn.health.AddHediff(wanted);
        }

        public static bool HasCommandMesh(Pawn pawn)
        {
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(CoordinationDefName);
            Hediff state = def == null ? null : pawn?.health?.hediffSet?.GetFirstHediffOfDef(def);
            return state != null && state.Severity >= 0.75f;
        }

        public static void ForcePatternBackup(Pawn pawn)
        {
            if (pawn?.Map == null || pawn.Faction == null || !IsEligible(pawn))
                return;

            ThingDef archiveDef = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_AsuranPatternArchive");
            if (archiveDef == null)
                return;

            Thing archive = pawn.Map.listerThings.ThingsOfDef(archiveDef)
                .Where(t => t != null &&
                            !t.Destroyed &&
                            t.Spawned &&
                            t.Faction == pawn.Faction &&
                            t.TryGetComp<CompAsuranPatternArchive>()?.Powered == true)
                .OrderBy(t => t.thingIDNumber)
                .FirstOrDefault();

            archive?.TryGetComp<CompAsuranPatternArchive>()?.RecordPawnNow(pawn);
        }

        private static HumanFormSpecialistRole ChooseRole(Pawn pawn)
        {
            string kind = pawn.kindDef?.defName;
            if (kind == "WNG_PrecursorEngineer")
                return HumanFormSpecialistRole.Engineer;
            if (kind == "WNG_PrecursorSoldier")
                return HumanFormSpecialistRole.Soldier;
            if (kind == "WNG_PrecursorCommander")
                return HumanFormSpecialistRole.Commander;

            int construction = SkillLevel(pawn, SkillDefOf.Construction);
            int crafting = SkillLevel(pawn, SkillDefOf.Crafting);
            int shooting = SkillLevel(pawn, SkillDefOf.Shooting);
            int melee = SkillLevel(pawn, SkillDefOf.Melee);
            int social = SkillLevel(pawn, SkillDefOf.Social);
            int intellectual = SkillLevel(pawn, SkillDefOf.Intellectual);

            if (social >= 12 && Math.Max(intellectual, shooting) >= 10)
                return HumanFormSpecialistRole.Commander;
            if (construction >= 10 || crafting >= 11)
                return HumanFormSpecialistRole.Engineer;
            if (social >= 9 && shooting >= 7)
                return HumanFormSpecialistRole.Infiltrator;
            if (intellectual >= 11 || social >= 10)
                return HumanFormSpecialistRole.Coordinator;
            if (shooting >= 9 || melee >= 9)
                return HumanFormSpecialistRole.Soldier;

            int roll = StableIdentityPercent(pawn);
            string faction = pawn.Faction?.def?.defName ?? string.Empty;

            if (faction == "WNG_HumanFormEnclave")
            {
                if (roll < 30) return HumanFormSpecialistRole.Engineer;
                if (roll < 40) return HumanFormSpecialistRole.Infiltrator;
                if (roll < 65) return HumanFormSpecialistRole.Soldier;
                if (roll < 90) return HumanFormSpecialistRole.Coordinator;
                return HumanFormSpecialistRole.Commander;
            }

            if (pawn.Faction == Faction.OfPlayer)
            {
                if (roll < 25) return HumanFormSpecialistRole.Engineer;
                if (roll < 40) return HumanFormSpecialistRole.Infiltrator;
                if (roll < 65) return HumanFormSpecialistRole.Soldier;
                if (roll < 90) return HumanFormSpecialistRole.Coordinator;
                return HumanFormSpecialistRole.Commander;
            }

            if (roll < 15) return HumanFormSpecialistRole.Engineer;
            if (roll < 30) return HumanFormSpecialistRole.Infiltrator;
            if (roll < 75) return HumanFormSpecialistRole.Soldier;
            if (roll < 90) return HumanFormSpecialistRole.Coordinator;
            return HumanFormSpecialistRole.Commander;
        }

        private static int SkillLevel(Pawn pawn, SkillDef def)
        {
            return pawn?.skills == null || def == null ? 0 : pawn.skills.GetSkill(def).Level;
        }

        private static int StableIdentityPercent(Pawn pawn)
        {
            string identity = (pawn?.Name?.ToStringFull ?? pawn?.LabelShort ?? "human-form") + "|" +
                              (pawn?.story?.Childhood?.defName ?? string.Empty) + "|" +
                              (pawn?.story?.Adulthood?.defName ?? string.Empty);

            unchecked
            {
                uint hash = 2166136261u;
                foreach (char character in identity)
                {
                    hash ^= character;
                    hash *= 16777619u;
                }
                return (int)(hash % 100u);
            }
        }
    }

    /// <summary>
    /// Restored persistent specialist behavior for human-form synthetics. This is distinct from
    /// disguise/infiltration presentation: the infiltrator role is precision sabotage doctrine.
    /// </summary>
    public sealed class GameComponent_HumanFormSpecialistRoles : GameComponent
    {
        private const int RefreshIntervalTicks = 300;
        private const float CoordinatorRadius = 18f;
        private const float CommanderRadius = 28f;

        private int nextTick;

        public GameComponent_HumanFormSpecialistRoles(Game game) { }

        public override void GameComponentTick()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now < nextTick)
                return;
            nextTick = SafeFutureTick(now, RefreshIntervalTicks);

            foreach (Map map in Find.Maps)
            {
                if (map?.mapPawns == null)
                    continue;

                List<Pawn> eligible = map.mapPawns.AllPawnsSpawned
                    .Where(HumanFormSpecialistRoleUtility.IsEligible)
                    .ToList();
                if (eligible.Count == 0)
                    continue;

                List<HumanFormSpecialistRole> roles = new List<HumanFormSpecialistRole>(eligible.Count);
                foreach (Pawn pawn in eligible)
                    roles.Add(HumanFormSpecialistRoleUtility.EnsureRole(pawn));

                for (int i = 0; i < eligible.Count; i++)
                    RefreshCoordination(eligible[i], eligible, roles);

                for (int i = 0; i < eligible.Count; i++)
                    RunRoleBehavior(eligible[i], roles[i]);
            }
        }

        private static void RefreshCoordination(
            Pawn pawn,
            List<Pawn> eligible,
            List<HumanFormSpecialistRole> roles)
        {
            if (pawn?.health?.hediffSet == null)
                return;

            HediffDef coordinationDef = DefDatabase<HediffDef>.GetNamedSilentFail("WNG_HumanFormCoordination");
            if (coordinationDef == null)
                return;

            float strength = 0f;
            if (!AsuranCollectiveUtility.IsDisrupted(pawn) && pawn.Faction != null)
            {
                for (int i = 0; i < eligible.Count; i++)
                {
                    Pawn source = eligible[i];
                    if (source == null ||
                        source.Dead ||
                        source.Downed ||
                        source.Faction != pawn.Faction ||
                        AsuranCollectiveUtility.IsDisrupted(source))
                    {
                        continue;
                    }

                    float radius;
                    float candidate;
                    if (roles[i] == HumanFormSpecialistRole.Commander)
                    {
                        radius = CommanderRadius;
                        candidate = 1f;
                    }
                    else if (roles[i] == HumanFormSpecialistRole.Coordinator)
                    {
                        radius = CoordinatorRadius;
                        candidate = 0.5f;
                    }
                    else
                    {
                        continue;
                    }

                    if (pawn.Position.DistanceToSquared(source.Position) <= radius * radius)
                        strength = Math.Max(strength, candidate);
                }
            }

            Hediff current = pawn.health.hediffSet.GetFirstHediffOfDef(coordinationDef);
            if (strength > 0f)
            {
                if (current == null)
                {
                    pawn.health.AddHediff(coordinationDef);
                    current = pawn.health.hediffSet.GetFirstHediffOfDef(coordinationDef);
                }
                if (current != null)
                    current.Severity = strength;
            }
            else if (current != null)
            {
                pawn.health.RemoveHediff(current);
            }
        }

        private static void RunRoleBehavior(Pawn pawn, HumanFormSpecialistRole role)
        {
            if (pawn == null ||
                pawn.Dead ||
                pawn.Downed ||
                !pawn.Spawned ||
                pawn.Map == null ||
                AsuranCollectiveUtility.IsDisrupted(pawn))
            {
                return;
            }

            bool commandLinked = HumanFormSpecialistRoleUtility.HasCommandMesh(pawn);
            switch (role)
            {
                case HumanFormSpecialistRole.Engineer:
                    if (pawn.IsHashIntervalTick(commandLinked ? 600 : 900))
                        EngineerPulse(pawn, commandLinked ? 2f : 1f);
                    break;

                case HumanFormSpecialistRole.Infiltrator:
                    if (pawn.IsHashIntervalTick(commandLinked ? 900 : 1500))
                        TryPrecisionSabotage(pawn);
                    break;

                case HumanFormSpecialistRole.Soldier:
                    if (pawn.IsHashIntervalTick(commandLinked ? 600 : 1200))
                        TryPrioritizeDefender(pawn);
                    break;

                case HumanFormSpecialistRole.Commander:
                    if (pawn.IsHashIntervalTick(2400))
                        ForceNearbyBackups(pawn);
                    break;
            }
        }

        private static void EngineerPulse(Pawn engineer, float repairAmount)
        {
            float radiusSq = 14f * 14f;

            Building building = engineer.Map.listerThings.AllThings
                .OfType<Building>()
                .Where(b => b != null &&
                            !b.Destroyed &&
                            b.Spawned &&
                            b.Faction == engineer.Faction &&
                            b.HitPoints < b.MaxHitPoints &&
                            b.Position.DistanceToSquared(engineer.Position) <= radiusSq)
                .OrderBy(b => (float)b.HitPoints / Math.Max(1, b.MaxHitPoints))
                .ThenBy(b => b.Position.DistanceToSquared(engineer.Position))
                .FirstOrDefault();

            if (building != null)
            {
                building.HitPoints = Math.Min(
                    building.MaxHitPoints,
                    building.HitPoints + Math.Max(1, (int)Math.Ceiling(repairAmount)));
                return;
            }

            Pawn patient = engineer.Map.mapPawns.AllPawnsSpawned
                .Where(p => p != null &&
                            !p.Dead &&
                            p.Faction == engineer.Faction &&
                            HumanFormSpecialistRoleUtility.IsEligible(p) &&
                            p.Position.DistanceToSquared(engineer.Position) <= radiusSq)
                .Where(p => p.health?.hediffSet?.hediffs
                    .OfType<Hediff_Injury>()
                    .Any(injury => injury.Severity > 0f && !injury.IsPermanent()) == true)
                .OrderBy(p => p.Position.DistanceToSquared(engineer.Position))
                .FirstOrDefault();

            Hediff_Injury injuryToHeal = patient?.health?.hediffSet?.hediffs
                .OfType<Hediff_Injury>()
                .Where(injury => injury.Severity > 0f && !injury.IsPermanent())
                .OrderByDescending(injury => injury.Severity)
                .FirstOrDefault();

            injuryToHeal?.Heal(Math.Max(0f, repairAmount));
        }

        private static void TryPrecisionSabotage(Pawn infiltrator)
        {
            if (!IsHostileToPlayer(infiltrator) ||
                infiltrator.jobs == null ||
                HasProtectedStrategicJob(infiltrator) ||
                PlayerDefenderNear(infiltrator, 9f) != null)
            {
                return;
            }

            Building target = infiltrator.Map.listerThings.AllThings
                .OfType<Building>()
                .Where(b => b != null &&
                            !b.Destroyed &&
                            b.Spawned &&
                            b.Faction == Faction.OfPlayer &&
                            b.Position.DistanceToSquared(infiltrator.Position) <= 45f * 45f)
                .OrderByDescending(SabotageScore)
                .ThenBy(b => b.Position.DistanceToSquared(infiltrator.Position))
                .FirstOrDefault();

            if (target == null)
                return;

            Job job = new Job(JobDefOf.AttackStatic, target)
            {
                expiryInterval = 1800,
                maxNumStaticAttacks = 4,
                endIfCantShootTargetFromCurPos = false
            };
            infiltrator.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        private static void TryPrioritizeDefender(Pawn soldier)
        {
            if (!IsHostileToPlayer(soldier) ||
                soldier.jobs == null ||
                HasProtectedStrategicJob(soldier))
            {
                return;
            }

            Pawn target = PlayerDefenderNear(soldier, 24f);
            if (target == null)
                return;

            if (soldier.CurJobDef == JobDefOf.AttackStatic && soldier.CurJob?.targetA.Thing == target)
                return;

            Job job = new Job(JobDefOf.AttackStatic, target)
            {
                expiryInterval = 900,
                endIfCantShootTargetFromCurPos = true
            };
            soldier.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        private static Pawn PlayerDefenderNear(Pawn pawn, float radius)
        {
            if (pawn?.Map == null)
                return null;

            float radiusSq = radius * radius;
            return pawn.Map.mapPawns.AllPawnsSpawned
                .Where(p => p != null &&
                            !p.Dead &&
                            !p.Downed &&
                            p.Faction == Faction.OfPlayer &&
                            p.Position.DistanceToSquared(pawn.Position) <= radiusSq)
                .OrderBy(p => p.Position.DistanceToSquared(pawn.Position))
                .FirstOrDefault();
        }

        private static bool IsHostileToPlayer(Pawn pawn)
        {
            return pawn?.Faction != null &&
                   pawn.Faction != Faction.OfPlayer &&
                   pawn.Faction.HostileTo(Faction.OfPlayer);
        }

        private static bool HasProtectedStrategicJob(Pawn pawn)
        {
            string defName = pawn?.CurJobDef?.defName;
            return !defName.NullOrEmpty() &&
                   defName.StartsWith("WNG_", StringComparison.Ordinal);
        }

        private static float SabotageScore(Building building)
        {
            if (building == null)
                return 0f;

            string name = (building.def?.defName ?? string.Empty).ToLowerInvariant();
            float score = Math.Max(0f, building.MarketValue) * 0.04f;

            if (building.TryGetComp<CompPowerTrader>() != null)
                score += 30f;
            if (building is Building_WorkTable)
                score += 20f;

            foreach (string token in new[]
                     {
                         "research", "comms", "reactor", "battery", "shield", "turret",
                         "archive", "fabricator", "grav", "stargate", "console"
                     })
            {
                if (name.Contains(token))
                    score += 28f;
            }

            return score;
        }

        private static void ForceNearbyBackups(Pawn commander)
        {
            if (commander?.Map == null || commander.Faction == null)
                return;

            commander.Map.mapPawns.AllPawnsSpawned
                .Where(p => HumanFormSpecialistRoleUtility.IsEligible(p) &&
                            p.Faction == commander.Faction &&
                            p.Position.DistanceToSquared(commander.Position) <= CommanderRadius * CommanderRadius)
                .OrderBy(p => p.Position.DistanceToSquared(commander.Position))
                .Take(6)
                .ToList()
                .ForEach(HumanFormSpecialistRoleUtility.ForcePatternBackup);
        }

        private static int SafeFutureTick(int now, int delay)
        {
            long result = (long)Math.Max(0, now) + Math.Max(1, delay);
            return result >= int.MaxValue ? int.MaxValue : (int)result;
        }
    }
}
