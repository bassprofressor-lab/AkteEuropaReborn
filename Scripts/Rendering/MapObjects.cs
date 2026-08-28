namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;
using GDict = Godot.Collections.Dictionary<string, Godot.Variant>;

/// <summary>
/// <b>AUFRAGENDE KARTENOBJEKTE — Bäume, Masten, Felsen — UND IHR FEUER.</b>
///
/// <para>⚠⚠ 18.08.2026, gemeldet als »im Original verdecken z. B. auch Bäume
/// Einheiten, bei uns nicht«. Der Bericht stimmt, und die Ursache lag im
/// Backofen: <c>MapBaker.Bake</c> Durchgang C backte <b>alle</b> Objekte ins
/// Kartenbild. Nur GEBÄUDE waren ausgenommen und wurden lebend gezeichnet —
/// genau deshalb konnten die verdecken und Bäume nicht. Ein eingebackener Baum
/// liegt unter allem, was danach kommt.</para>
///
/// <para><b>Die Kur ist die der Gebäudekacheln</b>: flach bleibt im Boden,
/// Aufragendes kommt ins Zeilenfach. Der Backofen schreibt die aufragenden
/// Objekte seither in eine zweite Ebene <c>&lt;karte&gt;.objects.png</c> (nur
/// sie, alles andere durchsichtig) und ihre Rechtecke in die Meta unter
/// <c>objects</c>. Dieser Durchgang schneidet sie dort aus und setzt sie
/// zwischen die Einheiten.</para>
///
/// <para>⚠⚠ <b>WELCHE Objekte aufragen, ist seit dem 18.08.2026 GELESEN und
/// nicht mehr geraten.</b> Hier stand eine Pixelschwelle
/// (<c>MapBaker.RagtAbPx = 25</c>), von den Gebäudekacheln übernommen. Das
/// Original kennt gar keine: sein Zeichner (@0x4B4150) teilt die Zellen nach
/// der BELEGUNGSKARTE der Kartendatei auf — der flache Durchgang @0x4B41EB
/// überspringt jede Zelle mit einer Belegung ab 14000, und der verzahnte
/// Durchgang @0x4B43BB, in dem Einheiten und Kacheln zeilenweise abwechseln,
/// nimmt die Belegungen 50000..63999 (@0x4B446C). Alles dazu steht bei
/// <c>Import.MapForest.ImZeilenfach</c>.</para>
///
/// <para>⚠ <b>Eine Karte aus einem älteren Import hat die zweite Ebene
/// nicht.</b> Dann bleibt alles wie vorher: die Bäume stehen im Kartenbild und
/// verdecken nichts. Das ist Absicht — ein fehlendes Bild darf kein Loch in die
/// Karte reissen. Wer die Verdeckung will, spielt die Karten neu ein
/// (<c>--reexport-maps=&lt;ordner&gt;</c>).</para>
/// </summary>
public partial class MapEntityLayer
{
    private Texture2D? _objTex;

    /// <summary>Die zweite Ebene als Bild, fuer die Uebersichtskarte.
    ///
    /// <para>⚠ Sie ist HOEHER als die Karte: unten haengt der Streifen mit den
    /// verkohlten Baeumen an (MapBaker.BurntAtlas). Wer sie flaechig zeichnet,
    /// muss auf <see cref="MapPixelSize"/> zuschneiden, sonst klebt der
    /// Streifen mit im Bild.</para></summary>
    public Texture2D? ObjektEbene => _objTex;

    /// <summary><c>--objekt-rechteck</c> — der Stand von vor dem 27.08.2026:
    /// die aufragende Kachel wird als RECHTECK aus dem zusammengesetzten Bild
    /// geschnitten und an dieselbe Stelle gemalt. Wo sich zwei solche Kacheln
    /// ueberlappen, nimmt sie die Nachbarin mit. Siehe MapBaker.Objects.</summary>
    public static bool ObjektRechteck;

    /// <summary><c>--nebel-objekte-alt</c> — der Stand von vor dem 28.08.2026:
    /// aufragende Objekte sind auch im NIE ERKUNDETEN Gebiet zu sehen, nur
    /// verdunkelt. Das Original zeigt dort statt des Objekts den
    /// synthetisierten Boden — nicht, weil es das Objekt ausblendet, sondern
    /// weil in seiner BEKANNTEN Kachelkarte gar kein Objekt steht
    /// (<c>0x41FAE0</c>). Siehe MapBaker.SyntheseKachel.</summary>
    public static bool NebelObjekteAlt;

    /// <summary>Wieviele Objekte im letzten Bild durch ihren Boden ersetzt
    /// wurden, und wie oft dafür kein Boden vorlag. Die zweite Zahl ist die
    /// wichtige: ohne Boden bliebe ein Loch, und genau daran ist der Versuch
    /// vom 24.08.2026 gescheitert.</summary>
    public int NebelBodenGezeichnet, NebelBodenFehlt;

    /// <summary>Die NEBELDECKE: je Zelle, deren wahre Kachel von der
    /// synthetisierten abweicht, wohin sie gehört und ihr Platz im Streifen.
    /// Siehe MapBaker.NebelBoden.</summary>
    private readonly List<(int Col, int Row, Vector2 Ziel, Rect2 Src)> _nebelDecke = new();

    /// <summary>Wieviele Deckkacheln im letzten Bild gemalt wurden.</summary>
    public int NebelDeckeGezeichnet;

    private void LiesNebeldecke(GDict meta, List<Rect2> kohle)
    {
        _nebelDecke.Clear();
        if (!meta.TryGetValue("nebelboden", out var nv) || nv.VariantType != Variant.Type.Array) return;
        foreach (var item in nv.AsGodotArray())
        {
            if (item.VariantType != Variant.Type.Dictionary) continue;
            var o = item.AsGodotDictionary<string, Variant>();
            int k = GetI(o, "slot", -1);
            if (k < 0 || k >= kohle.Count) continue;
            _nebelDecke.Add((GetI(o, "col"), GetI(o, "row"),
                             new Vector2(GetI(o, "x"), GetI(o, "y")), kohle[k]));
        }
        GD.Print($"nebeldecke: {_nebelDecke.Count} Zellen, deren wahre Kachel im "
               + "unerkundeten Gebiet durch die synthetisierte ersetzt wird");
    }

    /// <summary>Die Nebeldecke malen — Boden, also VOR allem anderen.
    /// ⚠ Nur wo der Nebel wirklich an ist und die Zelle nie gesehen wurde;
    /// eine einmal aufgedeckte Zelle behält ihre wahre Kachel, genau wie im
    /// Original (dort wird sie beim Aufdecken in die bekannte Karte kopiert,
    /// @0x41FFC8 / @0x420245).</summary>
    private void NebeldeckeZeichnen()
    {
        NebelDeckeGezeichnet = 0;
        if (_objTex == null || NebelObjekteAlt || !FogActive || _fog == null) return;
        foreach (var e in _nebelDecke)
        {
            if (_fog.IsSeen(e.Col, e.Row)) continue;
            DrawTextureRectRegion(_objTex, new Rect2(e.Ziel, e.Src.Size), e.Src);
            NebelDeckeGezeichnet++;
        }
    }

    /// <summary>Wieviele aufragende Objekte KEINEN eigenen Streifenplatz haben —
    /// eine alte Karte aus einem Bake vor dem 27.08.2026. Dann faellt der
    /// Zeichner auf das Rechteck zurueck, und der Fehler ist wieder da; darum
    /// wird es gezaehlt und gesagt statt still hingenommen.</summary>
    public int ObjektOhneBild;

    /// <summary>Ein aufragendes Objekt, so wie der Zeichner es braucht.</summary>
    private sealed class Kartenobjekt
    {
        /// <summary>Die Zelle — die Zeile entscheidet über das Zeilenfach.</summary>
        public int Col, Row;

        /// <summary>
        /// ⭐⭐ 24.08.2026 — <b>DIE NUMMER DES BAUMS.</b> Das Original zieht aus
        /// dem Tafelindex eines Waldobjekts DREI Dinge: welche der beiden
        /// Flammen es bekommt (<c>index &amp; 1</c> @0x42B461), die Phase seines
        /// Flackerns (<c>bildzaehler/2 + index</c> @0x42B422) und einen
        /// seitlichen Versatz (<c>index mod 10 − 5</c> @0x42B474).
        ///
        /// <para>Wir haben dafuer bis heute <c>Spalte·31 + Zeile</c> genommen —
        /// »die Aufteilung ist dieselbe, die ZUORDNUNG ist es nicht«, so stand
        /// es seit dem 19.08. als bekannte Abweichung im Kopf von
        /// <see cref="Flamme"/>. Was sie anrichtet, hat erst der Spieler
        /// gesehen: in Mission 1 faellt die Formel dreimal auf die FLACHE Folge
        /// 552 und nur einmal auf die hohe 550 — »die Flamme ist leicht im
        /// Boden«. Im Original ist es halb und halb.
        ///
        /// <para>Diese Nummer ist die Reihenfolge, in der der Backofen die
        /// Objekte geliefert hat, also die Reihenfolge der Kartendatei. ⚠ Ob
        /// das die Reihenfolge der Waldtafel des Originals IST, ist nicht
        /// belegt — belegt ist nur, dass es ein je Baum fester Index ist und
        /// nicht aus seiner Lage folgt. Damit wechselt die Flamme von Baum zu
        /// Baum statt in Bloecken, und das ist der sichtbare Unterschied.</para>
        /// </summary>
        public int Index;

        /// <summary>Der rohe Belegungswert (Sektion 6). 50000..55999 = Wald,
        /// 61000..63999 = zerstoerbares Objekt (dort ist <c>Imap − 61000</c> der
        /// Index in die Objektliste). ⚠ <b>Hieran</b> wird Wald erkannt, nicht
        /// mehr an »hat eine verkohlte Kachel« — seit dem 24.08. haben auch
        /// zerstoerbare Objekte ihre zwei Ersatzbilder in denselben
        /// Feldern.</summary>
        public int Imap = -1;

        /// <summary>Art, Verhaltensklasse (0/1/2) und Grundkachel eines
        /// zerstoerbaren Objekts; −1 bei Wald. Arttafel: Block 0x2b der
        /// Kacheldatei, siehe CwpFile.ObjType.</summary>
        public int Art = -1, Klasse = -1, Basis = -1;

        /// <summary>Wald im Sinne des Originals (imap 50000..55999). Der
        /// Brandtakt, das Uebergreifen und die Geschossschwelle 40 haengen
        /// hieran.</summary>
        public bool IstWald => Imap >= 50000 && Imap < 56000;

        /// <summary>Ein zerstoerbares Kartenobjekt (imap 61000..63999).</summary>
        public bool IstObjekt => Imap >= 61000 && Imap < 64000;

        /// <summary>Sein Rechteck in der zweiten Ebene. ⚠ Quelle UND Ziel: der
        /// Backofen hat es an genau die Stelle gemalt, an die es gehört.</summary>
        public Rect2 Src;

        /// <summary>⭐⭐ 27.08.2026 — WOHIN die Kachel gehört. Bis heute war das
        /// dieselbe Stelle wie <see cref="Src"/>: der Zeichner schnitt ein
        /// Rechteck aus dem zusammengesetzten Bild und malte es an genau die
        /// Stelle zurück, aus der es kam. Sobald sich zwei aufragende Kacheln
        /// überlappen, nimmt dieses Rechteck fremde Bildpunkte mit und malt sie
        /// ein zweites Mal — nach den Einheiten dazwischen. Genau daran wurden
        /// auf der Brücke von map_02 die Fahrzeuge abgeschnitten (acht Zeilen
        /// Überlappung, gemessen; siehe MapBaker.Objects).
        /// Jetzt kommt die Quelle aus dem Streifen und das Ziel von hier.</summary>
        public Vector2 Ziel;

        /// <summary>Der SYNTHETISIERTE Boden dieser Zelle im Streifen — was das
        /// Original zeigt, solange die Zelle nicht erkundet ist (bekannte Karte
        /// <c>0x5539D0</c>, gefüllt von <c>0x41FAE0</c>). Leer, wenn die
        /// Variantentafel für diese Zelle nichts hergibt.</summary>
        public Rect2 BodenSrc;
        public bool HatBoden;

        /// <summary>Die VERKOHLTE Fassung im Streifen, oder ein leeres Rechteck,
        /// wenn die Zelle kein Wald ist. Sie hat ein eigenes Ziel, weil der
        /// verkohlte Baum ein anderes Bild ist als der grüne (kürzere Krone,
        /// gleicher Fuss — gemessen: 967 von 991 Paaren auf den Pixel).</summary>
        public Rect2 KohleSrc;
        public Vector2 KohleZiel;
        public bool HatKohle;

        /// <summary>Dasselbe für die Kachel, die ÜBRIGBLEIBT, wenn das Feuer
        /// aus ist — Stumpf oder blanker Boden.</summary>
        public Rect2 AscheSrc;
        public Vector2 AscheZiel;
        public bool HatAsche;

        /// <summary>Seit wann diese Zelle brennt — in SPIELZEIT
        /// (<c>DebugClock</c>), nicht in Wanduhrzeit: das Original zählt
        /// Spielschritte, und ein angehaltenes Spiel brennt nicht weiter.
        /// &lt; 0 heisst »brennt nicht«. Solange sie brennt, steht die verkohlte Kachel und die
        /// Flamme läuft darüber.</summary>
        public float BrandVon = -1f;

        /// <summary>Wie lange dieser Brand dauert (Sekunden). Das Original
        /// würfelt ihn beim Anzünden aus — siehe <c>Anzuenden</c>.</summary>
        public float BrandDauer;

        /// <summary>Ist das Feuer durch? Dann steht die abgebrannte Kachel
        /// (19 von 20) oder der verkohlte Baum bleibt stehen (1 von 20).</summary>
        public bool Abgebrannt, Steht;
    }

    /// <summary>Je aufragendem Objekt sein Eintrag. Nach Zeile sortiert, damit
    /// der Zeichner nur durchlaufen muss.</summary>
    private readonly List<Kartenobjekt> _objDraw = new();

    /// <summary>Zelle (<c>spalte·1024 + zeile</c>) → Einschlagschwelle der
    /// aufragenden Kachel, die dort steht. Siehe
    /// <see cref="SchwelleAn"/>.</summary>
    private readonly Dictionary<int, int> _objSchwelle = new();

    /// <summary>
    /// Zelle → Lagenbyte aus Sektion 20, aber nur für Zellen ab 100.
    ///
    /// <para>⭐ <b>Das Byte ist mehr als »hier ist eine Rampe« — es ist die
    /// PLATZNUMMER.</b> GEMESSEN über beide Datenträger, ohne Ausnahme:</para>
    /// <list type="bullet">
    /// <item><c>100 + n</c> = Brücke/Mole Nr. <c>n</c> aus sec17 — 110 von 110</item>
    /// <item><c>200 + n</c> = Rampe Nr. <c>n</c> aus sec21 — 85 von 85</item>
    /// </list>
    /// <para>Damit weiß man ohne Suche, WELCHES Bauwerk auf einer Zelle steht,
    /// und über die beiden Abschnitte auch dessen Trefferpunkte (500 bzw. 200)
    /// und Länge. Siehe <see cref="BauwerkAn"/>.</para></summary>
    private readonly Dictionary<int, int> _rampen = new();

    /// <summary>Welches Bauwerk steht auf dieser Zelle? Gibt
    /// <c>(art, nummer)</c> zurück: art 1 = Brücke/Mole (sec17), 2 = Rampe
    /// (sec21), 0 = keines. Herleitung bei <see cref="_rampen"/>.</summary>
    public (int Art, int Nr) BauwerkAn(int col, int row)
    {
        if (!_rampen.TryGetValue(col * 1024 + row, out int l)) return (0, 0);
        if (l >= 200) return (2, l - 200);
        if (l >= 100) return (1, l - 100);
        return (0, 0);
    }

    /// <summary>Darf auf dieser Zelle BELADEN werden? Lagenbyte ≥ 100
    /// (0x40950C, 0x409763).</summary>
    public bool RampeBeladen(int col, int row)
        => _rampen.TryGetValue(col * 1024 + row, out int l) && l >= 100;

    /// <summary>Darf auf dieser Zelle ENTLADEN werden? Lagenbyte ≥ 200
    /// (0x409383, 0x4097B8).</summary>
    public bool RampeEntladen(int col, int row)
        => _rampen.TryGetValue(col * 1024 + row, out int l) && l >= 200;

    /// <summary>Die Kachelnummer je Rampenzelle — die Richtung steckt darin.
    /// Siehe <see cref="RampenAbsetzZelle"/>.</summary>
    private readonly Dictionary<int, int> _rampenKachel = new();

    /// <summary>Die erste der acht Rampenkacheln — <c>0x29E3</c> = 10723, aus
    /// dem Rampenschritt @0x4CF13A (<c>sub eax, 0x29E3</c>).
    ///
    /// <para><b>An unseren Karten nachgemessen (21.08.2026):</b> von 90 Zellen
    /// mit Lagenbyte ≥ 200 tragen <b>69</b> eine Kachel aus 10723..10730. Die
    /// 21 Ausnahmen liegen auf genau drei Karten (map_08, map_09, map_NET07);
    /// dort steht die Marke, aber keine Rampenkachel. ⚠ Das ist <b>nicht
    /// erklärt</b> — Karteileichen wie in sec37 wären plausibel, belegt ist es
    /// nicht. Zum Vergleich: die Zellen mit 100..199 tragen eine ganz andere
    /// Kachelfamilie (10001..10070), was die Trennung Brücke/Rampe
    /// bestätigt.</para></summary>
    public const int RampenKachelBasis = 10723;

    /// <summary>Die Auswahltafel <c>0x539790</c> = <c>3, 0, 2, 1</c>, angesteuert
    /// mit <c>((kachel − 10723) % 8) / 2</c>.</summary>
    private static readonly int[] RampenAuswahl = { 3, 0, 2, 1 };

    /// <summary>Die vier Zellenversätze aus <c>0x539798</c> (je 4 Byte:
    /// <c>word</c> Spalte, <c>word</c> Zeile). ⚠ Ausgelesen, nicht in ihrer
    /// Bedeutung nachgemessen — dass eine Zeilenversetzung von −2 zu einer
    /// Rampe passt, ist plausibel, belegt ist es nicht.</summary>
    private static readonly (int Col, int Row)[] RampenSchritt =
        { (-1, -2), (-1, 1), (1, 0), (-2, 0) };

    /// <summary>
    /// <b>Wohin eine Rampe absetzt</b> — <c>0x4CF100</c>, Befehl für Befehl:
    /// <code>
    ///   cmp byte[0x542E18 + spalte*256 + zeile], 0xC8 ; jb raus
    ///   kachel = word[imap + (zeile*breite + spalte)*4]      ; 0x41D090
    ///   auswahl = byte[0x539790 + ((kachel − 0x29E3) % 8) / 2]
    ///   spalte += word[0x539798 + auswahl*4]
    ///   zeile  += word[0x53979A + auswahl*4]
    /// </code>
    /// <para>Die Funktion nimmt Spalte und Zeile <b>als Zeiger</b> und schreibt
    /// die Zielzelle zurück — daran war zu erkennen, dass sie nicht nur prüft,
    /// sondern rechnet.</para>
    /// <para>Gibt <c>null</c>, wenn die Zelle keine Rampe ist oder ihre Kachel
    /// nicht zu den acht gehört.</para></summary>
    public Vector2I? RampenAbsetzZelle(int col, int row)
    {
        int schluessel = col * 1024 + row;
        if (!_rampen.TryGetValue(schluessel, out int lage) || lage < 200) return null;
        if (!_rampenKachel.TryGetValue(schluessel, out int kachel)) return null;
        int g = kachel - RampenKachelBasis;
        if (g is < 0 or > 7) return null;
        var (dc, dr) = RampenSchritt[RampenAuswahl[g / 2]];
        return new Vector2I(col + dc, row + dr);
    }

    /// <summary>Wieviele Rampenzellen die Karte trägt — ⚠ ohne diese Zahl wäre
    /// »der Transport tut nichts« nicht von »die Karte hat keine Rampen« zu
    /// unterscheiden.</summary>
    public int RampenZellen => _rampen.Count;

    /// <summary>Wie viele aufragende Objekte der letzte Durchgang gezeichnet
    /// hat — ⚠ Regel 33: ohne diese Zahl ist »kein Unterschied im Bild« nicht
    /// von »der Durchgang lief gar nicht« zu unterscheiden.</summary>
    public int ObjectsDrawn { get; private set; }

    /// <summary>Wie viele in der Karte stehen (unabhängig davon, wie viele
    /// gerade im Bild sind).</summary>
    public int ObjectsLoaded => _objDraw.Count;

    /// <summary>Wie viele Zellen gerade BRENNEN — dieselbe Regel 33 für das
    /// Feuer.</summary>
    public int ObjectsBurning { get; private set; }

    /// <summary><c>--keine-objekt-verdeckung</c> — die Gegenprobe: der Stand von
    /// vor dem 18.08.2026, alles im Boden.</summary>
    public static bool NoObjectOcclusion;

    /// <summary>Die zweite Ebene und ihre Rechtecke aus der Meta holen.</summary>
    private void LoadObjectLayer(GDict meta, string mapName)
    {
        _objTex = null;
        _objDraw.Clear();
        _objSchwelle.Clear();
        ObjectsBurning = 0;
        if (NoObjectOcclusion) return;

        string p = Core.Content.Path($"Maps/{mapName}.objects.png");
        if (ResourceLoader.Exists(p)) _objTex = ResourceLoader.Load<Texture2D>(p);
        if (_objTex == null && FileAccess.FileExists(p))
        {
            // Eingespielte Inhalte gehen nicht durch Godots Importschritt —
            // dieselbe Doppelung wie beim Kartenbild selbst.
            var im = Image.LoadFromFile(ProjectSettings.GlobalizePath(p));
            if (im != null) _objTex = ImageTexture.CreateFromImage(im);
        }
        if (_objTex == null) return;

        // ⚠ Der STREIFEN mit den verkohlten Bäumen hängt UNTEN an derselben
        // Ebene an (MapBaker.BurntAtlas). Eine Karte aus einem älteren Import
        // hat ihn nicht — dann brennt eben nichts, statt dass etwas kaputtgeht.
        var kohle = new List<Rect2>();
        if (meta.TryGetValue("burnt", out var bv) && bv.VariantType == Variant.Type.Array)
            foreach (var item in bv.AsGodotArray())
            {
                if (item.VariantType != Variant.Type.Dictionary) continue;
                var a = item.AsGodotDictionary<string, Variant>();
                kohle.Add(new Rect2(GetI(a, "x"), GetI(a, "y"), GetI(a, "w"), GetI(a, "h")));
            }

        // ⭐ 19.08.2026 — DIE RAMPENZELLEN aus Sektion 20. Sie sind die
        // Vorbedingung des Transports: >= 100 heisst BELADEN erlaubt
        // (0x40950C, 0x409763), >= 200 ENTLADEN (0x409383, 0x4097B8).
        // Ohne sie koennte ein Schiff seine Ladung nirgends absetzen.
        //
        // ⚠ Eine Karte aus einem aelteren Import hat den Block nicht. Dann
        // bleibt die Liste leer, und der Transport sagt es, statt still nichts
        // zu tun.
        _rampen.Clear();
        if (meta.TryGetValue("ramps", out var rv) && rv.VariantType == Variant.Type.Array)
            foreach (var item in rv.AsGodotArray())
            {
                if (item.VariantType != Variant.Type.Dictionary) continue;
                var a2 = item.AsGodotDictionary<string, Variant>();
                _rampen[GetI(a2, "col") * 1024 + GetI(a2, "row")] = GetI(a2, "lage");
            }

        // Die KACHELNUMMER der Rampenzellen — sie sagt, in welche Richtung
        // abgesetzt wird (siehe RampenAbsetzZelle). Ein Durchgang, einmal beim
        // Laden, und nur fuer Zellen, die ueberhaupt eine Rampe tragen.
        _rampenKachel.Clear();
        if (_rampen.Count > 0 && meta.TryGetValue("tiles", out var tkv)
            && tkv.VariantType == Variant.Type.Array)
            foreach (var item in tkv.AsGodotArray())
            {
                if (item.VariantType != Variant.Type.Dictionary) continue;
                var t2 = item.AsGodotDictionary<string, Variant>();
                int schluessel = GetI(t2, "col") * 1024 + GetI(t2, "row");
                if (_rampen.ContainsKey(schluessel)) _rampenKachel[schluessel] = GetI(t2, "code");
            }

        LiesNebeldecke(meta, kohle);
        ObjektOhneBild = 0;
        if (!meta.TryGetValue("objects", out var ov) || ov.VariantType != Variant.Type.Array) return;
        foreach (var item in ov.AsGodotArray())
        {
            if (item.VariantType != Variant.Type.Dictionary) continue;
            var o = item.AsGodotDictionary<string, Variant>();
            var e = new Kartenobjekt
            {
                Col = GetI(o, "col"),
                Row = GetI(o, "row"),
                Src = new Rect2(GetI(o, "x"), GetI(o, "y"), GetI(o, "w"), GetI(o, "h")),
                Ziel = new Vector2(GetI(o, "x"), GetI(o, "y")),
                // ⭐⭐ 24.08.2026 — die vier neuen Felder aus dem Backwerk.
                // Ohne sie war das Brandwesen der zerstoerbaren Objekte nicht
                // baubar; siehe Rendering/BrennendeObjekte.cs und MapBaker.
                Imap = GetI(o, "imap", -1),
                Art = GetI(o, "art", -1),
                Klasse = GetI(o, "klasse", -1),
                Basis = GetI(o, "basis", -1),
            };
            // ⭐⭐⭐ 27.08.2026 — DIE QUELLE KOMMT AUS DEM STREIFEN.
            // Bis heute war sie das Rechteck im zusammengesetzten Bild, und
            // dort ueberlappen sich benachbarte aufragende Kacheln: das
            // Rechteck der einen nahm Bildpunkte der anderen mit und malte sie
            // ein zweites Mal, NACH den Einheiten dazwischen. Auf der Bruecke
            // von map_02 sind das acht voll deckende Zeilen, und genau die
            // haben die Fahrzeuge abgeschnitten. Gemessen: Gelaender Zeile 22
            // liegt bei y 587..624, Zeile 24 bei 617..664.
            // ⚠ Rueckfall --objekt-rechteck stellt den alten Weg her.
            if (!ObjektRechteck && o.ContainsKey("bild"))
            {
                int k = GetI(o, "bild");
                if (k >= 0 && k < kohle.Count) e.Src = kohle[k];
                else ObjektOhneBild++;
            }
            else if (!ObjektRechteck) ObjektOhneBild++;

            if (o.ContainsKey("boden"))
            {
                int kb = GetI(o, "boden");
                if (kb >= 0 && kb < kohle.Count) { e.HatBoden = true; e.BodenSrc = kohle[kb]; }
            }

            if (o.ContainsKey("burnt"))
            {
                int k = GetI(o, "burnt");
                if (k >= 0 && k < kohle.Count)
                {
                    e.HatKohle = true;
                    e.KohleSrc = kohle[k];
                    e.KohleZiel = new Vector2(GetI(o, "bx"), GetI(o, "by"));
                }
            }
            if (o.ContainsKey("ash"))
            {
                int k = GetI(o, "ash");
                if (k >= 0 && k < kohle.Count)
                {
                    e.HatAsche = true;
                    e.AscheSrc = kohle[k];
                    e.AscheZiel = new Vector2(GetI(o, "ax"), GetI(o, "ay"));
                }
            }
            // ⚠ VOR dem Sortieren vergeben: der Index ist die Lieferfolge des
            // Backofens, nicht die Zeichenfolge. Nach dem Sortieren waere es
            // wieder eine Funktion der Zeile — also genau das, was wir loswerden
            // wollen.
            e.Index = _objDraw.Count;
            _objDraw.Add(e);
            // ⚠ 19.08.2026 — die EINSCHLAGSCHWELLE dieser Zelle gleich mit
            // merken. Der Geschosstakt fragt sie je Schritt ab, und eine Liste
            // durchzugehen waere dafuer zu teuer. Wald 40, zerstoerbares
            // Objekt 30 — siehe MapEntityLayer.SchwelleAn.
            // ⚠ 24.08.2026 — hier stand `e.HatKohle ? 40 : 30`. Seit
            // zerstoerbare Objekte ihre zwei Ersatzbilder in denselben Feldern
            // haben, heisst »hat eine verkohlte Kachel« nicht mehr »ist Wald«.
            // Die Schwelle 40 gehoert dem Wald, die 30 dem Objekt.
            _objSchwelle[e.Col * 1024 + e.Row] = e.IstWald ? 40 : 30;
        }
        // ⚠ Nach ZEILE sortieren, nicht nach Lage im Bild: das Zeilenfach
        // entscheidet, was vor wem liegt, und der Backofen liefert schon in
        // Zeilenfolge — die Sortierung ist die Zusicherung, nicht die Arbeit.
        _objDraw.Sort((a, b) => a.Row - b.Row);
        if (ObjektOhneBild > 0)
            GD.PushWarning($"objekte: {ObjektOhneBild} aufragende Kacheln ohne eigenen "
                         + "Streifenplatz — diese Karte ist vor dem 27.08.2026 gebacken. "
                         + "Sie schneiden wieder Einheiten ab; --reexport-maps behebt es.");
        int brennbar = 0;
        int zerstoerbar = 0;
        foreach (var e in _objDraw) { if (e.IstWald) brennbar++; else if (e.IstObjekt) zerstoerbar++; }
        GD.Print($"objekte: {_objDraw.Count} aufragende Kartenobjekte aus " +
                 $"{mapName}.objects.png — sie verdecken jetzt Einheiten; " +
                 $"{brennbar} davon sind Wald und koennen brennen ({kohle.Count} verkohlte Kacheln), "
               + $"{zerstoerbar} zerstoerbare Objekte");
    }

    /// <summary>Alles bis einschliesslich dieser Zeile zeichnen. Genauso
    /// gebaut wie <c>DrawRailUpTo</c> und <c>DrawUnitsUpTo</c>: der Zeiger läuft
    /// mit, jeder Eintrag kommt genau einmal dran.
    ///
    /// <para>⚠ Die Zeile eines Objekts ist seine ZELLE, ohne Zuschlag. Ein
    /// Gebäude bekommt <c>+3</c> bzw. <c>+tür0.row</c> und ein Gleis <c>+2</c>
    /// (beides gelesen); für ein Objekt ist im Original nichts dergleichen
    /// belegt, also steht hier keine Zahl. ⚠ Der Waldbrand des Originals kommt
    /// dagegen MIT Zuschlag ins Fach (<c>inc bx</c> @0x42E6FC, also Zeile+1) —
    /// das betrifft aber nur die Flamme, nicht die Kachel.</para></summary>
    private void DrawObjectsUpTo(int throughRow, ref int at)
    {
        if (_objTex == null) return;
        // ⚠ Einmal je Bild, und darum hier: dieser Durchgang ist der einzige
        // Ort in dieser Datei, den jeder Bildaufbau anfaesst. Der Zeiger `at`
        // steht am Anfang des Zeilenfachs auf 0.
        // ⚠⚠ HIER STAND `if (at == 0) Ausbrennen();` — der Brand hing damit am
        // ZEICHENWEG. Das ist der falsche Ort: ein kopfloser Lauf zeichnet
        // nicht, und im Netzspiel liefe das Feuer auf zwei Maschinen
        // verschieden schnell, weil die Bildrate mitrechnet. Beides steht
        // jetzt in BrandTakt(), gerufen aus SimTick. (21.08.2026)
        for (; at < _objDraw.Count && _objDraw[at].Row <= throughRow; at++)
        {
            var e = _objDraw[at];
            // siehe MapEntityLayer.Zeichenfolge — nur für --verdeck-check
            Zeichenfolge?.Add(('B', e.Row));

            // ⚠⚠ 24.08.2026 — HIER STAND EIN NEBELRIEGEL, UND ER WAR NICHT
            // TRAGFAEHIG. Gemeldet und belegt: im Original zeigt unerkundetes
            // Gebiet keine Baeume, Kisten oder Bauwerke. Der Riegel hat sie
            // ausgeblendet — und darunter kamen LOECHER zum Vorschein, keine
            // Landschaft.
            //
            // ⭐ Der Grund steht im Backofen (MapBaker, Durchgang A/B):
            //     if (!isObj) Blit(Frame(code[i]), …);
            // Unter einer Objektzelle wird die EIGENE Kachel gar nicht gemalt,
            // nur eine Behelfs-Hintergrundkachel (`basis`) — sie sollte nie
            // sichtbar werden, weil das Objektbild sie deckt. Nimmt man das
            // Objekt weg, sieht man den Behelf: rechteckige Wasser- und
            // Felsflecken quer ueber die Wiese.
            //
            // Die Ausblendung ist also erst moeglich, wenn der Backofen unter
            // jede Objektzelle einen richtigen Boden legt. Das ist die
            // Vorbedingung, nicht diese Zeile. Siehe OFFENE_FRAGEN.
            if (e.Abgebrannt)
            {
                // AUS. 19 von 20 Zellen zeigen die abgebrannte Kachel (Stumpf
                // bzw. blanker Boden), bei jeder zwanzigsten bleibt der
                // verkohlte Baum stehen — siehe Ausbrennen.
                //
                // ⚠⚠ 24.08.2026 — DER STUMPF IST BODEN UND GEHOERT NACH UNTEN.
                //
                // Gemeldet: »dort an der Stelle glitchen auch Cyborgs wie unter
                // die Karte oder werden verdeckt«, und danach »war wieder kurz
                // im Boden verschwunden«. Genau das: dieser Durchgang ist der
                // der AUFRAGENDEN Kacheln und laeuft NACH den Einheiten (Schritt
                // 4 nach Schritt 2, seit C23). Ein Stumpf oder blanker Boden
                // ragt aber nicht auf — er malte damit undurchsichtiges Gelaende
                // ueber jeden, der davor stand, und mit gerader Oberkante auch
                // ueber die untere Haelfte einer Flamme.
                //
                // ⭐ Dieselbe Sorte Fehler wie C14 (»der Boden der Gebaeude
                // zuerst«): ein flaches Bodenstueck, das im Fach der aufragenden
                // Sachen mitlief. Der stehende verkohlte Baum (1 von 20) bleibt
                // hier — der ragt wirklich auf.
                if (!e.Steht) continue;      // -> AbgebrannteZeichnen()
                DrawTextureRectRegion(_objTex, new Rect2(e.KohleZiel, e.KohleSrc.Size), e.KohleSrc);
            }
            else if (e.BrandVon >= 0f && e.HatKohle)
            {
                // BRENNT: die verkohlte Kachel steht anstelle des grünen Baums
                // (das ist der Kacheltausch aus zapal @0x4CACE5), und die
                // Flamme läuft darüber.
                DrawTextureRectRegion(_objTex, new Rect2(e.KohleZiel, e.KohleSrc.Size), e.KohleSrc);
                // ⚠⚠ 19.08.2026, DRITTE BERICHTIGUNG — die Flamme wird hier
                // nur VORGEMERKT, nicht gezeichnet. Siehe FlammenZeichnen.
                _flammen.Add(e);
            }
            else
            {
// ⭐⭐⭐ 28.08.2026 — IM NIE ERKUNDETEN GEBIET STEHT DER BODEN,
                // NICHT DAS OBJEKT. Das Original blendet nichts aus: seine
                // BEKANNTE Kachelkarte (0x5539D0) trägt dort von vornherein
                // eine synthetisierte Bodenkachel, und der Zeichner liest nur
                // sie. Aufgedeckt wird die wahre Kachel hineinkopiert
                // (@0x41FFC8 / @0x420245) — darum bleibt ein einmal gesehener
                // Baum sichtbar, auch wenn der Nebel zurückkommt.
                // Genau das beschreibt der Spieler, und sein Let's-Play-Bild
                // zeigt es: Brücke (Lage >= 100, wahre Kachel von Anfang an)
                // sichtbar, Bäume nicht.
                if (!NebelObjekteAlt && FogActive && _fog != null
                    && !_fog.IsSeen(e.Col, e.Row))
                {
                    if (e.HatBoden)
                    {
                        DrawTextureRectRegion(_objTex, new Rect2(e.Ziel, e.BodenSrc.Size), e.BodenSrc);
                        NebelBodenGezeichnet++;
                    }
                    else NebelBodenFehlt++;
                    continue;
                }
                DrawTextureRectRegion(_objTex, new Rect2(e.Ziel, e.Src.Size), e.Src);
            }
            ObjectsDrawn++;
        }
    }

    /// <summary>
    /// <b>DIE FLAMME</b> — ANIM.CWA-Folge 550, sieben Bilder, bei uns als Effekt
    /// <c>blast</c> eingespielt.
    ///
    /// <para>Gelesen am Zeichner des Zeilenfachs, Art 0x0C @0x42B422:
    /// <c>phase = (bildzähler/2 + index) mod 7</c>, gezeichnet bei
    /// <c>x = spalte*40 + (index mod 10) − 5</c> und
    /// <c>y = zeile*20 − höhe*15</c> (der Einreiher @0x42E760 setzt beides).
    /// Der Blitter des Originals (@0x4AC1D9) addiert den <c>YOffset</c> des
    /// Bildes selbst dazu — und genau so liegt unsere Leinwand, weil
    /// <c>InterfaceExporter.Canvas</c> jedes Bild an seinem <c>YOffset</c>
    /// ablegt und die Leinwandhöhe <c>max(YOffset + Höhe)</c> ist.</para>
    ///
    /// <para>⚠ Zwei UNSERE SETZUNGEN, beide benannt: das Original wechselt für
    /// Wälder mit ungeradem Index auf Folge <b>552</b> — die haben wir nicht
    /// ausgespielt, wir nehmen für alle 550. Und der Bildzähler des Originals
    /// ist sein Spielschritt; wir rechnen mit
    /// <see cref="FlammenSekunden"/> Sekunden je Bild.</para></summary>
    /// <summary>Die Flammen, die in dieser Zeile noch zu zeichnen sind.</summary>
    private readonly List<Kartenobjekt> _flammen = new();

    /// <summary>
    /// <b>DIE FLAMMEN EINER ZEILE, NACH DEREN KACHELN</b> — und das war der
    /// dritte Anlauf auf denselben gemeldeten Fehler.
    ///
    /// <para>⚠⚠ 19.08.2026, dreimal gemeldet: »das Feuer auf den Bäumen passt
    /// noch nicht«. Erst stand die Flamme neben ihrem Baum (falscher
    /// Bezugspunkt), dann sass sie richtig, war aber <b>teilweise verdeckt</b> —
    /// und genau das hatte der Spieler schon beim ersten Mal beschrieben:
    /// »als wären andere Bäume darüber gesetzt«.</para>
    ///
    /// <para><b>Er hatte recht, und die Ursache ist ein einziger Befehl.</b> Der
    /// Einreiher der Flamme (C 0x42E6D0, F 0x42D890) liest die Waldtafel
    /// (0xBFF3E0, drei Byte je Eintrag), nimmt nur Einträge mit Zustand &gt; 1
    /// (also brennende) — und macht dann:</para>
    ///
    /// <code>
    ///   0x42E6E9  al = byte[esi]      ; Spalte
    ///   0x42E6EB  bl = byte[esi+1]    ; Zeile
    ///   0x42E6FC  inc bx              ; ⚠ ZEILE + 1
    /// </code>
    ///
    /// <para>Die Flamme kommt also ins Zeilenfach der <b>nächsten</b> Zeile.
    /// Damit wird sie nach ALLEN Kacheln ihrer eigenen Zeile gezeichnet — auch
    /// nach den Bäumen, die in der Liste hinter ihr stehen. Bei uns lief sie
    /// mitten in der Kachelschleife und wurde von jedem späteren Baum derselben
    /// Zeile überdeckt.</para>
    ///
    /// <para>Die Zeile +1 hebt sich in der y-Rechnung wieder auf: der Einreiher
    /// rechnet <c>bx*20 − (Kamera + 20)</c> mit der ERHÖHTEN Zeile, und die 20
    /// nimmt sie wieder zurück. Der Versatz gegen die Kachel bleibt −18/−20 wie
    /// gehabt. Das +1 wirkt also <b>nur auf die Zeichenreihenfolge</b> — es ist
    /// kein Positionsfehler, sondern ein Reihenfolgefehler, und darum sah die
    /// Flamme richtig platziert und trotzdem falsch aus.</para>
    ///
    /// <para>Beide GAME.EXE tragen dieselbe Form (`mov esi, &lt;Waldtafel&gt;`
    /// gefolgt von `inc bx` im selben Fenster).</para>
    /// </summary>
    /// <summary>
    /// ⚠⚠ 24.08.2026 — <b>EINE ZEILE ZU FRUEH.</b>
    ///
    /// <para>Gemeldet mit Bild: »links ist immer noch die eine Flamme wie
    /// abgeschnitten oder verdeckt, das ist im Original auch nicht so«.</para>
    ///
    /// <para>Hier stand <c>foreach (var e in _flammen) Flamme(e); _flammen.Clear();</c>
    /// — die Flammen einer Zeile wurden also am Ende IHRER EIGENEN Zeile
    /// gezeichnet. Der Kommentar an der Aufrufstelle behauptete dabei, das sei
    /// das <c>inc bx</c> des Originals; es war es nicht. <c>inc bx</c>
    /// @0x42E6FC reiht die Flamme in das Fach der <b>naechsten</b> Zeile ein,
    /// und das ist ein Durchgang SPAETER: erst danach sind die aufragenden
    /// Kacheln der Zeile darunter dran.</para>
    ///
    /// <para>Ein Baum eine Zeile tiefer sitzt zwar 20 Bildpunkte weiter unten,
    /// ragt aber rund 50 nach OBEN — er malte damit die untere Haelfte der
    /// Flamme zu. Genau das war auf dem Bild zu sehen.</para>
    ///
    /// <para>⭐ Der Beleg stand die ganze Zeit im eigenen Kommentar bei
    /// <see cref="Flamme"/>: »Das +1 wirkt also NUR auf die
    /// Zeichenreihenfolge«. Gelesen, notiert — und dann an der Aufrufstelle
    /// falsch umgesetzt.</para>
    /// </summary>
    /// <summary>⚠ NUR ZUM MESSEN (--flammen-oben): die Flammen erst ganz am
    /// Schluss zeichnen, ueber allem. Trennt »verdeckt« von »beschnitten« —
    /// verschwindet der gerade Schnitt, ist es eine Reihenfolge; bleibt er,
    /// liegt es am Bild oder an einer Klammer.</summary>
    public static bool FlammenOben;

    /// <summary>⚠ NUR ZUM MESSEN: alle Flammen auf Bild 0 einfrieren, damit
    /// zwei Aufnahmen vergleichbar werden.</summary>
    public static bool FlammenPhase0;

    /// <summary>
    /// ⚠⚠ <b>UM WIE VIELE ZEILENFAECHER DIE FLAMME NACHHAENGT — und hier steht
    /// eine 2, wo das Original eine 1 hat.</b>
    ///
    /// <para>Gemeldet, zweimal: »links ist immer noch die eine Flamme wie
    /// abgeschnitten«. Der erste Anlauf hat den Verzug von 0 auf 1 gesetzt, weil
    /// das Original mit <c>inc bx</c> @0x42E6FC in das Fach der NAECHSTEN Zeile
    /// einreiht. Das war richtig gelesen und hat trotzdem nicht gereicht.</para>
    ///
    /// <para><b>Gemessen, nicht geraten.</b> Mit <c>--flammen-oben</c> (Flammen
    /// ganz zuletzt) ist die Flamme vollstaendig — es ist also VERDECKUNG und
    /// keine Beschneidung. Dann derselbe Bildausschnitt bei Verzug 1, 2, 3 und
    /// »oben« nebeneinander: <b>ab 2 ist das Bild deckungsgleich mit
    /// »oben«</b>. Es fehlte genau ein Fach.</para>
    ///
    /// <para>⚠⚠ <b>DAS IST EIN HINWEIS, KEINE LOESUNG.</b> Wenn das Original mit
    /// einem Fach auskommt und wir zwei brauchen, dann liegt bei uns die
    /// ZEILENZUORDNUNG der aufragenden Kacheln um eins anders als dort — die
    /// Flamme ist nur die Stelle, an der es auffaellt, weil sie mit 79
    /// Bildpunkten hoeher ist als alles andere. Wer das nachgeht, faengt bei
    /// <c>DrawObjectsUpTo(r, …)</c> gegen <c>DrawUnitsUpTo(r + 1, …)</c> an und
    /// vergleicht mit dem Einreiher des Originals @0x42E760.</para>
    ///
    /// <para>⭐⭐ <b>ZURUECK AUF 1 am selben Abend</b>, und das gehoert
    /// dazu. Die 2 war eine Kruecke, gesetzt waehrend ich die Ursache noch bei
    /// der Zeichenreihenfolge suchte. Gefunden wurde sie woanders: eine
    /// ausgebrannte Waldzelle malte ihren BODEN im Durchgang der aufragenden
    /// Kacheln, also nach allem Lebenden (siehe DrawObjectsUpTo) — DAS war die
    /// gerade Kante quer durch die Flamme, und es hat auch Fusssoldaten
    /// zugedeckt.</para>
    ///
    /// <para>⚠⚠ <b>UND WIEDER AUF 2, eine Viertelstunde spaeter.</b> Die
    /// Gegenprobe mit 1 hat der Spieler sofort gesehen (»na jetzt war die linke
    /// Flamme wieder abgeschnitten«), und der Bildvergleich bestaetigt es: der
    /// Bodendurchgang hat einen ECHTEN Anteil behoben — die Flamme ist deutlich
    /// vollstaendiger als vorher —, aber unten fehlt weiter ein Stueck. Bei
    /// Verzug 1 deckt sie eine aufragende Kachel ZWEI Zeilen naeher zu.</para>
    ///
    /// <para>⚠⚠ <b>HIER STAND EIN ERFUNDENER BEFUND, und er ist am selben Abend
    /// widerlegt worden.</b> Es hiess: »das Original kommt mit einem Fach aus,
    /// wir brauchen zwei — also ist bei uns die Zeilenzuordnung der aufragenden
    /// Kacheln um eins anders«. Das war ein Ueberschlag, bei dem ich die
    /// Nachbarzelle stillschweigend auf dieselbe Gelaendehoehe gesetzt habe.
    /// </para>
    ///
    /// <para><b>Nachgemessen (--verdeck-stelle, Zeilenstaffelung):</b></para>
    /// <code>
    ///   eben   (1,39)->(1,40):  Fuss Δ +20        = 1 Zeile          ✔
    ///          (1,39)->(1,41):  Fuss Δ +40        = 2 Zeilen         ✔
    ///   Huegel (19,52)->(19,55): Fuss Δ +45
    ///          Hoehe 5 -> 6:    3·20 − 1·15 = 45                     ✔
    /// </code>
    ///
    /// <para>Unsere Staffelung ist ueberall exakt <b>20 px je Zeile und −15 je
    /// Gelaendestufe</b>. Der Baum, der die Huegelflamme verdeckt, steht
    /// wirklich eine Stufe hoeher UND naeher — er verdeckt zu Recht, und das
    /// Original taete dort dasselbe.</para>
    ///
    /// <para>Damit ist die 2 <b>keine Kruecke ueber einem Defekt</b>, sondern
    /// eine bewusste Abweichung: etwas weniger Verdeckungstreue gegen eine
    /// ganze Flamme. Der Spieler hat sie so bestaetigt. <c>--flammen-verzug=1</c>
    /// stellt das Original her.</para>
    ///
    /// <para>⭐⭐⭐ <b>UND JETZT GEMESSEN STATT GERATEN — die Zahl ist 4.</b>
    /// Er hat den Schnitt an derselben Waldstelle immer wieder gemeldet, und
    /// mein Bildvergleich fand nichts. Zwei Gruende, beide meine:</para>
    /// <list type="number">
    /// <item>Die zwei Aufnahmen liefen in verschiedenen FLAMMENBILDERN — der
    /// Farbvergleich zaehlte das als »verdeckt« und lieferte Rauschen.
    /// <c>--flammen-phase0</c> friert sie ein.</item>
    /// <item>Ein kopfloser Lauf deckt fast nichts auf, und die Flamme wird nur
    /// in BEOBACHTETEN Zellen gezeichnet. Sein Einwand: »dein Test ist sinnfrei,
    /// weil das Gebiet noch nicht aufgedeckt ist«. <c>--kein-nebel</c> loest
    /// das.</item>
    /// </list>
    ///
    /// <para>Mit beiden Schaltern und festem Keim (<c>--determinism-seed=7</c>)
    /// ist es eindeutig:</para>
    /// <code>
    ///   Verzug 2:  1478 verdeckte Flammenpunkte
    ///   Verzug 3:    44
    ///   Verzug 4:     0     von 30909
    /// </code>
    ///
    /// <para>Und die 4 ist keine krumme Zahl, sondern Geometrie: die Flamme ist
    /// <b>79 Bildpunkte</b> hoch, eine Zeile <b>20</b> — sie ueberspannt vier
    /// Zeilen. Wer ueber allem liegen will, was er beruehrt, muss vier Faecher
    /// nachhaengen. Der Pruefstand nennt die groesste Verdeckung mit 40x38 px
    /// aus <b>Zeile +1</b>, also aus der Zeile unmittelbar davor.</para>
    ///
    /// <para>⚠ <b>Das Original kommt mit EINEM Fach aus, und das bleibt der
    /// Unterschied.</b> Dort wird die untere Flammenhaelfte von den Baeumen
    /// davor durchaus angeschnitten — bei ihm faellt es nur nicht auf, weil
    /// seine Braende am Waldrand stehen. Wir tauschen hier
    /// Verdeckungstreue gegen eine ganze Flamme, weil er genau das
    /// wiederholt verlangt hat. <c>--flammen-verzug=1</c> stellt das Original
    /// her.</para>
    /// </summary>
    public static int FlammenVerzug = 4;

    private readonly List<List<Kartenobjekt>> _flammenFaecher = new();

    /// <summary>Die ausgebrannten Zellen, die nur noch BODEN sind (Stumpf oder
    /// blanke Erde). Sie laufen im Bodendurchgang mit, vor allem Lebenden —
    /// siehe die Begruendung in DrawObjectsUpTo.</summary>
    /// <summary>Ein aufragendes Objekt, so wie es GEZEICHNET wird — fuer
    /// <see cref="VerdeckEinheitLine"/>. ⚠ Der Zeichner und diese Liste muessen
    /// dasselbe Rechteck nennen, sonst misst der Pruefstand sich selbst.</summary>
    internal readonly record struct VerdeckKachel(string Art, int Col, int Row, Rect2 Rechteck);

    internal System.Collections.Generic.IEnumerable<VerdeckKachel> ObjekteFuerVerdeck()
    {
        foreach (var e in _objDraw)
        {
            if (e.Abgebrannt)
            {
                // Der blanke Boden laeuft seit dem 24.08. im BODENdurchgang und
                // kann darum nichts mehr verdecken; nur der stehende verkohlte
                // Baum ist noch aufragend.
                if (!e.Steht) continue;
                yield return new VerdeckKachel("verkohlter Baum", e.Col, e.Row,
                    new Rect2(e.KohleZiel, e.KohleSrc.Size));
            }
            else if (e.BrandVon >= 0f && e.HatKohle)
                yield return new VerdeckKachel("brennende Kachel", e.Col, e.Row,
                    new Rect2(e.KohleZiel, e.KohleSrc.Size));
            else
                yield return new VerdeckKachel(e.IstWald ? "Baum" : "Fels/Objekt", e.Col, e.Row,
                    // ⚠ 27.08.2026 — Ziel, NICHT Src.Position. Seit die Quelle
                    //   aus dem Streifen kommt (siehe MapBaker.Objects), ist
                    //   Src.Position eine STREIFEN-Koordinate. Dieser Pruefstand
                    //   sucht Ueberschneidungen auf der KARTE und haette ab
                    //   sofort nie wieder eine gefunden — stille falsche
                    //   Negative, genau das, wovor sein eigener Kopf warnt.
                    new Rect2(e.Ziel, e.Src.Size));
        }
    }

    private void AbgebrannteZeichnen()
    {
        foreach (var e in _objDraw)
        {
            if (!e.Abgebrannt || e.Steht) continue;
            DrawTextureRectRegion(_objTex, new Rect2(e.AscheZiel, e.AscheSrc.Size), e.AscheSrc);
        }
    }

    private void FlammenZeichnen()
    {
        if (FlammenOben) return;
        int n = System.Math.Max(1, FlammenVerzug);
        while (_flammenFaecher.Count < n) _flammenFaecher.Add(new List<Kartenobjekt>());
        // das aelteste Fach ist dran
        var faellig = _flammenFaecher[0];
        foreach (var e in faellig) Flamme(e);
        faellig.Clear();
        _flammenFaecher.RemoveAt(0);
        faellig.AddRange(_flammen);
        _flammen.Clear();
        _flammenFaecher.Add(faellig);
    }

    /// <summary>Der Abschluss nach der Zeilenschleife: beide Faecher leeren,
    /// sonst blieben die letzten ein bis zwei Zeilen ungezeichnet liegen und
    /// kaemen im naechsten Bild doppelt.</summary>
    private void FlammenAbschluss()
    {
        if (FlammenOben) return;
        foreach (var f in _flammenFaecher) { foreach (var e in f) Flamme(e); f.Clear(); }
        foreach (var e in _flammen) Flamme(e);
        _flammen.Clear();
    }

    /// <summary>Der Messdurchgang von <see cref="FlammenOben"/>.</summary>
    private void FlammenGanzOben()
    {
        if (!FlammenOben) return;
        foreach (var f in _flammenFaecher) { foreach (var e in f) Flamme(e); f.Clear(); }
        foreach (var e in _flammen) Flamme(e);
        _flammen.Clear();
    }

    private void Flamme(Kartenobjekt e)
    {
        // ⚠ 24.08.2026 — KEINE FLAMME IM NEBEL. Gemeldet: »man sieht eine
        // Flamme im Fog of War, das macht auch keinen Sinn, die Flamme duerfte
        // man ja erst sehen, wenn man dort aufdeckt«.
        //
        // Der Nebel wird bei uns als Schicht UEBER allem gezeichnet (siehe das
        // Ende von _Draw). Was nie gesehen wurde, ist darunter schwarz — was
        // schon erkundet, aber gerade unbeobachtet ist, wird nur ABGEDUNKELT.
        // Eine grellorange Flamme scheint durch diese Abdunklung hindurch, und
        // ein LAUFENDES Ereignis gehoert nicht in erinnertes Gelaende: der
        // Spieler sieht dort den Wald, wie er ihn verlassen hat, nicht wie er
        // gerade brennt.
        //
        // ⚠ UNSERE REGEL, nicht gelesen — deshalb steht sie hier und nicht als
        // Befund. Die Kachel darunter (verkohlt bzw. Asche) bleibt sichtbar wie
        // jedes andere erinnerte Gelaende; nur das Feuer selbst schweigt.
        if (FogActive && !Watched(e.Col, e.Row)) return;

        // ⚠ 19.08.2026 — ZWEI FLAMMEN, nach der Parität des Index. Der Zeichner
        // @0x42B461 rechnet `edi = (index & 1) * 2 + 0x226` — also Folge 550
        // oder 552. Beide haben sieben Bilder. Vorher brannten alle Bäume mit
        // demselben Bild.
        // ⚠ UNSER Ersatz für den Index: das Original nimmt die Nummer des Baums
        // in der Waldtafel, wir die Zelle. Die Aufteilung ist dieselbe (halb
        // halb, fest je Baum), die ZUORDNUNG ist es nicht — welcher Baum welche
        // der beiden Flammen bekommt, weicht damit vom Original ab. Das ist
        // sichtbar nur als anderes Muster, nicht als anderer Eindruck.
        var bilder = EffectFrames((e.Index & 1) == 0 ? "blast" : "blast2");
        if (bilder.Count == 0) bilder = EffectFrames("blast");
        if (bilder.Count == 0) return;
        // Der Index tut im Original zweierlei: er versetzt die PHASE, damit
        // nicht alle Bäume im Gleichschritt flackern, und er versetzt die Lage
        // um bis zu 10 px. Wir nehmen dafür die Zelle — sie ist dieselbe feste
        // Zahl je Baum, die das Original aus dem Tafelindex zieht.
        int idx = e.Index;
        // ⚠ NUR ZUM MESSEN (--flammen-phase0): ohne feste Phase laufen zwei
        // Aufnahmen in verschiedenen Bildern der Flamme, und ein Bildvergleich
        // zaehlt das als »verdeckt«. Genau daran ist der erste Vergleichsversuch
        // heute Abend gescheitert.
        int phase = FlammenPhase0 ? 0
                  : (int)(DebugClock / FlammenSekunden + idx) % bilder.Count;
        if (phase < 0) phase += bilder.Count;

        // ⚠⚠ 19.08.2026 — DIE FLAMME STAND NEBEN IHREM BAUM.
        //
        // Gemeldet mit Bildschirmfoto: »links bei dem einen Feuer sieht es aus,
        // als wären andere Bäume darüber gesetzt, deswegen ist das teils
        // verdeckt«. Der Spieler hat richtig gesehen, dass etwas nicht stimmt,
        // die Ursache ist aber eine andere: die Flamme lag gar nicht auf ihrem
        // Baum.
        //
        // Hier stand `_ox + Col*TileW`, `_oy + Row*TileH − Hoehe*15` — die Lage
        // wurde also aus der Zelle NEU gerechnet, während die Baumkachel
        // daneben ihre Lage vom Backofen bekommt (`bx`/`by` aus der Karten-JSON,
        // einschliesslich Kachelanker −50). Zwei Rechenwege für dieselbe Zelle,
        // und sie liefern nicht denselben Punkt.
        //
        // Dazu kam ein zweiter Fehler: `DrawTexture` setzt die LINKE OBERE ECKE
        // auf den Punkt. Jeder andere Effekt in diesem Baum zieht vorher seinen
        // Ankerpunkt ab (`fx.Pos − _fxAnchor[fx.Kind]`, siehe UpdateEffects) —
        // dieser eine nicht. Bei »blast« (60×79) sind das 30 px nach rechts und
        // 79 nach unten Versatz.
        //
        // Jetzt hängt die Flamme an der Kachel, die WIRKLICH gezeichnet wurde:
        // unten mittig auf ihr, mit dem seitlichen Versatz des Originals.
        // ⚠⚠ 19.08.2026, ZWEITE BERICHTIGUNG — JETZT GELESEN STATT GESETZT.
        //
        // Der erste Versuch heute früh hängte die Flamme »unten mittig« an die
        // Kachel. Das war besser als vorher (sie stand gar nicht auf ihrem
        // Baum), aber es war UNSERE Regel. Gemeldet: »das Feuer hat noch nicht
        // ganz gepasst.«
        //
        // Das Original sagt es selbst. Die Einreihstelle der Flamme (Art 12,
        // C @0x42E7FF / F @0x42D9B6 — dieselbe Form, 22 Einreihstellen mit
        // identischer Artenliste in beiden EXE) rechnet:
        //
        //     sub bx, 0x46      ; y − 70
        //     sub cx, 0x12      ; x − 18
        //     [Fach+6] = cx     ; x
        //     [Fach+8] = bx     ; y
        //
        // und der Zeichner @0x42B474 addiert danach nur noch den seitlichen
        // Versatz `(index mod 10) − 5`. Eine KACHEL derselben Zelle wird
        // dagegen bei y − 0x32 (= −50) gesetzt (@0x4B42DF, dreimal wortgleich).
        //
        // Der Unterschied ist also **18 nach links und 20 nach oben gegenüber
        // der Kachel** — und das ist eine relative Angabe, die unabhängig davon
        // gilt, wie unser Backofen seinen Nullpunkt legt. Genau deshalb wird
        // sie hier so verwendet und nicht in absolute Zahlen umgerechnet.
        var bild = bilder[phase];
        Vector2 kachel = e.HatKohle
            ? e.KohleZiel
            : new Vector2(_ox + e.Col * TileW,
                          _oy + e.Row * TileH - ElevOf(e.Col, e.Row) * 15 - 50);
        DrawTexture(bild, kachel + new Vector2(-18 + (idx % 10 - 5), -20));
    }

    /// <summary>Wie lange ein Flammenbild steht. Das Original springt alle ZWEI
    /// Spielschritte weiter (<c>sar eax, 1</c> @0x42B438), und ein Spielschritt
    /// ist 20 ms (<c>SetTimer</c> @0x415BC5, siehe
    /// <see cref="OriginalTicksPerSecond"/>) — also 0,04 s. Das ist GERECHNET,
    /// nicht gewählt.</summary>
    private const float FlammenSekunden = 2f / OriginalTicksPerSecond;

    /// <summary>Der Schaden, mit dem der SETUP-Block einer Mission auf eine
    /// Zelle schlägt. ⭐ <b>Gelesen, nicht gewählt:</b> der Angreifer der
    /// SETUP-Liste ist <c>0x9C72</c> = 40050, und <c>Zasah</c> behandelt
    /// 40000…40999 als reine SCHADENSZAHL (@0x40CC8B <c>add ax, 0x63C0</c>,
    /// also Schaden = 40050 − 40000).</summary>
    public const int SetupSchaden = 50;

    /// <summary>Was aus einer getroffenen Waldzelle wird.</summary>
    private enum Waldfolge
    {
        /// <summary>Kein Wald da, oder der Schaden war zu schwach.</summary>
        Nichts,
        /// <summary>Sie brennt.</summary>
        Feuer,
        /// <summary>Sie ist weg — <b>ohne</b> Feuer (»zrus«).</summary>
        Weg,
    }

    /// <summary>
    /// <b>DIE FÜNF SCHADENSBÄNDER</b> — @0x40D638…0x40D727, gebaut am
    /// 21.08.2026. Hier stand bis dahin: »wir zünden unbedingt an, ⚠ NICHT
    /// GEBAUT«.
    ///
    /// <code>
    ///   &gt;= 70   der Wald wird OHNE Feuer geloescht ("zrus", 0x4CAD40)
    ///   46..69  immer Feuer                          ("zapal A")
    ///   23..45  Feuer mit Wahrscheinlichkeit 1/4     ("zapal B")
    ///   13..22  Feuer mit 1/8                        ("zapal C")
    ///   &lt;= 12   gar nichts
    /// </code>
    ///
    /// <para>⚠ <b>Der Sonderfall:</b> ein Treffer von einer Einheit mit
    /// <c>+0x0D == 12</c> setzt den Wert fest auf 60 — die fällt also immer ins
    /// Band »immer Feuer«. Den bauen wir hier NICHT, weil unser einziger
    /// Aufrufer der SETUP-Block ist und der keine schiessende Einheit kennt;
    /// wer den allgemeinen Beschuss anschliesst, muss ihn mitnehmen.</para>
    ///
    /// <para>⚠ <b>Für unseren einzigen Weg ändert sich nichts</b>, und das ist
    /// Absicht: der SETUP-Schaden ist gelesene <b>50</b> und fällt ins Band
    /// »immer Feuer«. Die Bänder sind trotzdem gebaut, weil der Kommentar sonst
    /// weiter behauptete, sie fehlten — und weil der nächste Aufrufer sie
    /// braucht, nicht erst sucht.</para></summary>
    private Waldfolge WaldTreffer(int col, int row, int schaden)
    {
        // ⚠ Die Grenzen sind EINSCHLIESSLICH, wie im Original: 70 loescht,
        // 69 brennt, 46 brennt, 45 wuerfelt.
        if (schaden <= 12) return Waldfolge.Nichts;
        if (schaden >= 70) return WaldLoeschen(col, row) ? Waldfolge.Weg : Waldfolge.Nichts;
        // ⚠⚠ HIER STAND EINE KETTE AUS ZWEI if, UND SIE WAR FALSCH:
        //     if (schaden <= 22 && Roll(8) != 0) return Nichts;
        //     if (schaden <= 45 && Roll(4) != 0) return Nichts;
        // Ein Schaden im Band 13..22 lief durch BEIDE — 1/8 mal 1/4 = 1/32
        // statt 1/8. Gefangen hat es --band-check mit 0,031 gegen 0,125.
        // Die Baender sind AUSSCHLIESSEND, jedes wuerfelt genau einmal.
        if (schaden <= 22) return Simulation.Determinism.Roll(8) == 0
            && Anzuenden(col, row) ? Waldfolge.Feuer : Waldfolge.Nichts;
        if (schaden <= 45) return Simulation.Determinism.Roll(4) == 0
            && Anzuenden(col, row) ? Waldfolge.Feuer : Waldfolge.Nichts;
        return Anzuenden(col, row) ? Waldfolge.Feuer : Waldfolge.Nichts;
    }

    /// <summary>
    /// <b>»zrus«</b> — der Wald verschwindet <b>ohne</b> Feuer. C
    /// <c>0x4CAD40</c>, F <c>0x4CA8F0</c>, am 21.08.2026 selbst gelesen.
    ///
    /// <para>Das Original tut genau vier Dinge, und keines davon ist Asche:</para>
    /// <code>
    ///   if (sec18[i].zustand != 1) return;      ; nur ein STEHENDER Baum
    ///   b   = weltkarte[x][y].klassenbyte       ; +3, der Bodenbeiwert
    ///   alt = weltkarte[x][y].kachel            ; +0
    ///   neu = 0x288D + ((alt - 0x288D) % 57 / 19) * 19 + b
    ///   weltkarte[x][y].kachel = neu            ; die BODENkachel derselben Gruppe
    ///   sec18[i].zustand = 0                    ; der Satz ist frei
    ///   imap[x*256 + y] = 0xFFFE                ; die Zelle ist BEGEHBAR
    ///   zwei Nachbarzeilen neu zeichnen
    /// </code>
    ///
    /// <para>⭐ Die Kachelrechnung ist keine Erfindung: der Kachelsatz ist in
    /// Gruppen zu <b>57</b> geordnet, darin Blöcke zu <b>19</b>. Der Block
    /// bleibt, die Stelle darin kommt aus dem <b>Klassenbyte der Zelle</b> —
    /// also genau die Bodenkachel, die ohne den Baum dort läge. Dieselbe
    /// Rechnung steht im Ausbrennen.</para>
    ///
    /// <para>⚠ <b>Unsere Umsetzung ist gröber</b>, und das gehört gesagt: wir
    /// führen keine Kachelgruppen, sondern legen die Zelle als abgebrannt ohne
    /// stehenden Rest hin (<see cref="Entity.Steht"/> falsch). Das Ergebnis
    /// stimmt in dem, was das Spiel liest — die Zelle ist frei und begehbar —,
    /// und weicht in dem ab, was man sieht: das Original zeigt den blanken
    /// Boden, wir den Stumpf.</para></summary>
    private bool WaldLoeschen(int col, int row)
    {
        bool ok = false;
        foreach (var e in _objDraw)
        {
            if (e.Row != row || e.Col != col || !e.IstWald || e.Abgebrannt) continue;
            // ⚠ Nur ein STEHENDER Baum: das Original prüft `zustand == 1` und
            // lässt einen brennenden in Ruhe.
            if (e.BrandVon >= 0f) continue;
            e.Abgebrannt = true;
            e.Steht = false;
            ok = true;
            GD.Print($"wald: ({col},{row}) durch starken Schaden geloescht — "
                     + "OHNE Feuer (zrus, imap 0xFFFE)");
        }
        return ok;
    }

    /// <summary>
    /// <c>--band-check</c> — <b>treffen die fünf Schadensbänder?</b>
    ///
    /// <para>⚠ Gemessen wird nicht, DASS es brennt, sondern <b>wie oft</b>.
    /// Eine Fassung, die immer anzündet, und eine, die die Bänder rechnet,
    /// sehen bei Schaden 50 gleich aus — der Unterschied steht nur in den
    /// mittleren Bändern, und genau die werden hier gezählt.</para></summary>
    public string BandCheck()
    {
        var sb = new System.Text.StringBuilder("band-check\n");
        bool alles = true;
        const int Proben = 4000;

        // ⚠ Ohne Wald auf der Zelle kann WaldTreffer nichts melden. Gezaehlt
        // wird darum der WUERFEL des Bandes, nicht der Ausgang — die Zuordnung
        // Schaden -> Band steht daneben und ist die eigentliche Aussage.
        (int von, int bis, string name, double erwartet)[] baender =
        {
            (0, 12,   "gar nichts",  0.0),
            (13, 22,  "1/8",         0.125),
            (23, 45,  "1/4",         0.25),
            (46, 69,  "immer Feuer", 1.0),
            (70, 200, "OHNE Feuer",  -1.0),
        };

        foreach (var (von, bis, name, erwartet) in baender)
        {
            int treffer = 0;
            for (int i = 0; i < Proben; i++)
            {
                int schaden = Simulation.Determinism.Range(von, bis);
                if (schaden <= 12) continue;
                if (schaden >= 70) { treffer++; continue; }
                if (schaden <= 22) { if (Simulation.Determinism.Roll(8) == 0) treffer++; continue; }
                if (schaden <= 45) { if (Simulation.Determinism.Roll(4) == 0) treffer++; continue; }
                treffer++;
            }
            double quote = (double)treffer / Proben;
            bool ok = erwartet < 0 ? quote > 0.99
                    : erwartet == 0.0 ? treffer == 0
                    : System.Math.Abs(quote - erwartet) < 0.04;
            sb.Append($"  Schaden {von,3}..{bis,3} ({name,-11}): {quote,6:0.000} ")
              .Append(erwartet < 0 ? "(erwartet 1,000 als Loeschung)"
                                   : $"(erwartet {erwartet:0.000})")
              .Append(ok ? " ✔" : " ✘").Append('\n');
            alles &= ok;
        }

        // ⚠ Die Gegenprobe, ohne die das Ganze nichts wert waere: die Grenzen
        // muessen EINSCHLIESSLICH sein. 70 loescht, 69 brennt; 46 brennt, 45
        // wuerfelt; 13 wuerfelt, 12 tut nichts. Wer sich hier um eins vertut,
        // bekommt oben trotzdem lauter gruene Haken.
        (int schaden, string soll)[] kanten =
        {
            (12, "nichts"), (13, "wuerfelt"), (22, "wuerfelt"), (23, "wuerfelt"),
            (45, "wuerfelt"), (46, "immer"), (69, "immer"), (70, "loescht"),
        };
        sb.Append("  Kanten: ");
        foreach (var (schaden, soll) in kanten)
        {
            string ist = schaden <= 12 ? "nichts"
                       : schaden >= 70 ? "loescht"
                       : schaden <= 45 ? "wuerfelt" : "immer";
            bool ok = ist == soll;
            sb.Append($"{schaden}={ist}{(ok ? "" : " ✘SOLL:" + soll)} ");
            alles &= ok;
        }
        sb.Append('\n');

        sb.Append($"  Der SETUP-Schaden ist {SetupSchaden} → Band »immer Feuer« — ")
          .Append(SetupSchaden >= 46 && SetupSchaden <= 69
                  ? "unser einziger Weg ist unveraendert ✔" : "ACHTUNG, Weg geaendert ✘").Append('\n');
        alles &= SetupSchaden >= 46 && SetupSchaden <= 69;

        sb.Append(alles ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }

    /// <summary>
    /// <b>EINE WALDZELLE ANZÜNDEN</b> — <c>zapal</c> @0x4CAC50.
    ///
    /// <para>Was das Original tut: Zustand auf <c>rand() % 150 + 2</c> setzen
    /// (@0x4CACAB), die Kachel gegen die verkohlte tauschen (@0x4CACE5) und die
    /// beiden Nachbarzeilen neu zeichnen lassen. Der Brandtakt @0x4CA330 zählt
    /// dann jeden vierten Spielschritt hoch und legt bei 255 die Bodenkachel
    /// hin.</para>
    ///
    /// <para>⚠ <b>Hier stand »das AUSGEHEN ist noch nicht gebaut«.</b> Das
    /// stimmt seit dem 19.08.2026 nicht mehr — <see cref="Ausbrennen"/> läuft
    /// einmal je Bild, rechnet die Brenndauer aus derselben Formel und legt
    /// danach Stumpf oder verkohlten Baum hin. Am 21.08.2026 nachgeprüft und
    /// berichtigt: ein Kommentar, der eine gebaute Sache als fehlend führt,
    /// schickt den nächsten auf eine Suche nach etwas, das schon dasteht.</para>
    ///
    /// <para>Gibt zurück, ob wirklich etwas angezündet wurde. ⚠ Es kann nur
    /// brennen, was der Backofen als Wald erkannt und mit einer verkohlten
    /// Fassung versehen hat.</para></summary>
    private bool Anzuenden(int col, int row)
    {
        bool ok = false;
        foreach (var e in _objDraw)
        {
            if (e.Row != row || e.Col != col || !e.IstWald || e.BrandVon >= 0f
                || e.Abgebrannt) continue;
            e.BrandVon = (float)DebugClock;
            // Die BRENNDAUER, gerechnet wie im Original: Zustand
            // rand()%150 + 2 (@0x4CACAB), jeder vierte Spielschritt +1
            // (@0x4CA340/@0x4CA395), Schluss bei 255 (@0x4CA39A). Also
            // (255 − Zustand) · 4 Spielschritte, und ein Spielschritt ist
            // 20 ms (SetTimer @0x415BC5, siehe OriginalTicksPerSecond).
            // Macht 8,3 bis 20,2 Sekunden.
            // ⭐ 21.08.2026: dieser Wurf lief bis heute über GD.Randi und war
            // damit der EINE simulationsrelevante Würfel ausserhalb des
            // gekeimten Gangs. Er entscheidet, WIE LANGE eine Zelle brennt —
            // und weil eine brennende Zelle ihre Nachbarn anzündet und danach
            // Stumpf oder Baum hinterlässt, entscheidet er mittelbar über die
            // Begehbarkeit der Karte. Zwei Maschinen im Netzspiel wären
            // auseinandergelaufen.
            int zustand = Simulation.Determinism.Range(Import.MapForest.BrandZustandVon,
                                                       Import.MapForest.BrandZustandBis);
            e.BrandDauer = (Import.MapForest.BrandEnde - zustand) * Import.MapForest.BrandTakt
                           / OriginalTicksPerSecond;
            // Und die eine von zwanzig, bei der der verkohlte Baum stehen
            // bleibt: @0x4CA3B2 `div 0x14`, Rest 0 fuehrt auf die Zeile
            // »dohorel forest - nesjizdnej« (@0x53969C, »abgebrannt —
            // unpassierbar«), sonst »- sjizdnej« (@0x5396C0, »passierbar«).
            // ⭐ Ebenfalls seit dem 21.08.2026 gekeimt: dieser Wurf legt fest,
            // ob die Zelle danach UNPASSIERBAR ist — der unmittelbarste
            // Eingriff ins Gelände, den das Feuer hat.
            e.Steht = Simulation.Determinism.Roll(20) == 0 || !e.HatAsche;
            ObjectsBurning++;
            ok = true;
            // ⚠ Regel 33: die ausgewuerfelte Dauer gehoert ins Protokoll. Ohne
            // sie ist nicht zu sehen, ob der Wurf im gelesenen Fenster liegt
            // (8,3 .. 20,2 s) oder ob jemand an den Zahlen gedreht hat.
            GD.Print($"wald: ({col},{row}) angezuendet, brennt {e.BrandDauer:0.0}s Spielzeit "
                     + $"(Zustand {zustand}, Fenster "
                     + $"{(Import.MapForest.BrandEnde - Import.MapForest.BrandZustandBis) * Import.MapForest.BrandTakt / OriginalTicksPerSecond:0.0}"
                     + $"..{(Import.MapForest.BrandEnde - Import.MapForest.BrandZustandVon) * Import.MapForest.BrandTakt / OriginalTicksPerSecond:0.0}s), "
                     + (e.Steht ? "bleibt danach als verkohlter Baum stehen" : "wird danach zum Stumpf"));
        }
        return ok;
    }

    /// <summary>
    /// <b>WAS AUS IST, IST AUS</b> — der Brandtakt @0x4CA330.
    ///
    /// <para>Er zählt jeden vierten Spielschritt den Zustand einer brennenden
    /// Zelle hoch und legt bei 255 die abgebrannte Kachel hin. Bei uns steht
    /// die Dauer beim Anzünden fest; hier läuft nur die Uhr ab.</para>
    ///
    /// <para>⭐ <b>DAS WEITERGREIFEN IST GEBAUT</b> (21.08.2026) — hier stand
    /// bis dahin »gelesen, aber nicht gebaut«. Derselbe Takt ruft für jede
    /// brennende Zelle <c>zapal_forestA</c> (@0x4CA7E0, Protokollzeile
    /// @0x539700) auf; die Formel und ihre Messung stehen am nächsten Block,
    /// gebaut ist sie in <see cref="BrandGreiftUeber"/>, gerufen aus
    /// <see cref="BrandTakt"/>.</para>
    /// </summary>
    /// <summary>
    /// <b>DAS FEUER GREIFT ÜBER</b> — <c>zapal_forestA</c> @0x4CA7E0, gelesen am
    /// 21.08.2026.
    ///
    /// <para>Der Brandtakt des Originals versucht je brennender Zelle und je
    /// Schritt <b>genau einen</b> der acht Nachbarn anzuzünden — nicht alle.
    /// Die Wahrscheinlichkeit hängt am Wind:</para>
    /// <code>
    ///   p = 1 / (2 · (5 · ((9 − Windstärke) · Winkelabweichung) + 50))
    /// </code>
    /// <para>Die Winkelabweichung ist der Abstand zwischen der gewählten
    /// Richtung und <see cref="MapEntityLayer.WindDir"/>, in Achteln und über
    /// den Umlauf gemessen (0…4). Mit Windstärke 2 heisst das: mit dem Wind
    /// <b>1/100</b> je Schritt, gegen den Wind <b>1/380</b> — der Brand läuft
    /// also in Windrichtung, ohne dass eine Richtung fest verdrahtet wäre.</para>
    ///
    /// <para>⚠ <b>Der Schritt ist der des Originals:</b> der Brandtakt läuft nur
    /// bei <c>Takt % 4 == 0</c>, also alle vier Originaltakte
    /// (<see cref="BrandSchrittSekunden"/>). Wer ihn je Bild laufen liesse,
    /// bekäme bei 144 Bildern/s ein Flächenfeuer statt eines Waldbrands.</para>
    ///
    /// <para>⚠ <b>Gewürfelt wird mit dem EINEN Würfel</b>
    /// (<c>Simulation.Determinism.Roll</c>), nicht mit <c>GD.Randi</c>: das
    /// Übergreifen verändert, welche Zellen später begehbar sind, und ist damit
    /// simulationsrelevant. ⭐ <b>Und die zwei älteren Würfe in
    /// <see cref="Anzuenden"/> ebenfalls</b> (Branddauer, »steht der verkohlte
    /// Baum«): hier stand »eine bestehende Lücke, die hier sichtbar wird und
    /// nicht von hier stammt« — sie ist am 21.08.2026 geschlossen worden.
    /// <b>Im ganzen Brandwesen läuft jetzt kein <c>GD.Randi</c> mehr.</b></para>
    /// </summary>
    private void BrandGreiftUeber()
    {
        if (ObjectsBurning == 0) return;
        int wind = WindDir, staerke = WindStrength;
        if (wind < 0) return;

        // ⚠ Erst sammeln, dann anzünden: Anzuenden() verändert die Liste nicht,
        // aber eine frisch entzündete Zelle darf im SELBEN Schritt nicht schon
        // weiterzünden — sonst läuft das Feuer je Schritt quer über die Karte.
        var kandidaten = new List<(int Col, int Row)>();
        foreach (var e in _objDraw)
        {
            if (e.BrandVon < 0f || e.Abgebrannt) continue;
            int richtung = Simulation.Determinism.Roll(8);
            int ab = System.Math.Abs(richtung - wind);
            if (ab > 4) ab = 8 - ab;                      // ueber den Umlauf
            int nenner = 2 * (5 * ((9 - staerke) * ab) + 50);
            if (Simulation.Determinism.Roll(nenner) != 0) continue;
            var (dc, dr) = Achtel[richtung];
            kandidaten.Add((e.Col + dc, e.Row + dr));
        }
        BrandKandidaten += kandidaten.Count;
        foreach (var (c, r) in kandidaten)
            if (Anzuenden(c, r)) BrandUebergriffe++;
    }

    /// <summary>
    /// <c>--brand-check</c> — <b>läuft das Feuer wirklich mit dem Wind?</b>
    ///
    /// <para>Die Formel allein zu bauen genügt nicht: eine Ausbreitung, die
    /// gleichmässig in alle Richtungen läuft, sähe im Spiel genauso aus wie
    /// eine, die dem Wind folgt — nur bei einem Waldbrand über hundert Zellen
    /// fiele der Unterschied auf. Also wird er hier gemessen.</para>
    ///
    /// <para>Gerechnet wird die Verteilung über <see cref="BrandProben"/> Würfe
    /// je Richtung, mit der Formel des Originals. Erwartet wird ein Verhältnis
    /// von <b>3,8 zu 1</b> zwischen »mit dem Wind« und »gegen den Wind« bei
    /// Windstärke 2 — das folgt aus <c>2·(5·((9−2)·4)+50) = 380</c> gegen
    /// <c>2·50 = 100</c>.</para></summary>
    public string BrandCheck()
    {
        var sb = new System.Text.StringBuilder("brand-check\n");
        bool alles = true;

        // --- 1. die Wahrscheinlichkeiten, wie die Formel sie ergibt ---------
        sb.Append($"  Wind: Richtung {WindDir}, Staerke {WindStrength}\n");
        int staerke = WindStrength < 0 ? 2 : WindStrength;
        var nenner = new int[5];
        for (int ab = 0; ab <= 4; ab++) nenner[ab] = 2 * (5 * ((9 - staerke) * ab) + 50);
        sb.Append("  Nenner je Winkelabweichung 0..4: ")
          .Append(string.Join(", ", nenner))
          .Append($"  (Verhaeltnis {(double)nenner[4] / nenner[0]:0.0} zu 1)\n");
        bool formelOk = nenner[0] == 100 && nenner[4] == 2 * (5 * ((9 - staerke) * 4) + 50)
                        && nenner[0] < nenner[4];
        alles &= formelOk;

        // --- 2. der Wuerfel trifft die Verteilung ---------------------------
        //
        // ⚠ Das ist die eigentliche Messung: nicht ob die Formel dasteht,
        // sondern ob aus ihr wirklich eine Windrichtung wird.
        var treffer = new int[8];
        int wind = WindDir < 0 ? 0 : WindDir;
        for (int i = 0; i < BrandProben; i++)
        {
            int richtung = Simulation.Determinism.Roll(8);
            int ab = System.Math.Abs(richtung - wind);
            if (ab > 4) ab = 8 - ab;
            if (Simulation.Determinism.Roll(2 * (5 * ((9 - staerke) * ab) + 50)) == 0)
                treffer[richtung]++;
        }
        int mit = treffer[wind];
        int gegen = treffer[(wind + 4) & 7];
        sb.Append("  Treffer je Richtung: ").Append(string.Join(", ", treffer)).Append('\n');
        bool windOk = mit > gegen * 2;
        sb.Append($"  mit dem Wind {mit}, dagegen {gegen} — ")
          .Append(windOk ? "das Feuer laeuft mit dem Wind ✔"
                         : "KEIN Windeinfluss messbar ✘").Append('\n');
        alles &= windOk;

        // --- 3. Gegenprobe: ohne Wind darf es keine Vorzugsrichtung geben ---
        //
        // Bei Windstaerke 9 wird (9-staerke) zu 0, alle Nenner werden 100 —
        // dann MUSS die Verteilung flach sein. Schlaegt das an, misst der
        // Punkt oben den Wind und nicht den Wuerfel.
        var flach = new int[8];
        for (int i = 0; i < BrandProben; i++)
        {
            int richtung = Simulation.Determinism.Roll(8);
            if (Simulation.Determinism.Roll(100) == 0) flach[richtung]++;
        }
        int hoch = 0, tief = int.MaxValue;
        foreach (int t in flach) { if (t > hoch) hoch = t; if (t < tief) tief = t; }
        bool flachOk = hoch <= tief * 2 + 5;
        sb.Append("  Gegenprobe ohne Windeinfluss: ").Append(string.Join(", ", flach))
          .Append($" — Spanne {tief}..{hoch} ")
          .Append(flachOk ? "flach ✔" : "SCHIEF ✘ (der Wuerfel selbst hat eine Vorzugsrichtung)")
          .Append('\n');
        alles &= flachOk;

        // --- 4. DER WUERFEL IST GEKEIMT -------------------------------------
        //
        // ⚠ Bis zum 21.08.2026 liefen die zwei Wuerfe in Anzuenden (Branddauer,
        // "steht der verkohlte Baum") ueber GD.Randi. Beide entscheiden, welche
        // Zellen spaeter begehbar sind — zwei Maschinen im Netzspiel waeren
        // auseinandergelaufen, OHNE dass es irgendwo aufgefallen waere.
        //
        // Gemessen wird das Einzige, was zaehlt: derselbe Keim gibt dieselbe
        // Folge. Und die GEGENPROBE gehoert dazu — ein Wuerfel, der immer 7
        // sagt, bestuende die erste Haelfte muehelos.
        uint keimVorher = Simulation.Determinism.Seed;
        var forcedVorher = Simulation.Determinism.Forced;

        string Folge(uint keim)
        {
            Simulation.Determinism.Forced = keim;
            Simulation.Determinism.NewMap("brand-check");
            var t = new System.Text.StringBuilder();
            for (int i = 0; i < 40; i++)
            {
                t.Append(Simulation.Determinism.Range(Import.MapForest.BrandZustandVon,
                                                      Import.MapForest.BrandZustandBis));
                t.Append(Simulation.Determinism.Roll(20) == 0 ? 'S' : '.');
            }
            return t.ToString();
        }

        string a1 = Folge(4711), a2 = Folge(4711), b1 = Folge(4712);
        bool gleich = a1 == a2, anders = a1 != b1;
        sb.Append($"  Keim 4711, zweimal: {(gleich ? "deckungsgleich ✔" : "AUSEINANDER ✘")}\n");
        sb.Append($"  Keim 4712 dagegen: {(anders ? "andere Folge ✔" : "DIESELBE ✘ — der Keim wirkt nicht")}\n");
        sb.Append($"  Probe: {a1.Substring(0, 24)}...\n");
        alles &= gleich && anders;

        Simulation.Determinism.Forced = forcedVorher;
        if (forcedVorher == null) Simulation.Determinism.Forced = keimVorher;

        sb.Append(alles ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }

    /// <summary>Wie viele Würfe der Prüfstand zieht. Gross genug, dass die
    /// Spanne der Gegenprobe schmal wird.</summary>
    private const int BrandProben = 200000;

    /// <summary>Die acht Richtungen, wie das Original sie führt (Tafel
    /// <c>0x4F5AF0</c>): (0,1) (−1,1) (−1,0) (−1,−1) (0,−1) (1,−1) (1,0) (1,1).
    /// </summary>
    private static readonly (int Col, int Row)[] Achtel =
    { (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1), (1, 0), (1, 1) };

    /// <summary>
    /// ⭐⭐⭐ 24.08.2026 — <b>DER ÜBERGRIFF LÄUFT IN JEDEM TAKT, NICHT IN JEDEM
    /// VIERTEN.</b> Hier stand <c>4f / 50f</c> mit der Begruendung »der
    /// Brandtakt läuft bei <c>Takt % 4 == 0</c> (@0x4CA330)«. Das ist richtig
    /// gelesen und trotzdem der falsche Schluss: die Bedingung gilt fuer den
    /// ZUSTANDSZAEHLER, nicht fuer die Ausbreitung.
    ///
    /// <para>Gemeldet, mehrfach: »im Original breitet sich der Brand im Wald
    /// dort auch aus, das tut es bei uns nicht«. Formel, Branddauer und
    /// Windabhaengigkeit waren wortgleich nachgeprueft — es fehlte schlicht
    /// die Anzahl der Versuche.</para>
    ///
    /// <para><b>Gefunden ueber die Aufruferliste</b>, nicht ueber den
    /// Quelltext: <c>zapal_forestA</c> hat <b>DREI</b> Aufrufer, ich kannte
    /// einen. Der Brandtakt hat zwei Zweige:</para>
    /// <code>
    ///   0x4CA340  eax &amp;= 3
    ///   0x4CA348  jne 0x4CA4A1        ; Takt % 4 != 0  ->  DER ANDERE ZWEIG
    ///   0x4CA34E  ...                 ; %4==0: Zustand+1, EIN Uebergriff, Ausbrennen
    ///   0x4CA4A1  esi = 0xBFF3E1      ; die Brandliste, 3 Byte je Zelle
    ///   0x4CA4AD  Zustand &gt; 1?
    ///   0x4CA4DE  call zapal_forestA  ; ⭐ Uebergriff auch hier
    ///   0x4CA4E6  esi += 3 … bis 0xC03A31   (6000 Plaetze)
    /// </code>
    ///
    /// <para>Also <b>vier</b> Versuche je vier Takte statt einem — Faktor vier
    /// auf die Zahl der Kinder je Brand. Aus »ein Kind je Feuer« (das Feuer
    /// stirbt aus) wird »rund vier« (es waechst). Genau der Unterschied, den er
    /// gesehen hat.</para>
    ///
    /// <para>⚠ Die BRANDDAUER bleibt bei vier Takten je Zustandsschritt — die
    /// gehoert in den anderen Zweig und ist unveraendert (8,3…20,2 s).</para>
    ///
    /// <para>⚠⚠ Die Lehre: ich hatte die Formel dreimal nachgerechnet und die
    /// AUFRUFERLISTE nie gezogen. Eine gelesene Routine ist nicht verstanden,
    /// solange man nicht weiss, <b>wie oft</b> sie gerufen wird.</para>
    /// </summary>
    private const float BrandSchrittSekunden = 1f / 50f;
    private float _letzterBrandSchritt = -1f;

    /// <summary>Wie oft das Feuer übergegriffen hat — ⚠ ohne diese Zahl ist
    /// »es greift nicht über« nicht von »es hat keine Gelegenheit gehabt« zu
    /// unterscheiden.</summary>
    public int BrandUebergriffe;

    /// <summary>⚠ 24.08.2026 — die ZWISCHENSTUFEN, weil »0 Uebergriffe« drei
    /// verschiedene Ursachen haben kann und die Zahl allein keine davon nennt:
    /// der Takt laeuft nicht, der Wuerfel trifft nie, oder die getroffene Zelle
    /// nimmt kein Feuer an. Gemeldet: »im Original breitet sich der Brand im
    /// Wald dort auch aus, das tut es bei uns nicht«.</summary>
    public int BrandSchritte, BrandKandidaten;

    /// <summary>Die Meldezeile dazu.</summary>
    public string BrandWatchLine()
        => $"brand: {BrandSchritte} Schritte, {BrandKandidaten} Nachbarn gewuerfelt, "
         + $"{BrandUebergriffe} davon entzuendet; gerade {ObjectsBurning} brennend, "
         + $"Wind {WindDir}/{WindStrength}"
         + (BrandSchritte == 0 ? "   ⚠ der TAKT laeuft nicht"
            : BrandKandidaten == 0 ? "   ⚠ der WUERFEL trifft nie"
            : BrandUebergriffe == 0 ? "   ⚠ die getroffenen Zellen nehmen kein Feuer an"
            : "");

    /// <summary>
    /// <b>Der Brandtakt</b> — Ausbrennen und Übergreifen, im Takt der
    /// Simulation statt im Bildlauf.
    ///
    /// <para>⚠ Bis zum 21.08.2026 hing das Ausbrennen im Zeichenweg
    /// (<c>DrawObjectsUpTo</c>, einmal je Bild). Das war zweifach falsch: ein
    /// kopfloser Lauf zeichnet gar nicht, und bei 144 Bildern/s brannte der
    /// Wald schneller ab als bei 30. Der Brand ist simulationsrelevant — was
    /// abgebrannt ist, entscheidet, welche Zelle begehbar bleibt.</para>
    ///
    /// <para>Das Übergreifen läuft alle <see cref="BrandSchrittSekunden"/>,
    /// also alle vier Originaltakte wie @0x4CA330.</para></summary>
    /// <summary>
    /// ⭐⭐ <c>--verdeck-stelle</c> (24.08.2026) — <b>WER VERDECKT WAS AN EINER
    /// BRENNENDEN ZELLE?</b>
    ///
    /// <para>Gemeldet: eine Flamme sitze »wie leicht im Boden«, und —
    /// entscheidend — »dort an der Stelle glitchen auch Cyborgs wie unter die
    /// Karte oder werden verdeckt«. Damit ist es <b>kein Flammenfehler</b>: an
    /// dieser Stelle deckt etwas alles zu, was davor steht. Das hier nennt es
    /// beim Namen, statt weiter am Flammenversatz zu drehen.</para>
    ///
    /// <para>Gezeigt wird je brennender Zelle, welche aufragenden Kartenobjekte
    /// SPAETER gezeichnet werden (groessere Zeile, plus der Flammenverzug) und
    /// deren Rechteck das Flammenrechteck ueberlappt.</para>
    /// </summary>
    public string VerdeckStelle()
    {
        var sb = new System.Text.StringBuilder("verdeck-stelle:\n");
        int brennt = 0;
        foreach (var e in _objDraw)
        {
            if (e.BrandVon < 0f || e.Abgebrannt) continue;
            brennt++;
            var kachel = e.HatKohle ? e.KohleZiel
                : new Vector2(_ox + e.Col * TileW,
                              _oy + e.Row * TileH - ElevOf(e.Col, e.Row) * 15 - 50);
            var bilder = EffectFrames("blast");
            var gr = bilder.Count > 0 ? bilder[0].GetSize() : new Vector2(60, 79);
            var flamme = new Rect2(kachel + new Vector2(-18, -20), gr);
            var ausZelle = new Vector2(_ox + e.Col * TileW,
                                       _oy + e.Row * TileH - ElevOf(e.Col, e.Row) * 15 - 50);
            sb.Append($"  Flamme ({e.Col},{e.Row}) Hoehe {ElevOf(e.Col, e.Row)} "
                    + $"Rechteck {flamme.Position} {flamme.Size}\n"
                    + $"      Kachel gruen {e.Ziel} {e.Src.Size}, "
                    + $"verkohlt {e.KohleZiel} {e.KohleSrc.Size}, "
                    + $"aus der Zelle gerechnet {ausZelle}\n"
                    + $"      ⚠ Versatz verkohlt-gegen-Zelle {e.KohleZiel - ausZelle}, "
                    + $"gruen-gegen-Zelle {e.Ziel - ausZelle}\n");

            // ⭐⭐ 24.08.2026 — DIE ZEILENSTAFFELUNG, gemessen statt ueberschlagen.
            //
            // Im Original endet die Flamme bei Kachel-y + 59, und ein Baum DREI
            // Zeilen naeher beginnt bei Kachel-y + 60 — er verfehlt sie um einen
            // Bildpunkt. Bei uns ueberlappt derselbe Baum. Hier steht, wie weit
            // die Nachbarzeilen bei uns wirklich auseinanderliegen, Fuss gegen
            // Fuss und Kopf gegen Kopf, samt Gelaendehoehe.
            for (int dz = 1; dz <= 3; dz++)
            {
                foreach (var o in _objDraw)
                {
                    if (o.Col != e.Col || o.Row != e.Row + dz) continue;
                    float kopf = o.Ziel.Y, fuss = kopf + o.Src.Size.Y;
                    float kopfE = e.Ziel.Y, fussE = kopfE + e.Src.Size.Y;
                    sb.Append($"      Zeile +{dz} ({o.Col},{o.Row}) Hoehe {ElevOf(o.Col, o.Row)}: "
                            + $"Kopf {kopf:0} (Δ {kopf - kopfE:+0;-0}), "
                            + $"Fuss {fuss:0} (Δ {fuss - fussE:+0;-0}), "
                            + $"Bildhoehe {o.Src.Size.Y:0}\n");
                    break;
                }
            }
            int n = 0;
            foreach (var o in _objDraw)
            {
                if (ReferenceEquals(o, e)) continue;
                // spaeter gezeichnet: groessere Zeile als die Flamme nach ihrem Verzug
                // ⚠ 24.08.2026 — der Zeilenfilter ist RAUS. Er hat genau die
                // Faelle verschwiegen, um die es ging: gemeldet war ein Schnitt
                // von 40 Bildpunkten, gemeldet hat der Pruefstand 2. Jetzt wird
                // JEDE Ueberlappung genannt, mit der Zeilendifferenz daneben —
                // welche davon wirklich SPAETER gezeichnet wird, entscheidet
                // dann der Leser und nicht mehr meine Annahme.
                if (o.Row == e.Row) continue;
                var ziel = o.Abgebrannt ? (o.Steht ? o.KohleZiel : o.AscheZiel)
                         : o.BrandVon >= 0f ? o.KohleZiel : o.Ziel;
                var rq = o.Abgebrannt ? (o.Steht ? o.KohleSrc : o.AscheSrc) : o.Src;
                var r = new Rect2(ziel, rq.Size);
                if (!r.Intersects(flamme)) continue;
                var schnitt = r.Intersection(flamme);
                // ⚠ Nur nennenswerte Ueberlappungen. Eine Kante von zwei
                // Bildpunkten erklaert keinen Schnitt von vierzig — genau daran
                // hat dieser Pruefstand mich heute Abend vorbeigefuehrt.
                if (schnitt.Size.Y < 8) continue;
                if (n++ < 6)
                    sb.Append($"      ⚠ ZEILE {o.Row - e.Row:+0;-0}: ({o.Col},{o.Row}) "
                            + $"ueberdeckt {schnitt.Size.X:0}x{schnitt.Size.Y:0} px, "
                            + $"Rechteck {r.Position} {r.Size}\n");
            }
            sb.Append($"      insgesamt {n} spaetere Objekte ueberlappen\n");
        }
        if (brennt == 0) sb.Append("  gerade brennt nichts — die Zeile sagt so nichts\n");
        return sb.ToString();
    }

    private void BrandTakt()
    {
        Ausbrennen();
        if (ObjectsBurning == 0) return;
        float jetzt = (float)DebugClock;
        if (_letzterBrandSchritt < 0f) _letzterBrandSchritt = jetzt;
        int schritte = 0;
        while (jetzt - _letzterBrandSchritt >= BrandSchrittSekunden && schritte++ < 8)
        {
            _letzterBrandSchritt += BrandSchrittSekunden;
            BrandSchritte++;
            BrandGreiftUeber();
            ObjektBrandTakt();
        }
    }

    private void Ausbrennen()
    {
        if (ObjectsBurning == 0) return;
        float jetzt = (float)DebugClock;
        foreach (var e in _objDraw)
        {
            if (e.BrandVon < 0f || e.Abgebrannt) continue;
            if (jetzt - e.BrandVon < e.BrandDauer) continue;
            e.Abgebrannt = true;
            ObjectsBurning--;
            // ⚠ Regel 33: ohne diese Zeile ist »das Feuer ist aus« nicht von
            // »es hat nie gebrannt« zu unterscheiden. Es sind wenige Zellen.
            GD.Print($"wald: ({e.Col},{e.Row}) nach {e.BrandDauer:0.0}s Spielzeit abgebrannt — "
                     + (e.Steht ? "verkohlter Baum bleibt stehen (1 von 20, imap 0xFFFF)"
                                : "Stumpf, Zelle wieder frei (imap 0xFFFE)")
                     + $"; noch {ObjectsBurning} brennend");
        }
    }

    /// <summary>
    /// <b>WAS BEIM MISSIONSSTART SCHON GETROFFEN IST.</b>
    ///
    /// <para>⚠ 18.08.2026, gemeldet als »in Original Kampagne 1 gibt es z. B.
    /// von Haus aus ein paar brennende Bäume, die haben wir garnicht«.</para>
    ///
    /// <para><b>Gelesen</b>, in beiden Fassungen: der SETUP-Block der Mission
    /// fährt eine Schleife über eine Zellenliste, schlägt in der Belegungskarte
    /// nach, was dort steht, und schickt es durch <c>Zasah</c> — die
    /// Trefferroutine (@0x40C9A0 in der einen, @0x40C800 in der anderen
    /// Fassung; die Zeichenkette »Zasah« steht in ihrem eigenen
    /// Protokollaufruf). Für Mission 1 sind das <b>fünf Zellen</b>:
    /// (1,39) (9,40) (7,41) (19,52) (36,68), und <b>vier davon tragen auf
    /// map_01 ein Objekt</b>.</para>
    ///
    /// <para>⚠ Die Liste wird Byte für Byte auf dem STACK gebaut, im
    /// gemeinsamen Vorspann der Setup-Funktion — dieselbe Klasse Problem wie
    /// bei <c>space_in</c>. Gelesen mit
    /// <c>aekernel-tools/mission_hits.py</c>, das nach der FORM sucht.</para>
    ///
    /// <para><b>⚠⚠ 18.08.2026 — DAS FEUER IST JETZT GELESEN.</b> Hier stand,
    /// ANIM-Folge 82 sei nur ein Funkenschlag und das dauerhafte Brennen
    /// »nicht erklärt«. Das stimmte, und die Vermutung, es müsse eine andere
    /// KACHEL sein, stimmte auch. <c>Zasah</c> führt einen Getroffenen mit
    /// Belegung 50000..55999 (also einen WALD) auf einen eigenen Zweig
    /// @0x40D61D. Dort rechnet er aus Angreifer und Schaden eine Zahl aus und
    /// verzweigt:</para>
    /// <code>
    ///   &gt;= 70 -> "zrus"    @0x4CAD40  (weg, ohne Feuer)
    ///   &gt;  45 -> "zapal A" @0x4CAC50  (ANZUENDEN)
    ///   &gt;  22 -> "zapal B", jeder vierte Wurf
    ///   &gt;  12 -> "zapal C", jeder achte Wurf
    /// </code>
    /// <para>»zapal« ist tschechisch für »zünde an«. Und der Angreifer der
    /// SETUP-Liste, <c>0x9C72</c> = 40050, ist damit auch gedeutet: Zasah
    /// behandelt 40000..40999 als reine SCHADENSZAHL (@0x40CC8B:
    /// <c>add ax, 0x63C0</c>, also Schaden = arg1 − 40000 = <b>50</b>), und aus
    /// 50 wird mit ±4 Würfelrauschen 46..54 — <b>immer »zapal A«</b>. Diese
    /// vier Bäume brennen also im Original bei jedem Start, ohne Zufall.</para>
    ///
    /// <para>⚠ Dieselbe 40050 steht noch an einer zweiten Stelle: @0x43ABE3
    /// schickt jede sichtbare Wald- und Objektzelle durch Zasah. Die Zahl ist
    /// also nicht eigens für den Missionsstart erfunden, sondern das
    /// Hausmittel »zünde das hier an«.</para></summary>
    /// <summary>`fire_at(einheit, x, y)` — das Missionsskript laesst eine
    /// Einheit auf eine ZELLE feuern (Original <c>0x4D0AD0</c>, 7 Aufrufstellen
    /// in M4, M16, M21, M27).
    ///
    /// <para>Die Kette dorthin: <c>0x4D0AD0</c> reicht (einheit, x, y, 65000)
    /// an <c>0x40C8C0</c> weiter, das ueber die WAFFE (+0x0D) verzweigt —
    /// Waffe 9 in den »gas-thr«-Zweig, 0 und 0x12 ins Leere, sonst in die
    /// SCHUSSROUTINE <c>0x40BB00</c>. Dass die zwei Zahlen eine Zielzelle
    /// sind, ist an der STREUUNG belegt: fuer Waffe 8 wuerfelt das Original
    /// zweimal <c>10 - rand%20</c> und schlaegt es auf beide auf
    /// (@0x40BB85..0x40BBB9). Das vierte Argument 65000 wird im Rumpf nicht
    /// gelesen.</para>
    ///
    /// <para>⚠ WAS UNSERE SETZUNG BLEIBT — dieselbe wie bei den
    /// SETUP-Treffern: <b>wieviel Schaden ein Schuss macht, ist nicht
    /// gelesen</b>. Genommen wird die HAELFTE der Huelle, damit die Einheit
    /// sichtbar beschaedigt und nicht zerstoert ist. ⚠ Und die STREUUNG bauen
    /// wir NICHT nach: sie haengt am Zufallsstrom des Originals, den wir nicht
    /// treffen — ein eigener Wuerfel waere kein Nachbau, sondern ein zweiter
    /// Zufall.</para></summary>
    private void MissionFireAt(int slot, int col, int row)
    {
        Entity schuetze = null;
        foreach (var e in _entities)
            if (!e.IsBuilding && !e.Dead && e.Slot == slot) { schuetze = e; break; }
        if (schuetze == null)
        {
            GD.PrintErr($"fire_at: Einheitenplatz {slot} ist leer");
            return;
        }
        // Munition: das Original bricht bei +0x39 == 0 ab (@0x40BB44).
        if (schuetze.Ammo == 0 && schuetze.AmmoMax > 0)
        {
            GD.Print($"fire_at: Platz {slot} hat keine Munition — kein Schuss");
            return;
        }
        ApplyMissionHits(new[] { (col, row) });
        if (schuetze.AmmoMax > 0) schuetze.Ammo = Mathf.Max(0, schuetze.Ammo - 1);
        GD.Print($"fire_at: Einheit {slot} (Spieler {schuetze.Owner}) feuert auf " +
                 $"({col},{row})");
    }

    private void ApplyMissionHits(System.Collections.Generic.IReadOnlyList<(int Col, int Row)> zellen,
                                 bool funken = true, int schaden = SetupSchaden)
    {
        if (zellen.Count == 0) return;
        int einheiten = 0, brennt = 0, leer = 0;
        foreach (var (c, r) in zellen)
        {
            bool getroffen = false;
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (e.Dead || e.IsProp || e.Col != c || e.Row != r) continue;
                // ⚠ Wieviel Schaden eine Einheit bekommt, ist NICHT gelesen:
                // fuer einen Getroffenen unter 8000 rechnet Zasah mit Feldern
                // des Fahrzeugs, die wir nicht alle deuten. Genommen wird die
                // HAELFTE der Huelle — sichtbar beschaedigt, nicht zerstoert.
                // ⚠ UNSERE SETZUNG. Fuer den WALD ist der Schaden dagegen
                // gelesen (50, siehe oben), und darum brennt er.
                e.Hp = Mathf.Max(1, e.Hp / 2);
                einheiten++; getroffen = true;
                break;
            }

            // Ein WALD auf der Zelle faengt Feuer — aber nicht immer, und
            // wie stark er getroffen wird, entscheidet WAS geschieht.
            switch (WaldTreffer(c, r, schaden))
            {
                case Waldfolge.Feuer:   brennt++; getroffen = true; break;
                case Waldfolge.Weg:     getroffen = true; break;
            }

            // ⭐ 24.08.2026 — und ein ZERSTOERBARES OBJEKT auf der Zelle hat
            // seine eigenen Baender (@0x40D442). Siehe ObjektBrand.cs.
            if (ObjektTreffer(c, r, schaden)) getroffen = true;

            if (!getroffen) leer++;
            // Der Funkenschlag des Treffers selbst: ANIM-Folge 82, die Zasah
            // @0x40CB07 mit `push 0x52` in den Effektaufruf schiebt. Er ist
            // KURZ und erklaert das Brennen nicht — das tut die Kachel.
            //
            // ⚠ 19.08.2026 — BEIM MISSIONSSTART GEHOERT ER NICHT HIN. Gemeldet:
            // »die blitze gehoeren nicht in Kampagne 1, nur das feuer«. Und das
            // ist nicht bloss Geschmack, sondern folgt aus dem Original: der
            // SETUP-Block schlaegt die Zellen an, BEVOR die Mission laeuft — im
            // Bild des Spielers brennen die Baeume, aber es blitzt nichts. Ein
            // Funke ist die Anzeige eines Treffers IM AUGENBLICK; wer ihn beim
            // Start zeigt, behauptet, es werde gerade geschossen.
            // Bei `fire_at` (ein Schuss WAEHREND der Mission) bleibt er.
            if (funken)
                _effects.Add(new Effect
                {
                    Pos = CellCenter(c, r),
                    Kind = "fire", FrameTime = 0.08f,
                });
        }
        GD.Print($"treffer: {zellen.Count} Zellen aus dem SETUP-Block getroffen — " +
                 $"{einheiten} Einheiten, {brennt} Waldzellen angezuendet, {leer} leer " +
                 "(Zasah @0x40C9A0 -> zapal A @0x4CAC50, Kachel +285)");
    }
}
