#nullable enable
namespace AkteEuropaReborn.Network;

using System;
using Godot;

/// <summary>
/// ⭐ 11.11.2026 (Online 2b) — <b>DIE NETZFENSTER IM SPIEL</b>, gehalten vom Taktgeber.
///
/// <list type="bullet">
///   <item><b>»Warten auf …« — Fensterart 42 des Originals</b> (<c>0x4861A0</c>, 300 × 160):
///   »Warten auf« + Namen der fehlenden Spieler, beim Gastgeber »Drücken Sie Ctrl-K, um
///   ihn/sie auszuschalten.« (Texte <c>0x5028FC…0x5028A0</c>). Erscheint nach 2000 ms
///   (die Frist der Sperre <c>0x4C5F50</c>). Ein Mitspieler sieht zusätzlich die Zeile des
///   Originals »Drücken Sie CTRL-E, um das Spiel zu beenden.« (<c>0x4FB59C</c>).</item>
///   <item><b>Chatzeile — Fensterart 36</b> (<c>0x47CD60</c>, 300 × 60 bei (50, 20)):
///   Enter öffnet sie (nur im Netz und ab Takt 20, <c>0x413015</c>), Enter schickt, Esc
///   verwirft.</item>
///   <item><b>Spielerliste</b> (F4 halten): Name, Ping, Zustand. ⚠ UNSERE ZUTAT — das
///   Original hat keine; Tab ist dort die Balkenumschaltung, darum F4.</item>
/// </list>
/// <para>Die Fenster ändern die Simulation nicht; Ctrl-K/Ctrl-E rufen den Taktgeber, der
/// über die Leitung ausschaltet (gleicher Takt auf allen Maschinen).</para>
/// </summary>
public partial class NetzOberflaeche : CanvasLayer
{
    public Action? OnCtrlK, OnCtrlE;
    public Func<int>? Takt;

    private PanelContainer _wait = null!, _list = null!;
    private Label _waitText = null!;
    private GridContainer _grid = null!;
    private LineEdit _chat = null!;

    public override void _Ready()
    {
        Layer = 60;
        ProcessMode = ProcessModeEnum.Always;

        _wait = Panel(new Vector2(380, 0));
        _wait.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _wait.Position = new Vector2(-190, 90);
        _waitText = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _wait.AddChild(_waitText);
        _wait.Visible = false;
        AddChild(_wait);

        _list = Panel(new Vector2(460, 0));
        _list.SetAnchorsPreset(Control.LayoutPreset.Center);
        _list.Position = new Vector2(-230, -120);
        _grid = new GridContainer { Columns = 3 };
        _grid.AddThemeConstantOverride("h_separation", 28);
        _grid.AddThemeConstantOverride("v_separation", 6);
        _list.AddChild(_grid);
        _list.Visible = false;
        AddChild(_list);

        _chat = new LineEdit
        {
            Position = new Vector2(50, 20),
            Size = new Vector2(300, 34),
            MaxLength = 200,
            PlaceholderText = "Nachricht — Enter schickt, Esc verwirft",
            Visible = false,
        };
        _chat.TextSubmitted += t =>
        {
            NetworkManager.SendChat(t);
            CloseChat();
        };
        AddChild(_chat);
    }

    private static PanelContainer Panel(Vector2 min)
    {
        var p = new PanelContainer { CustomMinimumSize = min };
        var sb = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.06f, 0.08f, 0.92f),
            BorderColor = new Color(0.55f, 0.58f, 0.6f),
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 10, ContentMarginBottom = 10,
        };
        sb.SetBorderWidthAll(2);
        p.AddThemeStyleboxOverride("panel", sb);
        return p;
    }

    /// <summary>Das Wartefenster zeigen oder verbergen.</summary>
    public void Warten(string[]? namen, bool gastgeber, ulong ms)
    {
        _waitMs = ms;
        if (namen == null || namen.Length == 0 || ms < 2000) { _wait.Visible = false; return; }
        string wer = string.Join(", ", namen);
        string pron = namen.Length == 1 ? "ihn/sie" : "sie";
        _waitText.Text = $"Warten auf\n{wer}\n({ms / 1000} s)\n\n" +
                         (gastgeber ? $"Drücken Sie Ctrl-K, um {pron} auszuschalten."
                                    : "Drücken Sie CTRL-E, um das Spiel zu beenden.");
        _wait.Visible = true;
    }

    public bool ChatOffen => _chat.Visible;

    private void CloseChat()
    {
        _chat.Text = "";
        _chat.Visible = false;
        _chat.ReleaseFocus();
    }

    public override void _Input(InputEvent e)
    {
        if (e is not InputEventKey k) return;

        // Spielerliste: solange F4 gehalten wird.
        if (k.Keycode == Key.F4 && !k.Echo)
        {
            _list.Visible = k.Pressed;
            if (k.Pressed) FillList();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (!k.Pressed || k.Echo) return;

        if (_chat.Visible)
        {
            if (k.Keycode == Key.Escape) { CloseChat(); GetViewport().SetInputAsHandled(); }
            return;                         // alles andere gehört der Zeile
        }
        if (k.CtrlPressed && k.Keycode == Key.K && _wait.Visible) { OnCtrlK?.Invoke(); GetViewport().SetInputAsHandled(); return; }
        if (k.CtrlPressed && k.Keycode == Key.E && _wait.Visible) { OnCtrlE?.Invoke(); GetViewport().SetInputAsHandled(); return; }
        if (k.Keycode is Key.Enter or Key.KpEnter && !k.CtrlPressed && !k.AltPressed
            && (Takt?.Invoke() ?? 0) > 20 && GetViewport().GuiGetFocusOwner() == null)
        {
            _chat.Visible = true;
            _chat.GrabFocus();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (_list.Visible) FillList();
        ProbeTick();
    }

    // ---- Prüfschalter --netz-probe=<präfix>: Bilder der Netzfenster im echten Fenster ----
    // Wartet bis »Warten auf« 3,5 s steht → Bild, Spielerliste → Bild, Ctrl-K → nach 2 s
    // Bild, Ctrl-E (zurück ins Menü, online gleich in die Lobby). Vorher bei 6 s ein Bild
    // für die Chatmeldung des Mitspielers und eines mit offener Chatzeile.
    private string? _probe;
    private int _probeStep;
    private ulong _probeT, _probeStart, _waitMs;

    public override void _EnterTree()
    {
        foreach (string a in Core.CommandLine.Args)
            if (a.StartsWith("--netz-probe=")) _probe = a["--netz-probe=".Length..];
    }

    private void Shot(string what)
    {
        string p = $"{_probe}_{what}.png";
        var err = GetViewport().GetTexture().GetImage().SavePng(p);
        GD.Print($"netz-probe: Bild {p} ({err})");
    }

    private void ProbeTick()
    {
        if (_probe == null) return;
        ulong now = Time.GetTicksMsec();
        if (_probeStart == 0) _probeStart = now;
        switch (_probeStep)
        {
            case 0 when now - _probeStart > 6500: Shot("chat"); _probeStep = 1; break;
            case 1:
                _chat.Visible = true; _chat.Text = "gg, gleich gehts weiter"; _probeT = now; _probeStep = 2; break;
            case 2 when now - _probeT > 400: Shot("chatzeile"); CloseChat(); _probeStep = 3; break;
            case 3 when _wait.Visible && _waitMs >= 3500: Shot("warten"); _list.Visible = true; FillList(); _probeT = now; _probeStep = 4; break;
            case 4 when now - _probeT > 400: Shot("liste"); _list.Visible = false; OnCtrlK?.Invoke(); _probeT = now; _probeStep = 5; break;
            case 5 when now - _probeT > 2500: Shot("nachkick"); _probeStep = 6; OnCtrlE?.Invoke(); break;
        }
    }

    private void FillList()
    {
        foreach (var c in _grid.GetChildren()) { _grid.RemoveChild(c); c.QueueFree(); }
        var s = NetworkManager.Link?.Session;
        void Cell(string t, bool kopf = false)
            => _grid.AddChild(new Label { Text = t, Modulate = kopf ? new Color(0.75f, 0.8f, 0.86f) : Colors.White });
        Cell("SPIELER", true); Cell("PING", true); Cell("ZUSTAND", true);
        if (s == null) return;
        int takt = Takt?.Invoke() ?? 0;
        for (int p = 0; p < s.Players; p++)
        {
            int ping = NetworkManager.PlayerPing(p);
            Cell(NetworkManager.PlayerName(p) + (p == s.Me ? "  (du)" : ""));
            Cell(ping >= 0 ? $"{ping} ms" : "—");
            Cell(NetworkManager.Link!.IsOut(p, takt) ? "ausgeschaltet" : "im Spiel");
        }
    }
}
