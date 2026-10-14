from pathlib import Path
import re
import xml.etree.ElementTree as ET

R=Path(".")
fail=[]

paths={
 "genes":"Defs/GeneDefs/Genes_Wraith.xml",
 "abilities":"Defs/AbilityDefs/Abilities_WraithLifeForce.xml",
 "life":"Source/WraithNaniteGravtech/Wraith/WraithLifeDrain.cs",
 "strategic":"Source/WraithNaniteGravtech/Wraith/WraithStrategicHunger.cs",
 "hive":"Source/WraithNaniteGravtech/Wraith/WraithHiveEcology.cs",
 "niche":"Source/WraithNaniteGravtech/Wraith/WraithPlayerFeedingNiche.cs",
 "retro":"Source/WraithNaniteGravtech/Wraith/WraithRetroviralTherapy.cs",
 "indep":"Source/WraithNaniteGravtech/Wraith/WraithFeedingIndependence.cs",
 "resist":"Defs/HediffDefs/Hediffs_FeedingResistance.xml",
 "retrodefs":"Defs/HediffDefs/Hediffs_WraithRetrovirus.xml",
 "indepdefs":"Defs/HediffDefs/Hediffs_WraithFeedingIndependence.xml",
}
for p in paths.values():
    if not (R/p).exists(): fail.append("missing "+p)
if fail:
    print("\n".join(fail)); raise SystemExit(1)

def txt(k): return (R/paths[k]).read_text(encoding="utf-8",errors="ignore")
genes=txt("genes"); abilities=txt("abilities"); life=txt("life"); strategic=txt("strategic")
hive=txt("hive"); niche=txt("niche"); retro=txt("retro"); indep=txt("indep")
resist=txt("resist"); retrodefs=txt("retrodefs"); indepdefs=txt("indepdefs")

# Child feeding contract: Life Force is present genetically but inactive until 13,
# so Food is only disabled when the gene becomes active.
groot=ET.parse(R/paths["genes"]).getroot()
life_gene=next((n for n in list(groot) if n.tag=="GeneDef" and (n.findtext("defName") or "")=="WNG_LifeForceMetabolism"),None)
if life_gene is None:
    fail.append("WNG_LifeForceMetabolism GeneDef missing")
else:
    if (life_gene.findtext("minAgeActive") or "").strip()!="13":
        fail.append("Wraith Life Force activation age is not 13")
    disabled=[(x.text or "").strip() for x in life_gene.findall("./disablesNeeds/li")]
    if "Food" not in disabled:
        fail.append("Life Force gene no longer disables Food when active")
    abs_=[(x.text or "").strip() for x in life_gene.findall("./abilities/li")]
    for a in ("WNG_LifeDrain","WNG_PartialFeed"):
        if a not in abs_: fail.append("Life Force gene lost ability "+a)

# Full/partial feeding definitions.
aroot=ET.parse(R/paths["abilities"]).getroot()
adefs={(n.findtext("defName") or "").strip():n for n in list(aroot) if n.tag=="AbilityDef"}
for name in ("WNG_LifeDrain","WNG_PartialFeed"):
    if name not in adefs: fail.append("missing feeding ability "+name)
def compvals(name):
    node=adefs.get(name)
    if node is None:return {}
    li=next((x for x in node.findall("./comps/li") if (x.get("Class") or "")=="WraithNaniteGravtech.CompProperties_AbilityLifeDrain"),None)
    if li is None:return {}
    return {c.tag:(c.text or "").strip() for c in list(li)}
full=compvals("WNG_LifeDrain"); partial=compvals("WNG_PartialFeed")
for key,val in {"lifeForceGain":"1","victimAgeYears":"50","casterRejuvenationYears":"5","killIfAlreadyDrained":"true","torsoDamage":"4"}.items():
    if full.get(key)!=val: fail.append(f"full feeding {key}={full.get(key)} expected {val}")
for key,val in {"lifeForceGain":"0.34","victimAgeYears":"10","casterRejuvenationYears":"1","killIfAlreadyDrained":"false","torsoDamage":"2"}.items():
    if partial.get(key)!=val: fail.append(f"partial feeding {key}={partial.get(key)} expected {val}")

# Feeding is an offensive touch attack and must remain usable against hostile biological pawns.
for name in ("WNG_LifeDrain","WNG_PartialFeed"):
    node=adefs.get(name)
    if node is None: continue
    if (node.findtext("hostile") or "").strip().lower()!="true":
        fail.append(name+" is no longer a hostile attack")
    if (node.findtext("aiCanUse") or "").strip().lower()!="true":
        fail.append(name+" is no longer AI-usable as a combat feeding action")
    target=node.find("./verbProperties/targetParams")
    if target is None or (target.findtext("neverTargetHostileFaction") or "").strip().lower()!="false":
        fail.append(name+" no longer explicitly permits hostile-faction targets")
    if target is None or (target.findtext("canTargetHumans") or "").strip().lower()!="true":
        fail.append(name+" no longer explicitly permits human targets")

for token in ("ApplyFeedingTorsoDamage(", "BodyPartDefOf.Torso", "DamageDefOf.Cut"):
    if token not in life:
        fail.append("feeding torso-damage contract missing: "+token)

# Ordinary feeding transaction must remain local and must not invoke strategic request UI/state.
for token in (
    "Gene_Resource_LifeForce",
    "alreadyLifeDrained && Props.killIfAlreadyDrained",
    "resource.AddLifeForce(",
    "AdjustBiologicalAge(victim",
    "AdjustBiologicalAge(caster",
    "FeedingResistanceExtractionFactor = 0.20f",
    "FeedingResistanceRejectionChance = 0.35f",
    "WNG_FeedingResistanceCrisis",
    "WNG_FeedingRejectionBacklash",
):
    if token not in life: fail.append("ordinary feeding contract missing: "+token)

# Remove comments before enforcing forbidden coupling.
def no_comments(s):
    s=re.sub(r"/\*.*?\*/","",s,flags=re.S)
    return re.sub(r"//.*","",s)
if "WraithStrategicHungerRegistry" in no_comments(life):
    fail.append("ordinary Life Drain directly references strategic hunger registry")
if "WraithStrategicHungerRegistry" in no_comments(hive):
    fail.append("local Mature-Hive feeding directly references strategic hunger registry")
if "WraithStrategicHungerRegistry" in no_comments(niche):
    fail.append("player Feeding Niche directly references strategic hunger registry")

# Strategic request system is the sole request/box owner and persists exact participants.
for token in (
    "public enum WraithFeedingRequestStage",
    "public List<Pawn> pendingSubjects",
    "public List<Pawn> pendingFeeders",
    'Scribe_Collections.Look(ref pendingSubjects, "pendingSubjects", LookMode.Reference)',
    'Scribe_Collections.Look(ref pendingFeeders, "pendingFeeders", LookMode.Reference)',
    "private bool requestWindowOpen",
    "Find.WindowStack.Add(new Dialog_MessageBox(",
    "ResolveRefusal(state, faction",
    "CreateRequestFeeders",
    "ControlledFeedVictimAgeYears = 10f",
    "ControlledFeedLifeForceGain = 0.34f",
    "resources[i]?.AddLifeForce(ControlledFeedLifeForceGain)",
):
    if token not in strategic: fail.append("strategic feeding request contract missing: "+token)

# Local feeding is nonlethal and bounded, separate from strategic UI.
for token in (
    "private List<Pawn> feedingStock = new List<Pawn>()",
    "private void RunLocalFeedingCycle()",
    "localFeedVictimAgeYears = 2",
    "localFeedLifeForceGain = 0.12f",
    "HungryWraiths(",
    "WNG_LifeDrained",
):
    if token not in hive: fail.append("Hive local-feeding contract missing: "+token)
for token in (
    "Keeper ration feeding",
    "HungryWraiths(",
    "WNG_LifeDrained",
):
    if token not in niche: fail.append("player niche feeding contract missing: "+token)

# Feeding resistance must exist on both sides of failed transfer.
for d in ("WNG_FeedingResistance","WNG_FeedingResistanceCrisis","WNG_FeedingRejectionBacklash"):
    if f"<defName>{d}</defName>" not in resist: fail.append("feeding resistance Hediff missing "+d)

# Retroviral humanization must preserve exact-pawn state and persist the relapse payload.
for token in (
    'HumanizationDefName = "WNG_WraithRetroviralHumanization"',
    "removedEndogenes",
    "removedXenogenes",
    "removedWraithAbilities",
    "removedAbilityCooldowns",
    "savedLifeForce",
    "suppressionExpiryTick",
    "ApplyHumanizedState()",
    "RestoreWraithState()",
    'Scribe_Collections.Look(ref removedEndogenes, "removedEndogenes", LookMode.Def)',
    'Scribe_Values.Look(ref savedLifeForce, "savedLifeForce", -1f)',
    'Scribe_Values.Look(ref suppressionExpiryTick, "suppressionExpiryTick", -1)',
):
    if token not in retro: fail.append("retroviral persistence/state contract missing: "+token)
if "<defName>WNG_WraithRetroviralHumanization</defName>" not in retrodefs:
    fail.append("retroviral humanization HediffDef missing")

# Feeding independence is a true metabolic transition: remove Life Force gene and refresh needs,
# with rollback if the postcondition fails. Rejection remains explicit.
for token in (
    'SuccessDefName = "WNG_FeedingIndependent"',
    'RejectionDefName = "WNG_FeedingIndependenceRejection"',
    'LifeForceGeneDefName = "WNG_LifeForceMetabolism"',
    "FirstGenerationSuccessChance = 0.40f",
    "pawn.genes.RemoveGene(lifeForce)",
    "pawn.needs?.AddOrRemoveNeedsAsAppropriate()",
    "if (!IsFeedingIndependent(pawn) || LifeForceGene(pawn) != null)",
    "pawn.genes.AddGene(",
):
    if token not in indep: fail.append("feeding-independence transaction contract missing: "+token)
for d in ("WNG_FeedingIndependent","WNG_FeedingIndependenceRejection"):
    if f"<defName>{d}</defName>" not in indepdefs: fail.append("feeding-independence Hediff missing "+d)

print("=== D153 WRAITH FEEDING AUDIT ===")
print(" - child ordinary-food window through age 12 / Life Force activation at 13: checked")
print(" - full and partial feeding transfer values: checked")
print(" - ordinary feeding vs strategic request isolation: checked")
print(" - strategic request participants/refusal/UI/save-state: checked")
print(" - local Hive and player Feeding Niche bounded feeding: checked")
print(" - feeding resistance rejection/extraction states: checked")
print(" - retroviral humanization/relapse persistence: checked")
print(" - permanent feeding-independence success/rejection transaction: checked")
if fail:
    print("FAILURES:")
    for x in fail: print(" -",x)
    raise SystemExit(1)
print("PASS: Wraith feeding, strategic-request isolation, child metabolism, resistance and treatment-state contracts are coherent.")
