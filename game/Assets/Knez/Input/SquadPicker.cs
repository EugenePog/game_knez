#nullable enable
using System.Collections.Generic;
using Knez.Game.View;
using Knez.Sim;
using Knez.Sim.Units;
using UnityEngine;
using EntityId = Knez.Sim.EntityId; // Unity 6 has a UnityEngine.EntityId too

namespace Knez.Game.Input
{
    /// <summary>Which of the player's squads a click or a drag box means. Reads the state only.</summary>
    public static class SquadPicker
    {
        /// <summary>How far outside a man's body a click still picks him: 0.4 m.</summary>
        public const float ClickSlop = 0.4f;

        /// <summary>The squad of side <paramref name="side"/> whose living man is nearest <paramref name="point"/>, within his
        /// body plus <see cref="ClickSlop"/>; or none.</summary>
        public static EntityId At(BattleState state, Vector2 point, int side)
        {
            EntityId best = EntityId.None;
            float bestDistance = float.MaxValue;
            foreach (Fighter fighter in state.Fighters)
            {
                if (!fighter.IsAlive || fighter.SideId != side || fighter.SquadId.IsNone)
                {
                    continue;
                }

                float distance = Vector2.Distance(point, fighter.Position.ToVector2());
                if (distance <= fighter.Radius.ToFloat() + ClickSlop && distance < bestDistance)
                {
                    best = fighter.SquadId;
                    bestDistance = distance;
                }
            }

            return best;
        }

        /// <summary>The squads of side <paramref name="side"/> with a living man inside <paramref name="box"/>, by id.</summary>
        public static List<EntityId> InBox(BattleState state, Rect box, int side)
        {
            var found = new List<EntityId>();
            foreach (Fighter fighter in state.Fighters)
            {
                if (fighter.IsAlive && fighter.SideId == side && !fighter.SquadId.IsNone
                    && box.Contains(fighter.Position.ToVector2()) && !found.Contains(fighter.SquadId))
                {
                    found.Add(fighter.SquadId);
                }
            }

            found.Sort((a, b) => a.Value.CompareTo(b.Value));
            return found;
        }
    }
}
