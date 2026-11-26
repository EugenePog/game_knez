#nullable enable
using System.Collections.Generic;
using Knez.Game.View;
using Knez.Sim;
using Knez.Sim.Commands;
using Knez.Sim.Numerics;
using Knez.Sim.Units;
using UnityEngine;
using EntityId = Knez.Sim.EntityId; // Unity 6 has a UnityEngine.EntityId too

namespace Knez.Game.Input
{
    /// <summary>
    /// Turns a right-click (or right-drag) into move commands for the selected squads. A click: each squad marches there
    /// and faces the way it marched. A drag from A to B: the squads face from A toward B. Several squads stand side by
    /// side across that facing, centred on A, their fronts <see cref="Gap"/> apart.
    /// </summary>
    public static class OrderBuilder
    {
        /// <summary>A drag shorter than this is a click: 0.6 m.</summary>
        public const float DragThreshold = 0.6f;

        /// <summary>Room between neighbouring squads ordered together: 2 m.</summary>
        public const float Gap = 2f;

        /// <summary>
        /// A right-click on an enemy man in a squad (M4 step 1): every selected squad attacks his squad. Empty when the
        /// click isn't on such a man — then it is a move (<see cref="Moves"/>).
        /// </summary>
        public static List<ICommand> Attacks(BattleState state, IReadOnlyList<EntityId> selected, Vector2 at, int side)
        {
            var commands = new List<ICommand>();
            EntityId enemyId = RetainerOrders.EnemyAt(state, at, side);
            Fighter? enemy = enemyId.IsNone ? null : state.FindFighter(enemyId);
            if (enemy == null || enemy.SquadId.IsNone)
            {
                return commands;
            }

            foreach (EntityId squad in selected)
            {
                commands.Add(new AttackSquadCommand(side, squad, enemy.SquadId));
            }

            return commands;
        }

        /// <summary>The commands, in selection (id) order, for side <paramref name="side"/>.</summary>
        public static List<ICommand> Moves(BattleState state, IReadOnlyList<EntityId> selected, Vector2 from, Vector2 to, int side)
        {
            var commands = new List<ICommand>(selected.Count);
            Vector2 drag = to - from;
            bool facingGiven = drag.magnitude >= DragThreshold;
            Angle? facing = facingGiven ? FixMath.Atan2(drag.y.ToFix(), drag.x.ToFix()) : (Angle?)null;
            Vector2 across = facingGiven ? new Vector2(drag.y, -drag.x).normalized : Vector2.right; // the squads' right

            // Widths of the fronts, to space the squads along the line through the click.
            var widths = new List<float>(selected.Count);
            float total = 0;
            foreach (EntityId id in selected)
            {
                Squad? squad = state.FindSquad(id);
                float width = squad == null ? 0 : Width(state, squad);
                widths.Add(width);
                total += width;
            }

            total += Gap * Mathf.Max(0, selected.Count - 1);
            float along = -total / 2;
            for (int i = 0; i < selected.Count; i++)
            {
                Vector2 centre = from + (across * (along + (widths[i] / 2)));
                along += widths[i] + Gap;
                commands.Add(new MoveSquadCommand(side, selected[i], centre.ToFixVec2(), facing));
            }

            return commands;
        }

        /// <summary>A squad's frontage, body edge to body edge, metres.</summary>
        private static float Width(BattleState state, Squad squad)
        {
            int files = Mathf.Min(squad.Frontage > 0 ? squad.Frontage : squad.Members.Count, squad.Members.Count);
            float radius = 0.3f;
            if (squad.Members.Count > 0)
            {
                radius = state.FindFighter(squad.Members[0])?.Radius.ToFloat() ?? radius;
            }

            return (squad.Formation.Spacing.ToFloat() * Mathf.Max(0, files - 1)) + (2 * radius);
        }
    }
}
