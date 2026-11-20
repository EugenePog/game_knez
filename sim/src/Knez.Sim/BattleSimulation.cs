using System;
using System.Collections.Generic;
using Knez.Sim.Ai;
using Knez.Sim.Combat;
using Knez.Sim.Commands;
using Knez.Sim.Morale;
using Knez.Sim.Movement;
using Knez.Sim.Orders;

namespace Knez.Sim
{
    /// <summary>
    /// Runs a battle one fixed tick at a time (ADR-0003). The view owns real time and calls <see cref="Step"/> 0..n
    /// times per frame; pausing means not calling it. Input arrives only through <see cref="Enqueue"/>.
    /// </summary>
    public sealed class BattleSimulation
    {
        private readonly ISimSystem[] _systems;
        private readonly TickContext _context;

        /// <summary>A simulation of <paramref name="state"/> with the standard tick pipeline and an empty queue.</summary>
        public BattleSimulation(BattleState state)
            : this(state, new CommandQueue(), CreateDefaultSystems())
        {
        }

        /// <summary>A simulation with an explicit queue and system list (tests and tools).</summary>
        public BattleSimulation(BattleState state, CommandQueue commands, IReadOnlyList<ISimSystem> systems)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            Commands = commands ?? throw new ArgumentNullException(nameof(commands));
            if (systems == null)
            {
                throw new ArgumentNullException(nameof(systems));
            }

            _systems = new ISimSystem[systems.Count];
            for (int i = 0; i < systems.Count; i++)
            {
                _systems[i] = systems[i];
            }

            _context = new TickContext(state, commands);
        }

        /// <summary>The battle state. The view reads it; only the systems write it.</summary>
        public BattleState State { get; }

        /// <summary>Pending commands and the command log.</summary>
        public CommandQueue Commands { get; }

        /// <summary>The systems in the order they run each tick.</summary>
        public IReadOnlyList<ISimSystem> Systems => _systems;

        /// <summary>
        /// True after a command or system threw during a tick. The state is then half-updated and no longer matches
        /// any replay, so <see cref="Step"/> refuses to continue; restore a snapshot or restart the battle.
        /// </summary>
        public bool IsFaulted { get; private set; }

        /// <summary>
        /// The standard tick pipeline in battle-sim.md §9 order. Stage 0 (begin tick) is <see cref="Step"/> itself;
        /// hashing (stage 13) is <see cref="ComputeHash"/>, called by tests and tools.
        /// </summary>
        public static ISimSystem[] CreateDefaultSystems() => new ISimSystem[]
        {
            new CommandSystem(),
            new AiSystem(),
            new OrderSystem(),
            new PathSystem(),
            new SteeringSystem(),
            new MovementSystem(),
            new TargetingSystem(),
            new CombatSystem(),
            new DamageSystem(),
            new MoraleSystem(),
            new FatigueSystem(),
            new CleanupSystem(),
        };

        /// <summary>Restores a snapshot with the standard tick pipeline.</summary>
        public static BattleSimulation Restore(SimSnapshot snapshot) => Restore(snapshot, CreateDefaultSystems());

        /// <summary>Restores a snapshot with an explicit system list.</summary>
        public static BattleSimulation Restore(SimSnapshot snapshot, IReadOnlyList<ISimSystem> systems) =>
            new BattleSimulation(snapshot.State.Clone(), snapshot.Commands.Clone(), systems);

        /// <summary>Schedules <paramref name="command"/> for the next tick (state tick T → applied at the start of T+1).</summary>
        public void Enqueue(ICommand command) => Commands.Add(State.Tick + 1, command);

        /// <summary>Schedules <paramref name="command"/> for a future tick — used to replay a command log.</summary>
        public void EnqueueAt(int tick, ICommand command)
        {
            if (tick <= State.Tick)
            {
                throw new ArgumentOutOfRangeException(nameof(tick), "Commands can only be scheduled for future ticks.");
            }

            Commands.Add(tick, command);
        }

        /// <summary>
        /// Advances the battle by exactly one tick. <see cref="BattleState.Events"/> then holds this tick's events only:
        /// a view stepping several times per frame reads them after every call.
        /// </summary>
        public void Step()
        {
            if (IsFaulted)
            {
                throw new InvalidOperationException(
                    "The simulation faulted in an earlier tick and its state can't be trusted; restore a snapshot or restart.");
            }

            try
            {
                State.BeginTick();
                for (int i = 0; i < _systems.Length; i++)
                {
                    _systems[i].Execute(State, _context);
                }
            }
            catch
            {
                IsFaulted = true;
                throw;
            }
        }

        /// <summary>Advances the battle by <paramref name="ticks"/> ticks.</summary>
        public void StepTicks(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                Step();
            }
        }

        /// <summary>Hash of the current state.</summary>
        public StateHash ComputeHash() => StateHasher.Compute(State);

        /// <summary>A copy of state and pending commands that <see cref="Restore(SimSnapshot)"/> can continue from.</summary>
        public SimSnapshot TakeSnapshot() => new SimSnapshot(State.Clone(), Commands.Clone());
    }
}
