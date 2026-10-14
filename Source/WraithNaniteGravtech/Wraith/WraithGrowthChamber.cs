using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_PlayerWraithGrowthChamber : CompProperties
    {
        public float donorMinimumLifeForce = 0.55f;
        public float donorLifeForceCost = 0.25f;
        public float newbornLifeForce = 0.35f;
        public float gravSubstructureBiomassFactor = 0.70f;
        public float gravSubstructureDurationFactor = 0.65f;

        public CompProperties_PlayerWraithGrowthChamber()
        {
            compClass = typeof(CompPlayerWraithGrowthChamber);
        }
    }

    /// <summary>
    /// Player-only natural Wraith reproduction. This component does not own Mature-Hive ecology,
    /// faction hunger, feeding stock, or an NPC population cap. Any living, conscious player Wraith,
    /// including the scenario-start Hunter caste, can initiate a paid cycle. The initiator need
    /// not remain present after payment; normal donor and resource constraints still apply.
    /// </summary>
    public sealed class CompPlayerWraithGrowthChamber : ThingComp
    {
        private const int HunterBiomass = 160;
        private const int HunterTicks = 120000;
        private const int WarriorBiomass = 220;
        private const int WarriorTicks = 180000;
        private const int KeeperBiomass = 280;
        private const int KeeperTicks = 240000;
        private const int CommanderBiomass = 340;
        private const int CommanderTicks = 300000;
        private const int QueenBiomass = 460;
        private const int QueenTicks = 360000;
        private const int CompletionRetryTicks = 250;

        private string activeKindDefName;
        private int activeFinishTick = -1;
        private int activeStartedTick = -1;
        private int activeChargedBiomass;
        private bool activeGravSubstructureBonus;
        private string activeTemplateLabel;
        private int nextCompletionAttemptTick = -1;

        public CompProperties_PlayerWraithGrowthChamber Props => (CompProperties_PlayerWraithGrowthChamber)props;
        public bool CycleActive => !activeKindDefName.NullOrEmpty() && activeFinishTick >= 0;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
                yield return gizmo;

            if (parent?.Faction != Faction.OfPlayer || !parent.Spawned || parent.Map == null)
                yield break;

            yield return MakeGestationCommand("Hunter", "WNG_WraithHunter", HunterBiomass, HunterTicks);
            yield return MakeGestationCommand("Warrior", "WNG_WraithWarrior", WarriorBiomass, WarriorTicks);
            yield return MakeGestationCommand("Keeper", "WNG_WraithKeeper", KeeperBiomass, KeeperTicks);
            yield return MakeCommanderGestationCommand();
            yield return MakeGestationCommand("Queen", "WNG_WraithQueen", QueenBiomass, QueenTicks);
        }

        // Resolve icons with static asset paths so validation can confirm
        // each bespoke caste asset and no accidental empty/unrendered button.
        private static Texture2D CasteIcon(string caste)
        {
            switch (caste)
            {
                case "Hunter": return ContentFinder<Texture2D>.Get("UI/WNG/GrowthChamber/GestateHunter", false);
                case "Warrior": return ContentFinder<Texture2D>.Get("UI/WNG/GrowthChamber/GestateWarrior", false);
                case "Keeper": return ContentFinder<Texture2D>.Get("UI/WNG/GrowthChamber/GestateKeeper", false);
                case "Commander": return ContentFinder<Texture2D>.Get("UI/WNG/GrowthChamber/GestateCommander", false);
                case "Queen": return ContentFinder<Texture2D>.Get("UI/WNG/GrowthChamber/GestateQueen", false);
                default: return null;
            }
        }

        private Command_Action MakeGestationCommand(string casteLabel, string kindDefName, int baseBiomass, int baseTicks)
        {
            bool boosted = HasGravSubstructureBonus();
            int biomass = EffectiveBiomassCost(baseBiomass, boosted);
            int ticks = EffectiveDuration(baseTicks, boosted);
            Command_Action command = new Command_Action
            {
                defaultLabel = "Gestate Wraith " + casteLabel,
                icon = CasteIcon(casteLabel),
                defaultDesc = "Initiate a " + casteLabel + " gestation cycle. Requires a living, conscious player Wraith (including a Hunter) to initiate, " +
                    biomass + " Wraith biomass and a Wraith donor with at least " + Props.donorMinimumLifeForce.ToString("0.00") +
                    " Life Force. The donor contributes " + Props.donorLifeForceCost.ToString("0.00") +
                    " Life Force. " + (boosted ? "Grav substructure is active: biomass and gestation time are reduced for this cycle." : "If the chamber is installed on grav substructure, the cycle receives the established efficiency bonus."),
                action = () => TryStartCycle(kindDefName, casteLabel, baseBiomass, baseTicks)
            };

            if (!CanStartCycle(baseBiomass, out string reason))
                command.Disable(reason);
            else
                command.defaultDesc += "\nGestation time: " + (ticks / 60000f).ToString("0.##") + " days.";

            return command;
        }

        private Command_Action MakeCommanderGestationCommand()
        {
            bool boosted = HasGravSubstructureBonus();
            int biomass = EffectiveBiomassCost(CommanderBiomass, boosted);
            int ticks = EffectiveDuration(CommanderTicks, boosted);
            Command_Action command = new Command_Action
            {
                defaultLabel = "Gestate Wraith Commander",
                icon = CasteIcon("Commander"),
                defaultDesc = "Initiate a Commander gestation cycle. Commander growth requires an existing Wraith biological template: either a Wraith corpse or a living Wraith prisoner. A living prisoner is lethally consumed by the process in the same irreversible sense as a Biotech ripscanner; a corpse is consumed outright. Also requires a living, conscious player Wraith (including a Hunter) to initiate, " +
                    biomass + " Wraith biomass and a Wraith donor with at least " + Props.donorMinimumLifeForce.ToString("0.00") +
                    " Life Force. The donor contributes " + Props.donorLifeForceCost.ToString("0.00") +
                    " Life Force. " + (boosted ? "Grav substructure is active: biomass and gestation time are reduced for this cycle." : "If the chamber is installed on grav substructure, the cycle receives the established efficiency bonus.") +
                    "\nGestation time: " + (ticks / 60000f).ToString("0.##") + " days.",
                action = BeginCommanderTargeting
            };

            if (!CanStartCycle(CommanderBiomass, out string reason))
                command.Disable(reason);
            else if (!HasCommanderTemplate())
                command.Disable("Requires either a Wraith corpse or a living Wraith prisoner on this map.");

            return command;
        }

        private void BeginCommanderTargeting()
        {
            if (!CanStartCycle(CommanderBiomass, out string reason))
            {
                Messages.Message(reason, parent, MessageTypeDefOf.RejectInput, false);
                return;
            }

            TargetingParameters parameters = new TargetingParameters
            {
                canTargetPawns = true,
                canTargetItems = true,
                canTargetBuildings = false,
                canTargetLocations = false,
                canTargetSelf = false,
                validator = info => IsValidCommanderTemplate(info.Thing)
            };
            Find.Targeter.BeginTargeting(parameters, ConfirmCommanderTemplate);
        }

        private void ConfirmCommanderTemplate(LocalTargetInfo target)
        {
            Thing template = target.Thing;
            if (!IsValidCommanderTemplate(template))
            {
                Messages.Message("Commander gestation requires either a Wraith corpse or a living Wraith prisoner.", parent, MessageTypeDefOf.RejectInput, false);
                return;
            }

            Pawn prisoner = template as Pawn;
            if (prisoner != null)
            {
                string name = prisoner.LabelShort;
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "Use " + name + " as the biological template for a Wraith Commander? The Growth Chamber process will kill and consume this prisoner.",
                    () => TryStartCommanderCycle(template),
                    destructive: true));
                return;
            }

            TryStartCommanderCycle(template);
        }

        private bool HasCommanderTemplate()
        {
            if (parent?.Map == null)
                return false;

            if (parent.Map.mapPawns.AllPawnsSpawned.Any(p => IsValidCommanderTemplate(p)))
                return true;

            IReadOnlyList<Thing> corpses = parent.Map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse);
            for (int i = 0; i < corpses.Count; i++)
            {
                if (IsValidCommanderTemplate(corpses[i]))
                    return true;
            }
            return false;
        }

        private bool IsValidCommanderTemplate(Thing thing)
        {
            if (thing == null || thing.Destroyed || !thing.Spawned || parent?.Map == null || thing.Map != parent.Map)
                return false;

            Corpse corpse = thing as Corpse;
            if (corpse != null)
                return corpse.InnerPawn != null && WraithHiveEcologyUtility.IsWraith(corpse.InnerPawn);

            Pawn pawn = thing as Pawn;
            return pawn != null && !pawn.Dead && pawn.IsPrisonerOfColony && WraithHiveEcologyUtility.IsWraith(pawn);
        }

        private void TryStartCommanderCycle(Thing template)
        {
            if (!CanStartCycle(CommanderBiomass, out string reason))
            {
                Messages.Message(reason, parent, MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (!IsValidCommanderTemplate(template))
            {
                Messages.Message("The selected Wraith biological template is no longer available.", parent, MessageTypeDefOf.RejectInput, false);
                return;
            }

            Pawn initiator = FindInitiator();
            bool boosted = HasGravSubstructureBonus();
            int biomassCost = EffectiveBiomassCost(CommanderBiomass, boosted);
            int durationTicks = EffectiveDuration(CommanderTicks, boosted);
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_WraithCommander");
            ThingDef biomass = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_Biomass");
            Gene_Resource_LifeForce donor = WraithHiveEcologyUtility.FindChargedDonor(parent.Map, Faction.OfPlayer, Props.donorMinimumLifeForce);
            if (kind == null || biomass == null || donor == null || initiator == null)
            {
                Messages.Message("Commander gestation could not resolve its caste, supervisor, donor or biomass definitions.", parent, MessageTypeDefOf.RejectInput, false);
                return;
            }

            float oldDonorValue = donor.Value;
            if (!WraithHiveEcologyUtility.TryConsumeResource(parent.Map, biomass, biomassCost))
            {
                Messages.Message("The required Wraith biomass could not be committed safely.", parent, MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (!donor.TrySpend(Math.Max(0f, Props.donorLifeForceCost)))
            {
                WraithHiveEcologyUtility.SpawnResource(parent.Map, parent.Position, biomass, biomassCost);
                Messages.Message("The donor could not commit the required Life Force; the biomass was refunded.", parent, MessageTypeDefOf.RejectInput, false);
                return;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            string templateLabel = CommanderTemplateLabel(template);
            activeKindDefName = "WNG_WraithCommander";
            activeStartedTick = now;
            activeFinishTick = now + Math.Max(1, durationTicks);
            activeChargedBiomass = biomassCost;
            activeGravSubstructureBonus = boosted;
            activeTemplateLabel = templateLabel;
            nextCompletionAttemptTick = activeFinishTick;

            if (!TryConsumeCommanderTemplate(template, out bool irreversible))
            {
                if (!irreversible)
                {
                    ClearCycle();
                    donor.Value = oldDonorValue;
                    WraithHiveEcologyUtility.SpawnResource(parent.Map, parent.Position, biomass, biomassCost);
                    Messages.Message("The Commander biological template could not be committed; Life Force and biomass were restored.", parent, MessageTypeDefOf.RejectInput, false);
                }
                else
                {
                    Log.Warning("[WNG] Commander template was irreversibly consumed after cycle staging but cleanup reported a failure; the paid Commander cycle remains active.");
                }
                return;
            }

            WraithLivingTechnologyUtility.BestEffortMessage(
                initiator.LabelShort + " initiated Commander gestation using " + templateLabel +
                (boosted ? " and the grav-substructure efficiency bonus." : "."),
                parent,
                MessageTypeDefOf.PositiveEvent);
        }

        private bool TryConsumeCommanderTemplate(Thing template, out bool irreversible)
        {
            irreversible = false;
            if (!IsValidCommanderTemplate(template))
                return false;

            Corpse corpse = template as Corpse;
            if (corpse != null)
            {
                try
                {
                    corpse.Destroy(DestroyMode.Vanish);
                    irreversible = corpse.Destroyed;
                    return irreversible;
                }
                catch (Exception ex)
                {
                    irreversible = corpse.Destroyed;
                    if (irreversible)
                    {
                        Log.Warning("[WNG] Commander corpse template was consumed but cleanup raised an exception: " + ex.Message);
                        return true;
                    }
                    Log.Error("[WNG] Commander corpse template consumption failed before commit: " + ex);
                    return false;
                }
            }

            Pawn prisoner = template as Pawn;
            if (prisoner == null)
                return false;

            try
            {
                BodyPartRecord brain = prisoner.health?.hediffSet?.GetBrain();
                DamageInfo dinfo = new DamageInfo(DamageDefOf.ExecutionCut, 9999f, 999f, -1f, null, brain);
                dinfo.SetIgnoreInstantKillProtection(ignore: true);
                dinfo.SetAllowDamagePropagation(val: false);
                prisoner.forceNoDeathNotification = true;
                try
                {
                    prisoner.TakeDamage(dinfo);
                }
                finally
                {
                    prisoner.forceNoDeathNotification = false;
                }

                irreversible = prisoner.Dead;
                if (!irreversible)
                    return false;

                try
                {
                    ThoughtUtility.GiveThoughtsForPawnExecuted(prisoner, null, PawnExecutionKind.Ripscanned);
                }
                catch (Exception ex)
                {
                    Log.Warning("[WNG] Commander template execution committed but execution-thought notification failed: " + ex.Message);
                }

                try
                {
                    prisoner.Corpse?.Destroy(DestroyMode.Vanish);
                }
                catch (Exception ex)
                {
                    Log.Warning("[WNG] Commander prisoner template was killed, but corpse cleanup failed: " + ex.Message);
                }
                return true;
            }
            catch (Exception ex)
            {
                irreversible = prisoner.Dead;
                if (irreversible)
                {
                    Log.Warning("[WNG] Commander prisoner template was irreversibly killed but post-kill handling failed: " + ex.Message);
                    try { prisoner.Corpse?.Destroy(DestroyMode.Vanish); } catch { }
                    return true;
                }
                Log.Error("[WNG] Commander prisoner template consumption failed before lethal commit: " + ex);
                return false;
            }
        }

        private string CommanderTemplateLabel(Thing template)
        {
            Corpse corpse = template as Corpse;
            if (corpse?.InnerPawn != null)
                return "the corpse of " + corpse.InnerPawn.LabelShort;
            Pawn pawn = template as Pawn;
            return pawn != null ? pawn.LabelShort : "a Wraith biological template";
        }

        private bool CanStartCycle(int baseBiomass, out string reason)
        {
            reason = null;
            if (parent == null || parent.Destroyed || !parent.Spawned || parent.Map == null || parent.Faction != Faction.OfPlayer)
            {
                reason = "The player Growth Chamber is unavailable.";
                return false;
            }
            if (CycleActive)
            {
                reason = "This Growth Chamber is already gestating a Wraith.";
                return false;
            }
            if (parent.TryGetComp<CompWraithHybridization>()?.CycleActive == true)
            {
                reason = "This Growth Chamber is already integrating a Wraith-human graft.";
                return false;
            }
            if (FindInitiator() == null)
            {
                reason = "A living, conscious player Wraith (including a Hunter) must be present to initiate gestation.";
                return false;
            }

            bool boosted = HasGravSubstructureBonus();
            int cost = EffectiveBiomassCost(baseBiomass, boosted);
            ThingDef biomass = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_Biomass");
            if (biomass == null || WraithHiveEcologyUtility.CountResource(parent.Map, biomass) < cost)
            {
                reason = "Requires " + cost + " Wraith biomass.";
                return false;
            }
            if (WraithHiveEcologyUtility.FindChargedDonor(parent.Map, Faction.OfPlayer, Props.donorMinimumLifeForce) == null)
            {
                reason = "Requires a living player Wraith donor with at least " + Props.donorMinimumLifeForce.ToString("0.00") + " Life Force.";
                return false;
            }
            return true;
        }

        private Pawn FindInitiator()
        {
            if (parent?.Map == null)
                return null;

            return parent.Map.mapPawns.AllPawnsSpawned
                .Where(p => p != null && !p.Dead && !p.Downed && p.Awake() && p.Faction == Faction.OfPlayer && WraithHiveEcologyUtility.IsWraith(p))
                .OrderBy(p => p.Position.DistanceToSquared(parent.Position))
                .FirstOrDefault();
        }

        private void TryStartCycle(string kindDefName, string casteLabel, int baseBiomass, int baseTicks)
        {
            if (!CanStartCycle(baseBiomass, out string reason))
            {
                Messages.Message(reason, parent, MessageTypeDefOf.RejectInput, false);
                return;
            }

            Pawn initiator = FindInitiator();
            bool boosted = HasGravSubstructureBonus();
            int biomassCost = EffectiveBiomassCost(baseBiomass, boosted);
            int durationTicks = EffectiveDuration(baseTicks, boosted);
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(kindDefName);
            ThingDef biomass = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_Biomass");
            Gene_Resource_LifeForce donor = WraithHiveEcologyUtility.FindChargedDonor(parent.Map, Faction.OfPlayer, Props.donorMinimumLifeForce);
            if (kind == null || biomass == null || donor == null || initiator == null)
            {
                Messages.Message("Growth Chamber gestation could not resolve its caste, supervisor, donor or biomass definitions.", parent, MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (!WraithHiveEcologyUtility.TryConsumeResource(parent.Map, biomass, biomassCost))
            {
                Messages.Message("The required Wraith biomass could not be committed safely.", parent, MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (!donor.TrySpend(Math.Max(0f, Props.donorLifeForceCost)))
            {
                WraithHiveEcologyUtility.SpawnResource(parent.Map, parent.Position, biomass, biomassCost);
                Messages.Message("The donor could not commit the required Life Force; the biomass was refunded.", parent, MessageTypeDefOf.RejectInput, false);
                return;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            activeKindDefName = kindDefName;
            activeStartedTick = now;
            activeFinishTick = now + Math.Max(1, durationTicks);
            activeChargedBiomass = biomassCost;
            activeGravSubstructureBonus = boosted;
            nextCompletionAttemptTick = activeFinishTick;

            WraithLivingTechnologyUtility.BestEffortMessage(
                initiator.LabelShort + " initiated " + casteLabel + " gestation in the Growth Chamber" +
                (boosted ? " using the grav-substructure efficiency bonus." : "."),
                parent,
                MessageTypeDefOf.PositiveEvent);
        }

        private bool HasGravSubstructureBonus()
        {
            if (parent == null || !parent.Spawned || parent.Map == null)
                return false;

            CellRect occupied = GenAdj.OccupiedRect(parent.Position, parent.Rotation, parent.def.Size);
            bool any = false;
            foreach (IntVec3 cell in occupied.Cells)
            {
                if (!cell.InBounds(parent.Map))
                    return false;
                TerrainDef foundation = parent.Map.terrainGrid.FoundationAt(cell);
                if (foundation == null || !foundation.IsSubstructure)
                    return false;
                any = true;
            }
            return any;
        }

        private int EffectiveBiomassCost(int baseCost, bool boosted)
        {
            float factor = boosted ? Mathf.Clamp(Props.gravSubstructureBiomassFactor, 0.01f, 1f) : 1f;
            return Math.Max(1, Mathf.CeilToInt(baseCost * factor));
        }

        private int EffectiveDuration(int baseTicks, bool boosted)
        {
            float factor = boosted ? Mathf.Clamp(Props.gravSubstructureDurationFactor, 0.01f, 1f) : 1f;
            return Math.Max(1, Mathf.CeilToInt(baseTicks * factor));
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!CycleActive || parent?.Faction != Faction.OfPlayer || !parent.Spawned || parent.Map == null)
                return;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (now < activeFinishTick || (nextCompletionAttemptTick >= 0 && now < nextCompletionAttemptTick))
                return;

            TryCompleteCycle(now);
        }

        private void TryCompleteCycle(int now)
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(activeKindDefName);
            if (kind == null)
            {
                Log.ErrorOnce("[WNG] Growth Chamber cannot resolve paid gestation caste " + activeKindDefName + "; cycle remains pending rather than creating a free replacement.", parent.thingIDNumber ^ 0x47524348);
                nextCompletionAttemptTick = now + CompletionRetryTicks;
                return;
            }

            Pawn newborn = null;
            try
            {
                newborn = PawnGenerator.GeneratePawn(kind, Faction.OfPlayer);
                if (newborn == null)
                    throw new InvalidOperationException("PawnGenerator returned no Wraith.");

                Gene_Resource_LifeForce resource = newborn.genes?.GetFirstGeneOfType<Gene_Resource_LifeForce>();
                if (resource != null)
                    resource.Value = Math.Min(resource.Max, Math.Max(0f, Props.newbornLifeForce));

                IntVec3 spawnCell = CellFinder.RandomClosewalkCellNear(parent.Position, parent.Map, 5);
                GenSpawn.Spawn(newborn, spawnCell, parent.Map);
                if (!newborn.Spawned || newborn.Map != parent.Map)
                    throw new InvalidOperationException("Gestated Wraith did not reach the map.");

                string caste = kind.label ?? kind.defName;
                ClearCycle();
                WraithLivingTechnologyUtility.BestEffortMessage(
                    "The Growth Chamber completed gestation of a " + caste + ".",
                    newborn,
                    MessageTypeDefOf.PositiveEvent);
            }
            catch (Exception ex)
            {
                // Physical spawn is the at-most-once commit boundary. Never leave a completed pawn
                // paired with an active cycle that could generate a duplicate after load/retry.
                bool committed = newborn != null && newborn.Spawned && newborn.Map == parent.Map;
                if (committed)
                {
                    ClearCycle();
                    Log.Warning("[WNG] Growth Chamber gestation physically committed, but post-spawn completion failed: " + ex.Message);
                    return;
                }

                if (newborn != null && !newborn.Destroyed && !newborn.Spawned)
                {
                    try { newborn.Destroy(DestroyMode.Vanish); }
                    catch { }
                }
                nextCompletionAttemptTick = now + CompletionRetryTicks;
                Log.ErrorOnce("[WNG] Paid Growth Chamber gestation could not complete; cycle remains paid and will retry placement: " + ex, parent.thingIDNumber ^ 0x47524349);
            }
        }

        private void ClearCycle()
        {
            activeKindDefName = null;
            activeFinishTick = -1;
            activeStartedTick = -1;
            activeChargedBiomass = 0;
            activeGravSubstructureBonus = false;
            activeTemplateLabel = null;
            nextCompletionAttemptTick = -1;
        }

        public override string CompInspectStringExtra()
        {
            if (parent?.Faction != Faction.OfPlayer)
                return null;

            string platform = HasGravSubstructureBonus()
                ? "Growth Chamber support: grav substructure (70% biomass / 65% gestation time on new cycles)"
                : "Growth Chamber support: ordinary terrain";
            if (!CycleActive)
                return platform + "\nReady for Hunter, Warrior, Keeper, Commander or Queen gestation. Any conscious player Wraith, including a starting Hunter, can initiate. Commander requires a Wraith corpse or living Wraith prisoner.";

            int now = Find.TickManager?.TicksGame ?? 0;
            int remaining = Math.Max(0, activeFinishTick - now);
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(activeKindDefName);
            string label = kind?.label ?? activeKindDefName ?? "Wraith";
            string templateLine = activeKindDefName == "WNG_WraithCommander" && !activeTemplateLabel.NullOrEmpty()
                ? "\nBiological template consumed: " + activeTemplateLabel
                : string.Empty;
            return platform + "\nGestating: " + label.CapitalizeFirst() +
                "\nRemaining: " + (remaining / 60000f).ToString("0.00") + " days" +
                "\nBiomass committed: " + activeChargedBiomass +
                (activeGravSubstructureBonus ? " (grav bonus snapshotted at initiation)" : string.Empty) +
                templateLine;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref activeKindDefName, "wngPlayerGrowthKindDefName");
            Scribe_Values.Look(ref activeFinishTick, "wngPlayerGrowthFinishTick", -1);
            Scribe_Values.Look(ref activeStartedTick, "wngPlayerGrowthStartedTick", -1);
            Scribe_Values.Look(ref activeChargedBiomass, "wngPlayerGrowthChargedBiomass", 0);
            Scribe_Values.Look(ref activeGravSubstructureBonus, "wngPlayerGrowthGravBonus", false);
            Scribe_Values.Look(ref activeTemplateLabel, "wngPlayerGrowthTemplateLabel");
            Scribe_Values.Look(ref nextCompletionAttemptTick, "wngPlayerGrowthNextCompletionAttemptTick", -1);
        }
    }
}
