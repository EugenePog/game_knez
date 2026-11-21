using System.Collections.Generic;
using Knez.Sim.Defs;
using Knez.Sim.Formations;
using Knez.Sim.Numerics;
using Knez.Sim.Orders;
using Knez.Sim.Units;

namespace Knez.Sim.Movement
{
    /// <summary>
    /// Routes for squad anchors (battle-sim.md §10, M1 step 6). An order whose straight line has room for the whole
    /// formation goes straight; any other waits for <see cref="PathSystem"/>, which runs A* over the 0.5 m
    /// <see cref="NavGrid"/>. Any cell a man fits in is passable. Where the corridor (<see cref="NavGrid.Corridor"/>) is
    /// narrower than the formation's frontage the squad regroups into fewer files a little before it and widens again once its rear is
    /// through (decided 2026-11-21); the search charges only the extra waiting that costs
    /// (<see cref="MovementDef.RegroupCost"/>). The cell route is cut to its straight runs, each shifted sideways so the
    /// formation keeps clear of walls (centred where the corridor is tight), and straightened. A target no route reaches
    /// becomes the nearest spot the squad can reach on its own side of whatever is in the way (decided 2026-11-21).
    /// </summary>
    internal static class PathPlanner
    {
        private const int StraightCost = 10; // per 0.5 m cell
        private const int DiagonalCost = 14;
        private const int CostPerMetre = 2 * StraightCost;

        /// <summary>
        /// Gives <paramref name="squad"/>, which has just got a move order, its route: straight to the target when the
        /// squad stands in its own width and that line has room for the formation all the way, otherwise a request
        /// <see cref="PathSystem"/> serves (this tick, budget permitting).
        /// </summary>
        public static void Request(BattleState state, Squad squad)
        {
            squad.ClearPath();
            FixVec2 target = squad.Order.Target;
            Shape shape = ShapeOf(state, squad, null);
            if (squad.Files == 0 && Terrain.SegmentClear(state.Scenario.Map, squad.Anchor, target, Threshold(shape.Wide)))
            {
                squad.PathList.Add(target);
                squad.PathFilesList.Add(-1);
                ApproachForFacingAbout(state, squad);
                return;
            }

            squad.PathRequestTick = state.Tick;
        }

        /// <summary>
        /// Plans the waiting <paramref name="squad"/>'s route from where its anchor is now and returns the search nodes it
        /// took. Sets <see cref="Squad.Path"/> and its regroup points; a target out of reach is replaced by the nearest
        /// spot reachable on the squad's side; an order facing the way it marched now faces along the route's last leg.
        /// </summary>
        public static int Plan(BattleState state, Squad squad, NavGrid grid, PathScratch scratch)
        {
            squad.ClearPath();
            SquadOrder order = squad.Order;
            MapDef map = state.Scenario.Map;
            Shape shape = ShapeOf(state, squad, scratch.RanksByFiles);
            Fix regroupCost = state.Scenario.Movement.RegroupCost;
            int start = grid.CellOf(squad.Anchor);
            int goal = grid.CellOf(order.Target);

            scratch.Begin();
            scratch.Open(start, 0, Heuristic(start, goal, grid), start);
            int expanded = 0;
            bool reached = false;
            while (!scratch.OpenEmpty)
            {
                int cell = scratch.PopBest();
                scratch.Close(cell);
                expanded++;
                if (cell == goal)
                {
                    // Only possible straight away when the target shares the anchor's cell: then the two must also see
                    // each other (a thin wall can run through a cell). Otherwise the edge into the goal checked it.
                    reached = cell != start || !Terrain.CrossesWall(map, squad.Anchor, order.Target);
                    break;
                }

                Expand(map, grid, scratch, cell, start, goal, squad.Anchor, order.Target, shape, regroupCost);
            }

            int last = reached ? goal : SquadSideOfTarget(grid, scratch, start, squad.Anchor, order.Target, shape.Body);
            FixVec2 end = reached ? order.Target : last == start ? squad.Anchor : grid.Centre(last);
            BuildRoute(state, grid, scratch, squad, last, start, end, shape);
            Angle facing = order.Facing;
            if (order.FacingFromMarch)
            {
                List<FixVec2> path = squad.PathList;
                FixVec2 from = path.Count >= 2 ? path[path.Count - 2] : squad.Anchor;
                FixVec2 leg = end - from;
                if (leg.Length > MovementRules.ArriveDistance)
                {
                    facing = FixMath.Atan2(leg.Y, leg.X);
                }
            }

            squad.Order = SquadOrder.Move(end, facing, order.FacingFromMarch);
            ApproachForFacingAbout(state, squad);
            return expanded;
        }

        /// <summary>
        /// When the order's facing is more than a quarter turn from the route's last leg, the squad will face about at the
        /// end. So that its new front rank lands on the ordered point (decided 2026-11-21), the route runs on one
        /// squad-depth past the target, where the rear rank — the front after facing about — will be over the target. Only
        /// where the ground allows it; otherwise the squad faces about at the target and steps back.
        /// </summary>
        private static void ApproachForFacingAbout(BattleState state, Squad squad)
        {
            SquadOrder order = squad.Order;
            List<FixVec2> path = squad.PathList;
            List<int> marks = squad.PathFilesList;
            if (order.FacingFromMarch || path.Count == 0)
            {
                return;
            }

            FixVec2 target = path[path.Count - 1];
            FixVec2 leg = target - (path.Count >= 2 ? path[path.Count - 2] : squad.Anchor);
            int turn = FixMath.Atan2(leg.Y, leg.X).DeltaTo(order.Facing);
            if (leg.Length <= MovementRules.ArriveDistance || (turn <= Angle.UnitsPerTurn / 4 && turn >= -(Angle.UnitsPerTurn / 4)))
            {
                return;
            }

            int files = squad.Files; // the width it will arrive in: the last regroup or widen on the route decides
            for (int i = 0; i < marks.Count; i++)
            {
                files = marks[i] >= 0 ? marks[i] : files;
            }

            Fix depth = SlotTracking.Depth(squad, files);
            Fix body = ShapeOf(state, squad, null).Body;
            MapDef map = state.Scenario.Map;
            FixVec2 approach = target - (order.Facing.Forward * depth);
            if (depth <= Fix.Zero || Terrain.Clearance(map, approach) < body || !Terrain.SegmentClear(map, target, approach, body))
            {
                return;
            }

            path.Insert(path.Count - 1, approach);
            marks.Insert(marks.Count - 1, -1);
        }

        /// <summary>
        /// The room a squad needs: the widest living member's radius (any narrower and no man fits), and half its
        /// frontage including that radius (any narrower and it must regroup into fewer files).
        /// </summary>
        public static void Room(BattleState state, Squad squad, out Fix body, out Fix wide)
        {
            Shape shape = ShapeOf(state, squad, null);
            body = shape.Body;
            wide = shape.Wide;
        }

        private static Shape ShapeOf(BattleState state, Squad squad, int[]? ranksByFiles)
        {
            Fix body = Fix.Zero;
            List<EntityId> members = squad.MemberList;
            for (int i = 0; i < members.Count; i++)
            {
                Fighter? fighter = state.FindFighter(members[i]);
                if (fighter != null && fighter.IsAlive)
                {
                    body = FixMath.Max(body, fighter.Radius);
                }
            }

            return new Shape(squad.Formation, squad.Frontage, members.Count < 1 ? 1 : members.Count, body, ranksByFiles);
        }

        /// <summary>The least room a sampled straight stretch may show when the route it replaces has <paramref name="room"/>,
        /// but never so little that a stretch could hop over a thin wall between two samples.</summary>
        private static Fix Threshold(Fix room) => FixMath.Max(room - MovementRules.ShortcutSlack, MovementRules.RouteSampleStep);

        /// <summary>
        /// For a target no route reaches: walking the straight line back from the target toward the anchor, the first
        /// cell the search reached that a man fits in — the near side of whatever stands between the squad and its target
        /// (decided 2026-11-21: not the geometrically nearest spot, which may be round the far side).
        /// </summary>
        private static int SquadSideOfTarget(NavGrid grid, PathScratch scratch, int start, FixVec2 anchor, FixVec2 target, Fix body)
        {
            FixVec2 back = anchor - target;
            long samples = (back.Length.Raw / MovementRules.RouteSampleStep.Raw) + 1;
            for (long i = 0; i <= samples; i++)
            {
                var point = new FixVec2(target.X + Fix.FromRaw(back.X.Raw * i / samples), target.Y + Fix.FromRaw(back.Y.Raw * i / samples));
                int cell = grid.CellOf(point);
                if (cell == start)
                {
                    return start;
                }

                if (scratch.Closed(cell) && grid.Clearance(cell) >= body)
                {
                    return cell;
                }
            }

            return start;
        }

        private static void Expand(MapDef map, NavGrid grid, PathScratch scratch, int cell, int start, int goal,
            FixVec2 anchor, FixVec2 target, Shape shape, Fix regroupCost)
        {
            int column = cell % grid.Columns;
            int row = cell / grid.Columns;
            Fix body = shape.Body;
            for (int d = 0; d < 8; d++)
            {
                // East, west, north, south, then the diagonals: a fixed order, so equal-cost routes always tie the same way.
                int sx = d == 0 || d == 4 || d == 6 ? 1 : d == 1 || d == 5 || d == 7 ? -1 : 0;
                int sy = d == 2 || d == 4 || d == 5 ? 1 : d == 3 || d == 6 || d == 7 ? -1 : 0;
                int nc = column + sx;
                int nr = row + sy;
                if (nc < 0 || nr < 0 || nc >= grid.Columns || nr >= grid.Rows)
                {
                    continue;
                }

                int next = (nr * grid.Columns) + nc;
                if (scratch.Closed(next) || (next != goal && grid.Clearance(next) < body))
                {
                    continue;
                }

                bool diagonal = d >= 4;
                if (diagonal
                    && (grid.Clearance((row * grid.Columns) + nc) < body || grid.Clearance((nr * grid.Columns) + column) < body
                        || grid.CornerClearance(nc < column ? nc : column, nr < row ? nr : row) + body <= NavGrid.CellHalfDiagonal))
                {
                    continue; // never cut a corner a man can't get round, nor step past a post on the cells' shared corner
                }

                // The route's first leg runs from the anchor, not from its cell's centre, and its last leg to the target:
                // both can be anywhere in their cells, so check them against walls (a thin wall can cross a cell).
                if ((cell == start || next == goal)
                    && Terrain.CrossesWall(map, cell == start ? anchor : grid.Centre(cell), next == goal ? target : grid.Centre(next)))
                {
                    continue;
                }

                int step = diagonal ? DiagonalCost : StraightCost;
                int filesHere = shape.FilesIn(grid.Corridor(cell));
                int filesThere = shape.FilesIn(grid.Corridor(next));
                if (filesThere < filesHere)
                {
                    // Narrowing: the squad grows deeper and its rear waits that much longer to go through.
                    Fix deeper = shape.Depth(filesThere) - shape.Depth(filesHere);
                    step += (int)((regroupCost * deeper * CostPerMetre).Raw >> Fix.FractionBits);
                }

                int g = scratch.G[cell] + step;
                if (!scratch.Seen(next) || g < scratch.G[next])
                {
                    scratch.Open(next, g, Heuristic(next, goal, grid), cell);
                }
            }
        }

        /// <summary>Octile distance to the goal at open-ground cost: never more than the real cost, so A* stays exact.</summary>
        private static int Heuristic(int cell, int goal, NavGrid grid)
        {
            int dx = (cell % grid.Columns) - (goal % grid.Columns);
            int dy = (cell / grid.Columns) - (goal / grid.Columns);
            dx = dx < 0 ? -dx : dx;
            dy = dy < 0 ? -dy : dy;
            int diagonal = dx < dy ? dx : dy;
            return (DiagonalCost * diagonal) + (StraightCost * (dx + dy - (2 * diagonal)));
        }

        /// <summary>
        /// Turns the cell chain ending at <paramref name="last"/> into the squad's route:
        /// <list type="number">
        /// <item>where the corridor across the march is narrower than the formation, mark where the squad regroups and
        /// widens again (<see cref="MarkRegroups"/>);</item>
        /// <item>shift each straight run of cells sideways so the formation, in the width it marches there, keeps clear of
        /// walls — centred on the tightest cell when it can't (<see cref="ShiftRuns"/>);</item>
        /// <item>waypoints: the anchor, both ends of each run, the first and last cell, the marked cells and
        /// <paramref name="end"/>; if any shifted leg would cross a wall, the whole route is laid unshifted instead (those
        /// legs are the ones the search walked);</item>
        /// <item>straighten across corners wherever the straight stretch has about as much room as the cells it skips
        /// (capped at the frontage); marked, shifted and narrow waypoints are kept.</item>
        /// </list>
        /// </summary>
        private static void BuildRoute(BattleState state, NavGrid grid, PathScratch scratch, Squad squad, int last, int start,
            FixVec2 end, Shape shape)
        {
            List<int> cells = scratch.Cells;
            cells.Clear();
            for (int cell = last; cell != start; cell = scratch.Parent[cell])
            {
                cells.Add(cell);
            }

            cells.Add(start);
            cells.Reverse(); // start first; reversing a list of ints allocates nothing
            int n = cells.Count;

            List<int> travel = scratch.CellTravel;
            List<int> fits = scratch.CellFiles;
            List<int> marks = scratch.CellMarks;
            travel.Clear();
            fits.Clear();
            marks.Clear();
            int arrival = squad.Order.FacingFromMarch ? -1 : NavGrid.TravelClass(squad.Order.Facing);
            for (int i = 0; i < n; i++)
            {
                int from = i > 0 ? cells[i - 1] : cells[i];
                int to = i > 0 ? cells[i] : n > 1 ? cells[1] : cells[0];
                int t = from == to ? (arrival < 0 ? 0 : arrival) : NavGrid.TravelClass(Sign(to % grid.Columns - from % grid.Columns), Sign(to / grid.Columns - from / grid.Columns));
                travel.Add(t);
                int fit = shape.FilesIn(grid.Corridor(cells[i]));
                if (i == 0 && squad.Files > 0 && squad.Files < fit)
                {
                    fit = squad.Files; // already narrowed: its rear may still be in the gap behind it
                }

                fits.Add(fit);
                marks.Add(-1);
            }

            int arrivalFits = shape.FilesIn(grid.Width(cells[n - 1], arrival < 0 ? travel[n - 1] : arrival));
            MarkRegroups(state.Scenario.Movement.RegroupLead, grid, scratch, shape, arrivalFits);
            if (marks[0] > 0)
            {
                squad.Files = marks[0]; // narrow ground here already: regroup before setting off
                squad.RegroupSince = state.Tick;
            }

            MapDef map = state.Scenario.Map;
            ShiftRuns(grid, scratch, shape, squad.Anchor, zero: false);
            LayPoints(grid, scratch, squad.Anchor, end, shape);
            DropDoublingBack(map, scratch);
            List<FixVec2> points = scratch.Points;
            for (int p = 1; p < points.Count; p++)
            {
                if (Terrain.CrossesWall(map, points[p - 1], points[p]))
                {
                    ShiftRuns(grid, scratch, shape, squad.Anchor, zero: true);
                    LayPoints(grid, scratch, squad.Anchor, end, shape);
                    DropDoublingBack(map, scratch);
                    break;
                }
            }

            List<int> at = scratch.PointCells;
            List<int> pointMarks = scratch.PointMarks;
            List<bool> pinned = scratch.PointPinned;
            int here = 0;
            while (here < points.Count - 1)
            {
                int to = here + 1;
                for (int next = here + 2; next < points.Count && !pinned[to]; next++)
                {
                    Fix room = shape.Wide;
                    for (int c = at[here]; c <= at[next]; c++)
                    {
                        room = FixMath.Min(room, grid.Clearance(cells[c]));
                    }

                    if (!Terrain.SegmentClear(map, points[here], points[next], Threshold(FixMath.Max(room, shape.Body))))
                    {
                        break;
                    }

                    to = next;
                }

                squad.PathList.Add(points[to]);
                squad.PathFilesList.Add(pointMarks[to]);
                here = to;
            }

            DropDoublingBack(map, squad); // straightening can leave a short leg that turns back
        }

        /// <summary>The same clean-up as <see cref="DropDoublingBack(MapDef, PathScratch)"/> on the finished route,
        /// starting from the anchor; the target is never dropped.</summary>
        private static void DropDoublingBack(MapDef map, Squad squad)
        {
            List<FixVec2> path = squad.PathList;
            List<int> files = squad.PathFilesList;
            int i = 0;
            while (i < path.Count - 1)
            {
                FixVec2 before = i > 0 ? path[i - 1] : squad.Anchor;
                FixVec2 inLeg = path[i] - before;
                FixVec2 outLeg = path[i + 1] - path[i];
                bool shortLeg = inLeg.Length < NavGrid.CellSize || outLeg.Length < NavGrid.CellSize;
                if (files[i] == -1 && shortLeg && FixVec2.Dot(inLeg, outLeg) < Fix.Zero && !Terrain.CrossesWall(map, before, path[i + 1]))
                {
                    path.RemoveAt(i);
                    files.RemoveAt(i);
                    i = i > 0 ? i - 1 : 0;
                    continue;
                }

                i++;
            }
        }

        /// <summary>
        /// Marks regroup (k files) and widen (0) cells along the chain. A stretch is a run of cells where fewer files fit
        /// than the formation has; the squad regroups <paramref name="lead"/> before it into as many files as its tightest
        /// cell allows, and widens once its narrowed depth plus the lead is past it. Stretches too close together to widen
        /// between count as one. It widens at the target only if its rear is through by then and the target has room for
        /// its frontage at the arrival facing (<paramref name="arrivalFits"/>); otherwise it ends narrowed. Fills
        /// <see cref="PathScratch.CellNarrowed"/>.
        /// </summary>
        private static void MarkRegroups(Fix lead, NavGrid grid, PathScratch scratch, Shape shape, int arrivalFits)
        {
            List<int> cells = scratch.Cells;
            List<int> fits = scratch.CellFiles;
            List<int> marks = scratch.CellMarks;
            List<int> narrowed = scratch.CellNarrowed;
            int n = cells.Count;
            narrowed.Clear();
            for (int c = 0; c < n; c++)
            {
                narrowed.Add(0);
            }

            int i = 0;
            while (i < n)
            {
                if (fits[i] >= shape.Files)
                {
                    i++;
                    continue;
                }

                int first = i;
                int stretchEnd = i;
                int need = fits[i];
                int widen;
                bool through;
                int next;
                while (true)
                {
                    while (stretchEnd + 1 < n && fits[stretchEnd + 1] < shape.Files)
                    {
                        stretchEnd++;
                        need = need < fits[stretchEnd] ? need : fits[stretchEnd];
                    }

                    widen = Walk(grid, cells, stretchEnd, +1, shape.Depth(need) + lead, out through);
                    next = stretchEnd + 1;
                    while (next < n && fits[next] >= shape.Files)
                    {
                        next++;
                    }

                    if (next < n && Walk(grid, cells, next, -1, lead, out _) <= widen)
                    {
                        stretchEnd = next; // too close to widen in between: one stretch
                        need = need < fits[next] ? need : fits[next];
                        continue;
                    }

                    break;
                }

                int regroup = Walk(grid, cells, first, -1, lead, out _);
                marks[regroup] = need;
                bool widens = through && (widen < n - 1 || arrivalFits >= shape.Files);
                if (widens)
                {
                    marks[widen] = 0;
                }

                for (int c = regroup; c < (widens ? widen : n); c++)
                {
                    narrowed[c] = need;
                }

                i = next;
            }
        }

        /// <summary>
        /// Splits the chain into straight runs and shifts each sideways so the formation (in the width it marches there)
        /// keeps clear of walls on both sides along the whole run, with up to a quarter metre to spare: by as little as
        /// that takes (the first run: as little off the line the squad stands on); where no shift does, onto the middle of
        /// the run's tightest cross-section. With
        /// <paramref name="zero"/>, no shifts at all.
        /// </summary>
        private static void ShiftRuns(NavGrid grid, PathScratch scratch, Shape shape, FixVec2 anchor, bool zero)
        {
            List<int> cells = scratch.Cells;
            List<int> narrowed = scratch.CellNarrowed;
            List<FixVec2> shiftIn = scratch.ShiftIn;
            List<FixVec2> shiftOut = scratch.ShiftOut;
            int n = cells.Count;
            shiftIn.Clear();
            shiftOut.Clear();
            for (int c = 0; c < n; c++)
            {
                shiftIn.Add(FixVec2.Zero);
                shiftOut.Add(FixVec2.Zero);
            }

            int runStart = 0;
            while (!zero && runStart < n - 1)
            {
                int step = Direction(grid, cells[runStart], cells[runStart + 1]);
                int runEnd = runStart + 1;
                while (runEnd + 1 < n && Direction(grid, cells[runEnd], cells[runEnd + 1]) == step)
                {
                    runEnd++;
                }

                int travel = scratch.CellTravel[runEnd];
                Fix lo = Fix.MinValue;
                Fix hi = Fix.MaxValue;
                Fix tightest = Fix.MaxValue;
                Fix centred = Fix.Zero;
                for (int c = runStart; c <= runEnd; c++)
                {
                    Fix half = shape.HalfFront(narrowed[c]);
                    Fix sideA = grid.SideRoom(cells[c], travel, first: true);
                    Fix sideB = grid.SideRoom(cells[c], travel, first: false);
                    lo = FixMath.Max(lo, half - sideB);
                    hi = FixMath.Min(hi, sideA - half);
                    if (sideA + sideB < tightest)
                    {
                        tightest = sideA + sideB;
                        centred = (sideA - sideB) / 2;
                    }
                }

                // The smallest shift that keeps clear with up to a quarter metre to spare each side (all of the spare room,
                // shared equally, in a tight gap: its middle); no shift keeps clear: the middle of the tightest cross-section.
                // The first run prefers the line the squad already stands on: no side-step to set off (and a narrowed squad's
                // rear, still in the gap behind it, stays where it fits).
                Fix preferred = runStart == 0 ? FixVec2.Dot(anchor - grid.Centre(cells[0]), NavGrid.Side(travel)) : Fix.Zero;
                Fix spare = FixMath.Min((hi - lo) / 2, NavGrid.CellSize / 2);
                Fix shift = lo > hi ? centred : FixMath.Clamp(preferred, lo + spare, hi - spare);
                FixVec2 offset = NavGrid.Side(travel) * shift;
                for (int c = runStart; c <= runEnd; c++)
                {
                    if (c > runStart)
                    {
                        shiftIn[c] = offset;
                    }

                    if (c < runEnd)
                    {
                        shiftOut[c] = offset;
                    }
                }

                runStart = runEnd;
            }
        }

        /// <summary>Fills the waypoint candidates (see <see cref="BuildRoute"/>) from the chain, its marks and shifts.</summary>
        private static void LayPoints(NavGrid grid, PathScratch scratch, FixVec2 anchor, FixVec2 end, Shape shape)
        {
            List<int> cells = scratch.Cells;
            List<int> marks = scratch.CellMarks;
            List<int> narrowed = scratch.CellNarrowed;
            List<FixVec2> shiftIn = scratch.ShiftIn;
            List<FixVec2> shiftOut = scratch.ShiftOut;
            List<FixVec2> points = scratch.Points;
            List<int> at = scratch.PointCells;
            List<int> pointMarks = scratch.PointMarks;
            List<bool> pinned = scratch.PointPinned;
            int n = cells.Count;
            points.Clear();
            at.Clear();
            pointMarks.Clear();
            pinned.Clear();
            Add(scratch, anchor, 0, -1, pin: false);
            for (int i = 1; i < n - 1; i++)
            {
                bool turns = Direction(grid, cells[i - 1], cells[i]) != Direction(grid, cells[i], cells[i + 1]);
                if (i == 1 || i == n - 2 || marks[i] != -1 || turns)
                {
                    bool pin = marks[i] != -1 || narrowed[i] > 0 || shiftIn[i] != FixVec2.Zero || shiftOut[i] != FixVec2.Zero;
                    FixVec2 centre = grid.Centre(cells[i]);
                    FixVec2 point = turns && shiftIn[i] != shiftOut[i]
                        ? Corner(grid, centre, cells[i - 1], cells[i], cells[i + 1], shiftIn[i], shiftOut[i])
                        : centre + shiftIn[i];
                    Add(scratch, point, i, marks[i], pin);
                }
            }

            Add(scratch, end, n - 1, n > 1 ? marks[n - 1] : -1, pin: true);
        }

        /// <summary>
        /// Where the run arriving at <paramref name="cell"/> (centre <paramref name="centre"/>) from
        /// <paramref name="before"/>, shifted by <paramref name="shiftIn"/>, meets the run leaving it toward
        /// <paramref name="after"/>, shifted by <paramref name="shiftOut"/>: the corner of the two shifted lines, so the
        /// route turns once instead of stepping between them. It can overshoot when the shifts differ a lot;
        /// <see cref="DropDoublingBack(MapDef, PathScratch)"/> cleans that up.
        /// </summary>
        private static FixVec2 Corner(NavGrid grid, FixVec2 centre, int before, int cell, int after, FixVec2 shiftIn, FixVec2 shiftOut)
        {
            StepOf(grid, before, cell, out int ax, out int ay);
            StepOf(grid, cell, after, out int bx, out int by);
            int across = (ax * by) - (ay * bx); // nonzero: the runs differ in direction and a route never reverses
            FixVec2 apart = shiftOut - shiftIn;
            // centre + shiftIn + a·t = centre + shiftOut + b·u  →  t = (apart × b) / (a × b), in cell steps.
            Fix t = ((apart.X * by) - (apart.Y * bx)) / across;
            return centre + shiftIn + new FixVec2(t * ax, t * ay);
        }

        /// <summary>The grid step from cell <paramref name="from"/> to its neighbour <paramref name="to"/>, decoded from
        /// their columns and rows (raw index differences are ambiguous on a grid two cells wide).</summary>
        private static void StepOf(NavGrid grid, int from, int to, out int dx, out int dy)
        {
            dx = Sign((to % grid.Columns) - (from % grid.Columns));
            dy = Sign((to / grid.Columns) - (from / grid.Columns));
        }

        /// <summary>The step from <paramref name="from"/> to <paramref name="to"/> as one comparable number.</summary>
        private static int Direction(NavGrid grid, int from, int to)
        {
            StepOf(grid, from, to, out int dx, out int dy);
            return ((dx + 1) * 3) + dy + 1;
        }

        /// <summary>
        /// Drops waypoints that send the squad back on itself: a short leg (under half a cell) followed or preceded by a
        /// turn of more than 90°. Shifted runs meeting at a corner can overshoot like that; following it would pivot the
        /// squad about and back. Marked waypoints stay; a drop that would cross a wall doesn't happen.
        /// </summary>
        private static void DropDoublingBack(MapDef map, PathScratch scratch)
        {
            List<FixVec2> points = scratch.Points;
            int i = 1;
            while (i < points.Count - 1)
            {
                FixVec2 inLeg = points[i] - points[i - 1];
                FixVec2 outLeg = points[i + 1] - points[i];
                bool shortLeg = inLeg.Length < NavGrid.CellSize || outLeg.Length < NavGrid.CellSize;
                if (scratch.PointMarks[i] == -1 && shortLeg && FixVec2.Dot(inLeg, outLeg) < Fix.Zero
                    && !Terrain.CrossesWall(map, points[i - 1], points[i + 1]))
                {
                    points.RemoveAt(i);
                    scratch.PointCells.RemoveAt(i);
                    scratch.PointMarks.RemoveAt(i);
                    scratch.PointPinned.RemoveAt(i);
                    i = i > 1 ? i - 1 : 1; // the new neighbours may now double back
                    continue;
                }

                i++;
            }
        }

        private static void Add(PathScratch scratch, FixVec2 point, int cell, int mark, bool pin)
        {
            scratch.Points.Add(point);
            scratch.PointCells.Add(cell);
            scratch.PointMarks.Add(mark);
            scratch.PointPinned.Add(pin);
        }

        private static int Sign(int value) => value > 0 ? 1 : value < 0 ? -1 : 0;

        /// <summary>The chain index reached by walking <paramref name="distance"/> metres from <paramref name="from"/>
        /// (forward with +1, back with −1), stopping at either end; <paramref name="covered"/> says whether the whole
        /// distance was walked.</summary>
        private static int Walk(NavGrid grid, List<int> cells, int from, int direction, Fix distance, out bool covered)
        {
            Fix walked = Fix.Zero;
            int i = from;
            while (walked < distance && i + direction >= 0 && i + direction < cells.Count)
            {
                int a = cells[i];
                int b = cells[i + direction];
                bool diagonal = a % grid.Columns != b % grid.Columns && a / grid.Columns != b / grid.Columns;
                walked += diagonal ? NavGrid.CellHalfDiagonal * 2 : NavGrid.CellSize;
                i += direction;
            }

            covered = walked >= distance;
            return i;
        }

        /// <summary>A squad's size and shape for route planning.</summary>
        private readonly struct Shape
        {
            private readonly FormationDef _formation;
            private readonly int[]? _ranks;

            public Shape(FormationDef formation, int frontage, int count, Fix body, int[]? ranksByFiles)
            {
                _formation = formation;
                Body = body;
                Files = FormationLayout.Width(formation, frontage, count);
                Wide = HalfFrontOf(formation, Files, body);
                _ranks = ranksByFiles;
                if (_ranks != null)
                {
                    for (int files = 1; files <= Files; files++)
                    {
                        _ranks[files] = FormationLayout.NarrowedRanks(Files, count, files);
                    }
                }
            }

            /// <summary>The widest member's radius.</summary>
            public Fix Body { get; }

            /// <summary>Files in the formation's own width.</summary>
            public int Files { get; }

            /// <summary>Half the frontage in its own width, plus a body radius.</summary>
            public Fix Wide { get; }

            /// <summary>Half the frontage of <paramref name="files"/> files (0 = its own width), plus a body radius.</summary>
            public Fix HalfFront(int files) => files <= 0 || files >= Files ? Wide : HalfFrontOf(_formation, files, Body);

            /// <summary>How many files fit in a corridor <paramref name="width"/> wide (at least 1).</summary>
            public int FilesIn(Fix width)
            {
                int files = 1;
                while (files < Files && width >= Body + Body + (_formation.Spacing * files))
                {
                    files++;
                }

                return files;
            }

            /// <summary>Front rank to rear rank when narrowed to <paramref name="files"/> files (needs the ranks table).</summary>
            public Fix Depth(int files) => _formation.RankSpacing * (_ranks![files < Files ? files : Files] - 1);

            private static Fix HalfFrontOf(FormationDef formation, int files, Fix body) => (formation.Spacing * (files - 1) / 2) + body;
        }
    }
}
