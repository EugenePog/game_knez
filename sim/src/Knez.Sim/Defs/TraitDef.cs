using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Knez.Sim.Numerics;

namespace Knez.Sim.Defs
{
    /// <summary>
    /// A Retainer's trait (D-025: proud, cautious, greedy, loyal, bold, cruel; <c>content/defs/retainers.json</c>,
    /// battle-sim.md §13) and its active ability (M3 step 11). Immutable, shared.
    /// </summary>
    public sealed class TraitDef : IDefId
    {
        /// <summary>Creates a trait; throws <see cref="ArgumentException"/> on an invalid id.</summary>
        public TraitDef(string id, AbilityDef ability)
        {
            Id = DefChecks.Id(id, nameof(id));
            Ability = ability ?? throw new ArgumentNullException(nameof(ability));
        }

        /// <summary>
        /// The six traits as <c>retainers.json</c> has them (decided by the user 2026-11-26: every cooldown 2 minutes;
        /// battle cry +100 / −50 within 15 m; close ranks −30 ‰ from the front at ×0.7 pace for 10 s; stand to the death
        /// 5 s), for scenarios built in code; a test keeps them equal to the file. Sorted by id.
        /// </summary>
        public static readonly IReadOnlyList<TraitDef> Standard = new ReadOnlyCollection<TraitDef>(new[]
        {
            new TraitDef("bold", AbilityDef.NotBuilt(AbilityKind.FrontRankRush, Fix.FromInt(120))),
            new TraitDef("cautious", AbilityDef.CloseRanks(Fix.FromInt(120), Fix.FromInt(10), 30, DefChecks.Decimal(7, 10))),
            new TraitDef("cruel", AbilityDef.NotBuilt(AbilityKind.NoQuarter, Fix.FromInt(120))),
            new TraitDef("greedy", AbilityDef.NotBuilt(AbilityKind.PromiseLoot, Fix.FromInt(120))),
            new TraitDef("loyal", AbilityDef.StandToTheDeath(Fix.FromInt(120), Fix.FromInt(5))),
            new TraitDef("proud", AbilityDef.BattleCry(Fix.FromInt(120), Fix.FromInt(15), 100, 50)),
        });

        /// <summary>Content id, e.g. "proud".</summary>
        public string Id { get; }

        /// <summary>His active ability.</summary>
        public AbilityDef Ability { get; }

        /// <summary>The standard trait with <paramref name="id"/>; throws when there is none.</summary>
        public static TraitDef StandardOf(string id)
        {
            for (int i = 0; i < Standard.Count; i++)
            {
                if (string.Equals(Standard[i].Id, id, StringComparison.Ordinal))
                {
                    return Standard[i];
                }
            }

            throw new ArgumentException($"no standard trait '{id}'.", nameof(id));
        }

        internal void AddTo(ref Fnv1a64 hash)
        {
            hash.Add(Id);
            Ability.AddTo(ref hash);
        }
    }
}
