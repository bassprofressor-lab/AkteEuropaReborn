using Godot;
using System.Collections.Generic;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// ⭐⭐ <b>DIE ERFINDUNG — der zweite Zweig der Forschung, <c>0x4AAF00</c>.</b>
///
/// <para>Die Forschung kann zweierlei: ein Bauteil, das man besitzt, um eine
/// Stufe <b>verbessern</b> (<see cref="Aufwerten"/>) — oder aus vierzig Rezepten
/// eine <b>neue Waffe erfinden</b>. Die drei Erfindungen stehen im Angebot immer
/// vorn, zu festen Preisen <b>500 / 2000 / 5000</b> (»Kleine«, »Mittlere«,
/// »Große Forschung«, <c>0x503B38</c>), und tragen im Laufsatz die Kennung
/// <c>200 + i</c>.</para>
///
/// <para><b>Der Ablauf</b> (gelesen: <c>berichte/erfindung-fable.md</c>):</para>
///
/// <code>
///   srand(Losnummer)                       ⭐ der Keim — danach ACHT Würfe
///   h  = Techstufe + Sockel[i] + rand % Spanne[i]      ; (1,6) (2,12) (3,18)
///   h1 = rand % h            (≤ 9)
///   h2 = rand % (h − h1)     (≤ 9, nur wenn Rest > 0)
///   h3 = Rest − h2 + h1/3 ;  h1 = 2·h1/3 ;  Ausgleichsschleife bis h3 ≤ 9
///   r1..r3 = rand % 4 + 4·h_k          ; die drei WERTrezepte
///   r4     = rand % 40                 ; Name, Bild, untere Reichweite
///   neue Bauteilzeile r = erste freie 1…49 mit +0x0D == 0
///   Werte mischen, Name aus R4 (Variante = achter Wurf)
/// </code>
///
/// <para>⭐⭐ <b>Der Prüfstand, und er ist so gut wie sie werden:</b> drei
/// Spielstände (<c>4.DM</c>, <c>5.DM</c>, <c>7.DM</c>) tragen in Bauteilzeile 20
/// eine erfundene Waffe namens <b>»Hiff-64«</b>. Aus <b>Losnummer 0, Index 2
/// (Große Forschung), Techstufe 5</b> fällt sie heraus — <b>58 von 58 Byte
/// identisch</b>, in allen drei Dateien, selbst nachgerechnet. Und der Name
/// erklärt sich mit: die angehängten Ziffern sind <b>h1 = 6</b> und
/// <b>h3 = 4</b>.</para>
///
/// <para>⚠ <b>Zwei Schrulligkeiten des Originals, beide belegt und beide
/// nachgebaut:</b> das Preisfeld <c>+0x20</c> zählt <b>Rezept 3 doppelt</b>
/// (<c>0x4AB383</c> nimmt <c>ecx = [esp+0x14]</c>, und das ist R3, nicht R4),
/// und die obere Reichweite wird auf <c>max(+0x14, +0x16 + 3)</c>
/// nachkorrigiert. Ohne beide käme »Hiff-64« nicht heraus.</para>
///
/// <para>⚠ <b>Der Würfel ist NICHT unser Netzzufall.</b> <c>0x4AAF00</c> ruft
/// <c>srand</c>/<c>rand</c> der MSVC-Bibliothek (<c>0x4D6C50</c>/<c>0x4D6C70</c>),
/// nicht <c>0x4C5B30</c>. Das Ergebnis ist damit eine reine Funktion von
/// (Losnummer, Index, Techstufe) — auf jedem Rechner dieselbe Waffe, sobald die
/// Losnummer mit dem Befehl ankommt. ⚠ Im Original setzt dieses <c>srand</c>
/// nebenbei den GLOBALEN Zufall zurück; das bauen wir <b>nicht</b> nach (eigener
/// Generator, der Spielzufall bleibt unberührt) — eine bewusste Abweichung, weil
/// sie sonst jeden Gleichlauf nach der ersten Erfindung zerschlüge.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary><c>--keine-erfindung</c>: die drei Erfindungen aus dem Angebot
    /// nehmen. Der Gegenschalter zu dieser Datei.</summary>
    public static bool KeineErfindung;

    /// <summary>Für die Probe: wie viele Waffen erfunden wurden, wie oft keine
    /// freie Bauteilzeile mehr da war, und wie oft das Budget gedeckelt
    /// wurde.</summary>
    public int ErfindungFertig, ErfindungKeineZeile, ErfindungGedeckelt;

    /// <summary>Die zuletzt erfundene Waffe — für die Probe und die Meldung.</summary>
    public string ErfindungLetzterName = "";
    public int ErfindungLetzteZeile = -1;

    // ---- der Würfel -----------------------------------------------------------

    /// <summary>
    /// <c>srand</c>/<c>rand</c> der MSVC-Bibliothek, wie <c>0x4D6C50</c> und
    /// <c>0x4D6C70</c> sie rechnen: <c>s = s·214013 + 2531011</c>, Ergebnis
    /// <c>(s &gt;&gt; 16) &amp; 0x7FFF</c>. Die Zahlen stehen so in der
    /// Zerlegung (<c>lea</c>-Kette ×214013, <c>add 0x269EC3</c>).
    ///
    /// <para>⭐ Ein <b>eigener</b> Generator, kein Griff in
    /// <see cref="Simulation.Determinism"/>: die Erfindung braucht genau diese
    /// Folge, und der Spielzufall darf davon nichts merken.</para></summary>
    private sealed class MsvcZufall
    {
        private uint _s;
        public int Wuerfe;
        public MsvcZufall(int keim) => _s = (uint)(keim & 0xFFFF);
        public int Naechster()
        {
            _s = _s * 214013u + 2531011u;
            Wuerfe++;
            return (int)((_s >> 16) & 0x7FFF);
        }
    }

    // ---- die Tafeln -----------------------------------------------------------

    /// <summary>Ein Rezept: die 70 rohen Bytes und seine vier Namen.</summary>
    public readonly record struct WaffenRezept(int Nummer, byte[] Roh, string[] Namen);

    private static Dictionary<int, WaffenRezept>? _rezepte;
    private static int[]? _techLeiter;
    private static (int Sockel, int Spanne)[]? _budgets;
    private static int[]? _erfindungPreise;

    /// <summary>Die 40 Rezepte aus <c>weapon_recipes.json</c>. Fehlt die Datei,
    /// bleibt die Tafel leer und es wird nichts erfunden — ⚠ und das ist die
    /// richtige Reaktion: Werte für eine Waffe zu erfinden wäre genau der
    /// Fehler, den <c>OFFENE_FRAGEN</c> AN.7 schon einmal aufgeschrieben hat.
    /// </summary>
    public static IReadOnlyDictionary<int, WaffenRezept> Rezepte
    {
        get
        {
            if (_rezepte != null) return _rezepte;
            _rezepte = new Dictionary<int, WaffenRezept>();
            string pfad = Core.Content.Path("Maps/weapon_recipes.json");
            if (!FileAccess.FileExists(pfad))
            {
                GD.PrintErr("erfindung: Maps/weapon_recipes.json fehlt — ohne die " +
                            "Rezepttafel wird nichts erfunden");
                return _rezepte;
            }
            using var f = FileAccess.Open(pfad, FileAccess.ModeFlags.Read);
            if (f == null) return _rezepte;
            var json = new Json();
            if (json.Parse(f.GetAsText()) != Error.Ok ||
                json.Data.VariantType != Variant.Type.Dictionary) return _rezepte;
            var root = json.Data.AsGodotDictionary<string, Variant>();
            if (!root.TryGetValue("recipes", out var rv) ||
                rv.VariantType != Variant.Type.Dictionary) return _rezepte;
            foreach (var kv in rv.AsGodotDictionary<string, Variant>())
            {
                if (!int.TryParse(kv.Key, out int nr)) continue;
                if (kv.Value.VariantType != Variant.Type.Dictionary) continue;
                var r = kv.Value.AsGodotDictionary<string, Variant>();
                var namen = new List<string>();
                if (r.TryGetValue("names", out var nv) && nv.VariantType == Variant.Type.Array)
                    foreach (var n in nv.AsGodotArray()) namen.Add(n.AsString());
                byte[] roh = r.TryGetValue("raw", out var hv)
                    ? HexBytes(hv.AsString()) : System.Array.Empty<byte>();
                _rezepte[nr] = new WaffenRezept(nr, roh, namen.ToArray());
            }
            return _rezepte;
        }
    }

    /// <summary>Techstufenleiter (<c>0x503AF0</c>) und Budgets
    /// (<c>0x503B80</c>) aus <c>research_ladder.json</c>.</summary>
    private static void ErfindungTafelnLaden()
    {
        if (_techLeiter != null) return;
        _techLeiter = System.Array.Empty<int>();
        _budgets = System.Array.Empty<(int, int)>();
        _erfindungPreise = System.Array.Empty<int>();
        string pfad = Core.Content.Path("Maps/research_ladder.json");
        if (!FileAccess.FileExists(pfad)) return;
        using var f = FileAccess.Open(pfad, FileAccess.ModeFlags.Read);
        if (f == null) return;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok ||
            json.Data.VariantType != Variant.Type.Dictionary) return;
        var root = json.Data.AsGodotDictionary<string, Variant>();
        if (root.TryGetValue("tech_ladder", out var tv) && tv.VariantType == Variant.Type.Array)
        {
            var a = tv.AsGodotArray();
            var v = new int[a.Count];
            for (int i = 0; i < a.Count; i++) v[i] = a[i].AsInt32();
            _techLeiter = v;
        }
        if (root.TryGetValue("invention_budget", out var bv) && bv.VariantType == Variant.Type.Array)
        {
            var a = bv.AsGodotArray();
            var v = new (int, int)[a.Count];
            for (int i = 0; i < a.Count; i++)
            {
                var paar = a[i].AsGodotArray();
                v[i] = (paar.Count > 0 ? paar[0].AsInt32() : 0,
                        paar.Count > 1 ? paar[1].AsInt32() : 1);
            }
            _budgets = v;
        }
        if (root.TryGetValue("invention_prices", out var pv) && pv.VariantType == Variant.Type.Array)
        {
            var a = pv.AsGodotArray();
            var v = new int[a.Count];
            for (int i = 0; i < a.Count; i++) v[i] = a[i].AsInt32();
            _erfindungPreise = v;
        }
    }

    /// <summary>Die Technikstufe dieser Mission — <c>0x503AF0[Mission]</c>.
    /// ⚠ Im Gefecht steht dort <c>byte[0x540EB8]</c>, die eingestellte
    /// Technikstufe; die haben wir nicht, darum <b>1</b> wie in Mission 0
    /// (dieselbe Setzung wie beim Grundpreis).</summary>
    private static int ErfindungTechstufe()
    {
        ErfindungTafelnLaden();
        int mission = UI.SkirmishSetup.CampaignMission;
        if (mission <= 0 || _techLeiter == null || _techLeiter.Length == 0) return 1;
        return _techLeiter[Mathf.Clamp(mission, 0, _techLeiter.Length - 1)];
    }

    /// <summary>Preis einer der drei Erfindungen: 500 / 2000 / 5000.</summary>
    private static int ErfindungPreis(int index)
    {
        ErfindungTafelnLaden();
        if (_erfindungPreise == null || index < 0 || index >= _erfindungPreise.Length) return 0;
        return _erfindungPreise[index];
    }

    private static string ErfindungAngebotName(int index) => index switch
    {
        0 => "Kleine Forschung",
        1 => "Mittlere Forschung",
        _ => "Große Forschung",
    };

    // ---- die freie Bauteilzeile (0x4AAE00) ------------------------------------

    /// <summary>Die erste Zeile 1…49 der Spielerscheibe mit <c>+0x0D == 0</c>.
    ///
    /// <para>⚠ <b>Findet das Original keine, schreibt es die Waffe in Zeile 0</b>
    /// — es prüft den Rückgabewert nicht (<c>0x4AB164</c>). Das ist ein Fehler
    /// des Originals, kein Merkmal: Zeile 0 ist keine Waffe. Wir brechen
    /// stattdessen ab und sagen es; nachgebaut würde er eine gültige Zeile
    /// zerstören.</para></summary>
    private int ErfindungFreieZeile(int spieler)
    {
        BauteileVorbereiten();
        for (int r = 1; r <= 49; r++)
        {
            var c = _bauteile![spieler][r];
            if (c.Length >= 58 && c[0x0D] == 0) return r;
        }
        return -1;
    }

    // ---- der Mischlauf (0x4AAF00) ---------------------------------------------

    /// <summary>Was ein Erfindungslauf ausgerechnet hat — für die Probe.</summary>
    public readonly record struct ErfindungLauf(int H, int H1, int H2, int H3,
                                                int R1, int R2, int R3, int R4,
                                                int Namenswahl, string Name, int Zeile,
                                                bool Gedeckelt);

    /// <summary>
    /// ⭐⭐ <b>Eine Waffe erfinden — <c>0x4AAF00</c>, Busbefehl 532.</b> Schreibt
    /// die neue Bauteilzeile in die Scheibe des Spielers und gibt zurück, was
    /// dabei gewürfelt wurde.
    ///
    /// <para>⚠ <b>Eine gekennzeichnete Setzung:</b> das Budget wird auf
    /// <b>27</b> gedeckelt. Darüber bleiben <c>h1 = h2 = 9</c> und
    /// <c>h3 ≥ 10</c>, die Ausgleichsschleife dreht im Original 10 000-mal leer
    /// (»Researching Error«, unsichtbar) und <c>r3 ≥ 40</c> liest dann
    /// <b>hinter der Rezepttafel</b> in der Aufwertungstafel. Das trifft in der
    /// Kampagne die Missionen mit Techstufe 8 und 9 und dort die Große
    /// Forschung. Einen Speicherüberlauf nachzubauen wäre keine Treue —
    /// gezählt wird er in <see cref="ErfindungGedeckelt"/>.</para></summary>
    /// <param name="techProbe">Nur für den Prüfstand: die Technikstufe von Hand
    /// setzen, statt sie aus der Mission zu nehmen. −1 = der Regelfall. Ohne
    /// diese Naht liesse sich »Hiff-64« nicht nachrechnen, denn der Spielstand
    /// dazu stand auf Technikstufe 5.</param>
    public ErfindungLauf? Erfinden(int spieler, int index, int los, int techProbe = -1)
    {
        if (spieler is < 0 or > 7 || index < 0) return null;
        ErfindungTafelnLaden();
        BauteileVorbereiten();
        if (Rezepte.Count == 0 || _budgets == null || _budgets.Length <= index) return null;

        var w = new MsvcZufall(los);
        var (sockel, spanne) = _budgets[index];
        if (spanne <= 0) spanne = 1;
        int tech = techProbe >= 0 ? techProbe : ErfindungTechstufe();
        int h = tech + w.Naechster() % spanne + sockel;
        h = (sbyte)h;                                   // movsx esi, bl
        bool gedeckelt = false;
        if (h > 27) { h = 27; gedeckelt = true; ErfindungGedeckelt++; }   // ⚠ SETZUNG
        if (h <= 0) return null;                        // idiv durch 0 waere ein Absturz

        int h1 = w.Naechster() % h;
        if (h1 > 9) h1 = 9;
        int rest = h - h1;
        int h2 = rest > 0 ? w.Naechster() % rest : 0;   // ⚠ NUR dann ein Wurf
        if (h2 > 9) h2 = 9;
        int h3 = rest - h2 + h1 / 3;
        h1 = 2 * h1 / 3;
        for (int n = 0; h3 > 9 && n < 10000; n++)
        {
            if (h1 < 9) { h1++; h3--; }
            if (h2 < 9) { h2++; h3--; }
            if (h1 >= 9 && h2 >= 9) break;              // sonst dreht die Schleife leer
        }

        int r1 = w.Naechster() % 4 + 4 * h1;
        int r2 = w.Naechster() % 4 + 4 * h2;
        int r3 = w.Naechster() % 4 + 4 * h3;
        int r4 = w.Naechster() % 40;
        if (!Rezepte.TryGetValue(r1, out var R1) || !Rezepte.TryGetValue(r2, out var R2) ||
            !Rezepte.TryGetValue(r3, out var R3) || !Rezepte.TryGetValue(r4, out var R4))
            return null;

        int zeile = ErfindungFreieZeile(spieler);
        if (zeile < 0)
        {
            ErfindungKeineZeile++;
            _order = "Too many weapons — keine freie Bauteilzeile";
            return null;
        }
        var B = _bauteile![spieler][zeile];
        if (B.Length < 58) return null;
        System.Array.Clear(B, 0, B.Length);

        B[0x00] = 1;                                    // besessen
        B[0x01] = 9;                                    // Stufe 9 — nie aufwertbar
        B[0x24] = 10;                                   // ⭐ Techstufe 10: ueberlebt den Missionsstart
        SetzeWort(B, 0x0E, Wort(R1.Roh, 0x06));
        B[0x10] = R1.Roh[0x09];
        B[0x13] = R1.Roh[0x08];
        B[0x20] = (byte)(R1.Roh[0x15] >> 2);
        B[0x11] = R2.Roh[0x0E];
        SetzeWort(B, 0x14, Wort(R2.Roh, 0x0A));
        B[0x20] = (byte)((B[0x20] + (R2.Roh[0x15] >> 2)) & 0xFF);
        SetzeWort(B, 0x18, Wort(R3.Roh, 0x10));
        SetzeWort(B, 0x1E, Wort(R3.Roh, 0x12));
        B[0x12] = R3.Roh[0x14];
        B[0x20] = (byte)((B[0x20] + (R3.Roh[0x15] >> 2)) & 0xFF);

        int v = w.Naechster() % 4;                      // der achte Wurf: die Namensvariante
        string name = v < R4.Namen.Length ? R4.Namen[v] : "";
        if (name.EndsWith("-"))
            // ⭐ »Hiff-64«: die zwei Ziffern sind h1 und h3.
            name += $"{r1 / 4}{r3 / 4}";
        NameInZeile(B, 0x02, name);
        NameInZeile(B, 0x25, name);                     // Langname = Kurzname

        B[0x0D] = R4.Roh[0x02];                         // Bild
        B[0x1C] = R4.Roh[0x04];
        B[0x23] = R4.Roh[0x03];
        SetzeWort(B, 0x16, Wort(R4.Roh, 0x0C));         // untere Reichweite
        // ⚠⚠ R3, NICHT R4 — 0x4AB383 nimmt ecx = [esp+0x14]. Ohne diese
        // Schrulligkeit kommt »Hiff-64« nicht heraus.
        B[0x20] = (byte)((B[0x20] + (R3.Roh[0x15] >> 2)) & 0xFF);

        // Reichweiten-Nachkorrektur: +0x14 = max(+0x14, +0x16 + 3)
        if (Wort(B, 0x14) < Wort(B, 0x16)) SetzeWort(B, 0x14, Wort(B, 0x16) + 3);
        if (Wort(B, 0x16) + 4 > Wort(B, 0x14)) SetzeWort(B, 0x14, Wort(B, 0x16) + 3);

        ErfindungFertig++;
        ErfindungLetzterName = name;
        ErfindungLetzteZeile = zeile;
        return new ErfindungLauf(h, h1, h2, h3, r1, r2, r3, r4, v, name, zeile, gedeckelt);
    }

    private static int Wort(byte[] b, int at) =>
        at + 1 < b.Length ? b[at] | (b[at + 1] << 8) : 0;

    private static void SetzeWort(byte[] b, int at, int v)
    {
        if (at + 1 >= b.Length) return;
        b[at] = (byte)(v & 0xFF);
        b[at + 1] = (byte)((v >> 8) & 0xFF);
    }

    /// <summary>Den Namen als cp437 in die Zeile schreiben, mit Nullabschluss.
    /// Der Satz hat ab <c>+0x02</c> und ab <c>+0x25</c> je Platz für 18
    /// Zeichen.</summary>
    private static void NameInZeile(byte[] b, int at, string name)
    {
        // Die Rezeptnamen sind reines ASCII (»Hiff-«, »Raptor«, »Acider«) — ein
        // Zeichen ueber 127 kommt in den 160 Namen nicht vor, darum genuegt die
        // Byteabbildung. Alles darueber wird zu '?', statt still zu verstuemmeln.
        int n = Mathf.Min(name.Length, 17);
        for (int i = 0; i < n; i++)
            b[at + i] = name[i] < 128 ? (byte)name[i] : (byte)'?';
        if (at + n < b.Length) b[at + n] = 0;
    }

    // ---- die erfundene Waffe im Entwurfsschirm ---------------------------------

    /// <summary>
    /// ⭐ <b>Was eine Erfindung wert wäre, wenn man sie nicht verbauen könnte.</b>
    /// Die neue Zeile steht in der Bauteiltafel des Spielers, aber der
    /// Entwurfsschirm füllt seine Waffenliste aus <c>weapons.json</c> — einer
    /// Ausfuhr der EXE, in der eine zur Laufzeit erfundene Waffe naturgemäß
    /// nicht vorkommt. Hier werden sie nachgetragen: jede Zeile 1…49 der
    /// Spielerscheibe mit <b>Techstufe 10</b> (das ist die Kennung der
    /// Erfindung, <c>0x4AB1B6</c>) und einem Namen.
    ///
    /// <para>Ruhig mehrfach aufrufbar — was schon in der Liste steht, kommt
    /// nicht doppelt hinein.</para></summary>
    private void ErfundeneWaffenNachtragen()
    {
        BauteileVorbereiten();
        int spieler = Mathf.Clamp(ViewPlayer, 0, 7);
        for (int r = 1; r <= 49; r++)
        {
            var c = _bauteile![spieler][r];
            if (c.Length < 58 || c[0x24] != 10 || c[0x00] == 0) continue;
            string nm = Import.Cp437.GetString(c, 0x02, 18).Trim();
            if (nm.Length == 0) continue;
            bool da = false;
            foreach (var w in Designer.Weapons) if (w.Id == r) { da = true; break; }
            if (da) continue;
            Designer.Weapons.Add(new DesignScreen.Part { Id = r, Name = nm });
        }
        Designer.Weapons.Sort((a, b) => a.Id.CompareTo(b.Id));
    }

    // ---- der Abschluss --------------------------------------------------------

    /// <summary>Der Abschluss einer Erfindung im Takt — Busbefehl 532. Die
    /// Losnummer wird danach neu gewürfelt (<c>0x4AAE70</c>).</summary>
    private void ErfindungAbschluss(ForschungLauf l, bool melden)
    {
        var lauf = Erfinden(l.Spieler, l.ErfindungIndex, _forschungLos);
        // 0x4AAE70 wuerfelt die Losnummer sofort nach dem Absenden neu.
        _forschungLos = Simulation.Determinism.Roll(0xFFFF);
        if (lauf == null) return;
        if (!melden || l.Spieler != ViewPlayer) return;
        _order = $"Nachricht des FORSCHUNGSLABORS: Neue Waffe erfunden — {lauf.Value.Name}";
        Audio.GameSounds.Play(Audio.GameSounds.ResearchDone);      // 136, wie die Aufwertung
        var basis = ForschungBasis(l.Basis);
        if (basis != null) NoteEvent(basis, $"Neue Waffe: {lauf.Value.Name}");
    }

    // ---- Fensterart 29: was das Fenster »Forschungsergebnisse« zeigt ----------

    /// <summary><c>--forschungsliste-alle</c> — den ersten Fehler des Originals
    /// abschalten: statt nur der Zeilen 1…70 wird der ganze Block durchsucht.
    /// ⚠ Ohne den Schalter suchen wir wie das Original, und die Zeilen 71
    /// (Transporter) und 72 (G-Technik) fallen heraus.</summary>
    public static bool ForschungslisteAlle;

    /// <summary><c>--forschungsliste-eigenblock</c> — den zweiten Fehler des
    /// Originals abschalten: den Namen aus dem Block des Betrachters holen statt
    /// aus Block 0.</summary>
    public static bool ForschungslisteEigenblock;

    /// <summary>Wieviele Zeilen der Filter angesehen hat, und ob die zwei
    /// Fehler des Originals dabei überhaupt etwas verändert hätten — für
    /// <c>--forschungsliste-check</c>.</summary>
    public int ForschungslisteGeprueft, ForschungslisteBlockUnterschied;

    /// <summary>
    /// <b>Die Zeilen der FORSCHUNGSERGEBNISSE</b> (Fensterart 29) — siehe
    /// <see cref="UI.ResearchListView"/>.
    ///
    /// <para>Der Filter ist der des Originals (@0x47CB2F…0x47CB64): jede
    /// Bauteilzeile <b>k = 1…70</b> im Block des Betrachters, deren Techstufe
    /// <c>+0x24</c> <b>10</b> ist. Die 10 setzt nur <see cref="Erfinden"/>
    /// (<c>B[0x24] = 10</c>), so wie im Original nur <c>0x4AB1B6</c> — es ist
    /// also die Marke »das hier ist erfunden«.</para>
    ///
    /// <para>⚠⚠ <b>ZWEI FEHLER DES ORIGINALS, beide nachgebaut.</b> Sie werden
    /// nicht stillschweigend berichtigt; jeder hat seinen Schalter, und der
    /// Prüfstand meldet, ob sie hier überhaupt etwas ausmachen:</para>
    /// <list type="number">
    ///   <item><b>Die Suche endet bei 70</b> (<c>cmp ax, 0x46; jle</c>
    ///   @0x47CB60), obwohl je Block 200 Zeilen Platz haben. Zeile 71
    ///   (Transporter) und 72 (G-Technik) sind damit unerreichbar. Bei uns
    ///   vergibt <c>ErfindungFreieZeile</c> die Zeilen — liegt eine Erfindung
    ///   über 70, sieht man sie ohne <c>--forschungsliste-alle</c> nicht.</item>
    ///   <item><b>Die Prüfung liest im Block des Spielers, den NAMEN aber ohne
    ///   den Block</b> (<c>0x5045C5 + 58·k</c> @0x47CC53, ohne die 200·P, die
    ///   andere Namensleser wie <c>0x46CC35</c> sehr wohl addieren). Für Spieler
    ///   0 ist das folgenlos; für jeden anderen stünde dort ein fremder Name.
    ///   <c>--forschungsliste-eigenblock</c> nimmt den Namen aus dem eigenen
    ///   Block.</item>
    /// </list>
    /// </summary>
    public List<string> ForschungsergebnisseZeilen()
    {
        var aus = new List<string>();
        ForschungslisteGeprueft = 0;
        ForschungslisteBlockUnterschied = 0;
        if (_bauteile == null) return aus;

        int p = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;
        if (p >= _bauteile.Length) return aus;
        var block = _bauteile[p];
        var block0 = _bauteile[0];
        int letzte = ForschungslisteAlle ? block.Length - 1 : 70;   // @0x47CB60

        for (int k = 1; k <= letzte && k < block.Length; k++)
        {
            ForschungslisteGeprueft++;
            var b = block[k];
            if (b == null || b.Length < 58 || b[0x24] != 10) continue;   // @0x47CB3E

            // ⚠ Der Name aus BLOCK 0 — der zweite Fehler des Originals.
            var quelle = ForschungslisteEigenblock ? b
                       : (k < block0.Length && block0[k] is { Length: >= 58 } n ? n : b);
            if (!ReferenceEquals(quelle, b) && NameAus(quelle) != NameAus(b))
                ForschungslisteBlockUnterschied++;
            string name = NameAus(quelle);
            aus.Add(name.Length > 0 ? name : $"Zeile {k}");
        }
        return aus;
    }

    /// <summary>Der lange Bauteilname aus <c>+0x25</c> (@0x47CC53), bis zur
    /// Null. Dasselbe Feld, in das <see cref="Erfinden"/> den Namen der
    /// Erfindung schreibt.</summary>
    private static string NameAus(byte[] b)
    {
        int n = 0;
        while (0x25 + n < b.Length && n < 24 && b[0x25 + n] != 0) n++;
        return System.Text.Encoding.ASCII.GetString(b, 0x25, n).Trim();
    }
}
