namespace AkteEuropaReborn.Rendering;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// The overview map: the whole battlefield at a glance, above the side panel.
///
/// The terrain costs nothing to draw — it is the baked map picture the viewer
/// already has loaded, shown small. On top of it go the units and buildings as
/// dots in their faction's colour, the camera's own view as a frame, and the
/// most recent incidents as alarm marks. Clicking jumps the camera there.
///
/// <para><b>Ours, and worth saying so.</b> The original has no permanent
/// minimap: its command bar offers "Karte des Einsatzgebietes zeigen"
/// (@0x4ef50c), a map screen you call up. The side panel's recessed box is the
/// info display and stays that. This sits above the panel instead of taking it
/// over, so nothing the original does was displaced to make room.</para>
/// </summary>
public partial class Minimap : Control
{
    /// <summary>An incident worth a mark: where, and how long it still glows.</summary>
    public readonly struct Alarm
    {
        public Alarm(Vector2 world, float age, bool lost) { World = world; Age = age; Lost = lost; }
        public Vector2 World { get; }
        public float Age { get; }
        /// <summary>A loss, as opposed to something merely under fire.</summary>
        public bool Lost { get; }
    }

    private Texture2D? _terrain;
    private Vector2 _mapPixels;                 // size of the baked picture
    private Func<List<(Vector2 Pos, int Owner, bool Building)>>? _dots;
    private Func<Rect2>? _view;
    private Func<List<Alarm>>? _alarms;
    private Action<Vector2>? _jump;

    /// <summary>How long a mark keeps glowing. Ours — the data says nothing
    /// about how long an alarm should last.</summary>
    public const float AlarmSeconds = 12f;

    public void Setup(Texture2D terrain, Vector2 mapPixels,
                      Func<List<(Vector2 Pos, int Owner, bool Building)>> dots,
                      Func<Rect2> view, Func<List<Alarm>> alarms, Action<Vector2> jump)
    {
        _terrain = terrain;
        _mapPixels = mapPixels;
        _dots = dots;
        _view = view;
        _alarms = alarms;
        _jump = jump;
        QueueRedraw();
    }

    /// <summary>The height this map wants for a given width, so the caller can
    /// lay it out without knowing the map's shape.</summary>
    public float HeightFor(float width)
        => _mapPixels.X <= 0 ? 0 : Mathf.Round(width * _mapPixels.Y / _mapPixels.X);

    private Vector2 ToLocal(Vector2 world)
        => _mapPixels.X <= 0 || _mapPixels.Y <= 0
            ? Vector2.Zero
            : new Vector2(world.X / _mapPixels.X * Size.X, world.Y / _mapPixels.Y * Size.Y);

    private Vector2 ToWorld(Vector2 local)
        => Size.X <= 0 || Size.Y <= 0
            ? Vector2.Zero
            : new Vector2(local.X / Size.X * _mapPixels.X, local.Y / Size.Y * _mapPixels.Y);

    public override void _Draw()
    {
        if (_terrain == null) return;
        var full = new Rect2(Vector2.Zero, Size);

        DrawTextureRect(_terrain, full, false, new Color(0.75f, 0.78f, 0.8f));
        DrawRect(full, new Color(0.55f, 0.58f, 0.6f), false, 1);

        if (_dots != null)
            foreach (var (pos, owner, building) in _dots())
            {
                var c = MapEntityLayer.FactionColor(owner);
                var p = ToLocal(pos);
                float s = building ? 5 : 3;
                DrawRect(new Rect2(p - new Vector2(s, s) * 0.5f, new Vector2(s, s)), c);
            }

        if (_alarms != null)
            foreach (var a in _alarms())
            {
                float t = 1f - Mathf.Clamp(a.Age / AlarmSeconds, 0f, 1f);
                if (t <= 0f) continue;
                // a ring that widens as it fades, so the eye finds it moving
                float r = 3f + (1f - t) * 7f;
                var c = a.Lost ? new Color(1f, 0.25f, 0.2f, t) : new Color(1f, 0.85f, 0.3f, t);
                DrawArc(ToLocal(a.World), r, 0, Mathf.Tau, 16, c, 1.5f);
            }

        if (_view != null)
        {
            var r = _view();
            var tl = ToLocal(r.Position);
            var br = ToLocal(r.End);
            DrawRect(new Rect2(tl, br - tl), new Color(1, 1, 1, 0.85f), false, 1);
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            _jump?.Invoke(ToWorld(mb.Position));
            AcceptEvent();
        }
        else if (@event is InputEventMouseMotion { ButtonMask: MouseButtonMask.Left } mm)
        {
            _jump?.Invoke(ToWorld(mm.Position));     // dragging scrubs the view
            AcceptEvent();
        }
    }
}
