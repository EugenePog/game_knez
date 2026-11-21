using Knez.Sim.Defs;
using Knez.Sim.Formations;
using Knez.Sim.Numerics;
using Knez.Sim.Units;

namespace Knez.Sim
{
    /// <summary>Builds the starting <see cref="BattleState"/> of a scenario.</summary>
    public static class BattleSetup
    {
        /// <summary>
        /// The state at tick 0: squads in scenario order, each followed by its fighters in slot order (so ids follow the
        /// scenario file), every fighter standing on its formation slot and facing with its squad.
        /// </summary>
        public static BattleState Create(ScenarioDef scenario, ulong seed)
        {
            var state = new BattleState(scenario, seed);
            for (int i = 0; i < scenario.Squads.Count; i++)
            {
                SquadSpawnDef spawn = scenario.Squads[i];
                Squad squad = state.AddSquad(spawn.SideId, spawn.Name, spawn.Formation, spawn.Position, spawn.Facing);
                squad.Frontage = FormationLayout.Width(spawn.Formation, spawn.Count);
                for (int slot = 0; slot < spawn.Count; slot++)
                {
                    FixVec2 position = FormationLayout.SlotPosition(spawn.Formation, spawn.Count, slot, spawn.Position, spawn.Facing);
                    state.AddSquadMember(squad, spawn.Fighter, position, slot);
                }
            }

            return state;
        }
    }
}
