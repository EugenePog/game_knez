using System;
using System.Globalization;

namespace Knez.Sim
{
    /// <summary>
    /// Hash of a <see cref="BattleState"/> with per-section sub-hashes, so a desync can be traced to the section that
    /// diverged first (battle-sim.md §15). Add a section when a new part of the state appears.
    /// </summary>
    public readonly struct StateHash : IEquatable<StateHash>
    {
        /// <summary>Combines the section hashes into <see cref="Total"/>. Internal so new sections aren't an API change.</summary>
        internal StateHash(ulong core, ulong rng, ulong squads, ulong fighters)
        {
            Core = core;
            Rng = rng;
            Squads = squads;
            Fighters = fighters;
            var total = Fnv1a64.Start();
            total.Add(core);
            total.Add(rng);
            total.Add(squads);
            total.Add(fighters);
            Total = total.Value;
        }

        /// <summary>Hash of everything; compare this first.</summary>
        public ulong Total { get; }

        /// <summary>Tick, seed, id counter, scenario fingerprint.</summary>
        public ulong Core { get; }

        /// <summary>All random stream states.</summary>
        public ulong Rng { get; }

        /// <summary>All squads in id order.</summary>
        public ulong Squads { get; }

        /// <summary>All fighters in id order.</summary>
        public ulong Fighters { get; }

        /// <summary>Equality of all sections.</summary>
        public static bool operator ==(StateHash a, StateHash b) => a.Equals(b);

        /// <summary>Inequality.</summary>
        public static bool operator !=(StateHash a, StateHash b) => !a.Equals(b);

        /// <inheritdoc/>
        public bool Equals(StateHash other) =>
            Total == other.Total && Core == other.Core && Rng == other.Rng && Squads == other.Squads && Fighters == other.Fighters;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is StateHash other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => unchecked((int)Total ^ (int)(Total >> 32));

        /// <summary>Total as 16 hex digits, followed by the sections.</summary>
        public override string ToString() =>
            Hex(Total) + " (core " + Hex(Core) + ", rng " + Hex(Rng) + ", squads " + Hex(Squads) + ", fighters " + Hex(Fighters) + ")";

        private static string Hex(ulong value) => value.ToString("x16", CultureInfo.InvariantCulture);
    }
}
