namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <c>--generator-check</c> (K13, 13.09.2026) — der Generator von vorn bis hinten.
/// Lesung <c>berichte/generator-fable.md</c>.
///
/// <para>Gemessen: gesperrter Platz → der Auftrag BLEIBT, kein Gebäude, keine
/// Absage; frei → Generator steht, Bauzustand 100, Panzerung 7, nicht anklickbar,
/// unverwundbar; nach 300 Originaltakten fertig; dann Fensterart 20 (440×200)
/// mit »Stromerzeugung : 0« (Neubau, wörtlich); Rechtsklick schliesst.
/// Nullmodelle: <c>--bauzustand-aus</c>, <c>--bauauftrag-alt</c>,
/// <c>--generatorfenster-alt</c>.</para>
/// </summary>
public partial class MapViewer
{
    private bool _generatorCheck;

    private async System.Threading.Tasks.Task GeneratorLauf()
    {
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var sb = new System.Text.StringBuilder("generator-check\n");
        int fehler = 0;
        void Soll(bool b, string was) { sb.Append(b ? "  ok     " : "  FEHLER ").Append(was).Append('\n'); if (!b) fehler++; }
        async System.Threading.Tasks.Task Warten(int k = 3)
        { for (int i = 0; i < k; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        void Takte(int n) { for (int t = 0; t < n; t++) _entities.SimTickFuerProbe(); }

        sb.Append("  ⚠ EINGRIFF: " + _entities.GeneratorProbeSetzen() + "\n");
        _entities.GeneratorProbeSperren(true);
        sb.Append("  ⚠ EINGRIFF: Nachbarzellen des Bauers fremd belegt\n");
        Takte(20);
        var st = _entities.GeneratorProbeStand();
        if (MapEntityLayer.BauauftragAlt)
            Soll(!st.AuftragDa && st.Generator == null, "Nullmodell --bauauftrag-alt: Auftrag verfallen");
        else
            Soll(st.AuftragDa && st.Generator == null, $"Platz gesperrt: Auftrag bleibt ({st.AuftragDa}), kein Generator — still (0x408354)");
        _entities.GeneratorProbeSperren(false);
        if (MapEntityLayer.BauauftragAlt)
        {
            GD.Print(sb.Append(fehler == 0 ? "generator-check: IN ORDNUNG" : $"generator-check: {fehler} FEHLER"));
            GetTree().Quit(0);
            return;
        }
        Takte(10);
        st = _entities.GeneratorProbeStand();
        var g = st.Generator;
        Soll(g != null, "Platz frei: Generator gebaut, Fahrzeug verbraucht");
        if (g == null) { GD.Print(sb.Append("generator-check: FEHLER")); GetTree().Quit(0); return; }
        bool aus = MapEntityLayer.BauzustandAus;
        sb.Append("  (" + _entities.GeneratorProbeMuster(g) + ")\n");
        Soll(aus ? g.Bauzustand == 0 : g.Bauzustand is >= 100 and < 250, $"Bauzustand {g.Bauzustand} (100…249; Nullmodell 0)");
        Soll(g.Armor == 7 || MapEntityLayer.PanzerungNeubauAlt, $"Panzerung {g.Armor} (7)");
        Soll(aus ? MapEntityLayer.FensterArtVon(g) != null : MapEntityLayer.FensterArtVon(g) == null, "im Bau nicht anklickbar");
        int schaden = _entities.GeneratorProbeBeschiessen(g);
        Soll(aus ? schaden > 0 : schaden == 0, $"im Bau unverwundbar: Schaden {schaden}");
        if (_shotPath.Length > 0 && !aus)
        {
            _camera.Position = _entities.GeneratorProbeKamera(g);
            UI.HelpWindow.CloseAll(); UI.HelpWindow.CommitClose();
            await Warten(4);
            GetViewport().GetTexture().GetImage().SavePng(_shotPath.Replace(".png", "_bau.png"));
        }
        Takte(2 * (210 - g.Bauzustand));
        Soll(g.Bauzustand is >= 200 and < 250, $"Geruest Stufe 2 erreicht (Bauzustand {g.Bauzustand})");
        if (_shotPath.Length > 0 && !aus)
        {
            await Warten(4);
            GetViewport().GetTexture().GetImage().SavePng(_shotPath.Replace(".png", "_bau2.png"));
        }
        Takte(120);
        Soll(g.Bauzustand == 0, $"nach 300 Originaltakten fertig (Bauzustand {g.Bauzustand})");

        UI.WindowManager.Mausquelle = () => new Vector2(300, 300);
        int getroffen = _entities.GeneratorProbeAnklicken(g);
        Soll(getroffen >= 0 && getroffen == _entities.IndexOfFuerProbe(g),
             $"Klick auf die Mitte des Rahmens trifft den Generator (Pick {getroffen}, Pos {g.Pos}, Fuss {g.FootW}x{g.FootH})");
        for (int t = 0; t <= UI.WindowManager.BilderAuf + 1; t++) UI.WindowManager.Takt();
        await Warten();
        var f = _gebaeudeFenster;
        if (MapEntityLayer.GeneratorfensterAlt)
        {
            Soll(UI.WindowManager.Offen(20) == null, "Nullmodell --generatorfenster-alt: kein Fenster");
        }
        else
        {
            var soll = new Vector2(440, 200);
            Soll(UI.WindowManager.Offen(20) != null && f.Visible && f.ZeigtOriginalGenerator && f.Size == soll,
                 $"Fensterart 20 offen, Kacheln, Mass {f.Size} ({soll})");
            var gv = f.GeneratorAnsicht!;
            Soll(gv.Zeile1 == "Stromerzeugung : 0", $"»{gv.Zeile1}« (Neubau: 0, wörtlich)");
            sb.Append($"  ({gv.Zeile2} · {gv.Zeile3})\n");
            if (_shotPath.Length > 0)
            {
                UI.HelpWindow.CloseAll(); UI.HelpWindow.CommitClose();
                await Warten(4);
                GetViewport().GetTexture().GetImage().SavePng(_shotPath);
            }
        }
        GD.Print(sb.Append(fehler == 0 ? "generator-check: IN ORDNUNG" : $"generator-check: {fehler} FEHLER"));
        GetTree().Quit(0);
    }
}
