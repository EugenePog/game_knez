namespace Knez.Game.View
{
    /// <summary>How a man's token is drawn, from his wound and state of mind (<see cref="FighterArt.State(Knez.Sim.Units.Fighter)"/>).</summary>
    public enum TokenState
    {
        /// <summary>In the fight (steady, shaken, wavering or rallying): full colour on his squad's base.</summary>
        Fighting = 0,

        /// <summary>Running: faded token on a faded base.</summary>
        Routing = 1,

        /// <summary>Down (fallen): grey, under the living, no base (art-style.md: "Down — the token is grey").</summary>
        Fallen = 2,

        /// <summary>Fled or left the field in order: out of the fight but alive — a faint token where he went, no base.</summary>
        Gone = 3,
    }
}
