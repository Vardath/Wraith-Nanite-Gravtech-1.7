using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_GoauldTransportRings : CompProperties
    {
        public int cooldownTicks = 300;
        public int arrivalRadius = 4;
        public bool requirePower = true;

        public CompProperties_GoauldTransportRings()
        {
            compClass = typeof(CompGoauldTransportRings);
        }
    }

    /// <summary>
    /// WNG-owned Goa'uld transport rings layered on RimWorld's native CompTransporter.
    /// CompTransporter remains the exact cargo owner/loading UI. A ring activation is an
    /// all-or-none transfer between compatible loaded-map endpoints; failed placement rolls
    /// every exact object back to the sender rather than leaving a half-transported manifest.
    /// </summary>
    public sealed class CompGoauldTransportRings : ThingComp
    {
        private int nextReadyTick;

        // Presentation only: retains existing transactional transport logic.
        private const int RingRiseTicks = 65;
        private const int RingHoldTicks = 32;
        private const int RingRetractTicks = 70;
        private const int RingVisualTicks = RingRiseTicks + RingHoldTicks + RingRetractTicks;
        private const string RingEffectTexturePath = "Things/Building/Goauld/WNG_GoauldRingEffect";
        private int ringVisualStartTick = -1;
        // Each installed platform lazily resolves the pooled material during
        // normal main-thread drawing; no Unity Material is cached in a
        // static field that can be touched during early startup.
        private Material ringMaterial;
        private static Mesh ringMesh;

        private CompProperties_GoauldTransportRings Props => (CompProperties_GoauldTransportRings)props;
        private CompTransporter Transporter => parent.TryGetComp<CompTransporter>();
        private CompPowerTrader Power => parent.TryGetComp<CompPowerTrader>();

        public bool Powered => !Props.requirePower || (Power != null && Power.PowerOn);
        public bool Ready => Find.TickManager.TicksGame >= nextReadyTick;

        public bool ReceiveReadyFor(CompGoauldTransportRings sender)
        {
            if (sender == null || sender == this || parent?.Spawned != true || !Powered || !Ready)
                return false;
            if (parent.Faction != sender.parent.Faction)
                return false;

            CompTransporter receiver = Transporter;
            return receiver != null && !receiver.innerContainer.Any && !receiver.AnythingLeftToLoad;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
                yield return gizmo;

            if (parent?.Faction != Faction.OfPlayer)
                yield break;

            CompTransporter transporter = Transporter;
            Command_Action activate = new Command_Action
            {
                defaultLabel = "Activate transport rings",
                defaultDesc = "Transport the exact loaded people, animals, mechs and cargo to another compatible powered ring platform on a currently loaded map. The activation commits only if the whole manifest can rematerialize.",
                icon = ContentFinder<Texture2D>.Get("Things/Building/Goauld/WNG_GoauldTransportRings", false),
                action = OpenDestinationDialog
            };

            if (transporter == null)
                activate.Disable("Transporter component unavailable.");
            else if (!Powered)
                activate.Disable("Transport rings require power.");
            else if (!Ready)
                activate.Disable("Transport rings are recharging for " + (nextReadyTick - Find.TickManager.TicksGame).ToStringTicksToPeriod() + ".");
            else if (!transporter.innerContainer.Any)
                activate.Disable("Load people, animals, mechs or cargo into the transport rings first.");
            else if (transporter.AnythingLeftToLoad)
                activate.Disable("Finish loading the assigned transport-ring cargo first.");
            else if (transporter.OverMassCapacity)
                activate.Disable("The loaded transport mass exceeds ring capacity.");
            else if (!FindDestinations().Any())
                activate.Disable("No compatible powered transport rings are available on currently loaded maps.");

            yield return activate;
        }

        private void OpenDestinationDialog()
        {
            List<CompGoauldTransportRings> destinations = FindDestinations();
            if (destinations.Count == 0)
            {
                Messages.Message("No compatible powered transport-ring destination is available on a currently loaded map.", parent, MessageTypeDefOf.RejectInput, false);
                return;
            }

            List<Dialog_WNGChoiceList.Choice> choices = new List<Dialog_WNGChoiceList.Choice>();
            foreach (CompGoauldTransportRings destination in destinations)
            {
                CompGoauldTransportRings captured = destination;
                choices.Add(new Dialog_WNGChoiceList.Choice(
                    DestinationLabel(captured),
                    delegate { TryTransportTo(captured); }));
            }

            Find.WindowStack.Add(new Dialog_WNGChoiceList(
                "Transport rings destination",
                "Choose the compatible powered ring platform that will receive the exact loaded manifest.",
                choices));
        }

        private List<CompGoauldTransportRings> FindDestinations()
        {
            List<CompGoauldTransportRings> result = new List<CompGoauldTransportRings>();
            foreach (Map map in Find.Maps)
            {
                if (map?.listerThings?.AllThings == null)
                    continue;

                List<Thing> things = map.listerThings.AllThings;
                for (int i = 0; i < things.Count; i++)
                {
                    CompGoauldTransportRings rings = things[i]?.TryGetComp<CompGoauldTransportRings>();
                    if (rings != null && rings.ReceiveReadyFor(this))
                        result.Add(rings);
                }
            }

            return result
                .OrderBy(r => MapLabel(r.parent.Map), StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.parent.Position.x)
                .ThenBy(r => r.parent.Position.z)
                .ToList();
        }

        private static string DestinationLabel(CompGoauldTransportRings rings)
        {
            Map map = rings.parent.Map;
            string sameMap = map == Find.CurrentMap ? " (current map)" : string.Empty;
            return MapLabel(map) + sameMap + " — rings at " + rings.parent.Position.x + ", " + rings.parent.Position.z;
        }

        private static string MapLabel(Map map)
        {
            if (map == null)
                return "Unknown map";
            string label = map.info?.parent?.Label;
            return label.NullOrEmpty() ? "Map " + map.uniqueID : label;
        }

        private void TryTransportTo(CompGoauldTransportRings destination)
        {
            CompTransporter source = Transporter;
            if (source == null || !parent.Spawned || !Powered || !Ready)
                return;
            if (source.AnythingLeftToLoad || !source.innerContainer.Any || source.OverMassCapacity)
                return;
            if (destination == null || !destination.ReceiveReadyFor(this))
            {
                Messages.Message("That transport-ring destination is no longer available.", parent, MessageTypeDefOf.RejectInput, false);
                return;
            }

            CompTransporter receiver = destination.Transporter;
            List<Thing> manifest = source.innerContainer.ToList();
            List<IntVec3> arrivalCells = PlanArrivalCells(destination, manifest.Count);
            if (arrivalCells == null)
            {
                Messages.Message("The receiving ring chamber cannot safely rematerialize the complete loaded manifest. Nothing was transported.", destination.parent, MessageTypeDefOf.RejectInput, false);
                return;
            }

            // Stage exact Things into the empty receiver holder first. No map object is changed
            // until the complete manifest has been transferred holder-to-holder.
            List<Thing> staged = new List<Thing>();
            for (int i = 0; i < manifest.Count; i++)
            {
                Thing thing = manifest[i];
                int count = thing.stackCount;
                Thing moved;
                int transferred = source.innerContainer.TryTransferToContainer(thing, receiver.innerContainer, count, out moved, false);
                if (transferred != count || moved == null || !ReferenceEquals(moved, thing))
                {
                    RollbackStaged(receiver, source);
                    Messages.Message("Transport-ring staging failed. The exact loaded manifest remained at the sender.", parent, MessageTypeDefOf.RejectInput, false);
                    return;
                }
                staged.Add(moved);
            }

            List<Thing> spawned = new List<Thing>();
            bool success = true;
            try
            {
                for (int i = 0; i < staged.Count; i++)
                {
                    Thing thing = staged[i];
                    Thing resultingThing;
                    int count = thing.stackCount;
                    if (!receiver.innerContainer.TryDrop(thing, arrivalCells[i], destination.parent.Map, ThingPlaceMode.Direct, count, out resultingThing, null, null)
                        || resultingThing == null || !ReferenceEquals(resultingThing, thing))
                    {
                        success = false;
                        break;
                    }

                    receiver.Notify_ThingRemoved(thing);
                    spawned.Add(thing);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Transport-ring rematerialization failed before commit: " + ex.Message);
                success = false;
            }

            if (!success)
            {
                RollbackSpawned(spawned, source);
                RollbackStaged(receiver, source);
                Messages.Message("Transport-ring rematerialization was interrupted. The exact manifest was restored to the sender.", parent, MessageTypeDefOf.CautionInput, false);
                return;
            }

            // Functional commit: every exact manifest object is now physically spawned at the
            // destination. Cooldowns and loading state are committed only after that boundary.
            int now = Find.TickManager.TicksGame;
            nextReadyTick = now + Math.Max(1, Props.cooldownTicks);
            destination.nextReadyTick = now + Math.Max(1, destination.Props.cooldownTicks);
            source.CancelLoad();

            foreach (Thing thing in spawned)
            {
                Pawn pawn = thing as Pawn;
                pawn?.Notify_Teleported();
            }

            try
            {
                // Effect starts only once every exact transferred object is safely committed.
                BeginRingVisual();
                destination.BeginRingVisual();
                Messages.Message("Transport complete to " + DestinationLabel(destination) + ": " + spawned.Count + " exact loaded entries rematerialized.", destination.parent, MessageTypeDefOf.PositiveEvent, false);
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Transport-ring transfer committed, but presentation failed: " + ex.Message);
            }
        }

        private void BeginRingVisual()
        {
            ringVisualStartTick = Find.TickManager.TicksGame;
            if (parent?.Spawned == true && parent.Map != null)
                FleckMaker.ThrowLightningGlow(parent.DrawPos, parent.Map, 2.4f);
        }

        public override void PostDraw()
        {
            base.PostDraw();
            if (ringVisualStartTick < 0 || parent?.Spawned != true)
                return;

            int elapsed = Find.TickManager.TicksGame - ringVisualStartTick;
            if (elapsed < 0 || elapsed >= RingVisualTicks)
                return;

            if (ringMaterial == null)
            {
                Texture2D texture = ContentFinder<Texture2D>.Get(RingEffectTexturePath, false);
                if (texture == null)
                    return;
                ringMaterial = MaterialPool.MatFrom(texture, ShaderDatabase.Transparent, Color.white);
            }

            if (ringMesh == null)
                ringMesh = MeshPool.GridPlane(new Vector2(3.0f, 3.0f));

            // In the overhead camera, screen-north offsets make these elliptical
            // horizontal annuli read as stacked rings rising above the floor.
            // The platform remains flat and unobstructed outside this brief effect.
            Vector3 center = parent.DrawPos;
            float altitude = Altitudes.AltitudeFor(AltitudeLayer.MoteOverhead);
            for (int ring = 0; ring < 5; ring++)
            {
                float rise = Mathf.Clamp01((elapsed - ring * 7f) / (RingRiseTicks - ring * 7f));
                float retract = Mathf.Clamp01((elapsed - RingRiseTicks - RingHoldTicks -
                    (4 - ring) * 7f) / (RingRetractTicks - 28f));
                float lift = Mathf.SmoothStep(0f, 1f, rise) *
                    (1f - Mathf.SmoothStep(0f, 1f, retract));
                if (lift <= 0.001f)
                    continue;

                Vector3 loc = center;
                loc.y = altitude + ring * 0.002f;
                loc.z += lift * (0.16f + ring * 0.26f);
                Graphics.DrawMesh(ringMesh, loc, Quaternion.identity, ringMaterial, 0);
            }
        }

        private List<IntVec3> PlanArrivalCells(CompGoauldTransportRings destination, int required)
        {
            Map map = destination.parent.Map;
            IntVec3 center = destination.parent.Position;
            Room room = center.GetRoom(map);
            int radius = Math.Max(1, destination.Props.arrivalRadius);
            CellRect platform = destination.parent.OccupiedRect();

            List<IntVec3> candidates = new List<IntVec3>();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, radius, true))
            {
                if (!cell.InBounds(map) || platform.Contains(cell) || !cell.Walkable(map))
                    continue;
                if (room != null && cell.GetRoom(map) != room)
                    continue;

                bool occupied = false;
                foreach (Thing existing in map.thingGrid.ThingsListAt(cell))
                {
                    if (existing is Pawn || existing.def.category == ThingCategory.Item)
                    {
                        occupied = true;
                        break;
                    }
                }
                if (!occupied)
                    candidates.Add(cell);
            }

            candidates = candidates
                .OrderBy(c => c.DistanceToSquared(center))
                .ThenBy(c => c.x)
                .ThenBy(c => c.z)
                .ToList();

            if (candidates.Count < required)
                return null;
            return candidates.Take(required).ToList();
        }

        private static void RollbackStaged(CompTransporter receiver, CompTransporter source)
        {
            if (receiver?.innerContainer == null || source?.innerContainer == null)
                return;

            List<Thing> remaining = receiver.innerContainer.ToList();
            for (int i = remaining.Count - 1; i >= 0; i--)
            {
                Thing thing = remaining[i];
                int count = thing.stackCount;
                Thing moved;
                int transferred = receiver.innerContainer.TryTransferToContainer(thing, source.innerContainer, count, out moved, false);
                if (transferred != count)
                    Log.Error("[WNG] Transport-ring rollback could not return " + thing.ToStringSafe() + " to its sender holder.");
            }
        }

        private static void RollbackSpawned(List<Thing> spawned, CompTransporter source)
        {
            for (int i = spawned.Count - 1; i >= 0; i--)
            {
                Thing thing = spawned[i];
                if (thing == null || thing.Destroyed)
                    continue;

                if (thing.Spawned)
                    thing.DeSpawn(DestroyMode.Vanish);
                if (!source.innerContainer.TryAdd(thing, false))
                    Log.Error("[WNG] Transport-ring rollback could not restore exact object " + thing.ToStringSafe() + " to the sender holder.");
            }
        }

        public override string CompInspectStringExtra()
        {
            if (!Powered)
                return "Transport rings: unpowered";
            if (!Ready)
                return "Transport rings: recharging (" + (nextReadyTick - Find.TickManager.TicksGame).ToStringTicksToPeriod() + ")";
            return "Transport rings: ready";
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref nextReadyTick, "wngRingNextReadyTick", 0);
        }
    }
}
