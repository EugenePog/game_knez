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

        /// <summary>
        /// Living fighters pressed more than <see cref="OverlapTolerance"/> into at least one other living fighter — not
        /// counting allies who are allowed through each other (a man on orders passing a holding allied squad's ranks): the
        /// metric is about bodies that should be apart and aren't. Allocates a little per call: a tool, not for systems.
        /// </summary>
        public static int OverlappingFighters(BattleState state)
        {
            List<Fighter> fighters = state.FighterList;
            var roles = new List<CrowdRole>(fighters.Count);
            for (int i = 0; i < fighters.Count; i++)
            {
                roles.Add(Crowding.Role(state, fighters[i]));
            }

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
                    if (j != i && b.IsAlive && !Crowding.PassThrough(fighters, roles, i, j) && Overlap(a, b))
                    {
                        count++;
                        break;
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// Living fighters passing through allied ranks right now: on orders, passing, and overlapping an allied holder.
        /// Reported next to <see cref="OverlappingFighters"/>, so an allowed overlap is visible and can't hide a failure.
        /// </summary>
        public static int PassingFighters(BattleState state)
        {
            List<Fighter> fighters = state.FighterList;
            var roles = new List<CrowdRole>(fighters.Count);
            for (int i = 0; i < fighters.Count; i++)
            {
                roles.Add(Crowding.Role(state, fighters[i]));
            }

            int count = 0;
            for (int i = 0; i < fighters.Count; i++)
            {
                if (roles[i] != CrowdRole.Ordered || !fighters[i].PassingThrough)
                {
                    continue;
                }

                for (int j = 0; j < fighters.Count; j++)
                {
                    if (j != i && fighters[j].IsAlive && Crowding.PassThrough(fighters, roles, i, j)
                        && (fighters[j].Position - fighters[i].Position).Length < fighters[i].Radius + fighters[j].Radius)
                    {
                        count++;
                        break;
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// True when <paramref name="fighter"/> holds his ground this tick (his squad has no order and he stands within
        /// 1.5 m of his slot): he weighs his formation's hold weight in pushes and opens his ranks to allied squads.
        /// </summary>
        public static bool HoldsGround(BattleState state, Fighter fighter) => Crowding.Holding(state, fighter);

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
