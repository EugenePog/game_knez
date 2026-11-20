using Knez.Sim.Numerics;

namespace Knez.Sim.Content
{
    /// <summary>
    /// Exact conversion of decimal text to sim numbers, with integer arithmetic only (battle-sim.md §5, ADR-0004).
    /// "0.1" is always raw 6554: the nearest 1/65536, ties away from zero, so negative text mirrors positive text.
    /// </summary>
    public static class ContentNumbers
    {
        /// <summary>Most digits after the decimal point; more would only describe sub-1/65536 detail.</summary>
        public const int MaxFractionDigits = 9;

        /// <summary>Largest whole part accepted; content values are metres, degrees and counts, far below this.</summary>
        public const long MaxWhole = 1000000000;

        /// <summary>
        /// Parses <c>-?digits(.digits)?</c> (no exponent, no leading '+', no leading zeros) into a <see cref="Fix"/>.
        /// On failure returns false and a reason for the error message.
        /// </summary>
        public static bool TryParseFix(string text, out Fix value, out string error)
        {
            value = Fix.Zero;
            if (!TrySplit(text, out bool negative, out long whole, out long fraction, out int fractionDigits, out error))
            {
                return false;
            }

            long scale = 1;
            for (int i = 0; i < fractionDigits; i++)
            {
                scale *= 10;
            }

            // fraction < 10^9, so fraction · 65536 < 6.6·10^13: no overflow. Round half up on the magnitude.
            long fractionRaw = (fraction * Fix.OneRaw + scale / 2) / scale;
            long raw = whole * Fix.OneRaw + fractionRaw;
            value = Fix.FromRaw(negative ? -raw : raw);
            return true;
        }

        /// <summary>Parses <c>-?digits</c> into an <see cref="int"/>; a fraction or an out-of-range value is an error.</summary>
        public static bool TryParseInt(string text, out int value, out string error)
        {
            value = 0;
            if (!TrySplit(text, out bool negative, out long whole, out _, out int fractionDigits, out error))
            {
                return false;
            }

            if (fractionDigits > 0)
            {
                error = $"expected a whole number, got {text}";
                return false;
            }

            long signed = negative ? -whole : whole;
            if (signed < int.MinValue || signed > int.MaxValue)
            {
                error = $"{text} is out of range for a whole number";
                return false;
            }

            value = (int)signed;
            return true;
        }

        private static bool TrySplit(string text, out bool negative, out long whole, out long fraction,
            out int fractionDigits, out string error)
        {
            negative = false;
            whole = 0;
            fraction = 0;
            fractionDigits = 0;
            error = string.Empty;
            if (string.IsNullOrEmpty(text))
            {
                error = "expected a number";
                return false;
            }

            int pos = 0;
            if (text[0] == '-')
            {
                negative = true;
                pos = 1;
            }

            int wholeStart = pos;
            while (pos < text.Length && IsDigit(text[pos]))
            {
                if (whole > MaxWhole)
                {
                    break;
                }

                whole = whole * 10 + (text[pos] - '0');
                pos++;
            }

            int wholeDigits = pos - wholeStart;
            if (wholeDigits == 0)
            {
                error = $"'{text}' is not a number";
                return false;
            }

            if (whole > MaxWhole)
            {
                error = $"{text} is too large (limit {MaxWhole})";
                return false;
            }

            if (wholeDigits > 1 && text[wholeStart] == '0')
            {
                error = $"'{text}': leading zeros are not allowed";
                return false;
            }

            if (pos < text.Length && text[pos] == '.')
            {
                pos++;
                int fractionStart = pos;
                while (pos < text.Length && IsDigit(text[pos]))
                {
                    if (pos - fractionStart == MaxFractionDigits)
                    {
                        error = $"{text} has more than {MaxFractionDigits} digits after the point";
                        return false;
                    }

                    fraction = fraction * 10 + (text[pos] - '0');
                    pos++;
                }

                fractionDigits = pos - fractionStart;
                if (fractionDigits == 0)
                {
                    error = $"'{text}': expected digits after the point";
                    return false;
                }
            }

            if (pos < text.Length)
            {
                error = text[pos] == 'e' || text[pos] == 'E'
                    ? $"{text}: exponents are not allowed in content numbers; write the number out"
                    : $"'{text}' is not a number";
                return false;
            }

            return true;
        }

        private static bool IsDigit(char c) => c >= '0' && c <= '9';
    }
}
