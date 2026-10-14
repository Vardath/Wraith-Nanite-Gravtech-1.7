using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_ReplicatorArtillery : CompProperties
    {
        public string integratedWeaponDef = "WNG_ReplicatorArtilleryCaster";

        public CompProperties_ReplicatorArtillery()
        {
            compClass = typeof(CompReplicatorArtillery);
        }
    }

    /// <summary>
    /// Owns only the dedicated artillery body's integrated long-range organ. It deliberately does not
    /// replace the cumulative Ranged adaptation system. Adaptation weapons are allowed to exist on
    /// ordinary bodies; the Artillery side-form replaces only those two WNG integrated adaptation
    /// weapons with its role weapon and never destroys unrelated equipment.
    /// </summary>
    public sealed class CompReplicatorArtillery : ThingComp
    {
        private CompProperties_ReplicatorArtillery Props => (CompProperties_ReplicatorArtillery)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            EnsureIntegratedWeapon(parent as Pawn);
        }

        public void EnsureIntegratedWeapon(Pawn pawn)
        {
            if (pawn?.equipment == null || string.IsNullOrWhiteSpace(Props.integratedWeaponDef))
                return;

            ThingDef artilleryDef = DefDatabase<ThingDef>.GetNamedSilentFail(Props.integratedWeaponDef);
            if (artilleryDef == null)
                return;

            ThingWithComps primary = pawn.equipment.Primary;
            if (primary?.def == artilleryDef)
                return;

            // Adaptation inheritance may have already grown one of these before the newly-generated
            // Artillery body is placed. They are WNG machine organs, so they are the only equipment
            // this specialist is allowed to replace automatically.
            if (primary?.def?.defName == "WNG_ReplicatorPulseCaster" ||
                primary?.def?.defName == "WNG_ReplicatorShieldDisruptor")
            {
                pawn.equipment.DestroyEquipment(primary);
            }

            if (pawn.equipment.Primary != null)
                return;

            ThingWithComps weapon = ThingMaker.MakeThing(artilleryDef) as ThingWithComps;
            if (weapon != null)
                pawn.equipment.AddEquipment(weapon);
        }
    }

    public static class ReplicatorArtilleryUtility
    {
        public const string ArtilleryDefName = "WNG_ReplicatorArtillery";

        public static bool IsArtillery(Pawn pawn)
        {
            return pawn?.def?.defName == ArtilleryDefName;
        }
    }

    /// <summary>
    /// Hostile autonomous Bulwark-mass reconfiguration into long-range support. The old WNG evidence
    /// consistently required both Ranged and Power knowledge and used a mature-swarm threshold of 22
    /// with one Artillery body. Those values are provisional tuning; the conserved-mass transaction
    /// and adaptation prerequisites are the important current contract.
    /// </summary>
    public sealed class MapComponent_ReplicatorArtilleryFormation : MapComponent
    {
        public const int FormationThreshold = 22;
        public const int FormationCheckIntervalTicks = 1800;
        public const int MaxArtillery = 1;

        private int nextFormationCheckTick;

        public MapComponent_ReplicatorArtilleryFormation(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now < nextFormationCheckTick)
                return;

            nextFormationCheckTick = SafeFutureTick(now, FormationCheckIntervalTicks);
            TryFormArtillery();
        }

        private void TryFormArtillery()
        {
            IReadOnlyList<Pawn> spawned = map.mapPawns.AllPawnsSpawned;
            Dictionary<string, List<Pawn>> blocksByDomain = new Dictionary<string, List<Pawn>>();

            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn pawn = spawned[i];
                if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Faction == null || pawn.Faction == Faction.OfPlayer ||
                    !ReplicatorAssimilationUtility.IsBlockReplicator(pawn))
                    continue;
                if (Faction.OfPlayer != null && !pawn.Faction.HostileTo(Faction.OfPlayer))
                    continue;

                string domainId = ReplicatorDomainUtility.DomainId(pawn);
                if (string.IsNullOrEmpty(domainId))
                    continue;

                if (!blocksByDomain.TryGetValue(domainId, out List<Pawn> list))
                {
                    list = new List<Pawn>();
                    blocksByDomain.Add(domainId, list);
                }
                list.Add(pawn);
            }

            foreach (KeyValuePair<string, List<Pawn>> pair in blocksByDomain)
            {
                List<Pawn> blocks = pair.Value;
                if (blocks.Count < FormationThreshold)
                    continue;

                int existing = 0;
                for (int i = 0; i < blocks.Count; i++)
                    if (ReplicatorArtilleryUtility.IsArtillery(blocks[i]))
                        existing++;
                if (existing >= MaxArtillery)
                    continue;

                Pawn source = FindBulwarkSource(blocks);
                if (source != null && TryConvert(source))
                    return;
            }
        }

        private Pawn FindBulwarkSource(List<Pawn> blocks)
        {
            Pawn source = null;
            for (int i = 0; i < blocks.Count; i++)
            {
                Pawn candidate = blocks[i];
                CompReplicatorAdaptation adaptation = candidate?.TryGetComp<CompReplicatorAdaptation>();
                if (candidate?.def?.defName != "WNG_ReplicatorBulwark" || candidate.Downed ||
                    ReplicatorInterferenceUtility.IsEmpDisrupted(candidate) ||
                    TemporaryAsuranIntrusionUtility.IsCommandSuppressed(candidate) ||
                    ReplicatorContainmentUtility.IsContained(map, candidate.Position) || adaptation == null ||
                    !adaptation.Has(ReplicatorAdaptationFlags.Ranged) || !adaptation.Has(ReplicatorAdaptationFlags.Power))
                {
                    continue;
                }

                if (source == null || candidate.thingIDNumber < source.thingIDNumber)
                    source = candidate;
            }
            return source;
        }

        private bool TryConvert(Pawn source)
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(ReplicatorArtilleryUtility.ArtilleryDefName);
            if (source == null || source.Dead || !source.Spawned || source.Map != map || source.Faction == null || kind == null)
                return false;

            Pawn artillery = null;
            try
            {
                artillery = PawnGenerator.GeneratePawn(kind, source.Faction);
                ReplicatorDomainUtility.CopyDomain(source, artillery);
                TemporaryAsuranIntrusionUtility.CopyState(source, artillery);
                ReplicatorSovereignControlUtility.CopyState(source, artillery);
                artillery.TryGetComp<CompReplicatorAdaptation>()?.InheritFrom(source.TryGetComp<CompReplicatorAdaptation>());
                artillery.TryGetComp<CompReplicatorMaterialProfile>()?.InheritFrom(source.TryGetComp<CompReplicatorMaterialProfile>());

                if (!GenPlace.TryPlaceThing(
                        artillery,
                        source.Position,
                        map,
                        ThingPlaceMode.Near,
                        null,
                        cell => !ReplicatorContainmentUtility.IsContained(map, cell)))
                {
                    if (!artillery.Destroyed)
                        artillery.Destroy(DestroyMode.Vanish);
                    return false;
                }

                artillery.TryGetComp<CompReplicatorArtillery>()?.EnsureIntegratedWeapon(artillery);

                source.Destroy(DestroyMode.Vanish);
                if (!source.Destroyed)
                {
                    if (!artillery.Destroyed)
                        artillery.Destroy(DestroyMode.Vanish);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[WNG] Replicator Artillery formation failed: " + ex);
                if (artillery != null && !artillery.Destroyed)
                    artillery.Destroy(DestroyMode.Vanish);
                return false;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref nextFormationCheckTick, "wngReplicatorArtilleryNextFormationCheck", 0);
        }

        private static int SafeFutureTick(int now, int delay)
        {
            long result = (long)Math.Max(0, now) + Math.Max(0, delay);
            return result >= int.MaxValue ? int.MaxValue : (int)result;
        }
    }
}
