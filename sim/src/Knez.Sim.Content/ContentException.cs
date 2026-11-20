using System;

namespace Knez.Sim.Content
{
    /// <summary>
    /// Invalid content: malformed JSON, a missing, unknown or mistyped member, a bad value or an unknown reference.
    /// The message starts with "file:line:column:" and the JSON path, e.g.
    /// <c>content/scenarios/x.json:12:21: squads[1].formation: unknown formation 'wedge' (known: column, line)</c>.
    /// </summary>
    public sealed class ContentException : Exception
    {
        /// <summary>Creates the exception with a complete, located message.</summary>
        public ContentException(string message)
            : base(message)
        {
        }

        /// <summary>Creates the exception, keeping the def constructor's exception as the cause.</summary>
        public ContentException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
