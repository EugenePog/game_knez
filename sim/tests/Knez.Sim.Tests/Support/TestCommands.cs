using Knez.Sim.Commands;
using Knez.Sim.Numerics;

namespace Knez.Sim.Tests.Support;

/// <summary>
/// Moves one of the issuing side's fighters by (dx, dy) eighths of a metre plus a random jitter of -1..1 eighths on X.
/// </summary>
public sealed class NudgeCommand(int sideId, EntityId fighterId, int dxEighths, int dyEighths) : ICommand
{
    public int SideId { get; } = sideId;

    public RejectReason Validate(BattleState state)
    {
        var fighter = state.FindFighter(fighterId);
        if (fighter is null)
        {
            return RejectReason.UnknownEntity;
        }

        if (!fighter.IsAlive)
        {
            return RejectReason.NotAlive;
        }

        return fighter.SideId == SideId ? RejectReason.None : RejectReason.WrongSide;
    }

    public void Apply(BattleState state, TickContext context)
    {
        var fighter = state.FindFighter(fighterId)!;
        int jitter = context.Rng(RngStream.Misc).NextInt(-1, 2);
        fighter.Position += new FixVec2(Fix.Ratio(dxEighths + jitter, 8), Fix.Ratio(dyEighths, 8));
    }
}

/// <summary>Writes "name@tick" to a journal when applied; never rejected.</summary>
public sealed class RecordingCommand(string name, List<string> journal) : ICommand
{
    public int SideId => 0;

    public RejectReason Validate(BattleState state) => RejectReason.None;

    public void Apply(BattleState state, TickContext context) => journal.Add($"{name}@{state.Tick}");
}

/// <summary>Always invalid; applying it is a bug.</summary>
public sealed class RejectedCommand : ICommand
{
    public static readonly RejectReason Reason = RejectReason.Because("always-rejected");

    public int SideId => 0;

    public RejectReason Validate(BattleState state) => Reason;

    public void Apply(BattleState state, TickContext context) =>
        throw new InvalidOperationException("A rejected command must never be applied.");
}

/// <summary>Valid, but throws when applied — for fault-handling tests.</summary>
public sealed class ThrowingCommand : ICommand
{
    public int SideId => 0;

    public RejectReason Validate(BattleState state) => RejectReason.None;

    public void Apply(BattleState state, TickContext context) => throw new InvalidOperationException("boom");
}
