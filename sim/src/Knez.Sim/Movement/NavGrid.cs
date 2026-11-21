using System;
using Knez.Sim.Defs;
using Knez.Sim.Numerics;

namespace Knez.Sim.Movement
{
    /// <summary>
    /// The map cut into 0.5 m cells for route planning (battle-sim.md §10; cell size decided 2026-11-21). Each cell keeps
    /// its clearance: the exact distance from its centre to the nearest wall or map edge, so one grid serves squads of
    /// any width. Derived from the immutable map, so it is neither hashed nor cloned. Only routes use it: bodies still
    /// collide with the exact wall rectangles (<see cref="Terrain"/>).
    /// </summary>
    internal sealed class NavGrid
    {
        /// <summary>Cells per metre: 0.5 m cells, a little under a body's 0.6 m width.</summary>
        public const int CellsPerMetre = 2;

        /// <summary>A cell's side: 0.5 m.</summary>
        public static readonly Fix CellSize = Fix.Ratio(1, CellsPerMetre);

        private readonly long[] _clearance;
        private readonly long[] _cornerClearance;
        private readonly int[] _rays;

        /// <summary>Rays reach at most this far (32 m): no formation is wider.</summary>
        public static readonly Fix RayCap = Fix.FromInt(32);

        /// <summary>
        /// Half a cell's diagonal, rounded up (0.354 m): every point of a diagonal step between two cell centres lies
        /// within this of the corner the two cells share.
        /// </summary>
        public static readonly Fix CellHalfDiagonal = Fix.Ratio(354, 1000);

        /// <summary>Builds the grid of <paramref name="map"/>: cells × walls work, once per simulation.</summary>
        public NavGrid(MapDef map)
        {
            Map = map;
            Columns = CellCount(map.Width);
            Rows = CellCount(map.Height);
            _clearance = new long[Columns * Rows];
            _cornerClearance = new long[Columns * Rows];
            Fix half = Fix.FromRaw(Fix.OneRaw / (2 * CellsPerMetre));
            for (int cell = 0; cell < _clearance.Length; cell++)
            {
                FixVec2 centre = Centre(cell);
                _clearance[cell] = Terrain.Clearance(map, centre).Raw;
                _cornerClearance[cell] = Terrain.Clearance(map, centre + new FixVec2(half, half)).Raw;
            }

            _rays = new int[Columns * Rows * 8];
            for (int cell = 0; cell < _clearance.Length; cell++)
            {
                FixVec2 centre = Centre(cell);
                for (int k = 0; k < 8; k++)
                {
                    RayStep(k, out int dx, out int dy);
                    _rays[(cell * 8) + k] = (int)Terrain.Ray(map, centre, dx, dy, RayCap).Raw;
                }
            }
        }

        /// <summary>The map the grid was built from.</summary>
        public MapDef Map { get; }

        /// <summary>Cells along X.</summary>
        public int Columns { get; }

        /// <summary>Cells along Y.</summary>
        public int Rows { get; }

        /// <summary>All cells; cell index = row × <see cref="Columns"/> + column.</summary>
        public int Count => _clearance.Length;

        /// <summary>Room around the centre of <paramref name="cell"/>, metres (zero in a wall or past the map edge).</summary>
        public Fix Clearance(int cell) => Fix.FromRaw(_clearance[cell]);


        /// <summary>
        /// The travel direction class of a grid step (<paramref name="dx"/>, <paramref name="dy"/>): 0 east–west, 1
        /// north–south, 2 north-east–south-west, 3 north-west–south-east.
        /// </summary>
        public static int TravelClass(int dx, int dy) => dy == 0 ? 0 : dx == 0 ? 1 : dx == dy ? 2 : 3;

        /// <summary>The travel class nearest to <paramref name="heading"/>.</summary>
        public static int TravelClass(Angle heading)
        {
            int octant = (((int)heading.Raw + (Angle.UnitsPerTurn / 16)) & (Angle.UnitsPerTurn - 1)) / (Angle.UnitsPerTurn / 8);
            switch (octant % 4)
            {
                case 0: return 0; // east / west
                case 1: return 2; // north-east / south-west
                case 2: return 1; // north / south
                default: return 3; // north-west / south-east
            }
        }

        /// <summary>
        /// The corridor across travel class <paramref name="travel"/> at the centre of <paramref name="cell"/>: wall to wall
        /// (or map edge) at right angles to the travel, capped at <see cref="RayCap"/> each way. A formation's frontage
        /// fits through where this is at least as wide.
        /// </summary>
        public Fix Width(int cell, int travel) =>
            Fix.FromRaw((long)_rays[(cell * 8) + (2 * travel)] + _rays[(cell * 8) + (2 * travel) + 1]);

        /// <summary>
        /// The narrowest corridor through the centre of <paramref name="cell"/>: the least <see cref="Width"/> over the
        /// four travel classes. A gap is narrow whichever way it is crossed (a diagonal crossing of a short gap would
        /// otherwise see past its ends), while a single wall alongside never makes ground narrow: the open side keeps every
        /// cross-section wide.
        /// </summary>
        public Fix Corridor(int cell)
        {
            Fix narrowest = Width(cell, 0);
            for (int travel = 1; travel < 4; travel++)
            {
                narrowest = FixMath.Min(narrowest, Width(cell, travel));
            }

            return narrowest;
        }

        /// <summary>
        /// Room on the first side of the corridor across <paramref name="travel"/> (<paramref name="first"/>) or the second.
        /// The first side lies along <see cref="Side"/>.
        /// </summary>
        public Fix SideRoom(int cell, int travel, bool first) => Fix.FromRaw(_rays[(cell * 8) + (2 * travel) + (first ? 0 : 1)]);

        /// <summary>A unit vector toward the first side of the corridor across <paramref name="travel"/>.</summary>
        public static FixVec2 Side(int travel)
        {
            RayStep(2 * travel, out int dx, out int dy);
            Fix unit = dx != 0 && dy != 0 ? Fix.FromRaw(46341) : Fix.One; // 1/√2 on the diagonals
            return new FixVec2(unit * dx, unit * dy);
        }

        /// <summary>Ray k's grid step. Pairs (2c, 2c + 1) lie across travel class c: N/S, E/W, NW/SE, NE/SW.</summary>
        private static void RayStep(int k, out int dx, out int dy)
        {
            switch (k)
            {
                case 0: dx = 0; dy = 1; return;
                case 1: dx = 0; dy = -1; return;
                case 2: dx = 1; dy = 0; return;
                case 3: dx = -1; dy = 0; return;
                case 4: dx = -1; dy = 1; return;
                case 5: dx = 1; dy = -1; return;
                case 6: dx = 1; dy = 1; return;
                default: dx = -1; dy = -1; return;
            }
        }

        /// <summary>
        /// Room around the north-east corner of the cell at <paramref name="column"/>, <paramref name="row"/>. A diagonal
        /// step through that corner stays clear of walls when this plus the body radius exceeds
        /// <see cref="CellHalfDiagonal"/>: room shrinks at most one-for-one with distance, from the corner along the
        /// step and from the two end cells (whose centres have at least a body radius of room).
        /// </summary>
        public Fix CornerClearance(int column, int row) => Fix.FromRaw(_cornerClearance[(row * Columns) + column]);

        /// <summary>The centre of <paramref name="cell"/>: exact, since a quarter metre is a whole number of raw units.</summary>
        public FixVec2 Centre(int cell)
        {
            int column = cell % Columns;
            int row = cell / Columns;
            return new FixVec2(
                Fix.FromRaw((2L * column + 1) * Fix.OneRaw / (2 * CellsPerMetre)),
                Fix.FromRaw((2L * row + 1) * Fix.OneRaw / (2 * CellsPerMetre)));
        }

        /// <summary>The cell containing <paramref name="point"/>, clamped onto the grid.</summary>
        public int CellOf(FixVec2 point) => Row(point.Y) * Columns + Column(point.X);

        private int Column(Fix x) => Clamp(Floor(x), Columns);

        private int Row(Fix y) => Clamp(Floor(y), Rows);

        private static long Floor(Fix metres) => (metres.Raw * CellsPerMetre) >> Fix.FractionBits; // arithmetic shift floors

        private static int Clamp(long index, int count) => index < 0 ? 0 : index >= count ? count - 1 : (int)index;

        private static int CellCount(Fix side) => (int)((side.Raw * CellsPerMetre + Fix.OneRaw - 1) >> Fix.FractionBits);
    }
}
