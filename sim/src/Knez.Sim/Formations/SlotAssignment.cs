using System.Collections.Generic;
using Knez.Sim.Numerics;
using Knez.Sim.Units;

namespace Knez.Sim.Formations
{
    /// <summary>
    /// Who stands in which slot (battle-sim.md §10, M1 step 7). Slots are stable; they change only when men fall (the
    /// gaps move to the rear rank: men step up from behind, decided 2026-11-21) or when the squad re-forms (each new slot,
    /// front first, takes the nearest man). Deterministic: ties go to the lower fighter id. Allocation-free; O(squad²)
    /// when something changes, a scan for the fallen otherwise (CleanupSystem checks every squad every tick).
    /// </summary>
    internal static class SlotAssignment
    {
        /// <summary>
        /// Drops the dead from <paramref name="squad"/> and closes the gaps they leave: each gap, front rank first, is filled
        /// by the nearest man (sideways) from the nearest rank behind that still has men, whose own place then becomes a gap
        /// in turn, so the gaps end up in the rear rank. The front keeps its width until fewer men are left than its files.
        /// Returns true when anyone fell.
        /// </summary>
        public static bool CloseRanks(BattleState state, Squad squad)
        {
            List<EntityId> members = squad.MemberList;
            int oldCount = members.Count;
            bool fell = false;
            for (int i = members.Count - 1; i >= 0; i--)
            {
                Fighter? fighter = state.FindFighter(members[i]);
                if (fighter == null || !fighter.IsAlive)
                {
                    if (fighter != null)
                    {
                        fighter.SquadId = EntityId.None;
                        fighter.SlotIndex = Fighter.NoSlot;
                    }

                    members.RemoveAt(i);
                    fell = true;
                }
            }

            if (!fell || members.Count == 0)
            {
                return fell;
            }

            int width = FormationLayout.Width(squad.Formation, squad.Frontage, oldCount);
            for (int gap = 0; gap < oldCount; gap++)
            {
                if (Holder(state, members, gap) != null)
                {
                    continue;
                }

                Place(width, oldCount, gap, out int gapRank, out int gapLateral);
                Fighter? best = null;
                int bestRank = int.MaxValue;
                int bestDistance = int.MaxValue;
                for (int m = 0; m < members.Count; m++)
                {
                    Fighter man = state.FindFighter(members[m])!;
                    Place(width, oldCount, man.SlotIndex, out int rank, out int lateral);
                    int distance = lateral > gapLateral ? lateral - gapLateral : gapLateral - lateral;
                    if (rank > gapRank && (rank < bestRank || (rank == bestRank && distance < bestDistance)))
                    {
                        best = man; // members are in id order, so ties keep the lower id
                        bestRank = rank;
                        bestDistance = distance;
                    }
                }

                if (best != null)
                {
                    best.SlotIndex = gap;
                    best.TurnLag = FixVec2.Zero; // his lag belonged to his old place (soft wheel, battle-sim.md §10)
                }
            }

            // Renumber 0, 1, 2, … in old slot order — rank by rank, left to right — so the gaps, now all at the back, drop
            // out and the layout centres what is left of the rear rank. Done in place: renumbered men are marked −2, −3, ….
            for (int next = 0; next < members.Count; next++)
            {
                Fighter? first = null;
                for (int m = 0; m < members.Count; m++)
                {
                    Fighter man = state.FindFighter(members[m])!;
                    if (man.SlotIndex >= 0 && (first == null || man.SlotIndex < first.SlotIndex))
                    {
                        first = man;
                    }
                }

                first!.SlotIndex = -(next + 2);
            }

            for (int m = 0; m < members.Count; m++)
            {
                Fighter man = state.FindFighter(members[m])!;
                man.SlotIndex = -man.SlotIndex - 2;
            }

            return true;
        }

        /// <summary>
        /// Gives every member of <paramref name="squad"/> a slot of its current layout (formation, frontage, narrowing,
        /// anchor and facing): front slots first, each to the nearest man not yet placed. Used when the squad changes
        /// formation or faces about.
        /// </summary>
        public static void Nearest(BattleState state, Squad squad)
        {
            List<EntityId> members = squad.MemberList;
            int count = members.Count;
            for (int m = 0; m < count; m++)
            {
                Fighter man = state.FindFighter(members[m])!;
                man.SlotIndex = Fighter.NoSlot;
                man.TurnLag = FixVec2.Zero; // the new place is picked from where he stands, so no lag carries over
            }

            int width = FormationLayout.Width(squad.Formation, squad.Frontage, count);
            for (int slot = 0; slot < count; slot++)
            {
                FixVec2 at = FormationLayout.ToWorld(squad.Anchor, squad.Facing,
                    FormationLayout.SlotOffsetAt(squad.Formation, width, count, slot, squad.Files));
                Fighter? best = null;
                Fix bestDistance = Fix.MaxValue;
                for (int m = 0; m < count; m++)
                {
                    Fighter man = state.FindFighter(members[m])!;
                    if (man.SlotIndex != Fighter.NoSlot)
                    {
                        continue;
                    }

                    Fix distance = (man.Position - at).Length;
                    if (distance < bestDistance)
                    {
                        best = man;
                        bestDistance = distance;
                    }
                }

                best!.SlotIndex = slot;
            }
        }

        private static Fighter? Holder(BattleState state, List<EntityId> members, int slot)
        {
            for (int m = 0; m < members.Count; m++)
            {
                Fighter man = state.FindFighter(members[m])!;
                if (man.SlotIndex == slot)
                {
                    return man;
                }
            }

            return null;
        }

        /// <summary>Rank and place across the front (in half spacings, 0 = middle) of <paramref name="slot"/> in the normal
        /// layout of <paramref name="count"/> men, <paramref name="width"/> wide.</summary>
        private static void Place(int width, int count, int slot, out int rank, out int lateral)
        {
            rank = slot / width;
            int inRank = width < count - (rank * width) ? width : count - (rank * width);
            lateral = (2 * (slot - (rank * width))) - (inRank - 1);
        }
    }
}
