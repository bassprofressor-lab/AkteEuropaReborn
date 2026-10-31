namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using GDict = Godot.Collections.Dictionary<string, Godot.Variant>;

/// <summary>
/// ⭐ 30.10.2026, bug-701 — <b>DIE BRUECKEN IM SPIELSTAND.</b> Bis heute schrieb
/// <c>SaveStateJson</c> die gemeinsame Brueckentafel <c>_stege</c> (sec17) gar nicht. Beim Laden
/// entsteht die Karte frisch (<c>LiesBruecken</c>), also: jede Kartenbruecke heil, keine
/// Pionierbruecke. Was verloren ging, misst <c>--spielstand-check</c> jetzt selbst (Teile
/// »Bruecken …« im Fingerabdruck, Probe in <see cref="SpielstandProbeBruecken"/>).
///
/// <para><b>Geschrieben</b> (zusaetzliche Schluessel, Format bleibt wie bei bug-683):</para>
/// <list type="bullet">
/// <item><c>bruecken</c>: je stehendem Satz Platz, Art (Karte/Pionier), TP, und fuer die
///   Pionierbruecke Kopfzelle, Richtung, Weite, Bildvariante. Eine Kartenbruecke, die fehlt,
///   ist abgerissen.</item>
/// <item><c>bauwerk_kacheln</c>/<c>bauwerk_ueberdeckt</c>: die Truemmer- und Wasserkacheln der
///   Abrisse (ihr Wurf ist schon gefallen — beim Laden wird NICHT neu gewuerfelt) und die
///   Zellen, die nicht mehr aus dem Bodenbild kommen.</item>
/// </list>
/// <para><b>Gelesen</b> vor den Einheiten: fehlende Kartenbruecken werden mit dem Abriss
/// <c>0x4CB0A0</c> nachgezogen (ohne Licht, ohne Leeren der Fahrbahn, ohne Wuerfel), TP
/// uebernommen, Pionierbruecken auf ihren Platz neu gelegt (Suchkarte, sec20, Anhebung bug-695;
/// ohne Erschlagen, Licht, Klang). Hoehen, Byte +3 und Suchkarte folgen daraus — sie werden
/// nicht eigens gespeichert. Danach die Kacheln aus dem Stand.</para>
/// <para>⚠ <b>UNSERE SETZUNG / NICHT gelesen:</b> wie das Original sec17 speichert (VERMUTET
/// als ganzer Block wie die anderen Sektionen). Rampen (sec21) gehen weiter NICHT in den Stand;
/// darum werden Kacheln auf den Zellen stehender Rampen nicht zurueckgelegt.</para>
/// <para>Gegenschalter <c>--bruecken-spielstand-alt</c>: weder schreiben noch lesen (der
/// Fingerabdruck zeigt die Bruecken trotzdem — so meldet <c>--spielstand-check</c> den Verlust).</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--bruecken-spielstand-alt</c> — Nullmodell zu bug-701.</summary>
    public static bool BrueckenSpielstandAlt;

    /// <summary>Alle Kartenbruecken, wie die Karte sie geladen hat — auch nach dem Abriss
    /// (fuer den Fingerabdruck ueber ihre Zellen).</summary>
    private readonly List<Steg> _kartenStegeGeladen = new();

    /// <summary>Wie viele Saetze das letzte Laden gesetzt hat (−1 = Stand ohne Bruecken).</summary>
    public static int BrueckenGeladen = -1;

    /// <summary>Aus <c>SaveStateJson</c>.</summary>
    private void BrueckenSchreiben(Core.SaveGame.Writer w)
    {
        if (BrueckenSpielstandAlt) return;
        w.ArrayStart("bruecken");
        foreach (var s in _stege.OrderBy(x => x.Slot))
        {
            w.ItemStart();
            w.Num("slot", s.Slot).Bool("karte", s.Karte).Num("tp", s.Tp);
            if (!s.Karte)
                w.Num("col", s.Col).Num("row", s.Row).Num("richtung", s.Richtung)
                 .Num("wasser", s.Wasser).Num("variante", s.Variante);
            w.ItemEnd();
        }
        w.ArrayEnd();
        RampenSchreiben(w);                       // bug-702
        w.Raw("bauwerk_kacheln", "[" + string.Join(",", _bauwerkKachel.OrderBy(k => k.Key).Select(k => $"[{k.Key},{k.Value}]")) + "]");
        w.Raw("bauwerk_ueberdeckt", "[" + string.Join(",", _bauwerkUeberdeckt.OrderBy(k => k)) + "]");
    }

    /// <summary>Aus <c>ApplySaveState</c>, VOR den Einheiten (die frische Karte steht).</summary>
    private void BrueckenAusSpielstand(GDict root)
    {
        BrueckenGeladen = -1;
        if (BrueckenSpielstandAlt || _nav == null || !root.TryGetValue("bruecken", out var bv)
            || bv.VariantType != Variant.Type.Array) return;
        BrueckenGeladen = 0;
        var karte = new Dictionary<int, int>();
        var pionier = new List<GDict>();
        foreach (var item in bv.AsGodotArray())
        {
            if (item.VariantType != Variant.Type.Dictionary) continue;
            var d = item.AsGodotDictionary<string, Variant>();
            if (d.TryGetValue("karte", out var kv) && kv.AsBool()) karte[GetI(d, "slot", -1)] = GetI(d, "tp", BrueckeTp);
            else pionier.Add(d);
        }
        int abgerissen = 0, beschaedigt = 0, gelegt = 0;
        // 1) Kartenbruecken: fehlt der Satz, ist sie abgerissen; sonst die TP.
        foreach (var st in _stege.FindAll(s => s.Karte))
        {
            if (!karte.TryGetValue(st.Slot, out int tp)) { BrueckeAbreissen(st, ausSpielstand: true); abgerissen++; continue; }
            if (tp == st.Tp) continue;
            st.Tp = tp;
            beschaedigt++;
            if (tp < BrueckeTp) foreach (var (z, _, _) in st.Zellen) _bauwerkUeberdeckt.Add(z.X * 1024 + z.Y);
        }
        // 2) Pionierbruecken: auf ihren Platz neu legen.
        foreach (var d in pionier)
        {
            int slot = GetI(d, "slot", -1), col = GetI(d, "col", -1), row = GetI(d, "row", -1);
            int dir = GetI(d, "richtung", -1), k = GetI(d, "wasser", -1);
            if (slot is < 0 or >= StegPlaetze || dir is < 0 or > 3 || k is < 0 or > BrueckeMaxWasser
                || _stege.Exists(x => x.Slot == slot)) continue;
            var s = new Steg
            {
                Col = col, Row = row, Richtung = dir, Wasser = k, Variante = GetI(d, "variante"),
                Tp = GetI(d, "tp", BrueckeTp), Slot = slot,
            };
            _stege.Add(s);
            foreach (var (z, mitte) in BrueckenZellen(col, row, dir, k))
            {
                var alt = _nav.BrueckeSetzen(z.X, z.Y, mitte);
                s.Zellen.Add((z, mitte, alt));
                _rampen[z.X * 1024 + z.Y] = 100 + slot;
            }
            PionierbrueckeAnheben(s);
            gelegt++;
        }
        if (abgerissen + gelegt > 0) KiBrueckeGeaendert("Bruecken aus dem Spielstand");
        // 2b) ⭐ bug-702: die Rampen (sec21) — VOR den Kacheln, denn stehende Rampen bekommen keine.
        RampenAusSpielstand(root);
        // 3) Die Kacheln der Abrisse — nicht auf stehenden Rampen (die gehen nicht in den Stand).
        var rampen = new HashSet<int>(_moleSaetze.Select(r => r.Col * 1024 + r.Row));
        int kacheln = 0;
        if (root.TryGetValue("bauwerk_kacheln", out var kk) && kk.VariantType == Variant.Type.Array)
            foreach (var pair in kk.AsGodotArray())
            {
                if (pair.VariantType != Variant.Type.Array) continue;
                var pa = pair.AsGodotArray();
                if (pa.Count < 2) continue;
                int key = pa[0].AsInt32();
                if (rampen.Contains(key)) continue;
                _bauwerkKachel[key] = pa[1].AsInt32();
                kacheln++;
            }
        if (root.TryGetValue("bauwerk_ueberdeckt", out var uv) && uv.VariantType == Variant.Type.Array)
            foreach (var q in uv.AsGodotArray()) { int key = q.AsInt32(); if (!rampen.Contains(key)) _bauwerkUeberdeckt.Add(key); }
        BrueckenGeladen = karte.Count + gelegt;
        GD.Print($"bruecken: aus dem Spielstand {karte.Count} Kartenbruecken ({abgerissen} abgerissen nachgezogen, "
               + $"{beschaedigt} mit anderen TP), {gelegt} Pionierbruecken gelegt, {kacheln} Bauwerkskacheln");
        QueueRedraw();
    }

    /// <summary>Fuer <c>--spielstand-check</c>: eine Kartenbruecke abreissen, eine zweite
    /// beschaedigen, eine Pionierbruecke bauen und beschaedigen — was es auf der Karte gibt.</summary>
    private string SpielstandProbeBruecken()
    {
        var sb = new StringBuilder("Bruecken:");
        var karte = _stege.FindAll(s => s.Karte);
        if (karte.Count > 0) { BrueckeAbreissen(karte[0]); sb.Append($" Kartenbruecke Platz {karte[0].Slot} abgerissen;"); }
        if (karte.Count > 1)
        {
            var st = karte[1];
            st.Tp = 300;
            foreach (var (z, _, _) in st.Zellen) _bauwerkUeberdeckt.Add(z.X * 1024 + z.Y);
            sb.Append($" Kartenbruecke Platz {st.Slot} auf TP 300;");
        }
        if (_nav != null)
        {
            Vector2I platz = new(-1, -1);
            for (int r = 0; r < _nav.Height && platz.X < 0; r++)
                for (int c = 0; c < _nav.Width && platz.X < 0; c++)
                    if (BrueckePlatzOk(c, r)) platz = new Vector2I(c, r);
            if (platz.X >= 0 && BrueckeAnlegen(-1, platz.X, platz.Y))
            {
                var s = _stege[^1];
                s.Tp = 333;
                sb.Append($" EINGRIFF Pionierbruecke ({platz.X},{platz.Y}) Platz {s.Slot} gebaut, TP 333");
            }
            else sb.Append(" keine Stelle fuer eine Pionierbruecke");
        }
        return sb.ToString();
    }

    /// <summary>Der Fingerabdruck der Bruecken — drei Teile, damit der Vergleich nennt, WAS fehlt:
    /// die Saetze, die Hoehen/Bytes und die Suchkarte/Lage/Kacheln ueber alle Zellen aller
    /// Kartenbruecken (auch abgerissener) und aller stehenden Pionierbruecken.</summary>
    private string BrueckenFingerabdruck()
    {
        var saetze = new StringBuilder();
        foreach (var s in _stege.OrderBy(x => x.Slot))
            saetze.Append($" {s.Slot}{(s.Karte ? "K" : "P")}:{s.Tp}" + (s.Karte ? "" : $"@{s.Col},{s.Row}/{s.Richtung}/{s.Wasser}"));
        var zellen = new SortedSet<int>();
        foreach (var st in _kartenStegeGeladen) foreach (var (z, _, _) in st.Zellen) zellen.Add(z.X * 1024 + z.Y);
        foreach (var st in _stege) if (!st.Karte) foreach (var (z, _, _) in st.Zellen) zellen.Add(z.X * 1024 + z.Y);
        ulong hoehe = 14695981039346656037UL, such = 14695981039346656037UL;
        void H(ref ulong h, int v) { h ^= (uint)v; h *= 1099511628211UL; }
        foreach (int key in zellen)
        {
            int c = key / 1024, r = key % 1024;
            H(ref hoehe, key); H(ref hoehe, ElevOf(c, r)); H(ref hoehe, _nav?.ElevAt(c, r) ?? -1);
            H(ref hoehe, NebelhoeheFuerProbe(c, r)); H(ref hoehe, _nav?.FlagAt(c, r) ?? -1); H(ref hoehe, HangArt(c, r));
            H(ref such, key); H(ref such, (int)(_nav?.GroundAt(c, r) ?? Simulation.NavGrid.Ground.Blocked));
            H(ref such, _rampen.TryGetValue(key, out int l) ? l : -1);
            H(ref such, _bauwerkKachel.TryGetValue(key, out int kc) ? kc : -1);
            H(ref such, _bauwerkUeberdeckt.Contains(key) ? 1 : 0);
        }
        return $"Bruecken{(saetze.Length > 0 ? saetze.ToString() : " keine")}" +
               $" | Bruecken Hoehen/Byte {zellen.Count} Zellen {hoehe:X16}" +
               $" | Bruecken Suchkarte/Lage/Kacheln {such:X16}";
    }
}
