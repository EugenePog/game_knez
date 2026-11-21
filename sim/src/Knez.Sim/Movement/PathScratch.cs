using System.Collections.Generic;
using Knez.Sim.Numerics;

namespace Knez.Sim.Movement
{
    /// <summary>
    /// Reusable buffers for one route search (<see cref="PathPlanner"/>), sized to the grid once per simulation. Lives in
    /// <see cref="TickContext"/>: nothing here outlives a search, so it is neither hashed nor cloned. Cells are reset
    /// lazily by a search stamp instead of clearing the arrays every time.
    /// </summary>
    internal sealed class PathScratch
    {
        private readonly int[] _seen;
        private readonly int[] _closedAt;
        private readonly int[] _heapPos;
        private readonly int[] _heap;
        private int _heapCount;
        private int _stamp;

        public PathScratch(int cells)
        {
            G = new int[cells];
            H = new int[cells];
            Parent = new int[cells];
            _seen = new int[cells];
            _closedAt = new int[cells];
            _heapPos = new int[cells];
            _heap = new int[cells];
        }

        /// <summary>Cost so far per cell (valid once <see cref="Seen"/>).</summary>
        public int[] G { get; }

        /// <summary>Heuristic per cell (valid once <see cref="Seen"/>).</summary>
        public int[] H { get; }

        /// <summary>The cell a cell was reached from (valid once <see cref="Seen"/>).</summary>
        public int[] Parent { get; }

        /// <summary>Route cells, goal first, then reversed into start-first order.</summary>
        public List<int> Cells { get; } = new List<int>();

        /// <summary>Waypoints with the anchor first, before smoothing.</summary>
        public List<FixVec2> Points { get; } = new List<FixVec2>();

        /// <summary>For each of <see cref="Points"/>, its index in <see cref="Cells"/>.</summary>
        public List<int> PointCells { get; } = new List<int>();

        /// <summary>For each of <see cref="Points"/>, what the squad does there (see <see cref="Units.Squad.PathFiles"/>).</summary>
        public List<int> PointMarks { get; } = new List<int>();

        /// <summary>For each of <see cref="Cells"/>, how many files of the squad fit there.</summary>
        public List<int> CellFiles { get; } = new List<int>();

        /// <summary>For each of <see cref="Cells"/>, what the squad does on reaching it (−1 nothing, 0 widen, k regroup).</summary>
        public List<int> CellMarks { get; } = new List<int>();

        /// <summary>For each of <see cref="Cells"/>, the travel class of the step into it (out of it for the first).</summary>
        public List<int> CellTravel { get; } = new List<int>();

        /// <summary>For each of <see cref="Cells"/>, the files the squad marches in there (0 = its own width).</summary>
        public List<int> CellNarrowed { get; } = new List<int>();

        /// <summary>For each of <see cref="Cells"/>, the sideways shift of the straight run arriving there.</summary>
        public List<FixVec2> ShiftIn { get; } = new List<FixVec2>();

        /// <summary>For each of <see cref="Cells"/>, the sideways shift of the straight run leaving there.</summary>
        public List<FixVec2> ShiftOut { get; } = new List<FixVec2>();

        /// <summary>For each of <see cref="Points"/>, whether straightening must keep it.</summary>
        public List<bool> PointPinned { get; } = new List<bool>();

        /// <summary>Ranks of the deepest file per file count (index = files), filled per search.</summary>
        public int[] RanksByFiles { get; } = new int[Defs.SquadSpawnDef.MaxCount + 1];

        /// <summary>Starts a new search: every cell becomes unseen and the open set empty.</summary>
        public void Begin()
        {
            _stamp++;
            _heapCount = 0;
        }

        public bool Seen(int cell) => _seen[cell] == _stamp;

        public bool Closed(int cell) => _closedAt[cell] == _stamp;

        public void Close(int cell) => _closedAt[cell] = _stamp;

        public bool OpenEmpty => _heapCount == 0;

        /// <summary>Records <paramref name="cell"/> with cost <paramref name="g"/> from <paramref name="parent"/> and puts it
        /// in the open set, or moves it up if it is there already.</summary>
        public void Open(int cell, int g, int h, int parent)
        {
            G[cell] = g;
            H[cell] = h;
            Parent[cell] = parent;
            if (_seen[cell] != _stamp)
            {
                _seen[cell] = _stamp;
                _heapPos[cell] = _heapCount;
                _heap[_heapCount++] = cell;
            }

            SiftUp(_heapPos[cell]);
        }

        /// <summary>Removes and returns the open cell with the lowest f = g + h; ties: lower h, then lower cell index.</summary>
        public int PopBest()
        {
            int best = _heap[0];
            _heapCount--;
            if (_heapCount > 0)
            {
                int last = _heap[_heapCount];
                _heap[0] = last;
                _heapPos[last] = 0;
                SiftDown(0);
            }

            return best;
        }

        private bool Before(int a, int b)
        {
            int fa = G[a] + H[a];
            int fb = G[b] + H[b];
            if (fa != fb)
            {
                return fa < fb;
            }

            return H[a] != H[b] ? H[a] < H[b] : a < b;
        }

        private void SiftUp(int i)
        {
            int cell = _heap[i];
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                int above = _heap[parent];
                if (!Before(cell, above))
                {
                    break;
                }

                _heap[i] = above;
                _heapPos[above] = i;
                i = parent;
            }

            _heap[i] = cell;
            _heapPos[cell] = i;
        }

        private void SiftDown(int i)
        {
            int cell = _heap[i];
            while (true)
            {
                int child = (2 * i) + 1;
                if (child >= _heapCount)
                {
                    break;
                }

                if (child + 1 < _heapCount && Before(_heap[child + 1], _heap[child]))
                {
                    child++;
                }

                if (!Before(_heap[child], cell))
                {
                    break;
                }

                _heap[i] = _heap[child];
                _heapPos[_heap[i]] = i;
                i = child;
            }

            _heap[i] = cell;
            _heapPos[cell] = i;
        }
    }
}
