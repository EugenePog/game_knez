using System.Collections.Generic;
using Knez.Sim.Events;
using Knez.Sim.Units;

namespace Knez.Sim.Combat
{
    /// <summary>
    /// Tick stage 8: blows (battle-sim.md §11, M3 step 3). In id order each living fighter counts down his action: when a
    /// windup runs out he strikes his <see cref="Fighter.Target"/> — if he still can (<see cref="Blows.CanStrike"/>),
    /// the chance (<see cref="Blows.Chance"/>) is rolled on the <see cref="RngStream.Combat"/> stream and a
    /// <see cref="BlowStruck"/> event says how it went; if not, the blow is lost — then he recovers. Idle (possibly just
    /// now), he starts a windup as soon as he can strike his target. Turning toward the target is movement's (stage 6);
    /// blows that got through wound in stage 9.
    /// </summary>
    public sealed class CombatSystem : ISimSystem
    {
        /// <inheritdoc/>
        public void Execute(BattleState state, TickContext context)
        {
            List<Fighter> fighters = state.FighterList;
            Defs.CombatDef rules = state.Scenario.Combat;
            context.Hits.Clear();
            for (int i = 0; i < fighters.Count; i++)
            {
                Fighter fighter = fighters[i];
                if (!fighter.IsAlive || fighter.IsRouting)
                {
                    fighter.Action = FighterAction.Idle; // the fallen and the running strike no one
                    fighter.ActionTicks = 0;
                    continue;
                }

                Fighter? target = fighter.IsEngaged ? state.FindFighter(fighter.Target) : null;
                if (fighter.Action == FighterAction.Windup)
                {
                    fighter.ActionTicks--;
                    if (fighter.ActionTicks <= 0)
                    {
                        if (target != null && Blows.CanStrike(rules, fighter, target))
                        {
                            int chance = Blows.Chance(state, fighter, target, out BlowSector sector);
                            bool landed = context.Rng(RngStream.Combat).RollPermille(chance);
                            state.AddEvent(new BlowStruck(state.Tick, fighter.Id, target.Id, sector, chance, landed));
                            if (landed)
                            {
                                context.Hits.Add(new PendingHit(fighter.Id, target.Id)); // wounds after every blow is struck
                            }
                        }

                        fighter.Action = FighterAction.Recovery;
                        fighter.ActionTicks = fighter.Kit.Weapon.RecoveryTicks;
                    }

                    continue;
                }

                if (fighter.Action == FighterAction.Recovery)
                {
                    fighter.ActionTicks--;
                    if (fighter.ActionTicks > 0)
                    {
                        continue;
                    }

                    fighter.Action = FighterAction.Idle;
                }

                if (target != null && Blows.CanStrike(rules, fighter, target))
                {
                    fighter.Action = FighterAction.Windup;
                    fighter.ActionTicks = fighter.Kit.Weapon.WindupTicks;
                }
            }
        }
    }
}
