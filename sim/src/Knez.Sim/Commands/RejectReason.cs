using System;

namespace Knez.Sim.Commands
{
    /// <summary>
    /// Why a command was rejected, as a stable kebab-case code the UI can translate ("wrong-side"). The default value,
    /// <see cref="None"/>, means the command is valid. Commands may define their own codes next to the common ones.
    /// </summary>
    public readonly struct RejectReason
    {
        /// <summary>Valid: nothing to reject.</summary>
        public static readonly RejectReason None = default;

        /// <summary>The command names an entity that doesn't exist.</summary>
        public static readonly RejectReason UnknownEntity = new RejectReason("unknown-entity");

        /// <summary>The entity exists but is dead.</summary>
        public static readonly RejectReason NotAlive = new RejectReason("not-alive");

        /// <summary>The entity belongs to another side than the one issuing the command.</summary>
        public static readonly RejectReason WrongSide = new RejectReason("wrong-side");

        /// <summary>The command points at a place outside the map.</summary>
        public static readonly RejectReason OffMap = new RejectReason("off-map");

        /// <summary>The squad is broken: its men are running and take no orders until it re-forms.</summary>
        public static readonly RejectReason SquadBroken = new RejectReason("squad-broken");

        /// <summary>The man is running and takes no orders until he rallies (M3 step 10).</summary>
        public static readonly RejectReason Routing = new RejectReason("routing");

        /// <summary>The squad already has as many men as a squad may (M3 step 10).</summary>
        public static readonly RejectReason SquadFull = new RejectReason("squad-full");

        /// <summary>The Retainer stands in a squad: the order is for a man standing alone, or he is already in that squad.</summary>
        public static readonly RejectReason InSquad = new RejectReason("in-squad");

        /// <summary>The Retainer stands alone: the order is for a man in a squad.</summary>
        public static readonly RejectReason NotInSquad = new RejectReason("not-in-squad");

        /// <summary>The target is not an enemy.</summary>
        public static readonly RejectReason NotAnEnemy = new RejectReason("not-an-enemy");

        /// <summary>The Retainer's ability is still cooling down (M3 step 11).</summary>
        public static readonly RejectReason CoolingDown = new RejectReason("cooling-down");

        /// <summary>The Retainer's ability is defined but not built yet (M3 step 11: promise loot, front-rank rush, no quarter).</summary>
        public static readonly RejectReason NotBuilt = new RejectReason("not-built");

        private RejectReason(string code)
        {
            Code = code;
        }

        /// <summary>The reason code, or null for <see cref="None"/>.</summary>
        public string? Code { get; }

        /// <summary>True when the command must not be applied.</summary>
        public bool IsRejected => Code != null;

        /// <summary>A command-specific reason.</summary>
        public static RejectReason Because(string code) => new RejectReason(code ?? throw new ArgumentNullException(nameof(code)));

        /// <inheritdoc/>
        public override string ToString() => Code ?? "valid";
    }
}
