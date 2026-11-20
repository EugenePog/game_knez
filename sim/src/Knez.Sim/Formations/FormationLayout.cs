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
        public static FixVec2 SlotOffset(FormationDef formation, int count, int slot)
        {
            if (slot < 0 || slot >= count)
            {
                throw new ArgumentOutOfRangeException(nameof(slot), $"slot {slot} is outside 0..{count - 1}.");
            }

            int width = Width(formation, count);
            int rank = slot / width;
            int file = slot - rank * width;
            int inRank = Math.Min(width, count - rank * width);

            // Centred: files sit at (2·file − (inRank − 1)) / 2 spacings from the middle. Fix / int truncates toward
            // zero, so mirrored files get exactly mirrored offsets.
            Fix x = formation.Spacing * (2 * file - (inRank - 1)) / 2;
            Fix y = -(formation.RankSpacing * rank);
            return new FixVec2(x, y);
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
