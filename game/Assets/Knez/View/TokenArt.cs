namespace Knez.Game.View
{
    /// <summary>
    /// One person token of the art manifest (docs/art-assets.md): whom it shows and where it lies. Plain data, read by
    /// <see cref="FighterArt"/> to pick a man's look.
    /// </summary>
    public sealed class TokenArt
    {
        /// <summary>Describes one token.</summary>
        public TokenArt(string id, string side, string kit, string experience, int variant, bool named, string path)
        {
            Id = id;
            Side = side;
            Kit = kit;
            Experience = experience;
            Variant = variant;
            Named = named;
            Path = path;
        }

        /// <summary>A Retainer's id in the manifest ("hrafn", "hero_clan_sword-shield-mail_0"); a fighter's token name.</summary>
        public string Id { get; }

        /// <summary>Art side: <c>clan</c>, <c>ruler</c> or <c>bandits</c>.</summary>
        public string Side { get; }

        /// <summary>Kit id as in <c>content/defs/combat.json</c>.</summary>
        public string Kit { get; }

        /// <summary>Experience id the man is dressed for (new, experienced, professional, elite).</summary>
        public string Experience { get; }

        /// <summary>Variant number among tokens of the same side and kit.</summary>
        public int Variant { get; }

        /// <summary>True for a named Retainer of the design roster (matched by name).</summary>
        public bool Named { get; }

        /// <summary>Resources path of the sprite, e.g. <c>Tokens/tok_clan_axe-shield_0</c>.</summary>
        public string Path { get; }

        /// <inheritdoc/>
        public override string ToString() => Path;
    }
}
