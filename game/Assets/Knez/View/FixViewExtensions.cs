using Knez.Sim.Numerics;
using UnityEngine;

namespace Knez.Game.View
{
    /// <summary>
    /// The one place sim numbers become floats (AGENTS.md: <c>Fix</c> → <c>float</c> happens in the view, never in the
    /// sim). Sim metres are Unity units; the sim's X east / Y north is Unity's X / Y (a top-down 2D view).
    /// </summary>
    public static class FixViewExtensions
    {
        /// <summary>Metres as a float.</summary>
        public static float ToFloat(this Fix value) => value.Raw / (float)Fix.OneRaw;

        /// <summary>A sim position as a Unity point.</summary>
        public static Vector2 ToVector2(this FixVec2 value) => new Vector2(value.X.ToFloat(), value.Y.ToFloat());

        /// <summary>A view float as sim metres, rounded to the nearest raw unit (input → commands; never back into the view).</summary>
        public static Fix ToFix(this float value) => Fix.FromRaw((long)System.Math.Round(value * (double)Fix.OneRaw));

        /// <summary>A Unity point as a sim position.</summary>
        public static FixVec2 ToFixVec2(this Vector2 value) => new FixVec2(value.x.ToFix(), value.y.ToFix());

        /// <summary>Degrees counter-clockwise from east, 0..360.</summary>
        public static float ToDegrees(this Angle value) => value.Raw * (360f / 65536f);
    }
}
