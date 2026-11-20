using System.Collections.Generic;
using Knez.Sim.Numerics;
using Knez.Sim.Units;

namespace Knez.Sim.Movement
{
    /// <summary>Measurements for the M1 movement criteria (battle-sim.md §10). Read-only; tools and tests call them.</summary>
    public static class MovementMetrics
    {
        /// <summary>Two bodies "overlap" for the metric when closer than the sum of their radii minus this: 5 cm.</summary>
        public static readonly Fix OverlapTolerance = Fix.FromRaw(Fix.OneRaw / 20);

        /// <summary>Living fighters pressed more than <see cref="OverlapTolerance"/> into at least one other living fighter.</summary>
        public static int OverlappingFighters(BattleState state)
        {
            List<Fighter> fighters = state.FighterList;
            int count = 0;
            for (int i = 0; i < fighters.Count; i++)
            {
                Fighter a = fighters[i];
                if (!a.IsAlive)
                {
                    continue;
                }

                for (int j = 0; j < fighters.Count; j++)
                {
                    Fighter b = fighters[j];
                    if (j != i && b.IsAlive && Overlap(a, b))
                    {
                        count++;
                        break;
                    }
                }
            }

            return count;
        }

        private static bool Overlap(Fighter a, Fighter b)
        {
            Fix limit = a.Radius + b.Radius - OverlapTolerance;
            FixVec2 d = b.Position - a.Position;
            if (FixMath.Abs(d.X) >= limit || FixMath.Abs(d.Y) >= limit)
            {
                return false;
            }

            return d.Length < limit;
        }
    }
}
