using System.Collections.Generic;
using Knez.Sim.Abilities;
using Knez.Sim.Combat;
using Knez.Sim.Commands;
using Knez.Sim.Defs;
using Knez.Sim.Events;
using Knez.Sim.Formations;
using Knez.Sim.Morale;
using Knez.Sim.Numerics;
using Knez.Sim.Orders;
using Knez.Sim.Units;

namespace Knez.Sim.Ai
{
    /// <summary>
    /// Tick stage 2: the enemy AI (battle-sim.md §14, M4 step 2; decided by the user 2026-11-26). For each side the scenario
    /// says it plays, once a second per squad (staggered by id, as stances are):
    /// <list type="bullet">
    /// <item>Strength is men in the fight, a heavily wounded man counting half. Below a third of the enemy's the side
    /// leaves the field: its squads march off its edge and never come back (<see cref="SquadAiState.Withdraw"/>).</item>
    /// <item>A broken squad regroups once it re-forms; a squad not in melee whose men average under 400 morale falls back
    /// 20 m toward its nearest ally (or its start); either stands until they are back to 600, falling back again while an
    /// enemy is within those 20 m.</item>
    /// <item>At 0.8 of the enemy's strength or more it attacks, else it holds its ground in Advance; once attacking, it holds
    /// only below 0.7 (a hysteresis, so hovering odds don't start and stop it every second). It keeps its target
    /// while that stands; a new one is the nearest enemy squad, 10 m nearer if an ally fights it, at most two on one while
    /// another stands free, plus up to 5 m at random (the <c>ai</c> stream). The second squad on a target, standing in its
    /// front, first marches round to its open flank.</item>
    /// </list>
    /// Its squads get the player's own orders (attack, stance, march), given inside the sim. Every fifth tick (staggered
    /// by id) its Retainers use their built abilities when the moment is right.
    /// </summary>
    public sealed class AiSystem : ISimSystem
    {
        private const int Whole = 1000;

        /// <summary>Retainers look for the moment for their ability this often, in ticks.</summary>
        private const int AbilityEvery = 5;

        /// <summary>A flank march is re-aimed when its point has moved this far (metres), like an attack.</summary>
        private static readonly Fix Reaim = Fix.FromInt(2);

        /// <summary>A squad falling back toward an ally stops this short of the ally's anchor (metres), clear of its ranks.</summary>
        private static readonly Fix AllyClearance = Fix.FromInt(8);

        /// <inheritdoc/>
        public void Execute(BattleState state, TickContext context)
        {
            ScenarioDef scenario = state.Scenario;
            if (scenario.AiSides.Count == 0)
            {
                return;
            }

            List<Squad> squads = state.SquadList;
            for (int i = 0; i < squads.Count; i++)
            {
                Squad squad = squads[i];
                if (scenario.PlaysAi(squad.SideId) && (state.Tick + squad.Id.Value) % BattleState.TicksPerSecond == 0)
                {
                    Decide(state, context, squad);
                }
            }

            List<Fighter> fighters = state.FighterList;
            for (int i = 0; i < fighters.Count; i++)
            {
                Fighter man = fighters[i];
                if (man.IsRetainer && scenario.PlaysAi(man.SideId) && (state.Tick + man.Id.Value) % AbilityEvery == 0)
                {
                    Ability(state, man);
                }
            }
        }

        private static void Decide(BattleState state, TickContext context, Squad squad)
        {
            if (!MoveSquadCommand.HasLivingMember(state, squad))
            {
                return; // nobody left to order
            }

            AiDef ai = state.Scenario.Ai;
            int own = Strength(state, squad.SideId, own: true);
            int enemy = Strength(state, squad.SideId, own: false);
            if (squad.AiState == SquadAiState.Withdraw || SideLeaving(state, squad.SideId) || (long)own * Whole < (long)enemy * ai.LeaveBelow)
            {
                Withdraw(state, squad);
                return;
            }

            if (squad.Broken)
            {
                squad.Stance = SquadStance.Hold;
                Set(state, squad, SquadAiState.Regroup, EntityId.None); // carried out once it re-forms
                return;
            }

            bool melee = OrderSystem.InMelee(state, squad);
            int morale = AverageMorale(state, squad, ai, out int ready);
            if (squad.AiState == SquadAiState.Regroup)
            {
                if (morale < ready)
                {
                    if (squad.Order.Kind == SquadOrderKind.None && !melee && EnemyGap(state, squad, ai.RegroupDistance) <= ai.RegroupDistance)
                    {
                        FallBack(state, squad); // still too near: further back
                    }

                    return;
                }
            }
            else if (!melee && morale < ai.RegroupMorale)
            {
                squad.Stance = SquadStance.Hold;
                Set(state, squad, SquadAiState.Regroup, EntityId.None);
                FallBack(state, squad);
                return;
            }

            squad.Stance = SquadStance.Advance;
            bool attacking = squad.AiState == SquadAiState.Engage || squad.AiState == SquadAiState.Advance;
            if ((long)own * Whole < (long)enemy * (attacking ? ai.HoldBelow : ai.AttackAt))
            {
                if (squad.AiState != SquadAiState.Hold)
                {
                    if (!melee)
                    {
                        Stop(state, squad); // holds where it stands; Advance meets whoever comes close
                    }

                    Set(state, squad, SquadAiState.Hold, EntityId.None);
                }
                else if (!melee && !squad.AttackTarget.IsNone)
                {
                    // An attack kept from a melee, or its stance's own: dropped once that squad is beyond the advance range,
                    // so a holding squad doesn't follow a retreating enemy across the field.
                    Fix range = state.Scenario.Movement.AdvanceRange;
                    Squad? held = state.FindSquad(squad.AttackTarget);
                    if (held == null || SquadGap(state, squad, held, range) > range)
                    {
                        Stop(state, squad);
                    }
                }

                return;
            }

            Squad? target;
            if (melee)
            {
                target = Live(state, squad.AttackTarget) ?? Foe(state, squad); // in a fight: it fights what it has
            }
            else if ((squad.AiState == SquadAiState.Engage || squad.AiState == SquadAiState.Advance) && Live(state, squad.AiTarget) != null)
            {
                target = Live(state, squad.AiTarget); // it keeps its target while that stands
            }
            else
            {
                target = Choose(state, context, squad); // its own rules, even over an attack its stance began
            }

            if (target == null)
            {
                if (squad.AiState != SquadAiState.Idle && !melee)
                {
                    Stop(state, squad);
                }

                Set(state, squad, SquadAiState.Idle, EntityId.None);
                return;
            }

            bool arrived = squad.AiState == SquadAiState.Advance && squad.AiTarget == target.Id && squad.Order.Kind == SquadOrderKind.None;
            if (!melee && !arrived && squad.AttackTarget != target.Id && InFront(state, target, squad.Anchor)
                && Fighting(state, squad, target) > 0)
            {
                // The second squad on it, standing in its front: round to its open flank first.
                FixVec2 point = FlankPoint(state, squad, target, ai);
                Angle? facing = FacingTo(point, target.Anchor);
                FixVec2 fitted = MoveSquadCommand.Fit(state, squad, point, facing, out _);
                bool fresh = squad.AiState != SquadAiState.Advance || squad.AiTarget != target.Id || squad.Order.Kind != SquadOrderKind.Move;
                if (fresh || (Settled(squad) && (squad.Order.Target - fitted).Length > Reaim))
                {
                    squad.AttackTarget = EntityId.None;
                    OrderSystem.EndChase(state, squad, reform: false);
                    MoveSquadCommand.March(state, squad, point, facing);
                }

                Set(state, squad, SquadAiState.Advance, target.Id);
                return;
            }

            if (squad.AttackTarget != target.Id)
            {
                OrderSystem.EndChase(state, squad, reform: false);
                OrderSystem.Attack(state, squad, target);
            }

            Set(state, squad, SquadAiState.Engage, target.Id);
        }

        /// <summary>
        /// Its side leaves the field: it marches off straight to its edge, facing it, once — when it starts leaving, if it
        /// isn't broken (a broken one's men are running for the same edge). When that march ends (at the edge, or as near as
        /// its route gets) or it re-forms, its men walk the rest one by one (steering), each leaving at the edge.
        /// </summary>
        private static void Withdraw(BattleState state, Squad squad)
        {
            if (squad.AiState == SquadAiState.Withdraw)
            {
                return;
            }

            squad.Leaving = true;
            squad.Stance = SquadStance.Hold;
            squad.AttackTarget = EntityId.None;
            OrderSystem.EndChase(state, squad, reform: false);
            Set(state, squad, SquadAiState.Withdraw, EntityId.None);
            if (!squad.Broken && OrderSystem.Standing(state, squad))
            {
                FixVec2 exit = ExitPoint(state, squad, out Angle facing);
                MoveSquadCommand.March(state, squad, exit, facing);
            }
        }

        /// <summary>
        /// Falls back <see cref="AiDef.RegroupDistance"/> toward its nearest ally (not broken, leaving or in melee), stopping
        /// short of its ranks, or else toward its own start; facing the nearest enemy squad. No march if that is under a metre.
        /// </summary>
        private static void FallBack(BattleState state, Squad squad)
        {
            Squad? ally = NearestAlly(state, squad);
            FixVec2 to = ally != null ? ally.Anchor : Start(state, squad);
            FixVec2 delta = to - squad.Anchor;
            Fix distance = delta.Length;
            Fix step = FixMath.Min(state.Scenario.Ai.RegroupDistance, ally != null ? distance - AllyClearance : distance);
            squad.AttackTarget = EntityId.None;
            OrderSystem.EndChase(state, squad, reform: false);
            if (step < Fix.One)
            {
                Stop(state, squad);
                return;
            }

            FixVec2 point = squad.Anchor + (delta * (step / distance));
            Squad? enemy = NearestEnemySquad(state, squad, point);
            MoveSquadCommand.March(state, squad, point, enemy != null ? FacingTo(point, enemy.Anchor) : squad.Facing);
        }

        /// <summary>
        /// The enemy squad it should go for: the nearest (anchor to the target's aim point), 10 m nearer if an ally already
        /// fights it, plus up to <see cref="AiDef.Jitter"/> at random; one that <see cref="AiDef.MaxOnTarget"/> allies fight
        /// is passed over while another stands free. Ties to the lower id.
        /// </summary>
        private static Squad? Choose(BattleState state, TickContext context, Squad squad)
        {
            AiDef ai = state.Scenario.Ai;
            List<Squad> squads = state.SquadList;
            bool free = false;
            for (int s = 0; s < squads.Count && !free; s++)
            {
                free = Candidate(state, squad, squads[s]) && Fighting(state, squad, squads[s]) == 0;
            }

            SimRng rng = context.Rng(RngStream.Ai);
            Squad? best = null;
            Fix bestScore = Fix.Zero;
            for (int s = 0; s < squads.Count; s++)
            {
                Squad other = squads[s];
                if (!Candidate(state, squad, other))
                {
                    continue;
                }

                int on = Fighting(state, squad, other);
                if (on >= ai.MaxOnTarget && free)
                {
                    continue;
                }

                Fix score = (OrderSystem.AimPoint(state, other) - squad.Anchor).Length;
                if (on > 0)
                {
                    score = score - ai.HelpBonus;
                }

                if (ai.Jitter > Fix.Zero)
                {
                    score = score + Fix.FromRaw(rng.NextInt((int)ai.Jitter.Raw + 1));
                }

                if (best == null || score < bestScore)
                {
                    best = other;
                    bestScore = score;
                }
            }

            return best;
        }

        // An enemy squad that can be attacked: not broken, someone in it in the fight.
        private static bool Candidate(BattleState state, Squad squad, Squad other) =>
            other.SideId != squad.SideId && !other.Broken && OrderSystem.Standing(state, other);

        /// <summary>How many squads of <paramref name="squad"/>'s side besides it fight <paramref name="target"/>: attack it,
        /// go for it on the AI's orders, or have a man in melee with one of its men.</summary>
        private static int Fighting(BattleState state, Squad squad, Squad target)
        {
            int count = 0;
            List<Squad> squads = state.SquadList;
            for (int s = 0; s < squads.Count; s++)
            {
                Squad ally = squads[s];
                if (ally == squad || ally.SideId != squad.SideId || ally.Broken || !OrderSystem.Standing(state, ally))
                {
                    continue;
                }

                bool going = ally.AttackTarget == target.Id
                    || ((ally.AiState == SquadAiState.Engage || ally.AiState == SquadAiState.Advance) && ally.AiTarget == target.Id);
                if (going || InMeleeWith(state, ally, target))
                {
                    count++;
                }
            }

            return count;
        }

        // Whether one of the squad's men in the fight is engaged with a man of the target squad who isn't running.
        private static bool InMeleeWith(BattleState state, Squad squad, Squad target)
        {
            List<EntityId> members = squad.MemberList;
            for (int m = 0; m < members.Count; m++)
            {
                Fighter? man = state.FindFighter(members[m]);
                if (man == null || !man.IsAlive || man.IsRouting || !man.IsEngaged)
                {
                    continue;
                }

                Fighter? enemy = state.FindFighter(man.Target);
                if (enemy != null && !enemy.IsRouting && enemy.SquadId == target.Id)
                {
                    return true;
                }
            }

            return false;
        }

        // The squad of the first enemy (by its men's id order) one of its men is fighting, if that squad stands.
        private static Squad? Foe(BattleState state, Squad squad)
        {
            List<EntityId> members = squad.MemberList;
            for (int m = 0; m < members.Count; m++)
            {
                Fighter? man = state.FindFighter(members[m]);
                if (man == null || !man.IsAlive || man.IsRouting || !man.IsEngaged)
                {
                    continue;
                }

                Fighter? enemy = state.FindFighter(man.Target);
                if (enemy != null && !enemy.IsRouting && !enemy.SquadId.IsNone)
                {
                    Squad? foe = Live(state, enemy.SquadId);
                    if (foe != null)
                    {
                        return foe;
                    }
                }
            }

            return null;
        }

        // The squad with this id, if it is not broken and has someone in the fight.
        private static Squad? Live(BattleState state, EntityId id)
        {
            if (id.IsNone)
            {
                return null;
            }

            Squad? squad = state.FindSquad(id);
            return squad != null && !squad.Broken && OrderSystem.Standing(state, squad) ? squad : null;
        }

        /// <summary>Whether <paramref name="point"/> lies in <paramref name="target"/>'s front arc (the combat front, 60°).</summary>
        private static bool InFront(BattleState state, Squad target, FixVec2 point)
        {
            FixVec2 to = point - target.Anchor;
            if (to == FixVec2.Zero)
            {
                return true;
            }

            int off = target.Facing.DeltaTo(FixMath.Atan2(to.Y, to.X));
            return (off < 0 ? -off : off) <= state.Scenario.Combat.FrontArcUnits;
        }

        /// <summary>
        /// The point <see cref="AiDef.FlankOffset"/> beyond <paramref name="target"/>'s flank (its front rank's end), on the
        /// side away from the allies already fighting it; when they are as near one side as the other (head on), the side
        /// nearer <paramref name="squad"/>.
        /// </summary>
        private static FixVec2 FlankPoint(BattleState state, Squad squad, Squad target, AiDef ai)
        {
            int count = target.MemberList.Count;
            int files = FormationLayout.Width(target.Formation, target.Frontage, count);
            Fix reach = (target.Formation.Spacing * (files - 1) / 2) + ai.FlankOffset;
            FixVec2 right = FormationLayout.ToWorld(target.Anchor, target.Facing, new FixVec2(reach, Fix.Zero));
            FixVec2 left = FormationLayout.ToWorld(target.Anchor, target.Facing, new FixVec2(-reach, Fix.Zero));
            Fix rightAllies = AllyDistance(state, squad, target, right);
            Fix leftAllies = AllyDistance(state, squad, target, left);
            if (FixMath.Abs(rightAllies - leftAllies) > Fix.One)
            {
                return rightAllies > leftAllies ? right : left;
            }

            return (right - squad.Anchor).LengthSquared <= (left - squad.Anchor).LengthSquared ? right : left;
        }

        // The distance from a point to the nearest anchor of an ally fighting the target.
        private static Fix AllyDistance(BattleState state, Squad squad, Squad target, FixVec2 point)
        {
            Fix best = Fix.MaxValue;
            List<Squad> squads = state.SquadList;
            for (int s = 0; s < squads.Count; s++)
            {
                Squad ally = squads[s];
                if (ally == squad || ally.SideId != squad.SideId || ally.Broken || !OrderSystem.Standing(state, ally))
                {
                    continue;
                }

                bool going = ally.AttackTarget == target.Id
                    || ((ally.AiState == SquadAiState.Engage || ally.AiState == SquadAiState.Advance) && ally.AiTarget == target.Id);
                if (going || InMeleeWith(state, ally, target))
                {
                    Fix d = (ally.Anchor - point).Length;
                    best = d < best ? d : best;
                }
            }

            return best;
        }

        // The nearest allied squad to fall back toward: standing, not broken, not leaving, not in melee; ties to the lower id.
        private static Squad? NearestAlly(BattleState state, Squad squad)
        {
            Squad? best = null;
            Fix bestDistance = Fix.Zero;
            List<Squad> squads = state.SquadList;
            for (int s = 0; s < squads.Count; s++)
            {
                Squad ally = squads[s];
                if (ally == squad || ally.SideId != squad.SideId || ally.Broken || ally.Leaving || !OrderSystem.Standing(state, ally)
                    || OrderSystem.InMelee(state, ally))
                {
                    continue;
                }

                Fix d = (ally.Anchor - squad.Anchor).LengthSquared;
                if (best == null || d < bestDistance)
                {
                    best = ally;
                    bestDistance = d;
                }
            }

            return best;
        }

        // The nearest enemy squad (anchor to anchor) that stands, from a point; ties to the lower id.
        private static Squad? NearestEnemySquad(BattleState state, Squad squad, FixVec2 point)
        {
            Squad? best = null;
            Fix bestDistance = Fix.Zero;
            List<Squad> squads = state.SquadList;
            for (int s = 0; s < squads.Count; s++)
            {
                Squad other = squads[s];
                if (!Candidate(state, squad, other))
                {
                    continue;
                }

                Fix d = (other.Anchor - point).LengthSquared;
                if (best == null || d < bestDistance)
                {
                    best = other;
                    bestDistance = d;
                }
            }

            return best;
        }

        // Where the squad started (its scenario entry, by its unique name).
        private static FixVec2 Start(BattleState state, Squad squad)
        {
            IReadOnlyList<SquadSpawnDef> spawns = state.Scenario.Squads;
            for (int i = 0; i < spawns.Count; i++)
            {
                if (string.Equals(spawns[i].Name, squad.Name, System.StringComparison.Ordinal))
                {
                    return spawns[i].Position;
                }
            }

            return squad.Anchor;
        }

        // Straight across from its anchor to its side's edge (the edge its runners make for), facing it.
        private static FixVec2 ExitPoint(BattleState state, Squad squad, out Angle facing)
        {
            facing = MoraleRules.EdgeFacing(state.Scenario, squad.SideId);
            MapDef map = state.Scenario.Map;
            switch (MoraleRules.Edge(state.Scenario, squad.SideId))
            {
                case 0: return new FixVec2(squad.Anchor.X, Fix.Zero);
                case 1: return new FixVec2(squad.Anchor.X, map.Height);
                case 2: return new FixVec2(Fix.Zero, squad.Anchor.Y);
                default: return new FixVec2(map.Width, squad.Anchor.Y);
            }
        }

        // Not stepping back out of a melee, regrouping for a gap or waiting for a route: a new march would cancel any of
        // them (the attack's re-aim waits for the same, battle-sim.md §10).
        private static bool Settled(Squad squad) => !squad.Withdrawing && squad.RegroupSince < 0 && squad.PathRequestTick < 0;

        private static Angle FacingTo(FixVec2 from, FixVec2 to)
        {
            FixVec2 d = to - from;
            return d == FixVec2.Zero ? Angle.FromDegrees(0) : FixMath.Atan2(d.Y, d.X);
        }

        // Stops where it stands: no order, no attack.
        private static void Stop(BattleState state, Squad squad)
        {
            squad.AttackTarget = EntityId.None;
            if (squad.Order.Kind != SquadOrderKind.None)
            {
                squad.Order = SquadOrder.None;
                squad.ClearPath();
            }
        }

        private static void Set(BattleState state, Squad squad, SquadAiState to, EntityId target)
        {
            if (squad.AiState == to && squad.AiTarget == target)
            {
                return;
            }

            state.AddEvent(new SquadAiChanged(state.Tick, squad.Id, squad.AiState, to, target));
            if (squad.AiState != to)
            {
                squad.AiSince = state.Tick;
            }

            squad.AiState = to;
            squad.AiTarget = target;
        }

        // Whether a squad of the side already leaves the field: once one goes, all go.
        private static bool SideLeaving(BattleState state, int side)
        {
            List<Squad> squads = state.SquadList;
            for (int s = 0; s < squads.Count; s++)
            {
                if (squads[s].SideId == side && squads[s].AiState == SquadAiState.Withdraw)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Men in the fight (alive, not running) of the side, or of every other side; a heavily wounded man counts
        /// half (in halves: 2 a man).</summary>
        private static int Strength(BattleState state, int side, bool own)
        {
            int total = 0;
            List<Fighter> fighters = state.FighterList;
            for (int i = 0; i < fighters.Count; i++)
            {
                Fighter man = fighters[i];
                if (man.IsAlive && !man.IsRouting && (man.SideId == side) == own)
                {
                    total += man.Wound == WoundLevel.Heavy ? 1 : 2;
                }
            }

            return total;
        }

        /// <summary>The average morale of the squad's men in the fight, and the average it counts as ready at:
        /// <see cref="AiDef.ReadyMorale"/>, or what they started with if less (they recover only that far).</summary>
        private static int AverageMorale(BattleState state, Squad squad, AiDef ai, out int ready)
        {
            long morale = 0;
            long start = 0;
            int n = 0;
            List<EntityId> members = squad.MemberList;
            for (int m = 0; m < members.Count; m++)
            {
                Fighter? man = state.FindFighter(members[m]);
                if (man != null && man.IsAlive && !man.IsRouting)
                {
                    morale += man.Morale;
                    start += man.Experience.Morale;
                    n++;
                }
            }

            if (n == 0)
            {
                ready = ai.ReadyMorale;
                return Whole;
            }

            int startAverage = (int)(start / n);
            ready = startAverage < ai.ReadyMorale ? startAverage : ai.ReadyMorale;
            return (int)(morale / n);
        }

        // ---- Abilities ----

        /// <summary>
        /// An AI Retainer uses his built ability when the moment is right (decided by the user 2026-11-26): battle cry with at
        /// least <see cref="AiDef.CryEnemies"/> enemy men in the fight within its radius and his squad in melee or an enemy
        /// within <see cref="AiDef.CryContact"/>; close ranks with his squad in melee or a man of an enemy squad attacking it
        /// within <see cref="AiDef.CloseRanksRange"/>; stand to the death with his squad in melee and
        /// <see cref="AiDef.StandShare"/> of its living men wavering or running. The abilities not built yet: never.
        /// </summary>
        private static void Ability(BattleState state, Fighter man)
        {
            if (!man.IsAlive || man.IsRouting)
            {
                return;
            }

            RetainerPart part = man.Retainer!.Value;
            AbilityDef ability = part.Trait.Ability;
            if (!ability.Built || state.Tick < part.AbilityReadyTick)
            {
                return;
            }

            Squad? squad = man.SquadId.IsNone ? null : state.FindSquad(man.SquadId);
            if (ability.OnSquad && (squad == null || squad.Broken))
            {
                return; // the command refuses it too
            }

            AiDef ai = state.Scenario.Ai;
            bool use;
            switch (ability.Kind)
            {
                case AbilityKind.BattleCry:
                    use = EnemiesWithin(state, man, ability.Radius) >= ai.CryEnemies
                        && (squad != null
                            ? OrderSystem.InMelee(state, squad) || EnemyGap(state, squad, ai.CryContact) <= ai.CryContact
                            : man.IsEngaged || ManGap(state, man, ai.CryContact) <= ai.CryContact);
                    break;
                case AbilityKind.CloseRanks:
                    use = OrderSystem.InMelee(state, squad!) || AttackerWithin(state, squad!, ai.CloseRanksRange);
                    break;
                case AbilityKind.StandToTheDeath:
                    use = OrderSystem.InMelee(state, squad!) && Shaky(state, squad!, ai.StandShare);
                    break;
                default:
                    use = false;
                    break;
            }

            if (use)
            {
                AbilityRules.Use(state, man);
            }
        }

        // Enemy men in the fight within the radius of a man, centre to centre (as the battle cry reaches).
        private static int EnemiesWithin(BattleState state, Fighter man, Fix radius)
        {
            Fix reach = radius * radius;
            int count = 0;
            List<Fighter> fighters = state.FighterList;
            for (int i = 0; i < fighters.Count; i++)
            {
                Fighter other = fighters[i];
                if (other.IsAlive && !other.IsRouting && other.SideId != man.SideId)
                {
                    FixVec2 to = other.Position - man.Position;
                    if (FixMath.Abs(to.X) <= radius && FixMath.Abs(to.Y) <= radius && to.LengthSquared <= reach)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        // Whether a man of an enemy squad attacking this one (its order, or the AI's) is within range of its men, edge to edge.
        private static bool AttackerWithin(BattleState state, Squad squad, Fix range)
        {
            List<Squad> squads = state.SquadList;
            for (int s = 0; s < squads.Count; s++)
            {
                Squad enemy = squads[s];
                if (enemy.SideId == squad.SideId || enemy.Broken)
                {
                    continue;
                }

                bool attacking = enemy.AttackTarget == squad.Id
                    || ((enemy.AiState == SquadAiState.Engage || enemy.AiState == SquadAiState.Advance) && enemy.AiTarget == squad.Id);
                if (attacking && SquadGap(state, squad, enemy, range) <= range)
                {
                    return true;
                }
            }

            return false;
        }

        // Whether at least the share (‰) of the squad's living men are wavering or running.
        private static bool Shaky(BattleState state, Squad squad, int share)
        {
            int living = 0;
            int shaky = 0;
            List<EntityId> members = squad.MemberList;
            for (int m = 0; m < members.Count; m++)
            {
                Fighter? man = state.FindFighter(members[m]);
                if (man == null || !man.IsAlive)
                {
                    continue;
                }

                living++;
                if (man.MoraleState == MoraleState.Wavering || man.MoraleState == MoraleState.Routing)
                {
                    shaky++;
                }
            }

            return living > 0 && (long)shaky * Whole >= (long)living * share;
        }

        // ---- Distances, edge to edge; past range counts as range + 1 m, so no square root for the far ones ----

        // From the squad's men in the fight to the nearest enemy man in the fight (squads and Retainers alone alike).
        private static Fix EnemyGap(BattleState state, Squad squad, Fix range)
        {
            Fix best = range + Fix.One;
            List<EntityId> members = squad.MemberList;
            for (int m = 0; m < members.Count; m++)
            {
                Fighter? man = state.FindFighter(members[m]);
                if (man != null && man.IsAlive && !man.IsRouting)
                {
                    Fix gap = ManGap(state, man, best);
                    best = gap < best ? gap : best;
                }
            }

            return best;
        }

        // From a man to the nearest enemy man in the fight.
        private static Fix ManGap(BattleState state, Fighter man, Fix range)
        {
            Fix best = range + Fix.One;
            List<Fighter> fighters = state.FighterList;
            for (int i = 0; i < fighters.Count; i++)
            {
                Fighter enemy = fighters[i];
                if (enemy.IsAlive && !enemy.IsRouting && enemy.SideId != man.SideId)
                {
                    best = Closer(man, enemy, best);
                }
            }

            return best;
        }

        // From the squad's men in the fight to the other squad's men in the fight.
        private static Fix SquadGap(BattleState state, Squad squad, Squad other, Fix range)
        {
            Fix best = range + Fix.One;
            List<EntityId> members = squad.MemberList;
            List<EntityId> others = other.MemberList;
            for (int m = 0; m < members.Count; m++)
            {
                Fighter? man = state.FindFighter(members[m]);
                if (man == null || !man.IsAlive || man.IsRouting)
                {
                    continue;
                }

                for (int o = 0; o < others.Count; o++)
                {
                    Fighter? enemy = state.FindFighter(others[o]);
                    if (enemy != null && enemy.IsAlive && !enemy.IsRouting)
                    {
                        best = Closer(man, enemy, best);
                    }
                }
            }

            return best;
        }

        private static Fix Closer(Fighter man, Fighter enemy, Fix best)
        {
            Fix bodies = man.Radius + enemy.Radius;
            Fix limit = best + bodies;
            FixVec2 to = enemy.Position - man.Position;
            if (FixMath.Abs(to.X) > limit || FixMath.Abs(to.Y) > limit)
            {
                return best;
            }

            Fix gap = to.Length - bodies;
            return gap < best ? gap : best;
        }
    }
}
