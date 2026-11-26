using System.Collections.Generic;
using Knez.Sim.Numerics;
using Knez.Sim.Units;

namespace Knez.Sim.Formations
{
    /// <summary>
    /// A Retainer joining or leaving a squad (battle-sim.md §13, M3 step 10). Joining lays the formation out for one more
    /// man (a formation of ranks may widen): he takes the front-rank slot nearest him — at spawn, nearest the anchor — ties
    /// to the lower slot; then every other slot, front first, takes the nearest man not yet placed, as on re-forming; turn
    /// lag resets. Leaving lays it out for one fewer, the same way. <see cref="Squad.StartCount"/> grows by one on joining
    /// and shrinks by one on leaving alive, so losses count against the men the squad has.
    /// </summary>
    internal static class SquadMembership
    {
        /// <summary>Puts <paramref name="retainer"/> (standing alone) into <paramref name="squad"/>'s front rank.
        /// <paramref name="atSpawn"/>: the battle is being set up — every man then stands in his new slot.</summary>
        public static void Join(BattleState state, Squad squad, Fighter retainer, bool atSpawn)
        {
            List<EntityId> members = squad.MemberList;
            int at = 0;
            while (at < members.Count && members[at].Value < retainer.Id.Value)
            {
                at++;
            }

            members.Insert(at, retainer.Id); // members stay sorted by id
            retainer.SquadId = squad.Id;
            retainer.LoneOrder = Orders.LoneOrder.None;
            squad.StartCount++;
            int count = members.Count;
            squad.Frontage = FormationLayout.Width(squad.Formation, count);
            int width = FormationLayout.Width(squad.Formation, squad.Frontage, count);
            FixVec2 from = atSpawn ? squad.Anchor : retainer.Position;
            int front = width < count ? width : count;
            int best = 0;
            Fix bestDistance = Fix.MaxValue;
            for (int slot = 0; slot < front; slot++)
            {
                Fix distance = (SlotPosition(squad, width, count, slot) - from).Length;
                if (distance < bestDistance)
                {
                    best = slot; // ascending: ties keep the lower slot
                    bestDistance = distance;
                }
            }

            if (atSpawn)
            {
                retainer.Position = SlotPosition(squad, width, count, best);
                retainer.Facing = squad.Facing;
            }

            SlotAssignment.Nearest(state, squad, retainer, best);
            if (atSpawn)
            {
                // The battle starts formed up: everyone stands in his place of the new layout.
                for (int m = 0; m < members.Count; m++)
                {
                    Fighter man = state.FindFighter(members[m])!;
                    man.Position = SlotPosition(squad, width, count, man.SlotIndex);
                    man.Facing = squad.Facing;
                }
            }
        }

        /// <summary>Takes <paramref name="retainer"/> out of his squad; he stands alone where he is, with no order.</summary>
        public static void Leave(BattleState state, Squad squad, Fighter retainer)
        {
            squad.MemberList.Remove(retainer.Id);
            retainer.SquadId = EntityId.None;
            retainer.SlotIndex = Fighter.NoSlot;
            retainer.TurnLag = FixVec2.Zero;
            retainer.LoneOrder = Orders.LoneOrder.None;
            squad.StartCount--;
            int count = squad.MemberList.Count;
            if (count == 0)
            {
                return;
            }

            squad.Frontage = FormationLayout.Width(squad.Formation, count);
            SlotAssignment.Nearest(state, squad);
        }

        private static FixVec2 SlotPosition(Squad squad, int width, int count, int slot) =>
            FormationLayout.ToWorld(squad.Anchor, squad.Facing, FormationLayout.SlotOffsetAt(squad.Formation, width, count, slot, squad.Files));
    }
}
