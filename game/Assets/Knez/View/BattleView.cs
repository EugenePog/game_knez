#nullable enable
using System.Collections.Generic;
using Knez.Sim;
using Knez.Sim.Combat;
using Knez.Sim.Defs;
using Knez.Sim.Events;
using Knez.Sim.Orders;
using Knez.Sim.Units;
using UnityEngine;
using EntityId = Knez.Sim.EntityId; // Unity 6 has a UnityEngine.EntityId too

namespace Knez.Game.View
{
    /// <summary>
    /// Draws a <see cref="BattleRunner"/>'s battle top-down (M2 step 4): walls (houses in timber brown, obstacles in stone
    /// grey), fighters as discs in their squad's colour
    /// with a tick for their facing (grey once fallen), each squad's anchor with a facing marker, and the route still
    /// ahead of a squad on the march. Reads the state only. Positions are interpolated between the last two ticks by the
    /// clock's alpha, so motion is smooth at any frame rate. The camera is fitted to the map. M3: a red dot on a wounded
    /// man (bigger = heavily), a running man in his squad's colour faded toward white, a thin line in the striker's squad
    /// colour from a man winding up a blow to his target, and when the blow falls a flash along it in the same colour
    /// plus a spark on the struck man — red if it got through, white if it was turned aside. M3 step 8: a morale bar over
    /// every man in the fight, as long as his morale and coloured by his state (<see cref="ShowMoraleBars"/>). M3 step 10:
    /// a gold ring round every Retainer; he takes the colour of the squad he is in, a dark side colour while he stands
    /// alone, and a thin gold line shows where a lone Retainer is going (or whom he is going at). M3 step 11: a battle cry
    /// shows as a gold disc as wide as its reach, fading out where he gave it.
    /// </summary>
    public sealed class BattleView : MonoBehaviour
    {
        [SerializeField] private BattleRunner? runner;
        [SerializeField] private Camera? viewCamera;

        private static readonly Color[] SideZero = { new Color(0.20f, 0.45f, 0.85f), new Color(0.15f, 0.60f, 0.45f), new Color(0.45f, 0.35f, 0.80f), new Color(0.10f, 0.55f, 0.75f) };
        private static readonly Color[] SideOne = { new Color(0.80f, 0.25f, 0.20f), new Color(0.85f, 0.55f, 0.15f), new Color(0.65f, 0.20f, 0.45f), new Color(0.70f, 0.40f, 0.25f) };
        private static readonly Color Fallen = new Color(0.55f, 0.55f, 0.55f, 0.6f);
        private static readonly Color Ground = new Color(0.93f, 0.89f, 0.82f);
        private static readonly Color House = new Color(0.55f, 0.36f, 0.20f); // timber
        private static readonly Color Obstacle = new Color(0.43f, 0.45f, 0.47f); // stone
        private static readonly Color[] Lone = { new Color(0.10f, 0.18f, 0.45f), new Color(0.45f, 0.09f, 0.08f) }; // a Retainer alone, by side
        private static readonly Color Gold = new Color(0.83f, 0.63f, 0.09f);

        private readonly List<Transform> _bodies = new List<Transform>();
        private readonly List<SpriteRenderer> _bodyRenderers = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> _halos = new List<SpriteRenderer>();
        private readonly List<EntityId> _selection = new List<EntityId>();
        private EntityId _selectedRetainer = EntityId.None;
        private readonly List<SpriteRenderer?> _rings = new List<SpriteRenderer?>(); // a Retainer's gold ring; null for other men
        private readonly List<LineRenderer?> _loneLines = new List<LineRenderer?>(); // where a lone Retainer is going
        private readonly List<Transform> _anchors = new List<Transform>();
        private readonly List<LineRenderer> _routes = new List<LineRenderer>();
        private readonly Dictionary<int, Color> _squadColours = new Dictionary<int, Color>(); // view-side cache keyed by id
        private readonly Dictionary<int, int> _index = new Dictionary<int, int>(); // fighter id → position in the list
        private readonly List<SpriteRenderer> _wounds = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> _barBacks = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> _barFills = new List<SpriteRenderer>();
        private readonly List<float> _radii = new List<float>();
        private const float BarWidth = 0.7f; // metres
        private const float BarHeight = 0.09f;
        private readonly List<LineRenderer> _aims = new List<LineRenderer>();
        private readonly List<Flash> _flashes = new List<Flash>();
        private readonly List<(SpriteRenderer Disc, float Until)> _cries = new List<(SpriteRenderer, float)>();
        private const float CrySeconds = 0.9f;
        private const float FlashSeconds = 0.35f;
        private static readonly Color Landed = new Color(0.90f, 0.05f, 0.05f);
        private static readonly Color Missed = new Color(1f, 1f, 1f);
        private Vector2[] _previous = new Vector2[0];
        private Vector2[] _current = new Vector2[0];
        private float[] _previousFacing = new float[0];
        private float[] _currentFacing = new float[0];

        /// <summary>Whether a morale bar is drawn over every man in the fight (B).</summary>
        public bool ShowMoraleBars { get; set; } = true;

        /// <summary>Sets what to draw from code (tests, bootstrapping) instead of the Inspector.</summary>
        public void Bind(BattleRunner battle, Camera? cam)
        {
            runner = battle;
            viewCamera = cam;
        }

        /// <summary>Highlights the men of <paramref name="squads"/> (the player's selection).</summary>
        public void SetSelection(IReadOnlyList<EntityId> squads)
        {
            _selection.Clear();
            _selection.AddRange(squads);
        }

        /// <summary>Highlights one Retainer (the player's selection), or none.</summary>
        public void SetSelectedRetainer(EntityId retainer) => _selectedRetainer = retainer;

        /// <summary>Builds the scene objects for the current battle; called once the runner has loaded it.</summary>
        public void Build()
        {
            BattleSimulation? sim = runner != null ? runner.Simulation : null;
            if (sim == null)
            {
                return;
            }

            BattleState state = sim.State;
            MapDef map = state.Scenario.Map;
            Make("Ground", ShapeSprites.Square, Ground, -10, new Vector2(map.Width.ToFloat() / 2, map.Height.ToFloat() / 2),
                new Vector2(map.Width.ToFloat(), map.Height.ToFloat()));
            foreach (WallDef wall in map.Walls)
            {
                Vector2 min = wall.Min.ToVector2();
                Vector2 max = wall.Max.ToVector2();
                bool house = wall.Kind == WallKind.House;
                Make(house ? "House" : "Obstacle", ShapeSprites.Square, house ? House : Obstacle, 0, (min + max) / 2, max - min);
            }

            int zero = 0;
            int one = 0;
            foreach (Squad squad in state.Squads)
            {
                Color colour = squad.SideId == 0 ? SideZero[zero++ % SideZero.Length] : SideOne[one++ % SideOne.Length];
                _squadColours[squad.Id.Value] = colour;
                Transform anchor = Make("Anchor " + squad.Name, ShapeSprites.Square, colour, 4, squad.Anchor.ToVector2(), new Vector2(0.5f, 0.12f)).transform;
                _anchors.Add(anchor);
                var route = new GameObject("Route " + squad.Name).AddComponent<LineRenderer>();
                route.transform.SetParent(transform, false);
                route.material = ShapeSprites.LineMaterial;
                route.startColor = route.endColor = new Color(colour.r, colour.g, colour.b, 0.55f);
                route.startWidth = route.endWidth = 0.12f;
                route.sortingOrder = 1;
                route.useWorldSpace = true;
                _routes.Add(route);
            }

            int count = state.Fighters.Count;
            _previous = new Vector2[count];
            _current = new Vector2[count];
            _previousFacing = new float[count];
            _currentFacing = new float[count];
            for (int i = 0; i < count; i++)
            {
                Fighter fighter = state.Fighters[i];
                float diameter = fighter.Radius.ToFloat() * 2;
                SpriteRenderer body = Make("Fighter " + fighter.Id.Value, ShapeSprites.Disc, Colour(fighter), 2, fighter.Position.ToVector2(), new Vector2(diameter, diameter));
                SpriteRenderer tick = Make("Facing", ShapeSprites.Square, new Color(0.1f, 0.1f, 0.1f, 0.85f), 3, Vector2.zero, Vector2.one);
                tick.transform.SetParent(body.transform, false);
                tick.transform.localPosition = new Vector3(0.3f, 0, 0); // local units: the body is one unit across
                tick.transform.localScale = new Vector3(0.45f, 0.14f, 1);
                // Under the walls (0), over the ground (-10): a Retainer's ring (1) shows inside it.
                SpriteRenderer halo = Make("Selected", ShapeSprites.Disc, new Color(1f, 1f, 0.85f, 0.9f), -1, Vector2.zero, Vector2.one);
                halo.transform.SetParent(body.transform, false);
                halo.transform.localPosition = Vector3.zero;
                halo.transform.localScale = new Vector3(fighter.IsRetainer ? 1.65f : 1.45f, fighter.IsRetainer ? 1.65f : 1.45f, 1);
                halo.enabled = false;
                SpriteRenderer? ring = null;
                LineRenderer? loneLine = null;
                if (fighter.IsRetainer)
                {
                    ring = Make("Retainer", ShapeSprites.Disc, Gold, 1, Vector2.zero, Vector2.one);
                    ring.transform.SetParent(body.transform, false);
                    ring.transform.localPosition = Vector3.zero;
                    ring.transform.localScale = new Vector3(1.32f, 1.32f, 1);
                    loneLine = Line("Lone " + fighter.Id.Value, 1);
                    loneLine.startColor = loneLine.endColor = new Color(Gold.r, Gold.g, Gold.b, 0.6f);
                    loneLine.startWidth = loneLine.endWidth = 0.08f;
                }

                _rings.Add(ring);
                _loneLines.Add(loneLine);
                SpriteRenderer wound = Make("Wound", ShapeSprites.Disc, new Color(0.80f, 0.08f, 0.08f), 4, Vector2.zero, Vector2.one);
                wound.transform.SetParent(body.transform, false);
                wound.transform.localPosition = Vector3.zero;
                wound.enabled = false;
                _wounds.Add(wound);
                // Bars aren't children of the body: it turns with his facing, they stay level over him.
                SpriteRenderer back = Make("Morale bar", ShapeSprites.Square, new Color(0, 0, 0, 0.55f), 7, Vector2.zero, new Vector2(BarWidth, BarHeight));
                SpriteRenderer fill = Make("Morale", ShapeSprites.Square, Color.green, 8, Vector2.zero, new Vector2(BarWidth, BarHeight));
                _barBacks.Add(back);
                _barFills.Add(fill);
                _radii.Add(fighter.Radius.ToFloat());
                _aims.Add(Line("Aim " + fighter.Id.Value, 2));
                _index[fighter.Id.Value] = i;
                _bodies.Add(body.transform);
                _bodyRenderers.Add(body);
                _halos.Add(halo);
            }

            Capture(state);
            System.Array.Copy(_current, _previous, count);
            System.Array.Copy(_currentFacing, _previousFacing, count);
            runner!.Ticked += OnTicked;
            FitCamera(map);
        }

        private void Start()
        {
            if (runner == null)
            {
                runner = GetComponent<BattleRunner>(); // the usual set-up: both on one object
            }

            Build();
        }

        private void OnDestroy()
        {
            if (runner != null)
            {
                runner.Ticked -= OnTicked;
            }
        }

        /// <summary>After each tick: the positions just drawn to become the previous ones, the new state the current.</summary>
        private void OnTicked(BattleState state)
        {
            (_previous, _current) = (_current, _previous);
            (_previousFacing, _currentFacing) = (_currentFacing, _previousFacing);
            Capture(state);
            foreach (SimEvent simEvent in state.Events)
            {
                if (simEvent is BlowStruck blow && _index.TryGetValue(blow.Attacker.Value, out int a) && _index.TryGetValue(blow.Defender.Value, out int d))
                {
                    Flash flash = FreeFlash();
                    flash.Attacker = a;
                    flash.Defender = d;
                    flash.Until = Time.unscaledTime + FlashSeconds;
                    Fighter striker = state.Fighters[a];
                    flash.Colour = SquadColour(striker); // who struck
                    flash.Line.startWidth = 0.16f;
                    flash.Line.endWidth = 0.05f; // thick at the striker, thin at the struck: the direction of the blow
                    flash.Line.enabled = true;
                    flash.Spark.color = blow.Landed ? Landed : Missed; // whether it got through
                    flash.Spark.transform.localScale = blow.Landed ? new Vector3(0.45f, 0.45f, 1) : new Vector3(0.3f, 0.3f, 1);
                    flash.Spark.enabled = true;
                }
                else if (simEvent is AbilityUsed used && _index.TryGetValue(used.Retainer.Value, out int r)
                    && state.Fighters[r].Retainer is RetainerPart part && part.Trait.Ability.Kind == AbilityKind.BattleCry)
                {
                    Cry(state.Fighters[r].Position.ToVector2(), part.Trait.Ability.Radius.ToFloat());
                }
            }
        }

        /// <summary>A battle cry's reach: a gold disc round where he cried, fading out (view-side only).</summary>
        private void Cry(Vector2 at, float radius)
        {
            int free = _cries.FindIndex(c => !c.Disc.enabled);
            SpriteRenderer disc = free >= 0 ? _cries[free].Disc : Make("Battle cry", ShapeSprites.Disc, Gold, 0, at, Vector2.one);
            disc.transform.position = at;
            disc.transform.localScale = new Vector3(radius * 2, radius * 2, 1);
            disc.enabled = true;
            var cry = (disc, Time.unscaledTime + CrySeconds);
            if (free >= 0)
            {
                _cries[free] = cry;
            }
            else
            {
                _cries.Add(cry);
            }
        }

        /// <summary>A blow's flash: a line from striker to struck, fading out (view-side only).</summary>
        private sealed class Flash
        {
            public Flash(LineRenderer line, SpriteRenderer spark)
            {
                Line = line;
                Spark = spark;
            }

            public LineRenderer Line { get; }

            public SpriteRenderer Spark { get; }

            public int Attacker { get; set; }

            public int Defender { get; set; }

            public float Until { get; set; }

            public Color Colour { get; set; }
        }

        private Flash FreeFlash()
        {
            foreach (Flash flash in _flashes)
            {
                if (!flash.Line.enabled)
                {
                    return flash;
                }
            }

            SpriteRenderer spark = Make("Spark", ShapeSprites.Disc, Missed, 6, Vector2.zero, new Vector2(0.3f, 0.3f));
            spark.enabled = false;
            var made = new Flash(Line("Blow", 5), spark);
            _flashes.Add(made);
            return made;
        }

        private LineRenderer Line(string name, int order)
        {
            var line = new GameObject(name).AddComponent<LineRenderer>();
            line.transform.SetParent(transform, false);
            line.material = ShapeSprites.LineMaterial;
            line.startWidth = line.endWidth = 0.05f;
            line.sortingOrder = order;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.enabled = false;
            return line;
        }

        private void LateUpdate()
        {
            BattleSimulation? sim = runner != null ? runner.Simulation : null;
            if (sim == null || _bodies.Count == 0)
            {
                return;
            }

            Draw(sim.State, runner!.Clock.Alpha);
        }

        /// <summary>
        /// Moves every scene object to the state: fighters between the tick before and the last tick by
        /// <paramref name="alpha"/> (0 = the tick before, 1 = the last; the clock's fraction toward the next tick), so the
        /// drawing runs one tick behind the sim and never has to guess ahead.
        /// </summary>
        public void Draw(BattleState state, float alpha)
        {
            float t = Mathf.Clamp01(alpha);
            for (int i = 0; i < _bodies.Count; i++)
            {
                Fighter fighter = state.Fighters[i];
                _bodies[i].position = Vector2.Lerp(_previous[i], _current[i], t);
                _bodies[i].rotation = Quaternion.Euler(0, 0, Mathf.LerpAngle(_previousFacing[i], _currentFacing[i], t));
                _bodyRenderers[i].color = Colour(fighter);
                _halos[i].enabled = fighter.IsAlive && (fighter.IsRetainer && fighter.Id == _selectedRetainer
                    || !fighter.SquadId.IsNone && _selection.Contains(fighter.SquadId));
                SpriteRenderer? ring = _rings[i];
                if (ring != null)
                {
                    ring.color = fighter.IsAlive ? Gold : Fallen;
                }

                LoneLine(state, i, fighter);
                Bars(i, fighter);
                bool hurt = fighter.IsAlive && (fighter.Wound == WoundLevel.Light || fighter.Wound == WoundLevel.Heavy);
                _wounds[i].enabled = hurt;
                if (hurt)
                {
                    float size = fighter.Wound == WoundLevel.Heavy ? 0.55f : 0.3f; // local units: the body is one unit across
                    _wounds[i].transform.localScale = new Vector3(size, size, 1);
                }
            }

            // Aim lines: a man winding up a blow, to his target (both drawn where they are this frame).
            for (int i = 0; i < _aims.Count; i++)
            {
                Fighter fighter = state.Fighters[i];
                LineRenderer aim = _aims[i];
                bool aiming = fighter.IsAlive && fighter.Action == FighterAction.Windup && _index.ContainsKey(fighter.Target.Value);
                aim.enabled = aiming;
                if (aiming)
                {
                    Color c = SquadColour(fighter);
                    aim.startColor = new Color(c.r, c.g, c.b, 0.85f);
                    aim.endColor = new Color(c.r, c.g, c.b, 0.25f);
                    aim.startWidth = 0.08f;
                    aim.endWidth = 0.03f;
                    aim.SetPosition(0, _bodies[i].position);
                    aim.SetPosition(1, _bodies[_index[fighter.Target.Value]].position);
                }
            }

            float now = Time.unscaledTime;
            foreach (var (disc, until) in _cries)
            {
                float left = (until - now) / CrySeconds;
                disc.enabled = disc.enabled && left > 0;
                disc.color = new Color(Gold.r, Gold.g, Gold.b, 0.3f * Mathf.Clamp01(left));
            }

            foreach (Flash flash in _flashes)
            {
                if (!flash.Line.enabled)
                {
                    continue;
                }

                float left = (flash.Until - now) / FlashSeconds;
                if (left <= 0)
                {
                    flash.Line.enabled = false;
                    flash.Spark.enabled = false;
                    continue;
                }

                Color c = flash.Colour;
                flash.Line.startColor = flash.Line.endColor = new Color(c.r, c.g, c.b, left);
                flash.Line.SetPosition(0, _bodies[flash.Attacker].position);
                flash.Line.SetPosition(1, _bodies[flash.Defender].position);
                Color k = flash.Spark.color;
                flash.Spark.color = new Color(k.r, k.g, k.b, left);
                flash.Spark.transform.position = _bodies[flash.Defender].position;
            }

            for (int s = 0; s < state.Squads.Count; s++)
            {
                Squad squad = state.Squads[s];
                _anchors[s].position = squad.Anchor.ToVector2();
                _anchors[s].rotation = Quaternion.Euler(0, 0, squad.Facing.ToDegrees() + 90); // the bar lies along the front
                LineRenderer route = _routes[s];
                if (squad.Order.Kind != SquadOrderKind.Move || squad.Path.Count == 0)
                {
                    route.positionCount = 0;
                    continue;
                }

                int ahead = squad.Path.Count - squad.PathIndex;
                route.positionCount = ahead + 1;
                route.SetPosition(0, squad.Anchor.ToVector2());
                for (int p = 0; p < ahead; p++)
                {
                    route.SetPosition(p + 1, squad.Path[squad.PathIndex + p].ToVector2());
                }
            }
        }

        private void Capture(BattleState state)
        {
            for (int i = 0; i < _current.Length; i++)
            {
                Fighter fighter = state.Fighters[i];
                _current[i] = fighter.Position.ToVector2();
                _currentFacing[i] = fighter.Facing.ToDegrees();
            }
        }

        /// <summary>Man <paramref name="i"/>'s morale bar: level over his body, filled to his morale (0..1000) in his
        /// state's colour; hidden once he is out of the fight or the bars are off.</summary>
        private void Bars(int i, Fighter fighter)
        {
            bool shown = ShowMoraleBars && fighter.IsAlive;
            _barBacks[i].enabled = shown;
            _barFills[i].enabled = shown;
            if (!shown)
            {
                return;
            }

            Vector2 centre = (Vector2)_bodies[i].position + new Vector2(0, _radii[i] + 0.16f);
            float filled = BarWidth * Mathf.Clamp01(fighter.Morale / 1000f);
            _barBacks[i].transform.position = centre;
            _barFills[i].transform.position = centre + new Vector2((filled - BarWidth) / 2, 0);
            _barFills[i].transform.localScale = new Vector3(filled, BarHeight, 1);
            _barFills[i].color = BattleHud.StateColour(fighter.MoraleState);
        }

        /// <summary>A lone Retainer's order as a line from him: to where he is walking, or to the man he is going at.</summary>
        private void LoneLine(BattleState state, int i, Fighter fighter)
        {
            LineRenderer? line = _loneLines[i];
            if (line == null)
            {
                return;
            }

            LoneOrder order = fighter.LoneOrder;
            bool shown = fighter.IsAlive && !fighter.IsRouting && fighter.SquadId.IsNone && order.Kind != LoneOrderKind.None
                && (order.Kind != LoneOrderKind.Attack || _index.ContainsKey(order.Enemy.Value));
            line.enabled = shown;
            if (!shown)
            {
                return;
            }

            line.SetPosition(0, _bodies[i].position);
            line.SetPosition(1, order.Kind == LoneOrderKind.Attack ? _bodies[_index[order.Enemy.Value]].position : (Vector3)order.Target.ToVector2());
        }

        // A running man keeps his squad's colour, faded toward white; his bar says how far gone he is.
        private Color Colour(Fighter fighter)
        {
            if (!fighter.IsAlive)
            {
                return Fallen;
            }

            Color squad = SquadColour(fighter);
            return fighter.IsRouting ? Color.Lerp(squad, Color.white, 0.55f) : squad;
        }

        /// <summary>The colour of the squad he is in now; a Retainer alone takes his side's dark colour.</summary>
        private Color SquadColour(Fighter fighter)
        {
            if (_squadColours.TryGetValue(fighter.SquadId.Value, out Color c))
            {
                return c;
            }

            return fighter.IsRetainer ? Lone[fighter.SideId == 0 ? 0 : 1] : Color.grey;
        }

        private void FitCamera(MapDef map)
        {
            Camera? cam = viewCamera != null ? viewCamera : Camera.main;
            if (cam == null)
            {
                return;
            }

            float w = map.Width.ToFloat();
            float h = map.Height.ToFloat();
            cam.orthographic = true;
            cam.transform.position = new Vector3(w / 2, h / 2, -10);
            cam.transform.rotation = Quaternion.identity;
            cam.orthographicSize = Mathf.Max(h / 2, w / 2 / Mathf.Max(0.1f, cam.aspect)) * 1.05f;
            cam.backgroundColor = new Color(0.18f, 0.19f, 0.21f);
        }

        private SpriteRenderer Make(string name, Sprite sprite, Color colour, int order, Vector2 at, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            go.transform.localScale = new Vector3(size.x, size.y, 1);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = colour;
            renderer.sortingOrder = order;
            return renderer;
        }
    }
}
