namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// Die Verdrahtung der Fensterart 38 (UI/MitnahmeView.cs) und
/// <c>--mitnahmefenster-check</c> (13.09.2026).
/// </summary>
public partial class MapViewer
{
    private UI.MitnahmeView? _mitnahme;
    private CanvasLayer? _mitnahmeLayer;
    private bool _mitnahmefensterCheck;

    /// <summary>Im Prüfstand: was »Start« geliefert hätte — ohne campaign.cfg.</summary>
    private List<Campaign.CampaignManager.CarriedUnit>? _mitnahmeProbe;
    private int _mitnahmeProbeGeld = -1;

    private void MitnahmeOeffnen(int n)
    {
        if (_mitnahme == null)
        {
            _mitnahmeLayer = new CanvasLayer { Layer = 96 };
            AddChild(_mitnahmeLayer);
            _mitnahme = new UI.MitnahmeView();
            _mitnahmeLayer.AddChild(_mitnahme);
            int spieler = _entities.ViewPlayer is >= 0 and <= 7 ? _entities.ViewPlayer : 0;
            _mitnahme.Links = tafel => _entities.MitnahmeLinks(spieler, tafel);
            _mitnahme.Zeile = _entities.MitnahmeZeile;
            _mitnahme.Auszahlung = tafel => _entities.MitnahmeAuszahlung(spieler, tafel);
            _mitnahme.OnStart = (mit, auszahlung) =>
            {
                var liste = new List<Campaign.CampaignManager.CarriedUnit>();
                foreach (int g in mit) liste.Add(_entities.MitnahmeSatz(g));
                UI.WindowManager.Schliessen(38);
                if (_mitnahmeLayer != null) _mitnahmeLayer.Visible = false;
                if (_mitnahmefensterCheck) { _mitnahmeProbe = liste; _mitnahmeProbeGeld = auszahlung; return; }
                Campaign.CampaignManager.Carried = liste;
                // Zustand 200 »Succ200« (0x416B05): die Auszahlung erst JETZT.
                Campaign.CampaignManager.Balance += auszahlung;
                GD.Print($"Mitnahme: {liste.Count} Einheit(en) gemerkt, ${auszahlung} fuer den Rest");
                if (_nextMission > 0) StartNextMission(); else ToMenu();
            };
        }
        _mitnahmeLayer!.Visible = true;
        UI.WindowManager.Oeffnen(38, _mitnahme, -1);          // 0x441270: Ereignisbyte 38
        _mitnahme.Oeffnen(n, Campaign.CampaignManager.Balance);
    }

    private async System.Threading.Tasks.Task MitnahmeLauf()
    {
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var sb = new System.Text.StringBuilder("mitnahmefenster-check\n");
        int fehler = 0;
        void Soll(bool b, string was) { sb.Append(b ? "  ok     " : "  FEHLER ").Append(was).Append('\n'); if (!b) fehler++; }
        async System.Threading.Tasks.Task Warten(int k = 3)
        { for (int i = 0; i < k; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }

        sb.Append("  ⚠ EINGRIFF: " + _entities.MitnahmeProbeBauen() + "\n");
        int n = Campaign.CampaignManager.SpotsFor(12).Count;
        Soll(n == 3, $"Mission 12 hat {n} Plaetze (Mission 11 setzt n = 3)");
        MitnahmeOeffnen(n);
        await Warten();
        var v = _mitnahme!;
        var soll = new Vector2(UI.MitnahmeView.WTiles * UI.WindowChrome.Cell * UI.MitnahmeView.Scale,
                               UI.MitnahmeView.HTiles * UI.WindowChrome.Cell * UI.MitnahmeView.Scale);
        Soll(v.Visible && v.Size == soll && v.Position == new Vector2(20, 20),
             $"Fenster 600x420 an (10,10): {v.Size} bei {v.Position}");
        int kandidaten = v.Anzahl;
        Soll(kandidaten >= 4, $"links {kandidaten} eigene bewaffnete Fahrzeuge (mindestens 4 fuer die Probe)");
        sb.Append("  (" + _entities.MitnahmeFilterZeile() + ")\n");
        Soll(v.AuswahlLinks == 1000 && v.AnzahlRechts == 0 && !v.ZuruecklassenDa,
             "beim Oeffnen: Zeile 0 gewaehlt, Tafel leer, kein Zuruecklassen");
        int alles = v.AuszahlungAnzeige;

        var leinwand = GetViewport().GetVisibleRect().Size;
        var fenstermass = (Vector2)GetWindow().Size;
        var faktor = new Vector2(fenstermass.X / leinwand.X, fenstermass.Y / leinwand.Y);
        async System.Threading.Tasks.Task Klick(Vector2 punkt, bool doppel = false)
        {
            var p = punkt * faktor;
            GetViewport().PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p });
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = p, GlobalPosition = p });
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = p, GlobalPosition = p });
            if (doppel)
                GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, DoubleClick = true, Position = p, GlobalPosition = p });
            await Warten();
        }
        Vector2 Mitte(Rect2 r) => r.Position + r.Size / 2f;

        // die GEBAUTE, angeschlagene Einheit mit Rang 42 zuerst waehlen
        int zeileGebaut = -1;
        for (int k = 0; k < v.LinksListe.Count; k++)
            if (_entities.MitnahmeRang(v.LinksListe[k]) == 42) { zeileGebaut = k; break; }
        Soll(zeileGebaut >= 0, $"die gebaute Einheit steht links (Zeile {zeileGebaut})");
        if (zeileGebaut >= 0) await Klick(Mitte(v.FeldAufDemSchirm(1000 + zeileGebaut)));
        int g0 = v.GriffLinks;
        await Klick(Mitte(v.FeldAufDemSchirm(1)));
        Soll(v.AnzahlRechts == 1 && v.Rechts[0] == g0 && v.Anzahl == kandidaten - 1,
             $"»Mitnehmen >>«: rechts {v.AnzahlRechts}, links {v.Anzahl}");
        Soll(v.AuszahlungAnzeige < alles, $"Auszahlung neu gerechnet: {alles} -> {v.AuszahlungAnzeige}");
        await Klick(Mitte(v.FeldAufDemSchirm(1001)), doppel: true);
        Soll(v.AnzahlRechts == 2, $"Doppelklick links = Mitnehmen: rechts {v.AnzahlRechts}");
        await Klick(Mitte(v.FeldAufDemSchirm(1)));
        Soll(v.AnzahlRechts == 3 && !v.MitnehmenDa, $"Tafel voll (3/3): Knopf »Mitnehmen« verschwunden ({v.MitnehmenDa})");
        if (_shotPath.Length > 0)
        {
            GetViewport().GetTexture().GetImage().SavePng(_shotPath);
            sb.Append($"  Bild nach {_shotPath}\n");
        }
        await Klick(Mitte(v.FeldAufDemSchirm(1)));
        Soll(v.AnzahlRechts == 3 && v.LetzterTreffer == 0, $"Klick auf den verschwundenen Knopf: still (Treffer {v.LetzterTreffer})");
        await Klick(Mitte(v.FeldAufDemSchirm(2001)));
        int weg = v.GriffRechts;
        await Klick(Mitte(v.FeldAufDemSchirm(2)));
        bool drin = false;
        foreach (int g in v.Rechts) if (g == weg) drin = true;
        Soll(v.AnzahlRechts == 2 && !drin && v.MitnehmenDa, $"»<< Zurücklassen«: rechts {v.AnzahlRechts}, Knopf wieder da");
        await Klick(Mitte(v.FeldAufDemSchirm(1)));

        var tafel = new List<int>(v.Rechts);
        int sollAus = v.AuszahlungAnzeige;
        await Klick(Mitte(v.FeldAufDemSchirm(7)));
        Soll(_mitnahmeProbe != null && _mitnahmeProbe.Count == 3 && _mitnahmeProbeGeld == sollAus,
             $"»Start«: {_mitnahmeProbe?.Count} mitgenommen, Auszahlung {_mitnahmeProbeGeld} ({sollAus}) — campaign.cfg NICHT beschrieben");
        if (_mitnahmeProbe != null)
        {
            var aufgestellt = _entities.MitnahmeProbeAufstellen(_mitnahmeProbe, tafel);
            sb.Append(aufgestellt.Zeile);
            Soll(aufgestellt.Alle && aufgestellt.Voll,
                 "jede mitgenommene Einheit (auch die GEBAUTE) wieder aufgestellt, voll repariert, Rang erhalten");
        }
        GD.Print(sb.Append(fehler == 0 ? "mitnahmefenster-check: IN ORDNUNG" : $"mitnahmefenster-check: {fehler} FEHLER"));
        GetTree().Quit(0);
    }
}
