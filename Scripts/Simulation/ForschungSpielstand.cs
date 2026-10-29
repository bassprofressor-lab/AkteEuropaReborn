namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Text;
using Godot;
using GDict = Godot.Collections.Dictionary<string, Godot.Variant>;

/// <summary>
/// ⭐ 29.10.2026, bug-666 (Nachtrag zu P2 der Testerrunde 0.6.12) — <b>die LAUFENDE
/// Forschung geht in den Spielstand</b> (Format 5).
///
/// <para>Das Original sichert sie: sec96 = die zehn Laufsaetze (<c>0xA3A9D0</c>, 10 × 16 B,
/// »Too many researches« beim elften) und sec97 = die Losnummer (<c>0xA3A9C8</c>), belegt an
/// Original-Spielstaenden (<c>OFFENE_FRAGEN.md</c> »sec96/97: 10 × 16 laufende Forschungen«).
/// Bei uns fehlten beide bis heute (<c>OFFENE_FRAGEN.md</c> »Der Spielstand: sec96/sec97
/// werden nicht gesichert«): wer mitten in einer Forschung speicherte und lud, hatte sein
/// Geld bezahlt und die Forschung verloren — die Basis stand weiter auf »forschen«.</para>
///
/// <para>Was mitgeht, Feld fuer Feld wie <see cref="ForschungLauf"/>: Platz 0…9 (die STELLE
/// im Feld, nicht nur der Inhalt — der Takt laeuft die Plaetze der Reihe nach ab und der
/// naechste Kauf nimmt den ersten freien), Basis (<c>+0x00</c>, unser <c>Entity.Slot</c>;
/// der Platz geht im Spielstand mit, <c>"slot"</c>), Preis = Dauer (<c>+0x02</c>),
/// Fortschritt (<c>+0x04</c>), Spieler (<c>+0x0D</c>), die beim KAUF gerechneten Werte
/// (<c>+0x06…+0x0E</c>, die neun Argumente von Busbefehl 531) und die Weiche
/// Erfindung/Index (Marke 200+i). Dazu die Losnummer sec97.</para>
///
/// <para>⚠ <b>[Setzung]</b> Ebenfalls mit: <c>_forschungWurf</c>, der vorgewuerfelte Arm der
/// naechsten Stufe je Bauteil. Im Original ist das das Laufzeitfeld <c>Zeile+0x02</c> der
/// Aufwertungstafel; OB der Speicherer 0x41D210 diese Tafel schreibt, ist NICHT gelesen. Ohne
/// ihn zeigte das Angebot nach dem Laden einen anderen Arm als vorher (Vorschau ≠ Kauf).</para>
///
/// <para>Beim MISSIONSWECHSEL bleibt es, wie es ist: das Original nullt die Laufsaetze
/// (<c>0x4D02C8…0x4D02DA</c>, <c>byte[0xA3A9D1 + 16·i] := 0</c>), und bei uns beginnt jede
/// Mission mit einer neuen Szene und leerem Feld. Der Spielstand traegt die Laufsaetze nur
/// innerhalb derselben Mission.</para>
///
/// <para>Gegenschalter <c>--laufende-forschung-alt</c>: schreibt und liest nichts davon und
/// schreibt das Format wie 0.6.12-P2 (4). Pruefstand <c>--laufende-forschung-check</c>
/// (<see cref="MapViewer"/>, <c>Rendering/LaufendeForschungLauf.cs</c>).</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--laufende-forschung-alt</c> — Stand bis bug-666: die Laufsaetze und die
    /// Losnummer gehen nicht in den Spielstand.</summary>
    public static bool LaufendeForschungAlt;

    /// <summary>Fuer den Pruefstand: wie viele Laufsaetze der letzte Ladevorgang
    /// zuruecklegte (−1 = der Stand trug keine).</summary>
    public static int LaufendeForschungGeladen = -1;

    // ---- schreiben / lesen --------------------------------------------------------

    /// <summary>sec96 + sec97 (+ unser Wuerfelstand) in den Spielstand (Format 5).</summary>
    private void LaufendeForschungInSpielstand(Core.SaveGame.Writer w)
    {
        if (LaufendeForschungAlt) return;
        w.Num("forschung_los", _forschungLos);                    // sec97, 0xA3A9C8
        w.ArrayStart("forschung_lauf");                           // sec96, 0xA3A9D0
        for (int i = 0; i < ForschungPlaetze; i++)
        {
            var l = _forschung[i];
            if (!l.Belegt) continue;
            var v = l.Werte;
            w.ItemStart();
            w.Num("platz", i).Num("basis", l.Basis).Num("preis", l.Preis)
             .Num("fortschritt", l.Fortschritt).Num("spieler", l.Spieler)
             .Bool("erfindung", l.Erfindung).Num("erfindung_index", l.ErfindungIndex)
             .Num("bauteil", v.Bauteil).Bool("fahrwerk", v.Fahrwerk).Num("stufe", v.Stufe)
             .Num("wurf", v.Wurf).Num("d0", v.D0).Num("a", v.A).Num("b", v.B).Num("c", v.C)
             .Num("d", v.D).Num("e", v.E).Bool("gueltig", v.Gueltig);
            w.ItemEnd();
        }
        w.ArrayEnd();
        // ⚠ [Setzung] siehe Kopf: der vorgewuerfelte Arm je Bauteil.
        var wuerfe = new List<int>(_forschungWurf.Keys);
        wuerfe.Sort();
        w.ArrayStart("forschung_wurf");
        foreach (int b in wuerfe) { w.ItemStart(); w.Num("b", b).Num("w", _forschungWurf[b]); w.ItemEnd(); }
        w.ArrayEnd();
    }

    /// <summary>Liest sie zurueck. Ein Stand ohne Schluessel (Format 1..4) laesst das Feld,
    /// wie es ist — in einer frisch geladenen Szene also leer (= bisheriges Verhalten).</summary>
    private void LaufendeForschungAusSpielstand(GDict root)
    {
        LaufendeForschungGeladen = -1;
        if (LaufendeForschungAlt || !root.TryGetValue("forschung_lauf", out var lv)
            || lv.VariantType != Variant.Type.Array) return;
        for (int i = 0; i < ForschungPlaetze; i++) _forschung[i] = default;
        _forschungLos = GetI(root, "forschung_los");
        int n = 0;
        foreach (var it in lv.AsGodotArray())
        {
            if (it.VariantType != Variant.Type.Dictionary) continue;
            var d = it.AsGodotDictionary<string, Variant>();
            int platz = GetI(d, "platz", -1);
            if (platz < 0 || platz >= ForschungPlaetze || _forschung[platz].Belegt) continue;
            _forschung[platz] = new ForschungLauf
            {
                Basis = GetI(d, "basis", -1),
                Belegt = true,
                Preis = GetI(d, "preis"),
                Fortschritt = GetI(d, "fortschritt"),
                Spieler = Mathf.Clamp(GetI(d, "spieler"), 0, 7),
                Erfindung = GetB(d, "erfindung"),
                ErfindungIndex = GetI(d, "erfindung_index", -1),
                Werte = new AufwertungWerte(GetI(d, "bauteil"), GetB(d, "fahrwerk"), GetI(d, "stufe"),
                                            GetI(d, "wurf"), GetI(d, "d0"), GetI(d, "a"), GetI(d, "b"),
                                            GetI(d, "c"), GetI(d, "d"), GetI(d, "e"), GetB(d, "gueltig")),
            };
            n++;
        }
        _forschungWurf.Clear();
        if (root.TryGetValue("forschung_wurf", out var wv) && wv.VariantType == Variant.Type.Array)
            foreach (var it in wv.AsGodotArray())
            {
                if (it.VariantType != Variant.Type.Dictionary) continue;
                var d = it.AsGodotDictionary<string, Variant>();
                _forschungWurf[GetI(d, "b")] = GetI(d, "w");
            }
        LaufendeForschungGeladen = n;
    }

    // ---- Abdruck (Spielstand-Vergleich) und Feldzeile (Pruefstand) -------------------

    /// <summary>Ein Laufsatz als Zeile — alle Felder, die der Spielstand traegt.</summary>
    public string LaufsatzZeile(int i)
    {
        var l = _forschung[i];
        if (!l.Belegt) return $"Platz {i} frei";
        var v = l.Werte;
        string was = l.Erfindung ? $"Erfindung {l.ErfindungIndex}" : $"Bauteil {v.Bauteil} Stufe {v.Stufe}";
        return $"Platz {i}: Basis {l.Basis}, Spieler {l.Spieler}, {was}, {l.Fortschritt}/{l.Preis} " +
               $"(Rest {l.Preis - l.Fortschritt}), Werte {v.Wurf}/{v.D0}/{v.A}/{v.B}/{v.C}/{v.D}/{v.E}" +
               $"{(v.Fahrwerk ? " F" : "")}{(v.Gueltig ? "" : " ungueltig")}";
    }

    /// <summary>Der Teil »Laufende Forschung« fuer <c>--spielstand-check</c>.</summary>
    public string LaufendeForschungFingerabdruck()
    {
        int n = 0; long s = 0;
        for (int i = 0; i < ForschungPlaetze; i++)
        {
            var l = _forschung[i];
            if (!l.Belegt) continue;
            n++;
            var v = l.Werte;
            s += (i + 1) * 1000003L + l.Basis * 7919L + l.Spieler * 131L + l.Preis * 31L + l.Fortschritt * 17L
               + (l.Erfindung ? 200 + l.ErfindungIndex : v.Bauteil) * 101L + v.Stufe * 13L + v.Wurf * 5L
               + (v.D0 + v.A * 3L + v.B * 5L + v.C * 7L + v.D * 11L + v.E * 13L);
        }
        long wuerfe = 0;
        foreach (var kv in _forschungWurf) wuerfe += (kv.Key + 1) * (kv.Value + 1);
        return $"Laufende Forschung {n} Laufsaetze, Pruefzahl {s}, Los {_forschungLos}, Wuerfe {_forschungWurf.Count}/{wuerfe}";
    }

    // ---- die Probe: eine Forschung in einer Basis starten -------------------------------

    /// <summary>Die erste eigene Basis (Art 1); fehlt sie, wird die erste fremde dem Spieler
    /// gegeben (⚠ EINGRIFF, im Text genannt). −1 = keine Basis auf der Karte.</summary>
    public int ForschungProbeBasis(int me, out string eingriff)
    {
        eingriff = "";
        int fremd = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var b = _entities[i];
            if (!b.IsBuilding || b.IsProp || b.Dead || b.BType != 1) continue;
            if (b.Owner == me) return i;
            if (fremd < 0) fremd = i;
        }
        if (fremd < 0) return -1;
        var f = _entities[fremd];
        eingriff = $"EINGRIFF Basis Platz {f.Slot} von Spieler {f.Owner} an Spieler {me}; ";
        f.Owner = f.Team = f.ShownOwner = me;
        return fremd;
    }

    /// <summary>Kauft in der Basis <paramref name="idx"/> die erste AUFWERTUNG des Angebots
    /// mit Preis ≥ <paramref name="mindest"/> (sonst die teuerste Zeile), auf dem echten Weg
    /// <see cref="ForschungKaufen"/>. Das Geld wird vorher um den Preis erhoeht
    /// (⚠ EINGRIFF), damit der Kauf nicht am Konto scheitert. Rueckgabe: Platz oder −1.</summary>
    public int ForschungProbeKaufen(int idx, int mindest, StringBuilder sb)
    {
        var basis = _entities[idx];
        int me = Mathf.Clamp(basis.Owner, 0, 7);
        var angebot = ForschungAngebote(me);
        if (angebot.Count == 0) { sb.Append("kein Angebot"); return -1; }
        int zeile = -1;
        for (int z = 0; z < angebot.Count && zeile < 0; z++)
            if (!angebot[z].Erfindung && angebot[z].Preis >= mindest) zeile = z;
        if (zeile < 0)
            for (int z = 0; z < angebot.Count; z++)
                if (zeile < 0 || angebot[z].Preis > angebot[zeile].Preis) zeile = z;
        var a = angebot[zeile];
        _money[me] += a.Preis;
        sb.Append($"EINGRIFF Geld +{a.Preis}; ");
        if (!ForschungKaufen(basis, zeile)) { sb.Append($"Kauf »{a.Name}« abgelehnt ({_order})"); return -1; }
        for (int i = 0; i < ForschungPlaetze; i++)
            if (_forschung[i].Belegt && _forschung[i].Basis == basis.Slot)
            {
                sb.Append($"gekauft »{a.Name}« ${a.Preis} in Basis Platz {basis.Slot}");
                return i;
            }
        sb.Append("gekauft, aber kein Laufsatz"); return -1;
    }

    /// <summary>Takte mit dem vollen Simulationstakt, bis der Laufsatz <paramref name="platz"/>
    /// frei wird (= abgeschlossen) — oder <paramref name="deckel"/> erreicht ist (dann −1).</summary>
    public int ForschungTakteBisAbschluss(int platz, int deckel)
    {
        if (platz < 0 || platz >= ForschungPlaetze || !_forschung[platz].Belegt) return -1;
        int start = ForschungFertig + ErfindungFertig;
        for (int t = 1; t <= deckel; t++)
        {
            SimTickFuerProbe();
            if (!_forschung[platz].Belegt)
                return ForschungFertig + ErfindungFertig > start ? t : -t;   // −t: frei, aber nicht fertig
        }
        return -1;
    }

    public bool LaufsatzBelegt(int platz) => platz >= 0 && platz < ForschungPlaetze && _forschung[platz].Belegt;
    public int LaufsatzRest(int platz) => LaufsatzBelegt(platz) ? _forschung[platz].Preis - _forschung[platz].Fortschritt : -1;

    /// <summary>Stufe des Bauteils im Laufsatz <paramref name="platz"/> beim Spieler
    /// (fuer die Wirkung: nach dem Abschluss eins hoeher).</summary>
    public int LaufsatzBauteil(int platz) => LaufsatzBelegt(platz) && !_forschung[platz].Erfindung ? _forschung[platz].Werte.Bauteil : -1;

    /// <summary>Fuer <c>--spielstand-check</c>: eine laufende Forschung mit etwas Fortschritt,
    /// damit der Teil »Laufende Forschung« etwas zu verlieren hat (bug-666).</summary>
    public string SpielstandProbeForschung()
    {
        int me = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;
        int idx = ForschungProbeBasis(me, out string eingriff);
        if (idx < 0) return "Forschung: keine Basis";
        var sb = new StringBuilder("Forschung: " + eingriff);
        int platz = ForschungProbeKaufen(idx, 100, sb);
        if (platz < 0) return sb.ToString();
        // ein Stueck laufen lassen — nur der Forschungstakt, die Lage bleibt
        int lauf = Mathf.Min(37, _forschung[platz].Preis - 1);
        for (int t = 0; t < lauf; t++) ForschungTick();
        sb.Append($", {lauf} Takte gelaufen: {LaufsatzZeile(platz)}");
        return sb.ToString();
    }
}
