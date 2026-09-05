using Godot;
using System.Collections.Generic;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// ⭐⭐ <b>DIE AUFWERTUNG — was eine Forschung wirklich am Bauteil ändert.</b>
///
/// <para>Gebaut am 03.09.2026 nach eigener Lesung
/// (<c>berichte/aufwertung-lesung.md</c>). Anlass war seine Meldung zum Sprit:
/// »ich komm teilweise gar nicht bis zu dem Forscher gefahren«. Der Verbrauch
/// ist originalgetreu (1 je Zelle, <c>0x407AA7</c>) und die Tanks stimmen —
/// was fehlte, war <b>dass der Tank überhaupt wächst</b>.</para>
///
/// <para><b>Der Ablauf des Originals</b>, in drei Schritten:</para>
///
/// <code>
///   0x4AAA20(bauteil):                          ⭐ DER WÜRFEL
///       suche unter den 50 Zeilen die mit +0x01 == bauteil   (linear)
///       nicht gefunden -> 'for_vyv not found!'
///       Zeile+0x02 := 0x4C5B30() &amp; 3          ; welcher Arm, EINER VON VIER
///
///   0x4AA360:                                   das Angebot bauen
///       immer:   +0x1A = (Zeile+0x04 − Bauteil+0x0E) / (9 − Stufe)
///       Fahrwerk:+0x1F = (Zeile+0x09 − Bauteil+0x13) / (9 − Stufe)
///       je nach Wurf EINES von:
///           0 -> +0x1C = Zeile+0x06        2 -> +0x26 = Zeile+0x0E  ⭐ TANK
///           1 -> +0x1D = Zeile+0x07        3 -> +0x2C = −Zeile+0x14
///
///   0x4AAA80:                                   anwenden
///       Bauteil+0x0E += d0        (Angebot +0x1A)      0x4AAAD9  add
///       Bauteil+0x13 := a         (Angebot +0x1F)      0x4AAB43  mov
///       Bauteil+0x10 += b         (Angebot +0x1C)      0x4AAB49  add
///       Bauteil+0x11 := c         (Angebot +0x1D)      0x4AAB60  mov
///       Bauteil+0x1A += d         (Angebot +0x26)      0x4AAB66  add   ⭐ TANK
///       Bauteil+0x21 += e         (Angebot +0x2C)      0x4AAB71  add
///       Bauteil+0x01 := Stufe+1                        0x4AAACA  mov
///       dann 0x4B24B0 (Entwürfe neu) und 0x4B3CD0 (8000 Einheiten nachziehen)
/// </code>
///
/// <para>⚠⚠ <b>DER TANK WÄCHST NUR BEI WURF 2 — in einem von vier Fällen.</b>
/// Das ist die Stelle, an der <c>berichte/sprit-fable.md</c> Abschnitt 4 zu
/// stark formuliert ist (»sicher ist: sie schreibt auf +0x1A«). In drei von
/// vier Fällen trifft die Aufwertung <c>+0x10</c>, <c>+0x11</c> oder
/// <c>+0x21</c>, und der Tank bleibt, wie er ist.</para>
///
/// <para>⚠ <b>Und beinahe wäre der umgekehrte Fehlschluss passiert:</b> in der
/// ausgelieferten EXE steht <c>Zeile+0x02</c> in allen 35 Zeilen auf <b>0</b>,
/// was aussieht wie »immer Arm 0, der Tank wächst nie«. Es ist aber ein
/// LAUFZEITFELD — <c>reloc_refs --range 0x5035F0 1200</c> findet 17 Leser und
/// <b>genau einen Schreiber</b> (<c>0x4AAA61</c>), und der würfelt.</para>
///
/// <para>⭐ <b>Der Wurf geht durch <see cref="Simulation.Determinism"/>.</b>
/// <c>0x4013ED</c> springt auf <c>0x4C5B30</c>, den deterministischen Zufall
/// des Originals — nicht auf MSVC-<c>rand()</c> (<c>0x4D6C70</c>). Ein
/// <c>GD.Randi()</c> an dieser Stelle würde den Gleichlauf brechen.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    // ---- die Bauteiltafel, JE SPIELER ---------------------------------------

    /// <summary>Bauteilzeilen je Spieler: <c>[spieler][bauteil]</c> → die
    /// 58 Byte des Satzes. Das Original führt sie als
    /// <c>0x5045A0 + 58·(Bauteil + 200·Spieler)</c>.
    ///
    /// <para>⭐ <b>Warum acht Blöcke und nicht einer:</b> die Ladertafel führt
    /// <c>sec46</c> mit <b>92 800 Byte</b> = 58 × 200 × <b>8</b> — der
    /// Spielstand trägt jeden Spielerblock einzeln. In der EXE ist nur Block 0
    /// gefüllt (nachgezählt: 101 von 200 Zeilen belegt, Blöcke 1…7 komplett
    /// null); die anderen sieben werden beim Start aus Block 0 kopiert
    /// (<c>rep movsd</c> @<c>0x4B22ED</c>, so schon im Kopf von
    /// <c>ExeTables.ComponentRow</c> vermerkt). Genau das tut
    /// <see cref="BauteileVorbereiten"/>.</para>
    ///
    /// <para>⚠ <b>UNSERE Setzung:</b> wir kopieren aus <c>component_stats.json</c>,
    /// und das ist der EXE-Block 0, nicht die acht Scheiben des Spielstands.
    /// Beleg dafür, dass das derselbe Anfangszustand ist: <c>PARTS.CWD</c> ist
    /// exakt 92 800 B, und ihre <b>Scheibe 0 stimmt mit dem .data-Block ab
    /// 0x5045A0 in 11 600 von 11 600 Byte überein</b> (OFFENE_FRAGEN, 21.08.).
    /// Sobald wir sec46 wirklich aus der Karte lesen, gehört diese Kopie
    /// ersetzt.</para></summary>
    private byte[][][]? _bauteile;

    /// <summary>Wie oft eine Aufwertung angewandt wurde, und wie oft sie dabei
    /// den TANK getroffen hat. ⭐ Das Nullmodell des Prüfstands: über viele
    /// Würfe muss <c>Tank</c> rund ein Viertel von <c>Angewandt</c> sein — nicht
    /// alles und nicht nichts.</summary>
    public int AufwertungAngewandt, AufwertungTank, AufwertungOhneZeile;

    /// <summary>Die Würfe, einzeln gezählt (0…3) — damit sich zeigen lässt,
    /// dass wirklich gewürfelt wird und nicht immer derselbe Arm kommt.</summary>
    public readonly int[] AufwertungWurf = new int[4];

    /// <summary><c>--aufwertung-alt</c> — die Gegenprobe: keine Aufwertung
    /// verändert ein Bauteil. Der Stand vor dem 03.09.2026, in dem der Tank
    /// für immer bei seinem Anfangswert blieb.</summary>
    public static bool AufwertungAlt;

    /// <summary><c>--aufwertung-immer-tank</c> — die zweite Gegenprobe: der
    /// Wurf wird übergangen und IMMER der Tankarm genommen. Nur zum Messen:
    /// damit ist zu zeigen, dass die Kette Bauteil → Entwurf → Einheit
    /// überhaupt trägt, ohne auf ein Viertel Wahrscheinlichkeit zu warten.
    /// ⚠ Das ist KEINE Nachbildung des Originals.</summary>
    public static bool AufwertungImmerTank;

    /// <summary>Legt die acht Spielerblöcke an, jeder eine Kopie des
    /// Grundstands. Ruhig mehrfach aufrufbar; tut nur beim ersten Mal etwas.
    /// </summary>
    private void BauteileVorbereiten()
    {
        if (_bauteile != null) return;
        _bauteile = new byte[8][][];
        for (int p = 0; p < 8; p++)
        {
            _bauteile[p] = new byte[200][];
            for (int b = 0; b < 200; b++)
                _bauteile[p][b] = GrundBauteil(b) is { } g ? (byte[])g.Clone() : new byte[58];
        }
        // ⭐ 03.09.2026 — ab jetzt rechnet die Entwurfsrechnung aus DIESEN
        // Blöcken, wenn sie einen Spieler kennt (0x4B1FB0 indiziert die
        // Bauteiltafel mit 200·Spieler, siehe den Kopf von DesignMath). Der
        // Nachschlager wird schon in _Ready gesetzt (MapEntityLayer.cs) — er ist
        // statisch, und nach einem Kartenwechsel dürfte er nicht auf die alte
        // Ebene zeigen; hier noch einmal, damit eine Ebene ohne _Ready (Probe,
        // Test) dieselbe Zusage hat.
        Simulation.DesignMath.SpielerZeile = BauteilFuer;
    }

    // ---- die Entwürfe, JE SPIELER --------------------------------------------

    /// <summary>Die neu gerechneten Entwurfsschwänze je Spieler, nach der
    /// Bauteilwahl <c>(Waffe, Fahrwerk, Ausrüstung)</c> — das Ergebnis von
    /// <c>0x4B1FB0</c> hängt nur an diesen dreien und am Spielerblock, nicht am
    /// Platz. Darum der Dreierschlüssel und nicht der Platz: unsere eigenen
    /// Entwürfe tragen alle Platz −1 (<c>AcceptDesign</c>), und ein
    /// Platzschlüssel würde sie zusammenwerfen.
    ///
    /// <para>Null, solange der Spieler nie aufgewertet hat — dann gilt für ihn
    /// der Grundentwurf aus sec47 unverändert, so wie im Original alle acht
    /// Blöcke beim Start Kopien sind (<c>0x4B23C0</c>: erst <c>0x4B22E0</c>
    /// kopiert, dann <c>0x4B24B0</c> rechnet alle 1600 neu).</para></summary>
    private Dictionary<(int Waffe, int Fahrwerk, int Ausruestung), Simulation.DesignMath.Derived>?[]
        _entwuerfeJeSpieler = new Dictionary<(int, int, int), Simulation.DesignMath.Derived>?[8];

    /// <summary>Wie oft <see cref="EntwuerfeNachziehen"/> lief und wie viele
    /// Entwürfe es dabei gerechnet hat — für die Probe.</summary>
    public int EntwuerfeNachgezogen, EntwuerfeGerechnet;

    /// <summary>
    /// ⭐ <b>Der Entwurf, wie er für DIESEN Spieler gilt.</b> Die Fertigung
    /// (<c>SendOutOfDepot</c>, <c>SpawnReinforcement</c>) fragt hier, bevor sie
    /// den Tank, die Munition und die Werte in die neue Einheit schreibt — das
    /// Gegenstück zum Aufsteller <c>0x4B1840</c>, der @0x4B1BFE aus
    /// <c>sec47[Entwurf + 200·Spieler]</c> liest, nicht aus Block 0.
    ///
    /// <para>Gibt den Entwurf unverändert zurück, wenn der Spieler nie
    /// aufgewertet hat oder <c>--entwuerfe-global-alt</c> steht. Sonst den
    /// Schwanz aus <see cref="_entwuerfeJeSpieler"/>; fehlt die Bauteilwahl
    /// dort (ein Entwurf, der NACH dem letzten Nachziehen angelegt wurde), wird
    /// sie jetzt gerechnet und eingetragen — das Original hätte ihn schon beim
    /// Anlegen aus dem Spielerblock gerechnet (<c>0x4B2510</c> ruft
    /// <c>0x4B1FB0(platz, spieler)</c> @0x4B25A5).</para></summary>
    private Design EntwurfFuer(int spieler, Design d)
    {
        if (Simulation.DesignMath.EntwuerfeGlobalAlt) return d;
        if (spieler is < 0 or > 7) return d;
        var tafel = _entwuerfeJeSpieler[spieler];
        if (tafel == null) return d;
        var k = (d.Weapon, d.Propulsion, d.Equip);
        if (!tafel.TryGetValue(k, out var der))
        {
            der = Simulation.DesignMath.Compute(d.Weapon, d.Propulsion, d.Equip, spieler);
            tafel[k] = der;
            EntwuerfeGerechnet++;
        }
        return new Design(d.Name, d.Propulsion, d.Equip, d.Weapon, d.Available, d.Slot, der);
    }

    /// <summary>Der Grundstand eines Bauteils aus <c>component_stats.json</c>
    /// (= EXE-Block 0). Kommt aus dem schon vorhandenen Lader; steht hier als
    /// eine Zeile, damit der Rest dieser Datei nicht wissen muss, woher.
    /// </summary>
    private static byte[]? GrundBauteil(int bauteil) =>
        Simulation.DesignMath.GrundZeile(bauteil);

    /// <summary>Die geltende Zeile eines Bauteils für einen Spieler — nach
    /// allen Aufwertungen, die er bezahlt hat.</summary>
    public byte[]? BauteilFuer(int spieler, int bauteil)
    {
        if (spieler is < 0 or > 7 || bauteil is < 0 or >= 200) return null;
        BauteileVorbereiten();
        return _bauteile![spieler][bauteil];
    }

    // ---- die Aufwertungstafel ------------------------------------------------

    /// <summary>Die 35 belegten Zeilen aus <c>upgrade_table.json</c>, nach
    /// Bauteilnummer (Zeile <c>+0x01</c>).</summary>
    private Dictionary<int, byte[]>? _aufwertungTafel;

    private void AufwertungTafelLaden()
    {
        if (_aufwertungTafel != null) return;
        _aufwertungTafel = new Dictionary<int, byte[]>();
        string pfad = Core.Content.Path("Maps/upgrade_table.json");
        if (!FileAccess.FileExists(pfad))
        {
            GD.PrintErr("aufwertung: Maps/upgrade_table.json fehlt — keine Aufwertung " +
                        "moeglich, ohne Zahlen zu erfinden");
            return;
        }
        using var f = FileAccess.Open(pfad, FileAccess.ModeFlags.Read);
        if (f == null) return;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok ||
            json.Data.VariantType != Variant.Type.Dictionary) return;
        var root = json.Data.AsGodotDictionary<string, Variant>();
        if (!root.TryGetValue("rows", out var rv) ||
            rv.VariantType != Variant.Type.Dictionary) return;
        foreach (var kv in rv.AsGodotDictionary<string, Variant>())
        {
            var roh = HexBytes(kv.Value.AsString());
            if (roh.Length < 24) continue;
            _aufwertungTafel[roh[0x01]] = roh;      // ⭐ Schluessel ist Zeile +0x01
        }
    }

    private static byte[] HexBytes(string s)
    {
        int n = s.Length / 2;
        var b = new byte[n];
        for (int i = 0; i < n; i++)
            b[i] = (byte)((Hex(s[2 * i]) << 4) | Hex(s[2 * i + 1]));
        return b;
        static int Hex(char c) => c <= '9' ? c - '0' : (char.ToLowerInvariant(c) - 'a' + 10);
    }

    // ---- anwenden -------------------------------------------------------------

    /// <summary>
    /// ⭐⭐ <b>Eine Aufwertung anwenden — <c>0x4AAA20</c> + <c>0x4AA360</c> +
    /// <c>0x4AAA80</c> in einem.</b> Gibt zurück, welcher Arm gewürfelt wurde,
    /// oder −1, wenn zu dem Bauteil keine Zeile existiert.
    /// </summary>
    public int Aufwerten(int spieler, int bauteil)
    {
        if (AufwertungAlt) return -1;
        // ⚠ DER PRÜFSTANDSWEG, und er würfelt VOR dem Anwenden. Das Original
        // dreht die Reihenfolge um: das ANGEBOT rechnet aus dem Arm, der schon
        // in der Zeile steht (`+0x02`), und `0x4AAA20` würfelt erst beim
        // Anwenden den Arm der NÄCHSTEN Stufe (AN.4 — »das Angebot zeigt darum
        // eine ehrliche Vorschau«). Über viele Aufwertungen ist die Verteilung
        // dieselbe, die VORSCHAU aber nicht; wer sie braucht, geht über
        // <see cref="AngebotRechnen"/> und <see cref="AufwertungSchreiben"/>,
        // die die Forschung benutzt. Hier bleibt die alte Reihenfolge stehen,
        // damit die Messung vom 03.09. (400 Aufwertungen, 113/96/84/108) sich
        // weiter auf dieselbe Kette bezieht.
        // ⚠ ERST prüfen, DANN würfeln — in dieser Reihenfolge und nicht
        // umgekehrt. Ein Wurf, der auf eine ungültige Aufwertung fällt, wäre
        // aus der deterministischen Kette gezogen und würde sie verschieben:
        // die Wurfverteilung der Probe kam nach dem Umbau vom 05.09. als
        // 113/111/95/86 statt 113/96/84/108 heraus, weil die 400er-Schleife
        // nach Stufe 9 weiterwürfelte. Dieselbe Verteilung, andere Kette —
        // und eine andere Kette macht jede frühere Messung unvergleichbar.
        if (!AufwertungMoeglich(spieler, bauteil)) return -1;
        int wurf = AufwertungImmerTank ? 2 : Simulation.Determinism.Roll(4);
        var w = AngebotRechnen(spieler, bauteil, wurf);
        if (!w.Gueltig) return -1;
        AufwertungWurf[wurf]++;
        if (wurf == 2 && w.Fahrwerk) AufwertungTank++;
        return AufwertungSchreiben(spieler, w) ? wurf : -1;
    }

    /// <summary>Gibt es zu diesem Bauteil überhaupt eine Aufwertung, und steht
    /// sie noch unter Stufe 9? Dieselben zwei Fragen, die das Angebot stellt
    /// (<c>0x4AA9D2</c>/<c>0x4AA9DC</c>) — hier getrennt, damit
    /// <see cref="Aufwerten"/> sie VOR dem Wurf stellen kann.</summary>
    private bool AufwertungMoeglich(int spieler, int bauteil)
    {
        if (spieler is < 0 or > 7 || bauteil is < 0 or >= 200) return false;
        AufwertungTafelLaden();
        BauteileVorbereiten();
        if (_aufwertungTafel == null || !_aufwertungTafel.ContainsKey(bauteil))
        {
            AufwertungOhneZeile++;
            return false;
        }
        var c = _bauteile![spieler][bauteil];
        return c.Length >= 58 && c[0x01] < 9;
    }

    /// <summary>
    /// ⭐⭐ <b>Was eine Aufwertung brächte — die Rechnung des ANGEBOTS
    /// (<c>0x4AA360</c>), ohne irgendetwas zu ändern.</b>
    ///
    /// <para>Das Original rechnet die sechs Zuwächse beim Bauen der
    /// Angebotstafel aus und legt sie in den 50-Byte-Satz; der Laufsatz trägt
    /// sie dann bis zum Abschluss mit, und <c>0x4AAA80</c> schreibt genau diese
    /// Zahlen. Darum ist das hier eine eigene Funktion und keine Kopie im
    /// Forschungsteil: <b>eine</b> Arithmetik, zwei Aufrufer.</para>
    ///
    /// <para>Nur EIN Arm ist besetzt — welcher, sagt <paramref name="wurf"/>
    /// (im Original das Laufzeitfeld <c>Zeile+0x02</c>). <c>D0</c> und
    /// <c>A</c> wachsen immer.</para></summary>
    public AufwertungWerte AngebotRechnen(int spieler, int bauteil, int wurf)
    {
        if (spieler is < 0 or > 7 || bauteil is < 0 or >= 200)
            return default;
        AufwertungTafelLaden();
        BauteileVorbereiten();
        if (_aufwertungTafel == null ||
            !_aufwertungTafel.TryGetValue(bauteil, out var z))
            // 'for_vyv not found!' @0x4AAA3E — das Original bricht hier ab.
            // (Gezählt wird in AufwertungMoeglich, sonst zählte derselbe Fall
            //  auf dem Prüfstandsweg doppelt.)
            return default;
        var c = _bauteile![spieler][bauteil];
        if (c.Length < 58) return default;

        int stufe = c[0x01];
        if (stufe >= 9) return default;         // 0x4AAA8E: schon auf dieser Stufe
        int rest = 9 - stufe;                   // ⭐ der Teiler des Originals
        wurf &= 3;
        bool fahrwerk = bauteil >= 0xA0;        // `setae` auf >= 0xA0, 0x4AA3CB

        // Immer: +0x0E waechst um seinen Anteil am Rest bis zum Endwert.
        int d0 = (W(z, 0x04) - W(c, 0x0E)) / rest;
        // Immer, aber SETZEND (mov): Fahrwerk +0x13, Waffe +0x12.
        int a = fahrwerk ? (z[0x09] - (sbyte)c[0x13]) / rest
                         : (z[0x08] - (sbyte)c[0x12]) / rest;
        int b = 0, cc = 0, d = 0, e = 0;
        if (fahrwerk)
            switch (wurf)
            {
                case 0: b = z[0x06]; break;              // add @0x4AAB49
                case 1: cc = z[0x07]; break;             // mov @0x4AAB60
                case 2: d = W(z, 0x0E); break;           // ⭐ DER TANK, add @0x4AAB66
                case 3: e = -W(z, 0x14); break;
            }
        else
            switch (wurf)
            {
                case 0: b = W(z, 0x0A); break;           // add @0x4AAB00
                case 1: cc = W(z, 0x0C); break;          // add @0x4AAB14
                case 2: d = -W(z, 0x10); break;          // sub @0x4AAB24
                case 3: e = -W(z, 0x12); break;
            }
        return new AufwertungWerte(bauteil, fahrwerk, stufe, wurf,
                                   d0, a, b, cc, d, e, true);
    }

    /// <summary>Die sechs Zuwächse einer Aufwertung, wie das Angebot sie zeigt
    /// und der Laufsatz sie bis zum Abschluss trägt. <c>Stufe</c> ist die
    /// Stufe VOR der Aufwertung.</summary>
    public readonly record struct AufwertungWerte(int Bauteil, bool Fahrwerk, int Stufe,
                                                  int Wurf, int D0, int A, int B, int C,
                                                  int D, int E, bool Gueltig);

    /// <summary>
    /// ⭐⭐ <b>Die Aufwertung anwenden — <c>0x4AAA80</c>.</b> Schreibt die
    /// gerechneten Zahlen in die Bauteilzeile des Spielers, hebt die Stufe und
    /// zieht Entwürfe und lebende Einheiten nach.
    ///
    /// <para>⭐ <b>Der Wächter des Originals bleibt drin</b> (<c>0x4AAAAE</c>:
    /// nur, wenn die verzeichnete Stufe noch unter der neuen liegt). Er ist der
    /// Grund, warum zwei Abschlüsse desselben Vorhabens nicht zweimal
    /// wirken.</para></summary>
    public bool AufwertungSchreiben(int spieler, AufwertungWerte w)
    {
        if (!w.Gueltig || spieler is < 0 or > 7) return false;
        BauteileVorbereiten();
        var c = _bauteile![spieler][w.Bauteil];
        if (c.Length < 58) return false;
        if (c[0x01] >= w.Stufe + 1) return false;      // Wächter @0x4AAAAE

        Add16(c, 0x0E, w.D0);
        if (w.Fahrwerk)
        {
            c[0x13] = (byte)Clamp8(w.A);               // mov @0x4AAB43
            c[0x10] = (byte)Clamp8(c[0x10] + w.B);     // add @0x4AAB49
            // ⚠ Arm 1 SETZT (mov @0x4AAB60) — auch auf 0. Darum am Wurf
            // entschieden und nicht daran, ob der Wert 0 ist: eine Zeile mit
            // `+0x07 == 0` würde das Feld sonst stehen lassen statt es zu
            // nullen, und das wäre eine andere Zahl als im Original.
            if (w.Wurf == 1) c[0x11] = (byte)Clamp8(w.C);
            Add16(c, 0x1A, w.D);                       // add @0x4AAB66 ⭐ TANK
            c[0x21] = (byte)Clamp8(c[0x21] + w.E);
        }
        else
        {
            c[0x12] = (byte)Clamp8(w.A);               // mov @0x4AAAFA
            Add16(c, 0x14, w.B);
            Add16(c, 0x18, w.C);
            Add16(c, 0x1E, w.D);
            c[0x20] = (byte)Clamp8(c[0x20] + w.E);
        }

        c[0x01] = (byte)(w.Stufe + 1);                 // mov @0x4AAACA
        AufwertungAngewandt++;

        // 0x4B24B0 und 0x4B3CD0: Entwuerfe neu rechnen, dann die lebenden
        // Einheiten nachziehen.
        EntwuerfeNachziehen(spieler);
        EinheitenNachziehen(spieler);
        return true;
    }

    private static int W(byte[] b, int at) => at + 1 < b.Length ? b[at] | (b[at + 1] << 8) : 0;

    private static void Add16(byte[] b, int at, int d)
    {
        if (at + 1 >= b.Length) return;
        int v = (b[at] | (b[at + 1] << 8)) + d;
        v = Mathf.Clamp(v, 0, 0xFFFF);
        b[at] = (byte)v; b[at + 1] = (byte)(v >> 8);
    }

    private static int Clamp8(int v) => Mathf.Clamp(v, 0, 255);

    /// <summary>
    /// <c>0x4B3CD0</c> → <c>0x4B3AF0</c>: jede lebende Einheit des Spielers auf
    /// ihren neu gerechneten Entwurf nachziehen.
    ///
    /// <para>⭐ <b>»Voll bleibt voll«</b> — das Original hebt einen vollen Tank
    /// auf den neuen Höchstwert mit an und klemmt einen halbvollen nur, wenn er
    /// über den neuen Höchstwert hinausragt. Wer den Tank einer vollen Einheit
    /// nur im Höchstwert anhebt und den Istwert stehen lässt, macht aus einer
    /// Aufwertung eine Verschlechterung des Füllstands.</para></summary>
    private void EinheitenNachziehen(int spieler)
    {
        foreach (var e in _entities)
        {
            if (e.IsProp || e.Dead || e.Owner != spieler) continue;
            // ⚠⚠ 03.09.2026 — HIER STAND `e.Chassis`, UND DAS WAR FALSCH.
            // `Entity.Chassis` ist bei uns die RUMPFKLASSE (2, 4, 5 …), mit der
            // die Klangwahl und die Bilder arbeiten — NICHT die Bauteilnummer.
            // Die Fahrwerke des Originals liegen bei 0xA0…0xAF, und die steht
            // bei uns in `UnitType`: die sechs Einheiten der Kampagne 3 tragen
            // 161/163/164, und `component_stats[161][+0x1A] = 440`,
            // `[163] = 400`, `[164] = 300` — genau ihre Tanks. Mit `Chassis`
            // schlug die Probe Bauteil 4 nach, fand Tank 0, und die Kette sah
            // aus, als truege sie nicht.
            if (e.UnitType < 0 || e.FuelMax <= 0) continue;
            var c = BauteilFuer(spieler, e.UnitType);
            if (c == null) continue;
            int neu = W(c, 0x1A);
            if (neu <= 0 || neu == e.FuelMax) continue;
            bool warVoll = e.Fuel >= e.FuelMax;
            e.FuelMax = neu;
            e.Fuel = warVoll ? neu : Mathf.Min(e.Fuel, neu);
        }
    }

    /// <summary>
    /// ⭐ <c>0x4B24B0</c> → <c>0x4B1FB0</c>: die Entwürfe des Spielers neu
    /// rechnen, aus SEINEM Bauteilblock. Gefüllt am 03.09.2026; bis dahin war
    /// das ein Platzhalter, und ein Neubau bekam den alten Tank.
    ///
    /// <code>
    ///   0x4B24B0:                                   ; kein Argument
    ///       für spieler = 0..7:                     ; ebx, 0x4B24CA cmp ebx, 8
    ///           für entwurf = 1..199:               ; esi, 0x4B24C1 cmp esi, 0xC8
    ///               0x4B1FB0(entwurf, spieler)      ; über Thunk 0x402540
    /// </code>
    ///
    /// <para>⚠ <b>Zwei Abweichungen, beide mit Absicht:</b></para>
    /// <list type="number">
    ///   <item>Das Original rechnet ALLE ACHT Spieler; wir nur den, dessen
    ///   Block sich geändert hat. Die anderen sieben Blöcke sind unberührt,
    ///   also käme für sie dasselbe heraus wie beim Start — und der Start ist
    ///   bei uns der rohe sec47-Schwanz, den <c>--selftest-designs</c> für alle
    ///   586 benannten Entwürfe als rechengleich belegt. Wer die sieben trotzdem
    ///   neu rechnete, ersetzte belegte Zahlen durch dieselben Zahlen.</item>
    ///   <item>Das Original schreibt in die Entwurfstafel (sec47); wir halten
    ///   die Ergebnisse daneben (<see cref="_entwuerfeJeSpieler"/>) und lassen
    ///   die statische Bauliste <c>_designs</c> in Ruhe. Grund: die Bauliste
    ///   überlebt den Kartenwechsel, die Aufwertungen dürfen es nicht.</item>
    /// </list>
    ///
    /// <para>Gerechnet werden die Bauteilwahlen aller Entwürfe der Bauliste und
    /// des Spielerblocks in <c>_designBySlot</c> (Platz <c>200·spieler …
    /// 200·spieler+199</c>, das ist das <c>entwurf + 200·spieler</c> von
    /// <c>0x4B1FD0</c>).</para></summary>
    private void EntwuerfeNachziehen(int spieler)
    {
        if (spieler is < 0 or > 7) return;
        var tafel = new Dictionary<(int, int, int), Simulation.DesignMath.Derived>();
        void Rechne(Design d)
        {
            var k = (d.Weapon, d.Propulsion, d.Equip);
            if (tafel.ContainsKey(k)) return;
            tafel[k] = Simulation.DesignMath.Compute(d.Weapon, d.Propulsion, d.Equip, spieler);
            EntwuerfeGerechnet++;
        }
        if (_designs != null) foreach (var d in _designs) Rechne(d);
        foreach (var kv in _designBySlot)
            if (kv.Key / DesignsPerPlayer == spieler) Rechne(kv.Value);
        _entwuerfeJeSpieler[spieler] = tafel;
        EntwuerfeNachgezogen++;
    }
}
