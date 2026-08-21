namespace AkteEuropaReborn.Campaign;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>DIE KONTEXTHILFE DER KAMPAGNE</b> — der gemeinsame Vorspann des
/// Originals, Block C <c>0x497540…0x49814D</c> / F <c>0x496E50…0x497A5E</c>.
/// ⭐ <b>C und F stimmen Befehl für Befehl überein</b> (72 Zeilen, 0
/// Unterschiede) — dieser Block ist in beiden Auslieferungen derselbe.
///
/// <para><b>Was es ist:</b> 34 Tore, jedes feuert <b>einmal in der ganzen
/// Kampagne</b> und zeigt dann einen Hilfetext. Kein Missionsskript, kein
/// Auslöser in der Karte.</para>
///
/// <para>⚠ <b>Am 21.08.2026 stand hier, die Tore prüften alle ein Bauteil der
/// angewählten Einheit.</b> Das war nur für 25 von ihnen richtig. Der Block
/// prüft <b>vier verschiedene Dinge</b>:</para>
/// <list type="bullet">
/// <item><c>einheit_feld</c> — ein Satzfeld der ANGEWÄHLTEN Einheit (25 Tore)</item>
/// <item><c>gebaeude_vorhanden</c> — der Spieler HAT ein Gebäude dieses Typs (4)</item>
/// <item><c>fenster_geoeffnet</c> — dieses Fenster wurde gerade geöffnet (3)</item>
/// <item><c>flughafen_platz_belegt</c> — ein Flugzeug steht auf Platz 0 (1)</item>
/// </list>
///
/// <para>⭐ <b>Die Missionsschranke ist gemessen, nicht gedeutet:</b> der ganze
/// Block läuft nur bei <c>word[C 0x539934] &lt; 50</c>
/// (<c>cmp word[…],0x32; jge Ende</c> @C `0x497643`). »Nur in der Kampagne« ist
/// damit eine Zahl.</para>
///
/// <para>⚠ <b>DREI BERICHTIGUNGEN AN DEN GEBAUTEN 25</b>, gefunden beim
/// vollständigen Lesen — der erste Ausleser hatte Vorbedingungen verschluckt:
/// <b>15 Tore</b> (v[347], v[356…369]) verlangen zusätzlich <c>ZBRAN == 0</c>,
/// zeigen also <b>nur bei unbewaffneten Einheiten</b>; <b>v[371]</b> ist kein
/// Oder, sondern <c>+0x0D != 19</c> UND <c>+0x0F == 172</c>; und <b>vier
/// Tore</b> zeigen bei <b>70/150</b> statt 100/200.</para>
///
/// <para>⚠ <b>UNSERE Umsetzung des Übertrags:</b> statt v[300…499] über den
/// Missionswechsel zu verschleppen (<c>0x4CFD80</c> nullt nur v[0…299]), führen
/// wir die gefeuerten Tore als eigene kampagnenweite Menge, die in den
/// Spielstand geht. Gleichwertig, weil die 34 Tore im Original die einzigen
/// Leser und Schreiber dieser Variablen sind.</para>
/// </summary>
public static class CampaignHints
{
    /// <summary>⭐ Der ganze Block läuft nur unterhalb dieser Missionsnummer —
    /// gelesen, nicht gesetzt (@C 0x497643).</summary>
    public const int MissionSchranke = 50;

    /// <summary>Was ein Tor prüft.</summary>
    public enum Art
    {
        /// <summary>Ein Satzfeld der angewählten Einheit.</summary>
        EinheitFeld,
        /// <summary>Der Spieler besitzt ein Gebäude dieses Typs.</summary>
        GebaeudeVorhanden,
        /// <summary>Dieses Fenster wurde gerade geöffnet.</summary>
        FensterGeoeffnet,
        /// <summary>Ein Flugzeug steht auf Stellplatz 0 eines eigenen Flughafens.</summary>
        FlughafenPlatzBelegt,
        /// <summary>⚠ Nicht auswertbar — der Grund steht in <see cref="Tor.Grund"/>.</summary>
        Ungebaut,
    }

    /// <summary>Ein Tor.</summary>
    public sealed class Tor
    {
        public int Var;                 // v[n] des Originals — unser Merkerschlüssel
        public Art Was = Art.Ungebaut;
        public int Text = -1;           // die HELPG-Nummer
        public int X = 100, Y = 200;    // ⚠ NICHT überall gleich

        // --- einheit_feld ---------------------------------------------------
        public int Feld = -1;           // Versatz im Einheitensatz
        public int[] Werte = System.Array.Empty<int>();
        /// <summary>⚠ Die verschluckte Vorbedingung: ein ZWEITES Feld, das
        /// stimmen muss, bevor das Tor überhaupt prüft.</summary>
        public int VorFeld = -1;
        public int VorWert;
        public bool VorUngleich;        // true = das Feld darf diesen Wert NICHT haben

        // --- gebaeude_vorhanden ---------------------------------------------
        public int MissionGroesser = -1;
        public int[] GebaeudeTypen = System.Array.Empty<int>();

        // --- fenster / flughafen --------------------------------------------
        public int FensterArt = -1;
        public int VoraussetzungVar = -1;   // dieses Tor muss schon gefeuert haben

        /// <summary>Warum das Tor nicht auswertbar ist — ⚠ wird GEZÄHLT und
        /// gedruckt, nicht verschwiegen.</summary>
        public string? Grund;
    }

    private static readonly List<Tor> _tore = new();
    private static bool _geladen;

    public static IReadOnlyList<Tor> Tore { get { Laden(); return _tore; } }

    /// <summary>Wie viele Tore die Datei führt, wie viele auswertbar sind und
    /// wie viele nicht — ⚠ ohne diese Zahlen ist »es erscheint nichts« nicht von
    /// »die Datei ist leer« zu unterscheiden.</summary>
    public static int ToreInDatei { get; private set; }
    public static int Auswertbar { get; private set; }
    public static int NichtAuswertbar { get; private set; }

    /// <summary>Welche Tore schon gefeuert haben. <b>Kampagnenweit</b> — sie
    /// überlebt den Missionswechsel und geht in den Spielstand.</summary>
    private static readonly HashSet<int> _gezeigt = new();

    public static int GezeigtCount => _gezeigt.Count;
    public static IEnumerable<int> Gezeigt => _gezeigt;
    public static bool Hat(int varNr) => _gezeigt.Contains(varNr);
    public static void Vergiss() => _gezeigt.Clear();
    public static void Merke(int varNr) => _gezeigt.Add(varNr);
    public static void Gefeuert(Tor t) => _gezeigt.Add(t.Var);

    /// <summary>
    /// ⭐ <b>Das Ereignisbyte</b> — im Original <c>byte[C 0x539930]</c>, die Art
    /// des zuletzt geöffneten Fensters. Der Schreiber C <c>0x441270</c> setzt
    /// <c>al = byte[44324·Fenster + 0x8B9038]</c> (ausser Art 3, der Karte).
    ///
    /// <para>⚠ Es ist ein EREIGNIS, kein Zustand: die drei Fenstertore setzen es
    /// nach dem Feuern auf 0 zurück. Wer es als »Fenster ist offen« liest, baut
    /// ein Tor, das nach dem Schliessen weiterfeuert.</para></summary>
    public static int Ereignis { get; set; }

    private static void Laden()
    {
        if (_geladen) return;
        _geladen = true;
        string pfad = Core.Content.Path("campaign_hints_raw.json");
        if (!FileAccess.FileExists(pfad)) pfad = "res://Data/campaign_hints_raw.json";
        if (!FileAccess.FileExists(pfad))
        {
            GD.Print("kontexthilfe: campaign_hints_raw.json fehlt — keine Tore");
            return;
        }
        using var f = FileAccess.Open(pfad, FileAccess.ModeFlags.Read);
        if (f == null) return;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok ||
            json.Data.VariantType != Variant.Type.Dictionary) return;
        var root = json.Data.AsGodotDictionary<string, Variant>();
        if (!root.TryGetValue("tore", out var tv) || tv.VariantType != Variant.Type.Array) return;

        foreach (var item in tv.AsGodotArray())
        {
            if (item.VariantType != Variant.Type.Dictionary) continue;
            var d = item.AsGodotDictionary<string, Variant>();
            ToreInDatei++;

            int Ganz(string k, int fehlt = -1)
                => d.TryGetValue(k, out var v) && v.VariantType != Variant.Type.Nil
                   ? v.AsInt32() : fehlt;
            int[] Liste(string k)
            {
                if (!d.TryGetValue(k, out var v) || v.VariantType != Variant.Type.Array)
                    return System.Array.Empty<int>();
                var l = new List<int>();
                foreach (var w in v.AsGodotArray()) l.Add(w.AsInt32());
                return l.ToArray();
            }

            var t = new Tor
            {
                Var = Ganz("var"),
                Text = Ganz("text"),
                X = Ganz("x", 100),
                Y = Ganz("y", 200),
            };

            // ⚠ Ein Tor, das die Datei ausdrücklich als strittig oder ungebaut
            // ausweist, wird NICHT ausgewertet — und der Grund wird
            // mitgeführt, damit der Prüfstand ihn drucken kann.
            if (d.TryGetValue("_strittig", out var sv)) t.Grund = sv.AsString();
            else if (d.TryGetValue("_ungebaut", out var uv)) t.Grund = uv.AsString();

            string art = d.TryGetValue("art", out var av) ? av.AsString() : "";
            if (t.Grund == null)
            {
                switch (art)
                {
                    case "einheit_feld":
                        t.Was = Art.EinheitFeld;
                        t.Feld = Ganz("feld");
                        t.Werte = Liste("werte");
                        if (d.TryGetValue("vorbedingung", out var vbv)
                            && vbv.VariantType == Variant.Type.Dictionary)
                        {
                            var vb = vbv.AsGodotDictionary<string, Variant>();
                            t.VorFeld = vb.TryGetValue("feld", out var vf) ? vf.AsInt32() : -1;
                            if (vb.TryGetValue("gleich", out var g)) t.VorWert = g.AsInt32();
                            else if (vb.TryGetValue("ungleich", out var ug))
                            { t.VorWert = ug.AsInt32(); t.VorUngleich = true; }
                        }
                        if (t.Feld < 0 || t.Werte.Length == 0)
                        { t.Was = Art.Ungebaut; t.Grund = "kein Feld oder kein Wert"; }
                        break;

                    case "gebaeude_vorhanden":
                        t.Was = Art.GebaeudeVorhanden;
                        t.MissionGroesser = Ganz("mission_groesser");
                        t.GebaeudeTypen = Liste("gebaeudetypen");
                        if (t.GebaeudeTypen.Length == 0)
                        { t.Was = Art.Ungebaut; t.Grund = "kein Gebaeudetyp"; }
                        break;

                    case "fenster_geoeffnet":
                        t.Was = Art.FensterGeoeffnet;
                        t.FensterArt = Ganz("fensterart");
                        break;

                    case "flughafen_platz_belegt":
                        t.Was = Art.FlughafenPlatzBelegt;
                        t.VoraussetzungVar = Ganz("voraussetzung_var");
                        break;

                    default:
                        t.Grund = $"unbekannte Art »{art}«";
                        break;
                }
            }

            if (t.Was == Art.Ungebaut) NichtAuswertbar++; else Auswertbar++;
            _tore.Add(t);
        }
        GD.Print($"kontexthilfe: {ToreInDatei} Tore, {Auswertbar} auswertbar, "
                 + $"{NichtAuswertbar} nicht (siehe OFFENE_FRAGEN.md Abschnitt AE)");
    }

    /// <summary>Was der Prüfer über die angewählte Einheit weiss. −1 heisst
    /// »nichts angewählt« — dann fallen alle <see cref="Art.EinheitFeld"/>-Tore
    /// aus, wie im Original bei <c>word[0x4FA0C8] &gt;= 0x1F40</c>.</summary>
    public readonly struct Auswahl
    {
        public readonly int Zbran, TopSpec, LEngine, Ausruestung, RobProd;
        public Auswahl(int zbran, int topSpec, int lEngine, int ausruestung, int robProd)
        {
            Zbran = zbran; TopSpec = topSpec; LEngine = lEngine;
            Ausruestung = ausruestung; RobProd = robProd;
        }
        public static Auswahl Keine => new(-1, -1, -1, -1, -1);
        public bool Leer => Zbran < 0 && TopSpec < 0 && LEngine < 0 && Ausruestung < 0;

        public int this[int feld] => feld switch
        {
            0x0D => Zbran, 0x0E => TopSpec, 0x0F => LEngine, 0x10 => Ausruestung,
            // ⭐ +0x43 ist der ENTWURFSPLATZ (»rob_prod«) — das Spiel nennt ihn
            // selbst so: `create_unit` @C 0x4B34E0 meldet »WRONG ROB_PROD in
            // PLACE!!!!«, wenn die Zahl auf einen leeren Entwurf zeigt, und
            // schreibt elf Befehle später genau diese Zahl nach +0x43.
            //
            // ⚠ Der Name »Missionsmarke« für dasselbe Byte war eine
            // Fehlbenennung, KEIN Fehlgriff: `find_unit` @C 0x4D0F20 vergleicht
            // denselben Versatz, unser `find_unit_with_mark` ist also richtig
            // gebaut. Nur der Kommentar hiess falsch.
            //
            // ⚠ Die Tafel hängt an der GATTUNG: Land → sec47 (200 Plätze je
            // Spieler), Schiff → 0x52EDA0 (nur 10). Für das eine Tor, das dieses
            // Feld prüft, ist das folgenlos — 55 ist als Schiffsplatz nicht
            // darstellbar.
            0x43 => RobProd,
            _ => int.MinValue,
        };
    }

    /// <summary>Was der Prüfer über die Welt weiss.</summary>
    public delegate bool HatGebaeude(int typ, int besitzer);
    /// <summary>Steht ein Flugzeug auf Stellplatz 0 eines eigenen Flughafens?
    /// ⚠ Das Original liest NUR Platz 0, nicht »irgendeinen Platz«.</summary>
    public delegate bool FlughafenBelegt();

    /// <summary>
    /// <b>Welches Tor feuert jetzt</b> — oder <c>null</c>.
    ///
    /// <para>⚠ <b>Das erste passende Tor gewinnt</b>, in der Reihenfolge der
    /// Datei; das ist die Reihenfolge des Originalblocks. Dort steht ein Tor
    /// hinter dem anderen, und das erste, dessen Bedingung greift, zeigt seinen
    /// Text — die späteren kommen im selben Durchgang nicht mehr dran, weil das
    /// Fenster das Spiel anhält.</para>
    ///
    /// <para>⚠ Die Reihenfolge im Block ist NICHT die der Variablennummern:
    /// v[347] steht zwischen v[358] und v[359], v[346] zwischen v[367] und
    /// v[368]. Beide sind nachträglich eingeschoben — und genau sie sind die
    /// zwei Ausreisser der Probe <c>Text = v − 300</c>.</para></summary>
    public static Tor? Passend(int mission, in Auswahl a,
                               HatGebaeude? hatGebaeude = null,
                               FlughafenBelegt? flughafen = null)
    {
        Laden();
        // ⭐ Die Missionsschranke des Originals, @C 0x497643.
        if (mission <= 0 || mission >= MissionSchranke) return null;

        foreach (var t in _tore)
        {
            if (_gezeigt.Contains(t.Var)) continue;
            switch (t.Was)
            {
                case Art.EinheitFeld:
                    if (a.Leer) continue;
                    // ⚠ Erst die Vorbedingung — sie ist bei 16 der 25 Tore da,
                    // und der erste Ausleser hatte sie verschluckt.
                    if (t.VorFeld >= 0)
                    {
                        int vw = a[t.VorFeld];
                        if (vw == int.MinValue) continue;
                        if (t.VorUngleich ? vw == t.VorWert : vw != t.VorWert) continue;
                    }
                    int wert = a[t.Feld];
                    if (wert == int.MinValue) continue;
                    foreach (int w in t.Werte) if (w == wert) return t;
                    continue;

                case Art.GebaeudeVorhanden:
                    if (hatGebaeude == null) continue;
                    if (mission <= t.MissionGroesser) continue;
                    foreach (int typ in t.GebaeudeTypen)
                        if (hatGebaeude(typ, 0)) return t;
                    continue;

                case Art.FensterGeoeffnet:
                    if (Ereignis != t.FensterArt) continue;
                    return t;

                case Art.FlughafenPlatzBelegt:
                    // ⚠ Das Tor verlangt, dass v[375] (»du hast einen
                    // Flughafen«) SCHON gefeuert hat — erst der Hinweis auf das
                    // Gebäude, dann der auf das Flugzeug darin.
                    if (t.VoraussetzungVar >= 0 && !_gezeigt.Contains(t.VoraussetzungVar))
                        continue;
                    if (flughafen == null || !flughafen()) continue;
                    return t;
            }
        }
        return null;
    }
}
