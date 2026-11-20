namespace Knez.Sim.Commands
{
    /// <summary>
    /// External input to the battle (a player order; later also replays and network input). Commands are the only way
    /// the outside world changes <see cref="BattleState"/> (AGENTS.md hard rule 4). Implementations are immutable.
    /// </summary>
    /// <remarks>
    /// At its tick the sim calls <see cref="Validate"/>; only if it returns <see cref="RejectReason.None"/> does it call
    /// <see cref="Apply"/>. Validation gets no <see cref="TickContext"/>, so it cannot draw random numbers or emit
    /// events: a rejected command provably changes nothing. Each command checks the issuing side's ownership itself.
    /// </remarks>
    public interface ICommand
    {
        /// <summary>The side issuing the command.</summary>
        int SideId { get; }

        /// <summary>Checks the command against the current state without changing anything.</summary>
        RejectReason Validate(BattleState state);

        /// <summary>Applies a command that passed <see cref="Validate"/> in this same tick.</summary>
        void Apply(BattleState state, TickContext context);
    }
}
