using System.Collections.Generic;
using Knez.Sim.Formations;
using Knez.Sim.Units;

namespace Knez.Sim
{
    /// <summary>
    /// Tick stage 12: squad bookkeeping after the tick's deaths (battle-sim.md §9, §10): men of a squad marching off the
    /// field who reach their edge leave it (M4 step 2); the fallen, the fled and the left leave their squad and
    /// the ranks close up from the back (<see cref="SlotAssignment.CloseRanks"/>). Dead fighters stay in the fighter list,
    /// marked dead, for the view. Then, once, the victory check: the battle is decided when at most one side has men in the
    /// fight who aren't routing (<see cref="BattleState.Winner"/>, a <see cref="Events.BattleEnded"/> event).
    /// </summary>
    public sealed class CleanupSystem : ISimSystem
    {
        /// <inheritdoc/>
        public void Execute(BattleState state, TickContext context)
        {
            List<Squad> squads = state.SquadList;
            for (int i = 0; i < squads.Count; i++)
            {
                if (squads[i].Leaving)
                {
                    Leave(state, squads[i]);
                }

                SlotAssignment.CloseRanks(state, squads[i]);
            }

            if (state.Winner == BattleState.Undecided)
            {
                Decide(state);
            }
        }

        /// <summary>A man still in the fight of a squad marching off the field leaves once he is within half a metre of his
        /// side's map edge (body to edge): the march's front rank stands a body's width from it (M4 step 2).</summary>
        private static void Leave(BattleState state, Squad squad)
        {
            List<EntityId> members = squad.MemberList;
            for (int m = 0; m < members.Count; m++)
            {
                Fighter? man = state.FindFighter(members[m]);
                if (man != null && man.IsAlive && !man.IsRouting
                    && (Morale.MoraleRules.RetreatPoint(state, man) - man.Position).Length <= man.Radius + LeaveSlack)
                {
                    Morale.MoraleSystem.LeaveField(state, man);
                }
            }
        }

        // How near his edge (body to edge, metres) a man of a squad leaving the field leaves it.
        private static readonly Numerics.Fix LeaveSlack = Numerics.Fix.Ratio(1, 2);

        /// <summary>
        /// The battle is decided once at most one side has a man in the fight who isn't routing (battle-sim.md §12): that
        /// side holds the field, or nobody (−1). Sides are those the scenario's squads and Retainers belong to.
        /// </summary>
        private static void Decide(BattleState state)
        {
            int standing = -1;
            int sides = 0;
            List<Fighter> fighters = state.FighterList;
            var scenarioSquads = state.Scenario.Squads;
            for (int side = 0; side <= Defs.SquadSpawnDef.MaxSideId; side++)
            {
                bool present = false;
                for (int s = 0; s < scenarioSquads.Count && !present; s++)
                {
                    present = scenarioSquads[s].SideId == side;
                }

                var retainers = state.Scenario.Retainers;
                for (int r = 0; r < retainers.Count && !present; r++)
                {
                    present = retainers[r].SideId == side; // a side of Retainers alone still fights
                }

                if (!present)
                {
                    continue;
                }

                for (int i = 0; i < fighters.Count; i++)
                {
                    Fighter man = fighters[i];
                    if (man.SideId == side && man.IsAlive && !man.IsRouting)
                    {
                        standing = side;
                        sides++;
                        break;
                    }
                }
            }

            if (sides <= 1 && HasTwoSides(state))
            {
                state.Winner = sides == 1 ? standing : -1;
                state.AddEvent(new Events.BattleEnded(state.Tick, state.Winner));
            }
        }

        /// <summary>A battle needs two sides to be decided — among its squads and its Retainers (a side may be Retainers
        /// alone, M3 step 10); a drill with one side never ends.</summary>
        private static bool HasTwoSides(BattleState state)
        {
            int first = -1;
            var squads = state.Scenario.Squads;
            for (int s = 0; s < squads.Count; s++)
            {
                first = first < 0 ? squads[s].SideId : first;
                if (squads[s].SideId != first)
                {
                    return true;
                }
            }

            var retainers = state.Scenario.Retainers;
            for (int r = 0; r < retainers.Count; r++)
            {
                first = first < 0 ? retainers[r].SideId : first;
                if (retainers[r].SideId != first)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
