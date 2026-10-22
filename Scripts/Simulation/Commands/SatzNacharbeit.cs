namespace AkteEuropaReborn.Rendering;

using Godot;
using AkteEuropaReborn.Simulation.Commands;

/// <summary>
/// ⭐⭐ <b>DIE NACHARBEIT NACH DEM SATZ</b> (bug-587/588/599, 22.10.2026,
/// <c>berichte/ring-rueckfaelle-opus.md</c> §5 Schritt 2).
///
/// <para><b>Warum es diese Stelle gibt.</b> Vor den Desync-Paketen (8621573,
/// 7e6e4a7) hat ein Knopf GEBUCHT und danach NACHGEARBEITET — neu gemalt,
/// gemeldet, ins Wertebuch eingetragen. Seitdem STELLT der Knopf nur einen Satz
/// ein, und gebucht wird einen Takt später in <c>Apply*</c>. Was beim Aufrufer
/// stand, lief damit VOR der Buchung (das Fenster malte den alten Stand), und was
/// im alten Rumpf HINTER der Abzweigung zum Ring stand, lief gar nicht mehr
/// (505: <c>UnitStatBook.Eintragen</c>). Hier steht alles davon an EINER Stelle,
/// unmittelbar hinter <see cref="ApplyCommandInner"/>.</para>
///
/// <para>⚠ <b>UNSERE SETZUNG</b>: das Original hat keine solche gemeinsame Stelle
/// — sein Fenster wird vom Fenstertakt <c>0x4505F0</c> (Arm <c>0x45066C</c>) alle
/// 30 Bilder neu gemalt (siehe <c>MapViewer.FensterNeumalenTakt</c>), und die
/// Werteliste liest sec47 bei jedem Bild live (<c>0x46925D</c>). Die Nacharbeit
/// macht beides SOFORT, also höchstens früher als das Original, nie später.</para>
///
/// <para>⚠⚠ <b>Gleichschritt.</b> Alles hier ist ANZEIGE: Neumalen, Auftragszeile,
/// Klang, Wertebuch (ein Anzeigebuch des Basisfensters, nicht der Spielzustand).
/// Nichts davon fasst Einheiten, Geld oder Würfel an — die Prüfsumme muss mit und
/// ohne <c>--satznacharbeit-alt</c> gleich sein.</para>
///
/// <para>Gegenschalter <c>--satznacharbeit-alt</c> (alles aus, Stand 0.6.10) oder
/// <c>--satznacharbeit-alt=fenster,katalog,meldung</c> je Teil.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Unterwahl von <c>--satznacharbeit-alt</c>: leer = keine, »*« = alle.</summary>
    public static string SatzNacharbeitAlt = "";

    /// <summary>Ist dieser Teil der Nacharbeit abgeschaltet?</summary>
    public static bool NacharbeitAlt(string teil)
        => SatzNacharbeitAlt == "*" || ("," + SatzNacharbeitAlt + ",").Contains("," + teil + ",");

    /// <summary>Das Gebäudefenster neu malen, wenn es offen ist (MapViewer). Das
    /// Argument ist die Befehlsnummer — nur für die Mitschrift.</summary>
    public System.Action<short>? OnGebaeudefensterNeu;

    /// <summary>Wie oft die Nacharbeit ein Neumalen angestossen hat (Prüfstand).</summary>
    public int NacharbeitNeumalen, NacharbeitKatalog, NacharbeitMeldung;

    /// <summary>Kennung im Satz 530 (P4): der Kauf kam aus dem BASISFENSTER (Zeile
    /// des Markts), nicht aus dem Fenster Art 33. Nur dieser Weg meldete früher
    /// (<see cref="MarketBuy"/>); das Art-33-Fenster bleibt stumm wie im Original
    /// (<c>0x4C138A</c>).</summary>
    public const short MarktAusBasisfenster = 1;

    /// <summary>Was der letzte Satz 530 im Kampagnenzweig tat — nur Anzeige.
    /// 0 = nichts/unbekannt, 1 = gekauft, 2 = Geld fehlt, 3 = still (Preis &lt; 1).</summary>
    private int _marktKaufErgebnis;
    private int _marktKaufPreis;

    /// <summary>Die Zielwahl eines Klicks (bug-599): wie viele Sätze 502 noch
    /// ausstehen, wie viele davon wirklich gestartet sind, und das Wort fürs Ziel.
    /// Gesetzt vom Absender (<see cref="ZielwahlKlick"/>), gezählt hier.</summary>
    private int _zielwahlOffen, _zielwahlGestartet;
    private string _zielwahlWas = "";

    /// <summary>Welche Sätze das Gebäudefenster verändern können. ⚠ Lieber zu
    /// viele als zu wenige: ein überflüssiges Neumalen kostet nichts (es liest nur),
    /// ein fehlendes ist genau der Fehler bug-587.</summary>
    private static bool FensterSatz(short op)
        => op is >= CommandOp.Special500 and <= CommandOp.BuildLast
           || op == CommandOp.OursLaunchAll || op == OpStaffelmarke || op == OpPanelzeile;

    /// <summary>Die Nacharbeit — aus <see cref="ApplyCommand"/>, für JEDEN Satz,
    /// angenommen oder nicht (<paramref name="ok"/>).</summary>
    private void SatzNacharbeit(in CommandRecord c, bool ok)
    {
        if (SatzNacharbeitAlt == "*") return;
        bool eigen = c.Player == ViewPlayer;

        // ---- Fenster (bug-587): das offene Gebäudefenster zeigt den neuen Stand.
        // Vorbild: Bahnschalter 500 (BahnschalterGeaendert) und Marktkauf 530
        // (OnMarktfensterNeu, Original 0x451370). Auch ein Satz des NETZPARTNERS
        // malt neu — das Fenster zeigt den gemeinsamen Zustand.
        if (!NacharbeitAlt("fenster") && FensterSatz(c.Op) && OnGebaeudefensterNeu != null)
        {
            NacharbeitNeumalen++;
            OnGebaeudefensterNeu(c.Op);
        }

        // ---- Katalog (bug-588): der frische Entwurf ins Wertebuch. Original 505
        // @0x4B2510 rechnet den Satz sofort (0x4B25A5 → 0x4B1FB0). NUR bei ok —
        // ein abgewiesener Doppelname gehört nicht ins Buch.
        if (ok && c.Op == CommandOp.EntwurfAnlegen && !NacharbeitAlt("katalog"))
        {
            string name = NameEntpacken(c, 5, 7);
            // ApplyEntwurf: P1 Waffe, P2 Ausrüstung, P3 Fahrwerk.
            UI.UnitStatBook.Eintragen(name, c.P1, c.P3, c.P2);
            NacharbeitKatalog++;
        }

        if (NacharbeitAlt("meldung") || !eigen) return;

        // ---- Meldung (bug-599) --------------------------------------------
        switch (c.Op)
        {
            case CommandOp.MarktKauf when c.P4 == MarktAusBasisfenster && TradeLikeOriginal:
            {
                // Der alte Direktweg MarketBuy schrieb die Auftragszeile; der
                // Kampagnenzweig des Behandlers ist (wie 0x4C138A) stumm. Die Zeile
                // gehört dem BASISFENSTER-Weg, also kommt sie hierher.
                if (_marktKaufErgebnis == 2)
                {
                    _order = "Sie haben leider nicht genug Geld.";       // 0x4FBDD0, wie MarketBuy
                    Audio.GameSounds.Play(Audio.GameSounds.Refused);
                    NacharbeitMeldung++;
                }
                else if (_marktKaufErgebnis == 1)
                {
                    var markt = MarktVonPlatz(c.P3);
                    _order = markt == null
                        ? $"gekauft fuer ${_marktKaufPreis}, Kontostand ${Money(c.Player)}"
                        : $"gekauft fuer ${_marktKaufPreis} — Lieferung an "
                          + $"{BuildingTypeName(markt.BType)} ({markt.Col},{markt.Row}), Kontostand ${Money(c.Player)}";
                    NacharbeitMeldung++;
                }
                _marktKaufErgebnis = 0;
                UpdatePanel();
                break;
            }
            case CommandOp.Flugzeugstart when c.P2 is ZielModusZiel or ZielModusZelle:
            {
                // Die Zielwahl zählt GESTARTETE Maschinen, nicht abgesetzte Sätze —
                // so zählte der Direktweg (StaffelStarten). Abgewiesene Sätze
                // (Heimatflughafen weg, Absturz) zählen nicht.
                if (_zielwahlOffen <= 0) break;
                _zielwahlOffen--;
                if (ok) { _zielwahlGestartet++; ZielwahlMaschinen++; }
                if (_zielwahlGestartet > 0)
                    _order = $"{_zielwahlGestartet} Maschinen greifen {_zielwahlWas} an";
                else if (_zielwahlOffen == 0)
                    _order = "keine Maschine konnte starten";
                NacharbeitMeldung++;
                UpdatePanel();
                break;
            }
            case CommandOp.OursResearch when !ok && ForschungWaechterSchlug(c):
            {
                // ⚠ UNSERE SETZUNG (Text): den Wächterfall gibt es nur bei uns
                // (die Liste hat sich zwischen Klick und Takt verschoben). Der
                // Direktweg kannte ihn nicht; stumm verpuffen darf der Klick nicht.
                // Klang wie die übrigen Absagen der Forschung: Refused (140).
                _order = "Forschung — das Angebot hat sich geaendert, bitte neu waehlen.";
                Audio.GameSounds.Play(Audio.GameSounds.Refused);
                NacharbeitMeldung++;
                UpdatePanel();
                break;
            }
        }
    }

    /// <summary>Schlug bei 2005 der WÄCHTER an (Zeile weg oder Preis anders)? Nur
    /// lesen — dieselbe Rechnung wie <see cref="ApplyForschung"/>. Eine Absage von
    /// <c>ForschungKaufen</c> selbst (Geld) meldet schon dort.</summary>
    private bool ForschungWaechterSchlug(in CommandRecord c)
    {
        if (ForschungAlt) return false;
        int i = c.P1;
        if (i < 0 || i >= _entities.Count) return false;
        var e = _entities[i];
        if (!e.IsBuilding || e.BType != 1 || e.Dead) return false;
        var angebot = ForschungAngebote(Mathf.Clamp(e.Owner, 0, 7));
        if (c.P2 < 0 || c.P2 >= angebot.Count) return true;
        return Mathf.Clamp(angebot[c.P2].Preis, short.MinValue, short.MaxValue) != c.P3;
    }
}
