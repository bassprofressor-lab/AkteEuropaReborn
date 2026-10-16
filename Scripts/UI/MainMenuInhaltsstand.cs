namespace AkteEuropaReborn.UI;

using System.Collections.Generic;
using System.Linq;
using Godot;
using AkteEuropaReborn.Import;

/// <summary>
/// ⭐ 16.10.2026 — <b>DER PRUEFER BEIM START</b>: sind Teile der eingelesenen
/// Inhalte von einem aelteren Importeur geschrieben (<see cref="InhaltsStand"/>),
/// fragt das Menue einmal, ob sie neu eingelesen werden sollen — nur die
/// veralteten Teile, ueber die vorhandenen Teil-Neuexporte.
///
/// <para>Spielerwunsch: »Können wir einen Prüfer im Installer einbauen, ob Karten
/// gebacken werden müssen, wenn z. B. schon jemand eine ältere Version
/// installiert hat? Sonst muss man das immer händisch machen.« ⚠ Er sitzt im
/// SPIEL, nicht im Installer: nur das Spiel kann backen, und es braucht dafuer
/// die CDs. Der Installer sagt beim Update nur, dass gefragt werden wird
/// (packaging/AkteEuropaReborn.iss).</para>
///
/// <para>⚠ UNSERE SETZUNGEN: (1) Gefragt wird nur in einem Lauf MIT Fenster und
/// OHNE Schalter — jeder Pruef- oder Entwicklerlauf (kopflos, <c>--setup</c>,
/// <c>--credits-check</c>, …) laeuft wie bisher durch und schreibt nur eine
/// Zeile ins Protokoll; <c>--inhaltsstand-zeigen</c> erzwingt den Schirm.
/// (2) Einmal je Programmstart: »Spaeter« spielt mit dem alten Stand weiter und
/// fragt beim naechsten Start wieder. (3) Die Quelle sucht
/// <see cref="InhaltsStand.QuelleFinden"/> selbst; brauchen Teile ALLE Karten und
/// traegt die Quelle nicht jede eingelesene Mission, wird wie beim Erstimport
/// die CD zwischengelagert und nach der anderen gefragt.
/// Gegenschalter <c>--inhaltsstand-aus</c> (nur der Kanarienvogel, wie bis
/// 0.6.9).</para>
/// </summary>
public partial class MainMenu
{
    /// <summary>Einmal je Programmstart gefragt (»Spaeter« fragt beim naechsten
    /// Start wieder, nicht beim naechsten Menue).</summary>
    private static bool _inhaltsstandGefragt;

    private List<InhaltsStand.Teil> _isAlt = new();
    private Core.ContentSources.Source? _isQuelle;
    private bool _isGelagert;
    private Label? _isQuelleText, _isStatus, _isStep, _isPromptText;
    private ProgressBar? _isBar;
    private VBoxContainer? _isPrompt;
    private Button? _isStart, _isSpaeter, _isWeiter, _isOrdner, _isSuchen, _isOhne, _isZumMenue;
    private List<int> _isFehlt = new();

    /// <summary>Im <c>_Ready</c> nach AutoStart: true, wenn der Schirm des
    /// Pruefers das Menue ersetzt hat.</summary>
    private bool InhaltsstandPruefen()
    {
        if (_inhaltsstandGefragt) return false;
        _inhaltsstandGefragt = true;
        string root = Core.Content.UserDir;
        var alt = InhaltsStand.Veraltet(root);
        if (alt.Count == 0)
        {
            if (InhaltsStand.Aus) GD.Print("inhaltsstand: Pruefer aus (--inhaltsstand-aus)");
            return false;
        }
        GD.Print($"inhaltsstand: veraltet {alt.Count} Teile [{string.Join(", ", alt.Select(t => t.Name))}] — "
                 + InhaltsStand.Beschreiben(root));
        var args = OS.GetCmdlineUserArgs();
        bool zeigen = args.Contains("--inhaltsstand-zeigen");
        if (!zeigen && (DisplayServer.GetName() == "headless" || args.Length > 0))
        {
            GD.Print("inhaltsstand: Prueflauf/kopflos — keine Rueckfrage, das Menue laeuft wie bisher");
            return false;
        }
        ShowInhaltsstandScreen(alt);
        return true;
    }

    private void ShowInhaltsstandScreen(List<InhaltsStand.Teil> alt)
    {
        _isAlt = alt;
        foreach (var c in GetChildren()) (c as Node)?.QueueFree();
        var bg = new ColorRect { Color = new Color(0.05f, 0.06f, 0.08f) };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var box = new VBoxContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            GrowHorizontal = GrowDirection.Both, GrowVertical = GrowDirection.Both,
            CustomMinimumSize = new Vector2(720, 0),
        };
        box.AddThemeConstantOverride("separation", 10);
        AddChild(box);

        var t = new Label { Text = "Spieldaten auf neuen Stand bringen", HorizontalAlignment = HorizontalAlignment.Center };
        t.AddThemeFontSizeOverride("font_size", 30);
        box.AddChild(t);

        bool alleKarten = alt.Any(x => x.Bedarf == InhaltsStand.Bedarf.AlleKarten);
        bool programm = alt.Any(x => x.Bedarf == InhaltsStand.Bedarf.Programm);
        string bedarf = alleKarten
            ? "Dafuer braucht es deine Spielkopie mit ALLEN Karten: beide CDs (CD 1, dann CD 2) oder eine Installation, die sie traegt."
            : programm ? "Dafuer braucht es deine Spielkopie: CD 1 oder deine Installation."
                       : "Dafuer braucht es keine CD.";
        box.AddChild(new Label
        {
            Text = "Diese Version liest Teile der Inhalte anders ein:\n" +
                   string.Join(", ", alt.Select(x => x.Anzeige)) + ".\n" +
                   "Sie muessen einmal neu eingelesen werden. " + bedarf,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(0.75f, 0.8f, 0.85f),
        });
        box.AddChild(new HSeparator());

        _isQuelleText = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        box.AddChild(_isQuelleText);

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 8);
        _isStart = new Button { Text = "Jetzt neu einlesen", CustomMinimumSize = new Vector2(200, 40) };
        _isOrdner = new Button { Text = "Ordner waehlen …", CustomMinimumSize = new Vector2(0, 40),
                                 TooltipText = "Eine Installation oder eine auf die Platte kopierte CD" };
        _isSuchen = new Button { Text = "Erneut suchen", CustomMinimumSize = new Vector2(0, 40) };
        _isSpaeter = new Button { Text = "Spaeter", CustomMinimumSize = new Vector2(140, 40),
                                  TooltipText = "Mit dem alten Stand weiterspielen — beim naechsten Start wird wieder gefragt." };
        row.AddChild(_isStart); row.AddChild(_isOrdner); row.AddChild(_isSuchen); row.AddChild(_isSpaeter);
        box.AddChild(row);

        _isBar = new ProgressBar { CustomMinimumSize = new Vector2(0, 22), ShowPercentage = true, Visible = false };
        box.AddChild(_isBar);
        _isStep = new Label { HorizontalAlignment = HorizontalAlignment.Center, Visible = false };
        box.AddChild(_isStep);
        _isStatus = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        box.AddChild(_isStatus);

        // der CD-Wechsel, im Stil des Einleseschirms (bug-379)
        _isPrompt = new VBoxContainer { Visible = false };
        _isPrompt.AddThemeConstantOverride("separation", 8);
        box.AddChild(_isPrompt);
        _isPromptText = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(1f, 0.85f, 0.45f),
        };
        _isPrompt.AddChild(_isPromptText);
        var pr = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        pr.AddThemeConstantOverride("separation", 8);
        _isPrompt.AddChild(pr);
        _isWeiter = new Button { Text = "Weiter", CustomMinimumSize = new Vector2(140, 36) };
        _isOhne = new Button { Text = "Nur die uebrigen Teile", CustomMinimumSize = new Vector2(0, 36),
                               TooltipText = "Was keine Karten braucht, jetzt einlesen — der Rest wird beim naechsten Start wieder angeboten." };
        pr.AddChild(_isWeiter); pr.AddChild(_isOhne);

        _isZumMenue = new Button { Text = "Weiter zum Menue", CustomMinimumSize = new Vector2(200, 40), Visible = false };
        box.AddChild(_isZumMenue);

        var dlg = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            Size = new Vector2I(820, 560),
            Title = "Ordner der Spielkopie waehlen",
        };
        AddChild(dlg);

        _isSpaeter.Pressed += () =>
        {
            GD.Print("inhaltsstand: »Spaeter« — weiter mit dem alten Stand");
            DropStaging();
            GetTree().ChangeSceneToFile(Core.Content.MenuSceneSafe);
        };
        _isZumMenue.Pressed += () => GetTree().ChangeSceneToFile(Core.Content.MenuSceneSafe);
        _isSuchen.Pressed += QuelleSuchen;
        _isOrdner.Pressed += () => dlg.PopupCentered();
        dlg.DirSelected += d =>
        {
            var f = Core.ContentSources.FromFolder(d);
            if (f == null || f.Kind == Core.ContentSources.Kind.Derived)
            {
                _isStatus!.Text = $"In {d} liegt keine Spielkopie (GAME.EXE oder DATA und LEVELS).";
                return;
            }
            if (_isPrompt!.Visible)
            {
                var u = Core.DiscCoverage.Useful(f, _isFehlt);
                if (u == null) { _isStatus!.Text = $"In {d} liegt keine der fehlenden Missionen."; return; }
                f = u;
            }
            _isQuelle = _isQuelle != null ? Core.DiscCoverage.Merge(_isQuelle, f) : f;
            QuelleZeigen();
            if (_isPrompt.Visible) Einlesen(alleTeile: true);
        };
        _isStart.Pressed += () => Einlesen(alleTeile: true);
        _isWeiter.Pressed += () =>
        {
            var u = Core.DiscCoverage.Useful(Core.ContentSources.Discs(), _isFehlt);
            if (u == null)
            {
                _isStatus!.Text = "Keine CD mit den fehlenden Missionen gefunden" +
                    (Core.ContentSources.Discs() != null ? " — im Laufwerk liegt noch dieselbe CD." : ".");
                return;
            }
            _isQuelle = _isQuelle != null ? Core.DiscCoverage.Merge(_isQuelle, u) : u;
            QuelleZeigen();
            Einlesen(alleTeile: true);
        };
        _isOhne.Pressed += () => Einlesen(alleTeile: false);

        QuelleSuchen();
    }

    private void QuelleSuchen()
    {
        var q = InhaltsStand.QuelleFinden(Core.Content.UserDir);
        // was schon zusammengetragen ist (Zwischenlager), bleibt vorn
        _isQuelle = _isQuelle != null && q != null ? Core.DiscCoverage.Merge(_isQuelle, q) : (q ?? _isQuelle);
        QuelleZeigen();
    }

    private void QuelleZeigen()
    {
        if (_isQuelleText == null) return;
        bool brauchtQuelle = _isAlt.Any(x => x.Bedarf != InhaltsStand.Bedarf.Nichts);
        if (_isQuelle == null)
        {
            _isQuelleText.Text = brauchtQuelle
                ? "Keine Spielkopie gefunden. Leg CD 1 ein und druecke »Erneut suchen« — oder waehle den Ordner deiner Installation."
                : "Es wird keine CD gebraucht.";
            if (_isStart != null) _isStart.Disabled = brauchtQuelle;
            return;
        }
        string warn = "";
        if (_isAlt.Any(x => x.Bedarf != InhaltsStand.Bedarf.Nichts) && !_isQuelle.Complete)
            warn = "\n⚠ GAME.EXE fehlt in dieser Quelle (sie liegt auf CD 1 bzw. in der Installation).";
        _isQuelleText.Text = "Gefunden: " + _isQuelle.Label + warn;
        if (_isStart != null) _isStart.Disabled = false;
    }

    private void Sperren(bool an)
    {
        foreach (var b in new[] { _isStart, _isOrdner, _isSuchen, _isSpaeter, _isWeiter, _isOhne })
            if (b != null) b.Disabled = an;
    }

    /// <summary>Die veralteten Teile einlesen. <paramref name="alleTeile"/> =
    /// false: nur die, die keine Karten brauchen (»Nur die uebrigen Teile«).</summary>
    private void Einlesen(bool alleTeile)
    {
        if (_isStatus == null) return;
        string root = Core.Content.UserDir.TrimEnd('/', '\\');
        var teile = alleTeile ? _isAlt.ToList()
                              : _isAlt.Where(x => x.Bedarf != InhaltsStand.Bedarf.AlleKarten).ToList();
        if (teile.Count == 0) { _isStatus.Text = "Ohne die Karten bleibt hier nichts einzulesen."; return; }
        var src = _isQuelle ?? new Core.ContentSources.Source { Label = "(keine)" };
        if (teile.Any(x => x.Bedarf != InhaltsStand.Bedarf.Nichts) && !src.Complete)
        {
            _isStatus.Text = "In der Quelle fehlt GAME.EXE — leg CD 1 ein oder waehle deine Installation.";
            return;
        }
        if (alleTeile && teile.Any(x => x.Bedarf == InhaltsStand.Bedarf.AlleKarten))
        {
            _isFehlt = InhaltsStand.FehlendeKarten(root, src);
            if (_isFehlt.Count > 0)
            {
                // wie beim Erstimport: die CD im Laufwerk erst zwischenlagern,
                // dann nach der anderen fragen (Core.DiscCoverage)
                if (!_isGelagert && src.Roots.Exists(Core.DiscCoverage.IsRemovable))
                {
                    Lagern(src);
                    return;
                }
                NachCdFragen();
                return;
            }
        }
        if (_isPrompt != null) _isPrompt.Visible = false;
        Sperren(true);
        if (_isBar != null) { _isBar.Visible = true; _isBar.Value = 0; _isBar.MaxValue = teile.Count; }
        if (_isStep != null) _isStep.Visible = true;
        // ⚠ die Bauer im HAUPTFADEN anlegen (Zielpfad und Version), arbeiten
        // duerfen sie auf dem Nebenlaeufer — wie StartImport
        var arbeit = teile.Select(x => (Teil: x, Bauer: new ContentBuilder(src, root))).ToList();
        GD.Print($"inhaltsstand: lese neu ein: {string.Join(", ", teile.Select(x => x.Name))} aus {src.Label}");
        System.Threading.Tasks.Task.Run(() =>
        {
            var fehl = new List<string>();
            for (int i = 0; i < arbeit.Count; i++)
            {
                var (teil, bauer) = arbeit[i];
                int k = i;
                bool ok;
                try
                {
                    Callable.From(() => IsFortschritt(k, arbeit.Count, teil.Anzeige, teil.Anzeige + " …")).CallDeferred();
                    ok = teil.Weg(bauer, line =>
                        Callable.From(() => IsFortschritt(k, arbeit.Count, teil.Anzeige, line)).CallDeferred());
                }
                catch (System.Exception e) { ok = false; GD.PrintErr($"inhaltsstand: {teil.Name}: {e.Message}"); }
                if (!ok) fehl.Add(teil.Anzeige);
                GD.Print($"inhaltsstand: {teil.Name} -> {(ok ? "gelungen" : "FEHLGESCHLAGEN")}");
            }
            Callable.From(() => IsFertig(fehl)).CallDeferred();
        });
    }

    private void Lagern(Core.ContentSources.Source src)
    {
        Sperren(true);
        if (_isBar != null) { _isBar.Visible = true; _isBar.Value = 0; }
        if (_isStep != null) _isStep.Visible = true;
        string staging = ProjectSettings.GlobalizePath(StagingRoot);
        System.Threading.Tasks.Task.Run(() =>
        {
            Core.ContentSources.Source res = src;
            string? err = null;
            try
            {
                res = Core.DiscCoverage.Stage(src, staging, (f, d, t) =>
                {
                    int dm = (int)(d >> 20), tm = (int)System.Math.Max(1, t >> 20);
                    Callable.From(() =>
                    {
                        if (!IsInstanceValid(this) || _isBar == null) return;
                        _isBar.MaxValue = tm; _isBar.Value = System.Math.Min(dm, tm);
                        if (_isStep != null) _isStep.Text = $"Kopie: {dm} von {tm} MB";
                        if (_isStatus != null) _isStatus.Text = $"CD wird fuer den Wechsel zwischengelagert: {f}";
                    }).CallDeferred();
                });
            }
            catch (System.Exception e) { err = e.Message; }
            Callable.From(() =>
            {
                if (!IsInstanceValid(this)) return;
                Sperren(false);
                if (err != null) { if (_isStatus != null) _isStatus.Text = "Zwischenlagern fehlgeschlagen: " + err; return; }
                _isGelagert = true;
                _isQuelle = res;
                QuelleZeigen();
                NachCdFragen();
            }).CallDeferred();
        });
    }

    private void NachCdFragen()
    {
        var cds = Core.DiscCoverage.DiscsFor(_isFehlt);
        string which = cds.Count == 1 ? $"CD {cds.Min}" : "die fehlende CD";
        if (_isPromptText != null)
            _isPromptText.Text = Core.DiscCoverage.Explain(_isFehlt) + "\n" +
                $"Die Karten muessen fuer alle eingelesenen Missionen neu entstehen. Bitte {which} einlegen und " +
                "»Weiter« druecken — oder ueber »Ordner waehlen …« eine kopierte CD angeben.";
        if (_isPrompt != null) _isPrompt.Visible = true;
        if (_isBar != null) _isBar.Visible = false;
        if (_isStep != null) _isStep.Visible = false;
        GD.Print($"inhaltsstand: {Core.DiscCoverage.Explain(_isFehlt)} — frage nach {which}");
    }

    private void IsFortschritt(int i, int n, string teil, string line)
    {
        if (!IsInstanceValid(this) || _isBar == null) return;
        _isBar.MaxValue = n;
        _isBar.Value = i;
        if (_isStep != null) _isStep.Text = $"Teil {i + 1} von {n}: {teil}";
        if (_isStatus != null) _isStatus.Text = line.Length > 160 ? line[..157] + " …" : line;
    }

    private void IsFertig(List<string> fehl)
    {
        if (!IsInstanceValid(this) || _isStatus == null) return;
        Campaign.CampaignManager.Forget();
        if (_isGelagert) { DropStaging(); _isGelagert = false; }
        if (_isBar != null) _isBar.Value = _isBar.MaxValue;
        Sperren(false);
        var rest = InhaltsStand.Veraltet(Core.Content.UserDir);
        if (fehl.Count == 0 && rest.Count == 0)
        {
            _isStatus.Text = "Fertig — alle Inhalte sind auf dem Stand dieser Version.";
            _isStatus.Modulate = new Color(0.6f, 0.95f, 0.6f);
        }
        else
        {
            _isStatus.Text = (fehl.Count > 0 ? "Fehlgeschlagen: " + string.Join(", ", fehl) + ".\n" : "") +
                (rest.Count > 0 ? "Noch auf altem Stand: " + string.Join(", ", rest.Select(x => x.Anzeige)) + ".\n" : "") +
                "Das Spiel laeuft damit weiter; beim naechsten Start wird wieder gefragt. " +
                "Das Protokoll (Godot-Ausgabe) nennt den Grund.";
            _isStatus.Modulate = new Color(1f, 0.55f, 0.5f);
        }
        GD.Print($"inhaltsstand: fertig, {fehl.Count} fehlgeschlagen, {rest.Count} noch veraltet — " +
                 InhaltsStand.Beschreiben(Core.Content.UserDir));
        _isAlt = rest;
        if (_isStart != null) _isStart.Visible = rest.Count > 0;
        if (_isZumMenue != null) _isZumMenue.Visible = true;
    }
}
