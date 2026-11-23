using Knez.Sim.Combat;

namespace Knez.Sim.Events
{
    /// <summary>A blow got through and wounded a man (battle-sim.md §11): how it landed and how he is now. A man whose
    /// wound is now <see cref="WoundLevel.Down"/> has fallen — he is out of the fight.</summary>
    public sealed class Wounded : SimEvent
    {
        /// <summary>Creates the event.</summary>
        public Wounded(int tick, EntityId fighter, EntityId by, WoundLevel blow, WoundLevel now)
            : base(tick)
        {
            Fighter = fighter;
            By = by;
            Blow = blow;
            Now = now;
        }

        /// <summary>Who was wounded.</summary>
        public EntityId Fighter { get; }

        /// <summary>Whose blow it was.</summary>
        public EntityId By { get; }

        /// <summary>How the blow landed (rolled from the weapon's row).</summary>
        public WoundLevel Blow { get; }

        /// <summary>His wound after it (wounds add up).</summary>
        public WoundLevel Now { get; }
    }
}
