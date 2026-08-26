using System.Collections.Generic;
using Godot;
using GDict = Godot.Collections.Dictionary<string, Godot.Variant>;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>DIE BRÜCKEN DER KARTE — sec17</b>, und die Bedingung <c>bridge</c> des
/// Missionsskripts.
///
/// <para><b>Gemeldet am 25.08.2026</b> aus Mission 2: bei uns öffnete sich
/// sofort bei Takt 0 die »Nachricht vom Versorgungszentrum« (Text 121,
/// »während der Schlacht wurden wichtige lokale Brücken zerstört … wir nehmen
/// 20$ von Ihrem Budget«), im Let's Play kommt sie an dieser Stelle nicht.</para>
///
/// <para><b>Die Regel im Original</b> steht @<c>0x499378</c>:</para>
/// <code>
///   cmp  word[0xBC56E0], 0      ; Einmal-Merker
///   jne  ende
///   mov  al, byte[0xBFEA92]     ; sec17-Satz 0, Feld +0x00
///   test al, al
///   jne  ende                   ; nur wenn == 0
///   show_text(330, 180, 121)
///   money(-20)
///   inc  word[0xBC56E0]
/// </code>
///
/// <para><c>0xBFEA92</c> ist <b>sec17</b>, 100 Plätze zu 24 Byte
/// (OFFENE_FRAGEN.md, Abschnitt »sec17 (Brücken/Molen) und sec21 (Rampen) —
/// schon im Gelände«). Satzform: <c>+0x00/+0x01</c> Zelle, <c>+0x02</c>
/// Richtung, <c>+0x03..+0x11</c> ein 3×5-Kachelfeld, <c>+0x13</c> Länge,
/// <c>+0x16</c> u16 Trefferpunkte = 500 in <b>110 von 110</b> Sätzen. Die
/// Bedingung liest also <b>die SPALTE der Brücke</b>, und »== 0« heißt
/// »Platz 0 ist leer«.</para>
///
/// <para>⚠⚠ <b>Was hier bis zum 25.08.2026 stand, war falsch — samt
/// Begründung.</b> <c>BridgeUsed</c> antwortete konstant <c>0</c>, und der
/// Kommentar daneben begründete das damit, sec17 sei eine Tafel für
/// <i>während des Spiels gebaute</i> Brücken und »in allen Kampagnenkarten
/// beim Start LEER«. <b>Beides ist widerlegt:</b> die Doku führt sec17 als
/// <b>110 Bauwerke auf 21 Karten</b>, die von Anfang an im Gelände stehen, und
/// nachgezählt über die exportierten Karten haben <b>25 Karten</b> sec17-Sätze
/// — <b>map_02 zwei</b>, auf den Plätzen 0 und 1:</para>
/// <code>
///   slot 0: col=2,  row=55, dir=1, len=3, hp=500
///   slot 1: col=5,  row=22, dir=0, len=1, hp=500
/// </code>
/// <para>Feld +0x00 von Satz 0 ist also <b>2</b>, nicht 0 — im Original
/// schweigt die Regel, und der Pionier, den sie schickt, erschien bei uns
/// prompt auf (3,53), gleich neben dieser Brücke bei (2,55).</para>
///
/// <para>⭐ <b>Die Lehre steht in der Projektnotiz und hat hier zugeschlagen:</b>
/// auch die BEGRÜNDUNG eines offenen Punktes gehört geprüft, nicht nur der
/// Punkt. Der Kommentar las sich schlüssig und war zwei Zeilen von der
/// widerlegenden Messung entfernt.</para>
///
/// <para>⚠ <b>UNSERE einzige Setzung hier:</b> ein Satz mit <c>hp &lt;= 0</c>
/// gilt als leer. Wir können Brücken (noch) nicht zerstören, also ändert das
/// heute nichts — aber die Regel existiert im Original nur für den Fall
/// »Brücke ist weg«, und ohne diesen Haken könnte sie nach dem Nachrüsten der
/// Zerstörung nie feuern. Wer sie nachrüstet, muss <see cref="_karteBruecken"/>
/// beim Zerstören nachführen.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>sec17 der geladenen Karte: Platznummer → Satz. Leer, wenn die
    /// Karte keine Brücken hat (8 der 33 Kampagnenkarten).</summary>
    private readonly Dictionary<int, (int Col, int Row, int Hp)> _karteBruecken = new();

    /// <summary>Anzahl der gelesenen sec17-Sätze — für die Startmeldung.</summary>
    public int BrueckenSaetze => _karteBruecken.Count;

    /// <summary>
    /// Liest <c>bridges</c> aus der <c>map_*.entities.json</c>. Der Exporter
    /// schreibt sie seit jeher (<c>EntitiesJson.cs</c>, Schlüssel
    /// <c>slot/col/row/dir/len/hp</c>); gelesen hat sie zur Laufzeit bis zum
    /// 25.08.2026 <b>niemand</b>.
    /// </summary>
    private void LiesBruecken(GDict root)
    {
        _karteBruecken.Clear();
        if (!root.TryGetValue("bridges", out var bv) || bv.VariantType != Variant.Type.Array)
            return;
        foreach (var item in bv.AsGodotArray())
        {
            if (item.VariantType != Variant.Type.Dictionary) continue;
            var b = item.AsGodotDictionary<string, Variant>();
            int slot = GetI(b, "slot", -1);
            if (slot < 0) continue;
            _karteBruecken[slot] = (GetI(b, "col"), GetI(b, "row"), GetI(b, "hp"));
        }
        // Die Zahl gehoert in den Start-Mitschnitt: an ihr haengt, ob die
        // Regel `bridge` schweigt oder feuert, und genau das war der Fehler.
        if (_karteBruecken.Count > 0)
            GD.Print($"bruecken: {_karteBruecken.Count} sec17-Satz/Saetze; "
                   + $"Platz 0 Feld+0x00 = {BrueckeFeld0(0)} "
                   + $"(0 hiesse LEER und laesst die Regel bridge feuern)");
    }

    /// <summary>
    /// Was das Original an <c>byte[0xBFEA92 + 24·slot]</c> liest: die SPALTE
    /// der Brücke auf diesem Platz, oder 0, wenn der Platz leer ist. Das
    /// Missionsskript vergleicht diesen Wert (<c>kind: "bridge"</c>).
    /// </summary>
    private int BrueckeFeld0(int slot)
        => _karteBruecken.TryGetValue(slot, out var b) && b.Hp > 0 ? b.Col : 0;
}
