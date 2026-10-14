using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace WraithNaniteGravtech.Anomaly
{
    public sealed class HediffCompProperties_IratusAttachment : HediffCompProperties
    {
        public float severityPerDay = 0.45f;
        public float failedRemovalSeverity = 0.18f;
        public float bugHealingPerDay = 12f;

        public HediffCompProperties_IratusAttachment()
        {
            compClass = typeof(HediffComp_IratusAttachment);
        }
    }

    /// <summary>
    /// Implant-like interface for a physically attached Iratus bug.
    /// The hediff tracks host physiology while this comp owns the exact attacking Pawn.
    /// No proxy bug is generated on removal, death, save/load, or surgery.
    /// </summary>
    public sealed class HediffComp_IratusAttachment : HediffComp, IThingHolder
    {
        private ThingOwner<Pawn> attachedBug;

        public HediffCompProperties_IratusAttachment Props => (HediffCompProperties_IratusAttachment)props;
        public IThingHolder ParentHolder => Pawn;

        public Pawn AttachedBug
        {
            get
            {
                EnsureHolder();
                return attachedBug.InnerListForReading.FirstOrDefault();
            }
        }

        private void EnsureHolder()
        {
            if (attachedBug == null)
                attachedBug = new ThingOwner<Pawn>(this, false, LookMode.Deep, false);
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            EnsureHolder();
            return attachedBug;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            EnsureHolder();
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, attachedBug);
        }

        public override string CompDescriptionExtra
        {
            get
            {
                Pawn bug = AttachedBug;
                if (bug == null)
                    return "No living Iratus organism is currently registered in this attachment.";
                return "Attached organism: " + bug.LabelShortCap + ". The creature is being preserved as the exact living pawn that latched onto this patient.";
            }
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            EnsureHolder();
            attachedBug.ExposeData();
        }

        public bool TryTakeExactBug(Pawn bug)
        {
            if (bug == null || bug.Destroyed || AttachedBug != null)
                return false;

            EnsureHolder();
            Map map = bug.MapHeld;
            IntVec3 cell = bug.PositionHeld;
            bool wasSpawned = bug.Spawned;

            try
            {
                bug.jobs?.StopAll();
                if (bug.Spawned)
                    bug.DeSpawn(DestroyMode.Vanish);
                if (attachedBug.TryAdd(bug, false))
                    return true;
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Iratus attachment custody failed; rolling exact bug back: " + ex.Message);
            }

            if (wasSpawned && map != null && bug != null && !bug.Destroyed && !bug.Spawned)
                GenSpawn.Spawn(bug, CellFinder.StandableCellNear(cell, map, 2f), map);
            return false;
        }

        public bool TryReleaseExactBug(Map map, IntVec3 near, Pawn surgeon = null)
        {
            Pawn bug = AttachedBug;
            if (bug == null || map == null)
                return false;

            Pawn dropped;
            if (!attachedBug.TryDrop(bug, near, map, ThingPlaceMode.Near, out dropped) || dropped != bug)
                return false;

            // The procedure gets the parasite off the patient, not safely into a cage.
            // A short stun models the immediate post-defibrillation handling window.
            dropped.stances?.stunner?.StunFor(300, surgeon, addBattleLog: false);
            return true;
        }

        public void NotifyFailedRemoval()
        {
            parent.Severity = Mathf.Min(0.99f, parent.Severity + Math.Max(0f, Props.failedRemovalSeverity));
            Pawn victim = Pawn;
            if (victim?.Faction == Faction.OfPlayer)
            {
                Messages.Message(
                    "The Iratus bug clamps down harder during the failed detachment attempt. " + victim.LabelShortCap + "'s condition worsens.",
                    victim,
                    MessageTypeDefOf.NegativeHealthEvent,
                    historical: false);
            }
        }

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            base.CompPostTickInterval(ref severityAdjustment, delta);
            Pawn bug = AttachedBug;
            if (bug == null)
                return;

            severityAdjustment += Math.Max(0f, Props.severityPerDay) * delta / 60000f;

            // Iratus biology regenerates strongly while feeding. Heal the exact hidden pawn rather than
            // replacing it with a fresh specimen when it detaches later.
            float heal = Math.Max(0f, Props.bugHealingPerDay) * delta / 60000f;
            if (heal > 0f && bug.health?.hediffSet?.hediffs != null)
            {
                foreach (Hediff_Injury injury in bug.health.hediffSet.hediffs.OfType<Hediff_Injury>().ToList())
                {
                    if (injury != null && injury.Severity > 0f)
                        injury.Heal(heal);
                }
            }
        }

        public override void Notify_PawnDied(DamageInfo? dinfo, Hediff culprit = null)
        {
            base.Notify_PawnDied(dinfo, culprit);
            // Canon: an Iratus stops feeding when its host dies. If a map exists, release the same bug.
            Map map = Pawn?.MapHeld;
            if (map != null)
                TryReleaseExactBug(map, Pawn.PositionHeld, null);
        }

        public override void CompPostPostRemoved()
        {
            base.CompPostPostRemoved();
            if (AttachedBug == null)
                return;

            Map map = Pawn?.MapHeld;
            if (map != null)
                TryReleaseExactBug(map, Pawn.PositionHeld, null);
        }
    }

    public static class IratusAttachmentUtility
    {
        public static HediffDef AttachmentDef => DefDatabase<HediffDef>.GetNamedSilentFail("WNG_IratusAttached");

        public static HediffComp_IratusAttachment AttachmentComp(Pawn pawn)
        {
            HediffDef def = AttachmentDef;
            HediffWithComps hediff = def == null ? null : pawn?.health?.hediffSet?.GetFirstHediffOfDef(def) as HediffWithComps;
            return hediff?.TryGetComp<HediffComp_IratusAttachment>();
        }

        public static bool HasAttachment(Pawn pawn) => AttachmentComp(pawn)?.AttachedBug != null;

        public static bool EligibleHost(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.RaceProps == null || !pawn.RaceProps.IsFlesh || pawn.RaceProps.IsMechanoid)
                return false;
            string xenotype = pawn.genes?.Xenotype?.defName;
            if (xenotype == "WNG_NanitePrecursor" || xenotype == "WNG_HumanFormReplicator")
                return false;
            return pawn.health?.hediffSet?.GetNotMissingParts()?.Any(p => p.def?.defName == "Neck") == true;
        }

        public static bool TryAttach(Pawn bug, Pawn host)
        {
            HediffDef def = AttachmentDef;
            if (def == null || bug == null || !bug.Spawned || !EligibleHost(host) || HasAttachment(host))
                return false;

            BodyPartRecord neck = host.health.hediffSet.GetNotMissingParts().FirstOrDefault(p => p.def?.defName == "Neck");
            if (neck == null)
                return false;

            HediffWithComps hediff = HediffMaker.MakeHediff(def, host, neck) as HediffWithComps;
            if (hediff == null)
                return false;
            hediff.Severity = 0.08f;
            host.health.AddHediff(hediff, neck);

            HediffComp_IratusAttachment comp = hediff.TryGetComp<HediffComp_IratusAttachment>();
            if (comp != null && comp.TryTakeExactBug(bug))
            {
                if (host.Faction == Faction.OfPlayer)
                {
                    Messages.Message(
                        "An Iratus bug has latched onto " + host.LabelShortCap + "'s neck and begun feeding.",
                        host,
                        MessageTypeDefOf.ThreatBig,
                        historical: false);
                }
                return true;
            }

            host.health.RemoveHediff(hediff);
            return false;
        }
    }

    public sealed class Verb_IratusLatch : Verb_MeleeAttackDamage
    {
        protected override DamageWorker.DamageResult ApplyMeleeDamageToTarget(LocalTargetInfo target)
        {
            DamageWorker.DamageResult result = base.ApplyMeleeDamageToTarget(target);
            Pawn bug = CasterPawn;
            Pawn victim = target.Pawn;
            if (bug != null && victim != null && result != null && result.totalDamageDealt > 0f)
                IratusAttachmentUtility.TryAttach(bug, victim);
            return result;
        }
    }

    public sealed class Recipe_RemoveIratusBug : Recipe_Surgery
    {
        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            Pawn pawn = thing as Pawn;
            if (pawn == null)
                return false;

            return IratusAttachmentUtility.HasAttachment(pawn) && base.AvailableOnNow(thing, part);
        }

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            HediffDef def = IratusAttachmentUtility.AttachmentDef;
            HediffWithComps hediff = def == null ? null : pawn?.health?.hediffSet?.GetFirstHediffOfDef(def) as HediffWithComps;
            HediffComp_IratusAttachment comp = hediff?.TryGetComp<HediffComp_IratusAttachment>();
            if (hediff == null || comp?.AttachedBug == null)
                return;

            if (CheckSurgeryFail(billDoer, pawn, ingredients, part, bill))
            {
                comp.NotifyFailedRemoval();
                return;
            }

            Map map = pawn.MapHeld;
            IntVec3 cell = billDoer?.PositionHeld ?? pawn.PositionHeld;
            if (map == null || !comp.TryReleaseExactBug(map, cell, billDoer))
            {
                Messages.Message(
                    "The Iratus detachment could not complete because the living parasite could not be placed safely. The attachment remains in place.",
                    pawn,
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            pawn.health.RemoveHediff(hediff);
            if (pawn.Faction == Faction.OfPlayer)
            {
                Messages.Message(
                    billDoer.LabelShortCap + " successfully detached the living Iratus bug from " + pawn.LabelShortCap + ".",
                    pawn,
                    MessageTypeDefOf.PositiveEvent,
                    historical: false);
            }
        }
    }
}
