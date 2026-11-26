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
    /// What the player's hands mean for a Retainer (M3 step 10, battle-sim.md §13): which Retainer a click is on, what
    /// dropping a dragged Retainer does, and what a right-click does with one selected. Plain C#: builds commands, never
    /// touches the state.
    /// </summary>
    public static class RetainerOrders
    {
        /// <summary>The living Retainer of side <paramref name="side"/> nearest <paramref name="point"/>, within his body plus
        /// <see cref="SquadPicker.ClickSlop"/>, who takes his own orders — alone, or unchained (<paramref name="chains"/>;
        /// without it, every Retainer); or none. A click on him means him; on a chained one, his squad.</summary>
        public static EntityId At(BattleState state, Vector2 point, int side, RetainerChains? chains = null) =>
            FighterAt(state, point, f => f.IsRetainer && f.SideId == side && (chains == null || chains.OnHisOwn(f)));

        /// <summary>The living fighter not of side <paramref name="side"/> nearest <paramref name="point"/>, or none.</summary>
        public static EntityId EnemyAt(BattleState state, Vector2 point, int side) => FighterAt(state, point, f => f.SideId != side);

        /// <summary>
        /// Dropping a Retainer dragged from <paramref name="from"/> at <paramref name="to"/>: on a squad of his side that
        /// isn't his, he joins it; on open ground he leaves his squad (if in one) and walks there, facing the way he went;
        /// on his own squad, or a drag too short to mean anything, nothing.
        /// </summary>
        public static List<ICommand> Drop(BattleState state, EntityId retainerId, Vector2 from, Vector2 to, int side)
        {
            var commands = new List<ICommand>();
            Fighter? retainer = state.FindFighter(retainerId);
            if (retainer == null || (to - from).magnitude < OrderBuilder.DragThreshold)
            {
                return commands;
            }

            EntityId squad = SquadPicker.At(state, to, side);
            if (!squad.IsNone && squad == retainer.SquadId)
            {
                return commands;
            }

            if (!squad.IsNone)
            {
                commands.Add(new AttachRetainerCommand(side, retainerId, squad));
                return commands;
            }

            if (!retainer.SquadId.IsNone)
            {
                commands.Add(new DetachRetainerCommand(side, retainerId));
            }

            commands.Add(new MoveRetainerCommand(side, retainerId, to.ToFixVec2()));
            return commands;
        }

        /// <summary>
        /// A right-click at <paramref name="from"/> (released at <paramref name="to"/>) with a Retainer selected: on an enemy,
        /// he goes at him; on a squad of his side, he joins it; on open ground he walks there — facing the drag's way when
        /// it was a drag. In a squad, he leaves it first (except to join another).
        /// </summary>
        public static List<ICommand> Order(BattleState state, EntityId retainerId, Vector2 from, Vector2 to, int side)
        {
            var commands = new List<ICommand>();
            Fighter? retainer = state.FindFighter(retainerId);
            if (retainer == null)
            {
                return commands;
            }

            EntityId enemy = EnemyAt(state, from, side);
            EntityId squad = enemy.IsNone ? SquadPicker.At(state, from, side) : EntityId.None;
            if (!squad.IsNone)
            {
                if (squad != retainer.SquadId)
                {
                    commands.Add(new AttachRetainerCommand(side, retainerId, squad));
                }

                return commands;
            }

            if (!retainer.SquadId.IsNone)
            {
                commands.Add(new DetachRetainerCommand(side, retainerId));
            }

            if (!enemy.IsNone)
            {
                commands.Add(new RetainerAttackCommand(side, retainerId, enemy));
                return commands;
            }

            Vector2 drag = to - from;
            Angle? facing = drag.magnitude >= OrderBuilder.DragThreshold ? FixMath.Atan2(drag.y.ToFix(), drag.x.ToFix()) : (Angle?)null;
            commands.Add(new MoveRetainerCommand(side, retainerId, from.ToFixVec2(), facing));
            return commands;
        }

        private static EntityId FighterAt(BattleState state, Vector2 point, System.Func<Fighter, bool> which)
        {
            EntityId best = EntityId.None;
            float bestDistance = float.MaxValue;
            foreach (Fighter fighter in state.Fighters)
            {
                if (!fighter.IsAlive || !which(fighter))
                {
                    continue;
                }

                float distance = Vector2.Distance(point, fighter.Position.ToVector2());
                if (distance <= fighter.Radius.ToFloat() + SquadPicker.ClickSlop && distance < bestDistance)
                {
                    best = fighter.Id;
                    bestDistance = distance;
                }
            }

            return best;
        }
    }
}
