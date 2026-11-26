namespace Knez.Game.View
{
    /// <summary>
    /// One map object of the art manifest (docs/art-assets.md): a top-down sprite and the rectangle it blocks. Lengths in
    /// metres; the wall's centre is relative to the sprite's pivot (the centre of the footprint), Y north.
    /// </summary>
    public sealed class MapObjectArt
    {
        /// <summary>Describes one map object.</summary>
        public MapObjectArt(string id, string sim, string group, string sprite, int wallCount, float wallX, float wallY, float wallWidth, float wallHeight)
        {
            Id = id;
            Sim = sim;
            Group = group;
            Sprite = sprite;
            WallCount = wallCount;
            WallX = wallX;
            WallY = wallY;
            WallWidth = wallWidth;
            WallHeight = wallHeight;
        }

        /// <summary>Catalogue id: <c>house</c>, <c>boulders</c>, <c>palisade</c>, …</summary>
        public string Id { get; }

        /// <summary>What it is to the sim: <c>house</c>, <c>obstacle</c>, <c>water</c> or <c>free</c>.</summary>
        public string Sim { get; }

        /// <summary>Design group: home, craft, fort, nature.</summary>
        public string Group { get; }

        /// <summary>Resources path of the top-down sprite, e.g. <c>Map/map_house</c>.</summary>
        public string Sprite { get; }

        /// <summary>How many wall rectangles the object blocks (a pen has five, a ford none).</summary>
        public int WallCount { get; }

        /// <summary>X of the centre of the rectangle round all its walls, from the sprite's pivot.</summary>
        public float WallX { get; }

        /// <summary>Y of the centre of the rectangle round all its walls, from the sprite's pivot.</summary>
        public float WallY { get; }

        /// <summary>Width (east-west) of the rectangle round all its walls; 0 without walls.</summary>
        public float WallWidth { get; }

        /// <summary>Height (north-south) of the rectangle round all its walls; 0 without walls.</summary>
        public float WallHeight { get; }

        /// <inheritdoc/>
        public override string ToString() => Id;
    }
}
