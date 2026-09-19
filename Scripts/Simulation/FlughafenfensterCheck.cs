namespace AkteEuropaReborn.Rendering;

using System.Text;
using Godot;

/// <summary>
/// <b><c>--flughafenfenster-check</c> — GEHT AM FLUGHAFEN NUR NOCH EIN FENSTER
/// AUF, UND KANN MAN DARIN NOCH KAUFEN?</b> (12.09.2026, bug-211)
///
/// <para><b>Gemeldet:</b> »beim flughafen gehen 2 fenster auf, einmal ein
/// eigenbau und einmal das original. (im gefecht). das original wäre mir
/// lieber, das eigenbau ding muss nicht mit aufgehen«.</para>
///
/// <para>Der Flughafen (Gebäudeart 9) führte auf ZWEI Wege gleichzeitig: über
/// <see cref="MapEntityLayer.FensterArtVon"/> auf das Originalfenster
/// (Fensterart 5 aus WINDOWS.CWW) und über <see cref="MapEntityLayer.Producer"/>
/// auf unser Baufenster. Beide gingen auf.</para>
///
/// <para><b>⚠⚠ Warum dieser Prüfstand DREI Dinge misst und nicht eines.</b> Am
/// 25.08.2026 stand derselbe Fall beim Nachschubposten, und dort ist die Lehre
/// aufgeschrieben worden: an <c>Producer()</c> hängt der KAUFWEG
/// (<c>BuildPanelPick</c> beginnt mit <c>var e = Producer();</c>). Wer den
/// Flughafen aus <c>Producer()</c> nähme, statt nur die ANZEIGE abzuschalten,
/// nähme dem Spieler die Flugzeuge weg — und ein Prüfstand, der nur »Fenster
/// zu« misst, ginge dabei grün durch. Gemessen wird darum:</para>
/// <list type="number">
///   <item>die ANZEIGE ist aus (<c>BuildPanelWanted == false</c>);</item>
///   <item>die LOGIK lebt (<c>Producer() != null</c>, Bauzeilen &gt; 0);</item>
///   <item>der KAUFWEG steht im Originalfenster (Angebote &gt; 0) und er
///   FUNKTIONIERT — der Knopf wird wirklich gedrückt, und danach muss ein
///   Flugzeug mehr im Hangar stehen und das Teilelager kleiner sein.</item>
/// </list>
///
/// <para>⚠ Der Lauf GREIFT EIN: er übergibt einen Flughafen an den Betrachter
/// und legt ihm Teile ins Lager, weil auf den Gefechtskarten alle Flughäfen
/// zivil anfangen und ohne Teile niemand kaufen kann. Beides wird danach
/// zurückgesetzt. Das steht hier, statt still zu wirken.</para>
///
/// <para><b>Nullmodell:</b> eine BASIS desselben Spielers muss unser Baufenster
/// weiterhin aufmachen — sonst misst Zeile 1 nur, dass <c>BuildPanelWanted</c>
/// immer falsch ist. (Genau dieser Fehler ist dem Posten-Prüfstand am 25.08.
/// schon einmal passiert, als er das falsche Vergleichsgebäude nahm.)</para>
/// </summary>
public partial class MapEntityLayer
{
    public string FlughafenfensterCheck()
    {
        var sb = new StringBuilder("flughafenfenster-check\n");
        if (_airDesigns == null || _airDesigns.Count == 0) FillCampaignAirDesigns();

        int idx = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var b = _entities[i];
            if (b.IsBuilding && !b.IsProp && !b.Dead && b.BType == 9) { idx = i; break; }
        }
        if (idx < 0) return sb.Append("  kein Flughafen auf dieser Karte — NICHT GEMESSEN").ToString();

        var e = _entities[idx];
        int merkeAuswahl = _selected, merkeEigner = e.Owner;
        int mw = e.StockW, mf = e.StockF, ms = e.StockS;
        int merkeMenu = e.MenuIndex;

        // ---- der Eingriff, benannt -----------------------------------------
        e.Owner = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;
        e.StockW = Mathf.Max(e.StockW, 99);
        e.StockF = Mathf.Max(e.StockF, 99);
        e.StockS = Mathf.Max(e.StockS, 99);
        _selected = idx;

        sb.Append($"  Flughafen Platz {e.Slot} ({e.Col},{e.Row}), an Spieler {e.Owner} uebergeben, "
                + $"Lager W{e.StockW} F{e.StockF} S{e.StockS}, "
                + $"Hangar {e.Hangar?.Count ?? 0}/{Mathf.Max(1, e.HangarSize)}\n");

        // ---- 1) die Anzeige --------------------------------------------------
        bool panelAn = BuildPanelWanted;
        var fart = FensterArtVon(e);
        sb.Append($"  1. unser Baufenster: {(panelAn ? "AN — FALSCH" : "aus (richtig)")}; "
                + $"Originalfenster: {(fart == null ? "KEINES — FALSCH" : $"Art {fart} (richtig)")}\n");

        // ---- 2) die Logik dahinter ------------------------------------------
        var prod = Producer();
        int zeilen = BuildPanelRows().Count;
        sb.Append($"  2. Logik dahinter: Producer {(prod != null ? "da" : "WEG — FALSCH")}, "
                + $"{zeilen} Bauzeilen\n");

        // ---- 3) der Kaufweg im Originalfenster -------------------------------
        var st = BuildingWindowData();
        int angebote = st?.Angebote.Count ?? 0;
        int hangarVor = e.Hangar?.Count ?? 0;
        int teileVor = e.StockW + e.StockF + e.StockS;
        bool gekauft = false;
        string kaufwort = "kein Angebot im Fenster";
        if (st != null && angebote > 0)
        {
            var ang = st.Angebote[0];
            if (ang.Kaufen == null) kaufwort = "Angebot ohne Kaufweg";
            else if (!ang.Bezahlbar) kaufwort = $"»{ang.Name}« nicht bezahlbar ({ang.PreisText})";
            else
            {
                ang.Kaufen();          // ⚠ der ECHTE Knopf, nicht nachgebaut
                int hangarNach = e.Hangar?.Count ?? 0;
                int teileNach = e.StockW + e.StockF + e.StockS;
                gekauft = hangarNach > hangarVor && teileNach < teileVor;
                kaufwort = $"»{ang.Name}« {ang.PreisText} gekauft -> Hangar {hangarVor}->{hangarNach}, "
                         + $"Teile {teileVor}->{teileNach}  »{_order}«";
            }
        }
        sb.Append($"  3. Kaufweg im Originalfenster: {angebote} Angebot(e), "
                + $"{(gekauft ? "KAUF GELUNGEN" : "KAUF MISSLUNGEN")} — {kaufwort}\n");

        // ---- 4) und der Start aus dem Hangar ---------------------------------
        int vorStart = e.Hangar?.Count ?? 0;
        LaunchAircraft(e.Owner);
        int nachStart = e.Hangar?.Count ?? 0;
        bool gestartet = vorStart > 0 && nachStart < vorStart;
        sb.Append($"  4. »Starten« aus dem Hangar: {vorStart} -> {nachStart} "
                + $"{(gestartet ? "(richtig)" : vorStart == 0 ? "— Hangar war leer, UNGEPRUEFT" : "— FALSCH")}\n");

        // ---- Nullmodell: eine BASIS muss unser Fenster weiter aufmachen -------
        int basis = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var b = _entities[i];
            if (!b.IsBuilding || b.IsProp || b.Dead || b.BType != 1) continue;
            if (b.Owner != e.Owner) continue;
            basis = i; break;
        }
        // ⭐⭐ 19.09.2026 — ZEILE 5, UND SIE FEHLTE SIEBEN TAGE.
        //
        // Am 12.09. galt dieser Prüfstand als gruen und der Fall als erledigt.
        // Am 19.09. meldete er: »wenn ich auf den flughafen anwaehle kommt
        // irgendein baue von uns, statt das originale« — und er hatte recht.
        // Die vier Zeilen darueber messen, dass EIN Fenster aufgeht und dass
        // man darin kaufen kann. Keine davon misst, WIE es aussieht.
        //
        // ⚠ Was hier gemessen wird, ist die VORBEDINGUNG, nicht die Zeichnung:
        // ein kopfloser Lauf malt nicht. Gemessen wird also, ob die Kacheln
        // ueberhaupt da sind (ohne --reexport-effects sind sie es nicht) und ob
        // der Gegenschalter aus ist. Faellt eines von beiden weg, faellt das
        // Fenster auf unsere Godot-Moebel zurueck — und das saehe von aussen
        // genauso aus wie ein nicht gebautes Fenster.
        bool kacheln = UI.FlughafenView.Usable;
        bool schalterAus = !UI.BuildingWindow.FlughafenfensterAlt;
        bool moebelOk = kacheln && schalterAus;
        sb.Append($"  5. Moebel des Originals (Fensterart 5, Zeichner 0x465050): "
                + $"Kacheln {(kacheln ? "da" : "FEHLEN — --reexport-effects laeuft nicht")}, "
                + $"Gegenschalter {(schalterAus ? "aus" : "AN (--flughafenfenster-alt)")} "
                + $"-> {(moebelOk ? "zeichnet das ORIGINAL" : "faellt auf unsere Moebel zurueck")}"
                + "   ⚠ VORBEDINGUNG, nicht die Zeichnung: kopflos wird nicht gemalt\n");

        bool nullOk = true;
        if (basis < 0) sb.Append("  Nullmodell: keine eigene Basis auf der Karte — NICHT GEMESSEN\n");
        else
        {
            _selected = basis;
            nullOk = BuildPanelWanted;
            sb.Append($"  Nullmodell: Basis Platz {_entities[basis].Slot} -> unser Baufenster "
                    + $"{(nullOk ? "an (richtig)" : "AUS — FALSCH")}\n");
        }

        // ---- alles zurueck ---------------------------------------------------
        e.Owner = merkeEigner;
        e.StockW = mw; e.StockF = mf; e.StockS = ms;
        e.MenuIndex = merkeMenu;
        _selected = merkeAuswahl;

        bool alles = !panelAn && fart != null && prod != null && zeilen > 0
                  && angebote > 0 && gekauft && nullOk && moebelOk;
        sb.Append(alles ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
