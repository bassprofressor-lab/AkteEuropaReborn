namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Text;
using Godot;

/// <summary>
/// <c>--werft-check</c> (12.10.2026, bug-495) — <b>was zeigt und was kann die WERFT?</b>
///
/// <para><b>Anlass:</b> seine Meldung aus K27 »Sea wolves«: »In der Werft kann ich irgendwie
/// nix produzieren. Er zeigt mir dort auch $ anstatt Rohstoffe an unter Produktion.«</para>
///
/// <para><b>Was das Original tut (gelesen, C):</b> das Werftfenster (Fensterart 11, Zeichner
/// <c>0x471800</c>) kennt KEIN Geld. Je Entwurf druckt es <c>"]"W"["F"{"S</c> aus dem
/// Schiffssatz <c>+0x19/+0x1A/+0x1B</c> (<c>0x4724B0…0x47263E</c>), und unter der Liste
/// (<c>0x4726CC…0x4728B9</c>, x 0x1E, y 0xF3) dieselben drei Sinnbilder mit dem LAGER DER
/// WERFT-STATION: Gebäude <c>[+0x8B9044]</c> → <c>+0x19</c> (<c>0xC06929</c>) → sec29
/// <c>0x87A1FA</c> → Gebäude <c>+0x2C/+0x2E/+0x30</c> (<c>0xC0693C/E/40</c>). Der Knopf
/// »Produzieren« (<c>0x44B253</c>) prüft genau diese drei Lager gegen die drei Preise und
/// sonst nichts — kein Kontostand, keine Energie.</para>
///
/// <para><b>Was er misst:</b></para>
/// <list type="number">
///   <item><b>Die Kopfzeile</b> unseres Fensters für die eigene Werft: sie muss das Lager der
///   Werft-Station tragen und darf kein <c>$</c> enthalten.</item>
///   <item><b>Die Bauliste</b>: Zeilen, Preise, bezahlbar gegen das Lager der Station.</item>
///   <item><b>Der Klickweg mit dem Lager der Karte</b> (kein Eingriff): was »Produzieren« sagt.</item>
///   <item><b>Der Klickweg mit genug Teilen</b> (EINGRIFF, benannt): wird bezahlt, läuft der Bau?</item>
/// </list>
///
/// <para><b>Nullmodell <c>--werft-titel-alt</c></b>: die Kopfzeile zeigt wieder den
/// Kontostand — Punkt 1 muss durchfallen.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--werft-titel-alt</c> — der Stand vor bug-495: die Werft zeigt in der
    /// Kopfzeile den Kontostand (<c>PRODUKTION  $…</c>) statt des Lagers der Werft-Station.</summary>
    public static bool WerftTitelAlt;

    /// <summary>Die Kopfzeile der Werft: das Lager der verbundenen Werft-Station, wie
    /// <c>0x4726CC…0x4728B9</c> es druckt. Ohne Station (sec29 leer) stehen Nullen —
    /// der Knopf <c>0x44B253</c> liest dann ebenfalls ein leeres Lager.</summary>
    private string WerftTitel(Entity dock)
    {
        int yi = ShipyardOf(dock);
        var y = yi >= 0 ? _entities[yi] : null;
        return $"WERFT  W{y?.StockW ?? 0} F{y?.StockF ?? 0} S{y?.StockS ?? 0}";
    }

    public string WerftCheck()
    {
        var sb = new StringBuilder("werft-check\n");
        int di = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (IsDock(e) && !e.Dead && e.Owner == ViewPlayer) { di = i; break; }
        }
        _werftKeine = di < 0;
        if (di < 0)
            return sb.Append("  ⚠ keine eigene Werft (Art 11) auf dieser Karte — ungeprueft\n  KEIN URTEIL").ToString();

        var dock = _entities[di];
        int yi = ShipyardOf(dock);
        var yard = yi >= 0 ? _entities[yi] : null;
        sb.AppendLine($"  Werft Platz {dock.Slot} ({dock.Col},{dock.Row}) Spieler {dock.Owner}, sec29 -> Station "
                    + (yard == null ? "KEINE" : $"Platz {yard.Slot} Art {yard.BType} Lager W{yard.StockW} F{yard.StockF} S{yard.StockS}"));
        int owner = Mathf.Clamp(dock.Owner, 0, 7);
        if (yard != null)
        {
            // Woher die Station Teile bekommt: fuer den Menschen NUR ueber die Bahn
            // (RailFreight: »nichts anderes bewegt die vier Lagerfelder fuer den
            // Spieler«; die Teilespende 0x4BE5C0 gilt nur der KI).
            int n = 0;
            foreach (var l in _railLines)
            {
                if (l.Bud1 != yard.Slot && l.Bud2 != yard.Slot) continue;
                n++;
                int other = l.Bud1 == yard.Slot ? l.Bud2 : l.Bud1;
                var o = _entities.Find(b => b.IsBuilding && b.Slot == other);
                sb.AppendLine($"     Bahnlinie {l.Slot}: Platz {l.Bud1} <-> {l.Bud2} (Gegenende Art {o?.BType ?? -1}, Spieler {o?.Owner ?? -1}), "
                            + $"Schalter W{l.Mode[0]} F{l.Mode[1]} S{l.Mode[2]} T{l.Mode[3]}, faze {l.Faze}");
            }
            sb.AppendLine($"     {n} Bahnlinie(n) an der Werft-Station");
            _werftStation = yi;
        }
        sb.AppendLine($"  Kontostand Spieler {owner}: ${_money[owner]} (zaehlt fuer die Werft NICHT, 0x44B253)");

        int vorher = _selected;
        _selected = di; _sel.Clear(); _sel.Add(di);

        // ---- 1. die Kopfzeile ------------------------------------------------
        string titel = BuildPanelTitle();
        bool titelOk = !titel.Contains('$') && yard != null
                       && titel.Contains($"W{yard.StockW} ") && titel.Contains($"F{yard.StockF} ")
                       && titel.EndsWith($"S{yard.StockS}");
        sb.AppendLine($"  1. Kopfzeile: »{titel}« -> {(titelOk ? "Lager der Station, kein $ ✔" : "⚠ FALSCH (Soll: Lager der Werft-Station, 0x4726CC)")}");

        // ---- 2. die Bauliste -------------------------------------------------
        var rows = BuildPanelRows();
        int bezahlbar = 0;
        foreach (var r in rows) if (r.Affordable) bezahlbar++;
        sb.AppendLine($"  2. Bauliste: {rows.Count} Zeilen, bezahlbar mit dem Stationslager: {bezahlbar}");
        int minW = int.MaxValue, minF = int.MaxValue, minS = int.MaxValue;
        foreach (var d in ShipMenu(dock))
        {
            minW = Mathf.Min(minW, d.CostW); minF = Mathf.Min(minF, d.CostF); minS = Mathf.Min(minS, d.CostS);
            sb.AppendLine($"     Entwurf {d.Index,2} {d.Name,-18} {d.CostW}/{d.CostF}/{d.CostS}"
                        + (yard != null && yard.StockW >= d.CostW && yard.StockF >= d.CostF && yard.StockS >= d.CostS
                           ? "  bezahlbar" : "  zu teuer"));
        }
        bool listeOk = rows.Count > 0;

        // ---- 3. Klick mit dem Lager der Karte --------------------------------
        int pick = Mathf.Max(0, rows.FindIndex(r => r.Affordable));
        int w0 = yard?.StockW ?? 0, f0 = yard?.StockF ?? 0, s0 = yard?.StockS ?? 0;
        if (rows.Count > 0) BuildPanelPick(pick);
        sb.AppendLine($"  3. Produzieren Zeile {pick + 1} mit dem Kartenlager: »{_order}«, Bauzeit {dock.BuildTime:0.0}s");

        // ---- 4. Klick mit genug Teilen (EINGRIFF) ----------------------------
        bool bautOk = false;
        if (yard != null && rows.Count > 0 && dock.BuildTime <= 0f)
        {
            yard.StockW = Mathf.Max(yard.StockW, 999); yard.StockF = Mathf.Max(yard.StockF, 999);
            yard.StockS = Mathf.Max(yard.StockS, 999);
            int wv = yard.StockW, fv = yard.StockF, sv = yard.StockS;
            _selected = di; _sel.Clear(); _sel.Add(di);
            BuildPanelPick(0);
            var d = ShipMenu(dock)[dock.MenuIndex % ShipMenu(dock).Count];
            bautOk = dock.BuildTime > 0f && wv - yard.StockW == d.CostW
                     && fv - yard.StockF == d.CostF && sv - yard.StockS == d.CostS;
            sb.AppendLine($"  4. ⚠ EINGRIFF Stationslager auf 999: Zeile 1 »{_order}«, Bauzeit {dock.BuildTime:0.0}s, "
                        + $"abgezogen {wv - yard.StockW}/{fv - yard.StockF}/{sv - yard.StockS} -> {(bautOk ? "✔" : "⚠ NEIN")}");
            dock.BuildTime = 0f;
            yard.StockW = w0; yard.StockF = f0; yard.StockS = s0;
        }
        else if (dock.BuildTime > 0f)
        {
            bautOk = true;
            sb.AppendLine("  4. entfaellt — Punkt 3 hat schon gebaut ✔ (der Bau laeuft weiter, Stapellauf siehe Abschluss)");
            _werftDock = di;
            _werftSchiffeVor = SchiffeVon(owner);
        }

        _selected = vorher; _sel.Clear(); if (vorher >= 0) _sel.Add(vorher);
        if (minW != int.MaxValue && yard != null)
            sb.AppendLine($"  billigstes Schiff je Teil: W{minW} F{minF} S{minS}; es fehlt am Kartenlager "
                        + $"W{Mathf.Max(0, minW - w0)} F{Mathf.Max(0, minF - f0)} S{Mathf.Max(0, minS - s0)}");
        bool alles = titelOk && listeOk && bautOk;
        _werftTeil1 = alles;
        sb.Append(alles ? "  Teil 1 (Fenster, Liste, Bestellung) ✔ — Stapellauf folgt" : "  ❌ DURCHGEFALLEN");
        return sb.ToString();
    }

    private int _werftDock = -1, _werftSchiffeVor, _werftStation = -1;
    private bool _werftTeil1, _werftKeine;

    /// <summary>Lebende Schiffe (Gattung 4/5) dieses Spielers.</summary>
    private int SchiffeVon(int spieler)
    {
        int n = 0;
        foreach (var e in _entities)
            if (!e.IsBuilding && !e.IsProp && !e.Dead && e.Owner == spieler && e.GameUnitType is 4 or 5) n++;
        return n;
    }

    /// <summary>Teil 2 nach der Wartezeit: ist das bestellte Schiff vom Stapel gelaufen?</summary>
    public string WerftCheckAbschluss()
    {
        if (_werftKeine) return "werft-check Abschluss: keine eigene Werft — KEIN URTEIL";
        if (_werftDock < 0)
            return _werftTeil1 ? "werft-check Abschluss: kein Bau gelaufen — KEIN URTEIL" : "werft-check Abschluss: Teil 1 durchgefallen";
        var dock = _entities[_werftDock];
        int owner = Mathf.Clamp(dock.Owner, 0, 7);
        int neu = SchiffeVon(owner) - _werftSchiffeVor;
        Entity? letzt = null;
        for (int i = _entities.Count - 1; i >= 0; i--)
        {
            var e = _entities[i];
            if (!e.IsBuilding && !e.IsProp && !e.Dead && e.Owner == owner && e.GameUnitType is 4 or 5) { letzt = e; break; }
        }
        string station = _werftStation >= 0
            ? $", Lager der Station jetzt W{_entities[_werftStation].StockW} F{_entities[_werftStation].StockF} S{_entities[_werftStation].StockS}"
            : "";
        bool ok = _werftTeil1 && neu >= 1 && dock.BuildTime <= 0f;
        return $"werft-check Abschluss: Restbauzeit {dock.BuildTime:0.0}s, neue Schiffe von Spieler {owner}: {neu}"
             + (neu >= 1 && letzt != null ? $" (zuletzt {letzt.Name} auf ({letzt.Col},{letzt.Row}))" : "") + station
             + (ok ? "\n  ✅ BESTANDEN" : "\n  ❌ DURCHGEFALLEN");
    }
}
