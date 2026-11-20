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
