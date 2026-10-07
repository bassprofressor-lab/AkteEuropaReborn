namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <c>--bauliste-check</c> — der Prüfstand zu den <b>Farben der Bauliste</b> im Basisfenster.
///
/// <para><b>Anlass:</b> seine Meldung vom 17.09.2026 — »im original sieht man dahinter die
/// benötigten ressourcen, bei uns auch, aber im original sind diese <b>farbig</b>«. Lesung:
/// <c>berichte/basis-bauliste-fable.md</c>.</para>
///
/// <para><b>Was er misst.</b> Nicht »sieht farbig aus«, sondern zwei prüfbare Dinge:</para>
/// <list type="number">
///   <item><b>Die Farben gegen die Palettendatei des Originals.</b> Jede Farbe der Bauliste
///   trägt ihren Palettenplatz; der Prüfstand schlägt ihn in <c>Assets/Legacy/DATA/NN.PAL</c>
///   nach (8 Byte Kopf, dann 768 Byte RGB 0…255) und vergleicht Byte für Byte. Damit hängen
///   unsere Werte an der Datei von 1997 und nicht mehr an einem Bildschirmfoto — und wer sie
///   später »schöner« macht, fällt hier durch.</item>
///   <item><b>Der Preistext.</b> Das Original setzt ihn als <c>"]"+W+"["+F+"{"+S</c>
///   <b>ohne Leerzeichen</b> (<c>0x469D81…0x469F3E</c>). Der Prüfstand baut ihn aus den
///   Zeilen der Bauliste nach und zählt, wie viele davon dreiteilig sind.</item>
/// </list>
///
/// <para><b>Nullmodell <c>--bauliste-alt</c>:</b> es stellt die Farben zurück, die bis zum
/// 17.09.2026 im Quelltext standen (»unsere Werte, nach dem Bildschirmfoto«) — damit muss der
/// Prüfstand durchfallen, und zwar an allen Plätzen außer dem Auswahlbalken.</para>
///
/// <para>⚠ 07.10.2026, bug-481: auf K1/K3 hat der Spieler keine Fahrzeugfabrik, und der
/// Prüfstand sagte »0 Zeilen, DURCHGEFALLEN« — kein Urteil. Jetzt drei gestaffelte,
/// benannte EINGRIFFE: eigenes Gebäude anwählen; sonst eine FREMDE Fahrzeugfabrik für
/// die Messung dem Betrachter zuschreiben (K3); sonst den Preistext direkt aus der
/// Entwurfsliste für Art 1 bauen (K1, dort steht gar keine Fabrik). Die Farben hängen
/// ohnehin nicht an der Karte.</para>
///
/// <para>⚠ Was er NICHT prüfen kann: ob die Zeile bei fehlendem Geld die Farbe wechselt. Das
/// ist eine Eigenschaft des Zeichners, keine Zahl zur Laufzeit — die Lesung hat es mit einer
/// Vollerhebung erledigt (177 Relokationen, das Lager wird in der Zeilenschleife nie gelesen),
/// und der Quelltext sagt es an der Stelle.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--bauliste-alt</c> — der Gegenschalter auf die geratenen Farben.</summary>
    public static bool BaulisteAlt;

    public string BaulisteCheck()
    {
        var sb = new System.Text.StringBuilder("bauliste-check\n");

        // ---- 1. die Farben gegen die Palettendatei -----------------------------
        byte[]? pal = PaletteLesen(out string palWoher);
        int stimmt = 0, geprueft = 0;
        if (pal == null)
        {
            sb.AppendLine($"   ⚠ keine Palette gefunden ({palWoher}) — der Prüfstand sagt NICHTS");
        }
        else
        {
            sb.AppendLine($"   Palette: {palWoher}");
            foreach (var (was, platz, farbe) in UI.BaseWindow.BaulistenFarben())
            {
                int o = 8 + 3 * platz;
                if (o + 2 >= pal.Length) continue;
                int r = pal[o], g = pal[o + 1], b = pal[o + 2];
                int ur = Mathf.RoundToInt(farbe.R * 255f),
                    ug = Mathf.RoundToInt(farbe.G * 255f),
                    ub = Mathf.RoundToInt(farbe.B * 255f);
                bool gleich = r == ur && g == ug && b == ub;
                geprueft++;
                if (gleich) stimmt++;
                sb.AppendLine($"     {(gleich ? "ok  " : "⚠ FALSCH")} {was,-34} Platz {platz,3}: " +
                              $"Palette ({r},{g},{b}) — bei uns ({ur},{ug},{ub})");
            }
        }

        // ---- 2. der Preistext --------------------------------------------------
        //
        // ⚠ Kopflos ist nichts angewählt, und BuildPanelRows() geht über Producer(),
        // also über die Auswahl. Der Prüfstand wählt sich darum selbst eine eigene
        // Fahrzeugfabrik (BType 1) — das ist ein EINGRIFF und wird so gesagt.
        int vorher = _selected;
        var rows = BuildPanelRows();
        if (rows.Count == 0)
            for (int i = 0; i < _entities.Count; i++)
            {
                var x = _entities[i];
                if (!x.IsBuilding || x.Dead || x.IsProp || x.Built == 0) continue;
                if (x.Owner != ViewPlayer) continue;
                _selected = i;
                var probe = BuildPanelRows();
                if (probe.Count == 0) continue;
                rows = probe;
                sb.AppendLine($"   ⚠ EINGRIFF: Gebäude Platz {x.Slot} (Art {x.BType}) " +
                              "angewählt — kopflos ist nichts ausgewählt");
                break;
            }
        // ⚠ 07.10.2026, bug-481 — K1 und K3 geben dem Spieler KEINE Fabrik (K1:
        // »man kann keine Basis einnehmen«, K3: nur drei Gebäude, keines eine
        // Fahrzeugfabrik des Spielers); der Prüfstand sagte dort »0 Zeilen,
        // DURCHGEFALLEN« und damit nichts. Der Preistext hängt aber nicht am
        // Besitzer, sondern an der Bauliste. Darum, als ZWEITER EINGRIFF und so
        // gesagt: eine FREMDE Fahrzeugfabrik (Art 1) wird für die Dauer der Messung
        // dem Betrachter zugeschrieben (Owner), die Zeilen gelesen, der Besitzer
        // wiederhergestellt. Die Karte bleibt danach, wie sie war.
        if (rows.Count == 0)
            for (int i = 0; i < _entities.Count; i++)
            {
                var x = _entities[i];
                if (!IsUnitPlant(x) || x.Dead || x.IsProp || x.Built == 0) continue;
                int besitzer = x.Owner;
                x.Owner = ViewPlayer;
                _selected = i;
                var probe = BuildPanelRows();
                x.Owner = besitzer;
                if (probe.Count == 0) continue;
                rows = probe;
                sb.AppendLine($"   ⚠ EINGRIFF: keine eigene Fabrik auf der Karte — fremde Fahrzeugfabrik " +
                              $"Platz {x.Slot} (Spieler {besitzer}) fuer die Messung dem Betrachter " +
                              "zugeschrieben und wieder zurueckgegeben");
                break;
            }
        // K1 hat GAR KEINE Fahrzeugfabrik (nur vier herrenlose Bauwerke). Der
        // Preistext ist aber eine Eigenschaft der Entwurfszeile, nicht der Karte:
        // BuildPanelRows setzt ihn fuer Art 1 aus BuildableFor(1, Besitzer) als
        // W/F/S — dritter EINGRIFF, gesagt: dieselbe Liste ohne Gebaeude.
        string preisQuelle = "";
        if (rows.Count == 0)
        {
            LoadDesigns();
            if (_designs != null)
                foreach (int mi in BuildableFor(1, ViewPlayer))
                {
                    var d = _designs[mi];
                    rows.Add(new UI.BuildPanel.Row(d.Name, $"{d.CostW}/{d.CostF}/{d.CostS}", false, false));
                }
            if (rows.Count > 0)
            {
                preisQuelle = " (Preistext aus der Entwurfsliste Art 1, ohne Fabrik)";
                sb.AppendLine("   ⚠ EINGRIFF: keine Fahrzeugfabrik auf der Karte — Preistext aus der " +
                              "Entwurfsliste fuer Art 1 (BuildableFor) statt aus einem Gebaeude");
            }
        }
        _selected = vorher;
        int dreiteilig = 0, eigene = 0;
        string beispiel = "";
        foreach (var z in rows)
        {
            var teile = z.Cost.Split('/');
            if (teile.Length != 3) continue;
            dreiteilig++;
            if (z.Own) eigene++;
            if (beispiel.Length == 0)
                beispiel = $"»]{teile[0]}[{teile[1]}{{{teile[2]}«  ({z.Name})";
        }
        sb.AppendLine($"   Bauliste: {rows.Count} Zeilen, davon {dreiteilig} mit dreiteiligem Preis" +
                      (eigene > 0 ? $", {eigene} selbst erstellt" : "") +
                      (beispiel.Length > 0 ? $"\n     erste Zeile: {beispiel}" : ""));

        bool bestanden = pal != null && geprueft > 0 && stimmt == geprueft && dreiteilig > 0;
        sb.AppendLine(bestanden
            ? $"   ✅ BESTANDEN — {stimmt}/{geprueft} Farben stimmen mit der Palette des " +
              $"Originals, {dreiteilig} Preise dreiteilig.{preisQuelle}"
            : rows.Count == 0
                ? "   ⚠ KEIN URTEIL — keine Fahrzeugfabrik auf der Karte und keine Entwurfsliste " +
                  "(eine Karte mit Fabrik nehmen, z. B. --campaign=21)."
                : $"   ❌ DURCHGEFALLEN — {geprueft - stimmt} Farbe(n) weichen von der Palette ab " +
                  "(Nullmodell --bauliste-alt muss genau das zeigen).");
        return sb.ToString();
    }

    /// <summary>Die erste lesbare <c>NN.PAL</c>. Die Oberflächenplätze sind in allen 27
    /// Paletten gleich (Ausnahmen 15/40/46/90 nur bei den Sinnbildern bzw. in zwei
    /// Sonderpaletten) — für die Prüfung genügt darum eine.</summary>
    private static byte[]? PaletteLesen(out string woher)
    {
        foreach (string name in new[] { "01.PAL", "02.PAL", "03.PAL" })
        {
            string pfad = Core.Content.Path($"DATA/{name}");
            if (!FileAccess.FileExists(pfad)) continue;
            using var f = FileAccess.Open(pfad, FileAccess.ModeFlags.Read);
            if (f == null) continue;
            var b = f.GetBuffer((long)f.GetLength());
            if (b.Length >= 8 + 768) { woher = name; return b; }
        }
        woher = "keine NN.PAL unter DATA/";
        return null;
    }
}
