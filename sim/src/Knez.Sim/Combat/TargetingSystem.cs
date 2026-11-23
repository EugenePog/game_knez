using System.Collections.Generic;
using Knez.Sim.Numerics;
using Knez.Sim.Units;

namespace Knez.Sim.Combat
{
    /// <summary>
    /// Tick stage 7: engagement (battle-sim.md §11, M3 step 2). After movement every living fighter takes as
    /// <see cref="Fighter.Target"/> the nearest living enemy whose body is within his weapon's reach of his own (edge to
    /// edge), ties broken by the lower id; with none in reach he has no target. Any direction counts: where a blow can
    /// come from is the attack's business (step 3). All pairs, no spatial index: phase 1 has at most ~60 fighters.
    /// </summary>
    public sealed class TargetingSystem : ISimSystem
    {
        /// <inheritdoc/>
        public void Execute(BattleState state, TickContext context)
        {
            List<Fighter> fighters = state.FighterList;
            for (int i = 0; i < fighters.Count; i++)
            {
                Fighter self = fighters[i];
                if (!self.IsAlive)
                {
                    self.Target = EntityId.None;
                    continue;
                }

                Fix reach = self.Def.Kit.Weapon.Reach;
                EntityId best = EntityId.None;
                Fix bestGap = Fix.Zero;
                for (int j = 0; j < fighters.Count; j++)
                {
                    Fighter other = fighters[j];
                    if (!other.IsAlive || other.SideId == self.SideId)
                    {
                        continue;
                    }

                    Fix bodies = self.Radius + other.Radius;
                    Fix limit = bodies + reach;
                    FixVec2 to = other.Position - self.Position;
                    if (FixMath.Abs(to.X) > limit || FixMath.Abs(to.Y) > limit)
                    {
                        continue;
                    }

                    Fix gap = to.Length - bodies;
                    if (gap > reach)
                    {
                        continue;
                    }

                    // The list is in id order, so a strictly nearer man replaces the best and a tie keeps the lower id.
                    if (best.IsNone || gap < bestGap)
                    {
                        best = other.Id;
                        bestGap = gap;
                    }
                }

                self.Target = best;
            }
        }
    }
}
