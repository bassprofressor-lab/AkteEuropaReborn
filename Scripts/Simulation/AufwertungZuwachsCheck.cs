namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Text;
using Godot;

/// <summary>
/// <b><c>--aufwertung-zuwachs-check</c> — bug-601 (Reaper, Testerrunde 0.6.11).</b>
/// Seine Meldung: nach der Forschung »6x6 Reifen« fällt die Verteidigung des Mobilen
/// Radars von 0/7 auf 0/3 (Bilder reaper1..3). Ursache: <see cref="AufwertungSchreiben"/>
/// SETZTE Fahrwerk +0x13, Arm 1 +0x11 und Waffe +0x12 auf den Zuwachs; das Original
/// ADDIERT (C 0x4AAB33/3D/43, 0x4AAB4F/55/60, 0x4AAAE9/EF/FA; F 0x4AA463/46D/473).
/// Bericht <c>berichte/radar-panzerung-6x6-opus.md</c>.
///
/// <para><b>Der Weg ist der des Spielers:</b> Basis anwählen, Forschung über den
/// RING (Befehl 2005, <see cref="PostForschung"/> → <c>RingLeeren</c>), dann laufen die
/// Takte (<see cref="SimTickFuerProbe"/>), bis der Abschluss fällt. Gemessen wird die
/// QUELLE (Spielerblock) UND die ANKUNFT: das A/V, das das Erstellungsfenster
/// (<c>UI.DesignWindow</c>, derselbe Text wie im Spiel) und die Werteliste
/// (<c>UI.BaseWindow.StatsFor</c>) ausgeben.</para>
///
/// <list type="number">
///   <item>vorher: 6x6 Reifen (163) +0x13 = 4, +0x11, Stufe 0; V Radar (67/163) = 7;
///   Kanone (1/163) A/V = 8/6, im Fenster und in der Werteliste.</item>
///   <item>Forschung 6x6 Reifen über den Ring, ⚠ EINGRIFF (angesagt): Arm 1 vorgegeben
///   (<c>_forschungWurf[163] := 1</c>), damit auch +0x11 gemessen wird — sonst trifft die
///   erste Aufwertung jedes Bauteils Arm 0 (AN.4).</item>
///   <item>nachher, Soll: +0x13 = 4, +0x11 = alt + Zeile+0x07, V Radar 7, Kanone 8/6 an
///   Quelle UND Ankunft.</item>
///   <item>Forschung Kanone (1) über den Ring: +0x12 = 3 bleibt 3.</item>
///   <item>die Reihe: Stufen 2…9 direkt (<see cref="AngebotRechnen"/> +
///   <see cref="AufwertungSchreiben"/>, Arm 0 fest) — 163 endet auf Zeile+0x09 = 9,
///   Kanone auf Zeile+0x08 = 8. Dazu MS-Rakete (8) als Probe der Bytesemantik:
///   105,110,115,120,126,132,223,58,150 (aus der Arithmetik des Originals gerechnet).</item>
/// </list>
/// <para><b>Nullmodell</b> <c>--aufwertung-setzen-alt</c>: +0x13 = 0, V Radar 3,
/// Kanone 8/2, Reihe endet auf 6 — Reapers Bild. Unterscheiden sich beide Läufe
/// nicht, misst der Prüfstand nichts.</para>
/// <para>Aufruf — ⚠ K21, nicht K3 wie im Bericht vorgeschlagen: K3 hat keine eigene
/// BASIS, und ohne Basis gibt es keine Forschung über den Ring. K21 hat Basis Platz 0,
/// 6x6 Reifen und Kanone im Angebot:
/// <c>-- --campaign=21 --no-briefing --no-movie --aufwertung-zuwachs-check</c></para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>A/V aus einem Wertetext (»A/V 8/6«) — Fenster und Werteliste.</summary>
    private static string AvAus(string text)
    {
        foreach (var z in text.Split('\n'))
            if (z.StartsWith("A/V ")) return z.Substring(4).Trim();
        return "?";
    }

    /// <summary>Das Erstellungsfenster auf (Waffe, Fahrwerk, keine Verbesserung)
    /// stellen, wie der Spieler mit den Pfeiltasten, und den ausgegebenen
    /// Wertetext zurückgeben. "" = Teil nicht in der Liste (nicht im Besitz).</summary>
    private string ZuwachsFenster(UI.DesignWindow dw, int waffe, int fahrwerk)
    {
        LoadDesignParts();
        bool aktiv = Designer.Active;
        if (!aktiv) Designer.Toggle();
        string text = "";
        if (Designer.Propulsions.Exists(p => p.Id == fahrwerk) && Designer.Weapons.Exists(p => p.Id == waffe))
        {
            while (Designer.Cursor != DesignScreen.Row.Propulsion) Designer.Move(1);
            for (int i = 0; i < 200 && Designer.CurrentPropulsion?.Id != fahrwerk; i++) Designer.Change(1);
            Designer.Move(1);   // Aufbau
            for (int i = 0; i < 200 && Designer.CurrentWeapon?.Id != waffe; i++) Designer.Change(1);
            Designer.Move(1);   // Verbesserung
            for (int i = 0; i < 40 && Designer.CurrentEquipment != null; i++) Designer.Change(1);
            if (Designer.CurrentPropulsion?.Id == fahrwerk && Designer.CurrentWeapon?.Id == waffe)
            {
                dw.Refresh();
                text = dw.ProbeWerte;
            }
        }
        if (!aktiv) Designer.Close();
        return text;
    }

    /// <summary>Die Werteliste (Produktion) eines Wertebuch-Entwurfs mit genau
    /// diesen Teilen und ohne Verbesserung; "" wenn es keinen gibt.</summary>
    private static string ZuwachsWerteliste(int waffe, int fahrwerk, out string name)
    {
        name = "";
        foreach (var e in UI.UnitStatBook.All())
            if (e.Weapon == waffe && e.Propulsion == fahrwerk && e.Equip <= 0)
            { name = e.Name; return UI.BaseWindow.StatsFor(e.Name); }
        return "";
    }

    /// <summary>Eine Forschung über den Ring kaufen und takten, bis sie fertig ist.
    /// Gibt die Takte zurück, −1 = nicht angeboten / abgelehnt.</summary>
    private int ZuwachsForschenUeberRing(int basisIdx, int spieler, int bauteil, StringBuilder sb)
    {
        var angebot = ForschungAngebote(spieler);
        int zeile = angebot.FindIndex(a => !a.Erfindung && a.Bauteil == bauteil);
        if (zeile < 0) { sb.Append($"\n   ⚠ Bauteil {bauteil} wird nicht angeboten"); return -1; }
        int preis = angebot[zeile].Preis;
        if (_money[spieler] < preis)
        {
            sb.Append($"\n   (Konto {_money[spieler]} $ auf {preis} $ gesetzt — Messwerkzeug)");
            _money[spieler] = preis;
        }
        _selected = basisIdx; _sel.Clear(); _sel.Add(basisIdx);
        UpdatePanel();
        int fertigVor = ForschungFertig;
        int gesendet = PostForschung(zeile);
        int ringTakte = RingLeeren();
        int takte = 0, deckel = preis * 2 + 400;
        while (ForschungFertig == fertigVor && takte < deckel) { SimTickFuerProbe(); takte++; }
        sb.Append($"\n   Forschung {ForschungBauteilName(bauteil)} ({bauteil}) Zeile {zeile}, ${preis}, Arm {angebot[zeile].Wurf}: "
                + $"{(gesendet > 0 ? "Satz 2005 gesendet" : "⚠ NICHT gesendet")}, Ring {ringTakte} Takt, "
                + (ForschungFertig > fertigVor ? $"fertig nach {takte} Takten" : $"⚠ NICHT fertig nach {takte} Takten"));
        return ForschungFertig > fertigVor ? takte : -1;
    }

    public string AufwertungZuwachsCheckLine(UI.DesignWindow? dw)
    {
        var sb = new StringBuilder("aufwertung-zuwachs-check (bug-601)");
        if (AufwertungSetzenAlt) sb.Append(" ⚠ NULLMODELL --aufwertung-setzen-alt: muss DURCHFALLEN (Reapers Bild)");
        if (AufwertungAlt || ForschungAlt)
            return sb.Append("\n   --aufwertung-alt/--forschung-alt steht — NICHT GEMESSEN\naufwertung-zuwachs-check: NICHT GEMESSEN").ToString();
        if (dw == null) return sb.Append("\n   kein Erstellungsfenster — NICHT GEMESSEN\naufwertung-zuwachs-check: NICHT GEMESSEN").ToString();
        UI.UnitStatBook.Load();
        BauteileVorbereiten();
        AufwertungTafelLaden();
        int h = UI.SkirmishSetup.Human is >= 0 and < 8 ? UI.SkirmishSetup.Human : 0;
        int basisIdx = -1;
        for (int i = 0; i < _entities.Count && basisIdx < 0; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding && e.BType == 1 && !e.Dead && e.Owner == h) basisIdx = i;
        }
        sb.Append($"\n   Mission {UI.SkirmishSetup.CampaignMission}, Mensch {h}, Betrachter {ViewPlayer}, Basis {(basisIdx >= 0 ? $"Platz {_entities[basisIdx].Slot}" : "KEINE")}");
        if (basisIdx < 0 || _aufwertungTafel == null || !_aufwertungTafel.ContainsKey(163) || !_aufwertungTafel.ContainsKey(1))
            return sb.Append("\n   keine Basis oder keine Aufwertungszeile 163/1 — NICHT GEMESSEN (K21 nehmen)\naufwertung-zuwachs-check: NICHT GEMESSEN").ToString();
        var c163 = BauteilFuer(h, 163)!;
        var c1 = BauteilFuer(h, 1)!;
        var z163 = _aufwertungTafel[163];
        var z1 = _aufwertungTafel[1];
        bool ok = true;
        void Soll(bool b, string text) { ok &= b; sb.Append($"\n   {(b ? "ok  " : "FEHL")} {text}"); }

        // ---- 1. vorher -------------------------------------------------------
        int s13Vor = c163[0x13], s11Vor = c163[0x11], stufeVor = c163[0x01], k12Vor = c1[0x12];
        var radarVor = Simulation.DesignMath.Compute(67, 163, 0, h);
        var kanoneVor = Simulation.DesignMath.Compute(1, 163, 0, h);
        string fKanVor = ZuwachsFenster(dw, 1, 163);
        string wKanVor = ZuwachsWerteliste(1, 163, out string nameKan);
        string wRadVor = ZuwachsWerteliste(67, 163, out string nameRad);
        sb.Append($"\n   vorher: 6x6 +0x13 {s13Vor}, +0x11 {s11Vor}, Stufe {stufeVor}; Kanone +0x12 {k12Vor}; "
                + $"Quelle Radar A/V {radarVor.Attack}/{radarVor.Defence}, Kanone-6x6 A/V {kanoneVor.Attack}/{kanoneVor.Defence}; "
                + $"Ankunft Erstellung Kanone+6x6 »{AvAus(fKanVor)}«, Werteliste »{nameKan}« »{AvAus(wKanVor)}«, »{nameRad}« »{AvAus(wRadVor)}«");
        Soll(stufeVor == 0 && s13Vor == 4 && radarVor.Defence == 7 && kanoneVor.Attack == 8 && kanoneVor.Defence == 6,
             "Teil 1 Ausgangslage: Stufe 0, +0x13 4, V Radar 7, Kanone 8/6 (Tafel)");

        // ---- 2. Forschung 6x6 Reifen über den Ring, Arm 1 ---------------------
        // ⚠ EINGRIFF (angesagt): Arm 1 vorgeben, damit +0x11 mitgemessen wird.
        _forschungWurf[163] = 1;
        sb.Append("\n   ⚠ EINGRIFF: Arm der ersten 6x6-Forschung auf 1 gesetzt (+0x11 mitmessen)");
        int t1 = ZuwachsForschenUeberRing(basisIdx, h, 163, sb);
        if (t1 < 0) return sb.Append("\naufwertung-zuwachs-check: NICHT GEMESSEN").ToString();
        var radarNach = Simulation.DesignMath.Compute(67, 163, 0, h);
        var kanoneNach = Simulation.DesignMath.Compute(1, 163, 0, h);
        string fKan = ZuwachsFenster(dw, 1, 163);
        string wKan = ZuwachsWerteliste(1, 163, out _);
        string wRad = ZuwachsWerteliste(67, 163, out _);
        int s11Soll = (byte)(s11Vor + z163[0x07]);
        sb.Append($"\n   nachher: 6x6 +0x13 {c163[0x13]}, +0x11 {c163[0x11]}, Stufe {c163[0x01]}; "
                + $"Quelle Radar A/V {radarNach.Attack}/{radarNach.Defence}, Kanone-6x6 {kanoneNach.Attack}/{kanoneNach.Defence}; "
                + $"Ankunft Erstellung »{AvAus(fKan)}«, Werteliste Kanone »{AvAus(wKan)}«, Radar »{AvAus(wRad)}«");
        Soll(c163[0x01] == 1, "Teil 2a Stufe 0 → 1 (über den Ring)");
        Soll(c163[0x13] == 4, $"Teil 2b +0x13 bleibt 4 (4 + (9−4)/9 = 4 + 0; gemessen {c163[0x13]})");
        Soll(c163[0x11] == s11Soll, $"Teil 2c +0x11 {s11Vor} + Zeile+0x07 {z163[0x07]} = {s11Soll} (gemessen {c163[0x11]})");
        Soll(radarNach.Defence == 7, $"Teil 2d Quelle V Radar 7 (gemessen {radarNach.Defence})");
        Soll(kanoneNach.Attack == 8 && kanoneNach.Defence == 6, $"Teil 2e Quelle Kanone-6x6 8/6 (gemessen {kanoneNach.Attack}/{kanoneNach.Defence})");
        Soll(AvAus(fKan) == "8/6", $"Teil 2f ANKUNFT Erstellungsfenster Kanone+6x6 »8/6« (gezeigt »{AvAus(fKan)}«)");
        if (wKan.Length > 0) Soll(AvAus(wKan) == "8/6", $"Teil 2g ANKUNFT Werteliste »{nameKan}« »8/6« (gezeigt »{AvAus(wKan)}«)");
        else sb.Append("\n   --   Teil 2g: kein Wertebuch-Entwurf Kanone/6x6 — Werteliste nicht gemessen");
        if (wRad.Length > 0) Soll(AvAus(wRad).EndsWith("/7"), $"Teil 2h ANKUNFT Werteliste »{nameRad}« V 7 (gezeigt »{AvAus(wRad)}«)");
        else sb.Append("\n   --   Teil 2h: kein Wertebuch-Entwurf Radar/6x6 — Werteliste nicht gemessen");

        // ---- 3. Forschung Kanone über den Ring -------------------------------
        int t2 = ZuwachsForschenUeberRing(basisIdx, h, 1, sb);
        Soll(t2 >= 0 && c1[0x12] == k12Vor, $"Teil 3 Kanone +0x12 {k12Vor} → {c1[0x12]} (Soll bleibt {k12Vor}: (8−3)/9 = 0)");

        // ---- 4. die Reihe über neun Stufen -----------------------------------
        // Direkt, Arm 0 fest (angesagt) — die Forschung über den Ring ist oben
        // belegt; hier geht es um die Arithmetik von acht weiteren Stufen.
        List<int> Reihe(int bauteil, int feld, int start)
        {
            var r = new List<int>();
            var c = BauteilFuer(h, bauteil)!;
            if (start >= 0) r.Add(start);
            while (c[0x01] < 9)
            {
                var w = AngebotRechnen(h, bauteil, 0);
                if (!w.Gueltig || !AufwertungSchreiben(h, w)) break;
                r.Add(c[feld]);
            }
            return r;
        }
        var r163 = Reihe(163, 0x13, c163[0x13]);
        var r1 = Reihe(1, 0x12, c1[0x12]);
        sb.Append("\n   Reihe 6x6 +0x13 nach Stufe 1..9: " + string.Join(",", r163));
        Soll(r163.Count == 9 && string.Join(",", r163) == "4,4,4,4,5,6,7,8,9" && r163[^1] == z163[0x09],
             $"Teil 4a 6x6 Reihe 4,4,4,4,5,6,7,8,9 endet auf Zeile+0x09 = {z163[0x09]}");
        sb.Append("\n   Reihe Kanone +0x12 nach Stufe 1..9: " + string.Join(",", r1));
        Soll(r1.Count == 9 && string.Join(",", r1) == "3,3,3,3,4,5,6,7,8" && r1[^1] == z1[0x08],
             $"Teil 4b Kanone Reihe 3,3,3,3,4,5,6,7,8 endet auf Zeile+0x08 = {z1[0x08]}");
        var radarEnde = Simulation.DesignMath.Compute(67, 163, 0, h);
        sb.Append($"\n   Radar nach Stufe 9: A/V {radarEnde.Attack}/{radarEnde.Defence} (Soll V 3+9 = 12)");
        if (_aufwertungTafel.TryGetValue(8, out var z8) && BauteilFuer(h, 8) is { Length: >= 58 } c8 && c8[0x01] == 0)
        {
            var r8 = Reihe(8, 0x12, -1);
            sb.Append("\n   Reihe MS-Rakete (8) +0x12 nach Stufe 1..9: " + string.Join(",", r8));
            Soll(string.Join(",", r8) == "105,110,115,120,126,132,223,58,150" && r8.Count == 9 && r8[^1] == z8[0x08],
                 $"Teil 4c MS-Rakete mit Bytesemantik (movsx im Angebot, add cl,al): 105,110,115,120,126,132,223,58,150, Ende Zeile+0x08 = {z8[0x08]}");
        }
        sb.Append($"\n   (Wuerfe {AufwertungWurf[0]}/{AufwertungWurf[1]}/{AufwertungWurf[2]}/{AufwertungWurf[3]}, angewandt {AufwertungAngewandt}, Forschung fertig {ForschungFertig})");
        sb.Append(ok ? "\naufwertung-zuwachs-check: BESTANDEN" : "\naufwertung-zuwachs-check: DURCHGEFALLEN");
        return sb.ToString();
    }
}

public partial class MapViewer
{
    private bool _aufwertungZuwachsCheck;

    /// <summary>bug-601: true = Schalter erkannt.</summary>
    private bool AufwertungZuwachsSchalter(string a)
    {
        switch (a)
        {
            case "--aufwertung-zuwachs-check": _aufwertungZuwachsCheck = true; return true;
            case "--aufwertung-setzen-alt": MapEntityLayer.AufwertungSetzenAlt = true; return true;
        }
        return false;
    }

    /// <summary>bug-601: <c>--aufwertung-zuwachs-check</c> — siehe
    /// <see cref="MapEntityLayer.AufwertungZuwachsCheckLine"/>. Ruft selbst Quit.</summary>
    private bool AufwertungZuwachsCheckLauf()
    {
        if (!_aufwertungZuwachsCheck || !_entities.ErwartungBereit()) return false;
        _aufwertungZuwachsCheck = false;
        GD.Print(_entities.AufwertungZuwachsCheckLine(_designWindow));
        GetTree().Quit(0);
        return true;
    }
}
