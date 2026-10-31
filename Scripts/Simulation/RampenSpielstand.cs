namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using GDict = Godot.Collections.Dictionary<string, Godot.Variant>;

/// <summary>
/// ⭐ 30.10.2026, bug-702 — <b>DIE RAMPEN (sec21) IM SPIELSTAND</b>, neben den Bruecken
/// (Simulation/BrueckenSpielstand.cs, bug-701). Bis heute ging <c>_moleSaetze</c> nicht in den
/// Stand: nach dem Laden stand jede Kartenrampe heil wieder da (TP 200, lebend gezeichnet,
/// bug-685), eine vom Pionier gebaute Mole war weg; gemessen in berichte/bau-pionierbruecke.md §12.
///
/// <para><b>Geschrieben</b>: Schluessel <c>rampen</c> (Format bleibt) — je stehendem Satz Platz,
/// Art, TP, fuer die Mole dazu Zelle und Bild (<c>2·Richtung + Wurf</c>, schon gefallen). Eine
/// Kartenrampe, die fehlt, ist abgerissen; ihre Truemmerkachel <c>10747 + Bild</c> liegt in
/// <c>bauwerk_kacheln</c> (bug-701).</para>
/// <para><b>Gelesen</b> in <see cref="BrueckenAusSpielstand"/>, vor den Kacheln: fehlende
/// Kartenrampe → <c>RampeAbreissen(…, ausSpielstand)</c> (ohne Leeren der Zelle), TP uebernommen,
/// Mole neu gelegt wie der Bau in <c>MoleArbeitTick</c> (sec6 frei, sec20 = 200 + Platz,
/// Kachel), ohne Pionier, Klang, Wurf.</para>
/// <para>⚠ <b>Eigener Gegenschalter <c>--rampen-spielstand-alt</c></b> (begruendet im Bericht
/// §12): Rampen und Bruecken sind getrennte Tafeln mit getrennten Fehlern; so zeigt das
/// Nullmodell nur den Rampenverlust und das von bug-701 bleibt, wie es gemessen wurde.
/// <c>--bruecken-spielstand-alt</c> schaltet weiter ALLES ab, was bug-701 schreibt — auch die
/// Rampen (der ganze Abschnitt wird dann nicht geschrieben).</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--rampen-spielstand-alt</c> — Nullmodell zu bug-702.</summary>
    public static bool RampenSpielstandAlt;

    /// <summary>Die Zellen aller Kartenrampen, wie die Karte sie geladen hat.</summary>
    private readonly List<int> _kartenRampenGeladen = new();

    /// <summary>Aus <c>LiesBruecken</c>, nach sec21.</summary>
    private void KartenRampenMerken()
    {
        _kartenRampenGeladen.Clear();
        foreach (var r in _moleSaetze) if (r.Karte) _kartenRampenGeladen.Add(r.Col * 1024 + r.Row);
    }

    private void RampenSchreiben(Core.SaveGame.Writer w)
    {
        if (RampenSpielstandAlt) return;
        w.ArrayStart("rampen");
        foreach (var r in _moleSaetze.OrderBy(x => x.Slot))
        {
            w.ItemStart();
            w.Num("slot", r.Slot).Bool("karte", r.Karte).Num("tp", r.Tp);
            if (!r.Karte) w.Num("col", r.Col).Num("row", r.Row).Num("bild", r.Bild);
            w.ItemEnd();
        }
        w.ArrayEnd();
    }

    private void RampenAusSpielstand(GDict root)
    {
        if (RampenSpielstandAlt || !root.TryGetValue("rampen", out var rv) || rv.VariantType != Variant.Type.Array) return;
        var karte = new Dictionary<int, int>();
        var mole = new List<GDict>();
        foreach (var item in rv.AsGodotArray())
        {
            if (item.VariantType != Variant.Type.Dictionary) continue;
            var d = item.AsGodotDictionary<string, Variant>();
            if (d.TryGetValue("karte", out var kv) && kv.AsBool()) karte[GetI(d, "slot", -1)] = GetI(d, "tp", 200);
            else mole.Add(d);
        }
        int weg = 0, tp = 0, gelegt = 0;
        foreach (var r in _moleSaetze.FindAll(x => x.Karte))
        {
            if (!karte.TryGetValue(r.Slot, out int t)) { RampeAbreissen(r, ausSpielstand: true); weg++; continue; }
            if (t != r.Tp) { r.Tp = t; tp++; }
        }
        foreach (var d in mole)
        {
            int slot = GetI(d, "slot", -1), col = GetI(d, "col", -1), row = GetI(d, "row", -1);
            if (slot is < 0 or >= RampenPlaetze || _moleSaetze.Exists(x => x.Slot == slot) || RampeAn(col, row) != null) continue;
            var r = new Rampe { Col = col, Row = row, Bild = GetI(d, "bild"), Tp = GetI(d, "tp", 200), Slot = slot };
            _moleSaetze.Add(r);
            _nav?.RampeSetzen(col, row, true);
            int key = col * 1024 + row;
            _rampen[key] = 200 + slot;
            _bauwerkKachel.Remove(key);
            _rampenKachel[key] = 10723 + r.Bild;
            gelegt++;
        }
        GD.Print($"rampen: aus dem Spielstand {karte.Count} Kartenrampen ({weg} abgerissen nachgezogen, {tp} mit anderen TP), {gelegt} Molen gelegt");
    }

    /// <summary>Fuer <c>--spielstand-check</c>: eine Kartenrampe abreissen, eine beschaedigen,
    /// und ⚠ EINGRIFF eine Mole auf dem echten Bauweg (<c>MoleArbeitTick</c>, ein Fusssoldat
    /// spielt den Pionier wie in <c>--bruecke-baustelle-check</c>).</summary>
    private string SpielstandProbeRampen()
    {
        var sb = new StringBuilder("Rampen:");
        var karte = _moleSaetze.FindAll(r => r.Karte);
        if (karte.Count > 0) { sb.Append($" Kartenrampe Platz {karte[0].Slot} ({karte[0].Col},{karte[0].Row}) abgerissen;"); RampeAbreissen(karte[0]); }
        if (karte.Count > 1) { karte[1].Tp = 120; sb.Append($" Kartenrampe Platz {karte[1].Slot} auf TP 120;"); }
        if (_nav == null) return sb.ToString();
        Vector2I platz = new(-1, -1);
        for (int r = 0; r < _nav.Height && platz.X < 0; r++)
            for (int c = 0; c < _nav.Width && platz.X < 0; c++)
                if (MolePlatzOk(c, r)) platz = new Vector2I(c, r);
        int p = -1;
        for (int i = 0; i < _entities.Count && p < 0; i++)
        {
            var o = _entities[i];
            if (!o.Dead && !o.IsBuilding && !o.IsProp && o.Infantry >= 0 && o.Owner == ViewPlayer && !Untergestellt(o)) p = i;
        }
        if (platz.X < 0 || p < 0) return sb.Append(" keine Stelle/kein Soldat fuer eine Mole").ToString();
        var e = _entities[p];
        int vor = MoleGebaut;
        P3Versetze(p, platz);
        e.Bauart = 2; e.BauZelle = platz; e.BauTakte = 0;
        for (int t = 0; t < 400 && e.Bauart != 0 && !e.Dead; t++) MoleArbeitTick(p, e);
        if (MoleGebaut > vor && RampeAn(platz.X, platz.Y) is { } neu)
        {
            neu.Tp = 150;
            sb.Append($" EINGRIFF Mole ({platz.X},{platz.Y}) Platz {neu.Slot} gebaut, TP 150");
        }
        else sb.Append($" Mole bei ({platz.X},{platz.Y}) NICHT gebaut ({MoleNote})");
        return sb.ToString();
    }

    /// <summary>Zwei Teile: die Saetze, und Suchkarte/Lage/Kacheln ueber alle Kartenrampenzellen
    /// (auch abgerissener) und alle stehenden Molen.</summary>
    private string RampenFingerabdruck()
    {
        var saetze = new StringBuilder();
        foreach (var r in _moleSaetze.OrderBy(x => x.Slot))
            saetze.Append($" {r.Slot}{(r.Karte ? "K" : "M")}:{r.Tp}" + (r.Karte ? "" : $"@{r.Col},{r.Row}/{r.Bild}"));
        var zellen = new SortedSet<int>(_kartenRampenGeladen);
        foreach (var r in _moleSaetze) zellen.Add(r.Col * 1024 + r.Row);
        ulong h = 14695981039346656037UL;
        void H(int v) { h ^= (uint)v; h *= 1099511628211UL; }
        foreach (int key in zellen)
        {
            int c = key / 1024, r = key % 1024;
            H(key); H((int)(_nav?.GroundAt(c, r) ?? Simulation.NavGrid.Ground.Blocked));
            H(_rampen.TryGetValue(key, out int l) ? l : -1);
            H(_rampenKachel.TryGetValue(key, out int rk) ? rk : -1);
            H(_bauwerkKachel.TryGetValue(key, out int bk) ? bk : -1);
        }
        return $"Rampen{(saetze.Length > 0 ? saetze.ToString() : " keine")}" +
               $" | Rampen Suchkarte/Lage/Kacheln {zellen.Count} Zellen {h:X16}";
    }
}
