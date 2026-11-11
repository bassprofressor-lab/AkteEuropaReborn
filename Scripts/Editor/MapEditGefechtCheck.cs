namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Godot;

/// <summary>
/// ⭐ 11.11.2026 — <b>--map-edit-gefecht-check</b> (bug-740/741/742, Paket E2).
///
/// <para>Belegt, dass eine vom Editor erzeugte Karte im GEFECHT spielbar ist —
/// gemessen an der geladenen Partie, nicht an der Datei allein:</para>
/// <list type="number">
///   <item>je Mitspieler mindestens eine Einheit, und JEDE Kartentruppe ist
///   kampffaehig (<see cref="CanFight"/>) mit Reichweite &gt; 0, Tempo &gt; 0 und
///   Sicht &gt; 0 (bug-740, M2);</item>
///   <item>je Mitspieler Basis (Typ 1) und Fabrik (Typ 2..4) aus der Karte;</item>
///   <item>jedes Kartengebaeude hat Griffzellen 60000+Platz (<c>_griffZelle</c>),
///   und in der Belegung steht KEIN Wert im Waldband 51000..51299 (bug-741, M4);</item>
///   <item>jede Griffzelle ist eine SPERRENDE Musterzelle (bug-742, M5).</item>
/// </list>
/// <para>Nullmodelle (beim ERZEUGEN der Karte gesetzt, nicht beim Gefecht):
/// <c>--editor-einheit-alt</c> → 0 kampffaehig; <c>--editor-griff-alt</c> → 0
/// Griffzellen, Waldband &gt; 0; <c>--editor-stempel-alt</c> → nicht sperrende
/// Griffzellen &gt; 0. Ablauf:</para>
/// <code>
///   … -- --map-new=e2probe,64,64,47 [Gegenschalter]
///   … -- --skirmish=map_e2probe --no-briefing --no-movie --map-edit-gefecht-check
/// </code>
/// <para>Liegt in <c>Scripts/Editor/</c> als Teil von <see cref="MapEntityLayer"/>,
/// wie <c>MapEditBridge.cs</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    public string MapEditGefechtCheck(string karte, IEnumerable<int> mitspieler)
    {
        var sb = new StringBuilder($"map-edit-gefecht-check {karte}\n");
        bool ok = true;

        // ---- die Datei: Waldband gegen Gebaeudegriffe -----------------------
        int wald = 0, griffDatei = 0;
        string pfad = ProjectSettings.GlobalizePath(Core.Content.UserRoot + "Maps/" + karte + ".entities.json");
        try
        {
            using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(pfad));
            if (doc.RootElement.TryGetProperty("spatial", out var sp) &&
                sp.TryGetProperty("nonempty", out var nv))
                foreach (var c in nv.EnumerateArray())
                {
                    int v = c.TryGetProperty("value", out var vv) ? vv.GetInt32() : -1;
                    if (v is >= 51000 and <= 51299) wald++;
                    if (v is >= 60000 and <= 60299) griffDatei++;
                }
        }
        catch (System.Exception e) { sb.AppendLine($"  Datei {pfad}: {e.Message}"); ok = false; }
        sb.AppendLine($"  Belegung: {griffDatei} Zellen 60000..60299, {wald} Zellen 51000..51299 " +
                      "(alter Editorgriff im Waldband — muss 0 sein)");
        ok &= wald == 0 && griffDatei > 0;

        // ---- je Mitspieler ---------------------------------------------------
        var neu = new HashSet<int>();
        foreach (var e in StartbasisProtokoll) neu.Add(e.Platz);
        foreach (int p in mitspieler)
        {
            int einheiten = 0, kampf = 0, reichweite = 0, tempo = 0, sicht = 0, basen = 0, fabriken = 0;
            foreach (var e in _entities)
            {
                if (e.Dead || e.IsProp || e.Owner != p) continue;
                if (e.IsBuilding)
                {
                    if (e.BType == 1) basen++;
                    if (e.BType is 2 or 3 or 4) fabriken++;
                    continue;
                }
                einheiten++;
                if (CanFight(e)) kampf++;
                if (e.Range > 0) reichweite++;
                if (e.Speed > 0) tempo++;
                if (e.Sight > 0) sicht++;
            }
            bool pOk = einheiten > 0 && kampf == einheiten && reichweite == einheiten
                       && tempo == einheiten && sicht == einheiten
                       && basen >= 1 && fabriken >= 1 && !neu.Contains(p);
            ok &= pOk;
            sb.AppendLine($"  P{p}: {einheiten} Einheiten, {kampf} kampffaehig, {reichweite} mit Reichweite, " +
                          $"{tempo} mit Tempo, {sicht} mit Sicht; {basen} Basis, {fabriken} Fabrik" +
                          (neu.Contains(p) ? " [Startbasis vom SPIEL aufgestellt — Karte trug keine]" : "") +
                          (pOk ? "" : "  <- FEHLT"));
        }

        // ---- Griffzellen der Kartengebaeude ----------------------------------
        int gebaeude = 0, mitGriff = 0, griffZellen = 0, nichtSperrend = 0, fremd = 0;
        foreach (var b in _entities)
        {
            if (!b.IsBuilding || b.IsProp || b.Dead) continue;
            gebaeude++;
            int n = 0;
            var bt = Patterns?.GetBuildingType(b.BType);
            foreach (var kv in _griffZelle)
            {
                if (kv.Value != b.Slot) continue;
                n++;
                if (Patterns != null && bt is { IsEmpty: false } t &&
                    !Patterns.PatternBlocks(t.FirstPattern, kv.Key.Col - b.Col, kv.Key.Row - b.Row))
                    nichtSperrend++;
            }
            griffZellen += n;
            if (n > 0) mitGriff++;
            // die Gegenrichtung: sperrende Musterzellen (ohne Tueren), die NICHT
            // diesem Gebaeude gehoeren — ein Nachbar hat sie ueberschrieben
            if (Patterns != null && bt is { IsEmpty: false } t2)
                for (int dx = 0; dx < Import.CwpFile.PatternWidth; dx++)
                    for (int dy = 0; dy < Import.CwpFile.PatternHeight; dy++)
                    {
                        if (!Patterns.PatternBlocks(t2.FirstPattern, dx, dy)) continue;
                        if (b.DoorCells.Contains((dx, dy))) continue;
                        var z = (b.Col + dx, b.Row + dy);
                        if (_nav != null && !_nav.InBounds(z.Item1, z.Item2)) continue;
                        if (!_griffZelle.TryGetValue(z, out int wer) || wer != b.Slot) fremd++;
                    }
        }
        sb.AppendLine($"  Gebaeude: {mitGriff} von {gebaeude} mit Griffzellen ({griffZellen} Zellen), " +
                      $"davon {nichtSperrend} NICHT sperrende Musterzellen (muss 0 sein); " +
                      $"{fremd} sperrende Musterzellen ohne eigenen Griff (Nachbar/Rand; Auskunft)");
        ok &= gebaeude > 0 && mitGriff == gebaeude && nichtSperrend == 0;

        return sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }
}
