using System.Collections.Generic;
using Knez.Sim.Numerics;
using Knez.Sim.Units;

namespace Knez.Sim
{
    /// <summary>
    /// Hashes every field of <see cref="BattleState"/> that affects the future, in a fixed canonical order.
    /// A field missing here is a desync nobody will catch: extend this whenever state grows (sim-feature checklist).
    /// </summary>
    public static class StateHasher
    {
        /// <summary>The state's hash, with section sub-hashes.</summary>
        public static StateHash Compute(BattleState state)
        {
            var core = Fnv1a64.Start();
            core.Add(state.Tick);
            core.Add(state.Seed);
            core.Add(state.NextEntityIdValue);
            core.Add(state.Scenario.Fingerprint); // the immutable content: map, squads, defs
            core.Add(state.Winner);

            var rng = Fnv1a64.Start();
            for (int i = 0; i < RngStreams.Count; i++)
            {
                SimRng stream = state.Rng((RngStream)i);
                rng.Add(stream.State);
                rng.Add(stream.Increment);
            }

            var squads = Fnv1a64.Start();
            List<Squad> squadList = state.SquadList;
            squads.Add(squadList.Count);
            for (int i = 0; i < squadList.Count; i++)
            {
                Squad squad = squadList[i];
                squads.Add(squad.Id.Value);
                squads.Add(squad.SideId);
                squads.Add(squad.Name);
                squads.Add(squad.Formation.Id); // def contents are in the scenario fingerprint
                squads.Add(squad.Anchor.X.Raw);
                squads.Add(squad.Anchor.Y.Raw);
                squads.Add((int)squad.Facing.Raw);
                squads.Add((int)squad.Order.Kind);
                squads.Add(squad.Order.Target.X.Raw);
                squads.Add(squad.Order.Target.Y.Raw);
                squads.Add((int)squad.Order.Facing.Raw);
                squads.Add(squad.Order.FacingFromMarch);
                squads.Add(squad.Velocity.X.Raw);
                squads.Add(squad.Velocity.Y.Raw);
                squads.Add(squad.Spin);
                squads.Add(squad.PathRequestTick);
                squads.Add(squad.PathIndex);
                List<FixVec2> path = squad.PathList;
                squads.Add(path.Count);
                for (int p = 0; p < path.Count; p++)
                {
                    squads.Add(path[p].X.Raw);
                    squads.Add(path[p].Y.Raw);
                    squads.Add(squad.PathFilesList[p]);
                }

                squads.Add(squad.Files);
                squads.Add(squad.Frontage);
                squads.Add(squad.Reforming);
                squads.Add(squad.Withdrawing);
                squads.Add(squad.StartCount);
                squads.Add(squad.Broken);
                squads.Add(squad.RegroupSince);
                squads.Add((int)squad.Stance); // M4 step 1
                squads.Add(squad.AttackTarget.Value);
                squads.Add(squad.Pursuing);
                squads.Add(squad.PursuitUntil);
                squads.Add(squad.PursuitFrom.X.Raw);
                squads.Add(squad.PursuitFrom.Y.Raw);
                squads.Add((int)squad.AiState); // M4 step 2
                squads.Add(squad.AiSince);
                squads.Add(squad.AiTarget.Value);
                squads.Add(squad.Leaving);
                List<AbilityEffect> effects = squad.EffectList; // M3 step 11; the numbers are in the fingerprint
                squads.Add(effects.Count);
                for (int e = 0; e < effects.Count; e++)
                {
                    squads.Add((int)effects[e].Ability.Kind);
                    squads.Add(effects[e].Until);
                    squads.Add(effects[e].By.Value);
                }

                List<EntityId> members = squad.MemberList;
                squads.Add(members.Count);
                for (int m = 0; m < members.Count; m++)
                {
                    squads.Add(members[m].Value);
                }
            }

            var fighters = Fnv1a64.Start();
            List<Fighter> list = state.FighterList;
            fighters.Add(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                Fighter fighter = list[i];
                fighters.Add(fighter.Id.Value);
                fighters.Add(fighter.Body.Id);
                fighters.Add(fighter.Experience.Id);
                fighters.Add(fighter.Kit.Id);
                fighters.Add(fighter.IsRetainer);
                if (fighter.Retainer.HasValue)
                {
                    fighters.Add(fighter.Retainer.Value.Name);
                    fighters.Add(fighter.Retainer.Value.Trait.Id);
                    fighters.Add(fighter.Retainer.Value.AbilityReadyTick);
                }

                Orders.LoneOrder order = fighter.LoneOrder;
                fighters.Add((int)order.Kind);
                fighters.Add(order.Target.X.Raw);
                fighters.Add(order.Target.Y.Raw);
                fighters.Add(order.HasFacing);
                fighters.Add((int)order.Facing.Raw);
                fighters.Add(order.Enemy.Value);
                fighters.Add(fighter.SideId);
                fighters.Add(fighter.IsAlive);
                fighters.Add(fighter.Position.X.Raw);
                fighters.Add(fighter.Position.Y.Raw);
                fighters.Add(fighter.Velocity.X.Raw);
                fighters.Add(fighter.Velocity.Y.Raw);
                fighters.Add((int)fighter.Facing.Raw);
                fighters.Add(fighter.SquadId.Value);
                fighters.Add(fighter.SlotIndex);
                fighters.Add(fighter.PassingThrough);
                fighters.Add(fighter.Target.Value);
                fighters.Add((int)fighter.Action);
                fighters.Add(fighter.ActionTicks);
                fighters.Add((int)fighter.Wound);
                fighters.Add(fighter.Morale);
                fighters.Add((int)fighter.MoraleState);
                fighters.Add(fighter.TurnLag.X.Raw);
                fighters.Add(fighter.TurnLag.Y.Raw);
            }

            return new StateHash(core.Value, rng.Value, squads.Value, fighters.Value);
        }
    }
}
