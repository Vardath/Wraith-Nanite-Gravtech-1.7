from pathlib import Path
import xml.etree.ElementTree as ET
R=Path("."); fail=[]
def txt(p): return (R/p).read_text(encoding="utf-8",errors="ignore")
kpath="Defs/PawnKindDefs/PawnKinds_HumanForm.xml"
apath="Defs/AbilityDefs/Abilities_HumanForm.xml"
dpath="Defs/ThingDefs/Things_AsuranPatternArchive.xml"
npath="Source/WraithNaniteGravtech/Asurans/NeuralInterface.cs"
ppath="Source/WraithNaniteGravtech/Asurans/AsuranPatternArchive.cs"
rpath="Source/WraithNaniteGravtech/Asurans/HumanFormSpecialistRoles.cs"
qpath="Source/WraithNaniteGravtech/Asurans/ReplicatorQueen.cs"
for p in (kpath,apath,dpath,npath,ppath,rpath,qpath):
    if not (R/p).exists(): fail.append("missing "+p)
if fail:
    print("\n".join(fail)); raise SystemExit(1)
kroot=ET.parse(R/kpath).getroot()
kinds={(n.findtext("defName") or "").strip():n for n in list(kroot) if n.tag=="PawnKindDef" and (n.findtext("defName") or "").strip()}
required=["WNG_PrecursorEngineer","WNG_PrecursorSoldier","WNG_PrecursorCommander","WNG_HumanFormReplicator","WNG_HumanFormInfiltrator","WNG_PlayerHumanFormReplicator","WNG_HumanFormCopy","WNG_ReplicatorQueenChild"]
for name in required:
    if name not in kinds: fail.append("missing PawnKind "+name)
child=kinds.get("WNG_ReplicatorQueenChild")
if child is not None:
    for tag,val in {"fixedGender":"Female","minGenerationAge":"13.01","maxGenerationAge":"13.99","weaponMoney":"0","apparelMoney":"5000~5000"}.items():
        if (child.findtext(tag) or "").strip()!=val: fail.append("Queen child "+tag+" mismatch")
    xs=child.findall("./xenotypeSet/xenotypeChances/*")
    if len(xs)!=1 or xs[0].tag!="WNG_HumanFormReplicator": fail.append("Queen child xenotype mismatch")
ability=txt(apath); neural=txt(npath); archive=txt(ppath); roles=txt(rpath); queen=txt(qpath)
if "<fallbackCopyPawnKind>WNG_HumanFormCopy</fallbackCopyPawnKind>" not in ability: fail.append("copy fallback kind missing")
for token in ["if (AsuranCollectiveUtility.IsNaniteSynthetic(subject))","Gene_Resource_NaniteReserve","reserve.CanSpend(cost)","GenerateRacePreservingBody(caster, subject, fallbackCopyPawnKind)","CopyExactPattern(subject, copy)","LayerHumanFormNanites(copy)","AsuranDefaultApparelUtility.EnsureRoleApparel(copy)","GenPlace.TryPlaceThing(copy, caster.Position, caster.Map, ThingPlaceMode.Near)","if (!reserve.TrySpend(cost))","PawnGenerator.GeneratePawn(fallback, caster.Faction)","copy.ageTracker.AgeBiologicalTicks","CopyGenome(source, copy)"]:
    if token not in neural: fail.append("Neural Interface contract missing: "+token)
if "DevelopmentalStage.Adult" in neural: fail.append("copy path became adult-only")
aroot=ET.parse(R/dpath).getroot()
anode=next((n for n in list(aroot) if n.tag=="ThingDef" and (n.findtext("defName") or "").strip()=="WNG_AsuranPatternArchive"),None)
if anode is None: fail.append("Pattern Archive ThingDef missing")
else:
    classes={(x.get("Class") or "").strip() for x in anode.findall("./comps/li")}
    if "WraithNaniteGravtech.CompProperties_AsuranPatternArchive" not in classes: fail.append("archive continuity comp missing")
    if "CompProperties_Power" not in classes: fail.append("archive power comp missing")
for token in ["public sealed class AsuranPatternSnapshot : IExposable",'Scribe_Values.Look(ref specialistRole, "specialistRole"','Scribe_Collections.Look(ref snapshots, "wngAsuranPatternSnapshots", LookMode.Deep)',"pawn.Faction == parent.Faction","AsuranCollectiveUtility.IsLinked(pawn)","!ReplicatorQueenUtility.IsExactQueen(pawn)","snapshots.RemoveAll(IsExactQueenSnapshot)","ReplicatorQueenUtility.IsExactQueen(dead)","PawnGenerator.GeneratePawn(kind, parent.Faction)","HumanFormSpecialistRoleUtility.RestoreRole(copy, role)","snapshot.pawnThingId = copy.thingIDNumber","snapshot.pendingReconstruction = false"]:
    if token not in archive: fail.append("Archive contract missing: "+token)
for token in ["private Pawn exactQueen;","public Pawn ExactQueen => exactQueen;","if (exactQueen != null && exactQueen != queen)",'Scribe_References.Look(ref exactQueen, "wngExactReplicatorQueen")',"State?.ExactQueen == pawn",'DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_ReplicatorQueenChild")']:
    if token not in queen: fail.append("Queen identity contract missing: "+token)
for token in ["WNG_HumanFormRoleEngineer","WNG_HumanFormRoleInfiltrator","WNG_HumanFormRoleSoldier","WNG_HumanFormRoleCoordinator","WNG_HumanFormRoleCommander","ReplicatorQueenUtility.IsExactQueen(pawn)",'pawn.kindDef?.defName != "WNG_ReplicatorQueenChild"',"HumanFormSpecialistRole EnsureRole","void RestoreRole"]:
    if token not in roles: fail.append("role contract missing: "+token)
print("=== D152 HUMAN-FORM / ASURAN COPY AUDIT ===")
print(" - PawnKinds checked:",len(required))
print(" - copy transaction, child age, Queen exclusion, archive, roles, faction and persistence checked")
if fail:
    print("FAILURES:")
    for x in fail: print(" -",x)
    raise SystemExit(1)
print("PASS: human-form / Asuran copy continuity contracts are coherent.")
