using System.Collections.Generic;
using Knez.Sim.Events;
using Knez.Sim.Units;

namespace Knez.Sim.Combat
{
    /// <summary>
    /// Tick stage 9: wounds (battle-sim.md §11, M3 step 4). Each blow that got through this tick (in the order they were
    /// struck) rolls how it lands from the attacker's weapon row on the <see cref="RngStream.Combat"/> stream and worsens
    /// the defender's wound (<see cref="Wounds.Worsen"/>); a <see cref="Wounded"/> event says so. A man who goes down is
    /// out of the fight at once: dead for the sim, his blow and target dropped; blows at him later in the same list roll
    /// nothing. Then anyone targeting the fallen re-picks the nearest enemy in reach (as targeting would), so the next
    /// tick's orders and steering never see a lock with a dead man, nor a lock that opens for a tick. Cleanup (stage 12) takes him out of his squad.
    /// </summary>
    public sealed class DamageSystem : ISimSystem
    {
        /// <inheritdoc/>
        public void Execute(BattleState state, TickContext context)
        {
            List<PendingHit> hits = context.Hits;
            if (hits.Count == 0)
            {
                return;
            }

            bool someoneFell = false;
            for (int i = 0; i < hits.Count; i++)
            {
                Fighter? defender = state.FindFighter(hits[i].Defender);
                Fighter? attacker = state.FindFighter(hits[i].Attacker);
                if (defender == null || attacker == null || !defender.IsAlive)
                {
                    continue;
                }

                int roll = context.Rng(RngStream.Combat).NextInt(Defs.DefChecks.Whole);
                WoundLevel blow = Wounds.Severity(attacker.Kit.Weapon, roll);
                WoundLevel now = Wounds.Worsen(defender.Wound, blow);
                defender.Wound = now;
                state.AddEvent(new Wounded(state.Tick, defender.Id, attacker.Id, blow, now));
                if (now == WoundLevel.Down)
                {
                    defender.IsAlive = false;
                    defender.Target = EntityId.None;
                    defender.Action = FighterAction.Idle;
                    defender.ActionTicks = 0;
                    someoneFell = true;
                }
            }

            hits.Clear();
            if (!someoneFell)
            {
                return;
            }

            List<Fighter> fighters = state.FighterList;
            for (int i = 0; i < fighters.Count; i++)
            {
                Fighter fighter = fighters[i];
                if (fighter.IsEngaged)
                {
                    Fighter? target = state.FindFighter(fighter.Target);
                    if (target == null || !target.IsAlive)
                    {
                        // Positions don't change before the next targeting, so this is the pick it would make: a man whose
                        // enemy fell turns to the next one in reach at once, and his squad's lock never blinks open.
                        fighter.Target = fighter.IsAlive && !fighter.IsRouting ? TargetingSystem.Nearest(fighters, fighter) : EntityId.None;
                    }
                }
            }
        }
    }
}
