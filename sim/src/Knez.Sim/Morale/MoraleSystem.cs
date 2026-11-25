using System.Collections.Generic;
using Knez.Sim.Combat;
using Knez.Sim.Defs;
using Knez.Sim.Events;
using Knez.Sim.Formations;
using Knez.Sim.Numerics;
using Knez.Sim.Orders;
using Knez.Sim.Units;

namespace Knez.Sim.Morale
{
    /// <summary>
    /// Tick stage 10: morale (battle-sim.md §12, M3 step 5). In order:
    /// <list type="number">
    /// <item>costs from this tick's blows and wounds (in event order), each with a reason code
    /// (<see cref="MoraleChanged"/>): a fallen squadmate, his own wound, a squadmate's heavy wound, blows at him or his
    /// squad from flank or rear;</item>
    /// <item>recovery: a man with no enemy within the safe distance regains morale up to where he started;</item>
    /// <item>states, man by man in id order: steady, shaken and wavering follow his morale (with hysteresis); below the
    /// routing threshold he routs — drops his blow and target; a routing man who reached his side's edge leaves the battle
    /// (fled); one who recovered to the rally threshold while safe rallies; a rallying man is back (by his morale) once
    /// he stands at his slot again, or routs anew;</item>
    /// <item>each man who began to rout this tick costs every other man of his squad;</item>
    /// <item>squads in id order: more than the break share of its men in the fight routing breaks a squad — all its men
    /// rout, its order is dropped, and squads nearby lose (allies) or gain (enemies) heart; a broken squad with more than
    /// the re-form share back re-forms where its rallied men stand.</item>
    /// </list>
    /// </summary>
    public sealed class MoraleSystem : ISimSystem
    {
        /// <inheritdoc/>
        public void Execute(BattleState state, TickContext context)
        {
            MoraleDef rules = state.Scenario.Morale;
            List<Fighter> fighters = state.FighterList;

            int eventCount = state.Events.Count; // this system adds events; only earlier stages' ones are inputs
            for (int e = 0; e < eventCount; e++)
            {
                SimEvent simEvent = state.Events[e];
                if (simEvent is Wounded wounded)
                {
                    ApplyWound(state, rules, wounded);
                }
                else if (simEvent is BlowStruck blow)
                {
                    ApplyBlow(state, rules, blow);
                }
            }

            List<bool> safe = context.Safe;
            safe.Clear();
            for (int i = 0; i < fighters.Count; i++)
            {
                safe.Add(fighters[i].IsAlive && MoraleRules.Safe(state, fighters[i]));
            }

            Recover(state, rules, safe);

            List<MoraleState> before = context.MoraleBefore;
            before.Clear();
            for (int i = 0; i < fighters.Count; i++)
            {
                before.Add(fighters[i].MoraleState);
            }

            for (int i = 0; i < fighters.Count; i++)
            {
                Transition(state, rules, fighters[i], safe[i]);
            }

            CostOfRouters(state, rules, before);
            Squads(state, rules);
        }

        private static void ApplyWound(BattleState state, MoraleDef rules, Wounded wounded)
        {
            Fighter? man = state.FindFighter(wounded.Fighter);
            if (man == null)
            {
                return;
            }

            if (wounded.Now == WoundLevel.Down)
            {
                Squad? squad = man.SquadId.IsNone ? null : state.FindSquad(man.SquadId);
                if (squad != null)
                {
                    ToSquad(state, squad, man, -MoraleRules.LossCost(rules, squad.StartCount), MoraleReason.SquadmateFell);
                }

                return;
            }

            Change(state, man, wounded.Now == WoundLevel.Heavy ? -rules.OwnHeavyWound : -rules.OwnLightWound, MoraleReason.Wounded);
            if (wounded.Now == WoundLevel.Heavy)
            {
                Squad? squad = man.SquadId.IsNone ? null : state.FindSquad(man.SquadId);
                if (squad != null)
                {
                    ToSquad(state, squad, man, -rules.MateHeavyWound, MoraleReason.SquadmateHeavilyWounded);
                }
            }
        }

        private static void ApplyBlow(BattleState state, MoraleDef rules, BlowStruck blow)
        {
            if (blow.Sector == BlowSector.Front)
            {
                return;
            }

            Fighter? man = state.FindFighter(blow.Defender);
            if (man == null)
            {
                return;
            }

            bool rear = blow.Sector == BlowSector.Rear;
            if (man.IsAlive)
            {
                Change(state, man, rear ? -rules.RearBlow : -rules.FlankBlow, rear ? MoraleReason.StruckFromRear : MoraleReason.StruckFromFlank);
            }

            // His squad feels a flank or rear attack even when the blow felled him — but not blows at a man already running
            // (he's being chased, not his squad attacked).
            Squad? squad = man.SquadId.IsNone ? null : state.FindSquad(man.SquadId);
            if (squad != null && !man.IsRouting)
            {
                ToSquad(state, squad, man, rear ? -rules.MateRearBlow : -rules.MateFlankBlow,
                    rear ? MoraleReason.SquadTakenInRear : MoraleReason.SquadFlanked);
            }
        }

        /// <summary>Recovery spread exactly over a second's ticks: tick t gains rate·t/20 − rate·(t−1)/20.</summary>
        private static void Recover(BattleState state, MoraleDef rules, List<bool> safe)
        {
            int tick = state.Tick;
            if (tick < 1 || rules.RecoveryPerSecond == 0)
            {
                return;
            }

            int gain = (rules.RecoveryPerSecond * tick / BattleState.TicksPerSecond)
                - (rules.RecoveryPerSecond * (tick - 1) / BattleState.TicksPerSecond);
            if (gain == 0)
            {
                return;
            }

            List<Fighter> fighters = state.FighterList;
            for (int i = 0; i < fighters.Count; i++)
            {
                Fighter man = fighters[i];
                int start = man.Experience.Morale;
                if (man.IsAlive && man.Morale < start && safe[i])
                {
                    man.Morale = man.Morale + gain > start ? start : man.Morale + gain;
                }
            }
        }

        private static void Transition(BattleState state, MoraleDef rules, Fighter man, bool safe)
        {
            if (!man.IsAlive)
            {
                return;
            }

            MoraleState from = man.MoraleState;
            Squad? squad = man.SquadId.IsNone ? null : state.FindSquad(man.SquadId);
            MoraleState to = from;
            if (from == MoraleState.Routing)
            {
                if (MoraleRules.AtEdge(state, man))
                {
                    to = MoraleState.Fled;
                    man.IsAlive = false; // out of the fight, alive; cleanup takes him out of his squad
                }
                else if (squad != null && man.Morale >= rules.RallyAt && safe)
                {
                    to = MoraleState.Rallying;
                }
            }
            else if (from == MoraleState.Rallying)
            {
                if (man.Morale < rules.Routing)
                {
                    to = MoraleState.Routing;
                }
                else if (squad != null && !squad.Broken
                    && Movement.Crowding.DistanceToSlot(state, squad, man) <= state.Scenario.Movement.HoldingReach)
                {
                    to = MoraleRules.Next(rules, MoraleState.Wavering, man.Morale); // back in his place
                }
            }
            else
            {
                to = MoraleRules.Next(rules, from, man.Morale);
            }

            if (to != from)
            {
                SetState(state, man, to);
            }
        }

        /// <summary>Each man who began to rout this tick costs every other living man of his squad.</summary>
        private static void CostOfRouters(BattleState state, MoraleDef rules, List<MoraleState> before)
        {
            if (rules.MateRouts == 0)
            {
                return;
            }

            List<Fighter> fighters = state.FighterList;
            for (int i = 0; i < fighters.Count; i++)
            {
                Fighter man = fighters[i];
                if (man.MoraleState == MoraleState.Routing && before[i] != MoraleState.Routing && !man.SquadId.IsNone)
                {
                    Squad? squad = state.FindSquad(man.SquadId);
                    if (squad != null)
                    {
                        ToSquad(state, squad, man, -rules.MateRouts, MoraleReason.SquadmateRouted);
                    }
                }
            }
        }

        private static void Squads(BattleState state, MoraleDef rules)
        {
            List<Squad> squads = state.SquadList;
            for (int s = 0; s < squads.Count; s++)
            {
                Squad squad = squads[s];
                int inFight = 0;
                int routing = 0;
                List<EntityId> members = squad.MemberList;
                for (int m = 0; m < members.Count; m++)
                {
                    Fighter? man = state.FindFighter(members[m]);
                    if (man != null && man.IsAlive)
                    {
                        inFight++;
                        routing += man.IsRouting ? 1 : 0;
                    }
                }

                if (inFight == 0)
                {
                    continue;
                }

                if (!squad.Broken && routing * 1000L > (long)rules.BreakShare * inFight)
                {
                    Break(state, rules, squad, inFight, routing);
                }
                else if (squad.Broken && (inFight - routing) * 1000L > (long)rules.ReformShare * inFight)
                {
                    Reform(state, squad, inFight, routing);
                }
            }
        }

        private static void Break(BattleState state, MoraleDef rules, Squad squad, int inFight, int routing)
        {
            squad.Broken = true;
            squad.Order = SquadOrder.None;
            squad.ClearPath();
            squad.Reforming = false;
            List<EntityId> members = squad.MemberList;
            for (int m = 0; m < members.Count; m++)
            {
                Fighter? man = state.FindFighter(members[m]);
                if (man != null && man.IsAlive && man.MoraleState != MoraleState.Routing)
                {
                    SetState(state, man, MoraleState.Routing); // morale unchanged: men swept along rally sooner
                }
            }

            state.AddEvent(new SquadBroke(state.Tick, squad.Id, reformed: false, inFight, routing));

            // Squads nearby (anchor to anchor) feel it: allies lose heart, enemies gain it. Their states follow next tick.
            List<Squad> squads = state.SquadList;
            for (int s = 0; s < squads.Count; s++)
            {
                Squad other = squads[s];
                if (ReferenceEquals(other, squad) || (other.Anchor - squad.Anchor).Length > rules.Nearby)
                {
                    continue;
                }

                bool ally = other.SideId == squad.SideId;
                ToSquad(state, other, null, ally ? -rules.AllySquadBroke : rules.EnemySquadBroke,
                    ally ? MoraleReason.AlliedSquadBroke : MoraleReason.EnemySquadBroke);
            }
        }

        /// <summary>Re-forms a broken squad where its rallied men stand: the anchor moves to their centre, places go to the
        /// nearest men, and those still running come back as they rally.</summary>
        private static void Reform(BattleState state, Squad squad, int inFight, int routing)
        {
            long x = 0;
            long y = 0;
            int n = 0;
            List<EntityId> members = squad.MemberList;
            for (int m = 0; m < members.Count; m++)
            {
                Fighter? man = state.FindFighter(members[m]);
                if (man != null && man.IsAlive && !man.IsRouting)
                {
                    x += man.Position.X.Raw;
                    y += man.Position.Y.Raw;
                    n++;
                }
            }

            squad.Broken = false;
            if (n > 0)
            {
                // Fitted like a move order's target: the whole formation on the map, the anchor clear of walls, or some
                // places can't be reached and the men walking to them never count as back.
                var centre = new FixVec2(Fix.FromRaw(x / n), Fix.FromRaw(y / n));
                FixVec2 anchor = Commands.MoveSquadCommand.FitInside(state, squad, centre, squad.Facing);
                Fighter? any = state.FindFighter(members[0]);
                squad.Anchor = Movement.Terrain.Constrain(state.Scenario.Map, anchor, any != null ? any.Radius : Fix.Zero);
                SlotAssignment.Nearest(state, squad);
            }

            state.AddEvent(new SquadBroke(state.Tick, squad.Id, reformed: true, inFight, routing));
        }

        private static void SetState(BattleState state, Fighter man, MoraleState to)
        {
            MoraleState from = man.MoraleState;
            man.MoraleState = to;
            if (to == MoraleState.Routing || to == MoraleState.Fled)
            {
                man.Action = FighterAction.Idle; // a running man strikes no one
                man.ActionTicks = 0;
                man.Target = EntityId.None;
            }

            state.AddEvent(new MoraleStateChanged(state.Tick, man.Id, from, to));
        }

        /// <summary>Applies <paramref name="delta"/> to every living man of <paramref name="squad"/> but
        /// <paramref name="except"/>.</summary>
        private static void ToSquad(BattleState state, Squad squad, Fighter? except, int delta, MoraleReason reason)
        {
            if (delta == 0)
            {
                return;
            }

            List<EntityId> members = squad.MemberList;
            for (int m = 0; m < members.Count; m++)
            {
                Fighter? man = state.FindFighter(members[m]);
                if (man != null && man.IsAlive && !ReferenceEquals(man, except))
                {
                    Change(state, man, delta, reason);
                }
            }
        }

        private static void Change(BattleState state, Fighter man, int delta, MoraleReason reason)
        {
            int now = man.Morale + delta;
            now = now < 0 ? 0 : now > 1000 ? 1000 : now;
            int applied = now - man.Morale;
            if (applied == 0)
            {
                return;
            }

            man.Morale = now;
            state.AddEvent(new MoraleChanged(state.Tick, man.Id, reason, applied, now));
        }
    }
}
