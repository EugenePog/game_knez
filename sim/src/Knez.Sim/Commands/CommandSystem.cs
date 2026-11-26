using System;
using System.Collections.Generic;
using Knez.Sim.Defs;
using Knez.Sim.Events;
using Knez.Sim.Units;

namespace Knez.Sim.Commands
{
    /// <summary>
    /// Tick stage 1 (battle-sim.md §8–9): first the scenario script's orders for this tick (in script order), then the
    /// commands due this tick (in enqueue order). Both are validated the same way; an invalid one emits
    /// <see cref="CommandRejected"/> and changes nothing. Scripted orders are not logged: the scenario holds them.
    /// </summary>
    public sealed class CommandSystem : ISimSystem
    {
        /// <inheritdoc/>
        public void Execute(BattleState state, TickContext context)
        {
            RunScript(state, context);

            var due = context.DueCommands;
            due.Clear();
            context.Commands.TakeDue(state.Tick, due);
            for (int i = 0; i < due.Count; i++)
            {
                Run(due[i].Command, state, context);
            }

            due.Clear();
        }

        private static void RunScript(BattleState state, TickContext context)
        {
            IReadOnlyList<ScriptedOrderDef> script = state.Scenario.Script;
            for (int i = FirstAtOrAfter(script, state.Tick); i < script.Count && script[i].Tick == state.Tick; i++)
            {
                Run(CommandOf(state, script[i]), state, context);
            }
        }

        /// <summary>The command a script entry stands for, issued by the side of the squad or Retainer it names.</summary>
        private static ICommand CommandOf(BattleState state, ScriptedOrderDef entry)
        {
            switch (entry.Kind)
            {
                case ScriptedOrderKind.Formation:
                {
                    Squad squad = FindByName(state.SquadList, entry.SquadName!);
                    return new SetFormationCommand(squad.SideId, squad.Id, entry.FormationId!);
                }

                case ScriptedOrderKind.AttachRetainer:
                {
                    Fighter retainer = FindRetainer(state, entry.RetainerName!);
                    return new AttachRetainerCommand(retainer.SideId, retainer.Id, FindByName(state.SquadList, entry.SquadName!).Id);
                }

                case ScriptedOrderKind.DetachRetainer:
                {
                    Fighter retainer = FindRetainer(state, entry.RetainerName!);
                    return new DetachRetainerCommand(retainer.SideId, retainer.Id);
                }

                case ScriptedOrderKind.MoveRetainer:
                {
                    Fighter retainer = FindRetainer(state, entry.RetainerName!);
                    return new MoveRetainerCommand(retainer.SideId, retainer.Id, entry.Target, entry.Facing);
                }

                case ScriptedOrderKind.RetainerAttack:
                {
                    Fighter retainer = FindRetainer(state, entry.RetainerName!);
                    return new RetainerAttackCommand(retainer.SideId, retainer.Id, entry.Enemy);
                }

                case ScriptedOrderKind.UseAbility:
                {
                    Fighter retainer = FindRetainer(state, entry.RetainerName!);
                    return new UseAbilityCommand(retainer.SideId, retainer.Id);
                }

                default:
                {
                    Squad squad = FindByName(state.SquadList, entry.SquadName!);
                    return new MoveSquadCommand(squad.SideId, squad.Id, entry.Target, entry.Facing);
                }
            }
        }

        private static Fighter FindRetainer(BattleState state, string name)
        {
            List<Fighter> fighters = state.FighterList;
            for (int i = 0; i < fighters.Count; i++)
            {
                RetainerPart? part = fighters[i].Retainer;
                if (part.HasValue && string.Equals(part.Value.Name, name, StringComparison.Ordinal))
                {
                    return fighters[i];
                }
            }

            // ScenarioDef checks every scripted Retainer name, and fighters are never removed.
            throw new InvalidOperationException($"The script names Retainer '{name}', which this battle doesn't have.");
        }

        private static void Run(ICommand command, BattleState state, TickContext context)
        {
            RejectReason reason = command.Validate(state);
            if (reason.IsRejected)
            {
                context.Emit(new CommandRejected(state.Tick, command, reason));
            }
            else
            {
                command.Apply(state, context);
            }
        }

        /// <summary>Index of the first entry with a tick at or after <paramref name="tick"/> (the script is sorted).</summary>
        private static int FirstAtOrAfter(IReadOnlyList<ScriptedOrderDef> script, int tick)
        {
            int lo = 0;
            int hi = script.Count;
            while (lo < hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                if (script[mid].Tick < tick)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            return lo;
        }

        private static Squad FindByName(List<Squad> squads, string name)
        {
            for (int i = 0; i < squads.Count; i++)
            {
                if (string.Equals(squads[i].Name, name, StringComparison.Ordinal))
                {
                    return squads[i];
                }
            }

            // ScenarioDef checks every scripted squad name, and squads are never removed.
            throw new InvalidOperationException($"The script names squad '{name}', which this battle doesn't have.");
        }
    }
}
