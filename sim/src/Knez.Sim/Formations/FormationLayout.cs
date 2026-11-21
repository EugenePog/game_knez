using System;
using Knez.Sim.Defs;
using Knez.Sim.Numerics;

namespace Knez.Sim.Formations
{
    /// <summary>
    /// Slot geometry of a formation (battle-sim.md §10). Slots are numbered front rank first, left to right; the anchor
    /// is the centre of the front rank; an incomplete rank is the rearmost one and is centred. Local coordinates:
    /// X to the squad's right, Y forward. Pure functions, no allocation — safe in per-fighter hot paths.
    /// </summary>
    public static class FormationLayout
    {
        /// <summary>Fighters per full rank for a squad of <paramref name="count"/>.</summary>
        public static int Width(FormationDef formation, int count)
        {
            if (count < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "a formation needs at least one fighter.");
            }

            return formation.Width > 0
                ? Math.Min(formation.Width, count)
                : (count + formation.Ranks - 1) / formation.Ranks;
        }

        /// <summary>Offset of <paramref name="slot"/> from the anchor in local coordinates (X right, Y forward).</summary>
        public static FixVec2 SlotOffset(FormationDef formation, int count, int slot) =>
            SlotOffsetAt(formation, Width(formation, count), count, slot, 0);

        /// <summary>
        /// Files across the front for <paramref name="count"/> men in a formation whose front was set at
        /// <paramref name="frontage"/> files (0 = the formation's own width for this count): losses thin the rear, so the
        /// front narrows only when fewer men are left than files.
        /// </summary>
        public static int Width(FormationDef formation, int frontage, int count) =>
            frontage <= 0 ? Width(formation, count) : Math.Min(frontage, count);

        /// <summary>Offset of <paramref name="slot"/> with <paramref name="width"/> files across the front (the incomplete
        /// rank is the rearmost, centred).</summary>
        private static FixVec2 Normal(FormationDef formation, int width, int count, int slot)
        {
            if (slot < 0 || slot >= count)
            {
                throw new ArgumentOutOfRangeException(nameof(slot), $"slot {slot} is outside 0..{count - 1}.");
            }

            int rank = slot / width;
            int file = slot - rank * width;
            int inRank = Math.Min(width, count - rank * width);

            // Centred: files sit at (2·file − (inRank − 1)) / 2 spacings from the middle. Fix / int truncates toward
            // zero, so mirrored files get exactly mirrored offsets.
            Fix x = formation.Spacing * (2 * file - (inRank - 1)) / 2;
            Fix y = -(formation.RankSpacing * rank);
            return new FixVec2(x, y);
        }

        /// <summary>
        /// Offset of <paramref name="slot"/> when the squad is narrowed to <paramref name="files"/> files to pass a gap (0,
        /// or at least the formation's own width: the normal layout). Each old rank splits left to right into
        /// <paramref name="files"/> bands as even as possible, the odd men going to the bands round-robin across ranks so
        /// the new files differ by one man at most; within a new file men stand by their old rank, inner files before outer ones, so the
        /// middle of the front stays in front and the flanks fall in behind. O(squad size) per call; only narrowed squads
        /// pay it.
        /// </summary>
        public static FixVec2 SlotOffset(FormationDef formation, int count, int slot, int files) =>
            SlotOffsetAt(formation, Width(formation, count), count, slot, files);

        /// <summary>
        /// Offset of <paramref name="slot"/> with <paramref name="width"/> files across the front (see
        /// <see cref="Width(FormationDef, int, int)"/>), narrowed to <paramref name="files"/> files for a gap (0: not narrowed).
        /// </summary>
        public static FixVec2 SlotOffsetAt(FormationDef formation, int width, int count, int slot, int files)
        {
            if (files <= 0 || files >= width)
            {
                return Normal(formation, width, count, slot);
            }

            if (slot < 0 || slot >= count)
            {
                throw new ArgumentOutOfRangeException(nameof(slot), $"slot {slot} is outside 0..{count - 1}.");
            }

            Place(width, count, slot, files, out int file, out int rank, out int lateral);
            int depth = 0;
            for (int other = 0; other < count; other++)
            {
                Place(width, count, other, files, out int otherFile, out int otherRank, out int otherLateral);
                if (otherFile == file && Before(otherRank, otherLateral, rank, lateral))
                {
                    depth++;
                }
            }

            Fix x = formation.Spacing * (2 * file - (files - 1)) / 2;
            Fix y = -(formation.RankSpacing * depth);
            return new FixVec2(x, y);
        }

        /// <summary>Ranks of the deepest file when a squad of <paramref name="count"/> is narrowed to <paramref name="files"/>
        /// files (the normal rank count when that is no narrowing).</summary>
        public static int NarrowedRanks(FormationDef formation, int count, int files) =>
            NarrowedRanks(Width(formation, count), count, files);

        /// <summary>As <see cref="NarrowedRanks(FormationDef, int, int)"/> for a front of <paramref name="width"/> files.</summary>
        public static int NarrowedRanks(int width, int count, int files)
        {
            if (files <= 0 || files >= width)
            {
                return (count + width - 1) / width;
            }

            int deepest = 0;
            for (int f = 0; f < files; f++)
            {
                int inFile = 0;
                for (int slot = 0; slot < count; slot++)
                {
                    Place(width, count, slot, files, out int file, out _, out _);
                    inFile += file == f ? 1 : 0;
                }

                deepest = Math.Max(deepest, inFile);
            }

            return deepest;
        }

        /// <summary>For <paramref name="slot"/> in the normal layout: its rank, its place across the front in half spacings
        /// (0 = the middle), and the narrowed file it goes to.</summary>
        private static void Place(int width, int count, int slot, int files, out int file, out int rank, out int lateral)
        {
            rank = slot / width;
            int inRank = Math.Min(width, count - rank * width);
            int position = slot - rank * width;
            lateral = 2 * position - (inRank - 1);

            // Bands of inRank / files men; the odd men go round-robin, carrying on from where the ranks in front left off
            // (all of them full: width % files odd men each), so every band gets its share of odd men overall.
            int perBand = inRank / files;
            int extra = inRank % files;
            int firstExtra = (rank * (width % files)) % files;
            int start = 0;
            file = files - 1;
            for (int band = 0; band < files; band++)
            {
                int size = perBand + (((band - firstExtra + files) % files) < extra ? 1 : 0);
                if (position < start + size)
                {
                    file = band;
                    return;
                }

                start += size;
            }
        }

        private static bool Before(int rankA, int lateralA, int rankB, int lateralB)
        {
            if (rankA != rankB)
            {
                return rankA < rankB;
            }

            int innerA = lateralA < 0 ? -lateralA : lateralA;
            int innerB = lateralB < 0 ? -lateralB : lateralB;
            return innerA != innerB ? innerA < innerB : lateralA < lateralB;
        }

        /// <summary>World position of a local offset for an anchor at <paramref name="anchor"/> facing <paramref name="facing"/>.</summary>
        public static FixVec2 ToWorld(FixVec2 anchor, Angle facing, FixVec2 local)
        {
            Fix cos = FixMath.Cos(facing);
            Fix sin = FixMath.Sin(facing);

            // forward = (cos, sin); right = forward turned 90° clockwise = (sin, −cos).
            return new FixVec2(
                anchor.X + local.X * sin + local.Y * cos,
                anchor.Y - local.X * cos + local.Y * sin);
        }

        /// <summary>World position of <paramref name="slot"/>.</summary>
        public static FixVec2 SlotPosition(FormationDef formation, int count, int slot, FixVec2 anchor, Angle facing) =>
            ToWorld(anchor, facing, SlotOffset(formation, count, slot));
    }
}
