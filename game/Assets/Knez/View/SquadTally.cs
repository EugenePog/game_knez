#nullable enable
using System.Collections.Generic;
using Knez.Sim;
using Knez.Sim.Combat;
using Knez.Sim.Events;
using Knez.Sim.Morale;
using Knez.Sim.Units;

namespace Knez.Game.View
{
    /// <summary>
    /// View-side bookkeeping for the HUD (M3): which squad each man started in (the fallen and the fled leave their squad
    /// in the sim), blows struck per squad, and each man's last blow. Plain C#, keyed by ids; never touches the state.
    /// </summary>
    public sealed class SquadTally
    {
        private readonly Dictionary<int, int> _home = new Dictionary<int, int>();
        private readonly Dictionary<int, (int Blows, int Landed)> _blows = new Dictionary<int, (int, int)>();
        private readonly Dictionary<int, BlowStruck> _lastBy = new Dictionary<int, BlowStruck>();
        private readonly Dictionary<int, BlowStruck> _lastAt = new Dictionary<int, BlowStruck>();

        /// <summary>Records every man's squad; call when the battle starts, before men fall.</summary>
        public void Begin(BattleState state)
        {
            _home.Clear();
            _blows.Clear();
            _lastBy.Clear();
            _lastAt.Clear();
            foreach (Fighter fighter in state.Fighters)
            {
                if (!fighter.SquadId.IsNone)
                {
                    _home[fighter.Id.Value] = fighter.SquadId.Value;
                }
            }
        }

        /// <summary>Counts the tick's blows; call after every tick.</summary>
        public void Observe(BattleState state)
        {
            foreach (SimEvent simEvent in state.Events)
            {
                if (simEvent is BlowStruck blow)
                {
                    _lastBy[blow.Attacker.Value] = blow;
                    _lastAt[blow.Defender.Value] = blow;
                    if (_home.TryGetValue(blow.Attacker.Value, out int squad))
                    {
                        _blows.TryGetValue(squad, out var count);
                        _blows[squad] = (count.Blows + 1, count.Landed + (blow.Landed ? 1 : 0));
                    }
                }
            }
        }

        /// <summary>The squad <paramref name="fighter"/> started in, or none.</summary>
        public EntityId HomeOf(Fighter fighter) =>
            _home.TryGetValue(fighter.Id.Value, out int squad) ? new EntityId(squad) : fighter.SquadId;

        /// <summary>The squad the fighter <paramref name="fighterId"/> started in, or none.</summary>
        public EntityId HomeOf(EntityId fighterId) =>
            _home.TryGetValue(fighterId.Value, out int squad) ? new EntityId(squad) : EntityId.None;

        /// <summary>
        /// Who a squad started with (M3 step 8): its men grouped by experience and kit (M3 step 9: one squad may mix them),
        /// in the order they first appear, with how many of each are still in the fight.
        /// </summary>
        public IReadOnlyList<(Fighter Sample, int Count, int InFight)> Composition(BattleState state, Squad squad)
        {
            var groups = new List<(Fighter Sample, int Count, int InFight)>();
            foreach (Fighter fighter in state.Fighters)
            {
                if (HomeOf(fighter) != squad.Id)
                {
                    continue;
                }

                int at = groups.FindIndex(g => g.Sample.Kit == fighter.Kit && g.Sample.Experience == fighter.Experience);
                int inFight = fighter.IsAlive ? 1 : 0;
                if (at < 0)
                {
                    groups.Add((fighter, 1, inFight));
                }
                else
                {
                    groups[at] = (groups[at].Sample, groups[at].Count + 1, groups[at].InFight + inFight);
                }
            }

            return groups;
        }

        /// <summary>The last blow <paramref name="fighter"/> struck, or null.</summary>
        public BlowStruck? LastBlowBy(Fighter fighter) => _lastBy.TryGetValue(fighter.Id.Value, out BlowStruck? b) ? b : null;

        /// <summary>The last blow struck at <paramref name="fighter"/>, or null.</summary>
        public BlowStruck? LastBlowAt(Fighter fighter) => _lastAt.TryGetValue(fighter.Id.Value, out BlowStruck? b) ? b : null;

        /// <summary>The squad's numbers now.</summary>
        public SquadStats Of(BattleState state, Squad squad)
        {
            var stats = new SquadStats();
            long morale = 0;
            foreach (Fighter fighter in state.Fighters)
            {
                if (HomeOf(fighter) != squad.Id)
                {
                    continue;
                }

                if (fighter.Wound == WoundLevel.Down)
                {
                    stats.Fallen++;
                }
                else if (fighter.MoraleState == MoraleState.Fled)
                {
                    stats.Fled++;
                }
                else if (fighter.IsAlive)
                {
                    stats.InFight++;
                    stats.Running += fighter.IsRouting ? 1 : 0;
                    stats.Light += fighter.Wound == WoundLevel.Light ? 1 : 0;
                    stats.Heavy += fighter.Wound == WoundLevel.Heavy ? 1 : 0;
                    morale += fighter.Morale;
                }
            }

            stats.Morale = stats.InFight > 0 ? (int)(morale / stats.InFight) : 0;
            if (_blows.TryGetValue(squad.Id.Value, out var blows))
            {
                stats.Blows = blows.Blows;
                stats.Landed = blows.Landed;
            }

            return stats;
        }
    }
}
