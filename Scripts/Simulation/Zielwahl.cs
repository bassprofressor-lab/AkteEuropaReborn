namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <b>DER »ANGRIFF«-KNOPF UND SEINE ZIELWAHL</b> (19.09.2026, nach
/// <c>berichte/flughafenfenster-k20-fable.md</c> §3, Bauaufgabe 2).
///
/// <para><b>Seine Meldung:</b> »angriff lässt die einheiten nur über dem
/// flughafen kreisen, aber ich sagte dir ja, das ich mit angriff einen punkt
/// setzen kann auf der minimap und die da dort hinfliegen«. Er hatte es mir
/// wirklich schon bei der ersten K20-Beschreibung gesagt (»einen ANgriff knopf
/// hat wo man dann via Minimap das ziel setzt«), und der Knopf tat bis heute
/// nur, was unser alter »Starten« tat.</para>
///
/// <para><b>GELESEN</b> (C, F dazu benannt):</para>
/// <code>
///   Taste 5 »Angriff«  @0x449A66..0x449AB6   KEIN Busbefehl:
///       oeffnet den Kartenschirm (Fensterart 3) an der MAUSPOSITION im
///       Zielwahl-Modus 2 (Zoom 2 px/Zelle, 0x444D90 / F 0x443D50 79 %)
///       und setzt byte[0x4FD640]
///   Klick im Kartenfenster  @0x4492DC
///   ODER Klick auf der HAUPTKARTE (Zustand 7 -> 0x437994[7])
///       -> 0x450310 (F 0x44EFC0):
///          Ziel aus der imap der angeklickten Zelle
///          Befehl 502, Modus 7 (ein ZIEL) bzw. 1 (eine ZELLE),
///          fuer JEDE Maschine der Staffel (40 Eintraege @0x44F1ED)
///          -> launch_aircraft 0x426020 (F 0x425200) setzt
///             +0x14/+0x15 (Zielzelle), +0x2E (Ziel), m_uk
///       danach schliesst das Fenster
/// </code>
///
/// <para><b>⚠ UNSERE ABWEICHUNG, und sie ist die grösste hier:</b> das Original
/// öffnet ein <b>eigenes Kartenfenster</b> (Art 3) an der Maus. Bei uns steht
/// die Übersichtskarte <b>dauerhaft</b> unten rechts — das ist eine
/// dokumentierte alte Setzung (<c>MapViewer</c>, Übersichtskarte). Statt ein
/// zweites Kartenfenster zu bauen, nimmt die Zielwahl <b>die stehende
/// Übersichtskarte</b>; das ist genau der Zustand, den die Lesung als
/// Gegenschalter <c>--zielwahl-minimap</c> vorgesehen hat, und es ist auch der
/// Weg, den er beschreibt. Der Klick auf die <b>Hauptkarte</b> gilt ebenfalls —
/// der ist Original (Zustand 7).</para>
///
/// <para><b>Was damit NICHT behauptet wird:</b> der Zoom 2 px/Zelle, der Rand
/// 20 und das Angriffskreuz als Zeiger sind gelesen, aber nicht gebaut,
/// solange es kein eigenes Kartenfenster gibt. Das steht als offener Punkt.</para>
///
/// <para>Gegenschalter <c>--zielwahl-aus</c>: »Angriff« startet wieder ohne
/// Ziel (der Stand vom 19.09. morgens). Messzeile <c>--zielwahl-check</c>.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary><c>--zielwahl-aus</c> — »Angriff« startet ohne Ziel.</summary>
    public static bool ZielwahlAus;

    /// <summary>Modus 7 — das Ziel ist eine EINHEIT oder ein GEBÄUDE
    /// (<c>0x44F121</c>).</summary>
    public const int ZielModusZiel = 7;

    /// <summary>Modus 1 — das Ziel ist eine leere ZELLE
    /// (<c>0x44F137</c>).</summary>
    public const int ZielModusZelle = 1;

    /// <summary>Höchstens 40 Maschinen je Auftrag (<c>0x44F1ED</c>).</summary>
    public const int ZielEintraege = 40;

    /// <summary>Schwebt gerade eine Zielwahl? Im Original
    /// <c>byte[0x4FD640]</c>.</summary>
    public bool ZielwahlSchwebt { get; private set; }

    /// <summary>Der Flughafen (<c>Entity.Slot</c>), dessen Fenster den Knopf
    /// gedrückt hat, und die Staffelmarke, die mitfliegen soll.</summary>
    private int _zielFlughafen = -1, _zielMarke = 0xFF;

    /// <summary>Wie oft eine Zielwahl begonnen, mit einem ZIEL beendet, mit
    /// einer ZELLE beendet und abgebrochen wurde; und wie viele Maschinen
    /// insgesamt losgeschickt wurden.</summary>
    public int ZielwahlBegonnen, ZielwahlAufZiel, ZielwahlAufZelle,
               ZielwahlAbgebrochen, ZielwahlMaschinen;

    /// <summary>Was die Oberfläche beim Beginn der Zielwahl tun soll — Zeiger
    /// umstellen und das Fenster wegnehmen. Null heisst: niemand hört zu.</summary>
    public System.Action<bool>? OnZielwahl;

    /// <summary>
    /// <b>Taste 5 »Angriff«</b> — sie sendet KEINEN Befehl, sie schaltet die
    /// Zielwahl ein.
    /// </summary>
    /// <returns>false, wenn nichts zu schicken ist — dann bleibt alles, wie es
    /// war, und der Aufrufer darf auf den alten Weg zurückfallen.</returns>
    public bool ZielwahlBeginnen(int flughafen, int marke)
    {
        if (ZielwahlAus) return false;
        var maschinen = StaffelMaschinen(flughafen, marke);
        if (maschinen.Count == 0)
        {
            _order = "Es steht keine Maschine im Hangar.";
            return false;
        }
        ZielwahlSchwebt = true;
        _zielFlughafen = flughafen;
        _zielMarke = marke;
        ZielwahlBegonnen++;
        _order = marke == 0xFF
            ? $"Angriff: Ziel auf der Karte waehlen ({maschinen.Count} Maschinen)"
            : $"Angriff: Ziel auf der Karte waehlen (Staffel {marke}, "
              + $"{maschinen.Count} Maschinen)";
        OnZielwahl?.Invoke(true);
        UpdatePanel();
        return true;
    }

    /// <summary>Abbruch — Fenster zu, Rechtsklick, oder ESC.</summary>
    public void ZielwahlAbbrechen()
    {
        if (!ZielwahlSchwebt) return;
        ZielwahlSchwebt = false;
        _zielFlughafen = -1;
        _zielMarke = 0xFF;
        ZielwahlAbgebrochen++;
        _order = "Angriff abgebrochen";
        OnZielwahl?.Invoke(false);
        UpdatePanel();
    }

    /// <summary>
    /// <b><c>0x450310</c> — der Klick, der das Ziel setzt.</b> Gilt für den
    /// Klick auf der Übersichtskarte UND auf der Hauptkarte.
    ///
    /// <para>Das Ziel kommt aus der Belegung der angeklickten Zelle: eine
    /// Einheit oder ein Gebäude ergibt <b>Modus 7</b> mit ihrem Griff, eine
    /// leere Zelle <b>Modus 1</b> mit den Koordinaten.</para>
    /// </summary>
    /// <returns>true, wenn die Zielwahl damit erledigt ist.</returns>
    public bool ZielwahlKlick(int col, int row)
    {
        if (!ZielwahlSchwebt) return false;
        int flughafen = _zielFlughafen, marke = _zielMarke;
        ZielwahlSchwebt = false;
        _zielFlughafen = -1;
        _zielMarke = 0xFF;
        OnZielwahl?.Invoke(false);

        if (_nav == null || !_nav.InBounds(col, row))
        {
            _order = "Das Ziel liegt ausserhalb der Karte";
            UpdatePanel();
            return true;
        }

        // Ziel aus der Zelle: erst ein Fahrzeug der Belegung, dann ein
        // Gebaeude, dann Fussvolk — sonst die Zelle selbst.
        int ziel = ZielAufZelle(col, row);
        int modus = ziel >= 0 ? ZielModusZiel : ZielModusZelle;
        int n = StaffelStarten(flughafen, marke, ziel, col, row);

        if (modus == ZielModusZiel) ZielwahlAufZiel++;
        else ZielwahlAufZelle++;
        ZielwahlMaschinen += n;

        string was = ziel >= 0 && ziel < _entities.Count
            ? (_entities[ziel].Name.Length > 0 ? _entities[ziel].Name : "ein Ziel")
            : $"({col},{row})";
        _order = n > 0
            ? $"{n} Maschinen greifen {was} an"
            : "keine Maschine konnte starten";
        UpdatePanel();
        QueueRedraw();
        return true;
    }

    /// <summary>Was auf dieser Zelle als Ziel gilt — oder −1 für »leere
    /// Zelle«. ⚠ Die REIHENFOLGE ist die des Originals (Truppzelle: der erste
    /// Feind), nicht eine eigene Vorliebe.</summary>
    private int ZielAufZelle(int col, int row)
    {
        if (_nav != null)
        {
            int occ = _nav.OccupantAt(col, row);
            if (occ >= 0 && occ < _entities.Count && !_entities[occ].Dead) return occ;
        }
        int bi = GebaeudeAufZelle(col, row);
        if (bi >= 0 && bi < _entities.Count && !_entities[bi].Dead) return bi;
        for (int k = 0; k < _entities.Count; k++)
        {
            var m = _entities[k];
            if (!m.Dead && !m.IsProp && !m.IsBuilding && m.Col == col && m.Row == row)
                return k;
        }
        return -1;
    }

    /// <summary>
    /// <b>Befehl 502 für die ganze Staffel</b> → <c>launch_aircraft</c>
    /// <c>0x426020</c>: Zielzelle <c>+0x14/+0x15</c>, Ziel <c>+0x2E</c>.
    ///
    /// <para>⚠ Eine Maschine, die schon fliegt, wird UMGELENKT statt gestartet
    /// (<c>uk 0 → launch</c>, <c>uk 1 → retarget</c>) — das ist gelesen; welche
    /// Felder das Umlenken im Einzelnen anfasst, ist es nicht, darum setzen wir
    /// dieselben drei wie beim Start.</para>
    /// </summary>
    private int StaffelStarten(int flughafen, int marke, int ziel, int col, int row)
    {
        var maschinen = StaffelMaschinen(flughafen, marke);
        var mitte = ZellMitte(col, row);
        int n = 0;
        foreach (var a in maschinen)
        {
            if (n >= ZielEintraege) break;              // 40 Eintraege @0x44F1ED
            if (a.Absturz) continue;
            if (a.Stored)
            {
                var home = GebaeudeMitSlot(a.HomeSlot);
                if (home == null) continue;
                a.Stored = false;
                a.Pos = home.Pos;
                a.Col = home.Col; a.Row = home.Row;
                a.Alt = ElevOf(a.Col, a.Row) * 15;      // air_takeoff @0x4260B9
                a.Sollhoehe = FlughoeheMax;             // air_order @0x425E6B
                home.Hangar?.Remove(a.Slot);
            }
            a.Target = ziel;                            // +0x2E
            a.Goal = mitte;                             // +0x14/+0x15
            a.PlayerGoal = mitte;
            n++;
        }
        return n;
    }

    /// <summary>Die Messzeile — <c>--zielwahl-check</c>.
    ///
    /// <para>⚠ KONTROLLZAHL ist <see cref="ZielwahlBegonnen"/> gegen die Summe
    /// der Ausgänge: jede begonnene Zielwahl muss genau einmal enden (Ziel,
    /// Zelle oder Abbruch). Läuft das auseinander, hängt eine Zielwahl —
    /// und eine hängende Zielwahl frisst jeden Kartenklick.</para></summary>
    public string ZielwahlAuskunft()
    {
        int enden = ZielwahlAufZiel + ZielwahlAufZelle + ZielwahlAbgebrochen;
        if (ZielwahlBegonnen == 0 && enden == 0) return "";
        return $"zielwahl-check: {ZielwahlBegonnen}x begonnen, {ZielwahlAufZiel}x auf ein "
             + $"ZIEL (Modus 7), {ZielwahlAufZelle}x auf eine ZELLE (Modus 1), "
             + $"{ZielwahlAbgebrochen}x abgebrochen = {enden} Enden "
             + (enden == ZielwahlBegonnen ? "(stimmt)" : "⚠ HAENGT")
             + $" · {ZielwahlMaschinen} Maschinen geschickt"
             + (ZielwahlAus ? "   [--zielwahl-aus: alles 0 ist das SOLL]" : "");
    }
}
