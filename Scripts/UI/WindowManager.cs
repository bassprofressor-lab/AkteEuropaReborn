namespace AkteEuropaReborn.UI;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>Die Fensterverwaltung des Originals</b> — die Schicht zwischen dem
/// Zeichnen und der Klickauswertung: <b>öffnen, schliessen, nach vorn holen,
/// altern lassen</b>.
///
/// <para>⭐⭐ Gelesen am 22.08.2026, <c>OFFENE_FRAGEN.md</c> Abschnitt <b>BM</b>
/// (Revier 5, <c>0x442FB0…0x4505F0</c>, 52 Funktionen). Bis dahin hatten wir
/// gar keine Verwaltung: jedes Fenster war ein einzelner Godot-Knoten, den
/// irgendwer anlegte und irgendwer wieder wegnahm. Sechs Regeln des Originals
/// hatten dadurch keinen Ort, an dem sie hätten stehen können.</para>
///
/// <para><b>Was hier steht, und woher es kommt:</b></para>
/// <list type="number">
///   <item><b>Die Doppelöffnungssperre</b> (BM.2). Ein Fenster gibt es je
///   <see cref="Art"/> einmal — Objektfenster je <see cref="Kennung"/> einmal.
///   ⭐ Das ist nicht geraten: von den 31 Öffnern mit Artwache prüfen <b>30</b>
///   genau die Art, die sie danach anlegen. Bei freier Wahl unter 48 Arten
///   wären 0,6 Treffer zu erwarten.</item>
///   <item><b>Die Reihenfolge</b> (BM.4). <b>Platz 0 ist oben.</b> Das steht
///   nirgends, es folgt aus zwei Suchläufen: die Maus (<c>0x446DE0</c>) und die
///   Tastatur (<c>0x413EC4</c>) gehen die Liste <b>von 0 aufwärts</b> und
///   brechen beim ersten Treffer ab — läge 0 unten, träfe man durch verdeckte
///   Fenster hindurch.</item>
///   <item><b>Neu kommt HINTEN hinein, dann nach vorn</b> — zwei getrennte
///   Schritte (<c>0x441270</c>, dann <c>0x44FC20</c>). ⭐ Genau darum können
///   vier Öffner den zweiten weglassen: <see cref="BleibtHinten"/>.</item>
///   <item><b>Die Blende</b> (BM.10): <b>4 Bilder auf</b> mit Klang 307,
///   <b>6 Bilder zu</b>. Das Original klappt den eigenen Punktpuffer zur
///   Mittelzeile zusammen und füllt den Rand mit <c>0xFF</c> = durchsichtig.</item>
///   <item><b>Die Lebensdauer</b> (BM.11): <c>+0xAD22</c> wird <b>alle 20
///   Takte</b> um eins heruntergezählt; bei 0 schliesst sich das Fenster.
///   <c>0x441270</c> setzt sie auf <b>0 = niemals</b>, und nur das
///   Meldungsfenster (Art 13, <c>0x4469A0</c>, 44 Rufstellen) bekommt sie als
///   Argument. <b>Meldungen verschwinden also von allein, und der Rufer
///   bestimmt, wie lange sie stehen.</b></item>
///   <item><b>Zwanzig Plätze.</b> ⭐ Zehn Fundstellen im Code nennen dieselben
///   zwei Rohzahlen: Schrittweite <c>0xAD24</c> = 44 324 und die Endadresse
///   <c>0x99C42A</c> = <c>0x8C3D5A + 20·44324</c>, auf das Byte genau.</item>
/// </list>
///
/// <para>⚠ <b>Was hier NICHT steht:</b> das Zeichnen (Abschnitt BA) und die
/// Klickauswertung (<c>ui_action</c>). Die Verwaltung kennt ihre Fenster nur
/// als Knoten mit Art und Kennung — sie malt keines.</para>
///
/// <para>⚠ <b>Unsere Abweichung, und sie ist bewusst:</b> das Original arbeitet
/// die Blende im eigenen <b>Punktpuffer</b> ab. Wir haben keine Punktpuffer je
/// Fenster, sondern Godot-Knoten; die Blende ist darum eine <b>Skalierung um
/// die Mittelachse</b>. Das Ergebnis sieht gleich aus (das Fenster klappt zur
/// Mittellinie zusammen), der Weg ist ein anderer. Die BILDZAHLEN 4 und 6 sind
/// dagegen die des Originals, und sie sind das, was man sieht.</para>
/// </summary>
public static class WindowManager
{
    /// <summary>⭐ Zwanzig Plätze — zehn Fundstellen, dieselbe Rohzahl.</summary>
    public const int MaxFenster = 20;

    // ---- die Fensterarten -------------------------------------------------
    //
    // Die Nummern des Originals, soweit belegt (BM.1a). Hier stehen nur die,
    // die wir schon haben oder gleich brauchen; die Tafel ist vollstaendig in
    // OFFENE_FRAGEN BM.1a.

    /// <summary>Art 13 — das MELDUNGSFENSTER, 44 Rufstellen. ⭐ Das einzige, das
    /// eine Standzeit mitbekommt (<c>0x4469A0</c>).</summary>
    public const int ArtMeldung = 13;

    /// <summary>Art 44 — die Statuszeile. Bleibt hinten.</summary>
    public const int ArtStatuszeile = 44;

    /// <summary>Art 45 — der Ladebalken »Laden…«.</summary>
    public const int ArtLaden = 45;

    /// <summary>
    /// ⚠ <b>UNSERE Nummern, oberhalb der 48 des Originals.</b> Dieselbe Regel
    /// wie bei <see cref="Simulation.Commands.CommandOp.OursFirst"/>: Fenster,
    /// die wir haben und für die im Original keine Art nachgewiesen ist,
    /// bekommen eine Nummer, die mit keiner gelesenen kollidieren kann.
    ///
    /// <para>Das Gruppen- und das Merkpunktfenster stehen NICHT in der
    /// Öffnertafel BM.1a. Ihre Öffner sind zwar gelesen (<c>0x442C70</c> bzw.
    /// <c>0x442D40</c>), ihre Fensterart aber nicht — und eine Zahl zu raten,
    /// nur damit sie »original« aussieht, wäre schlimmer als eine ehrlich
    /// eigene.</para>
    /// </summary>
    public const int UnsereErste = 100;
    public const int ArtGruppen = 100, ArtMerkpunkte = 101;

    /// <summary>⭐ Vier Bilder auf (BM.10, <c>word[0x87B054] &lt; 4</c>).</summary>
    public const int BilderAuf = 4;

    /// <summary>⭐ Sechs Bilder zu (<c>byte[0x87ADFC] &lt; 6</c>).</summary>
    public const int BilderZu = 6;

    /// <summary>⭐ Klang 307 = <c>0x133</c>, beim Aufgehen über
    /// <c>0x4047E0</c>.</summary>
    public const int KlangAuf = 307;

    /// <summary>⭐ Die Lebensdauer wird alle 20 Takte um eins gezählt
    /// (<c>word[0x4FA248] % 20 == 0</c>).</summary>
    public const int StandzeitTakte = 20;

    /// <summary>
    /// Die vier Arten, die <c>0x44FC20</c> NICHT rufen und darum liegen
    /// bleiben, wo <c>0x441270</c> sie hingelegt hat — hinten.
    ///
    /// <para>Art 1 = Befehlsmenü (<c>0x444490</c>), Art 2 =
    /// <c>0x4445D0</c>/<c>0x444680</c>, Art 44 = Statuszeile (<c>0x444300</c>).
    /// ⚠ Für die Statuszeile ist das offensichtlich richtig; wer alles nach
    /// vorn holt, bekommt sie über dem Bauschirm.</para>
    /// </summary>
    public static bool BleibtHinten(int art) => art is 1 or 2 or 44;

    public sealed class Fenster
    {
        /// <summary>Die Fensterart, <c>byte[+0x00]</c> im Original.</summary>
        public int Art;

        /// <summary>Die Objektkennung, <c>word[+0x0C]</c> — bei Gebäudefenstern
        /// der Platz. <c>-1</c> = Einzelstück, es gibt es nur einmal.</summary>
        public int Kennung = -1;

        /// <summary>Der Knoten, den wir statt eines Punktpuffers haben.</summary>
        public CanvasItem? Knoten;

        /// <summary><c>+0xAD22</c>. 0 = niemals von selbst schliessen.</summary>
        public int Standzeit;

        /// <summary>Bildzähler der Blende. <c>&gt;= 0</c> heisst »geht gerade
        /// auf«, <c>&lt; 0</c> heisst »offen«.</summary>
        public int AufBild = 0;

        /// <summary>Bildzähler des Zugehens, <c>-1</c> = geht nicht zu.</summary>
        public int ZuBild = -1;
    }

    /// <summary>
    /// Die Reihenfolgeliste <c>0x87AFF8</c>. <b>Platz 0 ist oben.</b>
    /// </summary>
    private static readonly List<Fenster> _liste = new();

    /// <summary>Wie viele Fenster offen sind (<c>byte[0x4FD64C]</c>).</summary>
    public static int Anzahl => _liste.Count;

    /// <summary>Die Liste von oben nach unten — für Prüfstände.</summary>
    public static IReadOnlyList<Fenster> Liste => _liste;

    /// <summary>Wie oft ein Öffnen an der Doppelöffnungssperre gescheitert ist,
    /// und wie viele Fenster von selbst zugegangen sind.</summary>
    public static int Abgewiesen, VonSelbstZu;

    /// <summary>Alles vergessen — beim Kartenwechsel.</summary>
    public static void Leeren()
    {
        foreach (var f in _liste) Fertig(f);
        _liste.Clear();
        Abgewiesen = 0;
        VonSelbstZu = 0;
        _takt = 0;
    }

    /// <summary>Ist ein Fenster dieser Art (und Kennung) schon offen?
    /// ⭐ Die Wache aus BM.2 — bei <paramref name="kennung"/> = -1 zählt allein
    /// die Art.</summary>
    public static Fenster? Offen(int art, int kennung = -1)
    {
        foreach (var f in _liste)
            if (f.Art == art && (kennung < 0 || f.Kennung == kennung)) return f;
        return null;
    }

    /// <summary>
    /// <b>Ein Fenster öffnen</b> — die Schablone aus BM.2, in ihrer
    /// Reihenfolge.
    /// </summary>
    /// <param name="standzeit">Wie viele Zwanzig-Takt-Schritte es steht.
    /// <b>0 = niemals von selbst zu</b>, wie <c>0x441270</c> es setzt. Nur das
    /// Meldungsfenster bekommt hier etwas anderes.</param>
    /// <returns>Das Fenster, oder <c>null</c>, wenn es schon offen war.</returns>
    public static Fenster? Oeffnen(int art, CanvasItem? knoten, int kennung = -1,
                                   int standzeit = 0)
    {
        // 1. Die Doppelöffnungssperre. ⚠ Zuerst, nicht zuletzt — das Original
        //    prüft VOR dem Anlegen, sonst hinge ein zweiter Anleger in der Luft.
        if (Offen(art, kennung) != null) { Abgewiesen++; return null; }

        // ⚠ Zwanzig Plätze. Das Original prüft das an dieser Stelle nicht — es
        // hat 20 feste Plätze und läuft in den einundzwanzigsten hinein. Wir
        // weisen ab und sagen es, statt still zu überschreiben.
        if (_liste.Count >= MaxFenster)
        {
            GD.Print($"fenster: Art {art} nicht geoeffnet — alle {MaxFenster} Plaetze belegt");
            Abgewiesen++;
            return null;
        }

        var f = new Fenster
        {
            Art = art, Kennung = kennung, Knoten = knoten,
            Standzeit = standzeit, AufBild = 0, ZuBild = -1,
        };

        // 2. HINTEN in die Liste (0x441270).
        _liste.Add(f);

        // 3. Und nach VORN holen — ausser den vier Arten, die es nicht tun.
        if (!BleibtHinten(art)) NachVorn(f);

        Blende(f);
        return f;
    }

    /// <summary><c>0x44FC20</c> — auf Platz 0, alle anderen eins nach hinten.</summary>
    public static void NachVorn(Fenster? f)
    {
        if (f == null || _liste.Count == 0 || _liste[0] == f) return;
        if (!_liste.Remove(f)) return;
        _liste.Insert(0, f);
        Zeichenfolge();
    }

    /// <summary><c>0x446DE0</c> — der Maustreffer holt sein Fenster nach vorn.
    /// Sucht von 0 aufwärts und bricht beim ERSTEN Treffer ab; das ist genau
    /// der Suchlauf, aus dem folgt, dass 0 oben ist.</summary>
    public static Fenster? Treffer(Vector2 punkt)
    {
        foreach (var f in _liste)
        {
            if (f.Knoten is not Control c || !c.Visible) continue;
            if (!c.GetGlobalRect().HasPoint(punkt)) continue;
            NachVorn(f);
            return f;
        }
        return null;
    }

    /// <summary><b>Schliessen anstossen</b> — das Fenster geht über
    /// <see cref="BilderZu"/> Bilder zu und verschwindet dann.</summary>
    public static void Schliessen(int art, int kennung = -1)
        => Schliessen(Offen(art, kennung));

    public static void Schliessen(Fenster? f)
    {
        if (f == null || f.ZuBild >= 0) return;
        f.ZuBild = 0;
    }

    /// <summary>Sofort weg, ohne Blende — für den Kartenwechsel.</summary>
    public static void Wegnehmen(Fenster? f)
    {
        if (f == null) return;
        Fertig(f);
        _liste.Remove(f);
        Zeichenfolge();
    }

    /// <summary>Der Knoten ist durch: unsichtbar, und die Blendenskalierung
    /// zurueck. ⚠ Ohne das Zuruecksetzen stuende ein wieder geoeffnetes
    /// Fenster als flacher Strich da — die Skalierung ueberlebt das Verstecken.</summary>
    private static void Fertig(Fenster f)
    {
        if (f.Knoten is not Control c) return;
        c.Visible = false;
        c.Scale = Vector2.One;
    }

    private static int _takt;

    /// <summary>
    /// <b>Der Fenstertakt</b> — <c>0x4505F0</c> und <c>0x44FB10</c> in einem.
    ///
    /// <para>⚠ Er gehört an den SIMULATIONSTAKT, nicht an die Bildrate: die
    /// Lebensdauer zählt in Takten, und ein Meldungsfenster darf nicht auf einem
    /// schnellen Rechner kürzer stehen als auf einem langsamen.</para>
    /// </summary>
    public static void Takt()
    {
        _takt++;

        // ---- die Blenden, je Takt ein Bild ---------------------------------
        for (int i = _liste.Count - 1; i >= 0; i--)
        {
            var f = _liste[i];
            if (f.ZuBild >= 0)
            {
                if (f.ZuBild < BilderZu) { f.ZuBild++; Blende(f); }
                else { Fertig(f); _liste.RemoveAt(i); Zeichenfolge(); }
                continue;
            }
            if (f.AufBild >= 0 && f.AufBild < BilderAuf) { f.AufBild++; Blende(f); }
        }

        // ---- die Lebensdauer, alle 20 Takte --------------------------------
        if (_takt % StandzeitTakte != 0) return;
        foreach (var f in _liste)
        {
            if (f.Standzeit <= 0 || f.ZuBild >= 0) continue;   // 0 = niemals
            if (--f.Standzeit > 0) continue;
            Schliessen(f);
            VonSelbstZu++;
        }
    }

    /// <summary>
    /// Die Blende auf den Knoten bringen.
    ///
    /// <para>⚠ UNSERE Umsetzung: das Original klappt den Punktpuffer zur
    /// Mittelzeile zusammen (<c>0x44F8B0</c>), wir skalieren um die Mittelachse.
    /// Gleicher Anblick, anderer Weg — siehe den Klassenkopf.</para>
    /// </summary>
    private static void Blende(Fenster f)
    {
        if (f.Knoten is not Control c) return;
        float anteil = f.ZuBild >= 0
            ? 1f - Mathf.Clamp(f.ZuBild / (float)BilderZu, 0f, 1f)
            : Mathf.Clamp(f.AufBild / (float)BilderAuf, 0f, 1f);

        c.PivotOffset = c.Size * 0.5f;         // um die MITTELLINIE, nicht die Ecke
        c.Scale = new Vector2(1f, Mathf.Max(anteil, 0.001f));
        c.Visible = anteil > 0.001f;

        // ⭐ Der Klang kommt beim AUFGEHEN, und zwar wenn die Blende fertig ist
        // (0x44FC90 malt erst bei >= 4 und spielt dann 0x133).
        if (f.ZuBild < 0 && f.AufBild == BilderAuf)
        {
            f.AufBild = -1;                    // offen, nicht mehr am Aufgehen
            Audio.SoundBankPlayer.Play(KlangAuf);
        }
    }

    /// <summary>Platz 0 oben — in Godot zeichnet das SPÄTERE Kind oben, also
    /// wird die Liste rückwärts auf die Knotenreihenfolge gelegt.</summary>
    private static void Zeichenfolge()
    {
        for (int i = _liste.Count - 1, k = 0; i >= 0; i--, k++)
        {
            if (_liste[i].Knoten is not Node n) continue;
            var eltern = n.GetParent();
            if (eltern != null && eltern.GetChildCount() > k) eltern.MoveChild(n, k);
        }
    }
}

public static partial class WindowManagerCheck
{
    /// <summary>
    /// <c>--fenster-check</c> — <b>die sechs Regeln der Fensterverwaltung</b>,
    /// jede mit ihrer Gegenprobe (22.08.2026, OFFENE_FRAGEN <b>BM</b>).
    ///
    /// <para>⚠ Gemessen wird ohne Godot-Knoten: die Verwaltung führt eine
    /// Liste, und genau die wird geprüft. Ein Lauf, der erst ein Fenster malen
    /// müsste, hinge an der Oberfläche statt an der Regel.</para>
    /// </summary>
    public static string Lauf()
    {
        var sb = new System.Text.StringBuilder("fenster-check\n");
        WindowManager.Leeren();
        bool alles = true;
        void Sag(string was, bool ok)
        {
            sb.Append($"  {was}: {(ok ? "richtig" : "FALSCH")}\n");
            alles &= ok;
        }

        // 1. Doppelöffnungssperre: dieselbe Art nur einmal
        var a = WindowManager.Oeffnen(19, null);
        var b = WindowManager.Oeffnen(19, null);
        Sag($"Art 19 zweimal geoeffnet -> {(b == null ? "abgewiesen" : "ZWEI")}, "
            + $"offen {WindowManager.Anzahl} (erwartet 1)",
            a != null && b == null && WindowManager.Anzahl == 1);

        // 1b. Gegenprobe: EINE ANDERE Art darf sehr wohl dazu
        var c = WindowManager.Oeffnen(20, null);
        Sag($"Art 20 dazu -> offen {WindowManager.Anzahl} (erwartet 2)",
            c != null && WindowManager.Anzahl == 2);

        // 1c. Objektfenster: je Kennung eines
        var d1 = WindowManager.Oeffnen(23, null, kennung: 7);
        var d2 = WindowManager.Oeffnen(23, null, kennung: 7);
        var d3 = WindowManager.Oeffnen(23, null, kennung: 8);
        Sag($"Art 23 Kennung 7 zweimal -> {(d2 == null ? "abgewiesen" : "ZWEI")}, "
            + "Kennung 8 dazu -> " + (d3 != null ? "geoeffnet" : "ABGEWIESEN"),
            d1 != null && d2 == null && d3 != null);

        // 2. Platz 0 ist oben: das zuletzt geöffnete steht vorn
        Sag($"zuletzt geoeffnet (Art {WindowManager.Liste[0].Art}) steht auf Platz 0",
            WindowManager.Liste[0].Art == 23 && WindowManager.Liste[0].Kennung == 8);

        // 3. Nach vorn holen
        WindowManager.NachVorn(a);
        Sag($"Art 19 nach vorn geholt -> Platz 0 ist Art {WindowManager.Liste[0].Art}",
            WindowManager.Liste[0] == a);

        // 4. Die vier Arten, die HINTEN bleiben
        var st = WindowManager.Oeffnen(WindowManager.ArtStatuszeile, null);
        Sag($"Statuszeile (Art 44) geoeffnet -> Platz 0 ist Art {WindowManager.Liste[0].Art}, "
            + $"sie selbst auf Platz {WindowManager.Liste.Count - 1}",
            WindowManager.Liste[0] == a && WindowManager.Liste[^1] == st);
        // Gegenprobe: eine gewoehnliche Art kommt sehr wohl nach vorn
        var gew = WindowManager.Oeffnen(29, null);
        Sag("Gegenprobe: Art 29 kommt nach vorn", WindowManager.Liste[0] == gew);

        // 5. Die Lebensdauer: 0 = niemals, n = nach 20*n Takten
        WindowManager.Leeren();
        var ewig = WindowManager.Oeffnen(19, null, standzeit: 0);
        var kurz = WindowManager.Oeffnen(WindowManager.ArtMeldung, null, standzeit: 2);
        for (int t = 0; t < 20 * 2 - 1; t++) WindowManager.Takt();
        bool nochDa = WindowManager.Offen(WindowManager.ArtMeldung) != null;
        for (int t = 0; t < 1 + WindowManager.BilderZu + 1; t++) WindowManager.Takt();
        bool jetztWeg = WindowManager.Offen(WindowManager.ArtMeldung) == null;
        bool ewigDa = WindowManager.Offen(19) != null;
        Sag($"Standzeit 2: nach 39 Takten {(nochDa ? "noch da" : "SCHON WEG")}, "
            + $"nach 40 + Zublende {(jetztWeg ? "weg" : "NOCH DA")}; "
            + $"Standzeit 0 {(ewigDa ? "bleibt" : "IST WEG")}",
            ewig != null && kurz != null && nochDa && jetztWeg && ewigDa);

        // 6. Zwanzig Plaetze
        WindowManager.Leeren();
        for (int i = 0; i < 25; i++) WindowManager.Oeffnen(200 + i, null);
        Sag($"25 Fenster geoeffnet -> {WindowManager.Anzahl} offen (erwartet "
            + $"{WindowManager.MaxFenster})",
            WindowManager.Anzahl == WindowManager.MaxFenster);

        WindowManager.Leeren();
        sb.Append(alles ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
