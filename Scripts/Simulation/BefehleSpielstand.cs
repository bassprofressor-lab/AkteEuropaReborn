namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Text;
using Godot;
using GDict = Godot.Collections.Dictionary<string, Godot.Variant>;

/// <summary>
/// ⭐⭐ 29.10.2026, bug-650 (KayelGee R8 K8, »Nach dem Laden verlieren Einheiten ihre Befehle,
/// auch Transporter«) — <b>SPIELSTAND FORMAT 4: DER AUFTRAG GEHT MIT</b>. Lesung:
/// <c>berichte/laden-befehle-r8-opus.md</c> (Feldliste §1.3, Bauvorschlag); Bau:
/// <c>berichte/bau-p2-0-6-12.md</c>.
///
/// <para><b>Original:</b> der Speicherer C <c>0x41D210</c> (F <c>0x41C3D0</c>) schreibt sec5
/// GANZ (8000 × 78 B, mit UKOL <c>+0x14</c>, CX/CY <c>+0x18/+0x19</c>, POHYB <c>+0x04</c>,
/// UTOK_NA <c>+0x36</c>, <c>+0x38</c>, <c>+0x40</c> …), dazu den <b>Wegpuffer sec15</b>
/// (C <c>0x7AEC38</c>, 8000 × 50 B; Schreibruf <c>0x41D451</c>, Leseruf <c>0x41E4AB</c>) und
/// sec48 <c>rob_trans</c> (400 × 18 B). Der Nachlauf des Laders (<c>0x41F318…0x41F37B</c>)
/// setzt keinen Auftrag zurueck. Gemessen: in 1.DM fahren 85 von 127 Einheiten, 19 von 20
/// Routen sind gestartet; auf 41 Kartenstarts 0 von 2435.</para>
///
/// <para><b>Bei uns bis 0.6.12:</b> Weg, Ziel, Angriffsziel, Route und alle Sonderauftraege
/// fehlten in der Datei (eine Entscheidung von 0.4.0, <c>bb183b0</c>, aelter als die Lesung
/// der Ladertafel). UKOL ging mit, die Felder dazu nicht — darum blieb ein GEBAUTER
/// Transporter im Werk (UKOL 54) nach dem Laden fuer immer unsichtbar (kein Satz, kein
/// Routentakt, <c>Transportroute.cs:428</c>).</para>
///
/// <para><b>Jetzt (Spielerentscheidung 29.10. Nr. 6):</b> der Weg wird WOERTLICH gespeichert
/// (Zellenliste und Wegzeiger, wie sec15 — nicht neu gesucht), dazu das Ziel, der Angriff
/// (Ziele als Stelle im Spielstand, wie die Gruppen), unsere eigene Befehlsschlange
/// (mitgespeichert, obwohl sie keine Entsprechung im Original hat), die Materialroute und
/// die Sonderauftraege der Feldliste. Ein Stand OHNE die Marke <c>"befehle"</c> (Format 1..3)
/// laedt wie bisher; nur die Routensaetze werden auch dort an die Einheiten gehaengt und
/// jeder Transporter ohne Satz bekommt einen (0x436190 — das Original legt ihn beim
/// Erzeugen an), das holt den unsichtbaren Wagen alter Staende zurueck.</para>
///
/// <para>⚠ <b>UNSERE SETZUNGEN:</b> (1) ein halber Schritt (<c>Progress</c>, <c>StepFrom</c>)
/// geht nicht mit, die Einheit beginnt den Schritt von der Zellmitte neu; (2) passt die
/// naechste Wegzelle nicht mehr an die Einheit (nicht benachbart), wird der Weg verworfen
/// und einmal zum Ziel neu gesucht (<c>RetryIn</c> = 1) — mit unveraendertem Raster kommt
/// das nicht vor; (3) Zaehler, die nur unser Zeichner oder unsere Bremsen fuehren
/// (<c>HatteWeg</c>, <c>Halten</c>, Arbeitszaehler der Werkstatt) bleiben draussen.</para>
///
/// <para>Gegenschalter <c>--laden-befehle-alt</c>: nichts davon wird geschrieben oder
/// gelesen, der Leser ist der von 0.6.11. Pruefstand <c>--befehle-laden-check</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--laden-befehle-alt</c> — bug-650: Weg, Ziel, Angriff, Schlange, Route
    /// und Sonderauftraege gehen nicht in den Spielstand (Stand 0.6.11).</summary>
    public static bool LadenBefehleAlt;

    private static string ZelleText(Vector2I v) => $"[{v.X},{v.Y}]";

    /// <summary>Eine Fliesskommazahl verlustfrei (»R«) — Writer.Num rundet auf 0,001,
    /// und ein gerundeter Nachladezaehler waere nach dem Laden ein anderer.</summary>
    private static string Genau(float f) => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    private static string ZellenText(List<Vector2I> l)
    {
        var sb = new StringBuilder("[");
        for (int i = 0; i < l.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(l[i].X).Append(',').Append(l[i].Y);
        }
        return sb.Append(']').ToString();
    }

    /// <summary>Die Materialroute (sec48-Satz) als Zahlenreihe — neunzehn Felder.</summary>
    private void RouteSchreiben(Core.SaveGame.Writer w, Entity e)
    {
        if (LadenBefehleAlt || e.Route is not Transportroute r) return;
        w.Raw("route", $"[{r.Quelle[0]},{r.Quelle[1]},{r.Quelle[2]},{r.Quelle[3]},{r.Ziel}," +
                       $"{r.Geladen[0]},{r.Geladen[1]},{r.Geladen[2]},{r.Geladen[3]},{r.Gesamt},{r.Fassung}," +
                       $"{r.Einheit},{(r.Gestartet ? 1 : 0)},{r.Fahrziel},{r.Reihum},{r.Warten}," +
                       $"{r.GeladenGesamt},{r.AbgeladenGesamt},{r.Ruhe}]");
    }

    private static Transportroute? RouteLesen(GDict d)
    {
        if (!d.TryGetValue("route", out var v) || v.VariantType != Variant.Type.Array) return null;
        var a = v.AsGodotArray();
        if (a.Count < 19) return null;
        int I(int k) => a[k].AsInt32();
        var r = new Transportroute();
        for (int k = 0; k < 4; k++) { r.Quelle[k] = I(k); r.Geladen[k] = I(5 + k); }
        r.Ziel = I(4); r.Gesamt = I(9); r.Fassung = I(10); r.Einheit = I(11); r.Gestartet = I(12) != 0;
        r.Fahrziel = I(13); r.Reihum = I(14); r.Warten = I(15);
        r.GeladenGesamt = I(16); r.AbgeladenGesamt = I(17); r.Ruhe = I(18);
        return r;
    }

    /// <summary>
    /// Die Befehlsfelder eines Satzes in <c>entities</c>. <paramref name="stelle"/> rechnet
    /// eine Listenstelle im Spiel in die Stelle im Spielstand um (−1 = nicht geschrieben).
    /// Was auf dem Vorgabewert steht, fehlt in der Datei — der Leser setzt dann den
    /// Vorgabewert (nicht den Kartenwert der Vorlage!), siehe <see cref="BefehleLesen"/>.
    /// </summary>
    private void BefehleSchreiben(Core.SaveGame.Writer w, Entity e, System.Func<int, int> stelle)
    {
        if (LadenBefehleAlt) return;
        int St(int idx) => idx >= 0 && idx < _entities.Count ? stelle(idx) : -1;
        if (!e.IsBuilding)
        {
            // Bewegung — CX/CY +0x18/+0x19, sec15 und der Wegzeiger +0x1A, CEKANI +0x1C
            w.Raw("goal", ZelleText(e.Goal));
            w.Num("block", e.Block);
            if (e.WaitTime != 0f) w.Raw("wait_time", Genau(e.WaitTime));
            if (e.Path != null) { w.Raw("path", ZellenText(e.Path)); w.Num("path_idx", e.PathIdx); }
            if (e.SpritWeg != null) { w.Raw("sprit_weg", ZellenText(e.SpritWeg)); w.Num("sprit_idx", e.SpritIdx); }
            if (e.RetryIn != 0) w.Num("retry_in", e.RetryIn);
            if (e.ChaseRetry != 0) w.Num("chase_retry", e.ChaseRetry);
            // UNSERE Befehlsschlange — je Eintrag Spalte, Zeile, Ziel (−1 Fahrt, −2 Ziel verloren)
            if (e.Orders.Count > 0)
            {
                var sb = new StringBuilder("[");
                for (int k = 0; k < e.Orders.Count; k++)
                {
                    var o = e.Orders[k];
                    int t = o.Target < 0 ? -1 : St(o.Target) is var s && s >= 0 ? s : -2;
                    if (k > 0) sb.Append(',');
                    sb.Append(o.Cell.X).Append(',').Append(o.Cell.Y).Append(',').Append(t);
                }
                w.Raw("orders", sb.Append(']').ToString());
            }
            // Sonderauftraege der Feldliste (Lesung §1.3)
            if (e.EinsteigTraeger != -1) w.Num("einsteig_traeger", e.EinsteigTraeger);      // +0x36, Befehl 17 (Platz)
            if (!e.UnloadVonRampe) w.Bool("unload_von_rampe", false);
            if (e.Bauart != 0 || e.BauZelle != new Vector2I(-1, -1) || e.BauTakte != 0 || e.BauAnlauf != 0)
            {
                w.Num("bauart", e.Bauart).Raw("bau_zelle", ZelleText(e.BauZelle))            // +0x40, Befehl 16
                 .Num("bau_takte", e.BauTakte).Num("bau_anlauf", e.BauAnlauf);
            }
            if (e.BuildOrder != 0 || e.BuildTarget != 0)
                w.Num("build_order", e.BuildOrder).Num("build_target", e.BuildTarget);       // +0x38/+0x40, Befehl 20
            if (e.RailGoal is { } rg) w.Raw("rail_goal", ZelleText(rg));                     // +0x48/+0x49
            if (e.RailWork != 0) w.Num("rail_work", e.RailWork);                             // +0x38
            if (e.LeavingDock >= 0) w.Num("leaving_dock", St(e.LeavingDock));                // Auftrag 52, +0x15
            if (e.AuslaufZellen != 0 || e.AuslaufV != 0 || e.AuslaufRichtung != 0 || e.AuslaufEnde != default)
                w.Num("auslauf_zellen", e.AuslaufZellen).Num("auslauf_v", e.AuslaufV)        // Auftrag 49
                 .Num("auslauf_richtung", e.AuslaufRichtung).Raw("auslauf_ende", ZelleText(e.AuslaufEnde));
            if (e.AusweichRichtung != 0) w.Num("ausweich_richtung", e.AusweichRichtung);    // AKCE +0x15
        }
        // Angriff — UTOK_NA +0x36 (auch Gebaeude mit Waffe)
        if (e.Target >= 0) w.Num("target", St(e.Target));
        if (e.Ordered) w.Bool("ordered", true);
        if (e.AngriffsZelle is { } az) w.Raw("angriffs_zelle", ZelleText(az));               // UTOK_NA-Band 30000+
        if (e.TurmHalteZiel >= 0) w.Num("turm_halte_ziel", St(e.TurmHalteZiel));
        // bug-645 (29.10.2026): Schussziel STRILI_NA +0x34 und Restauftrag (Gelegenheitsfeuer.cs)
        if (e.Schussziel >= 0) w.Num("schussziel", St(e.Schussziel));
        if (e.RestZiel is { } rz) w.Raw("rest_ziel", ZelleText(rz)).Raw("rest_goal", ZelleText(e.RestGoal));
        if (e.SchussZelle is { } sz) w.Raw("schuss_zelle", ZelleText(sz)).Num("schuss_zelle_fuer", St(e.SchussZelleFuer));
        if (e.GebaeudeFahrzelle is { } gf) w.Raw("gebaeude_fahrzelle", ZelleText(gf)).Num("gebaeude_fahrt_fuer", St(e.GebaeudeFahrtFuer));
        if (e.Cooldown != 0f) w.Raw("cooldown", Genau(e.Cooldown));
        if (e.IsBuilding && e.Patrouille) w.Bool("patrouille", true);                       // Flughafen +0x43
    }

    private static Vector2I? ZelleLesen(GDict d, string k)
    {
        if (!d.TryGetValue(k, out var v) || v.VariantType != Variant.Type.Array) return null;
        var a = v.AsGodotArray();
        return a.Count >= 2 ? new Vector2I(a[0].AsInt32(), a[1].AsInt32()) : null;
    }

    private static List<Vector2I>? ZellenLesen(GDict d, string k)
    {
        if (!d.TryGetValue(k, out var v) || v.VariantType != Variant.Type.Array) return null;
        var a = v.AsGodotArray();
        var l = new List<Vector2I>(a.Count / 2);
        for (int i = 0; i + 1 < a.Count; i += 2) l.Add(new Vector2I(a[i].AsInt32(), a[i + 1].AsInt32()));
        return l;
    }

    /// <summary>Wie viele Wege beim letzten Laden woertlich zurueckkamen bzw. verworfen
    /// wurden (Setzung 2 im Kopf) — fuer den Pruefstand.</summary>
    public static int BefehleWegeGeladen, BefehleWegeVerworfen;

    /// <summary>
    /// Die Befehlsfelder zurueck — NACH <c>InitEntityMovement</c>, das <c>Path</c> nullt.
    /// Nur ein Stand mit der Marke <c>"befehle"</c> (Format 4); bei ihm heisst ein
    /// fehlender Schluessel »Vorgabewert«.
    /// </summary>
    private void BefehleLesen(GDict root, List<int> listenstelle)
    {
        BefehleWegeGeladen = BefehleWegeVerworfen = 0;
        if (LadenBefehleAlt || !root.ContainsKey("befehle")) return;
        if (!root.TryGetValue("entities", out var ev) || ev.VariantType != Variant.Type.Array) return;
        var arr = ev.AsGodotArray();
        for (int stelle = 0; stelle < arr.Count && stelle < listenstelle.Count; stelle++)
        {
            if (arr[stelle].VariantType != Variant.Type.Dictionary) continue;
            var d = arr[stelle].AsGodotDictionary<string, Variant>();
            var e = _entities[listenstelle[stelle]];
            int I(string k, int dflt) => d.TryGetValue(k, out var x) ? x.AsInt32() : dflt;
            float F(string k) => d.TryGetValue(k, out var x) ? (float)x.AsDouble() : 0f;
            int L(string k) { int s = I(k, -1); return s >= 0 && s < listenstelle.Count ? listenstelle[s] : -1; }
            if (!e.IsBuilding)
            {
                if (ZelleLesen(d, "goal") is { } g) e.Goal = g;
                e.Block = I("block", e.Block);
                e.WaitTime = F("wait_time");
                e.Path = ZellenLesen(d, "path");
                e.PathIdx = I("path_idx", 0);
                e.RetryIn = I("retry_in", 0);
                if (e.Path != null)
                {
                    // Setzung 2: die naechste Zelle muss an der Einheit liegen — sonst
                    // einmal zum Ziel neu suchen (RetryPath im naechsten Takt).
                    if (e.PathIdx < e.Path.Count
                        && (Mathf.Abs(e.Path[e.PathIdx].X - e.Col) > 1 || Mathf.Abs(e.Path[e.PathIdx].Y - e.Row) > 1))
                    { e.Path = null; e.PathIdx = 0; e.RetryIn = 1; BefehleWegeVerworfen++; }
                    else BefehleWegeGeladen++;
                }
                e.SpritWeg = ZellenLesen(d, "sprit_weg");
                e.SpritIdx = I("sprit_idx", 0);
                e.ChaseRetry = I("chase_retry", 0);
                e.Orders.Clear();
                if (d.TryGetValue("orders", out var ov) && ov.VariantType == Variant.Type.Array)
                {
                    var a = ov.AsGodotArray();
                    for (int k = 0; k + 2 < a.Count; k += 3)
                    {
                        var c = new Vector2I(a[k].AsInt32(), a[k + 1].AsInt32());
                        int t = a[k + 2].AsInt32();
                        if (t == -1) e.Orders.Add(Order.Move(c));
                        else if (t >= 0 && t < listenstelle.Count) e.Orders.Add(Order.Attack(c, listenstelle[t]));
                    }
                }
                e.EinsteigTraeger = I("einsteig_traeger", -1);
                e.UnloadVonRampe = !d.TryGetValue("unload_von_rampe", out var uv) || uv.AsBool();
                e.Bauart = I("bauart", 0);
                e.BauZelle = ZelleLesen(d, "bau_zelle") ?? new Vector2I(-1, -1);
                e.BauTakte = I("bau_takte", 0); e.BauAnlauf = I("bau_anlauf", 0);
                e.BuildOrder = I("build_order", 0); e.BuildTarget = I("build_target", 0);
                e.RailGoal = ZelleLesen(d, "rail_goal");
                e.RailWork = I("rail_work", 0);
                e.LeavingDock = d.ContainsKey("leaving_dock") ? L("leaving_dock") : -1;
                e.AuslaufZellen = I("auslauf_zellen", 0); e.AuslaufV = I("auslauf_v", 0);
                e.AuslaufRichtung = I("auslauf_richtung", 0);
                e.AuslaufEnde = ZelleLesen(d, "auslauf_ende") ?? default;
                e.AusweichRichtung = I("ausweich_richtung", 0);
            }
            e.Target = L("target");
            e.Ordered = d.TryGetValue("ordered", out var od) && od.AsBool();
            e.AngriffsZelle = ZelleLesen(d, "angriffs_zelle");
            e.TurmHalteZiel = L("turm_halte_ziel");
            e.Schussziel = L("schussziel");
            e.RestZiel = ZelleLesen(d, "rest_ziel");
            e.RestGoal = ZelleLesen(d, "rest_goal") ?? default;
            e.SchussZelle = ZelleLesen(d, "schuss_zelle");
            e.SchussZelleFuer = e.SchussZelle != null ? L("schuss_zelle_fuer") : -1;
            e.GebaeudeFahrzelle = ZelleLesen(d, "gebaeude_fahrzelle");
            e.GebaeudeFahrtFuer = e.GebaeudeFahrzelle != null ? L("gebaeude_fahrt_fuer") : -1;
            e.Cooldown = F("cooldown");
            if (e.IsBuilding) e.Patrouille = d.TryGetValue("patrouille", out var pv) && pv.AsBool();
        }
    }

    /// <summary>
    /// Die Routensaetze nach dem Laden: <c>_routen</c> neu aus den Einheiten (auch der
    /// Ladung an Bord) — die Waisen der Kartenvorlage fallen heraus und zaehlen nicht
    /// mehr gegen die 400 —, und jeder lebende Transporter ohne Satz bekommt einen
    /// (<c>0x436190</c>, das Original legt ihn beim Erzeugen an). Laeuft fuer JEDES Format;
    /// unter <c>--laden-befehle-alt</c> nicht.
    /// </summary>
    private void RoutenNachLaden()
    {
        if (LadenBefehleAlt) return;
        _routen.Clear();
        void Nimm(Entity e)
        {
            if (e.Route is Transportroute r && !_routen.Contains(r)) _routen.Add(r);
        }
        foreach (var e in _entities) if (!e.IsProp && !e.Dead) Nimm(e);
        foreach (var kv in _fracht) foreach (var u in kv.Value) Nimm(u);
        RoutenNachgelegt = 0;
        foreach (var e in _entities)
            if (!e.Dead && e.Route == null && IstTransporter(e) && RouteAnlegen(e) != null) RoutenNachgelegt++;
    }

    /// <summary>Wie viele Transporter beim letzten Laden einen frischen Satz bekamen.</summary>
    public static int RoutenNachgelegt;

    // ---- die Abdruecke fuer die Pruefstaende ------------------------------------

    /// <summary>Die Kennung einer Listenstelle, die das Laden uebersteht (Stellen
    /// verschieben sich: Requisiten bleiben stehen, der Rest wird neu angehaengt).</summary>
    private int Kennung(int idx)
        => idx >= 0 && idx < _entities.Count
            ? (_entities[idx].Slot + 2) * 131 + _entities[idx].Col * 7 + _entities[idx].Row * 13 + (_entities[idx].IsBuilding ? 5 : 0)
            : -1;

    /// <summary>Alle Befehlsfelder EINER Einheit als Zeile, Verweise als Kennung — der
    /// feldweise Vergleich von <c>--befehle-laden-check</c>.</summary>
    public string BefehlsFeldzeile(Entity e)
    {
        var sb = new StringBuilder();
        sb.Append($"Zelle ({e.Col},{e.Row}) UKOL {e.Ukol} Ziel ({e.Goal.X},{e.Goal.Y})");
        sb.Append(e.Path == null ? " Weg -" : $" Weg {e.PathIdx}/{e.Path.Count} [{ZellenText(e.Path)}]");
        if (e.SpritWeg != null) sb.Append($" Spritweg {e.SpritIdx}/{e.SpritWeg.Count}");
        sb.Append($" Retry {e.RetryIn}/{e.ChaseRetry} Geduld {e.Block}");
        sb.Append($" Angriff {Kennung(e.Target)}{(e.Ordered ? " befohlen" : "")}");
        if (e.AngriffsZelle is { } az) sb.Append($" Bodenziel ({az.X},{az.Y})");
        if (e.SchussZelle is { } sz) sb.Append($" Schusszelle ({sz.X},{sz.Y}) fuer {Kennung(e.SchussZelleFuer)}");
        if (e.GebaeudeFahrzelle is { } gf) sb.Append($" Fahrzelle ({gf.X},{gf.Y}) fuer {Kennung(e.GebaeudeFahrtFuer)}");
        if (e.TurmHalteZiel >= 0) sb.Append($" Turmziel {Kennung(e.TurmHalteZiel)}");
        if (e.Cooldown != 0f) sb.Append($" Nachladen {e.Cooldown:0.###}");
        if (e.Orders.Count > 0)
        {
            sb.Append(" Schlange [");
            foreach (var o in e.Orders) sb.Append($"({o.Cell.X},{o.Cell.Y},{Kennung(o.Target)})");
            sb.Append(']');
        }
        if (e.Route is Transportroute r)
            sb.Append($" Route Q[{string.Join(",", r.Quelle)}] Z {r.Ziel} {(r.Gestartet ? "gestartet" : "steht")} Fahrziel {r.Fahrziel} " +
                      $"Warten {r.Warten} Ladung {r.Gesamt}/{r.Fassung} Reihum {r.Reihum}");
        else if (IstTransporter(e)) sb.Append(" Route KEINE");
        if (e.EinsteigTraeger != -1) sb.Append($" Einsteigen {e.EinsteigTraeger}");
        if (!e.UnloadVonRampe) sb.Append(" Absetzen nicht von Rampe");
        if (e.Bauart != 0 || e.BauTakte != 0) sb.Append($" Bauart {e.Bauart} auf ({e.BauZelle.X},{e.BauZelle.Y}) Takte {e.BauTakte} Anlauf {e.BauAnlauf}");
        if (e.BuildOrder != 0) sb.Append($" Bauauftrag {e.BuildOrder}/{e.BuildTarget}");
        if (e.RailGoal is { } rg) sb.Append($" Gleis ({rg.X},{rg.Y}) Arbeit {e.RailWork}");
        if (e.LeavingDock >= 0) sb.Append($" Dock {Kennung(e.LeavingDock)}");
        if (e.AuslaufZellen != 0) sb.Append($" Auslauf {e.AuslaufZellen}/{e.AuslaufV}/{e.AuslaufRichtung} -> ({e.AuslaufEnde.X},{e.AuslaufEnde.Y})");
        if (e.AusweichRichtung != 0) sb.Append($" Ausweichen {e.AusweichRichtung}");
        if (e.IsBuilding && e.Patrouille) sb.Append(" Patrouille");
        return sb.ToString();
    }

    /// <summary>Lage der Einheiten — Teil des Spielstand-Fingerabdrucks (bug-650: er
    /// enthielt nicht einmal die Lage).</summary>
    public string LageFingerabdruck()
    {
        long a = 0, b = 0; int n = 0;
        foreach (var e in _entities)
        {
            if (e.IsProp || e.IsBuilding || e.Dead) continue;
            n++; a += e.Col * 7 + e.Row * 13; b += (long)e.Col * e.Row;
        }
        return $"Lage {n} Einheiten, Pruefzahl {a}/{b}";
    }

    /// <summary>Die Befehle aller Einheiten als Pruefzahlen — Teil des
    /// Spielstand-Fingerabdrucks. »unsichtbar ohne Ausweg« = untergestellt, in keinem
    /// Gebaeude und ohne Routensatz (der Wagen aus der Lesung §1.4 Nr. 1).</summary>
    public string BefehleFingerabdruck()
    {
        int wege = 0, schlange = 0, angriffe = 0, routen = 0, gestartet = 0, unsichtbar = 0, sonder = 0;
        long rest = 0, wegpruef = 0, ziel = 0, apruef = 0, spruef = 0, rpruef = 0, sopruef = 0;
        foreach (var e in _entities)
        {
            if (e.IsProp || e.Dead) continue;
            if (!e.IsBuilding)
            {
                ziel += e.Goal.X * 7 + e.Goal.Y * 13;
                if (e.Path != null)
                {
                    wege++; rest += e.Path.Count - e.PathIdx;
                    for (int k = e.PathIdx; k < e.Path.Count; k++) wegpruef += e.Path[k].X * 3 + e.Path[k].Y * 5 + k;
                }
                foreach (var o in e.Orders) { schlange++; spruef += o.Cell.X * 3 + o.Cell.Y * 5 + Kennung(o.Target); }
                if (Untergestellt(e) && e.InGebaeude == null && e.Route == null) unsichtbar++;
                if (e.EinsteigTraeger != -1 || e.Bauart != 0 || e.BuildOrder != 0 || e.RailGoal != null
                    || e.LeavingDock >= 0 || e.AuslaufZellen != 0 || e.AusweichRichtung != 0)
                {
                    sonder++;
                    sopruef += e.EinsteigTraeger * 3 + e.Bauart * 5 + e.BauZelle.X + e.BauZelle.Y * 7 + e.BuildOrder * 11
                             + e.BuildTarget + (e.RailGoal?.X ?? 0) + e.RailWork * 13 + Kennung(e.LeavingDock)
                             + e.AuslaufZellen * 17 + e.AusweichRichtung * 19;
                }
            }
            else if (e.Patrouille) { sonder++; sopruef += 23; }
            if (e.Target >= 0) { angriffe++; apruef += Kennung(e.Target) + (e.Ordered ? 1 : 0); }
            if (e.Route is Transportroute r)
            {
                routen++;
                if (r.Gestartet) gestartet++;
                rpruef += r.Quelle[0] * 3 + r.Quelle[1] * 5 + r.Ziel * 7 + r.Fahrziel * 11 + r.Warten * 13 + r.Gesamt * 17
                        + (r.Gestartet ? 19 : 0);
            }
        }
        return $"Befehle: Wege {wege} (Rest {rest}, Pruefzahl {wegpruef}), Ziele {ziel}, Angriffe {angriffe} ({apruef}), " +
               $"Schlange {schlange} ({spruef}), Routen {routen}, gestartet {gestartet} ({rpruef}), " +
               $"Sonderauftraege {sonder} ({sopruef}), unsichtbar ohne Ausweg {unsichtbar}";
    }
}
