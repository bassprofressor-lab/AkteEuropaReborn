namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <c>--ring-check</c> — <b>»orange Ringe ohne Körper an Stellen Gefallener«.</b>
///
/// <para>Der Bericht stand seit dem 16.08.2026 unerklärt in der Liste, und er
/// nannte eine <b>Farbe</b>. Eine Farbe ist bei uns ein <b>Spieler</b>
/// (<c>Factions[5]</c> ist orange), also keine Ursache. Dieser Prüfstand macht
/// aus dem Bericht Zahlen: er zählt genau die Zeichenstelle mit, an der ein Ring
/// ohne Rumpf entsteht — den Rückfall in
/// <see cref="MapEntityLayer.DrawUnitBody"/>, wenn <b>keine einzige</b> der vier
/// Bildquellen (Infanterie, Rumpf+Turm, zusammengesetzt, nackt) etwas liefert.
/// </para>
///
/// <para>⚠ <b>Er misst die ZEICHENSTELLE, nicht eine Nachbildung davon.</b> Ein
/// Prüfstand, der die Bildsuche noch einmal nachprogrammierte, könnte an einer
/// anderen Stelle scheitern als der Zeichner — und dann zeigte er auf den
/// falschen Täter. Deshalb hängt der Zähler im Zeichner selbst
/// (<c>RingTrace</c>).</para>
///
/// <para>⚠ Und er läuft <b>nach einem Gefecht</b>, nicht am Anfang: die Meldung
/// spricht von »Stellen Gefallener«. Wer im ersten Takt zählt, misst die Karte
/// beim Laden und nicht den Fall, um den es geht.</para>
/// </summary>
public partial class MapEntityLayer
{
    private int _ringCheck = -1;
    private long _ringBis;

    /// <summary><c>--ring-check[=takte]</c> anwerfen.</summary>
    public void RingCheckStart(int takte)
    {
        RingTrace = true;
        _ringFall.Clear();
        _ringSlots.Clear();
        _ringBis = takte;
        _ringCheck = 0;
    }

    private void PollRingCheck()
    {
        if (_ringCheck != 0) return;
        // ⚠ Die Wartebedingung steht VOR der Ausgabe.
        if (DebugTicks < _ringBis) return;
        _ringCheck = -1;
        RingTrace = false;

        var sb = new System.Text.StringBuilder(
            $"ring-check nach {DebugTicks} Takten\n");

        // Wieviele Einheiten es ueberhaupt gibt, damit die Zahl darunter ein
        // Verhaeltnis hat und keine nackte Zahl ist.
        int lebend = 0, tot = 0, totInf = 0;
        foreach (var e in _entities)
        {
            if (e.IsBuilding || e.IsProp) continue;
            if (e.Dead) { tot++; if (e.Infantry >= 0) totInf++; }
            else lebend++;
        }
        sb.AppendLine($"  {lebend} lebende Einheiten, {tot} gefallene " +
                      $"({totInf} davon Infanterie — nur die bleiben liegen)");

        if (_ringFall.Count == 0)
        {
            sb.Append("  KEIN Ring ohne Koerper gezeichnet — der gemeldete Fall " +
                      "tritt in diesem Lauf nicht ein");
            GD.Print(sb); return;
        }

        // ⚠ Nach der HÄUFIGKEIT sortiert: die eine Gruppe, die es ausmacht,
        // soll oben stehen und nicht zwischen Einzelfaellen verschwinden.
        var liste = new List<KeyValuePair<string, int>>(_ringFall);
        liste.Sort((a, b) => b.Value.CompareTo(a.Value));
        int summe = 0;
        foreach (var kv in liste) summe += kv.Value;
        sb.AppendLine($"  ⚠ {_ringSlots.Count} Einheiten zeichnen einen Ring OHNE " +
                      $"Rumpf ({summe} Zeichenvorgaenge, {liste.Count} Bauarten):");
        for (int i = 0; i < liste.Count && i < 12; i++)
            sb.AppendLine($"    {liste[i].Value,6}x  {liste[i].Key}");
        if (liste.Count > 12) sb.AppendLine($"    … und {liste.Count - 12} weitere");

        // ⚠ Die entscheidende Trennung: liegt es an der BILDQUELLE (kein Satz in
        // der Bank) oder am ZUSTAND der Einheit (Rumpf −1, also nie einer
        // zugeordnet)? Ohne sie zeigt der Befund auf den falschen Taeter.
        int ohneRumpf = 0, mitRumpf = 0;
        foreach (int slot in _ringSlots)
        {
            var e = _entities.Find(x => x.Slot == slot && !x.IsBuilding && !x.IsProp);
            if (e == null) continue;
            if (e.UnitType < 0 && e.Infantry < 0 && string.IsNullOrEmpty(e.Combo)) ohneRumpf++;
            else mitRumpf++;
        }
        sb.Append($"  davon {ohneRumpf} ohne jede Bildangabe (Rumpf −1, Inf −1, " +
                  $"Combo leer) und {mitRumpf} mit einer Angabe, zu der die Bank " +
                  "nichts hergibt");
        GD.Print(sb.ToString());
    }
}
