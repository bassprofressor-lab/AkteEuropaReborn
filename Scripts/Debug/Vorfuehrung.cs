using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace AkteEuropaReborn.Debug;

/// <summary>
/// ⭐ 14.11.2026 — <b>DER VORFÜHRER</b>, ein Aufnahmehelfer für Videomaterial:
/// <c>--vorfuehrung=&lt;skriptdatei&gt;</c>.
///
/// <para>Er bedient das Spiel wie eine Hand: eine gezeichnete Maus fährt weich über den
/// Schirm, klickt, zieht, dreht das Rad und drückt Tasten — nach einer Textdatei, Zeile für
/// Zeile. Gebraucht wird das für Godots Filmschreiber (<c>--write-movie</c>): der zeichnet
/// nur das Spielbild, der echte Mauszeiger (ein Hardwarezeiger, siehe
/// <see cref="UI.GameCursors"/>) fehlt darin, und ein Prüflauf kann nicht klicken. Darum
/// zeichnet der Vorführer den Zeiger SELBST (das aktuelle Zeigerbild des Spiels, sonst einen
/// Pfeil) und schickt die Eingaben über <c>Input.ParseInputEvent</c> wie echte.</para>
///
/// <para>Skriptzeilen (Koordinaten im Spielbild, Zeiten in Sekunden; <c>#</c> = Kommentar):
/// <c>warte s</c> · <c>zu x y [s]</c> (weich hinfahren) · <c>klick [rechts]</c> ·
/// <c>doppelklick</c> · <c>ziehe x y [s]</c> (links gedrückt hinfahren) · <c>halte</c> /
/// <c>los</c> · <c>taste Name</c> (z. B. Enter, Escape, F4, Ctrl+K) · <c>text Wort</c> ·
/// <c>rad hoch|runter [n]</c> · <c>zeiger aus|an</c> · <c>ende</c> (Spiel beenden).</para>
///
/// <para>⚠ Nur mit dem Schalter da; ohne ihn ändert sich nichts. Echte Mausbewegungen über
/// dem Fenster mischen sich ein — während einer Aufnahme die Maus vom Fenster lassen.</para>
/// </summary>
public partial class Vorfuehrung : CanvasLayer
{
    private readonly List<string[]> _schritte = new();
    private int _i;
    private float _warte;
    private Vector2 _pos = new(960, 540), _von, _nach;
    private float _fahrt, _fahrtDauer;
    private bool _gedrueckt, _zeigen = true;
    private MouseButton? _losNach;
    private readonly List<(Vector2 P, float T)> _klicks = new();
    private Node2D _bild = null!;

    private static Vorfuehrung? _laufend;

    /// <summary>Die Mausposition im Raum von <paramref name="ci"/>. Ohne Vorführung genau
    /// <c>GetLocalMousePosition()</c>; mit ihr die gezeichnete Maus — der Fenster-Weg fragt die
    /// ECHTE Windows-Maus ab und wüsste nichts von der vorgeführten.</summary>
    public static Vector2 LokaleMaus(CanvasItem ci)
        => _laufend == null ? ci.GetLocalMousePosition()
                            : ci.GetGlobalTransformWithCanvas().AffineInverse() * _laufend._pos;

    /// <summary>Aus GameManager: den Vorführer anlegen, wenn der Schalter da ist.</summary>
    public static void WennVerlangt(Node wurzel)
    {
        foreach (string a in Core.CommandLine.Args)
            if (a.StartsWith("--vorfuehrung="))
            {
                var v = new Vorfuehrung();
                if (v.Laden(a["--vorfuehrung=".Length..])) wurzel.CallDeferred(Node.MethodName.AddChild, v);
                return;
            }
    }

    private bool Laden(string datei)
    {
        if (!FileAccess.FileExists(datei)) { GD.PrintErr($"vorfuehrung: {datei} fehlt"); return false; }
        foreach (string z in FileAccess.GetFileAsString(datei).Split('\n'))
        {
            string s = z.Trim();
            if (s.Length == 0 || s.StartsWith('#')) continue;
            _schritte.Add(s.Split(' ', 2) is { Length: 2 } t && t[0] == "text"
                ? t : s.Split(' ', System.StringSplitOptions.RemoveEmptyEntries));
        }
        GD.Print($"vorfuehrung: {_schritte.Count} Schritte aus {datei}");
        return true;
    }

    public override void _Ready()
    {
        Layer = 128;
        _laufend = this;
        ProcessMode = ProcessModeEnum.Always;
        _bild = new Zeichner { Herr = this };
        AddChild(_bild);
    }

    private static float F(string[] t, int i, float vorgabe)
        => t.Length > i && float.TryParse(t[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : vorgabe;

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        for (int k = _klicks.Count - 1; k >= 0; k--)
        {
            _klicks[k] = (_klicks[k].P, _klicks[k].T + dt);
            if (_klicks[k].T > 0.45f) _klicks.RemoveAt(k);
        }
        _bild.QueueRedraw();

        if (_losNach is { } lb && _warte < 0.08f) { Knopf(lb, false); _losNach = null; }
        if (_fahrtDauer > 0)
        {
            _fahrt += dt;
            float u = Mathf.Clamp(_fahrt / _fahrtDauer, 0, 1);
            float e = u * u * (3 - 2 * u);   // weich an, weich aus
            Bewege(_von.Lerp(_nach, e));
            if (u >= 1) _fahrtDauer = 0;
            return;
        }
        if (_warte > 0) { _warte -= dt; return; }
        if (_i >= _schritte.Count) return;

        var t = _schritte[_i++];
        GD.Print($"vorfuehrung: [{_i}/{_schritte.Count}] {string.Join(' ', t)}");
        switch (t[0])
        {
            case "warte": _warte = F(t, 1, 1); break;
            case "zu":
            case "ziehe":
                if (t[0] == "ziehe" && !_gedrueckt) Knopf(MouseButton.Left, true);
                _von = _pos; _nach = new Vector2(F(t, 1, _pos.X), F(t, 2, _pos.Y));
                _fahrt = 0; _fahrtDauer = Mathf.Max(0.05f, F(t, 3, 0.6f));
                if (t[0] == "ziehe") _schritte.Insert(_i, new[] { "los" });
                break;
            case "klick":
                var b = t.Length > 1 && t[1] == "rechts" ? MouseButton.Right : MouseButton.Left;
                Knopf(b, true); _losNach = b; _warte = 0.15f; break;
            case "doppelklick":
                Knopf(MouseButton.Left, true); Knopf(MouseButton.Left, false);
                Knopf(MouseButton.Left, true, doppel: true); Knopf(MouseButton.Left, false); _warte = 0.15f; break;
            case "halte": Knopf(MouseButton.Left, true); break;
            case "los": if (_gedrueckt) Knopf(MouseButton.Left, false); break;
            case "rad":
                var r = t.Length > 1 && t[1] == "runter" ? MouseButton.WheelDown : MouseButton.WheelUp;
                for (int n = 0; n < (int)F(t, 2, 1); n++) { Knopf(r, true); Knopf(r, false); }
                _warte = 0.1f; break;
            case "taste": Taste(t.Length > 1 ? t[1] : ""); _warte = 0.1f; break;
            case "text":
                foreach (char c in t.Length > 1 ? t[1] : "")
                {
                    var ev = new InputEventKey { Pressed = true, Unicode = c, Keycode = Key.None };
                    Input.ParseInputEvent(ev);
                    Input.ParseInputEvent(new InputEventKey { Pressed = false, Unicode = c });
                }
                _warte = 0.1f; break;
            case "zeiger": _zeigen = t.Length < 2 || t[1] != "aus"; break;
            case "ende": GetTree().Quit(); break;
            default: GD.PrintErr($"vorfuehrung: unbekannter Schritt »{t[0]}«"); break;
        }
    }

    private void Bewege(Vector2 p)
    {
        var ev = new InputEventMouseMotion
        {
            Position = p, GlobalPosition = p, Relative = p - _pos,
            ButtonMask = _gedrueckt ? MouseButtonMask.Left : 0,
        };
        _pos = p;
        Input.ParseInputEvent(ev);
    }

    private void Knopf(MouseButton b, bool unten, bool doppel = false)
    {
        if (b == MouseButton.Left) _gedrueckt = unten;
        if (unten && b is MouseButton.Left or MouseButton.Right) _klicks.Add((_pos, 0));
        Input.ParseInputEvent(new InputEventMouseButton
        {
            Position = _pos, GlobalPosition = _pos, ButtonIndex = b, Pressed = unten, DoubleClick = doppel,
            ButtonMask = _gedrueckt ? MouseButtonMask.Left : 0,
        });
    }

    private static void Taste(string name)
    {
        bool ctrl = false, shift = false;
        foreach (string teil in name.Split('+'))
        {
            if (teil == "Ctrl") { ctrl = true; continue; }
            if (teil == "Shift") { shift = true; continue; }
            Key k = OS.FindKeycodeFromString(teil);
            if (k == Key.None) { GD.PrintErr($"vorfuehrung: Taste »{teil}« unbekannt"); return; }
            foreach (bool p in new[] { true, false })
                Input.ParseInputEvent(new InputEventKey
                {
                    Pressed = p, Keycode = k, PhysicalKeycode = k, CtrlPressed = ctrl, ShiftPressed = shift,
                    Unicode = !ctrl && teil.Length == 1 ? char.ToLowerInvariant(teil[0]) : 0,
                });
        }
    }

    /// <summary>Zeichnet Zeiger und Klickringe über alles.</summary>
    private partial class Zeichner : Node2D
    {
        public Vorfuehrung Herr = null!;
        public override void _Draw()
        {
            foreach (var (p, t) in Herr._klicks)
            {
                float u = t / 0.45f;
                DrawArc(p, 6 + 22 * u, 0, Mathf.Tau, 32, new Color(1, 0.85f, 0.3f, 1 - u), 3);
            }
            if (!Herr._zeigen) return;
            var bild = UI.GameCursors.AktuellesBild;
            if (bild != null) { DrawTexture(bild, Herr._pos - UI.GameCursors.AktuellerHot); return; }
            var p0 = Herr._pos;
            Vector2[] pfeil = { p0, p0 + new Vector2(0, 26), p0 + new Vector2(7, 20), p0 + new Vector2(12, 31),
                                p0 + new Vector2(17, 29), p0 + new Vector2(12, 18), p0 + new Vector2(21, 18) };
            DrawColoredPolygon(pfeil, Colors.White);
            var rand = new Vector2[pfeil.Length + 1];
            pfeil.CopyTo(rand, 0); rand[^1] = pfeil[0];
            DrawPolyline(rand, Colors.Black, 2);
        }
    }
}
