using System;
using System.Collections.Generic;
using System.Globalization;
using Knez.Sim.Commands;
using Knez.Sim.Content.Json;
using Knez.Sim.Defs;
using Knez.Sim.Events;
using Knez.Sim.Numerics;
using Knez.Sim.Units;

namespace Knez.Sim.Content
{
    /// <summary>
    /// Keeps the orders a player gave during one battle and writes them out as a scenario file whose <c>script</c>
    /// holds them, so the battle can be replayed headless with the same seed (battle-sim.md §8). Only commands the sim
    /// applied are kept: a rejected one changed nothing, so the replay doesn't need it. Not battle state — it sits
    /// beside the simulation and is never hashed.
    /// </summary>
    public sealed class SessionRecorder
    {
        private readonly List<Entry> _pending = new List<Entry>();
        private readonly List<ScriptedOrderDef> _orders = new List<ScriptedOrderDef>();

        /// <summary>The applied orders so far, in the order the sim ran them.</summary>
        public IReadOnlyList<ScriptedOrderDef> Orders => _orders;

        /// <summary>
        /// Notes <paramref name="command"/>, just enqueued with <see cref="BattleSimulation.Enqueue"/> (so it runs in
        /// tick <c>state.Tick + 1</c>). Commands a scenario script can't express are ignored.
        /// </summary>
        public void Enqueued(BattleState state, ICommand command)
        {
            ScriptedOrderDef? order = ToOrder(state, state.Tick + 1, command);
            if (order != null)
            {
                _pending.Add(new Entry(command, order));
            }
        }

        /// <summary>Call after every tick: keeps this tick's applied commands and drops the rejected ones.</summary>
        public void Stepped(BattleState state)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].Order.Tick == state.Tick && Rejected(state, _pending[i].Command))
                {
                    _pending.RemoveAt(i);
                }
            }

            int done = 0;
            while (done < _pending.Count && _pending[done].Order.Tick <= state.Tick)
            {
                _orders.Add(_pending[done].Order);
                done++;
            }

            _pending.RemoveRange(0, done);
        }

        /// <summary>
        /// The scenario <paramref name="scenario"/> with id <paramref name="id"/>, <paramref name="description"/> and the
        /// recorded orders added to its script (after its own entries of the same tick, which run first in the sim too).
        /// Everything else is copied as it is.
        /// </summary>
        public string Write(SourceText scenario, string id, string description)
        {
            JsonValue root = JsonReader.Parse(scenario);
            if (root.Kind != JsonKind.Object)
            {
                throw new ContentException($"{scenario.Name}: a scenario is a JSON object.");
            }

            var writer = new JsonWriter();
            writer.Raw("{");
            bool first = true;
            bool scriptWritten = false;
            foreach (JsonMember member in root.Members)
            {
                if (member.Name == "description")
                {
                    continue; // written right after the id
                }

                Next(writer, ref first).String(member.Name).Raw(": ");
                if (member.Name == "id")
                {
                    writer.String(id);
                    Next(writer, ref first).String("description").Raw(": ").String(description);
                }
                else if (member.Name == "script")
                {
                    Script(writer, member.Value, scenario.Name);
                    scriptWritten = true;
                }
                else
                {
                    writer.Value(member.Value, 1);
                }
            }

            if (!scriptWritten && _orders.Count > 0)
            {
                Next(writer, ref first).String("script").Raw(": ");
                Script(writer, null, scenario.Name);
            }

            return writer.NewLine(0).Raw("}\n").ToString();
        }

        /// <summary>
        /// <paramref name="value"/> as decimal text that <see cref="ContentNumbers.TryParseFix"/> reads back to exactly the
        /// same raw value for |value| ≤ <see cref="ContentNumbers.MaxWhole"/> (any map position): up to 9 decimals, trailing
        /// zeros dropped (an error under 10⁻⁹ is far below 1/65536).
        /// </summary>
        public static string Number(Fix value)
        {
            bool negative = value.Raw < 0;
            ulong magnitude = negative ? unchecked((ulong)(-(value.Raw + 1)) + 1UL) : (ulong)value.Raw;
            ulong whole = magnitude >> 16;
            ulong digits = (((magnitude & 0xFFFFUL) * 1000000000UL) + 32768UL) >> 16;
            if (digits == 1000000000UL)
            {
                whole++;
                digits = 0;
            }

            string text = whole.ToString(CultureInfo.InvariantCulture);
            if (digits > 0)
            {
                text += "." + digits.ToString("D9", CultureInfo.InvariantCulture).TrimEnd('0');
            }

            return negative && (whole > 0 || digits > 0) ? "-" + text : text;
        }

        /// <summary>
        /// <paramref name="facing"/> in degrees as text that <see cref="Angle.FromDegrees(Fix)"/> turns back into exactly
        /// the same angle.
        /// </summary>
        public static string Degrees(Angle facing) => Number(Fix.FromRaw((long)facing.Raw * 360));

        /// <summary>The script entry that does what <paramref name="command"/> does in <paramref name="tick"/>, or null.</summary>
        public static ScriptedOrderDef? ToOrder(BattleState state, int tick, ICommand command)
        {
            switch (command)
            {
                case MoveSquadCommand move:
                {
                    Squad? squad = state.FindSquad(move.SquadId);
                    return squad == null ? null : new ScriptedOrderDef(tick, squad.Name, move.Target, move.Facing);
                }

                case SetFormationCommand formation:
                {
                    Squad? squad = state.FindSquad(formation.SquadId);
                    return squad == null ? null : ScriptedOrderDef.Formation(tick, squad.Name, formation.FormationId);
                }

                case AttachRetainerCommand attach:
                {
                    string? name = RetainerName(state, attach.RetainerId);
                    Squad? squad = state.FindSquad(attach.SquadId);
                    return name == null || squad == null ? null : ScriptedOrderDef.Attach(tick, name, squad.Name);
                }

                case DetachRetainerCommand detach:
                {
                    string? name = RetainerName(state, detach.RetainerId);
                    return name == null ? null : ScriptedOrderDef.Detach(tick, name);
                }

                case MoveRetainerCommand move:
                {
                    string? name = RetainerName(state, move.RetainerId);
                    return name == null ? null : ScriptedOrderDef.MoveRetainer(tick, name, move.Target, move.Facing);
                }

                case RetainerAttackCommand attack:
                {
                    string? name = RetainerName(state, attack.RetainerId);
                    return name == null ? null : ScriptedOrderDef.Attack(tick, name, attack.EnemyId);
                }

                case UseAbilityCommand ability:
                {
                    string? name = RetainerName(state, ability.RetainerId);
                    return name == null ? null : ScriptedOrderDef.Ability(tick, name);
                }

                default:
                    return null;
            }
        }

        private static string? RetainerName(BattleState state, EntityId id)
        {
            RetainerPart? part = state.FindFighter(id)?.Retainer;
            return part.HasValue ? part.Value.Name : null;
        }

        private void Script(JsonWriter writer, JsonValue? existing, string sourceName)
        {
            IReadOnlyList<JsonValue> own = existing != null ? existing.Items : Array.Empty<JsonValue>();
            if (own.Count + _orders.Count == 0)
            {
                writer.Raw("[]");
                return;
            }

            writer.Raw("[");
            int o = 0;
            int r = 0;
            while (o < own.Count || r < _orders.Count)
            {
                bool takeOwn = r >= _orders.Count || (o < own.Count && TickOf(own[o], sourceName) <= _orders[r].Tick);
                writer.NewLine(2);
                if (takeOwn)
                {
                    writer.Value(own[o++], 2);
                }
                else
                {
                    Order(writer, _orders[r++]);
                }

                if (o < own.Count || r < _orders.Count)
                {
                    writer.Raw(",");
                }
            }

            writer.NewLine(1).Raw("]");
        }

        private static void Order(JsonWriter writer, ScriptedOrderDef order)
        {
            writer.Raw("{ \"tick\": ").Raw(order.Tick.ToString(CultureInfo.InvariantCulture));
            switch (order.Kind)
            {
                case ScriptedOrderKind.Formation:
                    writer.Raw(", \"order\": \"formation\", \"squad\": ").String(order.SquadName!)
                        .Raw(", \"formation\": ").String(order.FormationId!).Raw(" }");
                    return;
                case ScriptedOrderKind.AttachRetainer:
                    writer.Raw(", \"order\": \"attach\", \"retainer\": ").String(order.RetainerName!)
                        .Raw(", \"squad\": ").String(order.SquadName!).Raw(" }");
                    return;
                case ScriptedOrderKind.DetachRetainer:
                    writer.Raw(", \"order\": \"detach\", \"retainer\": ").String(order.RetainerName!).Raw(" }");
                    return;
                case ScriptedOrderKind.UseAbility:
                    writer.Raw(", \"order\": \"ability\", \"retainer\": ").String(order.RetainerName!).Raw(" }");
                    return;
                case ScriptedOrderKind.RetainerAttack:
                    writer.Raw(", \"order\": \"attack\", \"retainer\": ").String(order.RetainerName!)
                        .Raw(", \"enemy\": ").Raw(order.Enemy.Value.ToString(CultureInfo.InvariantCulture)).Raw(" }");
                    return;
                case ScriptedOrderKind.MoveRetainer:
                    writer.Raw(", \"order\": \"move\", \"retainer\": ").String(order.RetainerName!);
                    break;
                default:
                    writer.Raw(", \"order\": \"move\", \"squad\": ").String(order.SquadName!);
                    break;
            }

            writer.Raw(", \"target\": [").Raw(Number(order.Target.X)).Raw(", ").Raw(Number(order.Target.Y)).Raw("]");
            if (order.Facing.HasValue)
            {
                writer.Raw(", \"facing\": ").Raw(Degrees(order.Facing.Value));
            }

            writer.Raw(" }");
        }

        private static int TickOf(JsonValue entry, string sourceName)
        {
            JsonValue? tick = entry.Get("tick");
            if (tick == null || tick.Kind != JsonKind.Number || !ContentNumbers.TryParseInt(tick.Text, out int value, out _))
            {
                throw new ContentException($"{sourceName}:{entry.Line}:{entry.Column}: a script entry needs a whole-number tick.");
            }

            return value;
        }

        private static bool Rejected(BattleState state, ICommand command)
        {
            IReadOnlyList<SimEvent> events = state.Events;
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i] is CommandRejected rejected && ReferenceEquals(rejected.Command, command))
                {
                    return true;
                }
            }

            return false;
        }

        private static JsonWriter Next(JsonWriter writer, ref bool first)
        {
            if (!first)
            {
                writer.Raw(",");
            }

            first = false;
            return writer.NewLine(1);
        }

        private readonly struct Entry
        {
            public Entry(ICommand command, ScriptedOrderDef order)
            {
                Command = command;
                Order = order;
            }

            public ICommand Command { get; }

            public ScriptedOrderDef Order { get; }
        }
    }
}
