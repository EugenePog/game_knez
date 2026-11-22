using Knez.Game.View;
using NUnit.Framework;

namespace Knez.Game.Tests
{
    public class SimClockTests
    {
        private static int Run(SimClock clock, int frames, double frameSeconds)
        {
            int ticks = 0;
            for (int i = 0; i < frames; i++)
            {
                ticks += clock.Advance(frameSeconds);
            }

            return ticks;
        }

        [Test]
        public void OneSecondAt60Fps_Steps20Ticks()
        {
            Assert.That(Run(new SimClock(), 60, 1.0 / 60), Is.EqualTo(20));
        }

        [Test]
        public void DoubleSpeed_Steps40Ticks_Paused_StepsNone()
        {
            Assert.That(Run(new SimClock { Speed = 2f }, 60, 1.0 / 60), Is.EqualTo(40));
            Assert.That(Run(new SimClock { Paused = true }, 60, 1.0 / 60), Is.Zero);
        }

        [Test]
        public void AFiveSecondHitch_StepsAtMostTheCap_AndDropsTheBacklog()
        {
            var clock = new SimClock();
            Assert.That(clock.Advance(5.0), Is.EqualTo(clock.MaxTicksPerFrame));
            Assert.That(clock.Advance(1.0 / 60), Is.LessThanOrEqualTo(1), "no catch-up burst afterwards");
        }

        [Test]
        public void FasterAndSlower_WalkTheSpeedList_AndStopAtItsEnds()
        {
            var clock = new SimClock();
            clock.Faster();
            Assert.That(clock.Speed, Is.EqualTo(2f));
            clock.Faster();
            clock.Faster();
            Assert.That(clock.Speed, Is.EqualTo(4f));
            for (int i = 0; i < 6; i++)
            {
                clock.Slower();
            }

            Assert.That(clock.Speed, Is.EqualTo(0.25f));
        }
    }
}
