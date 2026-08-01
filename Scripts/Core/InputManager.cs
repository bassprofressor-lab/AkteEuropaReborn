#nullable disable
namespace AkteEuropaReborn.Core;

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;

[GlobalClass]
public partial class InputManager : Node
{
    public static InputManager Instance { get; private set; }

    public enum Action
    {
        MoveUp, MoveDown, MoveLeft, MoveRight,
        ZoomIn, ZoomOut,
        Select, Deselect, SelectAdd,
        Command,
        Group1, Group2, Group3, Group4, Group5,
        Group6, Group7, Group8, Group9, Group0,
        Pause,
        SpeedUp, SpeedDown
    }

    private readonly Dictionary<Action, string> _actionMap = new()
    {
        { Action.MoveUp, "move_up" },
        { Action.MoveDown, "move_down" },
        { Action.MoveLeft, "move_left" },
        { Action.MoveRight, "move_right" },
        { Action.ZoomIn, "zoom_in" },
        { Action.ZoomOut, "zoom_out" },
        { Action.Select, "select" },
        { Action.Deselect, "deselect" },
        { Action.SelectAdd, "select_add" },
        { Action.Command, "command" },
        { Action.Group1, "group_1" },
        { Action.Group2, "group_2" },
        { Action.Group3, "group_3" },
        { Action.Group4, "group_4" },
        { Action.Group5, "group_5" },
        { Action.Group6, "group_6" },
        { Action.Group7, "group_7" },
        { Action.Group8, "group_8" },
        { Action.Group9, "group_9" },
        { Action.Group0, "group_0" },
        { Action.Pause, "pause" },
        { Action.SpeedUp, "speed_up" },
        { Action.SpeedDown, "speed_down" }
    };

    public override void _Ready()
    {
        Instance = this;
        EnsureActions();
    }

    private void EnsureActions()
    {
        foreach (var kvp in _actionMap)
        {
            if (!InputMap.HasAction(kvp.Value))
                InputMap.AddAction(kvp.Value);
        }

        var defaults = new Dictionary<string, Godot.Collections.Array<InputEvent>>
        {
            { "move_up", new() { new InputEventKey { Keycode = Key.W }, new InputEventKey { Keycode = Key.Up } } },
            { "move_down", new() { new InputEventKey { Keycode = Key.S }, new InputEventKey { Keycode = Key.Down } } },
            { "move_left", new() { new InputEventKey { Keycode = Key.A }, new InputEventKey { Keycode = Key.Left } } },
            { "move_right", new() { new InputEventKey { Keycode = Key.D }, new InputEventKey { Keycode = Key.Right } } },
            { "zoom_in", new() { new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp } } },
            { "zoom_out", new() { new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown } } },
            { "select", new() { new InputEventMouseButton { ButtonIndex = MouseButton.Left } } },
            { "command", new() { new InputEventMouseButton { ButtonIndex = MouseButton.Right } } },
            { "deselect", new() { new InputEventMouseButton { ButtonIndex = MouseButton.Middle }, new InputEventKey { Keycode = Key.Escape } } },
            { "select_add", new() { new InputEventKey { Keycode = Key.Shift } } },
            { "group_1", new() { new InputEventKey { Keycode = Key.Key1 } } },
            { "group_2", new() { new InputEventKey { Keycode = Key.Key2 } } },
            { "group_3", new() { new InputEventKey { Keycode = Key.Key3 } } },
            { "group_4", new() { new InputEventKey { Keycode = Key.Key4 } } },
            { "group_5", new() { new InputEventKey { Keycode = Key.Key5 } } },
            { "group_6", new() { new InputEventKey { Keycode = Key.Key6 } } },
            { "group_7", new() { new InputEventKey { Keycode = Key.Key7 } } },
            { "group_8", new() { new InputEventKey { Keycode = Key.Key8 } } },
            { "group_9", new() { new InputEventKey { Keycode = Key.Key9 } } },
            { "group_0", new() { new InputEventKey { Keycode = Key.Key0 } } },
            { "pause", new() { new InputEventKey { Keycode = Key.Escape }, new InputEventKey { Keycode = Key.P } } },
            { "speed_up", new() { new InputEventKey { Keycode = Key.Equal } } },
            { "speed_down", new() { new InputEventKey { Keycode = Key.Minus } } }
        };

        foreach (var kvp in defaults)
        {
            if (InputMap.ActionGetEvents(kvp.Key).Count == 0)
            {
                foreach (var ev in kvp.Value)
                    InputMap.ActionAddEvent(kvp.Key, ev);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsActionPressed(Action action) => Input.IsActionPressed(_actionMap[action]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsActionJustPressed(Action action) => Input.IsActionJustPressed(_actionMap[action]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsActionJustReleased(Action action) => Input.IsActionJustReleased(_actionMap[action]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float GetActionStrength(Action action) => Input.GetActionStrength(_actionMap[action]);

    public Vector2 GetMoveVector()
    {
        var vec = Vector2.Zero;
        if (IsActionPressed(Action.MoveLeft)) vec.X -= 1;
        if (IsActionPressed(Action.MoveRight)) vec.X += 1;
        if (IsActionPressed(Action.MoveUp)) vec.Y -= 1;
        if (IsActionPressed(Action.MoveDown)) vec.Y += 1;
        return vec.Normalized();
    }

    public float GetZoomDelta()
    {
        float zoom = 0;
        if (IsActionJustPressed(Action.ZoomIn)) zoom += 1;
        if (IsActionJustPressed(Action.ZoomOut)) zoom -= 1;
        return zoom;
    }

    public int GetNumberKeyPressed()
    {
        for (int i = 0; i <= 9; i++)
        {
            var action = (Action)((int)Action.Group1 + i);
            if (IsActionJustPressed(action)) return i == 0 ? 10 : i;
        }
        return -1;
    }

    public bool IsMouseOverUI => false;

    public Vector2 MouseScreenPosition => GetViewport().GetMousePosition();

    public Vector2 MouseWorldPosition
    {
        get
        {
            var camera = Game.Instance?.GameCamera;
            if (camera != null)
                return camera.GetGlobalMousePosition();
            return MouseScreenPosition;
        }
    }

    public void SetActionBinding(Action action, Godot.Collections.Array<InputEvent> events)
    {
        InputMap.ActionEraseEvents(_actionMap[action]);
        foreach (var ev in events)
            InputMap.ActionAddEvent(_actionMap[action], ev);
    }

    public Godot.Collections.Array<InputEvent> GetActionBindings(Action action) => InputMap.ActionGetEvents(_actionMap[action]);
}