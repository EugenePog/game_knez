using System;
using Knez.Sim.Numerics;

namespace Knez.Sim.Defs
{
    /// <summary>
    /// Immutable fighter type from content (battle-sim.md §7). Shared by every fighter of the type and by every clone
    /// of the state; never copied, never written. M1 step 1 carries only the body size; speed, stats and equipment
    /// arrive with the systems that use them.
    /// </summary>
    public sealed class FighterDef
    {
        /// <summary>Creates a def; throws <see cref="ArgumentException"/> on invalid values.</summary>
        public FighterDef(string id, Fix radius)
        {
            Id = DefChecks.Id(id, nameof(id));
            Radius = DefChecks.PositiveMetres(radius, MaxRadiusMetres, nameof(radius));
        }

        /// <summary>Sanity bound for <see cref="Radius"/>, not a balance number.</summary>
        public const int MaxRadiusMetres = 2;

        /// <summary>Content id, e.g. "warrior".</summary>
        public string Id { get; }

        /// <summary>Collision circle radius in metres.</summary>
        public Fix Radius { get; }

        internal void AddTo(ref Fnv1a64 hash)
        {
            hash.Add(Id);
            hash.Add(Radius.Raw);
        }
    }
}
