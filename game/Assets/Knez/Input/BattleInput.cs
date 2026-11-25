#nullable enable
using System.Collections.Generic;
using Knez.Game.View;
using Knez.Sim;
using Knez.Sim.Commands;
using UnityEngine;
using EntityId = Knez.Sim.EntityId; // Unity 6 has a UnityEngine.EntityId too
using UnityEngine.InputSystem;

namespace Knez.Game.Input
{
    /// <summary>
    /// The player's hands on the battle (M2 step 5). Every action becomes a command for the next tick through
    /// <see cref="BattleSimulation.Enqueue"/>; nothing here writes the state.
    /// <list type="bullet">
    /// <item>Left click: select a squad of yours (Shift adds); left drag: box-select; Esc: clear.</item>
    /// <item>Right click: move there; right drag: move there facing the drag's direction.</item>
    /// <item>1 / 2 / 3: line, shield wall, column (formations from the scenario's catalogue).</item>
    /// <item>Space: pause; = / −: faster / slower.</item>
    /// <item>L: battle log on/off; B: morale bars over the men on/off (M3 step 8).</item>
    /// <item>Clicks and the wheel over the HUD's panels (<see cref="BattleHud.Covers"/>) don't reach the map.</item>
    /// <item>WASD or arrows, or middle drag: pan; scroll: zoom.</item>
    /// </list>
    /// </summary>
    public sealed class BattleInput : MonoBehaviour
    {
        [SerializeField] private BattleRunner? runner;
        [SerializeField] private BattleView? view;
        [SerializeField] private BattleHud? hud;
        [SerializeField] private Camera? viewCamera;
        [Tooltip("The side the player commands.")]
        [SerializeField] private int side;

        private static readonly string[] FormationKeys = { "line", "shieldwall", "column" };
        private readonly List<EntityId> _selected = new List<EntityId>();
        private Vector2? _leftDown;
        private Vector2? _rightDown;
        private SpriteRenderer? _box;

        /// <summary>The selected squads, by id.</summary>
        public IReadOnlyList<EntityId> Selected => _selected;

        private void Start()
        {
            // Unity's == on purpose: an empty Inspector field is a "fake null" object that ?? doesn't see as null.
            if (runner == null)
            {
                runner = GetComponent<BattleRunner>();
            }

            if (view == null)
            {
                view = GetComponent<BattleView>();
            }

            if (hud == null)
            {
                hud = GetComponent<BattleHud>();
            }

            if (viewCamera == null)
            {
                viewCamera = UnityEngine.Camera.main;
            }
            var box = new GameObject("Selection box");
            box.transform.SetParent(transform, false);
            _box = box.AddComponent<SpriteRenderer>();
            _box.sprite = ShapeSprites.Square;
            _box.color = new Color(1f, 1f, 1f, 0.18f);
            _box.sortingOrder = 20;
            _box.enabled = false;
        }

        private void Update()
        {
            BattleSimulation? sim = runner != null ? runner.Simulation : null;
            Mouse? mouse = Mouse.current;
            Keyboard? keyboard = Keyboard.current;
            if (sim == null || runner == null || viewCamera == null || mouse == null || keyboard == null)
            {
                return;
            }

            Keys(sim, keyboard);
            bool overHud = hud != null && hud.Covers(mouse.position.ReadValue());
            PanAndZoom(mouse, keyboard, overHud);
            Vector2 world = viewCamera.ScreenToWorldPoint(mouse.position.ReadValue());

            if (mouse.leftButton.wasPressedThisFrame && !overHud)
            {
                _leftDown = world;
            }

            if (_leftDown.HasValue && _box != null)
            {
                Rect rect = Box(_leftDown.Value, world);
                _box.enabled = rect.width > OrderBuilder.DragThreshold || rect.height > OrderBuilder.DragThreshold;
                _box.transform.position = rect.center;
                _box.transform.localScale = new Vector3(rect.width, rect.height, 1);
            }

            if (mouse.leftButton.wasReleasedThisFrame && _leftDown.HasValue)
            {
                Select(sim.State, _leftDown.Value, world, keyboard.shiftKey.isPressed);
                _leftDown = null;
                if (_box != null)
                {
                    _box.enabled = false;
                }
            }

            if (mouse.rightButton.wasPressedThisFrame && !overHud)
            {
                _rightDown = world;
            }

            if (mouse.rightButton.wasReleasedThisFrame && _rightDown.HasValue)
            {
                foreach (ICommand command in OrderBuilder.Moves(sim.State, _selected, _rightDown.Value, world, side))
                {
                    runner.Enqueue(command);
                }

                _rightDown = null;
            }
        }

        private void Select(BattleState state, Vector2 from, Vector2 to, bool add)
        {
            if (!add)
            {
                _selected.Clear();
            }

            Rect rect = Box(from, to);
            List<EntityId> picked = rect.width > OrderBuilder.DragThreshold || rect.height > OrderBuilder.DragThreshold
                ? SquadPicker.InBox(state, rect, side)
                : One(SquadPicker.At(state, to, side));
            foreach (EntityId id in picked)
            {
                if (!_selected.Contains(id))
                {
                    _selected.Add(id);
                }
            }

            _selected.Sort((a, b) => a.Value.CompareTo(b.Value));
            if (view != null)
            {
                view.SetSelection(_selected);
            }
        }

        private void Keys(BattleSimulation sim, Keyboard keyboard)
        {
            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                runner!.TogglePause();
            }

            if (keyboard.equalsKey.wasPressedThisFrame || keyboard.numpadPlusKey.wasPressedThisFrame)
            {
                runner!.Clock.Faster();
            }

            if (keyboard.minusKey.wasPressedThisFrame || keyboard.numpadMinusKey.wasPressedThisFrame)
            {
                runner!.Clock.Slower();
            }

            if (keyboard.lKey.wasPressedThisFrame && hud != null)
            {
                hud.ShowLog = !hud.ShowLog;
            }

            if (keyboard.bKey.wasPressedThisFrame && view != null)
            {
                view.ShowMoraleBars = !view.ShowMoraleBars;
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                _selected.Clear();
                if (view != null)
                {
                    view.SetSelection(_selected);
                }
            }

            Key[] digits = { Key.Digit1, Key.Digit2, Key.Digit3 };
            for (int i = 0; i < digits.Length; i++)
            {
                if (keyboard[digits[i]].wasPressedThisFrame && sim.State.Scenario.FindFormation(FormationKeys[i]) != null)
                {
                    foreach (EntityId id in _selected)
                    {
                        runner!.Enqueue(new SetFormationCommand(side, id, FormationKeys[i]));
                    }
                }
            }
        }

        private void PanAndZoom(Mouse mouse, Keyboard keyboard, bool overHud)
        {
            Transform cam = viewCamera!.transform;
            float speed = viewCamera.orthographicSize * 1.2f * Time.unscaledDeltaTime;
            Vector2 pan = Vector2.zero;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) pan.x -= 1;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) pan.x += 1;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) pan.y -= 1;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) pan.y += 1;
            cam.position += (Vector3)(pan * speed);
            if (mouse.middleButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                float metresPerPixel = 2 * viewCamera.orthographicSize / Screen.height;
                cam.position -= (Vector3)(delta * metresPerPixel);
            }

            float scroll = overHud ? 0 : mouse.scroll.ReadValue().y; // over the HUD the wheel scrolls its panels
            if (scroll != 0)
            {
                viewCamera.orthographicSize = Mathf.Clamp(viewCamera.orthographicSize * (scroll > 0 ? 0.9f : 1.1f), 3f, 150f);
            }
        }

        private static Rect Box(Vector2 a, Vector2 b) => Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));

        private static List<EntityId> One(EntityId id) => id.IsNone ? new List<EntityId>() : new List<EntityId> { id };
    }
}
