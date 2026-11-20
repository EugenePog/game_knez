using Knez.Sim.Events;

namespace Knez.Sim.Commands
{
    /// <summary>Tick stage 1: applies the commands due this tick, in enqueue order (battle-sim.md §8–9).</summary>
    public sealed class CommandSystem : ISimSystem
    {
        /// <inheritdoc/>
        public void Execute(BattleState state, TickContext context)
        {
            var due = context.DueCommands;
            due.Clear();
            context.Commands.TakeDue(state.Tick, due);
            for (int i = 0; i < due.Count; i++)
            {
                ICommand command = due[i].Command;
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

            due.Clear();
        }
    }
}
