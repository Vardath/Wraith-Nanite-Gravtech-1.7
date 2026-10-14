using RimWorld;
using Verse;
using Verse.Sound;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_WNGAmbientLoop : CompProperties
    {
        public SoundDef sound;
        public int repeatTicks = 360;

        public CompProperties_WNGAmbientLoop()
        {
            compClass = typeof(CompWNGAmbientLoop);
        }
    }

    public sealed class CompWNGAmbientLoop : ThingComp
    {
        private int nextPulseTick = -1;

        private CompProperties_WNGAmbientLoop Props => (CompProperties_WNGAmbientLoop)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            ScheduleFrom(Find.TickManager.TicksGame);
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned || parent.Map == null || Props.sound == null)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            if (nextPulseTick < 0)
            {
                ScheduleFrom(now);
                return;
            }

            if (now < nextPulseTick)
            {
                return;
            }

            Props.sound.PlayOneShot(new TargetInfo(parent.Position, parent.Map));
            int cadence = System.Math.Max(60, Props.repeatTicks);
            nextPulseTick = now + cadence;
        }

        private void ScheduleFrom(int now)
        {
            int cadence = System.Math.Max(60, Props.repeatTicks);
            int identity = parent != null ? parent.thingIDNumber : 0;
            int phase = identity >= 0 ? identity % cadence : (-identity) % cadence;
            nextPulseTick = now + phase;
        }
    }
}
