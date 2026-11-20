using System;

namespace Knez.Sim
{
    /// <summary>
    /// PCG32 (XSH-RR) pseudo-random stream — the sim's only source of randomness (AGENTS.md hard rule 2).
    /// Its state is part of <see cref="BattleState"/>: hashed and snapshotted like any other field.
    /// </summary>
    public sealed class SimRng
    {
        private const ulong Multiplier = 6364136223846793005UL;
        private const ulong Golden = 0x9E3779B97F4A7C15UL;

        private ulong _state;
        private readonly ulong _increment;

        /// <summary>Seeds a stream exactly as the PCG reference <c>pcg32_srandom_r(initstate, initseq)</c>.</summary>
        public SimRng(ulong seed, ulong sequence)
        {
            _increment = (sequence << 1) | 1UL;
            _state = 0;
            NextUInt();
            _state = unchecked(_state + seed);
            NextUInt();
        }

        /// <summary>Current internal state (for hashing).</summary>
        public ulong State => _state;

        /// <summary>Stream selector (for hashing).</summary>
        public ulong Increment => _increment;

        /// <summary>The stream <paramref name="stream"/> of a battle seeded with <paramref name="battleSeed"/>.</summary>
        public static SimRng ForStream(ulong battleSeed, RngStream stream)
        {
            ulong streamIndex = (ulong)stream + 1UL;
            return new SimRng(SplitMix64(unchecked(battleSeed ^ (streamIndex * Golden))), streamIndex);
        }

        /// <summary>Next uniformly distributed 32-bit value.</summary>
        public uint NextUInt()
        {
            ulong old = _state;
            _state = unchecked(old * Multiplier + _increment);
            uint xorShifted = unchecked((uint)(((old >> 18) ^ old) >> 27)); // keep the low 32 bits, by design
            int rotation = (int)(old >> 59);
            return (xorShifted >> rotation) | (xorShifted << ((-rotation) & 31));
        }

        /// <summary>Uniform integer in [0, <paramref name="maxExclusive"/>), without modulo bias.</summary>
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Upper bound must be positive.");
            }

            uint bound = (uint)maxExclusive;
            uint threshold = unchecked(0u - bound) % bound;
            while (true)
            {
                uint value = NextUInt();
                if (value >= threshold)
                {
                    return (int)(value % bound);
                }
            }
        }

        /// <summary>Uniform integer in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).</summary>
        public int NextInt(int minInclusive, int maxExclusive) => minInclusive + NextInt(maxExclusive - minInclusive);

        /// <summary>True with probability <paramref name="permille"/> / 1000.</summary>
        public bool RollPermille(int permille) => NextInt(1000) < permille;

        /// <summary>An independent copy that continues with exactly the same sequence.</summary>
        public SimRng Clone() => (SimRng)MemberwiseClone();

        /// <summary>SplitMix64 finaliser: spreads a seed's bits so neighbouring seeds give unrelated streams.</summary>
        public static ulong SplitMix64(ulong x)
        {
            unchecked
            {
                x += Golden;
                x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
                x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
                return x ^ (x >> 31);
            }
        }
    }
}
