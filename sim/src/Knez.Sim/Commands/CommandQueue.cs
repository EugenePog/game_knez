using System;
using System.Collections.Generic;

namespace Knez.Sim.Commands
{
    /// <summary>
    /// Pending commands ordered by (tick, enqueue sequence), plus the log of every command taken for execution.
    /// The log together with the scenario and seed is the replay (ADR-0003). Use
    /// <see cref="BattleSimulation.Enqueue"/> to add commands.
    /// </summary>
    public sealed class CommandQueue
    {
        private readonly List<ScheduledCommand> _pending;
        private readonly List<ScheduledCommand> _log;
        private long _nextSequence;

        /// <summary>An empty queue.</summary>
        public CommandQueue()
        {
            _pending = new List<ScheduledCommand>();
            _log = new List<ScheduledCommand>();
        }

        private CommandQueue(CommandQueue source)
        {
            _pending = new List<ScheduledCommand>(source._pending);
            _log = new List<ScheduledCommand>(source._log);
            _nextSequence = source._nextSequence;
        }

        /// <summary>Commands not yet applied, in application order.</summary>
        public IReadOnlyList<ScheduledCommand> Pending => _pending;

        /// <summary>Every command taken for execution so far (applied or rejected), in application order.</summary>
        public IReadOnlyList<ScheduledCommand> Log => _log;

        /// <summary>A copy with the same pending commands, log and sequence counter. Commands are immutable and shared.</summary>
        public CommandQueue Clone() => new CommandQueue(this);

        internal void Add(int tick, ICommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            var scheduled = new ScheduledCommand(tick, _nextSequence++, command);
            int index = _pending.Count;
            while (index > 0 && _pending[index - 1].Tick > tick)
            {
                index--;
            }

            _pending.Insert(index, scheduled);
        }

        /// <summary>Moves every command due at <paramref name="tick"/> into <paramref name="into"/> and the log.</summary>
        internal void TakeDue(int tick, List<ScheduledCommand> into)
        {
            int count = 0;
            while (count < _pending.Count && _pending[count].Tick <= tick)
            {
                if (_pending[count].Tick < tick)
                {
                    throw new InvalidOperationException("A command was scheduled for a tick that has already passed.");
                }

                into.Add(_pending[count]);
                _log.Add(_pending[count]);
                count++;
            }

            _pending.RemoveRange(0, count);
        }
    }
}
