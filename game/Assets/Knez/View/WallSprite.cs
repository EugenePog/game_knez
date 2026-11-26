namespace Knez.Game.View
{
    /// <summary>One map sprite placed for a wall (<see cref="WallArt"/>): where its pivot goes, how it is turned and scaled.</summary>
    public readonly struct WallSprite
    {
        /// <summary>Places <paramref name="sprite"/> with its pivot at (<paramref name="x"/>, <paramref name="y"/>) metres.</summary>
        public WallSprite(string sprite, string objectId, float x, float y, float rotation, float scale)
        {
            Sprite = sprite;
            ObjectId = objectId;
            X = x;
            Y = y;
            Rotation = rotation;
            Scale = scale;
        }

        /// <summary>Resources path of the map sprite.</summary>
        public string Sprite { get; }

        /// <summary>The catalogue object drawn (<c>house</c>, <c>boulders</c>, …).</summary>
        public string ObjectId { get; }

        /// <summary>X of the sprite's pivot, metres.</summary>
        public float X { get; }

        /// <summary>Y of the sprite's pivot, metres.</summary>
        public float Y { get; }

        /// <summary>Turn in degrees counter-clockwise: 0 or 90 (walls are axis-aligned).</summary>
        public float Rotation { get; }

        /// <summary>Uniform scale of the sprite (never stretched one way only).</summary>
        public float Scale { get; }

        /// <inheritdoc/>
        public override string ToString() => $"{ObjectId} at ({X:0.##}, {Y:0.##}) turned {Rotation:0} scaled {Scale:0.###}";
    }
}
