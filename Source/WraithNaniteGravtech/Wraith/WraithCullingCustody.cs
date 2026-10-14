using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Def-driven tuning for the craft-independent Wraith culling custody/rescue layer.
    /// Individual craft keep their own local capacity/timing. This Def only controls what happens
    /// after an exact captive has been handed into persistent Wraith custody.
    /// </summary>
    public sealed class WraithCullingTuningDef : Def
    {
        public int custodyCheckIntervalTicks = 600;
        public int rescueOfferDelayTicks = 60000;
        public int rescueRetryDelayTicks = 120000;
        public int rescueSiteDurationTicks = 720000;
        public int maxCaptivesPerRescueSite = 3;
        public int rescueSiteMinDistance = 6;
        public int rescueSiteMaxDistance = 18;
        public float baseRescueThreatPoints = 450f;
        public float threatPerCaptive = 175f;
        public float threatPerFailure = 250f;
        public int captivityPressureIntervalTicks = 120000;
        public int captiveAgeYearsPerPressure = 2;
        public int maxCaptivityStage = 4;
        public float threatPerCaptivityStage = 125f;
        public float storytellerThreatFactor = 0.65f;
    }

    /// <summary>
    /// One exact abductee record. The Pawn reference is metadata only: while off-map, the Pawn's
    /// actual ownership is the registry ThingOwner. While a rescue map is loaded, the same Pawn
    /// object is temporarily owned by that map. No proxy PawnKind/name record is ever authoritative.
    /// </summary>
    public sealed class WraithAbducteeRecord : IExposable
    {
        public Pawn pawn;
        public Faction captorFaction;
        public Faction originalFaction;
        public Faction originalGuestHost;
        public GuestStatus originalGuestStatus = GuestStatus.Guest;
        public int capturedTick = -1;
        public int nextRescueOfferTick = -1;
        public int rescueSiteId = -1;
        public int rescueSiteExpiryTick = -1;
        public int rescueFailures;
        public int captivityStage;
        public int nextCaptivityPressureTick = -1;
        public bool custodyAppliedFeedingStock;
        public bool custodyAppliedExperimentSubject;
        public bool custodyAppliedConditioning;
        public bool nativeEnthrallmentCommitted;
        public bool releasedAtSite;

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_References.Look(ref captorFaction, "captorFaction");
            Scribe_References.Look(ref originalFaction, "originalFaction");
            Scribe_References.Look(ref originalGuestHost, "originalGuestHost");
            Scribe_Values.Look(ref originalGuestStatus, "originalGuestStatus", GuestStatus.Guest);
            Scribe_Values.Look(ref capturedTick, "capturedTick", -1);
            Scribe_Values.Look(ref nextRescueOfferTick, "nextRescueOfferTick", -1);
            Scribe_Values.Look(ref rescueSiteId, "rescueSiteId", -1);
            Scribe_Values.Look(ref rescueSiteExpiryTick, "rescueSiteExpiryTick", -1);
            Scribe_Values.Look(ref rescueFailures, "rescueFailures", 0);
            Scribe_Values.Look(ref captivityStage, "captivityStage", 0);
            Scribe_Values.Look(ref nextCaptivityPressureTick, "nextCaptivityPressureTick", -1);
            Scribe_Values.Look(ref custodyAppliedFeedingStock, "custodyAppliedFeedingStock", false);
            Scribe_Values.Look(ref custodyAppliedExperimentSubject, "custodyAppliedExperimentSubject", false);
            Scribe_Values.Look(ref custodyAppliedConditioning, "custodyAppliedConditioning", false);
            Scribe_Values.Look(ref nativeEnthrallmentCommitted, "nativeEnthrallmentCommitted", false);
            Scribe_Values.Look(ref releasedAtSite, "releasedAtSite", false);
        }
    }

    public static class WraithCullingUtility
    {
        /// <summary>
        /// Culling targets are biological humanlikes. Wraith are not livestock, and the two known
        /// future nanite-humanoid xenotype identities are excluded now so the custody primitive does
        /// not later become an accidental anti-Asuran feeding path.
        /// </summary>
        public static bool IsEligibleBiologicalHuman(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.RaceProps == null)
                return false;
            if (!pawn.RaceProps.Humanlike || !pawn.RaceProps.IsFlesh || pawn.RaceProps.IsMechanoid)
                return false;
            if (WraithHiveEcologyUtility.IsWraith(pawn))
                return false;

            string xenotype = pawn.genes?.Xenotype?.defName;
            if (xenotype == "WNG_NanitePrecursor" || xenotype == "WNG_HumanFormReplicator")
                return false;

            return true;
        }

        public static IntVec3 SafeReturnCell(Map map, IntVec3 preferred)
        {
            if (map == null)
                return IntVec3.Invalid;
            if (preferred.IsValid && preferred.InBounds(map) && preferred.Standable(map))
                return preferred;
            return CellFinder.RandomClosewalkCellNear(map.Center, map, 12);
        }
    }

    /// <summary>
    /// Craft-independent exact-pawn custody authority for Wraith culling.
    ///
    /// Ownership contract:
    /// - spawned Pawn -> transactional DeSpawn -> this ThingOwner;
    /// - another ThingOwner -> native TryAddOrTransfer -> this ThingOwner;
    /// - this ThingOwner -> native TryDrop -> rescue map;
    /// - rescue map about to disappear -> transactional DeSpawn -> this ThingOwner again.
    ///
    /// There is never a delete/regenerate proxy step, and this component never uses WorldPawns as a
    /// second storage owner. Later Darts should transfer their exact contained Pawn into this owner.
    /// </summary>
    public sealed class WraithCullingCustodyRegistry : GameComponent, IThingHolder
    {
        private ThingOwner<Pawn> custody;
        private List<WraithAbducteeRecord> records = new List<WraithAbducteeRecord>();

        public WraithCullingCustodyRegistry(Game game)
        {
            custody = new ThingOwner<Pawn>(this, false, LookMode.Deep, false);
        }

        public IThingHolder ParentHolder => null;

        public ThingOwner GetDirectlyHeldThings()
        {
            EnsureCustody();
            return custody;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            EnsureCustody();
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, custody);
        }

        public IReadOnlyList<WraithAbducteeRecord> Records => records;

        private WraithCullingTuningDef Tuning
        {
            get
            {
                return DefDatabase<WraithCullingTuningDef>.GetNamedSilentFail("WNG_WraithCullingTuning")
                    ?? DefDatabase<WraithCullingTuningDef>.AllDefsListForReading.FirstOrDefault();
            }
        }

        private void EnsureCustody()
        {
            if (custody == null)
                custody = new ThingOwner<Pawn>(this, false, LookMode.Deep, false);
            if (records == null)
                records = new List<WraithAbducteeRecord>();
        }

        public bool Contains(Pawn pawn)
        {
            EnsureCustody();
            return pawn != null && custody.Contains(pawn);
        }

        public WraithAbducteeRecord RecordFor(Pawn pawn)
        {
            if (pawn == null || records == null)
                return null;
            return records.FirstOrDefault(r => r != null && r.pawn == pawn);
        }

        private WraithAbducteeRecord NewRecord(Pawn pawn, Faction captorFaction)
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            int delay = Math.Max(1, Tuning?.rescueOfferDelayTicks ?? 60000);
            int pressure = Math.Max(60000, Tuning?.captivityPressureIntervalTicks ?? 120000);
            return new WraithAbducteeRecord
            {
                pawn = pawn,
                captorFaction = captorFaction,
                originalFaction = pawn?.Faction,
                originalGuestHost = pawn?.guest?.HostFaction,
                originalGuestStatus = pawn?.guest?.GuestStatus ?? GuestStatus.Guest,
                capturedTick = now,
                nextRescueOfferTick = now + delay,
                rescueSiteId = -1,
                rescueSiteExpiryTick = -1,
                rescueFailures = 0,
                captivityStage = 0,
                nextCaptivityPressureTick = now + pressure,
                custodyAppliedFeedingStock = false,
                custodyAppliedExperimentSubject = false,
                custodyAppliedConditioning = false,
                nativeEnthrallmentCommitted = false,
                releasedAtSite = false
            };
        }

        /// <summary>
        /// Transactionally moves one exact spawned Pawn into persistent Wraith custody.
        /// If custody rejects the Pawn, the same object is respawned on its original map.
        /// </summary>
        public bool TryRegisterFromMap(Pawn pawn, Faction captorFaction)
        {
            EnsureCustody();
            if (!WraithCullingUtility.IsEligibleBiologicalHuman(pawn) || !pawn.Spawned || pawn.Map == null)
                return false;
            if (!WraithLineageUtility.IsWraithLineage(captorFaction) || RecordFor(pawn) != null)
                return false;

            Map sourceMap = pawn.Map;
            IntVec3 sourceCell = pawn.Position;
            WraithAbducteeRecord record = NewRecord(pawn, captorFaction);

            try
            {
                pawn.jobs?.StopAll();
                pawn.DeSpawn(DestroyMode.Vanish);
                if (!custody.TryAdd(pawn, false))
                {
                    GenSpawn.Spawn(pawn, WraithCullingUtility.SafeReturnCell(sourceMap, sourceCell), sourceMap);
                    return false;
                }

                records.Add(record);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[WNG] Exact Wraith culling capture rolled back: " + ex.Message);
                if (custody.Contains(pawn))
                    custody.Remove(pawn);
                if (pawn != null && !pawn.Destroyed && !pawn.Spawned)
                    GenSpawn.Spawn(pawn, WraithCullingUtility.SafeReturnCell(sourceMap, sourceCell), sourceMap);
                return false;
            }
        }

        /// <summary>
        /// Native owner-to-owner handoff for a later Dart/transport buffer. This method deliberately
        /// uses TryAddOrTransfer while the source still owns the Pawn, avoiding the old remove-first
        /// handoff that could orphan a captive if the second step failed.
        /// </summary>
        public bool TryRegisterFromHolder(Pawn pawn, Faction captorFaction)
        {
            EnsureCustody();
            if (!WraithCullingUtility.IsEligibleBiologicalHuman(pawn) || pawn.Spawned)
                return false;
            if (!WraithLineageUtility.IsWraithLineage(captorFaction) || RecordFor(pawn) != null)
                return false;

            ThingOwner source = pawn.holdingOwner;
            if (source == null || source == custody)
                return false;

            WraithAbducteeRecord record = NewRecord(pawn, captorFaction);
            if (!custody.TryAddOrTransfer(pawn, false))
                return false;

            try
            {
                records.Add(record);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[WNG] Exact Wraith holder transfer rolled back: " + ex.Message);
                source.TryAddOrTransfer(pawn, false);
                return false;
            }
        }

        /// <summary>
        /// Atomically transfers a set of exact culled Pawns from one physical transport holder into
        /// persistent Wraith custody. Either every validated captive and metadata record commits, or
        /// every moved Pawn is returned to the original holder. This is the hostile Dart map-exit seam.
        /// </summary>
        public bool TryRegisterBatchFromHolder(IEnumerable<Pawn> pawns, Faction captorFaction)
        {
            EnsureCustody();
            List<Pawn> batch = pawns?.Where(p => p != null).Distinct().ToList() ?? new List<Pawn>();
            if (batch.Count == 0)
                return true;
            if (!WraithLineageUtility.IsWraithLineage(captorFaction))
                return false;
            if (batch.Any(p => p.Spawned || !WraithCullingUtility.IsEligibleBiologicalHuman(p) || RecordFor(p) != null))
                return false;

            ThingOwner source = batch[0].holdingOwner;
            if (source == null || source == custody || batch.Any(p => p.holdingOwner != source))
                return false;

            List<Pawn> moved = new List<Pawn>();
            List<WraithAbducteeRecord> stagedRecords = batch.Select(p => NewRecord(p, captorFaction)).ToList();
            try
            {
                foreach (Pawn pawn in batch)
                {
                    if (!custody.TryAddOrTransfer(pawn, false))
                    {
                        foreach (Pawn prior in moved) source.TryAddOrTransfer(prior, false);
                        return false;
                    }
                    moved.Add(pawn);
                }
                records.AddRange(stagedRecords);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[WNG] Exact Wraith Dart batch custody transfer rolled back: " + ex.Message);
                foreach (WraithAbducteeRecord record in stagedRecords) records.Remove(record);
                foreach (Pawn pawn in moved)
                    if (pawn != null && pawn.holdingOwner == custody) source.TryAddOrTransfer(pawn, false);
                return false;
            }
        }

        /// <summary>
        /// Transactionally returns one exact recently-culled Pawn from persistent Wraith custody
        /// during a successful same-gate pursuit. Captives already assigned to a rescue site are
        /// deliberately excluded so pursuit never steals ownership from an active site workflow.
        /// </summary>
        public bool TryReleasePursuitCaptive(
            int pawnId,
            Faction expectedCaptor,
            Map map,
            IntVec3 near,
            out Pawn released)
        {
            EnsureCustody();
            released = null;
            if (pawnId <= 0 || expectedCaptor == null || map == null)
                return false;

            WraithAbducteeRecord record = records.FirstOrDefault(r =>
                r != null &&
                r.pawn != null &&
                !r.pawn.Dead &&
                r.pawn.thingIDNumber == pawnId &&
                r.captorFaction == expectedCaptor &&
                r.rescueSiteId < 0 &&
                custody.Contains(r.pawn));
            if (record == null)
                return false;

            Pawn pawn = record.pawn;
            Faction priorFaction = pawn.Faction;
            Faction priorGuestHost = pawn.guest?.HostFaction;
            GuestStatus priorGuestStatus = pawn.guest?.GuestStatus ?? GuestStatus.Guest;
            Pawn dropped;
            if (!custody.TryDrop(pawn, near, map, ThingPlaceMode.Near, out dropped) || dropped != pawn)
                return false;

            try
            {
                RestoreOriginalCustodyState(record);
                records.Remove(record);
                released = pawn;
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[WNG] Same-gate pursuit captive release failed; recapturing exact Pawn: " + ex.Message);
                try
                {
                    if (pawn.Faction != priorFaction)
                        pawn.SetFaction(priorFaction);
                    if (pawn.guest != null)
                        pawn.guest.SetGuestStatus(priorGuestHost, priorGuestStatus);
                }
                catch { }

                if (pawn.Spawned)
                    pawn.DeSpawn(DestroyMode.Vanish);
                if (!custody.TryAdd(pawn, false))
                {
                    Log.Error("[WNG] Same-gate pursuit rollback could not restore exact captive to Wraith custody.");
                    if (!pawn.Spawned && !pawn.Destroyed)
                        GenSpawn.Spawn(pawn, WraithCullingUtility.SafeReturnCell(map, near), map);
                }
                return false;
            }
        }

        public int CaptivityStage(WraithAbducteeRecord record)
        {
            int max = Math.Max(1, Tuning?.maxCaptivityStage ?? 4);
            return record == null ? -1 : Math.Max(0, Math.Min(max, record.captivityStage));
        }

        private void AdvanceCaptiveLifecycle(int now)
        {
            if (records == null || records.Count == 0)
                return;

            int pressureInterval = Math.Max(60000, Tuning?.captivityPressureIntervalTicks ?? 120000);
            int maxStage = Math.Max(1, Tuning?.maxCaptivityStage ?? 4);
            Dictionary<int, List<string>> changedByStage = new Dictionary<int, List<string>>();

            foreach (WraithAbducteeRecord record in records)
            {
                Pawn pawn = record?.pawn;
                if (record == null || pawn == null || pawn.Dead || record.releasedAtSite)
                    continue;

                record.captivityStage = Math.Max(0, Math.Min(maxStage, record.captivityStage));
                ApplyCaptiveState(record);

                if (record.nextCaptivityPressureTick < 0)
                    record.nextCaptivityPressureTick = now + pressureInterval;
                if (now < record.nextCaptivityPressureTick)
                    continue;

                int previous = record.captivityStage;
                ApplyCaptivePressure(record, now);
                if (record.captivityStage <= previous)
                    continue;

                if (!changedByStage.TryGetValue(record.captivityStage, out List<string> names))
                {
                    names = new List<string>();
                    changedByStage[record.captivityStage] = names;
                }
                names.Add(pawn.LabelShort);
            }

            foreach (KeyValuePair<int, List<string>> pair in changedByStage.OrderBy(p => p.Key))
            {
                TryStageLetter(pair.Key, pair.Value);
            }
        }

        private void ApplyCaptivePressure(WraithAbducteeRecord record, int now)
        {
            Pawn pawn = record?.pawn;
            if (pawn == null || pawn.Dead)
                return;

            int maxStage = Math.Max(1, Tuning?.maxCaptivityStage ?? 4);
            record.captivityStage = Math.Max(0, Math.Min(maxStage, record.captivityStage));
            if (record.captivityStage < maxStage)
                record.captivityStage++;

            int pressureInterval = Math.Max(60000, Tuning?.captivityPressureIntervalTicks ?? 120000);
            record.nextCaptivityPressureTick = now + pressureInterval;

            int ageYears = Math.Max(0, Tuning?.captiveAgeYearsPerPressure ?? 2);
            if (ageYears > 0)
                WraithHiveEcologyUtility.AdjustBiologicalAge(pawn, ageYears);
            WraithHiveEcologyUtility.RefreshHediff(pawn, "WNG_LifeDrained");
            ApplyCaptiveState(record);
        }

        private static bool EnsureCustodyHediff(Pawn pawn, string defName)
        {
            if (pawn?.health?.hediffSet == null)
                return false;
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
            if (def == null || pawn.health.hediffSet.HasHediff(def))
                return false;
            pawn.health.AddHediff(def);
            return true;
        }

        private static void RemoveCustodyHediff(Pawn pawn, string defName, ref bool addedByCustody)
        {
            if (!addedByCustody || pawn?.health?.hediffSet == null)
                return;
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
            Hediff hediff = def == null ? null : pawn.health.hediffSet.GetFirstHediffOfDef(def);
            if (hediff != null)
                pawn.health.RemoveHediff(hediff);
            addedByCustody = false;
        }

        private void ApplyCaptiveState(WraithAbducteeRecord record)
        {
            Pawn pawn = record?.pawn;
            if (pawn == null || pawn.Dead)
                return;

            if (EnsureCustodyHediff(pawn, "WNG_WraithFeedingStock"))
                record.custodyAppliedFeedingStock = true;
            if (record.captivityStage >= 2 && EnsureCustodyHediff(pawn, "WNG_WraithExperimentSubject"))
                record.custodyAppliedExperimentSubject = true;
            if (record.captivityStage >= 3 && EnsureCustodyHediff(pawn, "WNG_WraithConditionedCaptive"))
                record.custodyAppliedConditioning = true;
        }

        private void CleanupCaptivityState(WraithAbducteeRecord record)
        {
            Pawn pawn = record?.pawn;
            if (pawn == null)
                return;

            RemoveCustodyHediff(pawn, "WNG_WraithFeedingStock", ref record.custodyAppliedFeedingStock);
            RemoveCustodyHediff(pawn, "WNG_WraithExperimentSubject", ref record.custodyAppliedExperimentSubject);
            RemoveCustodyHediff(pawn, "WNG_WraithConditionedCaptive", ref record.custodyAppliedConditioning);
        }

        private static void TryStageLetter(int stage, List<string> names)
        {
            if (names == null || names.Count == 0)
                return;

            string label;
            string text;
            if (stage >= 4)
            {
                label = "Wraith enthrallment threshold reached";
                text = " have endured enough feeding, invasive study and psychic conditioning to reach the Wraith's full enthrallment threshold. The exact captives remain rescueable; if next materialized under Wraith guard, current native slavery mechanics will commit the enthrallment rather than an obsolete marker Hediff.";
            }
            else if (stage >= 3)
            {
                label = "Wraith conditioning detected";
                text = " are now undergoing deliberate psychic conditioning after repeated captivity. Another prolonged delay risks native enthrallment.";
            }
            else if (stage >= 2)
            {
                label = "Wraith experimentation detected";
                text = " are now being used as biological experiment subjects as well as renewable feeding stock.";
            }
            else
            {
                label = "Wraith feeding cycle detected";
                text = " have endured another controlled feeding cycle. The Wraith are preserving them as renewable feeding stock rather than killing them outright.";
            }

            try
            {
                Find.LetterStack.ReceiveLetter(label, names.ToCommaList(useAnd: true) + text, LetterDefOf.NegativeEvent);
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Staged Wraith captivity committed but notification failed: " + ex.Message);
            }
        }

        public bool TryCommitNativeEnthrallmentAtSite(WraithAbducteeRecord record, Pawn caster)
        {
            Pawn pawn = record?.pawn;
            if (record == null || pawn == null || pawn.Dead || record.releasedAtSite ||
                record.captivityStage < Math.Max(1, Tuning?.maxCaptivityStage ?? 4) ||
                record.nativeEnthrallmentCommitted)
                return false;
            if (caster == null || caster.Dead || !caster.Spawned || caster.Map == null ||
                pawn.Map != caster.Map || caster.Faction == null || caster.Faction != record.captorFaction ||
                pawn.guest == null || pawn.Faction == caster.Faction)
                return false;

            try
            {
                if (!pawn.IsPrisoner)
                    pawn.guest.SetGuestStatus(caster.Faction, GuestStatus.Prisoner);

                GenGuest.TryEnslavePrisoner(caster, pawn);
                if (!pawn.IsSlave || pawn.Faction != caster.Faction)
                {
                    Log.Warning("[WNG] Stage-4 Wraith captivity reached native Enthrallment threshold but RimWorld did not commit slave state.");
                    return false;
                }

                record.nativeEnthrallmentCommitted = true;
                try
                {
                    Messages.Message(
                        pawn.LabelShortCap + " has been fully enthralled by " + caster.LabelShortCap +
                        " using RimWorld's native slave state. The exact Pawn remains rescueable.",
                        pawn,
                        MessageTypeDefOf.NegativeEvent,
                        historical: false);
                }
                catch { }
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[WNG] Native stage-4 Wraith enthrallment failed without replacing the exact captive: " + ex);
                return false;
            }
        }

        public List<WraithAbducteeRecord> RecordsForSite(int siteId)
        {
            if (siteId < 0 || records == null)
                return new List<WraithAbducteeRecord>();
            return records.Where(r => r != null && r.rescueSiteId == siteId && r.pawn != null && !r.pawn.Dead).ToList();
        }

        /// <summary>
        /// Materializes the exact Pawn object from custody onto its rescue map. Native ThingOwner
        /// TryDrop remains authoritative for ownership transfer. If post-drop prisoner setup fails,
        /// the same Pawn is immediately recaptured into custody.
        /// </summary>
        public bool TryMaterializeForSite(WraithAbducteeRecord record, Map map, IntVec3 near)
        {
            EnsureCustody();
            if (record?.pawn == null || record.pawn.Dead || map == null || !custody.Contains(record.pawn))
                return false;

            Pawn pawn = record.pawn;
            Pawn dropped;
            if (!custody.TryDrop(pawn, near, map, ThingPlaceMode.Near, out dropped) || dropped != pawn)
                return false;

            try
            {
                if (pawn.guest != null && record.captorFaction != null)
                    pawn.guest.SetGuestStatus(record.captorFaction, GuestStatus.Prisoner);
                ApplyCaptiveState(record);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[WNG] Exact Wraith rematerialization setup failed; recapturing same Pawn: " + ex.Message);
                if (pawn.Spawned)
                    pawn.DeSpawn(DestroyMode.Vanish);
                custody.TryAdd(pawn, false);
                return false;
            }
        }

        private void RestoreOriginalCustodyState(WraithAbducteeRecord record)
        {
            Pawn pawn = record?.pawn;
            if (pawn == null)
                return;

            bool wasNativeEnthralled =
                record.nativeEnthrallmentCommitted;
            Faction conditioningFaction =
                record.captorFaction;
            bool restoreAsPlayerSleeper =
                wasNativeEnthralled &&
                record.originalFaction == Faction.OfPlayer &&
                pawn.RaceProps?.Humanlike == true;

            // Stage-4 native enthrallment may have changed faction ownership. Only undo the
            // Wraith-owned transition this registry itself committed; unrelated later changes win.
            if (wasNativeEnthralled &&
                pawn.Faction == record.captorFaction &&
                pawn.Faction != record.originalFaction)
            {
                pawn.SetFaction(record.originalFaction);
            }

            if (pawn.guest != null)
            {
                Faction host = record.originalGuestHost;
                GuestStatus status = record.originalGuestStatus;
                if (status == GuestStatus.Prisoner && host == null && record.originalFaction != Faction.OfPlayer)
                    status = GuestStatus.Guest;
                pawn.guest.SetGuestStatus(host, status);
            }

            CleanupCaptivityState(record);
            record.nativeEnthrallmentCommitted = false;

            // Native Enthrallment itself is now over. The separate residual sleeper state is a
            // post-rescue consequence only and never controls faction, slavery or guest ownership.
            if (restoreAsPlayerSleeper)
            {
                try
                {
                    WraithSleeperAgentUtility.TryApplyResidualConditioning(
                        pawn,
                        conditioningFaction);
                }
                catch (Exception ex)
                {
                    Log.Warning(
                        "[WNG] Exact Wraith captive recovery committed but residual sleeper conditioning could not be applied: " +
                        ex.Message);
                }
            }
        }

        public List<Pawn> ReleaseSiteCaptives(int siteId)
        {
            List<Pawn> released = new List<Pawn>();
            foreach (WraithAbducteeRecord record in RecordsForSite(siteId))
            {
                Pawn pawn = record.pawn;
                if (record.releasedAtSite || pawn == null || pawn.Dead || !pawn.Spawned)
                    continue;

                RestoreOriginalCustodyState(record);
                record.releasedAtSite = true;
                released.Add(pawn);
            }
            return released;
        }

        /// <summary>
        /// Called before a rescue map disappears. Any exact captive not already carried into a
        /// player caravan/home is transactionally returned to persistent custody. No replacement
        /// Pawn is generated if the map is lost or the rescue expires.
        /// </summary>
        public void ReclaimUnrecoveredSiteCaptives(int siteId, Map map)
        {
            EnsureCustody();
            int now = Find.TickManager?.TicksGame ?? 0;
            int retry = Math.Max(1, Tuning?.rescueRetryDelayTicks ?? 120000);

            foreach (WraithAbducteeRecord record in RecordsForSite(siteId))
            {
                Pawn pawn = record.pawn;
                if (pawn == null || pawn.Dead || IsConclusiveRecovery(record))
                    continue;

                if (pawn.Spawned && pawn.Map == map)
                {
                    IntVec3 oldCell = pawn.Position;
                    try
                    {
                        pawn.jobs?.StopAll();
                        pawn.DeSpawn(DestroyMode.Vanish);
                        if (!custody.TryAdd(pawn, false))
                        {
                            GenSpawn.Spawn(pawn, WraithCullingUtility.SafeReturnCell(map, oldCell), map);
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[WNG] Rescue-site captive reclaim failed: " + ex.Message);
                        if (!pawn.Spawned && !pawn.Destroyed && !custody.Contains(pawn))
                            GenSpawn.Spawn(pawn, WraithCullingUtility.SafeReturnCell(map, oldCell), map);
                        continue;
                    }
                }

                if (custody.Contains(pawn))
                {
                    int priorStage = record.captivityStage;
                    ApplyCaptivePressure(record, now);
                    if (record.captivityStage > priorStage)
                        TryStageLetter(record.captivityStage, new List<string> { pawn.LabelShort });

                    record.rescueSiteId = -1;
                    record.rescueSiteExpiryTick = -1;
                    record.nextRescueOfferTick = now + retry;
                    record.rescueFailures++;
                    record.releasedAtSite = false;
                }
            }
        }

        public void NotifyRescueSiteDestroyed(int siteId)
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            int retry = Math.Max(1, Tuning?.rescueRetryDelayTicks ?? 120000);
            foreach (WraithAbducteeRecord record in RecordsForSite(siteId))
            {
                if (record.pawn == null || record.pawn.Dead || IsConclusiveRecovery(record))
                    continue;
                if (!custody.Contains(record.pawn))
                    continue;

                int priorStage = record.captivityStage;
                ApplyCaptivePressure(record, now);
                if (record.captivityStage > priorStage)
                    TryStageLetter(record.captivityStage, new List<string> { record.pawn.LabelShort });

                record.rescueSiteId = -1;
                record.rescueSiteExpiryTick = -1;
                record.nextRescueOfferTick = now + retry;
                record.rescueFailures++;
                record.releasedAtSite = false;
            }
        }

        private bool IsConclusiveRecovery(WraithAbducteeRecord record)
        {
            Pawn pawn = record?.pawn;
            if (pawn == null || pawn.Dead)
                return false;

            Caravan caravan = pawn.GetCaravan();
            if (caravan != null && caravan.Faction == Faction.OfPlayer)
                return true;

            if (!pawn.Spawned || pawn.Map == null)
                return false;
            if (pawn.Map.Parent != null && pawn.Map.Parent.ID == record.rescueSiteId)
                return false;

            return pawn.Map.IsPlayerHome || pawn.Map.ParentFaction == Faction.OfPlayer;
        }

        private void ReconcileRecoveredOrDead()
        {
            for (int i = records.Count - 1; i >= 0; i--)
            {
                WraithAbducteeRecord record = records[i];
                Pawn pawn = record?.pawn;
                if (record == null || pawn == null || pawn.Dead)
                {
                    if (pawn != null && custody.Contains(pawn))
                        custody.Remove(pawn);
                    records.RemoveAt(i);
                    continue;
                }

                if (!IsConclusiveRecovery(record))
                    continue;

                RestoreOriginalCustodyState(record);
                records.RemoveAt(i);
                Find.LetterStack.ReceiveLetter(
                    "Wraith abductee recovered",
                    pawn.LabelShortCap + " is safely back under player control. This is the same Pawn that was culled; temporary feeding-stock, experiment and conditioning markers were cleared, while biological aging and feeding trauma inflicted during captivity remain real consequences.",
                    LetterDefOf.PositiveEvent,
                    pawn);
            }
        }

        private void ExpireRescueSites(int now)
        {
            HashSet<int> processed = new HashSet<int>();
            foreach (WraithAbducteeRecord record in records.ToList())
            {
                if (record == null || record.rescueSiteId < 0 || record.rescueSiteExpiryTick < 0 || now < record.rescueSiteExpiryTick)
                    continue;
                if (!processed.Add(record.rescueSiteId))
                    continue;

                Site site = Find.WorldObjects.AllWorldObjects.OfType<Site>().FirstOrDefault(s => s.ID == record.rescueSiteId);
                if (site != null && site.HasMap)
                {
                    foreach (WraithAbducteeRecord sameSite in RecordsForSite(record.rescueSiteId))
                        sameSite.rescueSiteExpiryTick = now + 60000;
                    continue;
                }

                int siteId = record.rescueSiteId;
                if (site != null)
                    site.Destroy();
                NotifyRescueSiteDestroyed(siteId);
            }
        }

        private bool CaptorAlreadyHasSite(Faction captor)
        {
            return records.Any(r => r != null && r.captorFaction == captor && r.rescueSiteId >= 0);
        }

        private void TryOfferRescueSites(int now)
        {
            WraithCullingTuningDef tuning = Tuning;
            if (tuning == null)
                return;

            SitePartDef holdingDef = DefDatabase<SitePartDef>.GetNamedSilentFail("WNG_WraithHoldingSite");
            if (holdingDef == null)
                return;

            IEnumerable<Faction> captors = records
                .Where(r => r != null && r.pawn != null && !r.pawn.Dead && r.rescueSiteId < 0 && r.nextRescueOfferTick >= 0 && now >= r.nextRescueOfferTick)
                .Select(r => r.captorFaction)
                .Where(f => f != null)
                .Distinct()
                .ToList();

            foreach (Faction captor in captors)
            {
                if (captor.defeated || !WraithLineageUtility.IsWraithLineage(captor) || !captor.HostileTo(Faction.OfPlayer) || CaptorAlreadyHasSite(captor))
                    continue;

                List<WraithAbducteeRecord> eligible = records
                    .Where(r => r != null && r.captorFaction == captor && r.pawn != null && !r.pawn.Dead && r.rescueSiteId < 0 && r.nextRescueOfferTick >= 0 && now >= r.nextRescueOfferTick)
                    .OrderBy(r => r.capturedTick)
                    .Take(Math.Max(1, tuning.maxCaptivesPerRescueSite))
                    .ToList();
                if (eligible.Count == 0 || eligible.Any(r => !custody.Contains(r.pawn)))
                    continue;

                PlanetTile tile;
                if (!TileFinder.TryFindNewSiteTile(out tile, Math.Max(1, tuning.rescueSiteMinDistance), Math.Max(tuning.rescueSiteMinDistance + 1, tuning.rescueSiteMaxDistance), false))
                    continue;

                int maxFailures = eligible.Max(r => r.rescueFailures);
                int maxStage = eligible.Max(r => Math.Max(0, Math.Min(Math.Max(1, tuning.maxCaptivityStage), r.captivityStage)));
                float threatPoints = Math.Max(
                    tuning.baseRescueThreatPoints +
                    eligible.Count * tuning.threatPerCaptive +
                    maxFailures * tuning.threatPerFailure +
                    maxStage * tuning.threatPerCaptivityStage,
                    StorytellerUtility.DefaultSiteThreatPointsNow() * Math.Max(0f, tuning.storytellerThreatFactor));

                Site site = SiteMaker.MakeSite(
                    holdingDef,
                    tile,
                    captor,
                    ifHostileThenMustRemainHostile: false,
                    threatPoints: threatPoints);
                site.customLabel = CaptivitySiteLabel(captor, maxStage);
                Find.WorldObjects.Add(site);

                int expiry = now + Math.Max(60000, tuning.rescueSiteDurationTicks);
                foreach (WraithAbducteeRecord record in eligible)
                {
                    record.rescueSiteId = site.ID;
                    record.rescueSiteExpiryTick = expiry;
                    record.nextRescueOfferTick = -1;
                    record.releasedAtSite = false;
                }

                string names = eligible.Select(r => r.pawn.LabelShort).ToCommaList(useAnd: true);
                Find.LetterStack.ReceiveLetter(
                    "Wraith captives located",
                    "Culling traffic has exposed " + CaptivitySiteArticle(captor, maxStage) + " containing " + names +
                    ". " + CaptivitySiteThreatText(maxStage) +
                    " Assault the site before it relocates. If it escapes, the same exact captives remain recoverable but endure another feeding/conditioning cycle.",
                    LetterDefOf.ThreatBig,
                    site);
            }
        }

        private static string CaptivitySiteLabel(Faction captor, int stage)
        {
            string prefix = captor?.Name.NullOrEmpty() == false ? captor.Name + " " : "";
            if (stage >= 4) return prefix + "thrall-conditioning site";
            if (stage >= 3) return prefix + "psychic conditioning site";
            if (stage >= 2) return prefix + "laboratory holding site";
            return prefix + "feeding preserve";
        }

        private static string CaptivitySiteArticle(Faction captor, int stage)
        {
            string owner = captor?.Name.NullOrEmpty() == false ? captor.Name + " " : "Wraith ";
            if (stage >= 4) return "a " + owner + "thrall-conditioning site";
            if (stage >= 3) return "a " + owner + "psychic conditioning site";
            if (stage >= 2) return "a " + owner + "laboratory holding site";
            return "a " + owner + "feeding preserve";
        }

        private static string CaptivitySiteThreatText(int stage)
        {
            if (stage >= 4)
                return "The captives have reached the native Enthrallment threshold; once materialized under a real Wraith Keeper, current RimWorld slave state can be committed while preserving exact Pawn identity.";
            if (stage >= 3)
                return "The captives are undergoing deliberate psychic conditioning after repeated feeding and invasive study; further delay risks native enthrallment.";
            if (stage >= 2)
                return "The captives are being used as both renewable feeding stock and biological experiment subjects.";
            return "The captives are being deliberately kept alive as renewable feeding stock.";
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            EnsureCustody();
            WraithCullingTuningDef tuning = Tuning;
            int interval = Math.Max(60, tuning?.custodyCheckIntervalTicks ?? 600);
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now % interval != 0)
                return;

            ReconcileRecoveredOrDead();
            AdvanceCaptiveLifecycle(now);
            ExpireRescueSites(now);
            TryOfferRescueSites(now);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            EnsureCustody();
            custody.ExposeData();
            Scribe_Collections.Look(ref records, "wngWraithCullingRecords", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (records == null)
                    records = new List<WraithAbducteeRecord>();
                records.RemoveAll(r => r == null || r.pawn == null);
                int now = Find.TickManager?.TicksGame ?? 0;
                int pressure = Math.Max(60000, Tuning?.captivityPressureIntervalTicks ?? 120000);
                int maxStage = Math.Max(1, Tuning?.maxCaptivityStage ?? 4);
                foreach (WraithAbducteeRecord record in records)
                {
                    record.captivityStage = Math.Max(0, Math.Min(maxStage, record.captivityStage));
                    if (record.nextCaptivityPressureTick < 0)
                        record.nextCaptivityPressureTick = now + pressure;
                    ApplyCaptiveState(record);
                }
            }
        }
    }
}
