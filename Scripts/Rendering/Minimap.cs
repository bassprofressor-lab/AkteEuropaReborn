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

    /// <summary>The fog, one pixel per cell, or null when it is switched off —
    /// see <see cref="MapEntityLayer.FogTexture"/>.</summary>
    private Func<Texture2D?>? _fog;

    /// <summary>
    /// DER EIGENE STARTPLATZ, in Kartenbildpunkten — oder null.
    ///
    /// <para>Gewuenscht als B7: »Bei Gefecht Startplatz wuerde ich gerne sehen
    /// wo dieser ist auf der Minimap.« Der Punkt steht FEST, er wandert nicht
    /// mit der Basis mit: er wird beim Start einmal genommen und bleibt, auch
    /// wenn die Basis faellt. Genau das ist der Zweck — man will wissen, wo man
    /// hergekommen ist.</para>
    ///
    /// <para>⚠ UNSERE ZUTAT, wie die Minimap selbst. Das Original hat keine
    /// stehende Uebersichtskarte (siehe Kopf dieser Datei), also auch keinen
    /// Startplatzmerker darauf.</para>
    /// </summary>
    private Func<Vector2?>? _home;

    /// <summary>Wie oft der Startplatzmerker gezeichnet wurde — fuer den
    /// Pruefstand, damit »gebaut« und »sichtbar« zwei Zahlen sind.</summary>
    public int HomeDrawn;

    /// <summary>How long a mark keeps glowing. Ours — the data says nothing
    /// about how long an alarm should last.</summary>
    public const float AlarmSeconds = 12f;

    public void Setup(Texture2D terrain, Vector2 mapPixels,
                      Func<List<(Vector2 Pos, int Owner, bool Building)>> dots,
                      Func<Rect2> view, Func<List<Alarm>> alarms, Action<Vector2> jump,
                      Func<Texture2D?>? fog = null, Func<Vector2?>? home = null)
    {
        _home = home;
        _terrain = terrain;
        _mapPixels = mapPixels;
        _dots = dots;
        _view = view;
        _alarms = alarms;
        _jump = jump;
        _fog = fog;
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

    /// <summary>What was drawn last time, so the map only repaints when there is
    /// something new to show.
    ///
    /// ⚠ It used to repaint <b>never</b>: <see cref="Setup"/> queued one redraw
    /// and nothing ever queued another, so the view frame stayed wherever the
    /// camera had been when the map loaded and the dots stayed with it. Reported
    /// as "Mini Map bleibt immer auf dem Ursprungspunkt stehen", and that is
    /// exactly what it was.</summary>
    private Rect2 _lastView;
    private float _dotTimer;

    /// <summary>How often the dots are refreshed. Ours: the frame follows the
    /// camera immediately because that is what the eye tracks, while units move
    /// slowly enough that five times a second is plenty and costs nothing.</summary>
    private const float DotRefresh = 0.2f;

    /// <summary>Harness counters: how often the camera moved and how often the
    /// map actually repainted. Before the fix the second one stayed at 1 no
    /// matter what the first did, which is the whole of the bug.</summary>
    public int ViewMoves { get; private set; }
    public int Repaints { get; private set; }

    /// <summary>How often the fog was actually drawn over the overview. Zero
    /// while the fog is switched off, and zero would also be the symptom if the
    /// texture never arrived — hence the counter.</summary>
    public int FogDrawn { get; private set; }

    public override void _Process(double delta)
    {
        if (_terrain == null) return;
        bool due = false;
        if (_view != null)
        {
            var r = _view();
            if (r != _lastView) { _lastView = r; due = true; ViewMoves++; }
        }
        _dotTimer -= (float)delta;
        if (_dotTimer <= 0f) { _dotTimer = DotRefresh; due = true; }
        if (due) QueueRedraw();
    }

    public override void _Draw()
    {
        if (_terrain == null) return;
        Repaints++;
        var full = new Rect2(Vector2.Zero, Size);

        DrawTextureRect(_terrain, full, false, new Color(0.75f, 0.78f, 0.8f));

        // The fog, over the terrain but UNDER the dots and the frame: what one
        // remembers of the ground is dimmed, what one is watching right now
        // stays bright, and what was never seen is black. The dots are drawn on
        // top because the entity layer already decides which of them may be
        // shown at all — dimming them a second time here would hide friendly
        // units in their own remembered territory.
        //
        // Fehlerliste Punkt 23. Nothing new is computed: this is the map's own
        // fog texture, one pixel per cell, stretched over the overview.
        var fog = _fog?.Invoke();
        if (fog != null)
        {
            DrawTextureRect(fog, full, false);
            FogDrawn++;
        }

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

        // Der eigene Startplatz (B7). Er liegt UNTER dem Sichtfenster und UEBER
        // den Punkten: ein Merker, den ein Einheitenpunkt verdecken kann, ist
        // genau dann weg, wenn man ihn sucht — naemlich wenn dort etwas steht.
        // Form: eine Raute mit dunklem Rand, damit sie auf hellem wie auf
        // dunklem Gelaende steht, plus ein kurzer Stiel nach unten.
        if (_home?.Invoke() is { } h)
        {
            var p = ToLocal(h);
            const float R = 5f;
            var pts = new[]
            {
                p + new Vector2(0, -R), p + new Vector2(R, 0),
                p + new Vector2(0, R), p + new Vector2(-R, 0),
            };
            var outline = new[] { pts[0], pts[1], pts[2], pts[3], pts[0] };
            DrawPolygon(pts, new[] { new Color(1f, 1f, 1f, 0.9f) });
            DrawPolyline(outline, new Color(0.1f, 0.1f, 0.12f, 0.95f), 1.5f);
            HomeDrawn++;
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
