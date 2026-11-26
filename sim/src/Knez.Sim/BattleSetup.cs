using Knez.Sim.Defs;
using Knez.Sim.Formations;
using Knez.Sim.Numerics;
using Knez.Sim.Units;

namespace Knez.Sim
{
    /// <summary>Builds the starting <see cref="BattleState"/> of a scenario.</summary>
    public static class BattleSetup
    {
        private static Squad FindSquad(BattleState state, string name)
        {
            var squads = state.SquadList;
            for (int i = 0; i < squads.Count; i++)
            {
                if (string.Equals(squads[i].Name, name, System.StringComparison.Ordinal))
                {
                    return squads[i];
                }
            }

            throw new System.InvalidOperationException($"No squad '{name}' (ScenarioDef checks Retainers' squads).");
        }

        /// <summary>
        /// The state at tick 0: squads in scenario order, each followed by its fighters in slot order (so ids follow the
        /// scenario file), every fighter standing on its formation slot and facing with its squad. Then the Retainers, in
        /// list order (battle-sim.md §13): one who starts in a squad joins its front rank and the squad stands formed in the
        /// layout for one more man; one who starts alone stands where the scenario puts him.
        /// </summary>
        public static BattleState Create(ScenarioDef scenario, ulong seed)
        {
            var state = new BattleState(scenario, seed);
            for (int i = 0; i < scenario.Squads.Count; i++)
            {
                SquadSpawnDef spawn = scenario.Squads[i];
                Squad squad = state.AddSquad(spawn.SideId, spawn.Name, spawn.Formation, spawn.Position, spawn.Facing);
                squad.Frontage = FormationLayout.Width(spawn.Formation, spawn.Count);
                squad.Stance = spawn.Stance;
                for (int slot = 0; slot < spawn.Count; slot++)
                {
                    FixVec2 position = FormationLayout.SlotPosition(spawn.Formation, spawn.Count, slot, spawn.Position, spawn.Facing);
                    state.AddSquadMember(squad, spawn.Body, spawn.GroupAt(slot), position, slot); // groups fill slots in order
                }
            }

            for (int i = 0; i < scenario.Retainers.Count; i++)
            {
                RetainerSpawnDef spawn = scenario.Retainers[i];
                Fighter retainer = state.AddFighter(spawn.Body, spawn.Experience, spawn.Kit, spawn.SideId, spawn.Position, spawn.Facing);
                retainer.Retainer = new RetainerPart(spawn.Name, spawn.Trait);
                if (spawn.SquadName != null)
                {
                    SquadMembership.Join(state, FindSquad(state, spawn.SquadName), retainer, atSpawn: true);
                }
            }

            return state;
        }
    }
}
