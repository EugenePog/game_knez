using System;
using Knez.Sim.Numerics;

namespace Knez.Sim.Defs
{
    /// <summary>
    /// Argument checks shared by the def constructors. Content loaders turn the exceptions into located content errors,
    /// so messages are for people editing JSON and carry no parameter name (Mono and CoreCLR format that differently).
    /// </summary>
    internal static class DefChecks
    {
        public static string Id(string? id, string parameter)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException($"{parameter} must be a non-empty string.");
            }

            return id!;
        }

        public static Fix PositiveMetres(Fix value, int maxMetres, string parameter)
        {
            if (value <= Fix.Zero || value > Fix.FromInt(maxMetres))
            {
                throw new ArgumentException($"{parameter} must be > 0 and <= {maxMetres} m (got {value}).");
            }

            return value;
        }

        public static int Range(int value, int min, int max, string parameter)
        {
            if (value < min || value > max)
            {
                throw new ArgumentException($"{parameter} must be {min}..{max} (got {value}).");
            }

            return value;
        }
    }
}
