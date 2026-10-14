using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_WraithDartHackable : CompProperties_Hackable
    {
        public CompProperties_WraithDartHackable() { compClass = typeof(CompWraithDartHackable); }
    }

    public sealed class CompWraithDartHackable : CompHackable
    {
        protected override void OnHacked(Pawn hacker = null, bool suppressMessages = false)
        {
            // Native CompHackable owns hacking work/progress/lockout. WNG owns the post-hack
            // consequence because a hostile Dart presents a real either/or rescue decision.
            base.OnHacked(hacker, suppressMessages: true);
            if (hacker?.Faction != Faction.OfPlayer || parent == null || parent.Destroyed) return;
            parent.TryGetComp<CompWraithDartMission>()?.NotifyHackCompleted(hacker);
        }
    }

    public sealed class CompProperties_WraithDartMission : CompProperties
    {
        public int retreatDelayTicks = 7200;
        public int releaseRetreatDelayTicks = 600;
        public CompProperties_WraithDartMission() { compClass = typeof(CompWraithDartMission); }
    }

    public sealed class CompWraithDartMission : ThingComp
    {
        private Faction captorFaction;
        private int retreatAtTick = -1;
        private bool hostileMission;
        private bool hackChoicePending;
        private Pawn lastHacker;
        public CompProperties_WraithDartMission Props => (CompProperties_WraithDartMission)props;

        public void BeginHostileMission(Faction captor)
        {
            captorFaction = captor;
            hostileMission = captor != null;
            hackChoicePending = false;
            lastHacker = null;
            int now = Find.TickManager?.TicksGame ?? 0;
            retreatAtTick = hostileMission ? Math.Min(int.MaxValue, now + Math.Max(600, Props.retreatDelayTicks)) : -1;
        }

        public void NotifyHackCompleted(Pawn hacker)
        {
            if (hacker?.Faction != Faction.OfPlayer || parent == null || parent.Destroyed) return;
            hackChoicePending = true;
            lastHacker = hacker;
            // Completing the native hack freezes hostile auto-retreat until the explicit outcome is
            // resolved. The Dart does not silently become player-owned merely because progress hit 100%.
            retreatAtTick = -1;
            OpenHackChoice();
        }

        private void OpenHackChoice()
        {
            if (!hackChoicePending || parent == null || parent.Destroyed || !parent.Spawned || parent.Map == null) return;
            int count = StoredColonists().Count;
            if (count <= 0)
            {
                ResolveCaptureCraft();
                return;
            }

            string names = StoredColonists().Select(p => p.LabelShortCap).ToCommaList(useAnd: true);
            string text = "The Dart's living controls have been breached. Its culling buffer still contains " + names
                + ". The hack can be committed in only one way:\n\n"
                + "• Release captives — rematerialize the exact pawns here, but the Dart remains Wraith property and immediately withdraws.\n"
                + "• Keep Dart — seize the physical shuttle for the colony. The buffered captives complete transfer into Wraith custody and remain recoverable later through the existing holding-site rescue system.\n\n"
                + "This choice is irreversible.";

            Action release = ResolveReleaseCaptives;
            Action capture = ResolveCaptureCraft;
            Find.WindowStack.Add(new Dialog_MessageBox(
                text,
                "Release captives",
                release,
                "Keep Dart",
                capture,
                "Wraith Dart hack",
                buttonADestructive: false,
                acceptAction: null,
                cancelAction: delegate { }));
        }

        public override void CompTick()
        {
            base.CompTick();
            if (hackChoicePending) return;
            if (!hostileMission || parent == null || parent.Destroyed || !parent.Spawned || parent.Map == null || !parent.IsHashIntervalTick(60)) return;
            if (captorFaction == null || parent.Faction != captorFaction || !WraithLineageUtility.IsWraithLineage(captorFaction))
            {
                hostileMission = false;
                retreatAtTick = -1;
                return;
            }
            int now = Find.TickManager?.TicksGame ?? 0;
            if (retreatAtTick >= 0 && now >= retreatAtTick)
            {
                ThingDef leaving = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_WraithDartLeaving");
                if (!WNGWraithDartDeparture.TryDepartWithoutWorldObject(parent as Building_PassengerShuttle, leaving, out _, out string failure))
                {
                    Log.Warning("[WNG] Hostile Dart retreat failed before custody commit: " + failure);
                    retreatAtTick = Math.Min(int.MaxValue, now + 600);
                }
                else
                {
                    hostileMission = false;
                    retreatAtTick = -1;
                }
            }
        }

        private List<Pawn> StoredColonists()
        {
            CompTransporter transporter = parent?.TryGetComp<CompTransporter>();
            return transporter?.innerContainer?.OfType<Pawn>()
                .Where(p => p != null && !p.Dead && WraithCullingUtility.IsEligibleBiologicalHuman(p))
                .ToList() ?? new List<Pawn>();
        }

        private void ResolveReleaseCaptives()
        {
            if (!hackChoicePending || parent == null || parent.Destroyed || !parent.Spawned || parent.Map == null) return;
            CompTransporter transporter = parent.TryGetComp<CompTransporter>();
            if (transporter == null) return;

            Map map = parent.Map;
            IntVec3 near = parent.Position;
            List<Pawn> captives = StoredColonists().ToList();
            List<Pawn> droppedPawns = new List<Pawn>();
            try
            {
                foreach (Pawn pawn in captives)
                {
                    Thing dropped;
                    if (!transporter.innerContainer.TryDrop(pawn, near, map, ThingPlaceMode.Near, 1, out dropped) || dropped != pawn)
                        throw new InvalidOperationException("one or more buffered captives could not be rematerialized");
                    droppedPawns.Add(pawn);
                }
            }
            catch (Exception ex)
            {
                // All-or-none rescue. If a later drop fails, return every already-rematerialized exact
                // Pawn to the same Dart holder so the player can retry the choice without duplication.
                foreach (Pawn pawn in droppedPawns.AsEnumerable().Reverse())
                {
                    if (pawn == null || pawn.Destroyed) continue;
                    try
                    {
                        if (pawn.Spawned) pawn.DeSpawn(DestroyMode.Vanish);
                        if (!transporter.innerContainer.TryAdd(pawn, false) && !pawn.Spawned && !pawn.Destroyed)
                            GenSpawn.Spawn(pawn, WraithCullingUtility.SafeReturnCell(map, near), map);
                    }
                    catch (Exception rollback)
                    {
                        Log.Error("[WNG] Dart captive-release rollback failed for " + pawn + ": " + rollback);
                    }
                }
                Log.Warning("[WNG] Dart captive release remained uncommitted: " + ex.Message);
                Messages.Message("The Dart could not safely rematerialize every buffered captive. Nothing was committed; resolve the hack again after the obstruction is cleared.", parent, MessageTypeDefOf.RejectInput, false);
                return;
            }

            hackChoicePending = false;
            lastHacker = null;
            hostileMission = captorFaction != null && WraithLineageUtility.IsWraithLineage(captorFaction);
            int now = Find.TickManager?.TicksGame ?? 0;
            retreatAtTick = hostileMission ? Math.Min(int.MaxValue, now + Math.Max(60, Props.releaseRetreatDelayTicks)) : -1;
            Messages.Message("The exact culled pawns are rematerialized safely. The colony forfeits the Dart, which begins an emergency Wraith withdrawal.", parent, MessageTypeDefOf.PositiveEvent, false);
        }

        private bool TryStagePlayerDart(out Building_PassengerShuttle staged, out string failure)
        {
            staged = null;
            failure = null;
            ThingDef playerDef = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_WraithDart");
            if (playerDef == null) { failure = "player Dart Def is missing"; return false; }
            staged = ThingMaker.MakeThing(playerDef) as Building_PassengerShuttle;
            if (staged == null) { failure = "player Dart could not be created"; return false; }
            staged.SetFaction(Faction.OfPlayer);
            staged.HitPoints = Math.Min(staged.MaxHitPoints, Math.Max(1, parent.HitPoints));

            CompRefuelable srcFuel = parent.TryGetComp<CompRefuelable>();
            CompRefuelable dstFuel = staged.TryGetComp<CompRefuelable>();
            if (srcFuel != null && dstFuel != null && srcFuel.Fuel > 0f)
            {
                float mult = Math.Max(0.0001f, dstFuel.Props.FuelMultiplierCurrentDifficulty);
                dstFuel.Refuel(Math.Min(dstFuel.Props.fuelCapacity, srcFuel.Fuel) / mult);
            }
            return true;
        }

        private void RollBackPlayerDartConversion(Building_PassengerShuttle source, Building_PassengerShuttle staged, Map map, IntVec3 cell, Rot4 rot)
        {
            try
            {
                CompTransporter src = source?.TryGetComp<CompTransporter>();
                CompTransporter dst = staged?.TryGetComp<CompTransporter>();
                if (src != null && dst != null && dst.GetDirectlyHeldThings().Count > 0)
                    src.GetDirectlyHeldThings().TryAddRangeOrTransfer(dst.GetDirectlyHeldThings(), true, false);
                if (staged != null && staged.Spawned) staged.DeSpawn(DestroyMode.Vanish);
                if (source != null && !source.Destroyed && !source.Spawned) GenSpawn.Spawn(source, cell, map, rot);
                if (staged != null && !staged.Destroyed) staged.Destroy(DestroyMode.Vanish);
            }
            catch (Exception ex)
            {
                Log.Error("[WNG] Dart capture rollback failed: " + ex);
            }
        }

        private void ResolveCaptureCraft()
        {
            if (!hackChoicePending || parent == null || parent.Destroyed || !parent.Spawned || parent.Map == null) return;
            Building_PassengerShuttle source = parent as Building_PassengerShuttle;
            if (source == null || captorFaction == null || !WraithLineageUtility.IsWraithLineage(captorFaction)) return;
            CompTransporter srcTransporter = source.TryGetComp<CompTransporter>();
            if (srcTransporter == null) return;

            if (!TryStagePlayerDart(out Building_PassengerShuttle staged, out string stageFailure))
            {
                Messages.Message("The Dart control conversion could not be staged: " + stageFailure, parent, MessageTypeDefOf.RejectInput, false);
                return;
            }
            CompTransporter dstTransporter = staged.TryGetComp<CompTransporter>();
            if (dstTransporter == null)
            {
                staged.Destroy(DestroyMode.Vanish);
                return;
            }

            Map map = source.Map;
            IntVec3 cell = source.Position;
            Rot4 rot = source.Rotation;
            List<Pawn> captives = StoredColonists();
            bool custodyCommitted = false;
            try
            {
                // The NPC and player variants must use different TransportShipDefs because Odyssey's
                // playerShuttle flag is Def-level. Preserve the actual craft's state while swapping the
                // underlying shuttle Def at the successful-hack boundary.
                dstTransporter.GetDirectlyHeldThings().TryAddRangeOrTransfer(srcTransporter.GetDirectlyHeldThings(), true, false);
                if (srcTransporter.GetDirectlyHeldThings().Count > 0)
                    throw new InvalidOperationException("not all Dart contents could be staged into the captured shuttle");

                source.DeSpawn(DestroyMode.Vanish);
                GenSpawn.Spawn(staged, cell, map, rot);

                // Stephen's explicit design choice: keeping the Dart forfeits the immediate rescue.
                // Exact buffered colonists therefore commit into the existing Wraith custody system;
                // the hacked/captured Dart itself remains physically with the colony.
                WraithCullingCustodyRegistry registry = Current.Game?.GetComponent<WraithCullingCustodyRegistry>();
                if (captives.Count > 0 && (registry == null || !registry.TryRegisterBatchFromHolder(captives, captorFaction)))
                {
                    RollBackPlayerDartConversion(source, staged, map, cell, rot);
                    Messages.Message("The Dart could not safely commit its buffered captives to Wraith custody, so the shuttle seizure was rolled back.", source, MessageTypeDefOf.RejectInput, false);
                    return;
                }
                custodyCommitted = true;

                // From this point the choice is committed. Cleanup/presentation must never roll back
                // custody or recreate a second Dart.
                try { if (!source.Destroyed) source.Destroy(DestroyMode.Vanish); }
                catch (Exception cleanup) { Log.Warning("[WNG] Captured Dart source cleanup failed after commit: " + cleanup); }

                hackChoicePending = false;
                hostileMission = false;
                retreatAtTick = -1;
                lastHacker = null;
                Messages.Message("The colony keeps the hacked Wraith Dart. Its buffered colonists are now in exact Wraith custody and remain eligible for a later rescue.", staged, MessageTypeDefOf.PositiveEvent, false);
            }
            catch (Exception ex)
            {
                if (custodyCommitted)
                {
                    Log.Error("[WNG] Post-commit Dart capture presentation/cleanup failed without rollback: " + ex);
                    hackChoicePending = false;
                    hostileMission = false;
                    retreatAtTick = -1;
                    lastHacker = null;
                    return;
                }
                Log.Error("[WNG] Dart capture transaction rolled back before custody commit: " + ex);
                RollBackPlayerDartConversion(source, staged, map, cell, rot);
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra()) yield return gizmo;
            if (!hackChoicePending || parent == null || parent.Destroyed) yield break;
            yield return new Command_Action
            {
                defaultLabel = "Resolve Wraith Dart hack",
                defaultDesc = "Choose whether this completed hack frees the exact buffered captives or keeps the shuttle. The two outcomes are mutually exclusive.",
                action = OpenHackChoice
            };
        }

        public override string CompInspectStringExtra()
        {
            string text = base.CompInspectStringExtra();
            int count = StoredColonists().Count;
            if (count > 0) text = (text.NullOrEmpty() ? "" : text + "\n") + "Buffered captives: " + count;
            if (hackChoicePending)
                text = (text.NullOrEmpty() ? "" : text + "\n") + "Hack complete: outcome awaiting choice";
            else if (hostileMission && retreatAtTick >= 0)
            {
                int remaining = Math.Max(0, retreatAtTick - (Find.TickManager?.TicksGame ?? 0));
                text = (text.NullOrEmpty() ? "" : text + "\n") + "Culling retreat in: " + remaining.ToStringTicksToPeriod();
            }
            return text;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref captorFaction, "wngDartCaptorFaction");
            Scribe_Values.Look(ref retreatAtTick, "wngDartRetreatAtTick", -1);
            Scribe_Values.Look(ref hostileMission, "wngDartHostileMission", false);
            Scribe_Values.Look(ref hackChoicePending, "wngDartHackChoicePending", false);
            Scribe_References.Look(ref lastHacker, "wngDartLastHacker");
        }
    }

    public sealed class WraithDartMissionRecord : IExposable, IThingHolder
    {
        public Faction captorFaction;
        public int passesRemaining = 2;
        public int nextEventTick;
        public bool landingPending;
        public int landingAttempts;
        private ThingOwner<Pawn> captives;

        public WraithDartMissionRecord() { EnsureHolder(); }
        public IThingHolder ParentHolder => null;
        private void EnsureHolder() { if (captives == null) captives = new ThingOwner<Pawn>(this, false, LookMode.Deep, false); }
        public ThingOwner GetDirectlyHeldThings() { EnsureHolder(); return captives; }
        public void GetChildHolders(List<IThingHolder> outChildren) { EnsureHolder(); ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, captives); }
        public IReadOnlyList<Pawn> Captives { get { EnsureHolder(); return captives.InnerListForReading; } }

        public void ExposeData()
        {
            Scribe_References.Look(ref captorFaction, "captorFaction");
            Scribe_Values.Look(ref passesRemaining, "passesRemaining", 2);
            Scribe_Values.Look(ref nextEventTick, "nextEventTick", 0);
            Scribe_Values.Look(ref landingPending, "landingPending", false);
            Scribe_Values.Look(ref landingAttempts, "landingAttempts", 0);
            EnsureHolder();
            captives.ExposeData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                passesRemaining = Math.Max(0, Math.Min(2, passesRemaining));
                if (passesRemaining == 0) landingPending = true;
            }
        }

        public bool TryCapture(Pawn pawn)
        {
            EnsureHolder();
            if (pawn == null || !pawn.Spawned || pawn.Map == null || !WraithCullingUtility.IsEligibleBiologicalHuman(pawn)) return false;
            Map map = pawn.Map; IntVec3 cell = pawn.Position;
            try
            {
                pawn.jobs?.StopAll();
                pawn.DeSpawn(DestroyMode.Vanish);
                if (captives.TryAdd(pawn, false)) return true;
            }
            catch (Exception ex) { Log.Warning("[WNG] Dart pass capture rolled back: " + ex.Message); }
            if (pawn != null && !pawn.Destroyed && !pawn.Spawned) GenSpawn.Spawn(pawn, WraithCullingUtility.SafeReturnCell(map, cell), map);
            return false;
        }

        public void ReleaseAll(Map map, IntVec3 near)
        {
            EnsureHolder();
            if (map == null) return;
            foreach (Pawn pawn in captives.InnerListForReading.OfType<Pawn>().ToList())
            {
                Pawn dropped;
                captives.TryDrop(pawn, near, map, ThingPlaceMode.Near, out dropped);
            }
        }
    }

    public sealed class MapComponent_WraithDartCulling : MapComponent
    {
        private const int MaxCaptives = 3;
        private const int MaxPerPass = 2;
        private const float BeamHalfWidth = 4f;
        private const float CaptureChance = 0.58f;
        private const int MaxLandingAttempts = 20;
        private List<WraithDartMissionRecord> missions = new List<WraithDartMissionRecord>();

        public MapComponent_WraithDartCulling(Map map) : base(map) { }
        public bool HasActiveMission => missions != null && missions.Any(m => m != null);

        public bool Schedule(Faction captor)
        {
            if (captor == null || !WraithLineageUtility.IsWraithLineage(captor) || HasActiveMission) return false;
            int now = Find.TickManager?.TicksGame ?? 0;
            missions.Add(new WraithDartMissionRecord { captorFaction = captor, passesRemaining = 2, nextEventTick = Math.Min(int.MaxValue, now + 180) });
            return true;
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (map == null)
                return;
            if (missions == null || missions.Count == 0 || Find.TickManager == null) return;
            int now = Find.TickManager.TicksGame;
            for (int i = missions.Count - 1; i >= 0; i--)
            {
                WraithDartMissionRecord r = missions[i];
                if (r == null) { missions.RemoveAt(i); continue; }
                if (now < r.nextEventTick) continue;
                if (r.captorFaction == null || r.captorFaction.defeated || !WraithLineageUtility.IsWraithLineage(r.captorFaction))
                {
                    r.ReleaseAll(map, map.Center); missions.RemoveAt(i); continue;
                }
                if (!r.landingPending && r.passesRemaining > 0)
                {
                    PerformPass(r);
                    r.passesRemaining--;
                    r.nextEventTick = Math.Min(int.MaxValue, now + 420);
                    if (r.passesRemaining <= 0) r.landingPending = true;
                    continue;
                }
                if (r.landingPending && TrySpawnFinalDart(r)) { missions.RemoveAt(i); continue; }
                r.landingAttempts++;
                if (r.landingAttempts >= MaxLandingAttempts)
                {
                    Log.Warning("[WNG] Dart completed both culling passes but could not land after bounded retries; exact staged captives were returned to the colony map.");
                    r.ReleaseAll(map, map.Center); missions.RemoveAt(i);
                }
                else r.nextEventTick = Math.Min(int.MaxValue, now + 600);
            }
        }

        private void PerformPass(WraithDartMissionRecord r)
        {
            int margin = 6;
            int z1 = Rand.RangeInclusive(margin, Math.Max(margin, map.Size.z - margin - 1));
            int z2 = Mathf.Clamp(z1 + Rand.RangeInclusive(-12, 12), margin, Math.Max(margin, map.Size.z - margin - 1));
            IntVec3 a = new IntVec3(2, 0, z1);
            IntVec3 b = new IntVec3(Math.Max(2, map.Size.x - 3), 0, z2);
            SpawnFlyover(new IntVec3((a.x + b.x) / 2, 0, (a.z + b.z) / 2));
            int remaining = MaxCaptives - r.Captives.Count;
            int wanted = Math.Min(MaxPerPass, Math.Max(0, remaining));
            if (wanted <= 0) return;
            List<Pawn> prey = map.mapPawns.FreeColonistsSpawned
                .Where(p => p != null && !p.Dead && !p.Downed && p.Spawned && p.MentalState == null)
                .Where(WraithCullingUtility.IsEligibleBiologicalHuman)
                .Where(p => DistanceToPassLine(p.Position, a, b) <= BeamHalfWidth)
                .OrderBy(p => DistanceToPassLine(p.Position, a, b))
                .Where(_ => Rand.Chance(CaptureChance)).Take(wanted).ToList();
            foreach (Pawn pawn in prey)
            {
                IntVec3 cell = pawn.Position;
                if (!r.TryCapture(pawn)) continue;
                DefDatabase<SoundDef>.GetNamedSilentFail("WNG_WraithDartCulling")?.PlayOneShot(new TargetInfo(cell, map));
            }
        }

        private void SpawnFlyover(IntVec3 cell)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_WraithDartFlyover");
            if (def == null || !cell.InBounds(map)) return;
            try { GenSpawn.Spawn(SkyfallerMaker.MakeSkyfaller(def), cell, map, Rot4.East); }
            catch (Exception ex) { Log.Warning("[WNG] Dart flyover visual failed without cancelling the real culling pass: " + ex.Message); }
        }

        private bool TrySpawnFinalDart(WraithDartMissionRecord r)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_WraithDart_NPC");
            if (def == null) return false;
            IntVec3 cell;
            if (!CellFinder.TryFindRandomCellNear(map.Center, map, 35, c => c.Standable(map) && !c.Roofed(map), out cell)) return false;
            Thing craft = ThingMaker.MakeThing(def);
            craft.SetFaction(r.captorFaction);
            CompTransporter transporter = craft.TryGetComp<CompTransporter>();
            if (transporter == null) return false;
            try { GenSpawn.Spawn(craft, cell, map, Rot4.East); }
            catch { if (craft.Spawned) craft.Destroy(DestroyMode.Vanish); return false; }

            Pawn pilot = null;
            try
            {
                PawnKindDef pilotKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_WraithHunter");
                if (pilotKind == null) throw new InvalidOperationException("Wraith Dart pilot kind is missing");
                pilot = PawnGenerator.GeneratePawn(pilotKind, r.captorFaction);
                if (pilot == null || !transporter.innerContainer.TryAdd(pilot, false))
                    throw new InvalidOperationException("physical Wraith pilot could not board the Dart");
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Final Dart landing rolled back because its physical pilot could not be staged: " + ex.Message);
                if (pilot != null && pilot.holdingOwner == transporter.innerContainer) transporter.innerContainer.Remove(pilot);
                if (pilot != null && !pilot.Destroyed) pilot.Destroy(DestroyMode.Vanish);
                craft.Destroy(DestroyMode.Vanish);
                return false;
            }

            List<Pawn> moved = new List<Pawn>();
            foreach (Pawn pawn in r.Captives.ToList())
            {
                if (!transporter.innerContainer.TryAddOrTransfer(pawn, false))
                {
                    foreach (Pawn prior in moved) r.GetDirectlyHeldThings().TryAddOrTransfer(prior, false);
                    if (pilot != null && pilot.holdingOwner == transporter.innerContainer) transporter.innerContainer.Remove(pilot);
                    if (pilot != null && !pilot.Destroyed) pilot.Destroy(DestroyMode.Vanish);
                    craft.Destroy(DestroyMode.Vanish);
                    return false;
                }
                moved.Add(pawn);
            }
            craft.TryGetComp<CompWraithDartMission>()?.BeginHostileMission(r.captorFaction);
            return true;
        }

        private static float DistanceToPassLine(IntVec3 point, IntVec3 a, IntVec3 b)
        {
            float dx = b.x - a.x, dz = b.z - a.z;
            float lengthSq = dx * dx + dz * dz;
            if (lengthSq < 0.001f) return point.DistanceTo(a);
            return Math.Abs(dx * (a.z - point.z) - (a.x - point.x) * dz) / (float)Math.Sqrt(lengthSq);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref missions, "wngDartCullingMissions", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) missions ??= new List<WraithDartMissionRecord>();
        }
    }

    public sealed class IncidentWorker_WraithDartCulling : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            Map map = parms?.target as Map;
            return map != null && map.IsPlayerHome && !map.GetComponent<MapComponent_WraithDartCulling>().HasActiveMission
                && WraithLineageUtility.ActiveLineages().Any(f => f != null && !f.defeated && Faction.OfPlayer != null && f.HostileTo(Faction.OfPlayer));
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = parms?.target as Map;
            if (map == null) return false;
            List<Faction> factions = WraithLineageUtility.ActiveLineages().Where(f => f != null && !f.defeated && Faction.OfPlayer != null && f.HostileTo(Faction.OfPlayer)).ToList();
            if (factions.Count == 0) return false;
            Faction captor = factions.RandomElement();
            if (!map.GetComponent<MapComponent_WraithDartCulling>().Schedule(captor)) return false;
            Find.LetterStack.ReceiveLetter("Wraith Dart culling run", "A " + captor.Name + " Dart is making two culling passes over the colony. Anyone absorbed remains the same physical pawn. After the second pass the Dart will land briefly before escape. Hacking that physical shuttle forces a choice: rematerialize its buffered captives and forfeit the Dart, or keep the Dart while those captives complete transfer into Wraith custody for later rescue.", LetterDefOf.ThreatBig, new TargetInfo(map.Center, map));
            return true;
        }
    }

    public static class WNGWraithDartDeparture
    {
        public static bool TryDepartWithoutWorldObject(Building_PassengerShuttle shuttle, ThingDef leavingDef, out FlyShipLeaving leaving, out string failure)
        {
            leaving = null; failure = null;
            if (shuttle == null || shuttle.Destroyed || !shuttle.Spawned || shuttle.Map == null) { failure = "shuttle is not spawned"; return false; }
            CompTransporter transporter = shuttle.TryGetComp<CompTransporter>();
            CompLaunchable launchable = shuttle.TryGetComp<CompLaunchable>();
            if (transporter == null || launchable == null || leavingDef == null) { failure = "native shuttle transport contract is incomplete"; return false; }
            Map map = shuttle.Map; IntVec3 position = shuttle.Position; Rot4 rotation = shuttle.Rotation; ActiveTransporter active = null;
            try
            {
                ThingDef activeDef = launchable.Props.activeTransporterDef ?? ThingDefOf.ActiveDropPod;
                active = ThingMaker.MakeThing(activeDef) as ActiveTransporter;
                if (active == null) throw new InvalidOperationException("active transporter could not be created");
                active.Contents = new ActiveTransporterInfo();
                active.Contents.innerContainer.TryAddRangeOrTransfer(transporter.GetDirectlyHeldThings(), true, false);
                if (transporter.GetDirectlyHeldThings().Count > 0) throw new InvalidOperationException("not all Dart contents transferred to native departure holder");
                active.Contents.sentTransporterDef = shuttle.def; active.Rotation = rotation; active.Contents.SetShuttle(shuttle);
                leaving = SkyfallerMaker.MakeSkyfaller(leavingDef, active) as FlyShipLeaving;
                if (leaving == null) throw new InvalidOperationException("leaving skyfaller could not be created");
                leaving.groupID = transporter.groupID; leaving.createWorldObject = false;
                GenSpawn.Spawn(leaving, position, map, rotation); return true;
            }
            catch (Exception ex)
            {
                failure = ex.Message;
                try
                {
                    if (leaving != null && leaving.Spawned) leaving.Destroy(DestroyMode.Vanish);
                    if (active?.Contents != null)
                    {
                        Thing containedShuttle = active.Contents.GetShuttle(); if (containedShuttle != null) active.Contents.RemoveShuttle();
                        transporter.GetDirectlyHeldThings().TryAddRangeOrTransfer(active.Contents.innerContainer, true, false);
                    }
                    if (!shuttle.Destroyed && !shuttle.Spawned) GenSpawn.Spawn(shuttle, position, map, rotation);
                    if (active != null && !active.Destroyed) active.Destroy(DestroyMode.Vanish);
                }
                catch (Exception rollback) { Log.Error("[WNG] Dart departure rollback also failed: " + rollback); }
                leaving = null; return false;
            }
        }
    }

    public sealed class Skyfaller_WNGWraithDartFlyover : Skyfaller
    {
        protected override void Impact() => Destroy(DestroyMode.Vanish);
        protected override void LeaveMap() => Destroy(DestroyMode.Vanish);
    }

    public sealed class Skyfaller_WNGWraithDartLeaving : PassengerShuttleLeaving
    {
        protected override void LeaveMap()
        {
            if (!createWorldObject && Contents != null)
            {
                Faction captor = Contents.GetShuttle()?.Faction;
                if (captor != null && WraithLineageUtility.IsWraithLineage(captor) && Faction.OfPlayer != null && captor.HostileTo(Faction.OfPlayer))
                {
                    List<Pawn> captives = Contents.innerContainer.OfType<Pawn>().Where(p => p != null && !p.Dead && WraithCullingUtility.IsEligibleBiologicalHuman(p)).ToList();
                    WraithCullingCustodyRegistry registry = Current.Game?.GetComponent<WraithCullingCustodyRegistry>();
                    if (captives.Count > 0 && (registry == null || !registry.TryRegisterBatchFromHolder(captives, captor)))
                    {
                        Log.Error("[WNG] Hostile Dart reached the map-exit boundary but exact captive custody transfer failed; departure is held rather than deleting or proxying captives.");
                        return;
                    }
                    if (captives.Count > 0)
                    {
                        string names = captives.Select(p => p.LabelShortCap).ToCommaList(useAnd: true);
                        Find.LetterStack.ReceiveLetter("Colonists culled", captor.Name + " escaped with " + names + ". The exact pawns are now in Wraith custody and can later be recovered through the rescue system.", LetterDefOf.ThreatBig);
                    }
                }
            }
            base.LeaveMap();
        }
    }
}
