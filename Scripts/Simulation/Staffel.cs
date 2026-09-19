namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>DIE STAFFEL — der Knopf »Gruppieren« am Flughafen</b> (19.09.2026, nach
/// <c>berichte/flughafenfenster-k20-fable.md</c> §2).
///
/// <para>Seine Beobachtung: »man einen Gruppieren Button hat, um einheiten
/// direkt zu einer gruppe zu bilden«. Gelesen ist dazu:</para>
///
/// <code>
///   Taste 8 (Hilfe #15)  @0x449B5C..0x449C16   KEIN Busbefehl
///       Fenster+0x1A := naechste Nummer aus 0x428D60(cis, puffer)
///   0x428D60  (F 0x427F50)  die am Flughafen BENUTZTEN Staffelnummern,
///             plus EINE freie — mehr nicht
///   Zeile anklicken, ZWEITER Klick auf die gewaehlte  @0x44A028
///             Flugzeug +0x26 := Fenstermarke   (oder 0xFF = heraus)
///   0x439170(gruppe, flughafen)   schreibt der sec81-Gruppe den Namen
///             'Airplanes' und die Griffe 20000 + Platz
/// </code>
///
/// <para>⭐⭐ <b>Die Staffel ist KEINE eigene Einrichtung.</b> Sie ist eine der
/// zehn <b>sec81-Spielergruppen</b>, die der Spieler auch am Boden hat — nicht
/// die KI-Gruppen aus sec60/68. Wer sie als drittes Gruppensystem baut, baut
/// eines zuviel; der Beitritt muss auf dem bestehenden Gruppenweg aufsetzen
/// (<c>MapViewer</c>, die zehn Gruppen).</para>
///
/// <para><b>Wozu sie dient:</b> »Angriff« und »Bombe wechseln« wirken
/// <b>staffelweit</b>, die Handsteuerung nicht.</para>
///
/// <para><b>UNSERE SETZUNGEN, benannt:</b></para>
/// <list type="bullet">
///   <item>Der <b>Anfangswert</b> von <c>Fenster+0x1A</c> ist UNGELESEN (der
///   Anleger <c>0x457A79</c> steht nicht darauf). Wir fangen mit
///   <c>0xFF</c> = keine an — das ist die Wahl, die am wenigsten behauptet.</item>
///   <item>Welche Nummer »die eine freie« ist, wenn alle zehn belegt sind, ist
///   ungelesen; wir bleiben dann auf den benutzten stehen.</item>
///   <item>Dass der Beitritt die sec81-Gruppe NEU baut und der Austritt
///   <b>nicht</b>, ist gelesen — und deshalb steht es auch so hier, obwohl es
///   unsymmetrisch aussieht.</item>
/// </list>
///
/// <para>Gegenschalter <c>--staffel-aus</c>: der Knopf ist gesperrt und die
/// Felder bleiben unbenutzt — das Nullmodell, unter dem »Bombe wechseln«
/// nur EINE Maschine drehen darf.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary><c>--staffel-aus</c></summary>
    public static bool StaffelAus;

    /// <summary>Wie oft der Knopf weitergedreht hat, wie oft eine Maschine
    /// beigetreten und wie oft sie ausgetreten ist.</summary>
    public int StaffelDrehungen, StaffelBeitritte, StaffelAustritte;

    /// <summary>
    /// <b><c>0x428D60</c> — die Nummern, durch die »Gruppieren« dreht:</b> die
    /// am Flughafen BENUTZTEN, und dahinter <b>eine</b> freie.
    ///
    /// <para>⚠ Nicht »alle zehn«. Das ist der Grund, warum der Knopf sich in
    /// einem leeren Hangar nicht bewegt und in einem mit zwei Staffeln genau
    /// drei Stellungen hat.</para>
    /// </summary>
    public List<int> StaffelNummern(int flughafen)
    {
        var benutzt = new SortedSet<int>();
        foreach (var a in _special)
        {
            if (a.Dead || a.HomeSlot != flughafen) continue;
            if (a.Staffel != 0xFF) benutzt.Add(a.Staffel);
        }
        var liste = new List<int>(benutzt);
        for (int n = 0; n < 10; n++)
            if (!benutzt.Contains(n)) { liste.Add(n); break; }
        return liste;
    }

    /// <summary>Taste 8 — die Marke des FENSTERS eine Stellung weiter.
    /// <c>0xFF</c> steht vor der ersten Nummer.</summary>
    public int StaffelWeiter(int flughafen, int marke)
    {
        if (StaffelAus) return 0xFF;
        var liste = StaffelNummern(flughafen);
        if (liste.Count == 0) return 0xFF;
        StaffelDrehungen++;
        int i = liste.IndexOf(marke);
        // Von 0xFF aus auf die erste; hinter der letzten wieder auf 0xFF.
        if (i < 0) return liste[0];
        return i + 1 < liste.Count ? liste[i + 1] : 0xFF;
    }

    /// <summary>
    /// Der <b>ZWEITE</b> Klick auf die schon gewählte Hangarzeile (@0x44A028):
    /// trägt die Marke ein oder nimmt sie heraus.
    ///
    /// <para>⚠ <b>Der Beitritt baut die sec81-Gruppe neu, der Austritt
    /// nicht</b> — so steht es im Original, und die Unsymmetrie ist kein
    /// Versehen: eine Gruppe, aus der einer austritt, behält ihre übrigen
    /// Mitglieder, ohne neu gebildet zu werden.</para>
    /// </summary>
    public void StaffelUmschalten(int platz, int marke)
    {
        if (StaffelAus || marke == 0xFF) return;
        var a = FlugzeugAufPlatz(platz);
        if (a == null) return;
        if (a.Staffel == marke)
        {
            a.Staffel = 0xFF;
            StaffelAustritte++;
            return;
        }
        a.Staffel = marke;
        StaffelBeitritte++;
        StaffelGruppeNeu(a.HomeSlot, marke);
    }

    /// <summary>
    /// <b><c>0x439170(gruppe, flughafen)</c></b> — die sec81-Gruppe dieser
    /// Nummer wird NEU gebildet: Name <c>'Airplanes'</c>, Mitglieder alle
    /// Flugzeuge <b>dieses Flughafens</b> mit dieser Staffel.
    ///
    /// <para>⚠ Die Griffe sind im Original <c>20000 + sec19-Platz</c>. Bei uns
    /// führt der Gruppenweg über unsere eigenen Kennungen; die Zahl 20000 ist
    /// die des Originals und steht hier nur als Herkunftsangabe.</para>
    /// </summary>
    private void StaffelGruppeNeu(int flughafen, int marke)
    {
        var mitglieder = new List<int>();
        foreach (var a in _special)
        {
            if (a.Dead || a.HomeSlot != flughafen) continue;
            if (a.Staffel == marke) mitglieder.Add(a.Slot);
        }
        StaffelGruppen[marke] = mitglieder;
    }

    /// <summary>Die zehn Staffeln, wie der Flughafenknopf sie füllt. ⚠ Sie
    /// liegen hier und nicht in den Bodengruppen, weil unser Gruppenweg auf
    /// Einheitenkennungen läuft und ein Flugzeug bei uns keine trägt — das ist
    /// eine ABWEICHUNG vom Original, wo es dieselben zehn Gruppen sind. Sie
    /// steht hier, damit sie nicht als gelesene Trennung durchgeht.</summary>
    public readonly Dictionary<int, List<int>> StaffelGruppen = new();

    /// <summary>Alle Maschinen einer Staffel dieses Flughafens — das, was
    /// »Angriff« mitnimmt.</summary>
    public List<Special> StaffelMaschinen(int flughafen, int marke)
    {
        var l = new List<Special>();
        foreach (var a in _special)
        {
            if (a.Dead || a.HomeSlot != flughafen) continue;
            if (marke == 0xFF || a.Staffel == marke) l.Add(a);
        }
        return l;
    }

    /// <summary>Die Messzeile — <c>--staffel-check</c>. ⚠ Kontrollzahl ist die
    /// Zahl der NUMMERN: sie muss unter <c>--staffel-aus</c> auf 0 fallen,
    /// während die Zahl der Maschinen im Hangar gleich bleibt.</summary>
    public string StaffelAuskunft()
    {
        if (StaffelDrehungen == 0 && StaffelBeitritte == 0 && StaffelAustritte == 0)
            return "";
        return $"staffel-check: {StaffelDrehungen}x gedreht, {StaffelBeitritte} Beitritte, "
             + $"{StaffelAustritte} Austritte, {StaffelGruppen.Count} Staffeln belegt"
             + (StaffelAus ? "   [--staffel-aus: alles 0 ist das SOLL]" : "");
    }
}
