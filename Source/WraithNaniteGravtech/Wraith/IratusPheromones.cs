using System;
using RimWorld;
using Verse;
namespace WraithNaniteGravtech
{
 public sealed class CompProperties_IratusPheromoneAttractor : CompProperties
 {
  public int exposedTicksRequired = 60000;
  public int maxIratusPerMap = 12;
  public int maxArrivalCount = 3;
  public float startingChancePerRareTick = 0.001f;
  public float maximumChancePerRareTick = 0.01f;
  public int rampUpTicks = 240000;
  public CompProperties_IratusPheromoneAttractor(){compClass=typeof(CompIratusPheromoneAttractor);}
 }
 // Like Replicator blocks: save-persistent on-map exposure and safe stack splitting.
 public sealed class CompIratusPheromoneAttractor : ThingComp
 {
  private const int RareInterval = 250;
  private int exposedTicks;
  private CompProperties_IratusPheromoneAttractor Props => (CompProperties_IratusPheromoneAttractor)props;
  public override void PostSplitOff(Thing piece)
  {
   base.PostSplitOff(piece);
   var other=piece?.TryGetComp<CompIratusPheromoneAttractor>();
   if(other!=null)other.exposedTicks=exposedTicks;
  }
  public override void PreAbsorbStack(Thing otherStack,int count)
  {
   base.PreAbsorbStack(otherStack,count);
   var other=otherStack?.TryGetComp<CompIratusPheromoneAttractor>();
   if(other!=null)exposedTicks=Math.Max(exposedTicks,other.exposedTicks);
  }
  public override void CompTickRare()
  {
   base.CompTickRare();
   if(parent?.Spawned!=true||parent.Map==null||parent.Destroyed||parent.stackCount<=0)return;
   exposedTicks=exposedTicks>=int.MaxValue-RareInterval?int.MaxValue:exposedTicks+RareInterval;
   int threshold=Math.Max(RareInterval,Props.exposedTicksRequired);
   if(exposedTicks<threshold)return;
   // Risk starts low after the first exposed day, then rises gradually
   // for four more game days. Roll only every rare tick, never every tick.
   float fraction=Math.Min(1f,(float)(exposedTicks-threshold)/Math.Max(RareInterval,Props.rampUpTicks));
   float chance=Props.startingChancePerRareTick+
     (Props.maximumChancePerRareTick-Props.startingChancePerRareTick)*fraction;
   if(!Rand.Chance(Math.Max(0f,Math.Min(1f,chance)))||!TryAttract())return;
   // One unit is consumed only after at least one wild Iratus is placed.
   if(parent.stackCount==1)parent.Destroy(DestroyMode.Vanish);
   else {parent.stackCount--;exposedTicks=0;}
  }
  private bool TryAttract()
  {
   Map map=parent?.Map;
   var kind=DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_IratusBug");
   if(map==null||kind?.race==null||parent.Destroyed)return false;
   int existing=0;
   foreach(Pawn pawn in map.mapPawns.AllPawnsSpawned)
    if(pawn!=null&&!pawn.Dead&&pawn.def==kind.race)existing++;
   int capacity=Math.Max(1,Props.maxIratusPerMap)-existing;
   if(capacity<=0||!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 entry,map,0f))return false;
   int wanted=Math.Min(Math.Min(Math.Max(1,Props.maxArrivalCount),
     1+Math.Max(0,parent.stackCount-1)/3),capacity);
   int placed=0;
   for(int i=0;i<wanted;i++)
   {
    Pawn bug=null;
    try
    {
     bug=PawnGenerator.GeneratePawn(kind,null);
     if(bug==null)continue;
     if(!GenPlace.TryPlaceThing(bug,entry,map,ThingPlaceMode.Near,null,
       cell=>cell.InBounds(map)&&cell.Standable(map)))
     {
      if(!bug.Destroyed)bug.Destroy(DestroyMode.Vanish);
      continue;
     }
     if(bug.Spawned)placed++;
    }
    catch(Exception ex)
    {
     if(bug!=null&&!bug.Spawned&&!bug.Destroyed)bug.Destroy(DestroyMode.Vanish);
     Log.Warning("[WNG] Iratus pheromone arrival failed: "+ex.GetType().Name+": "+ex.Message);
    }
   }
   if(placed<=0)return false;
   if(map.IsPlayerHome)
    Messages.Message("Exposed Iratus pheromones attracted "+placed+" Iratus bug"+
      (placed==1?"":"s")+" to the colony map.",new TargetInfo(entry,map),
      MessageTypeDefOf.ThreatSmall,false);
   return true;
  }
  public override void PostExposeData()
  {
   base.PostExposeData();
   Scribe_Values.Look(ref exposedTicks,"wngIratusPheromoneExposure",0);
  }
  public override string CompInspectStringExtra()
  {
   int required=Math.Max(RareInterval,Props.exposedTicksRequired);
   int over=Math.Max(0,exposedTicks-required);
   float fraction=Math.Min(1f,(float)over/Math.Max(RareInterval,Props.rampUpTicks));
   float chance=Props.startingChancePerRareTick+
     (Props.maximumChancePerRareTick-Props.startingChancePerRareTick)*fraction;
   return "Iratus pheromones: "+Math.Min(exposedTicks,required)+" / "+required+
     " ticks until active. "+(exposedTicks>=required
       ? "Attraction risk "+(chance*100f).ToString("0.00")+"% per 250 ticks."
       : "No Iratus attraction before one exposed day.")+
     " A successful arrival consumes one unit.";
  }
 }
}
