#nullable enable
namespace Knez.Game.View
{
    /// <summary>One squad's numbers for the HUD (M3): its men by condition, their average morale, and its blows.</summary>
    public sealed class SquadStats
    {
        /// <summary>Men still in the fight (alive, not fled), running ones included.</summary>
        public int InFight { get; internal set; }

        /// <summary>Of those, men running.</summary>
        public int Running { get; internal set; }

        /// <summary>Men in the fight with a light wound.</summary>
        public int Light { get; internal set; }

        /// <summary>Men in the fight with a heavy wound.</summary>
        public int Heavy { get; internal set; }

        /// <summary>Men who fell.</summary>
        public int Fallen { get; internal set; }

        /// <summary>Men who fled the field.</summary>
        public int Fled { get; internal set; }

        /// <summary>Average morale of the men in the fight (0 when none).</summary>
        public int Morale { get; internal set; }

        /// <summary>Blows the squad's men struck this battle.</summary>
        public int Blows { get; internal set; }

        /// <summary>Of those, blows that got through.</summary>
        public int Landed { get; internal set; }
    }
}
