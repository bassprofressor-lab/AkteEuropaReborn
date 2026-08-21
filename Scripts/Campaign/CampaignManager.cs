namespace AkteEuropaReborn.Campaign;

using System.Collections.Generic;
using Godot;

/// <summary>
/// The campaign: which missions there are, which one comes next, and how far
/// the player has got.
///
/// The mission list is derived at import time (see
/// <c>Import.CatalogueExporter.WriteCampaign</c>) and the order is the level
/// file numbering — which is the game's own numbering, not a guess: the saved
/// game `1.DM` is called "Mission 26" and its elevation grid is level 26's.
///
/// In <c>user://campaign.cfg</c> stehen ZWEI Zahlen: die hoechste geschaffte
/// Mission und der KONTOSTAND. Der Kontostand geht von einer Mission in die
/// naechste mit — das ist gelesen, siehe <see cref="Balance"/>. Alles andere
/// bringt der Levelsatz selbst mit.
/// </summary>
public static class CampaignManager
{
    public sealed class Mission
    {
        public int Index;
        public string Map = "", Title = "";
        public int Width, Height, Tileset;
        public string Label => $"{Index:00} — {(Title.Length > 0 ? Title : Map)}";
    }

    private static List<Mission>? _missions;

    /// <summary>The missions in order; empty when nothing has been imported.</summary>
    public static IReadOnlyList<Mission> Missions => _missions ??= Load();

    /// <summary>Forget the cached list — used after an import.</summary>
    public static void Forget() => _missions = null;

    public const string SavePath = "user://campaign.cfg";

    /// <summary>
    /// ⚠ <b>Warum hier `using` steht und NICHT ein gehaltenes Abbild wie in
    /// <see cref="UI.Settings"/></b> — 13.08.2026, und der Unterschied ist
    /// wichtig genug für einen eigenen Absatz.
    ///
    /// <para><c>ConfigFile</c> ist ein <c>RefCounted</c>; ein nicht freigegebenes
    /// stirbt beim Herunterfahren im Finalizer. Genau das hat heute Prüfläufe mit
    /// <c>0xC0000005</c> beenden lassen (Rückgabewerte 139 und 132 im Wechsel),
    /// und in <c>Settings</c> war die Antwort ein einziges, dauerhaft gehaltenes
    /// Abbild — dort schreibt nur <c>Set()</c>, das Abbild kann also nicht
    /// veralten.</para>
    ///
    /// <para><b>Hier wäre dasselbe ein Fehler.</b> Der Kampagnenstand wird von
    /// AUSSEN weggeräumt: <c>--fresh-campaign</c> setzt ihn zurück, damit ein
    /// Prüflauf nicht den Fortschritt des vorigen mitschleppt (der Grund dafür
    /// ist gemessen — M23 meldete <c>$47465</c> und M5 <c>$970</c> statt
    /// <c>$470</c>, weil der Stand aus früheren Läufen dastand). Ein gehaltenes
    /// Abbild würde das nicht mitbekommen und den alten Fortschritt weiter
    /// behaupten. <c>using</c> gibt das Objekt frei, ohne den Wert zu merken —
    /// das Leseverhalten bleibt <b>Byte für Byte dasselbe wie vorher</b>, nur
    /// ohne das Leck.</para>
    ///
    /// <para>Wer das hier später zu einem Abbild „verbessert", bricht damit
    /// <c>--fresh-campaign</c> und jede Messung, die darauf beruht.</para>
    /// </summary>

    /// <summary>The highest mission the player has finished; 0 at the start,
    /// so the next one is the first.</summary>
    public static int Completed
    {
        get
        {
            using var c = new ConfigFile();
            return c.Load(SavePath) == Error.Ok
                ? (int)c.GetValue("campaign", "completed", 0) : 0;
        }
        set
        {
            using var c = new ConfigFile();
            c.Load(SavePath);
            c.SetValue("campaign", "completed", value);
            c.Save(SavePath);
        }
    }

    /// <summary>The mission to play next, or null once the campaign is over.
    /// A mission the imported content does not have is skipped instead of
    /// blocking the rest — someone with only disc 1 gets 1 to 15.</summary>
    public static Mission? Next()
    {
        int done = Completed;
        foreach (var m in Missions)
            if (m.Index > done) return m;
        return null;
    }

    public static Mission? ByIndex(int index)
    {
        foreach (var m in Missions)
            if (m.Index == index) return m;
        return null;
    }

    /// <summary>Record a mission as finished. Only ever moves forward, so
    /// replaying an early one does not throw the progress away.</summary>
    public static void Finished(int index)
    {
        if (index > Completed) Completed = index;
    }

    /// <summary>
    /// Der KONTOSTAND, der von einer Mission in die naechste mitgeht.
    ///
    /// <para>⚠ 11.08.2026 — bis heute stand im Kopf dieser Datei »nothing else
    /// carries over«. Das ist widerlegt. Gefragt hatte der Spieler: »In
    /// Kampagne 2 habe ich kein Geld. Liegt das daran, dass ich die 3 Schiffe
    /// in Kampagne 1 nicht zerstoert habe?«</para>
    ///
    /// <para><b>Was gelesen wurde.</b> Der Kontostand liegt bei 0xA9C600
    /// (acht i32, <c>get_money(spieler)</c> @0x4CF5E0 liest
    /// <c>dword[0xA9C600 + 4*spieler]</c>). Alle 49 Verweise darauf wurden
    /// durchgesehen. Er wird an genau drei Stellen VERAENDERT, und alle drei
    /// ADDIEREN:</para>
    /// <list type="bullet">
    /// <item>@0x4169E3: <c>mov ecx,[0xA9C600]; add ecx,[0xA9A1D8]; mov
    /// [0xA9C600],ecx</c> — am Missionsende wird die MISSIONSBEZAHLUNG
    /// (0xA9A1D8) auf den Kontostand aufgeschlagen, nicht eingesetzt.</item>
    /// <item>@0x4139B8: <c>add ecx, 0xD05</c> — eine Zugabe.</item>
    /// <item>@0x416AF7: <c>add dword [0xA9C600], eax</c>.</item>
    /// <item>⚠ NACHGETRAGEN 11.08.2026: eine VIERTE Stelle wurde uebersehen.
    /// @0x41ABC3 <c>mov [eax*4+0xA9C600], edx</c> SETZT das Konto, statt zu
    /// addieren — in einer Schleife ueber alle acht Spieler, mit
    /// <c>word[0x5407A0]</c>. Das ist der GEFECHTSaufbau, nicht der
    /// Kampagnenaufbau; die Begruendung steht unten beim Anfangsstand. Fuer den
    /// Kampagnenweg bleibt es also dabei: dort wird nur addiert.</item>
    /// </list>
    ///
    /// <para>Es gibt keinen <c>mov dword [0xA9C600], &lt;zahl&gt;</c> mit einer
    /// KONSTANTEN und kein Loeschen des Bereichs — gesucht wurde nach der
    /// Bytefolge <c>C7 05 00 C6 A9 00</c> (0 Treffer) und nach jedem
    /// <c>push 0xA9C600</c> (2 Treffer). Diese zwei sind
    /// <c>fwrite(0xA9C600, 1, 0x20, f)</c> @0x41D8DF und
    /// <c>fread(0xA9C600, 1, 0x20, f)</c> @0x41E95A — <b>Spielstand</b>, nicht
    /// Missionsstart. Und die Kampagnen-Levelsaetze bringen ihn auch nicht mit:
    /// von 43 eingelesenen Karten tragen nur die drei .DM-SPIELSTAENDE einen
    /// sec73 (map_DM_4: Spieler 0 = 44850$), keine einzige .CWM-Mission.</para>
    ///
    /// <para><b>Also laeuft der Kontostand durch.</b></para>
    ///
    /// <para>⚠ 11.08.2026, ZWEITE Lesung — die Deutung des Fotos oben war
    /// FALSCH herum. Dort steht »Missionsbezahlung $320« und »Kontostand $470«,
    /// und der Text schloss daraus, die 150 seien MITGEBRACHT. Sie sind es
    /// nicht: die 150 sind dreimal 50 fuer die drei Schiffe und werden IN
    /// Mission 1 verdient (mission_scripts.json, Regeln 11/12/13). Die 320 sind
    /// die feste Missionsbezahlung, die am Ende obendrauf kommt — siehe
    /// <see cref="PayFor"/>. Die Rechnung ist 0 + 320 + 150 = 470, nicht
    /// 150 + 320.</para>
    ///
    /// <para><b>Damit ist der ANFANGSSTAND einer neuen Kampagne belegt und
    /// keine Setzung mehr: er ist 0.</b> Zwei unabhaengige Gruende. Erstens die
    /// Rechnung aus dem Foto. Zweitens: der einzige Befehl im Programm, der ein
    /// Spielerkonto SETZT statt addiert, ist <c>mov [eax*4+0xA9C600], edx</c>
    /// @0x41ABC3, und sein <c>edx</c> ist <c>word[0x5407A0]</c>. Das ist die
    /// GEFECHTS-Einstellung »Startgeld«: @0x44D412 haengt ein Menuepunkt daran,
    /// der sie um 0x3E8 (1000) erhoeht und bei 0x2710 (10000) auf 0 umlaufen
    /// laesst, @0x4426CC setzt sie zusammen mit den uebrigen Gefechtsschaltern
    /// (0x54079C, 0x540798, 0x540B94) zurueck. Dieselbe Routine setzt drei
    /// Befehle vorher <c>mov [0xA9A1D8], 0x186A0</c> — 100000 Missionsbezahlung,
    /// eine Zahl, die in keiner Kampagnenmission vorkommt. Das ist der
    /// Gefechtsaufbau, nicht der Kampagnenaufbau.</para>
    /// </summary>
    public static int Balance
    {
        get
        {
            using var c = new ConfigFile();
            return c.Load(SavePath) == Error.Ok
                ? (int)c.GetValue("campaign", "balance", 0) : 0;
        }
        set
        {
            using var c = new ConfigFile();
            c.Load(SavePath);
            c.SetValue("campaign", "balance", value);
            c.Save(SavePath);
        }
    }

    /// <summary>
    /// <b>DER KONTOSTAND, MIT DEM EINE MISSION BEGONNEN HAT.</b>
    ///
    /// <para>⚠⚠ 18.08.2026, gemeldet: »das ist doof, wenn jemand Kampagne 1
    /// immer und immer wieder spielen will, dann hat er ja immer einen
    /// grösseren Kontostand, bloss weil er schon in Wirklichkeit bei
    /// Kampagne 22 ist.«</para>
    ///
    /// <para>Er hat recht, und der Grund ist die Bauart: <see cref="Balance"/>
    /// ist EINE laufende Zahl. Wer Mission 1 zum zweiten Mal spielt, bringt
    /// den Stand von Mission 22 mit — das Original kann das nicht anders
    /// gemeint haben, denn dort ist der Kontostand die Kette der Missionen
    /// <i>dieses</i> Durchlaufs.</para>
    ///
    /// <para>Gemerkt wird darum der Stand <b>beim Beginn jeder Mission</b>.
    /// Wird eine schon geschaffte Mission noch einmal begonnen, gilt wieder
    /// ihr eigener Anfangsstand statt des aktuellen.</para>
    ///
    /// <para>⚠ <b>UNSERE SETZUNG</b>, und sie ist es doppelt: das Original
    /// führt (soweit gelesen) keine solche Tabelle, und ob es Wiederholen
    /// überhaupt vorsieht, ist nicht gelesen — sein Menü kennt nur »Neues
    /// Spiel« und »Spiel laden«. Die Missionsübersicht, aus der man eine
    /// geschaffte Mission erneut wählt, ist ohnehin unsere Zutat
    /// (<see cref="UI.CampaignScreen"/>).</para></summary>
    public static int StartBalanceOf(int mission)
    {
        using var c = new ConfigFile();
        if (c.Load(SavePath) != Error.Ok) return 0;
        var v = c.GetValue("mission_balance", mission.ToString(), -1);
        return (int)v;
    }

    /// <summary>Den Anfangsstand dieser Mission festhalten — beim ERSTEN Mal.
    /// ⚠ Ein zweiter Anlauf darf ihn nicht überschreiben, sonst wäre die
    /// Tabelle nach einem Wiederholungslauf wieder die laufende Zahl.</summary>
    public static void NoteStartBalance(int mission, int value)
    {
        if (mission <= 0 || StartBalanceOf(mission) >= 0) return;
        using var c = new ConfigFile();
        c.Load(SavePath);
        c.SetValue("mission_balance", mission.ToString(), value);
        c.Save(SavePath);
    }

    /// <summary>Womit diese Mission zu beginnen hat: ihr gemerkter
    /// Anfangsstand, sonst der laufende. Und der gemerkte wird dabei
    /// angelegt, falls er fehlt.</summary>
    /// <summary>
    /// <b>EINE MITGENOMMENE EINHEIT.</b> Am Ende einer Mission darf der
    /// Spieler einige seiner Überlebenden in die nächste hinübernehmen; der
    /// Rest wird verkauft.
    ///
    /// <para>⚠ <b>Das Original speichert hier etwas ANDERES als wir</b>, und
    /// der Unterschied ist keine Bequemlichkeit. Es hält in
    /// <c>word[0x9937B8 + 2·i]</c> (20 Plätze, <c>0xFFFF</c> = leer) bloss die
    /// <b>Platznummern</b> der Einheiten — die Sätze selbst bleiben einfach im
    /// Speicher stehen, weil die nächste Karte den 1000er-Block des Menschen
    /// gar nicht belegt. Bei uns wird zwischen zwei Missionen der ganze Stand
    /// neu geladen; eine blosse Platznummer zeigte danach ins Leere. Also
    /// merken wir uns, was nötig ist, um die Einheit wieder herzustellen.</para>
    ///
    /// <para>⚠ <see cref="Energie"/> reist mit: das Original nimmt die Einheit
    /// <b>wie sie ist</b> mit, nicht frisch repariert. Wer eine angeschlagene
    /// Einheit mitnimmt, bekommt eine angeschlagene.</para>
    /// </summary>
    public sealed class CarriedUnit
    {
        public int Design;          // Entwurfsnummer
        public int Energie = 100;   // Restleben in Prozent
        public string Name = "";

        public string Pack() => $"{Design}|{Energie}|{Name.Replace('|', ' ')}";

        public static CarriedUnit? Unpack(string s)
        {
            var t = s.Split('|');
            if (t.Length < 2 || !int.TryParse(t[0], out int d) ||
                !int.TryParse(t[1], out int e)) return null;
            return new CarriedUnit
            { Design = d, Energie = e, Name = t.Length > 2 ? t[2] : "" };
        }
    }

    /// <summary>Wieviele Einheiten das Original höchstens mitnehmen lässt —
    /// die Liste hat <b>20</b> Plätze (<c>0x4421E6</c>: <c>cmp cl, 0x14</c>).
    /// ⚠ Wieviele eine EINZELNE Mission zulässt, sagt das Fenster; gemessen
    /// sind es <see cref="SpotsFor"/> viele, denn mehr kann sie gar nicht
    /// aufstellen.</summary>
    public const int CarrySlots = 20;

    /// <summary>Was aus der letzten Mission mitkommt. Leer, wenn nichts.</summary>
    public static List<CarriedUnit> Carried
    {
        get
        {
            var list = new List<CarriedUnit>();
            using var c = new ConfigFile();
            if (c.Load(SavePath) != Error.Ok) return list;
            var roh = c.GetValue("campaign", "carried", new Godot.Collections.Array());
            if (roh.VariantType != Variant.Type.Array) return list;
            foreach (var v in roh.AsGodotArray())
            {
                var u = CarriedUnit.Unpack(v.AsString());
                if (u != null) list.Add(u);
            }
            return list;
        }
        set
        {
            var arr = new Godot.Collections.Array();
            foreach (var u in value)
            {
                if (arr.Count >= CarrySlots) break;
                arr.Add(u.Pack());
            }
            using var c = new ConfigFile();
            c.Load(SavePath);
            c.SetValue("campaign", "carried", arr);
            c.Save(SavePath);
        }
    }

    /// <summary>Wieviele Skriptvariablen das Original zwischen zwei Missionen
    /// mitnimmt: <b>500 Wörter</b> — <c>rep movsd</c> mit <c>ecx = 0xFA</c>,
    /// also 1000 Byte (C <c>0x4D0410</c> beim Laden, <c>0x4D0130</c> beim
    /// Speichern).</summary>
    public const int VarCount = 500;

    /// <summary>
    /// <b>DIE SKRIPTVARIABLEN DER VORMISSION.</b> Gelesen am 20.08.2026.
    ///
    /// <para>Das Original trägt sie mit, und zwar in beide Richtungen
    /// spiegelbildlich:</para>
    /// <code>
    ///   Speichern 0x4D0126:  esi=0xBC5690  edi=0xBC3DF8  ecx=0xFA  rep movsd
    ///   Laden     0x4D0406:  esi=0xBC3DF8  edi=0xBC5690  ecx=0xFA  rep movsd
    /// </code>
    /// <para><c>0xBC5690</c> ist <c>v[0]</c>; die Variablen sind <b>Wörter</b>.
    /// Gegenprobe: Mission 26 setzt laut Skript <c>init {"101": 1}</c>, und im
    /// Init-Arm steht <c>inc word ptr [0xBC575A]</c> — und
    /// <c>0xBC5690 + 2·101 = 0xBC575A</c>.</para>
    ///
    /// <para><b>Warum das zählt.</b> Die Verstärkungskette von Mission 26 ist
    /// <b>in sich kreisförmig</b>: <c>v[0]</c> wird nur auf 1 gesetzt, wenn es
    /// vorher 3 war, 3 nur über 2, 2 nur über 1. Startet <c>v[0]</c> bei
    /// <b>0</b>, feuert <c>space_in</c> <b>nie</b> — die drei Wellen zu je 17
    /// Einheiten bleiben aus. Mit einem übernommenen <c>v[0]</c> läuft die
    /// Kette sofort an.</para>
    ///
    /// <para>⚠ <b>Auch die »once«-Zähler reisen mit</b>, denn sie liegen im
    /// selben Feld. Eine Regel, die in der Vormission einmal gefeuert hat,
    /// kann damit in der nächsten schon als abgehakt gelten. Das ist <b>kein
    /// Versehen von uns, sondern das Verhalten des Originals</b> — dort ist es
    /// dasselbe Feld und derselbe <c>rep movsd</c>. Wer hier »aufräumt«, baut
    /// eine eigene Mechanik.</para>
    ///
    /// <para>⚠ Gespeichert werden nur die Plätze <b>ungleich null</b>, als
    /// <c>n:wert</c>. Fünfhundert Nullen in die Datei zu schreiben wäre
    /// dasselbe in unleserlich.</para>
    /// </summary>
    public static Dictionary<int, int> CarriedVars
    {
        get
        {
            var d = new Dictionary<int, int>();
            using var c = new ConfigFile();
            if (c.Load(SavePath) != Error.Ok) return d;
            string roh = (string)c.GetValue("campaign", "vars", "");
            foreach (string t in roh.Split(' ', System.StringSplitOptions.RemoveEmptyEntries))
            {
                int k = t.IndexOf(':');
                if (k <= 0) continue;
                if (int.TryParse(t[..k], out int n) && int.TryParse(t[(k + 1)..], out int v)
                    && n >= 0 && n < VarCount) d[n] = v;
            }
            return d;
        }
        set
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in value)
            {
                if (kv.Key < 0 || kv.Key >= VarCount || kv.Value == 0) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(kv.Key).Append(':').Append(kv.Value);
            }
            using var c = new ConfigFile();
            c.Load(SavePath);
            c.SetValue("campaign", "vars", sb.ToString());
            c.Save(SavePath);
        }
    }

    private static Godot.Collections.Dictionary? _spots;

    /// <summary>
    /// <b>WO die mitgenommenen Einheiten stehen</b> — je Mission feste Plätze,
    /// aus beiden <c>GAME.EXE</c> gelesen und in <c>Data/carry_spots.json</c>
    /// abgelegt (108 Plätze, C und F Zeichen für Zeichen gleich).
    ///
    /// <para>⚠ <b>Nicht abgeleitet, sondern gelesen.</b> Die Plätze stehen im
    /// Init-Arm der Mission als Folge von <c>place_carry(col, row, 0)</c>. Für
    /// Mission 26 sind es (200,50) (198,52) (203,52) (194,49) (204,46) — genau
    /// die fünf Stellen um Senja, an denen sie im Original stehen.</para>
    ///
    /// <para>⚠ <b>Missionen 1 bis 11 haben keine</b>, dort gibt es nichts
    /// mitzunehmen. Mission 12 hat drei, alle übrigen fünf. Die Zahl der Plätze
    /// IST damit zugleich die Obergrenze dieser Mission.</para>
    /// </summary>
    public static List<(int Col, int Row)> SpotsFor(int mission)
    {
        var list = new List<(int, int)>();
        if (_spots == null)
        {
            _spots = new Godot.Collections.Dictionary();
            foreach (string pfad in new[] { "res://Data/carry_spots.json",
                                            "user://data/carry_spots.json" })
            {
                if (!FileAccess.FileExists(pfad)) continue;
                using var f = FileAccess.Open(pfad, FileAccess.ModeFlags.Read);
                if (f == null) continue;
                var j = Json.ParseString(f.GetAsText());
                if (j.VariantType != Variant.Type.Dictionary) continue;
                var d = j.AsGodotDictionary();
                if (d.TryGetValue("missionen", out var mv) &&
                    mv.VariantType == Variant.Type.Dictionary)
                { _spots = mv.AsGodotDictionary(); break; }
            }
        }
        if (!_spots.TryGetValue(mission.ToString(), out var av) ||
            av.VariantType != Variant.Type.Array) return list;
        foreach (var p in av.AsGodotArray())
        {
            if (p.VariantType != Variant.Type.Array) continue;
            var q = p.AsGodotArray();
            if (q.Count >= 2) list.Add((q[0].AsInt32(), q[1].AsInt32()));
        }
        return list;
    }

    /// <summary>Was <b>Berkewitz Corp.</b> für eine zurückgelassene Einheit
    /// zahlt: <b>30 %</b> ihres Werts. Gelesen aus <c>0x4C1720</c>, wo über den
    /// 1000er-Block des Menschen summiert wird — <c>esi += 3·Wert / 10</c>,
    /// mit ganzzahliger Division, also abgerundet.</summary>
    public static int SellPrice(int wert) => 3 * wert / 10;

    public static int BalanceForStartOf(int mission)
    {
        int gemerkt = StartBalanceOf(mission);
        if (gemerkt >= 0) return gemerkt;

        // ⚠⚠ KEIN EINTRAG — und dann entscheidet, ob die Mission SCHON
        // GESCHAFFT ist. Das ist der Fall, den die Meldung meint: ein
        // Spielstand aus der Zeit vor dieser Tabelle hat für Mission 1 nichts
        // gemerkt, und der laufende Stand ist der von Mission 22.
        //
        //  * Mission 1 beginnt mit 0. Das ist BELEGT, nicht gesetzt — siehe
        //    den Nachweis bei <see cref="Balance"/> (0 + 320 + 150 = 470 auf
        //    dem Bildschirmfoto des Spielers).
        //  * Eine andere schon geschaffte Mission bekommt die Summe der
        //    Missionsbezahlungen davor. ⚠ Das ist eine REKONSTRUKTION und als
        //    solche ungenau: was IN einer Mission verdient wurde (Mission 1
        //    holt 150 aus drei Schiffen), fehlt darin. Sie ist trotzdem
        //    ungleich näher als der laufende Stand — und sie wird nur einmal
        //    gebraucht, danach steht der Eintrag.
        //  * Eine noch NICHT geschaffte Mission ist der Normalfall: dort ist
        //    der laufende Stand genau richtig.
        int wert;
        if (mission > Completed) wert = Balance;
        else if (mission <= 1) wert = 0;
        else
        {
            wert = 0;
            for (int m = 1; m < mission; m++) wert += PayFor(m);
        }
        NoteStartBalance(mission, wert);
        return wert;
    }

    public static void Reset()
    {
        Completed = 0;
        Balance = 0;
        // ⚠ Auch die gemerkten Anfangsstaende — sonst schleppte ein neuer
        // Durchlauf die Zahlen des alten mit, und genau das war die Meldung.
        using var c = new ConfigFile();
        c.Load(SavePath);
        if (c.HasSection("mission_balance")) c.EraseSection("mission_balance");
        c.Save(SavePath);
    }

    // ---- die Missionsbezahlung ---------------------------------------------

    /// <summary>
    /// Die MISSIONSBEZAHLUNG einer Mission — ein FESTER Betrag, der am Ende
    /// aufs Konto kommt, gleichgueltig wie die Mission gelaufen ist.
    ///
    /// <para><b>Woher die Zahlen sind.</b> Der Bezahlungszaehler 0xA9A1D8 wird
    /// im ganzen Abbild nur so beschrieben:</para>
    /// <list type="bullet">
    /// <item><b>36 x <c>mov dword [0xA9A1D8], &lt;konstante&gt;</c></b> — die
    /// Bytefolge <c>C7 05 D8 A1 A9 00</c>. Fuenfunddreissig davon liegen in
    /// einem geschlossenen Block 0x488794..0x494130, je eine je Mission.</item>
    /// <item>1 x <c>mov dword [0xA9A1D8], edx</c> @0x41F07F, zwischen zwei
    /// <c>rep stosd</c>-Nullungen — der globale Anlauf.</item>
    /// <item>1 x <c>add dword [0xA9A1D8], ecx</c> @0x4D07F1. Die schon bekannte
    /// Funktion ohne Aufrufer; ihr einziger Thunk @0x402271 wird genau einmal
    /// gerufen, @0x41AC9D, mit 0.</item>
    /// <item><c>fwrite</c>/<c>fread</c> @0x41D8F1 / @0x41E96C — Spielstand.</item>
    /// </list>
    ///
    /// <para><b>Damit ist die Vermutung »je Abschuss verdient« widerlegt.</b> Es
    /// gibt keinen einzigen Schreibzugriff auf 0xA9A1D8 in der Trefferroutine
    /// oder sonst irgendwo im Kampfteil. Dass im Abschlussfenster
    /// »Ausgeschaltete 21« und »Missionsbezahlung $320« untereinander stehen,
    /// ist Anordnung, nicht Ursache.</para>
    ///
    /// <para><b>Welche Konstante zu welcher Mission gehoert.</b> Ueber den
    /// Verteiler @0x488493:</para>
    /// <code>
    ///   movsx eax, word [0x539934]        ; der Kampagnenzaehler
    ///   cmp   eax, 0x63                   ; &gt; 99 -&gt; Standardzweig
    ///   ja    ...
    ///   mov   cl, byte [eax + 0x494308]   ; 100 Bytes Missionsnr. -&gt; Fallnr.
    ///   jmp   dword [ecx*4 + 0x494274]    ; 37 Faelle
    /// </code>
    /// <para>Die Bytetabelle @0x494308 schliesst luecken- und versatzlos an die
    /// 37 x 4 Byte Sprungtabelle @0x494274 an, und sie bildet 1..33 auf die
    /// Faelle 1..33 ab — Fall N ist Mission N. Jeder dieser Faelle enthaelt
    /// genau eine der Konstanten. Daraus die Liste unten.</para>
    ///
    /// <para><b>Auf beiden Fassungen geprueft.</b> In F:\ (1.420.800 B) liegt
    /// der Zaehler bei 0x00A99238 statt 0xA9A1D8; dort stehen dieselben Werte in
    /// derselben Reihenfolge. Einziger Unterschied: F:\ hat 37 statt 36 Stellen,
    /// die zusaetzliche ist eine 2000 an der Stelle von Fall 34 — jenseits der
    /// 33 Missionen, die die Namenstabelle kennt, also ohne Belang.</para>
    ///
    /// <para><b>Die Gegenprobe.</b> Mission 1 zahlt 320. Das Foto
    /// akte-europa_5.jpg zeigt »Missionsbezahlung $320«, und mit den drei
    /// Schiffen zu je 50 kommt »Kontostand $470« heraus: 0 + 320 + 150.</para>
    ///
    /// <para><b>Und die Antwort auf die Frage danach:</b> die drei Schiffe sind
    /// eine ZUGABE, keine Voraussetzung. Wer sie stehen laesst, geht mit 320 aus
    /// Mission 1 — genug fuer zwei Hubschrauber zu je 150 (0x52FAC0/0x52FAC4).
    /// </para>
    ///
    /// <para>Missionen ausserhalb 1..33 fallen in den Standardzweig und zahlen
    /// nichts; die Ausnahme ist Mission 99 mit 100 (Fall 35). Wir geben
    /// dafuer 0 zurueck, denn unsere Kampagne hat 33 Missionen.</para>
    /// </summary>
    /// <param name="mission">Die Missionsnummer, 1-basiert wie im Original.</param>
    public static int PayFor(int mission)
        => mission >= 1 && mission <= Pay.Length ? Pay[mission - 1] : 0;

    /// <summary>Mission 1..33, in dieser Reihenfolge. Nicht abgetippt, sondern
    /// aus GAME.EXE gelesen — siehe <see cref="PayFor"/>.</summary>
    private static readonly int[] Pay =
    {
          320,   350,   375,   400,   500,   650,  2000,   800,  1000,  1200,
         2000,  2000,  2500,  3000,  3400,  4000,  4500,  4000,  5000,  5000,
         5500,  6000,  2000,  8000,  2000, 10000, 12000,  8000, 15000, 16000,
        18000, 20000, 30000,
    };

    // ---- the unlock schedule ------------------------------------------------

    /// <summary>What a mission may build.
    ///
    /// The schedule comes out of the campaign state machine @0x4884a6 and is
    /// carried as derived metadata in <c>res://Data/campaign_schedule.json</c> —
    /// it is our reading of the binary, not content from it, which is why it
    /// ships with the engine instead of being imported.
    ///
    /// What was missing until now was which state belongs to which mission.
    /// The map loader settles it: it indexes the mission-name table with the
    /// campaign counter itself (@0x41e25e, `21*counter + 0x4f81c0`), and the
    /// entries read "Mission 1" … "Mission 33". State N is mission N.
    ///
    /// A state's lists are what it unlocks, so the set for a mission is
    /// everything states 1..N have unlocked, minus what a state took away.
    ///
    /// <para>⚠ <b>Components are the fourth list, and the biggest one</b> (since
    /// 10.08.2026). `set_part(player, part, value)` @0x4D0520 has 1037 of the
    /// 1533 setter call sites in the mission blocks — four times as many as the
    /// design setter — and writes »this player OWNS this part« into sec46 +0x00.
    /// Unlike the other three it is per player, so the set is keyed by
    /// (player, part); <see cref="Unlocks.Components"/> is player 0, the human.
    /// </para>
    ///
    /// <para>⚠ <b>It is data, not a barrier</b>, and that was measured before it
    /// was written down: the ownership byte has thirteen readers in the
    /// original and every one of them is a menu — the construction screen's
    /// three pickers @0x46C490, the chassis list @0x455870,
    /// `research_offer_refresh` @0x4AA950 and the market module
    /// @0x4C0860..0x4C0E60, which uses it to pick WHICH design is offered for
    /// sale. `build_in_base`, `build_in_airport` and the production button do
    /// not look at it. Anything that turns this list into a refusal is stricter
    /// than the original — the same trap the release byte sec47 already
    /// set.</para></summary>
    public sealed class Unlocks
    {
        public readonly SortedSet<int> Ships = new();
        public readonly SortedSet<int> Aircraft = new();
        public readonly SortedSet<int> Vehicles = new();

        /// <summary>Every (player, part) the schedule has switched on.</summary>
        public readonly SortedSet<(int Player, int Part)> Parts = new();

        /// <summary>The human player's parts — the ones the construction screen
        /// of the original would offer.</summary>
        public SortedSet<int> Components
        {
            get
            {
                var s = new SortedSet<int>();
                foreach (var (p, x) in Parts) if (p == 0) s.Add(x);
                return s;
            }
        }

        public bool Known;
    }

    public const string SchedulePath = "res://Data/campaign_schedule.json";

    private static Godot.Collections.Array? _states;
    private static readonly Dictionary<int, Unlocks> _unlockCache = new();

    public static Unlocks UnlocksFor(int mission)
    {
        if (_unlockCache.TryGetValue(mission, out var hit)) return hit;
        var u = new Unlocks();
        _states ??= LoadStates();
        if (_states != null)
        {
            u.Known = true;
            foreach (var item in _states)
            {
                if (item.VariantType != Variant.Type.Dictionary) continue;
                var st = item.AsGodotDictionary<string, Variant>();
                int id = st.TryGetValue("state", out var sv) ? sv.AsInt32() : -1;
                if (id < 0 || id > mission) continue;
                Apply(st, "ships", u.Ships, true);
                Apply(st, "ships_off", u.Ships, false);
                Apply(st, "aircraft", u.Aircraft, true);
                Apply(st, "vehicles", u.Vehicles, true);
                ApplyParts(st, "components", u.Parts, true);
                ApplyParts(st, "components_off", u.Parts, false);
            }
        }
        _unlockCache[mission] = u;
        if (u.Known)
        {
            // What the schedule hands the HUMAN by this mission. Printed
            // because it is new and because the number is the whole point: the
            // components are four times the rest of the schedule put together,
            // and until 10.08.2026 the file carried none of them.
            var mine = u.Components;
            int others = u.Parts.Count - mine.Count;
            GD.Print($"campaign: Fahrplan M{mission} — {mine.Count} Bauteile beim Menschen" +
                     (others > 0 ? $", {others} bei den Computerspielern" : "") +
                     (mine.Count > 0 ? $" ({string.Join(",", mine)})" : ""));
        }
        return u;
    }

    /// <summary>A schedule entry is either a bare number or a small array whose
    /// first element is the number.</summary>
    private static void Apply(Godot.Collections.Dictionary<string, Variant> st,
                              string key, SortedSet<int> into, bool add)
    {
        if (!st.TryGetValue(key, out var v) || v.VariantType != Variant.Type.Array) return;
        foreach (var e in v.AsGodotArray())
        {
            int n = e.VariantType == Variant.Type.Array
                ? (e.AsGodotArray().Count > 0 ? e.AsGodotArray()[0].AsInt32() : -1)
                : e.AsInt32();
            if (n < 0) continue;
            if (add) into.Add(n); else into.Remove(n);
        }
    }

    /// <summary>A component entry is a pair <c>[Spieler, Bauteil]</c>. Written
    /// that way rather than as a bare number because the original writes this
    /// one table per player — the same mission hands the human parts 1, 4, 5, …
    /// and one of its computer opponents part 67.
    ///
    /// <para>⚠ A triple <c>[Spieler, Bauteil, Wert]</c> is accepted too, and
    /// entries with a <c>null</c> in them are skipped. That is not politeness:
    /// the file shipped in <c>res://Data/</c> — the fallback when nothing has
    /// been imported — was written by an older Python tool in exactly that
    /// shape, and it leaves a null wherever it could not resolve a register.
    /// Read as pairs those nulls turn into »player 0 owns part 0«.</para>
    /// </summary>
    private static void ApplyParts(Godot.Collections.Dictionary<string, Variant> st,
                                   string key, SortedSet<(int, int)> into, bool add)
    {
        if (!st.TryGetValue(key, out var v) || v.VariantType != Variant.Type.Array) return;
        foreach (var e in v.AsGodotArray())
        {
            if (e.VariantType != Variant.Type.Array) continue;
            var a = e.AsGodotArray();
            if (a.Count < 2) continue;
            if (a[0].VariantType == Variant.Type.Nil || a[1].VariantType == Variant.Type.Nil)
                continue;
            bool on = add;
            if (a.Count >= 3 && a[2].VariantType != Variant.Type.Nil)
                on = add && a[2].AsInt32() != 0;
            var pair = (a[0].AsInt32(), a[1].AsInt32());
            if (on) into.Add(pair); else into.Remove(pair);
        }
    }

    private static Godot.Collections.Array? LoadStates()
    {
        // ⚠ The imported schedule wins over the one shipped in Data/. That file
        // was written by an earlier tool and is incomplete: it hands design 52
        // to the player from mission 8 where the game gives it from mission 6,
        // and design 51 from 15 where the game gives it from 12. The end state
        // after mission 33 matches, which is why nobody noticed — the missions
        // in between were simply poorer than the original's.
        string path = Core.Content.Path("Maps/campaign_schedule.json");
        if (!FileAccess.FileExists(path)) path = SchedulePath;
        if (!FileAccess.FileExists(path)) return null;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return null;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok ||
            json.Data.VariantType != Variant.Type.Dictionary) return null;
        var root = json.Data.AsGodotDictionary<string, Variant>();
        if (!root.TryGetValue("states", out var sv) || sv.VariantType != Variant.Type.Array)
            return null;
        var arr = sv.AsGodotArray();
        GD.Print($"campaign: Fahrplan mit {arr.Count} Zustaenden geladen");
        return arr;
    }

    private static List<Mission> Load()
    {
        var list = new List<Mission>();
        string path = Core.Content.Path("Maps/campaign.json");
        if (!FileAccess.FileExists(path)) return list;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return list;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok ||
            json.Data.VariantType != Variant.Type.Dictionary) return list;
        var root = json.Data.AsGodotDictionary<string, Variant>();
        if (!root.TryGetValue("missions", out var mv) || mv.VariantType != Variant.Type.Array)
            return list;
        foreach (var item in mv.AsGodotArray())
        {
            if (item.VariantType != Variant.Type.Dictionary) continue;
            var d = item.AsGodotDictionary<string, Variant>();
            list.Add(new Mission
            {
                Index = Get(d, "index"),
                Map = d.TryGetValue("map", out var mp) ? mp.AsString() : "",
                Title = d.TryGetValue("title", out var t) ? t.AsString() : "",
                Width = Get(d, "width"),
                Height = Get(d, "height"),
                Tileset = Get(d, "tileset"),
            });
        }
        list.Sort((a, b) => a.Index.CompareTo(b.Index));
        GD.Print($"campaign: {list.Count} Missionen aus {path}");
        return list;
    }

    private static int Get(Godot.Collections.Dictionary<string, Variant> d, string k)
        => d.TryGetValue(k, out var v) && v.VariantType != Variant.Type.Nil ? v.AsInt32() : 0;
}
