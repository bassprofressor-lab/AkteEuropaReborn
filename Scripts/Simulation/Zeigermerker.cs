using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>DIE ZEIGERMERKER DES EINHEITENMENÜS</b> — die vier Bytes, mit denen das
/// Original einen Menüdruck <b>aufschiebt</b>, bis der Spieler das nächste Mal
/// klickt oder der nächste Takt kommt.
///
/// <para>Gebaut am 08.09.2026 auf seine Ansage »und dann die zeigermerker
/// bauen«. Bis dahin sagten fünf Symbole beim Druck, dass es sie nicht gibt.</para>
///
/// <para><b>Die vier Merker, alle gelesen</b> (<c>berichte/revier3.md</c> §3.2
/// und <c>berichte/kampagne3-fable.md</c> 2.2):</para>
/// <list type="bullet">
///   <item><c>byte[0xA182F9]</c> — <b>Angreifen</b> (Menücodes 0, 28, 31). Er
///   setzt <c>dword[0x502AD4] := 2</c>, also den ANGRIFFSZEIGER, und ist
///   derselbe Merker, den auch die Strg-Taste hält. Der nächste Klick schickt
///   Busbefehl 11 auf die ZELLE und verbraucht ihn
///   (<c>byte[0xA182F9] := 0</c> @0x437567).</item>
///   <item><c>byte[0xA31A9C]</c> — <b>Bewegen</b> (Codes 1, 29, 32). Derselbe
///   Bau, nur mit dem Fahrbefehl.</item>
///   <item><c>byte[0xA1833B]</c> — <b>Beschützen</b> (Codes 2, 26, 30, 33).
///   ⚠ Er wartet NICHT auf einen Klick: die Hauptschleife löst ihn im nächsten
///   Takt ein und schickt <b>Befehl 12</b> an die GEWÄHLTE Einheit
///   (<c>0x433750</c>).</item>
///   <item><c>byte[0xA18330]</c> — <b>Handsteuerung</b> (Code 5). Ebenfalls
///   sofort im nächsten Takt: <c>UKOL == 1</c> → Befehl 5 (Steuerung aufgeben),
///   sonst Befehl 4 (übernehmen). Die Pfeiltasten schicken dann Befehl 1
///   (<c>0x433460</c>).</item>
/// </list>
///
/// <para>⚠ <b>Was hier NICHT gebaut ist und warum:</b> <b>Befehl 12</b>
/// (Beschützen) — sein Behandler ist nicht gelesen, und was »beschützen« im
/// Original tut, steht nirgends. Eine erfundene Wirkung wäre schlimmer als
/// keine; der Druck sagt es. Ebenso ist der Behandler von <b>Befehl 13</b>
/// (Selbstzerstörung, <c>0x4C2F40</c> → <c>0x402130</c>) nicht gelesen — dort
/// ist aber der NAME eindeutig, und wir lassen die Einheit sterben. Das ist
/// eine ausgewiesene Setzung.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary>Welcher Klickmerker gerade scharf ist — der MENUECODE, und
    /// <b>−1 heisst keiner</b>.
    ///
    /// <para>⚠⚠ 08.09.2026, und der eigene Pruefstand hat es fast verschluckt:
    /// hier stand »0 = keiner«, und <see cref="CodeAngreifen"/> IST 0. »Scharf
    /// auf Angreifen« und »gar kein Merker« waren damit derselbe Zustand — der
    /// Angriffsmerker haette nie gefeuert. Und die Probe fragte
    /// <c>Zeigermerker == CodeAngreifen</c>, also <c>0 == 0</c>, und meldete
    /// gruen. ⭐ Ein Feld, dessen »leer« mit einem gueltigen Wert
    /// zusammenfaellt, ist ein Fehler, auch wenn gerade nichts auffaellt.</para>
    /// </summary>
    public int Zeigermerker = -1;

    /// <summary>Was der Spieler dazu lesen soll.</summary>
    public string ZeigerNote = "";

    /// <summary>Steht die gewählte Einheit unter HANDSTEUERUNG? Im Original
    /// <c>UKOL == 1</c> (⚠ als »handgesteuert« MARKIERT VERMUTET, siehe
    /// revier3 §3.2), bei uns dieses Feld — wir führen UKOL nur für die drei
    /// Werte 0/48/50.</summary>
    public int HandsteuerungIdx = -1;

    /// <summary>Wie oft ein Merker gesetzt, verbraucht und abgebrochen wurde —
    /// ⚠ ohne die drei Zahlen ist »der Merker wirkt« nicht von »er wird nie
    /// erreicht« zu unterscheiden.</summary>
    public int MerkerGesetzt, MerkerEingeloest, MerkerAbgebrochen;

    /// <summary>Den Klickmerker scharf machen (Angreifen/Bewegen).</summary>
    public string MerkerSetzen(int code)
    {
        Zeigermerker = code;
        MerkerGesetzt++;
        ZeigerNote = code == CodeAngreifen
            ? "Angreifen: jetzt das Ziel anklicken (Rechtsklick bricht ab)"
            : "Bewegen: jetzt die Zelle anklicken (Rechtsklick bricht ab)";
        return ZeigerNote;
    }

    public void MerkerAbbrechen()
    {
        if (Zeigermerker < 0) return;
        Zeigermerker = -1;
        MerkerAbgebrochen++;
        ZeigerNote = "abgebrochen";
    }

    /// <summary>
    /// Der Klick, der den Merker einlöst. Angreifen geht auf das, was unter
    /// dem Zeiger liegt (Einheit, Gebäude — sonst die Bodenzelle, wie
    /// <c>0x437417</c>), Bewegen auf die Zelle.
    /// </summary>
    public bool MerkerKlick(Vector2 mapPos)
    {
        if (Zeigermerker < 0) return false;
        int code = Zeigermerker;
        Zeigermerker = -1;                    // @0x437567: der Merker wird VERBRAUCHT
        MerkerEingeloest++;

        if (code == CodeBewegen)
        {
            int n = PostMove(mapPos);
            ZeigerNote = n > 0 ? "" : "dorthin fuehrt kein Weg";
            return true;
        }

        // Angreifen: erst die Einheit unter dem Zeiger, sonst der Boden — genau
        // die Reihenfolge des Klickarms 0x437417.
        bool ab = PostAttack(mapPos) || PostAttackGround(mapPos);
        ZeigerNote = ab ? "" : "darauf kann diese Einheit nicht schiessen";
        return true;
    }

    /// <summary>
    /// <b>Selbstzerstörung</b> — Befehl 13 (<c>0x4487D9</c>, P1 = Einheit).
    /// ⚠ Der Behandler (<c>0x4C2F40</c> → <c>0x402130</c>) ist NICHT gelesen;
    /// gebaut ist die Bedeutung des Namens: die Einheit stirbt, mit demselben
    /// Weg wie ein tödlicher Treffer (Wrack, Klang, Zählung).
    /// </summary>
    public string SelbstzerstoerungAusfuehren()
    {
        int idx = MenueEinheit();
        if (idx < 0) return "keine eigene Einheit gewaehlt";
        var e = _entities[idx];
        // ⚠ Dieselbe Schwelle wie im Menue (0x4419F8): unter 15 % Huelle bietet
        // das Original den Eintrag gar nicht erst an.
        if (e.HpMax > 0 && e.Hp * 100 / e.HpMax <= 15)
            return "zu schwer beschaedigt fuer die Selbstzerstoerung";
        Kill(idx, e);
        Selbstzerstoert++;
        return $"{LabelOf(e)} hat sich selbst zerstoert";
    }

    public int Selbstzerstoert;

    /// <summary>
    /// <b>Handsteuerung</b> — Merker B. Beim Einlösen prüft das Original
    /// <c>UKOL == 1</c>: steht sie schon unter Steuerung, kommt Befehl 5
    /// (aufgeben), sonst Befehl 4 (übernehmen). Danach schicken die
    /// Pfeiltasten Befehl 1 (<c>0x433460</c>).
    /// </summary>
    public string HandsteuerungUmschalten()
    {
        int idx = MenueEinheit();
        if (idx < 0) return "keine eigene Einheit gewaehlt";
        if (HandsteuerungIdx == idx)
        {
            HandsteuerungIdx = -1;
            return "Handsteuerung abgegeben";
        }
        if (!_entities[idx].Mobile) return "diese Einheit faehrt nicht";
        HandsteuerungIdx = idx;
        return "Handsteuerung: mit den Pfeiltasten fahren, nochmal druecken beendet sie";
    }

    /// <summary>Ein Schritt der Handsteuerung — <c>0x433460</c> schickt je
    /// Pfeiltaste Befehl 1. Die Richtung ist die Achtelrichtung des
    /// Originals.</summary>
    public bool HandsteuerungSchritt(int dx, int dy)
    {
        if (HandsteuerungIdx < 0 || HandsteuerungIdx >= _entities.Count) return false;
        var e = _entities[HandsteuerungIdx];
        if (e.Dead || !e.Mobile) { HandsteuerungIdx = -1; return false; }
        // ⚠ UNSERE Umsetzung: das Original steuert im Schritttakt, wir setzen
        // ein Fahrziel EINE Zelle weiter. Der Weg dorthin geht durch dieselbe
        // Wegsuche wie jeder andere Fahrbefehl — ein zweiter, direkter
        // Bewegungspfad waere die schlimmere Abweichung.
        var ziel = new Vector2I(e.Col + dx, e.Row + dy);
        _sel.Clear(); _sel.Add(HandsteuerungIdx);
        return PostMove(CellCenter(ziel.X, ziel.Y)) > 0;
    }

    /// <summary>Die Meldezeile — sie gehoert in jeden Lauf.</summary>
    public string MerkerWatchLine()
        => $"zeigermerker: {MerkerGesetzt}x gesetzt, {MerkerEingeloest}x eingeloest, "
         + $"{MerkerAbgebrochen}x abgebrochen, {Selbstzerstoert}x Selbstzerstoerung"
         + (MerkerGesetzt == 0 ? "   ⚠ nie gesetzt — die Zahlen sagen nichts" : "");
}
