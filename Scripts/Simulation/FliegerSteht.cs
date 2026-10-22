namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Text;
using Godot;

/// <summary>
/// <b>bug-598 — »ein kleiner Aufklärungsflieger, der da in der Luft steht und sich
/// nicht mehr bewegt«</b> (Reaper R4, 19.10.2026, Bild reaper6.png;
/// berichte/flieger-steht-opus.md).
///
/// <para>Im Original kann ein FLUGZEUG (Art 1/2/3) nicht in der Luft stehen. Bei
/// uns gab es drei Wege dahin, alle drei hier mit Gegenschalter:</para>
/// <list type="bullet">
///   <item><b>A</b> unbewaffnet mit Ziel → Heimkehr (uk 7 @0x4236AE/@0x4236E3),
///   <c>--unbewaffnet-angriff-alt</c>.</item>
///   <item><b>B</b> Sprit 0 → Absturz (@0x422F40 → 0x401A37 → 0x425EA0),
///   <c>--sprit-absturz-aus</c>.</item>
///   <item><b>C</b> <c>sp_unten</c> gebauter Flugzeuge aus der Vorlage +0x2A
///   (spawn_aircraft @0x4B15B6), <c>--sp-unten-alt</c>.</item>
/// </list>
/// <para>Prüfstand <c>--flieger-steht-check</c> (Lauf, am Ausstieg),
/// <c>--flieger-steht-check=spion</c> und <c>=hand</c> (sofort, eigene Szene).</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--unbewaffnet-angriff-alt</c> — Stand 0.6.10: ein Flieger ohne
    /// Munition behält sein Ziel und klebt darüber.</summary>
    public static bool UnbewaffnetAngriffAlt;
    /// <summary><c>--sprit-absturz-aus</c> — Stand 0.6.10: ein leerer Flieger
    /// bleibt in der Luft stehen (AirDrift steigt aus).</summary>
    public static bool SpritAbsturzAus;
    /// <summary><c>--sp-unten-alt</c> — Stand 0.6.10: gebaute Flugzeuge haben
    /// <c>sp_unten</c> 0 und können bis zum Stillstand bremsen.</summary>
    public static bool SpUntenAlt;

    public int AngriffOhneMunitionHeim, SpritAbstuerze;

    /// <summary>
    /// <b><c>sp_unten</c> der Flugzeugvorlage</b> — Byte <c>+0x2A</c> der Tafel
    /// <c>0x51B020</c> (48 B je Zeile), das spawn_aircraft @0x4B15B6 (und der
    /// KI-Spawner @0x4B3156) nach Satz <c>+0x18</c> schreibt. Am 22.10.2026 aus
    /// der C-EXE gelesen (scratchpad vorlage.py):
    /// <code>
    ///   Jagdflieger 4 · Bomber 4 · Spionageflieger 4
    ///   Transport/Kampf/Treibstoff/Munition/Mechaniker-Heli 0
    /// </code>
    /// Gegenprobe Kartensätze: alle Art-1/2 tragen stufe_unten 4 (44/45), alle
    /// Helis 0. ⚠ Die Zuordnung Vorlagenzeile → Art ist unsere: Art 1/2/3 sind die
    /// drei Flugzeugzeilen, alles ab 10 sind Hubschrauber.
    /// </summary>
    public static int SpUntenVorlage(int kind) => SpUntenAlt ? 0 : kind is 1 or 2 or 3 ? 4 : 0;

    // ================== der Prüfstand: Takt und Auskunft ======================

    /// <summary>Messung je Maschine, nur gelesen (kein Wurf, kein Schreiben in
    /// den Spielzustand — der Gleichschritt bleibt unberührt).</summary>
    public static bool FliegerStehtAktiv;

    private sealed class StehMessung
    {
        public Vector2 Letzte;
        public int Lauf, Laengste, Kind;
        public string Grund = "", Name = "";
        public bool Gesehen;
    }
    private readonly Dictionary<Special, StehMessung> _stehen = new();

    /// <summary>Unter dieser Strecke (px) je Takt gilt eine Maschine als stehend.</summary>
    private const float StehSchwellePx = 0.5f;
    /// <summary>⚠ UNSERE SETZUNG: ab wie vielen Takten ohne Bewegung »steht« eine
    /// Maschine (zwei Sekunden bei 60 Takten) — der Start am Platz dauert einige
    /// Takte (Stufe 0 → hoch), das ist kein Stehen.</summary>
    public const int StehGrenzeTakte = 120;

    private string StehGrund(Special a)
        => a.Fuel <= 0 ? "Sprit 0"
         : a.Target >= 0 && a.Ammo <= 0 ? "Ziel ohne Munition"
         : FlugStufe(a) <= 0 ? "Stufe 0"
         : "sonst";

    private void FliegerStehtTakt()
    {
        foreach (var a in _special)
        {
            if (!_stehen.TryGetValue(a, out var m))
            { m = new StehMessung { Letzte = a.Pos, Kind = a.Kind, Name = a.Name }; _stehen[a] = m; continue; }
            if (a.Dead || a.Stored || a.Absturz) { m.Lauf = 0; m.Letzte = a.Pos; continue; }
            m.Gesehen = true;
            if (a.Pos.DistanceTo(m.Letzte) < StehSchwellePx)
            {
                m.Lauf++;
                if (m.Lauf > m.Laengste) { m.Laengste = m.Lauf; m.Grund = StehGrund(a); }
            }
            else m.Lauf = 0;
            m.Letzte = a.Pos;
        }
    }

    /// <summary>Die Zeile am Ausstieg — <c>--flieger-steht-check</c>.</summary>
    public string FliegerStehtAuskunft()
    {
        int flugzeuge = 0, helis = 0, stehF = 0, stehH = 0, laengsteF = 0, laengsteH = 0;
        var gruende = new SortedDictionary<string, int>();
        var arten = new SortedDictionary<int, int>();
        foreach (var m in _stehen.Values)
        {
            if (!m.Gesehen) continue;
            bool heli = m.Kind >= 10;
            if (heli) { helis++; laengsteH = System.Math.Max(laengsteH, m.Laengste); }
            else { flugzeuge++; laengsteF = System.Math.Max(laengsteF, m.Laengste); }
            if (m.Laengste <= StehGrenzeTakte) continue;
            if (heli) { stehH++; continue; }
            stehF++;
            arten[m.Kind] = arten.GetValueOrDefault(m.Kind) + 1;
            gruende[m.Grund] = gruende.GetValueOrDefault(m.Grund) + 1;
        }
        string urteil = flugzeuge == 0 ? "NICHT GEMESSEN (kein Flugzeug in der Luft)"
                      : stehF == 0 ? "BESTANDEN" : "DURCHGEFALLEN";
        var sb = new StringBuilder("flieger-steht-check: ");
        sb.Append($"Flugzeuge (Art 1-3) in der Luft {flugzeuge}, davon > {StehGrenzeTakte} Takte regungslos {stehF}");
        if (stehF > 0)
        {
            sb.Append(" (");
            foreach (var kv in arten) sb.Append($"Art {kv.Key}: {kv.Value} ");
            sb.Append("· Gruende ");
            foreach (var kv in gruende) sb.Append($"{kv.Key} {kv.Value} ");
            sb.Append(')');
        }
        sb.Append($" · laengste {laengsteF} Takte · Helis (Art >= 10, Schweben erlaubt) {helis}, davon > {StehGrenzeTakte} Takte {stehH}, laengste {laengsteH}");
        sb.Append($" · Sprit-Abstuerze {SpritAbstuerze} · Angriff ohne Munition heim {AngriffOhneMunitionHeim}");
        sb.Append(SpritAbsturzAus ? " [--sprit-absturz-aus]" : "")
          .Append(UnbewaffnetAngriffAlt ? " [--unbewaffnet-angriff-alt]" : "")
          .Append(SpUntenAlt ? " [--sp-unten-alt]" : "");
        sb.Append($" · Ergebnis {urteil}");
        return sb.ToString();
    }

    // ================== die zwei gestellten Szenen ===========================

    /// <summary>Ein eigener Flughafen und darin ein frisch GEKAUFTES Flugzeug der
    /// Art <paramref name="art"/> — gekauft über <see cref="BuyAircraft"/> wie der
    /// Knopf (spawn_aircraft). ⚠ EINGRIFF: fehlen Teile, werden sie dem Flughafen
    /// zugelegt (angesagt).</summary>
    private Special? FsKaufen(int[] arten, StringBuilder sb, out Entity? feld)
    {
        feld = _entities.Find(x => x.IsBuilding && !x.Dead && !x.IsProp && x.BType == 9
                                   && x.Owner == ViewPlayer && x.Built != 0);
        if (feld == null)
        {
            var alle = _entities.FindAll(x => x.IsBuilding && !x.Dead && !x.IsProp && x.BType == 9);
            sb.Append($"  kein eigener fertiger Flughafen (Spieler {ViewPlayer}; auf der Karte: "
                    + string.Join(", ", alle.ConvertAll(x => $"Spieler {x.Owner} gebaut {x.Built}"))
                    + ") — NICHT GEMESSEN\n");
            return null;
        }
        var menu = AirMenu(feld);
        int k = -1;
        foreach (int art in arten) if (k < 0) k = menu.FindIndex(x => x.Kind == art);
        if (k < 0) { sb.Append($"  Art {string.Join("/", arten)} am Flughafen nicht kaufbar — NICHT GEMESSEN\n"); return null; }
        var d = menu[k];
        if (d.CostW > feld.StockW || d.CostF > feld.StockF || d.CostS > feld.StockS)
        {
            feld.StockW = System.Math.Max(feld.StockW, d.CostW);
            feld.StockF = System.Math.Max(feld.StockF, d.CostF);
            feld.StockS = System.Math.Max(feld.StockS, d.CostS);
            sb.Append("  ⚠ EINGRIFF: Teile am Flughafen zugelegt\n");
        }
        if (feld.Hangar != null && feld.Hangar.Count >= System.Math.Max(1, feld.HangarSize))
        { sb.Append("  Hangar voll — NICHT GEMESSEN\n"); return null; }
        feld.MenuIndex = k;
        int vor = _special.Count;
        if (!BuyAircraft(feld) || _special.Count == vor) { sb.Append($"  Kauf abgelehnt: {_order} — NICHT GEMESSEN\n"); return null; }
        var a = _special[^1];
        sb.Append($"  gekauft: {a.Name} (Art {a.Kind}, Platz {a.Slot}), sp_oben {a.Speed}, sp_unten {a.StufeUnten}, "
                + $"Munition {a.Ammo}, Sprit {a.Fuel}\n");
        return a;
    }

    /// <summary><c>--flieger-steht-check=spion</c> — Fall A: ein Spion mit
    /// Angriffsbefehl über den Spielerweg (Befehl 502 Modus 7 über den Ring, wie
    /// <c>PostStaffelStarten</c> je Maschine).</summary>
    public string FliegerStehtSpionLine()
    {
        var sb = new StringBuilder("flieger-steht-check=spion\n");
        FliegerStehtAktiv = true;
        var a = FsKaufen(new[] { 3 }, sb, out var feld);
        if (a == null || feld == null) return sb.Append("flieger-steht-check: Ergebnis NICHT GEMESSEN").ToString();
        // das naechste feindliche Fahrzeug als Ziel
        int ziel = -1; float best = float.MaxValue;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsProp || e.Dead || e.IsBuilding || e.HpMax <= 0 || e.Owner is < 0 or > 7) continue;
            if (Allied(a.Owner, e.Owner) || Untergestellt(e)) continue;
            float dd = feld.Pos.DistanceTo(e.Pos);
            if (dd < best) { best = dd; ziel = i; }
        }
        if (ziel < 0) return sb.Append("  kein feindliches Ziel — NICHT GEMESSEN\nflieger-steht-check: Ergebnis NICHT GEMESSEN").ToString();
        var t = _entities[ziel];
        sb.Append($"  Ziel: Satz {ziel} ({LabelOf(t)}, Spieler {t.Owner}) auf ({t.Col},{t.Row}), {best / TileW:0} Zellbreiten vom Flughafen\n");
        int heimVor = AngriffOhneMunitionHeim;
        Emit(Simulation.Commands.CommandRecord.Make(Simulation.Commands.CommandOp.Flugzeugstart,
                (byte)ViewPlayer, (short)a.Slot, (short)ZielModusZiel,
                (short)t.Col, (short)t.Row, (short)ziel));
        int ring = RingLeeren();
        bool gestartet = !a.Stored;
        int ueberZiel = 0, gelandetTakt = -1, takte = 3600;
        int lauf = 0, laengste = 0;
        var letzte = a.Pos;
        for (int k = 0; k < takte; k++)
        {
            SimTickFuerProbe();
            if (a.Dead || a.Absturz) break;
            if (a.Stored) { if (gelandetTakt < 0) gelandetTakt = k + 1; letzte = a.Pos; lauf = 0; continue; }
            if (!t.Dead && a.Pos.DistanceTo(AirAimPoint(t)) < TileW * 1.5f) ueberZiel++;
            if (a.Pos.DistanceTo(letzte) < StehSchwellePx) { lauf++; laengste = System.Math.Max(laengste, lauf); }
            else lauf = 0;
            letzte = a.Pos;
        }
        int heim = AngriffOhneMunitionHeim - heimVor;
        bool ok = gestartet && laengste <= StehGrenzeTakte && heim >= 1 && ueberZiel == 0;
        sb.Append($"  Befehl 502 Modus 7 ueber den Ring ({ring} Takt(e)), gestartet {gestartet}; in {takte} Takten: "
                + $"Angriff ohne Munition heim +{heim}, Takte ueber dem Ziel {ueberZiel}, laengste Zeit regungslos {laengste} Takte, "
                + $"gelandet {(gelandetTakt >= 0 ? $"nach {gelandetTakt} Takten" : "nie")}"
                + (a.Absturz || a.Dead ? ", ABGESCHOSSEN" : "")
                + (UnbewaffnetAngriffAlt ? " [Nullmodell --unbewaffnet-angriff-alt]" : "")
                + $" (Soll: heim >= 1, ueber dem Ziel 0, regungslos <= {StehGrenzeTakte})\n");
        sb.Append($"flieger-steht-check: Ergebnis {(ok ? "BESTANDEN" : "DURCHGEFALLEN")}");
        return sb.ToString();
    }

    /// <summary><c>--flieger-steht-check=hand</c> — Fall C: ein GEKAUFTER
    /// Jagdflieger unter Handsteuerung, hoch, dann 40× ↓ (HandsteuerungLuftTakt,
    /// derselbe Weg wie die Taste). Soll: Stufe = sp_unten 4, und er fliegt weiter.
    /// Danach Fall B unter Hand: Tank auf 1 — Soll: Absturz, nicht Stillstand.</summary>
    public string FliegerStehtHandLine()
    {
        var sb = new StringBuilder("flieger-steht-check=hand\n");
        FliegerStehtAktiv = true;
        var a = FsKaufen(new[] { 1, 2, 3 }, sb, out _);
        if (a == null) return sb.Append("flieger-steht-check: Ergebnis NICHT GEMESSEN").ToString();
        if (!HandsteuerungLuftBeginnen(a.Slot))
            return sb.Append("  Handsteuerung nicht uebernommen — NICHT GEMESSEN\nflieger-steht-check: Ergebnis NICHT GEMESSEN").ToString();
        for (int t = 0; t < 30; t++) HandProbeTakt(hoch: true, steigen: true);
        int stufeHoch = a.Stufe;
        for (int t = 0; t < 40; t++) HandProbeTakt(runter: true);
        int stufeUnten = a.Stufe;
        // jetzt fliegen lassen — die Handsteuerung hat keinen eigenen Zweig (AirDrift)
        var start = a.Pos;
        int lauf = 0, laengste = 0;
        var letzte = a.Pos;
        for (int t = 0; t < 300 && !a.Absturz && !a.Dead; t++)
        {
            SimTickFuerProbe();
            if (a.Pos.DistanceTo(letzte) < StehSchwellePx) { lauf++; laengste = System.Math.Max(laengste, lauf); } else lauf = 0;
            letzte = a.Pos;
        }
        float strecke = a.Pos.DistanceTo(start);
        bool cOk = stufeUnten == 4 && laengste <= StehGrenzeTakte && strecke > TileW;
        sb.Append($"  C Handsteuerung: Stufe hoch {stufeHoch}, nach 40x runter {stufeUnten} (Soll sp_unten 4), "
                + $"in 300 Takten {strecke / TileW:0.0} Zellbreiten geflogen, laengste Zeit regungslos {laengste} Takte"
                + (SpUntenAlt ? " [Nullmodell --sp-unten-alt]" : "") + (cOk ? "" : "   ⚠") + "\n");
        // B unter Hand: der letzte Tropfen
        bool bOk = true;
        if (!a.Absturz && !a.Dead)
        {
            int abVor = SpritAbstuerze;
            a.Fuel = 1; a.FuelFrac = 0f;
            int bisAbsturz = -1; lauf = 0; laengste = 0; letzte = a.Pos;
            for (int t = 0; t < 600; t++)
            {
                SimTickFuerProbe();
                if (a.Absturz || a.Dead) { bisAbsturz = t + 1; break; }
                if (a.Pos.DistanceTo(letzte) < StehSchwellePx) { lauf++; laengste = System.Math.Max(laengste, lauf); } else lauf = 0;
                letzte = a.Pos;
            }
            bOk = bisAbsturz > 0 && SpritAbstuerze - abVor == 1 && laengste <= StehGrenzeTakte;
            sb.Append($"  B Handsteuerung, Sprit 1: Sprit-Absturz +{SpritAbstuerze - abVor} "
                    + $"{(bisAbsturz > 0 ? $"nach {bisAbsturz} Takten" : "NIE")}, laengste Zeit regungslos {laengste} Takte"
                    + (SpritAbsturzAus ? " [Nullmodell --sprit-absturz-aus]" : "") + (bOk ? "" : "   ⚠") + "\n");
        }
        else sb.Append("  B Handsteuerung: Maschine schon abgestuerzt — uebersprungen\n");
        sb.Append($"flieger-steht-check: Ergebnis {(cOk && bOk ? "BESTANDEN" : "DURCHGEFALLEN")}");
        return sb.ToString();
    }
}

public partial class MapViewer
{
    private string? _fliegerStehtCheck;
    private bool _fliegerStehtGedruckt;

    /// <summary>Die Szenen »spion« und »hand« laufen sofort und beenden.</summary>
    private bool FliegerStehtProbeLauf()
    {
        if (_fliegerStehtCheck is not ("spion" or "hand") || !_entities.ErwartungBereit()) return false;
        string fall = _fliegerStehtCheck;
        _fliegerStehtCheck = null;
        _fliegerStehtGedruckt = true;
        GD.Print(fall == "spion" ? _entities.FliegerStehtSpionLine() : _entities.FliegerStehtHandLine());
        GetTree().Quit(0);
        return true;
    }

    /// <summary>Der Lauf (<c>--flieger-steht-check</c> ohne Fall) am Ausstieg.</summary>
    private void FliegerStehtAusgeben()
    {
        if (_fliegerStehtGedruckt || _fliegerStehtCheck != "lauf") return;
        _fliegerStehtGedruckt = true;
        GD.Print(_entities.FliegerStehtAuskunft());
    }
}
