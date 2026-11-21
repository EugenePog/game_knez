using Knez.Sim.Formations;
using Knez.Sim.Numerics;
using Knez.Sim.Units;

namespace Knez.Sim.Movement
{
    /// <summary>
    /// Where a member's formation slot is and how it moved this tick. Shared by steering (follow the slot) and movement
    /// (is the fighter keeping up with its slot or running?), so both see the same slot motion — including the part that
    /// comes from the squad turning, which can be faster than walking for outer slots.
    /// </summary>
    internal static class SlotTracking
    {
        /// <summary>The slot's offset from the anchor in local coordinates for <paramref name="fighter"/> in <paramref name="squad"/>.</summary>
        public static FixVec2 Offset(Squad squad, Fighter fighter) =>
            FormationLayout.SlotOffsetAt(squad.Formation, FormationLayout.Width(squad.Formation, squad.Frontage, squad.MemberList.Count),
                squad.MemberList.Count, fighter.SlotIndex, squad.Files);

        /// <summary>Front rank to rear rank of the squad as it stands now, metres (narrowed or not).</summary>
        public static Fix Depth(Squad squad) => Depth(squad, squad.Files);

        /// <summary>Front rank to rear rank of the squad in <paramref name="files"/> files (0 = its own width), metres.</summary>
        public static Fix Depth(Squad squad, int files)
        {
            int count = squad.MemberList.Count;
            if (count == 0)
            {
                return Fix.Zero;
            }

            int width = FormationLayout.Width(squad.Formation, squad.Frontage, count);
            int ranks = files > 0 && files < width
                ? FormationLayout.NarrowedRanks(width, count, files)
                : (count + width - 1) / width;
            return squad.Formation.RankSpacing * (ranks - 1);
        }

        /// <summary>The width of the squad's front as it stands now, slot centre to slot centre (narrowed or not).</summary>
        public static Fix FrontWidth(Squad squad)
        {
            int count = squad.MemberList.Count;
            if (count == 0)
            {
                return Fix.Zero;
            }

            int width = FormationLayout.Width(squad.Formation, squad.Frontage, count);
            int files = squad.Files > 0 && squad.Files < width ? squad.Files : width;
            return squad.Formation.Spacing * (files - 1);
        }

        /// <summary>The slot's world position now.</summary>
        public static FixVec2 Position(Squad squad, FixVec2 offset) => FormationLayout.ToWorld(squad.Anchor, squad.Facing, offset);

        /// <summary>Where the slot was a tick ago (before this tick's anchor move and turn).</summary>
        public static FixVec2 PreviousPosition(Squad squad, FixVec2 offset) =>
            FormationLayout.ToWorld(squad.Anchor - squad.Velocity, squad.Facing - Angle.FromRaw(squad.Spin), offset);

        /// <summary>How far the slot moved this tick, from the anchor's velocity and the front's spin.</summary>
        public static FixVec2 Motion(Squad squad, FixVec2 offset, FixVec2 slotNow)
        {
            if (squad.Velocity == FixVec2.Zero && squad.Spin == 0)
            {
                return FixVec2.Zero;
            }

            FixVec2 previous = FormationLayout.ToWorld(squad.Anchor - squad.Velocity, squad.Facing - Angle.FromRaw(squad.Spin), offset);
            return slotNow - previous;
        }
    }
}
