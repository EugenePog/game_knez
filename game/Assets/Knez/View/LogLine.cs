namespace Knez.Game.View
{
    /// <summary>One line of the battle log (M3 step 8): when, what kind, which side it concerns (−1: none), and the words.</summary>
    public readonly struct LogLine
    {
        /// <summary>Creates a line.</summary>
        public LogLine(int tick, LogKind kind, int side, string text)
        {
            Tick = tick;
            Kind = kind;
            Side = side;
            Text = text;
        }

        /// <summary>The tick it happened in.</summary>
        public int Tick { get; }

        /// <summary>What it is about.</summary>
        public LogKind Kind { get; }

        /// <summary>The side of the man or squad it is about, or −1.</summary>
        public int Side { get; }

        /// <summary>The words, without the time.</summary>
        public string Text { get; }
    }
}
