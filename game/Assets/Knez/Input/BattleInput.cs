#nullable enable
using System.Collections.Generic;
using Knez.Game.View;
using Knez.Sim;
using Knez.Sim.Commands;
using Knez.Sim.Units;
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
    /// <item>Retainers (M3 step 10): drag one onto a squad of yours to join it, onto open ground to leave his squad and
    /// walk there; click one to select him, then right-click: ground — walk there (drag: facing), an enemy — go at him, a
    /// squad of yours — join it; X: leave his squad; Q (or the HUD's button): his ability (M3 step 11).</item>
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
        private EntityId _draggedRetainer = EntityId.None;
        private EntityId _selectedRetainer = EntityId.None;
        private LineRenderer? _dragLine;
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
            _dragLine = new GameObject("Retainer drag").AddComponent<LineRenderer>();
            _dragLine.transform.SetParent(transform, false);
            _dragLine.material = ShapeSprites.LineMaterial;
            _dragLine.startColor = _dragLine.endColor = new Color(0.83f, 0.63f, 0.09f, 0.9f);
            _dragLine.startWidth = _dragLine.endWidth = 0.12f;
            _dragLine.sortingOrder = 20;
            _dragLine.positionCount = 2;
            _dragLine.useWorldSpace = true;
            _dragLine.enabled = false;
        }

        /// <summary>The selected Retainer, or none.</summary>
        public EntityId SelectedRetainer => _selectedRetainer;

        private void Update()
        {
            BattleSimulation? sim = runner != null ? runner.Simulation : null;
            Mouse? mouse = Mouse.current;
            Keyboard? keyboard = Keyboard.current;
            if (sim == null || runner == null || viewCamera == null || mouse == null || keyboard == null)
            {
                return;
            }

            DropDeadRetainer(sim.State);
            if (hud != null)
            {
                hud.SelectedRetainer = _selectedRetainer; // the HUD draws his ability's button
                hud.PlayerSide = side;
            }

            Keys(sim, keyboard);
            bool overHud = hud != null && hud.Covers(mouse.position.ReadValue());
            PanAndZoom(mouse, keyboard, overHud);
            Vector2 world = viewCamera.ScreenToWorldPoint(mouse.position.ReadValue());

            if (mouse.leftButton.wasPressedThisFrame && !overHud)
            {
                _leftDown = world;
                _draggedRetainer = RetainerOrders.At(sim.State, world, side); // a press on a Retainer drags him
            }

            if (!_draggedRetainer.IsNone && _leftDown.HasValue && _dragLine != null)
            {
                Fighter? dragged = sim.State.FindFighter(_draggedRetainer);
                _dragLine.enabled = dragged != null && (world - _leftDown.Value).magnitude >= OrderBuilder.DragThreshold;
                if (dragged != null)
                {
                    _dragLine.SetPosition(0, dragged.Position.ToVector2());
                    _dragLine.SetPosition(1, world);
                }
            }
            else if (_leftDown.HasValue && _box != null)
            {
                Rect rect = Box(_leftDown.Value, world);
                _box.enabled = rect.width > OrderBuilder.DragThreshold || rect.height > OrderBuilder.DragThreshold;
                _box.transform.position = rect.center;
                _box.transform.localScale = new Vector3(rect.width, rect.height, 1);
            }

            if (mouse.leftButton.wasReleasedThisFrame && _leftDown.HasValue)
            {
                if (!_draggedRetainer.IsNone)
                {
                    if ((world - _leftDown.Value).magnitude < OrderBuilder.DragThreshold)
                    {
                        SelectRetainer(_draggedRetainer);
                    }
                    else
                    {
                        foreach (ICommand command in RetainerOrders.Drop(sim.State, _draggedRetainer, _leftDown.Value, world, side))
                        {
                            runner.Enqueue(command);
                        }
                    }
                }
                else
                {
                    Select(sim.State, _leftDown.Value, world, keyboard.shiftKey.isPressed);
                    SelectRetainer(EntityId.None);
                }

                _leftDown = null;
                _draggedRetainer = EntityId.None;
                if (_box != null)
                {
                    _box.enabled = false;
                }

                if (_dragLine != null)
                {
                    _dragLine.enabled = false;
                }
            }

            if (mouse.rightButton.wasPressedThisFrame && !overHud)
            {
                _rightDown = world;
            }

            if (mouse.rightButton.wasReleasedThisFrame && _rightDown.HasValue)
            {
                List<ICommand> commands = !_selectedRetainer.IsNone
                    ? RetainerOrders.Order(sim.State, _selectedRetainer, _rightDown.Value, world, side)
                    : OrderBuilder.Moves(sim.State, _selected, _rightDown.Value, world, side);
                foreach (ICommand command in commands)
                {
                    runner.Enqueue(command);
                }

                _rightDown = null;
            }
        }

        /// <summary>Selects one Retainer (none: clears it); squads and a Retainer aren't selected together.</summary>
        private void SelectRetainer(EntityId retainer)
        {
            _selectedRetainer = retainer;
            if (!retainer.IsNone)
            {
                _selected.Clear();
            }

            if (view != null)
            {
                view.SetSelection(_selected);
                view.SetSelectedRetainer(retainer);
            }
        }

        /// <summary>A selected Retainer who fell or fled is no longer selected.</summary>
        private void DropDeadRetainer(BattleState state)
        {
            Fighter? selected = _selectedRetainer.IsNone ? null : state.FindFighter(_selectedRetainer);
            if (!_selectedRetainer.IsNone && (selected == null || !selected.IsAlive))
            {
                SelectRetainer(EntityId.None);
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

            if (keyboard.xKey.wasPressedThisFrame && !_selectedRetainer.IsNone)
            {
                runner!.Enqueue(new DetachRetainerCommand(side, _selectedRetainer)); // X: he leaves his squad
            }

            if (keyboard.qKey.wasPressedThisFrame && !_selectedRetainer.IsNone)
            {
                runner!.Enqueue(new UseAbilityCommand(side, _selectedRetainer)); // Q: his ability
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                SelectRetainer(EntityId.None);
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
