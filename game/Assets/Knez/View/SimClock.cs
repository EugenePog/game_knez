using Knez.Sim;

namespace Knez.Game.View
{
    /// <summary>
    /// Turns real time into simulation ticks (ADR-0003: the view owns real time; the sim only counts ticks). Each frame
    /// <see cref="Advance"/> says how many ticks to step: 20 per second of game time, scaled by <see cref="Speed"/>, none
    /// while <see cref="Paused"/>. A long frame (a hitch, a breakpoint) steps at most <see cref="MaxTicksPerFrame"/> and
    /// drops the rest, so the battle slows down instead of freezing to catch up. Plain C#: floats are fine in the view.
    /// </summary>
    public sealed class SimClock
    {
        /// <summary>The speeds the player can pick, slowest first.</summary>
        public static readonly float[] Speeds = { 0.25f, 0.5f, 1f, 2f, 4f };

        private double _pending; // game seconds owed to the sim, less than one tick after each Advance

        /// <summary>Most ticks stepped in one frame (at ×4 a 60 fps frame needs about 1.3).</summary>
        public int MaxTicksPerFrame { get; set; } = 8;

        /// <summary>No ticks while paused; the view keeps drawing the last state.</summary>
        public bool Paused { get; set; }

        /// <summary>Game seconds per real second.</summary>
        public float Speed { get; set; } = 1f;

        /// <summary>How far the next tick is, 0..1: the view interpolates between the last two states by this.</summary>
        public float Alpha => (float)(_pending * BattleState.TicksPerSecond);

        /// <summary>Adds <paramref name="realSeconds"/> of real time and returns the ticks to step now.</summary>
        public int Advance(double realSeconds)
        {
            if (Paused || realSeconds <= 0)
            {
                return 0;
            }

            _pending += realSeconds * Speed;
            // A hair of tolerance: frame times sum to 0.99999… of a tick, which must still count as a tick.
            int ticks = (int)((_pending * BattleState.TicksPerSecond) + 1e-6);
            if (ticks > MaxTicksPerFrame)
            {
                ticks = MaxTicksPerFrame;
                _pending = 0; // drop the backlog rather than run ever further behind
                return ticks;
            }

            _pending -= ticks / (double)BattleState.TicksPerSecond;
            return ticks;
        }

        /// <summary>The next faster speed (stays at the fastest).</summary>
        public void Faster() => Speed = Step(+1);

        /// <summary>The next slower speed (stays at the slowest).</summary>
        public void Slower() => Speed = Step(-1);

        private float Step(int direction)
        {
            int index = 0;
            for (int i = 0; i < Speeds.Length; i++)
            {
                if (Speeds[i] <= Speed + 0.001f)
                {
                    index = i;
                }
            }

            index += direction;
            return Speeds[index < 0 ? 0 : index >= Speeds.Length ? Speeds.Length - 1 : index];
        }
    }
}
