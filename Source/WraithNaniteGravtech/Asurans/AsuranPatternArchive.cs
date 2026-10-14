using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// One saved Asuran pattern. This is deliberately a reconstruction record rather than an
    /// exact Pawn holder: a rebuilt body is a new Pawn based on the last stored state. Social
    /// relations, memories and experience after backup are not fabricated back into existence.
    /// </summary>
    public sealed class AsuranPatternSnapshot : IExposable
    {
        public int pawnThingId = -1;
        public int gender;
        public int backupTick;
        public int reconstructionTick = -1;
        public bool pendingReconstruction;
        public int ideologicalStance;
        public int specialistRole;

        public int nameKind;
        public string firstName;
        public string nickName;
        public string lastName;
        public string singleName;
        public string kindDefName;
        public string xenotypeDefName;
        public string xenotypeName;
        public string childhoodDefName;
        public string adulthoodDefName;
        public string storyTitle;
        public string birthLastName;
        public string hairDefName;
        public string bodyTypeDefName;
        public string headTypeDefName;
        public string beardDefName;
        public string faceTattooDefName;
        public string bodyTattooDefName;
        public long biologicalAgeTicks;
        public long chronologicalAgeTicks;

        public float hairR, hairG, hairB, hairA = 1f;
        public bool hasSkinOverride;
        public float skinR, skinG, skinB, skinA = 1f;

        public List<string> skillDefNames = new List<string>();
        public List<int> skillLevels = new List<int>();
        public List<int> skillPassions = new List<int>();
        public List<float> skillXpSinceLast = new List<float>();
        public List<float> skillXpSinceMidnight = new List<float>();
        public List<string> traitDefNames = new List<string>();
        public List<int> traitDegrees = new List<int>();
        public List<bool> traitForced = new List<bool>();
        public List<string> endogeneDefNames = new List<string>();
        public List<string> xenogeneDefNames = new List<string>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref pawnThingId, "pawnThingId", -1);
            Scribe_Values.Look(ref gender, "gender", 0);
            Scribe_Values.Look(ref backupTick, "backupTick", 0);
            Scribe_Values.Look(ref reconstructionTick, "reconstructionTick", -1);
            Scribe_Values.Look(ref pendingReconstruction, "pendingReconstruction", false);
            Scribe_Values.Look(ref ideologicalStance, "ideologicalStance", 0);
            Scribe_Values.Look(ref specialistRole, "specialistRole", 0);
            Scribe_Values.Look(ref nameKind, "nameKind", 0);
            Scribe_Values.Look(ref firstName, "firstName");
            Scribe_Values.Look(ref nickName, "nickName");
            Scribe_Values.Look(ref lastName, "lastName");
            Scribe_Values.Look(ref singleName, "singleName");
            Scribe_Values.Look(ref kindDefName, "kindDefName");
            Scribe_Values.Look(ref xenotypeDefName, "xenotypeDefName");
            Scribe_Values.Look(ref xenotypeName, "xenotypeName");
            Scribe_Values.Look(ref childhoodDefName, "childhoodDefName");
            Scribe_Values.Look(ref adulthoodDefName, "adulthoodDefName");
            Scribe_Values.Look(ref storyTitle, "storyTitle");
            Scribe_Values.Look(ref birthLastName, "birthLastName");
            Scribe_Values.Look(ref hairDefName, "hairDefName");
            Scribe_Values.Look(ref bodyTypeDefName, "bodyTypeDefName");
            Scribe_Values.Look(ref headTypeDefName, "headTypeDefName");
            Scribe_Values.Look(ref beardDefName, "beardDefName");
            Scribe_Values.Look(ref faceTattooDefName, "faceTattooDefName");
            Scribe_Values.Look(ref bodyTattooDefName, "bodyTattooDefName");
            Scribe_Values.Look(ref biologicalAgeTicks, "biologicalAgeTicks", 0L);
            Scribe_Values.Look(ref chronologicalAgeTicks, "chronologicalAgeTicks", 0L);
            Scribe_Values.Look(ref hairR, "hairR", 0f); Scribe_Values.Look(ref hairG, "hairG", 0f); Scribe_Values.Look(ref hairB, "hairB", 0f); Scribe_Values.Look(ref hairA, "hairA", 1f);
            Scribe_Values.Look(ref hasSkinOverride, "hasSkinOverride", false);
            Scribe_Values.Look(ref skinR, "skinR", 0f); Scribe_Values.Look(ref skinG, "skinG", 0f); Scribe_Values.Look(ref skinB, "skinB", 0f); Scribe_Values.Look(ref skinA, "skinA", 1f);
            Scribe_Collections.Look(ref skillDefNames, "skillDefNames", LookMode.Value);
            Scribe_Collections.Look(ref skillLevels, "skillLevels", LookMode.Value);
            Scribe_Collections.Look(ref skillPassions, "skillPassions", LookMode.Value);
            Scribe_Collections.Look(ref skillXpSinceLast, "skillXpSinceLast", LookMode.Value);
            Scribe_Collections.Look(ref skillXpSinceMidnight, "skillXpSinceMidnight", LookMode.Value);
            Scribe_Collections.Look(ref traitDefNames, "traitDefNames", LookMode.Value);
            Scribe_Collections.Look(ref traitDegrees, "traitDegrees", LookMode.Value);
            Scribe_Collections.Look(ref traitForced, "traitForced", LookMode.Value);
            Scribe_Collections.Look(ref endogeneDefNames, "endogeneDefNames", LookMode.Value);
            Scribe_Collections.Look(ref xenogeneDefNames, "xenogeneDefNames", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                skillDefNames ??= new List<string>(); skillLevels ??= new List<int>(); skillPassions ??= new List<int>();
                skillXpSinceLast ??= new List<float>(); skillXpSinceMidnight ??= new List<float>();
                traitDefNames ??= new List<string>(); traitDegrees ??= new List<int>(); traitForced ??= new List<bool>();
                endogeneDefNames ??= new List<string>(); xenogeneDefNames ??= new List<string>();
            }
        }
    }

    public sealed class CompProperties_AsuranPatternArchive : CompProperties
    {
        public int backupIntervalTicks = 120000;
        public int reconstructionDelayTicks = 90000;
        public int retryIntervalTicks = 30000;
        public int maxRecords = 16;
        public int plasteelCost = 80;
        public int advancedComponentCost = 3;
        public CompProperties_AsuranPatternArchive() { compClass = typeof(CompAsuranPatternArchive); }
    }

    public sealed class CompAsuranPatternArchive : ThingComp
    {
        private List<AsuranPatternSnapshot> snapshots = new List<AsuranPatternSnapshot>();
        private int nextBackupTick = -1;
        private int lastSuccessfulBackupTick = -1;
        public CompProperties_AsuranPatternArchive Props => (CompProperties_AsuranPatternArchive)props;
        public bool Powered
        {
            get
            {
                CompPowerTrader power = parent?.TryGetComp<CompPowerTrader>();
                return power == null || power.PowerOn;
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (!respawningAfterLoad && nextBackupTick < 0)
                nextBackupTick = SafeFutureTick(Find.TickManager.TicksGame, Math.Max(1, Props.backupIntervalTicks));
        }

        public override void CompTick()
        {
            base.CompTick();
            if (parent == null || !parent.Spawned || parent.Map == null || parent.Faction == null || !parent.IsHashIntervalTick(250) || !Powered)
                return;

            int now = Find.TickManager.TicksGame;
            if (nextBackupTick < 0)
                nextBackupTick = SafeFutureTick(now, Math.Max(1, Props.backupIntervalTicks));
            if (now >= nextBackupTick)
            {
                BackupLinkedPawns(now);
                nextBackupTick = SafeFutureTick(now, Math.Max(1, Props.backupIntervalTicks));
            }

            if (IsPrimaryPoweredArchive())
            {
                DetectLocalDeaths(now);
                TryReconstructDue(now);
            }
        }

        public void RecordPawnNow(Pawn pawn)
        {
            if (CanArchivePawn(pawn))
            {
                RecordPawn(pawn, Find.TickManager.TicksGame);
                Trim();
            }
        }

        private bool CanArchivePawn(Pawn pawn)
        {
            return pawn != null && !pawn.Dead && pawn.Spawned && pawn.Map == parent.Map
                && pawn.Faction == parent.Faction && AsuranCollectiveUtility.IsLinked(pawn)
                && !AsuranCollectiveUtility.IsDisrupted(pawn)
                && !ReplicatorQueenUtility.IsExactQueen(pawn);
        }

        private static bool IsExactQueenSnapshot(AsuranPatternSnapshot snapshot)
        {
            Pawn exactQueen = ReplicatorQueenUtility.State?.ExactQueen;
            return snapshot != null && exactQueen != null && snapshot.pawnThingId == exactQueen.thingIDNumber;
        }

        private void BackupLinkedPawns(int now)
        {
            foreach (Pawn pawn in parent.Map.mapPawns.AllPawnsSpawned)
                if (CanArchivePawn(pawn))
                    RecordPawn(pawn, now);
            lastSuccessfulBackupTick = now;
            Trim();
        }

        private void RecordPawn(Pawn pawn, int now)
        {
            AsuranPatternSnapshot snapshot = snapshots.FirstOrDefault(x => x != null && x.pawnThingId == pawn.thingIDNumber);
            if (snapshot == null)
            {
                snapshot = new AsuranPatternSnapshot { pawnThingId = pawn.thingIDNumber };
                snapshots.Add(snapshot);
            }

            CaptureName(pawn, snapshot);
            snapshot.kindDefName = pawn.kindDef?.defName;
            snapshot.xenotypeDefName = pawn.genes?.Xenotype?.defName;
            snapshot.xenotypeName = pawn.genes?.xenotypeName;
            snapshot.gender = (int)pawn.gender;
            snapshot.biologicalAgeTicks = pawn.ageTracker?.AgeBiologicalTicks ?? 0L;
            snapshot.chronologicalAgeTicks = pawn.ageTracker?.AgeChronologicalTicks ?? snapshot.biologicalAgeTicks;
            snapshot.backupTick = now;
            snapshot.pendingReconstruction = false;
            snapshot.reconstructionTick = -1;
            AsuranIdeologyUtility.EnsureStance(pawn);
            snapshot.ideologicalStance = (int)AsuranIdeologyUtility.StanceOf(pawn);
            snapshot.specialistRole = (int)HumanFormSpecialistRoleUtility.EnsureRole(pawn);

            snapshot.childhoodDefName = pawn.story?.Childhood?.defName;
            snapshot.adulthoodDefName = pawn.story?.Adulthood?.defName;
            snapshot.storyTitle = pawn.story?.Title;
            snapshot.birthLastName = pawn.story?.birthLastName;
            snapshot.hairDefName = pawn.story?.hairDef?.defName;
            snapshot.bodyTypeDefName = pawn.story?.bodyType?.defName;
            snapshot.headTypeDefName = pawn.story?.headType?.defName;
            snapshot.beardDefName = pawn.style?.beardDef?.defName;
            snapshot.faceTattooDefName = pawn.style?.FaceTattoo?.defName;
            snapshot.bodyTattooDefName = pawn.style?.BodyTattoo?.defName;
            if (pawn.story != null)
            {
                Color hair = pawn.story.HairColor;
                snapshot.hairR = hair.r; snapshot.hairG = hair.g; snapshot.hairB = hair.b; snapshot.hairA = hair.a;
                Color? skin = pawn.story.skinColorOverride;
                snapshot.hasSkinOverride = skin.HasValue;
                if (skin.HasValue)
                {
                    snapshot.skinR = skin.Value.r; snapshot.skinG = skin.Value.g; snapshot.skinB = skin.Value.b; snapshot.skinA = skin.Value.a;
                }
            }

            snapshot.skillDefNames.Clear(); snapshot.skillLevels.Clear(); snapshot.skillPassions.Clear(); snapshot.skillXpSinceLast.Clear(); snapshot.skillXpSinceMidnight.Clear();
            if (pawn.skills != null)
                foreach (SkillRecord skill in pawn.skills.skills)
                {
                    if (skill?.def == null) continue;
                    snapshot.skillDefNames.Add(skill.def.defName);
                    snapshot.skillLevels.Add(skill.Level);
                    snapshot.skillPassions.Add((int)skill.passion);
                    snapshot.skillXpSinceLast.Add(Math.Max(0f, skill.xpSinceLastLevel));
                    snapshot.skillXpSinceMidnight.Add(Math.Max(0f, skill.xpSinceMidnight));
                }

            snapshot.traitDefNames.Clear(); snapshot.traitDegrees.Clear(); snapshot.traitForced.Clear();
            if (pawn.story?.traits?.allTraits != null)
                foreach (Trait trait in pawn.story.traits.allTraits)
                {
                    if (trait?.def == null) continue;
                    snapshot.traitDefNames.Add(trait.def.defName);
                    snapshot.traitDegrees.Add(trait.Degree);
                    snapshot.traitForced.Add(trait.ScenForced);
                }

            snapshot.endogeneDefNames.Clear(); snapshot.xenogeneDefNames.Clear();
            if (pawn.genes != null)
            {
                foreach (Gene gene in pawn.genes.Endogenes)
                    if (gene?.def != null) snapshot.endogeneDefNames.Add(gene.def.defName);
                foreach (Gene gene in pawn.genes.Xenogenes)
                    if (gene?.def != null) snapshot.xenogeneDefNames.Add(gene.def.defName);
            }
        }

        private static void CaptureName(Pawn pawn, AsuranPatternSnapshot snapshot)
        {
            if (pawn?.Name is NameTriple triple)
            {
                snapshot.nameKind = 1;
                snapshot.firstName = triple.First;
                snapshot.nickName = triple.Nick;
                snapshot.lastName = triple.Last;
                snapshot.singleName = null;
            }
            else
            {
                snapshot.nameKind = 0;
                snapshot.singleName = pawn?.Name?.ToStringFull ?? pawn?.LabelShort ?? "Asuran";
                snapshot.firstName = snapshot.nickName = snapshot.lastName = null;
            }
        }

        private void DetectLocalDeaths(int now)
        {
            foreach (Thing thing in parent.Map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse))
            {
                Pawn dead = (thing as Corpse)?.InnerPawn;
                if (dead == null || !dead.Dead || dead.Faction != parent.Faction)
                    continue;
                if (ReplicatorQueenUtility.IsExactQueen(dead))
                    continue;
                AsuranPatternSnapshot snapshot = snapshots.FirstOrDefault(x => x != null && x.pawnThingId == dead.thingIDNumber);
                if (snapshot == null || snapshot.pendingReconstruction || IsExactQueenSnapshot(snapshot))
                    continue;
                snapshot.pendingReconstruction = true;
                snapshot.reconstructionTick = SafeFutureTick(now, Math.Max(1, Props.reconstructionDelayTicks));
            }
        }

        private void TryReconstructDue(int now)
        {
            // Exact Queen continuity belongs to the story-critical direct Pawn reference, never
            // to ordinary archive reconstruction. Drop any stale pre-D055 Queen snapshot instead of
            // allowing an old save to manufacture a replacement sovereign Pawn.
            snapshots.RemoveAll(IsExactQueenSnapshot);

            foreach (AsuranPatternSnapshot snapshot in snapshots
                .Where(x => x != null && x.pendingReconstruction && x.reconstructionTick >= 0 && now >= x.reconstructionTick)
                .OrderBy(x => x.reconstructionTick).ToList())
            {
                if (!CanPay())
                {
                    snapshot.reconstructionTick = SafeFutureTick(now, Math.Max(1, Props.retryIntervalTicks));
                    continue;
                }

                Pawn copy = BuildFromSnapshot(snapshot);
                if (copy == null)
                {
                    snapshot.reconstructionTick = SafeFutureTick(now, Math.Max(1, Props.retryIntervalTicks));
                    continue;
                }

                bool spawned = false;
                try
                {
                    ApplySnapshot(snapshot, copy);
                    StripGeneratedGear(copy);
                    AsuranDefaultApparelUtility.EnsureRoleApparel(copy);
                    if (!GenPlace.TryPlaceThing(copy, parent.Position, parent.Map, ThingPlaceMode.Near))
                    {
                        DestroyUncommitted(copy);
                        snapshot.reconstructionTick = SafeFutureTick(now, Math.Max(1, Props.retryIntervalTicks));
                        continue;
                    }
                    spawned = true;

                    // No asynchronous resource race exists within this tick. Recheck after physical
                    // placement, and roll the uncommitted body back if the bill is no longer payable.
                    if (!CanPay() || !ConsumeResources())
                    {
                        DestroyUncommitted(copy);
                        snapshot.reconstructionTick = SafeFutureTick(now, Math.Max(1, Props.retryIntervalTicks));
                        continue;
                    }

                    // Resource debit + real physical body is the at-most-once functional commit.
                    snapshot.pawnThingId = copy.thingIDNumber;
                    snapshot.pendingReconstruction = false;
                    snapshot.reconstructionTick = -1;
                    snapshot.backupTick = now;
                    copy.Drawer?.renderer?.SetAllGraphicsDirty();
                    try
                    {
                        if (parent.Faction == Faction.OfPlayer)
                            Messages.Message(copy.LabelShortCap + " was reconstructed from the latest stored Asuran pattern. Changes after that backup and social relationships were not restored.", copy, MessageTypeDefOf.PositiveEvent, false);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("[WNG] Pattern reconstruction committed but presentation failed: " + ex.Message);
                    }
                }
                catch (Exception ex)
                {
                    if (copy != null && !copy.Destroyed && (!spawned || snapshot.pendingReconstruction))
                        DestroyUncommitted(copy);
                    Log.Warning("[WNG] Pattern reconstruction failed before commit: " + ex.Message);
                    if (snapshot.pendingReconstruction)
                        snapshot.reconstructionTick = SafeFutureTick(now, Math.Max(1, Props.retryIntervalTicks));
                }
            }
        }

        private Pawn BuildFromSnapshot(AsuranPatternSnapshot snapshot)
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(snapshot.kindDefName)
                ?? DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_HumanFormReplicator")
                ?? DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_HumanFormCopy");
            if (kind == null || parent.Faction == null)
                return null;
            try { return PawnGenerator.GeneratePawn(kind, parent.Faction); }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Pattern reconstruction body generation failed: " + ex.Message);
                return null;
            }
        }

        private static void ApplySnapshot(AsuranPatternSnapshot snapshot, Pawn copy)
        {
            if (snapshot == null || copy == null) return;
            copy.Name = snapshot.nameKind == 1
                ? (Name)new NameTriple(snapshot.firstName ?? string.Empty, snapshot.nickName ?? string.Empty, snapshot.lastName ?? string.Empty)
                : new NameSingle(snapshot.singleName.NullOrEmpty() ? "Asuran" : snapshot.singleName);
            copy.gender = (Gender)snapshot.gender;
            if (copy.ageTracker != null)
            {
                copy.ageTracker.AgeBiologicalTicks = Math.Max(0L, snapshot.biologicalAgeTicks);
                copy.ageTracker.AgeChronologicalTicks = Math.Max(copy.ageTracker.AgeBiologicalTicks, snapshot.chronologicalAgeTicks);
            }

            if (copy.story != null)
            {
                BackstoryDef childhood = DefDatabase<BackstoryDef>.GetNamedSilentFail(snapshot.childhoodDefName);
                BackstoryDef adulthood = DefDatabase<BackstoryDef>.GetNamedSilentFail(snapshot.adulthoodDefName);
                HairDef hair = DefDatabase<HairDef>.GetNamedSilentFail(snapshot.hairDefName);
                BodyTypeDef body = DefDatabase<BodyTypeDef>.GetNamedSilentFail(snapshot.bodyTypeDefName);
                HeadTypeDef head = DefDatabase<HeadTypeDef>.GetNamedSilentFail(snapshot.headTypeDefName);
                if (childhood != null) copy.story.Childhood = childhood;
                if (adulthood != null) copy.story.Adulthood = adulthood;
                copy.story.Title = snapshot.storyTitle;
                copy.story.birthLastName = snapshot.birthLastName;
                if (hair != null) copy.story.hairDef = hair;
                if (body != null) copy.story.bodyType = body;
                if (head != null) copy.story.headType = head;
                copy.story.HairColor = new Color(snapshot.hairR, snapshot.hairG, snapshot.hairB, snapshot.hairA);
                copy.story.skinColorOverride = snapshot.hasSkinOverride ? new Color(snapshot.skinR, snapshot.skinG, snapshot.skinB, snapshot.skinA) : (Color?)null;
                if (copy.story.traits?.allTraits != null)
                {
                    copy.story.traits.allTraits.Clear();
                    int traitCount = Math.Min(snapshot.traitDefNames.Count, Math.Min(snapshot.traitDegrees.Count, snapshot.traitForced.Count));
                    for (int i = 0; i < traitCount; i++)
                    {
                        TraitDef traitDef = DefDatabase<TraitDef>.GetNamedSilentFail(snapshot.traitDefNames[i]);
                        if (traitDef != null)
                            copy.story.traits.GainTrait(new Trait(traitDef, snapshot.traitDegrees[i], snapshot.traitForced[i]));
                    }
                }
            }

            if (copy.style != null)
            {
                copy.style.beardDef = DefDatabase<BeardDef>.GetNamedSilentFail(snapshot.beardDefName) ?? copy.style.beardDef;
                copy.style.FaceTattoo = DefDatabase<TattooDef>.GetNamedSilentFail(snapshot.faceTattooDefName) ?? copy.style.FaceTattoo;
                copy.style.BodyTattoo = DefDatabase<TattooDef>.GetNamedSilentFail(snapshot.bodyTattooDefName) ?? copy.style.BodyTattoo;
            }

            if (copy.skills != null)
            {
                int count = Math.Min(snapshot.skillDefNames.Count, Math.Min(snapshot.skillLevels.Count,
                    Math.Min(snapshot.skillPassions.Count, Math.Min(snapshot.skillXpSinceLast.Count, snapshot.skillXpSinceMidnight.Count))));
                for (int i = 0; i < count; i++)
                {
                    SkillDef def = DefDatabase<SkillDef>.GetNamedSilentFail(snapshot.skillDefNames[i]);
                    SkillRecord skill = def == null ? null : copy.skills.GetSkill(def);
                    if (skill == null) continue;
                    skill.Level = Math.Max(0, Math.Min(20, snapshot.skillLevels[i]));
                    skill.passion = (Passion)snapshot.skillPassions[i];
                    skill.xpSinceLastLevel = Math.Max(0f, snapshot.skillXpSinceLast[i]);
                    skill.xpSinceMidnight = Math.Max(0f, snapshot.skillXpSinceMidnight[i]);
                }
            }

            if (ModsConfig.BiotechActive && copy.genes != null)
            {
                foreach (Gene existing in copy.genes.GenesListForReading.ToList())
                    copy.genes.RemoveGene(existing);
                XenotypeDef xenotype = DefDatabase<XenotypeDef>.GetNamedSilentFail(snapshot.xenotypeDefName);
                if (xenotype != null) copy.genes.SetXenotype(xenotype);
                foreach (string defName in snapshot.endogeneDefNames)
                {
                    GeneDef def = DefDatabase<GeneDef>.GetNamedSilentFail(defName);
                    if (def != null && !HasGene(copy, def)) copy.genes.AddGene(def, false);
                }
                foreach (string defName in snapshot.xenogeneDefNames)
                {
                    GeneDef def = DefDatabase<GeneDef>.GetNamedSilentFail(defName);
                    if (def != null && !HasGene(copy, def)) copy.genes.AddGene(def, true);
                }
                copy.genes.xenotypeName = snapshot.xenotypeName;
            }

            if (Enum.IsDefined(typeof(AsuranIdeologicalStance), snapshot.ideologicalStance))
            {
                AsuranIdeologicalStance stance = (AsuranIdeologicalStance)snapshot.ideologicalStance;
                if (stance != AsuranIdeologicalStance.Unassigned)
                    AsuranIdeologyUtility.RestoreStance(copy, stance);
            }

            if (Enum.IsDefined(typeof(HumanFormSpecialistRole), snapshot.specialistRole))
            {
                HumanFormSpecialistRole role = (HumanFormSpecialistRole)snapshot.specialistRole;
                if (role != HumanFormSpecialistRole.Unassigned)
                    HumanFormSpecialistRoleUtility.RestoreRole(copy, role);
            }

            HediffDef continuityLoss = DefDatabase<HediffDef>.GetNamedSilentFail("WNG_AsuranContinuityLoss");
            if (continuityLoss != null && copy.health?.hediffSet?.GetFirstHediffOfDef(continuityLoss) == null)
                copy.health.AddHediff(continuityLoss);
        }

        private static bool HasGene(Pawn pawn, GeneDef def) => pawn?.genes?.GenesListForReading.Any(g => g?.def == def) == true;
        private static void StripGeneratedGear(Pawn pawn) { pawn?.apparel?.DestroyAll(); pawn?.equipment?.DestroyAllEquipment(); }
        private static void DestroyUncommitted(Pawn pawn) { if (pawn != null && !pawn.Destroyed) pawn.Destroy(DestroyMode.Vanish); }

        private bool CanPay()
        {
            ThingDef advanced = DefDatabase<ThingDef>.GetNamedSilentFail("ComponentSpacer");
            return advanced != null
                && CountMapResource(ThingDefOf.Plasteel) >= Math.Max(0, Props.plasteelCost)
                && CountMapResource(advanced) >= Math.Max(0, Props.advancedComponentCost);
        }

        private bool ConsumeResources()
        {
            if (!CanPay()) return false;
            ConsumeMapResource(ThingDefOf.Plasteel, Math.Max(0, Props.plasteelCost));
            ThingDef advanced = DefDatabase<ThingDef>.GetNamedSilentFail("ComponentSpacer");
            ConsumeMapResource(advanced, Math.Max(0, Props.advancedComponentCost));
            return true;
        }

        private int CountMapResource(ThingDef def)
        {
            if (def == null || parent?.Map == null) return 0;
            return parent.Map.listerThings.ThingsOfDef(def)
                .Where(t => t != null && !t.Destroyed && t.Spawned)
                .Sum(t => Math.Max(0, t.stackCount));
        }

        private void ConsumeMapResource(ThingDef def, int amount)
        {
            int remaining = Math.Max(0, amount);
            if (remaining <= 0 || def == null || parent?.Map == null) return;
            foreach (Thing thing in parent.Map.listerThings.ThingsOfDef(def).Where(t => t != null && !t.Destroyed && t.Spawned).ToList())
            {
                if (remaining <= 0) break;
                int take = Math.Min(remaining, thing.stackCount);
                if (take >= thing.stackCount) thing.Destroy(DestroyMode.Vanish);
                else thing.stackCount -= take;
                remaining -= take;
            }
        }

        private bool IsPrimaryPoweredArchive()
        {
            Thing first = parent.Map.listerThings.ThingsOfDef(parent.def)
                .Where(t => t != null && !t.Destroyed && t.Spawned && t.Faction == parent.Faction)
                .Where(t => t.TryGetComp<CompAsuranPatternArchive>()?.Powered == true)
                .OrderBy(t => t.thingIDNumber).FirstOrDefault();
            return first == parent;
        }

        private void Trim()
        {
            int max = Math.Max(1, Props.maxRecords);
            if (snapshots.Count <= max) return;
            snapshots = snapshots
                .Where(s => s != null)
                .OrderByDescending(s => s.pendingReconstruction)
                .ThenByDescending(s => s.backupTick)
                .Take(max).ToList();
        }

        private static int SafeFutureTick(int now, int delta)
        {
            long value = (long)now + Math.Max(1, delta);
            return value >= int.MaxValue ? int.MaxValue : (int)value;
        }

        public override string CompInspectStringExtra()
        {
            int pending = snapshots.Count(s => s != null && s.pendingReconstruction);
            string age = lastSuccessfulBackupTick < 0 ? "none" : ((Find.TickManager.TicksGame - lastSuccessfulBackupTick) / 60000f).ToString("0.0") + " days";
            return "Stored patterns: " + snapshots.Count
                + "\nPending reconstructions: " + pending
                + "\nLatest archive sweep: " + age
                + "\nReconstruction cost: " + Props.plasteelCost + " plasteel, " + Props.advancedComponentCost + " advanced components";
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref snapshots, "wngAsuranPatternSnapshots", LookMode.Deep);
            Scribe_Values.Look(ref nextBackupTick, "wngAsuranNextBackupTick", -1);
            Scribe_Values.Look(ref lastSuccessfulBackupTick, "wngAsuranLastBackupTick", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                snapshots ??= new List<AsuranPatternSnapshot>();
        }
    }
}
