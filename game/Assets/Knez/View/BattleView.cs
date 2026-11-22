#nullable enable
using System.Collections.Generic;
using Knez.Sim;
using Knez.Sim.Defs;
using Knez.Sim.Orders;
using Knez.Sim.Units;
using UnityEngine;
using EntityId = Knez.Sim.EntityId; // Unity 6 has a UnityEngine.EntityId too

namespace Knez.Game.View
{
    /// <summary>
    /// Draws a <see cref="BattleRunner"/>'s battle top-down (M2 step 4): walls, fighters as discs in their squad's colour
    /// with a tick for their facing (grey once fallen), each squad's anchor with a facing marker, and the route still
    /// ahead of a squad on the march. Reads the state only. Positions are interpolated between the last two ticks by the
    /// clock's alpha, so motion is smooth at any frame rate. The camera is fitted to the map.
    /// </summary>
    public sealed class BattleView : MonoBehaviour
    {
        [SerializeField] private BattleRunner? runner;
        [SerializeField] private Camera? viewCamera;

        private static readonly Color[] SideZero = { new Color(0.20f, 0.45f, 0.85f), new Color(0.15f, 0.60f, 0.45f), new Color(0.45f, 0.35f, 0.80f), new Color(0.10f, 0.55f, 0.75f) };
        private static readonly Color[] SideOne = { new Color(0.80f, 0.25f, 0.20f), new Color(0.85f, 0.55f, 0.15f), new Color(0.65f, 0.20f, 0.45f), new Color(0.70f, 0.40f, 0.25f) };
        private static readonly Color Fallen = new Color(0.55f, 0.55f, 0.55f, 0.6f);
        private static readonly Color Ground = new Color(0.93f, 0.89f, 0.82f);
        private static readonly Color Wall = new Color(0.38f, 0.32f, 0.27f);

        private readonly List<Transform> _bodies = new List<Transform>();
        private readonly List<SpriteRenderer> _bodyRenderers = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> _halos = new List<SpriteRenderer>();
        private readonly List<EntityId> _selection = new List<EntityId>();
        private readonly List<Transform> _anchors = new List<Transform>();
        private readonly List<LineRenderer> _routes = new List<LineRenderer>();
        private readonly Dictionary<int, Color> _squadColours = new Dictionary<int, Color>(); // view-side cache keyed by id
        private Vector2[] _previous = new Vector2[0];
        private Vector2[] _current = new Vector2[0];
        private float[] _previousFacing = new float[0];
        private float[] _currentFacing = new float[0];

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
                Make("Wall", ShapeSprites.Square, Wall, 0, (min + max) / 2, max - min);
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
                SpriteRenderer halo = Make("Selected", ShapeSprites.Disc, new Color(1f, 1f, 0.85f, 0.9f), 1, Vector2.zero, Vector2.one);
                halo.transform.SetParent(body.transform, false);
                halo.transform.localPosition = Vector3.zero;
                halo.transform.localScale = new Vector3(1.45f, 1.45f, 1);
                halo.enabled = false;
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
                _halos[i].enabled = fighter.IsAlive && _selection.Contains(fighter.SquadId);
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

        private Color Colour(Fighter fighter) =>
            !fighter.IsAlive ? Fallen : _squadColours.TryGetValue(fighter.SquadId.Value, out Color c) ? c : Fallen;

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
