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

    /// <summary>Ein aufragendes Objekt, so wie der Zeichner es braucht.</summary>
    private sealed class Kartenobjekt
    {
        /// <summary>Die Zelle — die Zeile entscheidet über das Zeilenfach.</summary>
        public int Col, Row;

        /// <summary>Sein Rechteck in der zweiten Ebene. ⚠ Quelle UND Ziel: der
        /// Backofen hat es an genau die Stelle gemalt, an die es gehört.</summary>
        public Rect2 Src;

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
            };
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
            _objDraw.Add(e);
        }
        // ⚠ Nach ZEILE sortieren, nicht nach Lage im Bild: das Zeilenfach
        // entscheidet, was vor wem liegt, und der Backofen liefert schon in
        // Zeilenfolge — die Sortierung ist die Zusicherung, nicht die Arbeit.
        _objDraw.Sort((a, b) => a.Row - b.Row);
        int brennbar = 0;
        foreach (var e in _objDraw) if (e.HatKohle) brennbar++;
        GD.Print($"objekte: {_objDraw.Count} aufragende Kartenobjekte aus " +
                 $"{mapName}.objects.png — sie verdecken jetzt Einheiten; " +
                 $"{brennbar} davon sind Wald und koennen brennen ({kohle.Count} verkohlte Kacheln)");
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
        if (at == 0) Ausbrennen();
        for (; at < _objDraw.Count && _objDraw[at].Row <= throughRow; at++)
        {
            var e = _objDraw[at];
            if (e.Abgebrannt)
            {
                // AUS. 19 von 20 Zellen zeigen die abgebrannte Kachel (Stumpf
                // bzw. blanker Boden), bei jeder zwanzigsten bleibt der
                // verkohlte Baum stehen — siehe Ausbrennen.
                var q = e.Steht ? e.KohleSrc : e.AscheSrc;
                var z = e.Steht ? e.KohleZiel : e.AscheZiel;
                DrawTextureRectRegion(_objTex, new Rect2(z, q.Size), q);
            }
            else if (e.BrandVon >= 0f && e.HatKohle)
            {
                // BRENNT: die verkohlte Kachel steht anstelle des grünen Baums
                // (das ist der Kacheltausch aus zapal @0x4CACE5), und die
                // Flamme läuft darüber.
                DrawTextureRectRegion(_objTex, new Rect2(e.KohleZiel, e.KohleSrc.Size), e.KohleSrc);
                Flamme(e);
            }
            else
            {
                DrawTextureRectRegion(_objTex, new Rect2(e.Src.Position, e.Src.Size), e.Src);
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
    private void Flamme(Kartenobjekt e)
    {
        var bilder = EffectFrames("blast");
        if (bilder.Count == 0) return;
        // Der Index tut im Original zweierlei: er versetzt die PHASE, damit
        // nicht alle Bäume im Gleichschritt flackern, und er versetzt die Lage
        // um bis zu 10 px. Wir nehmen dafür die Zelle — sie ist dieselbe feste
        // Zahl je Baum, die das Original aus dem Tafelindex zieht.
        int idx = e.Col * 31 + e.Row;
        int phase = (int)(DebugClock / FlammenSekunden + idx) % bilder.Count;
        if (phase < 0) phase += bilder.Count;
        DrawTexture(bilder[phase],
                    new Vector2(_ox + e.Col * TileW + idx % 10 - 5,
                                _oy + e.Row * TileH - ElevOf(e.Col, e.Row) * 15));
    }

    /// <summary>Wie lange ein Flammenbild steht. Das Original springt alle ZWEI
    /// Spielschritte weiter (<c>sar eax, 1</c> @0x42B438), und ein Spielschritt
    /// ist 20 ms (<c>SetTimer</c> @0x415BC5, siehe
    /// <see cref="OriginalTicksPerSecond"/>) — also 0,04 s. Das ist GERECHNET,
    /// nicht gewählt.</summary>
    private const float FlammenSekunden = 2f / OriginalTicksPerSecond;

    /// <summary>
    /// <b>EINE WALDZELLE ANZÜNDEN</b> — <c>zapal</c> @0x4CAC50.
    ///
    /// <para>Was das Original tut: Zustand auf <c>rand() % 150 + 2</c> setzen
    /// (@0x4CACAB), die Kachel gegen die verkohlte tauschen (@0x4CACE5) und die
    /// beiden Nachbarzeilen neu zeichnen lassen. Der Brandtakt @0x4CA330 zählt
    /// dann jeden vierten Spielschritt hoch und legt bei 255 die Bodenkachel
    /// hin. Bei uns steht die Kachel, solange die Zelle brennt; das AUSGEHEN
    /// ist noch nicht gebaut — siehe <c>CHANGELOG.de.md</c>.</para>
    ///
    /// <para>Gibt zurück, ob wirklich etwas angezündet wurde. ⚠ Es kann nur
    /// brennen, was der Backofen als Wald erkannt und mit einer verkohlten
    /// Fassung versehen hat.</para></summary>
    private bool Anzuenden(int col, int row)
    {
        bool ok = false;
        foreach (var e in _objDraw)
        {
            if (e.Row != row || e.Col != col || !e.HatKohle || e.BrandVon >= 0f
                || e.Abgebrannt) continue;
            e.BrandVon = (float)DebugClock;
            // Die BRENNDAUER, gerechnet wie im Original: Zustand
            // rand()%150 + 2 (@0x4CACAB), jeder vierte Spielschritt +1
            // (@0x4CA340/@0x4CA395), Schluss bei 255 (@0x4CA39A). Also
            // (255 − Zustand) · 4 Spielschritte, und ein Spielschritt ist
            // 20 ms (SetTimer @0x415BC5, siehe OriginalTicksPerSecond).
            // Macht 8,3 bis 20,2 Sekunden.
            int zustand = (int)(GD.Randi() % (uint)(Import.MapForest.BrandZustandBis
                                                    - Import.MapForest.BrandZustandVon + 1))
                          + Import.MapForest.BrandZustandVon;
            e.BrandDauer = (Import.MapForest.BrandEnde - zustand) * Import.MapForest.BrandTakt
                           / OriginalTicksPerSecond;
            // Und die eine von zwanzig, bei der der verkohlte Baum stehen
            // bleibt: @0x4CA3B2 `div 0x14`, Rest 0 fuehrt auf die Zeile
            // »dohorel forest - nesjizdnej« (@0x53969C, »abgebrannt —
            // unpassierbar«), sonst »- sjizdnej« (@0x5396C0, »passierbar«).
            e.Steht = GD.Randi() % 20 == 0 || !e.HatAsche;
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
    /// <para>⚠⚠ <b>WAS FEHLT, und es gehört gesagt:</b> das Original lässt ein
    /// Feuer WEITERGREIFEN. Derselbe Takt ruft für jede brennende Zelle
    /// <c>zapal_forestA</c> (@0x4CA7E0, Protokollzeile @0x539700) auf, das eine
    /// von acht Nachbarrichtungen auswürfelt und den Nachbarwald mit einer
    /// Wahrscheinlichkeit anzündet, die an WINDRICHTUNG (<c>0x4F8D68</c>) und
    /// WINDSTÄRKE (<c>0x4F8D6C</c>) hängt (@0x4CA873..0x4CA8DA). Das ist
    /// gelesen, aber nicht gebaut. Solange es fehlt, gehen die vier Feuer des
    /// Missionsstarts nach ihrer Zeit aus und entzünden nichts weiter.</para>
    /// </summary>
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
    private void ApplyMissionHits(System.Collections.Generic.IReadOnlyList<(int Col, int Row)> zellen)
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

            // Ein WALD auf der Zelle faengt Feuer — das ist der ganze Punkt.
            if (Anzuenden(c, r)) { brennt++; getroffen = true; }

            if (!getroffen) leer++;
            // Der Funkenschlag des Treffers selbst: ANIM-Folge 82, die Zasah
            // @0x40CB07 mit `push 0x52` in den Effektaufruf schiebt. Er ist
            // KURZ und erklaert das Brennen nicht — das tut die Kachel.
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
