namespace Knez.Sim
{
    /// <summary>FNV-1a 64-bit hash over little-endian bytes. Fixed algorithm, so equal input hashes equal everywhere.</summary>
    internal struct Fnv1a64
    {
        private const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        private ulong _hash;

        private Fnv1a64(ulong hash)
        {
            _hash = hash;
        }

        public ulong Value => _hash;

        public static Fnv1a64 Start() => new Fnv1a64(OffsetBasis);

        public void Add(ulong value)
        {
            unchecked
            {
                for (int i = 0; i < 8; i++)
                {
                    _hash ^= (byte)(value >> (8 * i));
                    _hash *= Prime;
                }
            }
        }

        public void Add(long value) => Add(unchecked((ulong)value));

        public void Add(int value) => Add(unchecked((ulong)(long)value));

        public void Add(bool value) => Add(value ? 1UL : 0UL);

        /// <summary>Length, then each UTF-16 code unit as two little-endian bytes (ordinal: no culture, no normalisation).</summary>
        public void Add(string value)
        {
            Add(value.Length);
            unchecked
            {
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    _hash ^= (byte)c;
                    _hash *= Prime;
                    _hash ^= (byte)(c >> 8);
                    _hash *= Prime;
                }
            }
        }
    }
}
