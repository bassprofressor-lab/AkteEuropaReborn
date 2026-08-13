namespace AkteEuropaReborn.UI;

using System.Collections.Generic;
using Godot;

/// <summary>
/// Die Bank der kleinen 3D-Bilder, mit denen das Original ein Bauteil und eine
/// fertige Einheit zeigt — 86 Stück, aus <c>ANIM.CWA</c> Folgen 400…403
/// (Rahmen 1176…1261), exportiert von
/// <see cref="Import.InterfaceExporter.WritePortraits"/> nach
/// <c>UI/portraits/pNN.png</c>.
///
/// <para><b>Der Anlass.</b> Ein Spieler, der das Original vor sich hatte:
/// »für jede einheit … kleine bilder, die man unten links im HUD gesehen hat,
/// wenn die Einheit angewählt war, und im Modularen Bau System hatte man die
/// Grafiken der einzelteile gesehen und wie die einheit am ende aussah«. Genau
/// diese drei Stellen bedient diese Klasse: den Bedienblock
/// (<c>MapViewer</c>), das Fenster »Erstellung« (<see cref="DesignWindow"/>)
/// und das Basisfenster (<see cref="BaseWindow"/>).</para>
///
/// <para><b>Wie ein Bild gewählt wird — gelesen, nicht gewählt.</b></para>
/// <list type="bullet">
/// <item><b>Ein Bauteil</b> (Fall 5 des Zeichners <c>0x4508A0</c>): die
/// Bildnummer IST das Byte <c>+0x0D</c> seines 58-Byte-Satzes, also
/// <c>Maps/component_stats.json</c> Spalte 13 — siehe
/// <see cref="UnitStatBook.IconOf"/>. Die Rechnung des Originals ist
/// <c>Bild = word[0x7A468A] + icon</c>, und <c>0x7A468A</c> ist das Feld
/// <c>start_frame</c> von Folge 400 (<c>0x7A468A − 0x7A4048 = 1602 = 400·4+2</c>)
/// — bei uns also schlicht <c>pNN.png</c> mit <c>NN = icon</c>.
/// <c>icon == 100</c> lenkt das Original auf <c>56</c> um, die dunkle
/// »?«-Tafel (0x450C44: <c>cmp cx,0x64; ... mov cx,0x38</c>).</item>
/// <item><b>Eine fertige Einheit</b> (Fall 0) ist <b>ZWEI Bilder übereinander
/// am gleichen Ursprung</b>: <b>unten das Fahrwerk</b> (<c>sec47 +0x18</c>),
/// <b>darüber das Aufbauteil</b> (<c>sec47 +0x17</c>). Das Fahrwerksbild hat
/// dafür eine dunkle Rechteckmulde auf der Deckplatte, in die der Turm gesetzt
/// wird. Die Reihenfolge ist an der Annahmeprüfung <c>0x45E2BE</c> belegt (dort
/// stellt das Spiel die Größenklasse <c>+0x23</c> des Teils aus
/// <c>0x8B8C28</c> gegen die aus <c>0x992BB0</c> und verwirft, wenn die erste
/// kleiner ist — Kapazität stellt das Fahrwerk, also ist <c>0x8B8C28</c> das
/// Fahrwerk und der erste Blit).</item>
/// <item>⚠ <b>Die Verbesserung</b> (<c>sec47 +0x19</c>) hat ein Bild, wird vom
/// Original aber <b>nicht in die Vorschau gezeichnet</b>. Wir zeichnen sie
/// deshalb auch nicht in die zusammengesetzte Vorschau — nur einzeln in ihrem
/// eigenen Feld.</item>
/// <item>⚠ <b>Bei angewählter GRUPPE zeigt das Original kein Bild.</b> Der
/// Gruppen-Zweig des Bedienblocks (<c>0x47067A…0x470AB1</c>) hat keinen Aufruf
/// von <c>0x4508A0</c>, sondern sechs Textzeilen. Dass unser Bedienblock bei
/// mehreren gewählten Einheiten leer bleibt, ist Treue, kein Mangel.</item>
/// </list>
///
/// <para><b>Der Kasten.</b> Im Original ist er <c>0x456A50(x, y, 3, 3)</c> =
/// 3 Zellen von 20 px = <b>60×60</b>, gefüllt mit Palettenindex <b>0x2F</b>
/// (in 01.PAL 19,19,15). Über alle 86 Bilder ist das Maximum von Breite und
/// <c>yoff + Zeilen</c> ebenfalls genau 60 — die zwei Zahlen treffen sich, und
/// deshalb liegt jedes Bild auf derselben 60×60-Leinwand.</para>
///
/// <para>⚠ <b>UNSERE Setzung</b> ist allein die VERGRÖSSERUNG: das Original
/// zeichnet 60×60 Punkte 1:1 auf einen 1280×1024-Schirm, wir skalieren auf die
/// Feldgröße, die die Oberfläche hergibt, und immer mit Nearest — alles andere
/// macht aus einem 60×60-Bild Matsch.</para>
/// </summary>
public static class PortraitBank
{
    /// <summary>Die Kantenlänge der Bilder. Gemessen, siehe Klassenkopf.</summary>
    public const int Box = 60;

    /// <summary>Die Bildnummer, auf die das Original <c>icon == 100</c> umlenkt
    /// — die dunkle Tafel mit dem »?« (0x450C44).</summary>
    public const int Unknown = 56, UnknownFrom = 100;

    /// <summary>Die Muldenfüllung des Originals: Palettenindex 0x2F. Der Wert
    /// kommt aus <c>UI/portraits_index.json</c> und damit aus DER Palette, mit
    /// der auch die Bilder gemalt sind; dieser Vorgabewert ist der gemessene aus
    /// DATA/01.PAL und greift nur, solange der Index nicht gelesen ist.</summary>
    public static Color Fill { get; private set; } = Color.Color8(19, 19, 15, 255);

    /// <summary>Wieviele Bilder die Bank laut Index hat (86 bei der
    /// ausgelieferten ANIM.CWA).</summary>
    public static int Count { get; private set; }

    /// <summary>Warum nichts zu sehen ist, wenn nichts zu sehen ist — für
    /// Meldungen und Prüfstände.</summary>
    public static string Trouble { get; private set; } = "";

    private static bool _read;
    private static readonly Dictionary<int, Texture2D?> _cache = new();

    /// <summary>Wo jede der vier Folgen in der Bank ANFÄNGT — aus dem Feld
    /// <c>ranges</c> des Index, das <see cref="Import.InterfaceExporter"/> beim
    /// Auspacken aus den <c>start_frame</c>-Feldern von ANIM.CWA rechnet
    /// (400 → 0, 401 → 57, 402 → 67, 403 → 74). GELESEN, nicht gesetzt: wer
    /// eine andere ANIM.CWA auspackt, bekommt hier andere Zahlen und die
    /// Rechnungen unten stimmen weiter.</summary>
    private static readonly Dictionary<int, int> _first = new();

    /// <summary>Wieviele Bilder jede Folge HAT — das Feld <c>count</c> desselben
    /// <c>ranges</c>-Eintrags (400 → 57, 401 → 10, 402 → 7, 403 → 12). Auch das
    /// GELESEN, und es wird gebraucht: die Schiffskette des Originals kann eine
    /// Nummer erzeugen, die über das Ende ihrer eigenen Folge hinauszeigt, siehe
    /// <see cref="PictureOfShip"/>.</summary>
    private static readonly Dictionary<int, int> _count = new();

    /// <summary>Ist die Bank da? Nach dem ersten Aufruf ist
    /// <see cref="Trouble"/> gesetzt, falls nicht.</summary>
    public static bool Ready
    {
        get { Load(); return Count > 0; }
    }

    private static void Load()
    {
        if (_read) return;
        _read = true;
        string path = Core.Content.Path("UI/portraits_index.json");
        if (!FileAccess.FileExists(path))
        {
            Trouble = $"UI/portraits_index.json fehlt ({path}) — " +
                      "»--reexport-effects=<Quelle>« schreibt die Bank";
            return;
        }
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) { Trouble = "portraits_index.json nicht lesbar"; return; }
        // ⚠ `using`, und der Grund ist gemessen: `Json` ist ein RefCounted wie
        // `ConfigFile`, und ein nicht freigegebenes stirbt beim Herunterfahren im
        // Finalizer. Ein Prüflauf auf map_NET02 endete deshalb mit
        // Rückgabewert 139 (0xC0000005 in GC.RunFinalizers) und 97 Leckzeilen —
        // ausschliesslich `JSON` und `Image`, kein einziges `ConfigFile`. Die
        // Objekte hier waren der Rest desselben Fehlers, der am 13.08.2026 schon
        // in `Settings` und `CampaignManager` steckte.
        // `root` unten ist nach `AsGodotDictionary` eine eigene Referenz, das
        // Json-Objekt wird danach nicht mehr gebraucht.
        using var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok ||
            json.Data.VariantType != Variant.Type.Dictionary)
        {
            Trouble = "portraits_index.json ist kein JSON-Objekt";
            return;
        }
        var root = json.Data.AsGodotDictionary<string, Variant>();
        Count = root.TryGetValue("pictures", out var pv) ? pv.AsInt32() : 0;
        if (root.TryGetValue("fill_rgb", out var fv) &&
            fv.VariantType == Variant.Type.Array)
        {
            var a = fv.AsGodotArray();
            if (a.Count >= 3)
                Fill = Color.Color8((byte)a[0].AsInt32(), (byte)a[1].AsInt32(),
                                    (byte)a[2].AsInt32(), 255);
        }
        if (root.TryGetValue("ranges", out var rv) &&
            rv.VariantType == Variant.Type.Dictionary)
            foreach (var kv in rv.AsGodotDictionary<string, Variant>())
            {
                if (kv.Value.VariantType != Variant.Type.Dictionary) continue;
                var d = kv.Value.AsGodotDictionary<string, Variant>();
                if (d.TryGetValue("first_picture", out var fp))
                    _first[kv.Key.ToInt()] = fp.AsInt32();
                if (d.TryGetValue("count", out var cn))
                    _count[kv.Key.ToInt()] = cn.AsInt32();
            }
        if (Count <= 0) Trouble = "portraits_index.json nennt 0 Bilder";
    }

    /// <summary>Die Bildnummer, mit der eine ANIM-Folge in der Bank beginnt,
    /// oder −1, wenn der Index sie nicht nennt.</summary>
    public static int FirstPictureOf(int sequence)
    {
        Load();
        return _first.TryGetValue(sequence, out int v) ? v : -1;
    }

    /// <summary>Wieviele Bilder eine ANIM-Folge hat, oder −1, wenn der Index es
    /// nicht sagt.</summary>
    public static int PicturesIn(int sequence)
    {
        Load();
        return _count.TryGetValue(sequence, out int v) ? v : -1;
    }

    /// <summary>Ein Bild der Bank, oder null. ⚠ Die vom Import geschriebenen
    /// PNG haben keinen Godot-Importschritt, der Ressourcenlader sieht sie also
    /// nicht — derselbe Umweg wie bei der Schrift in
    /// <c>MapViewer.ApplyLegacyFont</c>.</summary>
    public static Texture2D? Picture(int n)
    {
        Load();
        if (n < 0 || (Count > 0 && n >= Count)) return null;
        if (_cache.TryGetValue(n, out var have)) return have;
        string path = Core.Content.Path($"UI/portraits/p{n:00}.png");
        Texture2D? tex = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
        if (tex == null && FileAccess.FileExists(path))
        {
            // ⚠ `using`: `ImageTexture.CreateFromImage` nimmt sich seine eigene
            // Kopie, das Image wird danach nicht mehr gebraucht. Ohne die
            // Freigabe leckte je geladenes Bild eines — auf map_NET02 zusammen
            // mit den JSON-Objekten 97 Stück, und der Lauf endete mit 139.
            using var img = Image.LoadFromFile(path);
            if (img != null) tex = ImageTexture.CreateFromImage(img);
        }
        _cache[n] = tex;
        return tex;
    }

    /// <summary>Das Bild eines BAUTEILS — Fall 5 des Originals, samt seiner
    /// einen Umlenkung <c>100 → 56</c>. 0 heisst »kein Bild« (so führt das
    /// Original die neun Verbesserungen 80…88), dann kommt null zurück.</summary>
    public static Texture2D? OfComponent(int component)
    {
        int icon = UnitStatBook.IconOf(component);
        if (icon == UnknownFrom) icon = Unknown;
        return icon <= 0 ? null : Picture(icon);
    }

    /// <summary>Die Bildnummer eines Bauteils, wie das Original sie nach der
    /// Umlenkung benutzt; 0 = kein Bild. Getrennt von
    /// <see cref="OfComponent"/>, weil ein Prüfstand die ZAHL braucht.</summary>
    public static int IconOfComponent(int component)
    {
        int icon = UnitStatBook.IconOf(component);
        return icon == UnknownFrom ? Unknown : icon;
    }

    // ---- die sieben Flugzeugbilder (Folge 402) ------------------------------
    //
    // Fall 3 des Zeichners 0x4508A0 — »Flugzeugplatz«, aufgerufen aus dem
    // Bedienblock bei 0x470C41 mit (3, id − 0x4E20, surf, 11, 61), also für ein
    // Objekt der Nummer 20000+. Die Kette, Schritt für Schritt:
    //
    //     typ = byte[0x6DDF78 + 68*platz]       ; 1..14
    //     i   = typ − 1
    //     a   = byte[0x450DC8 + i]              ; die 14er-Tafel
    //     if (a == 7) -> "Wrong airplane type" @0x4FE93C, KEIN Bild
    //     bild = byte[0x450DA8 + a]             ; die 7er-Sprungtafel
    //     Bild = word[0x7A4692] + bild          ; 0x7A4692 = Folge 402, start_frame
    //
    // ⚠ 0x6DDF78 ist NICHT eine neue Adresse, die man raten müsste: sec19 wird
    // vom Kartenlader nach **0x6DDF70** gelegt, Schrittweite **68** (siehe
    // Import.CwmExtra.Specials) — 0x6DDF78 − 0x6DDF70 = **8**, und +0x08 ist
    // dort das KIND-Byte, das wir schon lesen und schon in
    // Rendering.MapEntityLayer.Special.Kind tragen. Das Typbyte 1..14 IST also
    // unser `Kind`, an der Adresse abgezählt und nicht ausgelegt.

    /// <summary>Die ANIM-Folge der Flugzeugbilder — 7 Stück.</summary>
    public const int AirSequence = 402;

    /// <summary>Der Eintrag der 14er-Tafel, der »kein Bild« heisst: das Original
    /// springt damit in den Fehlerzweig <c>"Wrong airplane type"</c>.</summary>
    public const int AirNoPicture = 7;

    /// <summary>Die Bytetafel <c>@0x450DC8</c>, 14 Einträge, Index
    /// <c>Kind − 1</c>. Sieben ihrer vierzehn Einträge sind die 7 =
    /// »kein Bild« — die Kinds 4…9 (die das Spiel nicht besetzt) und die
    /// <b>12</b>.</summary>
    private static readonly int[] AirTable =
        { 0, 1, 2, 7, 7, 7, 7, 7, 7, 3, 4, 7, 5, 6 };

    /// <summary>Die Sprungtafel <c>@0x450DA8</c>, 7 Einträge: aus dem Eintrag
    /// der Tafel oben wird die laufende Nummer INNERHALB der Folge 402.</summary>
    private static readonly int[] AirJump = { 0, 1, 6, 2, 3, 4, 5 };

    /// <summary>
    /// Das Bild eines Flugzeugs, als Bildnummer der Bank; <b>0 heisst KEIN
    /// Bild</b> — und das ist bei sieben der vierzehn Kinds der Fall, im
    /// Original genau so.
    /// </summary>
    /// <param name="kind">Das Typbyte 1…14 aus sec19 <c>+0x08</c>.</param>
    public static int PictureOfAircraft(int kind)
    {
        if (kind < 1 || kind > AirTable.Length) return 0;
        int a = AirTable[kind - 1];
        if (a == AirNoPicture) return 0;
        int first = FirstPictureOf(AirSequence);
        if (first < 0) return 0;                     // Index ohne »ranges«
        return first + AirJump[a];
    }

    /// <summary>Warum ein Flugzeug kein Bild bekommt — für Prüfstand und
    /// Bedienblock EINE Formulierung, damit die Zahl und der Text nicht
    /// auseinanderlaufen können.</summary>
    public static string AirTrouble(int kind)
    {
        if (kind < 1 || kind > AirTable.Length)
            return $"Art {kind} liegt ausserhalb 1..14 (leerer sec19-Satz)";
        if (AirTable[kind - 1] == AirNoPicture)
            return $"Art {kind}: die Tafel @0x450DC8 sagt 7 — »Wrong airplane " +
                   "type«, das Original zeigt hier KEIN Bild";
        return FirstPictureOf(AirSequence) < 0
            ? "portraits_index.json nennt keine »ranges« — Folge 402 unbekannt"
            : "";
    }

    // ---- die zwölf Infanteriebilder (Folge 403) -----------------------------
    //
    // Fall 0 des Zeichners 0x4508A0 verzweigt bei WAFFE >= 0xB9 in den
    // Infanteriezweig @0x45099B. Der ist kurz und ganz gelesen:
    //
    //     0045099B  sub  esi, 0x32                ; esi = Entwurfsnummer
    //     0045099E  cmp  esi, 0x90                ; 0x90 = 144
    //     004509A4  ja   0x4509b5                 ; darüber -> Fehlerzweig
    //     004509A6  xor  eax, eax
    //     004509A8  mov  al, byte [esi + 0x450ccc] ; die 145er-Bytetafel
    //     004509AE  jmp  dword [eax*4 + 0x450c98]  ; die SCHALTERTAFEL
    //     ...
    //     00450A1C  movsx ecx, word [0x7a4696]     ; Folge 403, start_frame
    //     00450A24  add  eax, ecx                  ; Bild = first + Fall
    //
    // ⚠ **0x450C98 ist KEINE Bytetafel, sondern die Sprungtafel eines
    // Schalters** — 13 CODEADRESSEN à 4 Byte (0x4509CC, 0x4509D1, … 0x450A0D
    // und als 13. die 0x4509B5). Jeder der zwölf Zweige besteht aus genau einem
    // `mov ax, <n>` und einem Sprung auf den gemeinsamen Zeichenschluss
    // 0x450A11; die zwölf n sind, in dieser Reihenfolge,
    // **0,1,2,6,4,3,5,7,8,9,11,10**.
    //
    // **Damit geht die Eins auf, um die es in diesem Auftrag ging.** Der
    // Prüfstand meldete 13 Entwürfe ohne Bild gegen 12 Bilder — und die 13
    // Sprungeinträge sind 12 Fälle PLUS der Fehlerzweig 0x4509B5, der
    // `"Wrong index of infantry"` (@0x4FE96C) ausgibt. Der Eintrag 12 der
    // Bytetafel IST dieser Fehlerfall, und die Bytetafel trägt ihn 133 mal:
    //
    //     idx   0…  6 -> 0,1,2,3,4,5,6      (Entwurf  50…56)
    //     idx   7…139 -> 12  (133 Stück)    = "Wrong index of infantry"
    //     idx 140…144 -> 7,8,9,10,11        (Entwurf 190…194)
    //
    // Zwölf Werte 0…11 für zwölf Entwürfe, kein Wert doppelt, keiner fehlt.
    // Und die Entwurfsliste stimmt dagegen: `unit_designs.json` trägt pro
    // Spielerblock genau zwölf Entwürfe mit Fahrwerk 148/149, nämlich **50…56**
    // (Chaingunner, Laser Trooper, Radar Scout, Pioneer, Lander, Target,
    // Android) und **190…194** (Cpt.Cossarro, Col.Hullman, Com.Wiffer,
    // Scientist, Civilian). `194 − 0x32 = 144` ist der LETZTE Eintrag der Tafel,
    // und 144 ist die Zahl, gegen die 0x45099E prüft — die Tafel ist genau so
    // lang, wie die Entwurfsnummern reichen.
    //
    // **Gegenprobe am Bild** (die zwölf Tafeln p74…p85 angesehen). Es sind
    // gerade die VERTAUSCHUNGEN der Permutation, die sie belegen:
    //   Pioneer  53 -> Fall 3 -> Bild 6 = p80: Schweißflamme und Schürze
    //   Target   55 -> Fall 5 -> Bild 3 = p77: Markierstab mit hellem Funken
    //   Android  56 -> Fall 6 -> Bild 5 = p79: Kapuze, kein Gesicht
    //   Scientist 193 -> Fall 10 -> Bild 11 = p85: weisser KITTEL und Brille
    //   Civilian  194 -> Fall 11 -> Bild 10 = p84: Sonnenbrille, Hemd, kein Kittel
    // Ohne die Sprungtafel (also 1:1 durchgezählt) bekäme der Pionier den
    // Markierstab und der Zivilist den Laborkittel. Weiter passen
    // Chaingunner->p74 (Maschinengewehr in den Händen), Laser Trooper->p75
    // (Strahlwaffe), Radar Scout->p76 (Tornister voller Antennen und Kabel) und
    // Com.Wiffer->p83 (Gurt-MG, seine Waffenzeile 199 heisst »Schw.M-Gewehr«).

    /// <summary>Die ANIM-Folge der Infanteriebilder — 12 Stück.</summary>
    public const int InfSequence = 403;

    /// <summary>Die 0x32, die <c>0x45099B</c> von der Entwurfsnummer abzieht:
    /// Entwurf 50 ist der erste Infanterist.</summary>
    public const int InfFirstDesign = 0x32;

    /// <summary>Der Eintrag der Bytetafel <c>@0x450CCC</c>, der »kein Bild«
    /// heisst — der 13. Sprungeintrag, der Fehlerzweig
    /// <c>"Wrong index of infantry"</c>.</summary>
    public const int InfNoPicture = 12;

    /// <summary>Die Bytetafel <c>@0x450CCC</c>, Index <c>Entwurf − 0x32</c>:
    /// nur diese zwölf Indizes tragen etwas anderes als die
    /// <see cref="InfNoPicture"/>, alle 133 übrigen den Fehlerfall. So
    /// geschrieben statt als 145er-Feld, weil die zwölf Einträge die Aussage
    /// sind und die 133 Zwölfen nur Füllung.</summary>
    private static readonly Dictionary<int, int> InfTable = new()
    {
        { 0, 0 }, { 1, 1 }, { 2, 2 }, { 3, 3 }, { 4, 4 }, { 5, 5 }, { 6, 6 },
        { 140, 7 }, { 141, 8 }, { 142, 9 }, { 143, 10 }, { 144, 11 },
    };

    /// <summary>Der letzte gültige Index der Tafel oben — die 0x90 aus
    /// <c>cmp esi, 0x90</c> @0x45099E.</summary>
    public const int InfLastIndex = 0x90;

    /// <summary>Die zwölf <c>mov ax, n</c> der Schaltertafel <c>@0x450C98</c>,
    /// in der Reihenfolge ihrer Fälle: aus dem Eintrag der Bytetafel wird die
    /// laufende Nummer INNERHALB der Folge 403.</summary>
    private static readonly int[] InfJump = { 0, 1, 2, 6, 4, 3, 5, 7, 8, 9, 11, 10 };

    /// <summary>
    /// Das Bild eines Fußsoldaten, als Bildnummer der Bank; <b>0 heisst KEIN
    /// Bild</b>.
    ///
    /// <para>⚠ Der Fehlerzweig des Originals gibt <c>"Wrong index of infantry"</c>
    /// aus und zeichnet danach trotzdem weiter — mit <c>ax = 0</c>, also mit dem
    /// ERSTEN Bild der Folge (<c>xor ax,ax; jmp 0x450a11</c> @0x4509C4). Wir
    /// zeichnen dort NICHTS. Der Unterschied ist nicht zu sehen: in den
    /// Infanteriezweig kommt nur, wessen Waffenzeile ≥ 0xB9 ist, und das sind
    /// über alle 586 Sätze von <c>unit_designs.json</c> genau die zwölf
    /// Entwürfe, die die Tafel kennt. Der Zweig ist unerreichbar; ein falsches
    /// erstes Bild wäre schlimmer als keines.</para>
    /// </summary>
    /// <param name="design">Die Entwurfsnummer 0…199 innerhalb des
    /// Spielerblocks von sec47 (Schrittweite 200).</param>
    public static int PictureOfInfantry(int design)
    {
        int i = design - InfFirstDesign;
        if (i < 0 || i > InfLastIndex) return 0;             // ja 0x4509b5
        if (!InfTable.TryGetValue(i, out int a) || a == InfNoPicture) return 0;
        int first = FirstPictureOf(InfSequence);
        if (first < 0) return 0;                             // Index ohne »ranges«
        return first + InfJump[a];
    }

    /// <summary>Warum ein Fußsoldat kein Bild bekommt — wie
    /// <see cref="AirTrouble"/> EINE Formulierung für Prüfstand und
    /// Bedienblock.</summary>
    public static string InfTrouble(int design)
    {
        int i = design - InfFirstDesign;
        if (i < 0 || i > InfLastIndex)
            return $"Entwurf {design} liegt ausserhalb 0x32..0xC2 — " +
                   "0x45099E verwirft ihn (»Wrong index of infantry«)";
        if (!InfTable.TryGetValue(i, out int a) || a == InfNoPicture)
            return $"Entwurf {design}: die Tafel @0x450CCC sagt {InfNoPicture} — " +
                   "»Wrong index of infantry«, kein Infanterieentwurf";
        return FirstPictureOf(InfSequence) < 0
            ? "portraits_index.json nennt keine »ranges« — Folge 403 unbekannt"
            : "";
    }

    /// <summary>Alle Entwurfsnummern, die die Tafel <c>@0x450CCC</c> als
    /// Infanterie führt — für den Prüfstand, damit er die ZWÖLF nachzählen kann,
    /// ohne sie selbst zu wissen.</summary>
    public static IEnumerable<int> InfantryDesigns()
    {
        foreach (var kv in InfTable)
            if (kv.Value != InfNoPicture) yield return kv.Key + InfFirstDesign;
    }

    // ---- die zehn Schiffsbilder (Folge 401) ---------------------------------
    //
    // Fall 1 des Zeichners 0x4508A0 — »Schiffsplatz«, aufgerufen aus dem
    // Bedienblock bei 0x470188/0x4701A9 mit (1, entwurf, surf, 11, 61), gezogen
    // wenn das Klassenbyte +0x0a des Satzes 4 oder 5 ist. Die Kette, ganz
    // gelesen (GAME.EXE 1.421.824 B, C:\Program Files (x86)\Akte Europa):
    //
    //     0045 0A42  mov  dl, byte [0x4fa284]        ; der eigene Spieler
    //     0045 0A48  movsx eax, word [esp+0x18]      ; das 2. Argument = Entwurf
    //     0045 0A4D  lea  ebx,[edx+edx*4] / lea edx,[eax+ebx*2]   ; entwurf+10*sp
    //     0045 0A53  lea  ebx,[edx+edx*4] / lea edx,[edx+ebx*4]   ; *21
    //     0045 0A59  mov  cl, byte [edx*2 + 0x52edb7]  ; = SHIP_PROD[n] +0x17
    //     0045 0A60  lea  eax,[edx*2]                  ; = 42*n, der Satzanfang
    //     0045 0A67  sub  ecx, 0x96                    ; 0x96 = 150
    //     0045 0A6D  cmp  ecx, 9
    //     0045 0A70  ja   0x450a79                     ; -> "Wrong ship chassis"
    //     0045 0A72  jmp  dword [ecx*4 + 0x450d60]     ; die SCHALTERTAFEL
    //     ...
    //     0045 0AE5  movsx ecx, word [0x7a468e]        ; Folge 401, start_frame
    //     0045 0AE8  add  eax, ecx                     ; Bild = first + Fall
    //
    // ⚠ **0x450D60 ist KEINE Bytetafel, sondern die Sprungtafel eines
    // Schalters** — genau wie 0x450C98 bei der Infanterie. Zehn CODEADRESSEN à
    // 4 Byte: 0x450A92, 0x450A97, 0x450AAC, 0x450AB2, 0x450AB8, 0x450ABE,
    // 0x450AC4, 0x450ACA, 0x450AD0, 0x450AD6. Neun davon sind ein einzelnes
    // `mov ax, <n>` samt Sprung auf den gemeinsamen Zeichenschluss 0x450ADA; die
    // Zahlen sind, in dieser Reihenfolge, **0, ·, 2, 3, 4, 5, 6, 8, 9, 10**.
    //
    // ⚠ **Der zweite Fall (Rumpf 151) ist der Sonderfall, und er IST die
    // Permutation.** Er hat kein `mov ax,n`, sondern rechnet weiter:
    //
    //     0045 0A97  mov  al, byte [eax + 0x52edb8]    ; = SHIP_PROD[n] +0x18
    //     0045 0A9D  sub  al, 6
    //     0045 0A9F  cmp  al, 1
    //     0045 0AA1  sbb  eax, eax          ; −1 wenn al==0 (also Byte==6), sonst 0
    //     0045 0AA3  and  eax, 0xfffffffa   ; −1 -> −6 ; 0 -> 0
    //     0045 0AA6  add  ax, 7             ; also: Byte==6 -> 1, sonst -> 7
    //
    // Das Byte +0x18 des SHIP_PROD-Satzes ist die **Variante**
    // (Assets/Legacy/Maps/ships.json, Feld »variant«).
    //
    // **Warum es diesen Sonderfall gibt — und warum »chassis − 150« FALSCH
    // wäre.** Die zehn Entwürfe benutzen die Rümpfe
    // **150,151,152,153,154,155,156,151,157,158**: der Rumpf **151 kommt ZWEIMAL
    // vor**, beim L.Kreuzer/Rocket Ship (Variante 6) und bei der
    // Flak-Barkasse/AA Ship (Variante 0). Deshalb reichen die zehn Rümpfe nicht
    // als Bildschlüssel, deshalb steht in Fall 1 ein zweiter Blick auf die
    // Variante, und deshalb rutschen 157 und 158 um eins nach hinten (auf 8 und
    // 9), damit die 7 für die Flak-Barkasse frei bleibt. Naiv gerechnet bekäme
    // die Flak-Barkasse das Bild des L.Kreuzers, das Schlachtschiff das der
    // Flak-Barkasse, der Kreuzer das des Schlachtschiffs — und das grösste Bild
    // der Folge bliebe unbenutzt.
    //
    // **Gegenprobe am Bild** (p57…p66 angesehen, aekernel-tools/icon_out/bank):
    //   p57 Patrol-Boot     kleines schnelles Boot mit SITZENDEM Schützen am
    //                       Ständer — Waffe »2x Maschinengewehr«
    //   p58 L.Kreuzer       zwei lange, steil aufgerichtete RAKETENROHRE —
    //                       Waffe »L.Raketenwerfer«
    //   p59 Küstenwache     schlanker KATAMARAN mit einem Geschütz vorn — der
    //                       Entwurf heisst in den .DM-Fassungen »Katamaran«
    //   p60 Frachter        stumpfer Kasten voller grosser CONTAINER, unbewaffnet
    //   p61 U-Boot          schlanke Zigarre, flach im Wasser, KEIN Aufbau
    //   p62 Treibstofftender zwei grosse glatte ZYLINDERTANKS an Deck
    //   p63 Munitiontender  ein Raster kleiner WÜRFELKISTEN an Deck
    //   p64 Flak-Barkasse   **derselbe Rumpf wie p58** — gleiche Reling, gleiches
    //                       Deck, gleiche Farbe — aber ein kurzer, dicker
    //                       Doppellauf steil nach oben: »Flak-Geschütz«
    //   p65 Schlachtshiff   schwerer Kriegsschiffsrumpf mit vielarmigem
    //                       Werfergestell — »Schw.Raketenwerfer«
    //   p66 Kreuzer         der längste, vollgepackteste Rumpf mit einem grossen
    //                       Raketenkörper — »Mittelstreckenrakete«
    //
    // ⭐ **p58 und p64 sind DERSELBE RUMPF mit zwei verschiedenen Aufbauten.**
    // Die Bildbank trägt den doppelten Rumpf 151 also selbst, sichtbar, und
    // belegt damit die Permutation von aussen: ohne den Blick auf die Variante
    // hätte die Flak-Barkasse Raketen statt Flak, und das Schlachtschiff wäre
    // derselbe kleine Kahn wie der L.Kreuzer.
    //
    // ⚠ **Rumpf 159 (Fall 9) führt ins Leere.** Sein `mov ax,0xa` zeigt auf das
    // 11. Bild einer Folge, die nur 10 hat — das wäre p67 und damit das erste
    // FLUGZEUGbild. Kein Entwurf benutzt 159, der Fall ist unerreichbar; wir
    // fangen ihn trotzdem an der aus dem Index gelesenen Länge der Folge ab.

    /// <summary>Die ANIM-Folge der Schiffsbilder — 10 Stück.</summary>
    public const int ShipSequence = 401;

    /// <summary>Die 0x96, die <c>0x450A67</c> vom Rumpfbyte abzieht: Rumpf 150
    /// ist der erste Schiffsrumpf.</summary>
    public const int ShipFirstChassis = 0x96;

    /// <summary>Der letzte Fall, den <c>cmp ecx,9</c> @0x450A6D durchlässt.
    /// </summary>
    public const int ShipLastCase = 9;

    /// <summary>Der Fall mit dem DOPPELTEN Rumpf — 151, der Fall, der nicht
    /// einfach eine Zahl setzt, sondern noch auf die Variante sieht
    /// (0x450A97).</summary>
    public const int ShipDoubleCase = 1;

    /// <summary>Die Variante, die <c>0x450A9D</c> heraussucht (<c>sub al,6</c>):
    /// sie führt auf <see cref="ShipJump"/>[1], jede andere auf
    /// <see cref="ShipDoubleOther"/>.</summary>
    public const int ShipDoubleVariant = 6;

    /// <summary>Der andere Ausgang von Fall 1 — die 7 aus <c>add ax,7</c>
    /// @0x450AA6, das Bild der Flak-Barkasse.</summary>
    public const int ShipDoubleOther = 7;

    /// <summary>Die zehn <c>mov ax, n</c> der Schaltertafel <c>@0x450D60</c>, in
    /// der Reihenfolge ihrer Fälle (= Rumpf − 150). ⚠ Der Eintrag 1 gilt nur für
    /// <see cref="ShipDoubleVariant"/>; Fall 1 hat zwei Ausgänge, siehe
    /// <see cref="ShipDoubleOther"/>.</summary>
    private static readonly int[] ShipJump = { 0, 1, 2, 3, 4, 5, 6, 8, 9, 10 };

    /// <summary>
    /// Das Bild eines Schiffes, als Bildnummer der Bank; <b>0 heisst KEIN
    /// Bild</b>.
    ///
    /// <para>⚠ Der Fehlerzweig des Originals (@0x450A79) gibt
    /// <c>"Wrong ship chassis"</c> (@0x4FE954) aus und zeichnet danach TROTZDEM —
    /// mit <c>ax = word[esp+0x16]</c>, also mit dem Stück Stapel, das noch dort
    /// lag. Wir zeichnen dort nichts.</para>
    /// </summary>
    /// <param name="chassis">Der Rumpf 150…159 — das Byte <c>+0x17</c> des
    /// SHIP_PROD-Satzes, das der Erzeuger @0x4B2B20 in <c>+0x0f</c> des
    /// Einheitensatzes schreibt (bei uns <c>Entity.UnitType</c>).</param>
    /// <param name="variant">Die Variante — das Byte <c>+0x18</c> desselben
    /// Satzes, das derselbe Erzeuger nach <c>+0x0d</c> schreibt. Sie wird nur im
    /// Fall des doppelten Rumpfes 151 überhaupt angesehen.</param>
    public static int PictureOfShip(int chassis, int variant)
    {
        int i = chassis - ShipFirstChassis;
        if (i < 0 || i > ShipLastCase) return 0;         // ja 0x450A79
        int a = i == ShipDoubleCase
            ? (variant == ShipDoubleVariant ? ShipJump[ShipDoubleCase] : ShipDoubleOther)
            : ShipJump[i];
        int first = FirstPictureOf(ShipSequence);
        if (first < 0) return 0;                         // Index ohne »ranges«
        int have = PicturesIn(ShipSequence);
        if (have >= 0 && a >= have) return 0;            // Rumpf 159, siehe oben
        return first + a;
    }

    /// <summary>Warum ein Schiff kein Bild bekommt — wie
    /// <see cref="AirTrouble"/> und <see cref="InfTrouble"/> EINE Formulierung
    /// für Prüfstand und Bedienblock.</summary>
    public static string ShipTrouble(int chassis, int variant)
    {
        int i = chassis - ShipFirstChassis;
        if (i < 0 || i > ShipLastCase)
            return $"Rumpf {chassis} liegt ausserhalb 150..159 — 0x450A6D " +
                   "verwirft ihn (»Wrong ship chassis«)";
        if (FirstPictureOf(ShipSequence) < 0)
            return "portraits_index.json nennt keine »ranges« — Folge 401 unbekannt";
        int a = i == ShipDoubleCase
            ? (variant == ShipDoubleVariant ? ShipJump[ShipDoubleCase] : ShipDoubleOther)
            : ShipJump[i];
        int have = PicturesIn(ShipSequence);
        return have >= 0 && a >= have
            ? $"Rumpf {chassis} zeigt auf Bild {a} der Folge 401, die nur {have} " +
              "hat — der unerreichbare Fall 9 des Originals"
            : "";
    }

    /// <summary>Alle Rümpfe, die die Schaltertafel <c>@0x450D60</c> führt — für
    /// den Prüfstand, damit er die ZEHN Fälle abschreiten kann, ohne sie selbst
    /// zu wissen.</summary>
    public static IEnumerable<int> ShipChassis()
    {
        for (int i = 0; i <= ShipLastCase; i++) yield return ShipFirstChassis + i;
    }

    // ---- GEBÄUDE: es gibt kein Bild, und das ist der Befund ----------------
    //
    // ⚠ Diese Stelle behauptet eine ABWESENHEIT, und darum steht hier, WIE sie
    // gemessen ist — mit rohen Abtasten über GAME.EXE (1.421.824 B,
    // »C:\Program Files (x86)\Akte Europa«), nicht mit einem leeren xref.
    //
    // 1. DER ZEICHNER HAT SECHS FÄLLE, UND KEINER IST EIN GEBÄUDE.
    //    0x4508A0 springt über die Tafel @0x450C80; davor steht
    //    »cmp eax, 5 ; ja 0x4509C4«, die Tafel ist also genau sechs Dwords lang
    //    (0x4508D3, 0x450A3E, 0x450B07, 0x450B85, 0x450C1C, 0x450C44 —
    //    nachgelesen; ab 0x450C98 fängt schon die 13er-Tafel der Infanterie an).
    //    Die sechs sind in DrawerCases beim Namen genannt.
    //
    // 2. DER ZEICHNER BERÜHRT DIE GEBÄUDETAFEL NIE.
    //    Roher Dword-Abtast über 0x4508A0…0x450D00: die Tafeln, in die er
    //    greift, sind 0x51CE37/38 (Entwürfe sec47), 0x52EDB7/B8 (Schiffsbau),
    //    0x6DDF78 (Flugzeugplätze sec19), 0x7A468A/8E/92/96 (die Folgen
    //    400…403) und 0x815580 (die Rahmenzeiger). Treffer im Bereich der
    //    Gebäudesätze 0xC06000…0xC08000: NULL. Die Gebäudetafel ist
    //    0xC06914, 76 Byte je Satz, 255 Sätze (GAMESTATE_RE.md §3.86) — sie
    //    kommt im Zeichner nicht vor.
    //
    // 3. EIN GEBÄUDE KANN GAR NICHT »DAS ANGEWÄHLTE OBJEKT« SEIN.
    //    Der Bedienblock liest das angewählte Objekt aus word[0x4FA0C8]. Roher
    //    Abtast nach allen SCHREIBSTELLEN dieses Wortes über den ganzen .text
    //    (18 Stück): 0xFFFF (nichts), 0x2710 = 10000 (Gruppe) und zwei
    //    Registerschreibungen in der Anwählroutine 0x4331E0. Die teilt bei
    //    0x43322C/0x433309 in genau zwei Objektarten: Griff < 0x1F40 =
    //    Landeinheit (Satz 78 Byte @0x6E26C8), Griff >= 0x4E20 = Flugzeugplatz
    //    (Satz 68 Byte @0x591EFF ff., Griff − 0x4E20). Alles dazwischen
    //    schreibt den Griff NICHT (0x43332F holt die Register aus einem
    //    Stapelplatz und 0x43333D speichert den ALTEN Wert zurück) — der
    //    Zahlenraum zwischen 8000 und 20000 trägt nur die Sonderwerte.
    //    Zwei weitere Routinen zählen denselben Raum unabhängig ab:
    //    das Abwählen @0x436030 (<0x1F40 · 0x4E20…0x4F4B · 0x2710 · sonst
    //    nichts) und der Bedienblock selbst @0x46FE10
    //    (0x470107 »cmp cx,0x1F40«, 0x4705F4 »cmp cx,0x2710«,
    //    0x470C10/0x470C1B »0x4E20 <= cx < 0x4F4C«, sonst 0x470E76).
    //    Gebäude werden über ihren PLATZ angesprochen (76·Platz + 0xC06914),
    //    nicht über diesen Griff — sie kommen in ihm nicht vor.
    //
    // 4. UND WAS DER BEDIENBLOCK STATTDESSEN ZEIGT, ist gelesen: der Zweig
    //    0x470E76 druckt »Kontostand « (0x501CF0) an (11,45) und
    //    »Sprit gesamt « (0x501CE0) an (11,61) — die Übersicht. Das Bildfeld
    //    (11,61) trägt dort also TEXT, kein Bild.
    //
    // 5. GEGENPROBE ÜBER ALLE FENSTER: roher E8-Abtast über den ganzen .text
    //    nach dem einzigen Stummel des Zeichners (0x4023E2 → 0x4508A0) findet
    //    14 Aufrufstellen. Über die Zeichnertafel @0x487888 (48 Einträge,
    //    Index = Fensterart − 1) liegen sie in fünf Fensterarten: 5 Flughafen
    //    (0x465BBC kind 3, 0x466999 kind 2), 6 Basis (0x46840A/0x4691D5 kind 0,
    //    0x46A50B kind 5), 7 Erstellung (0x46D5DF/0x46D63A kind 5),
    //    9 Bedienblock (0x4701A9 Einheit, 0x470C41 kind 3 Flugzeugplatz),
    //    11 Hafen (0x471C41 kind 1) sowie 23/33/38 (0x47966B, 0x47E2F2,
    //    0x482A8D, 0x4836F7, alle kind 0 = Entwurf). Kein einziger Aufruf
    //    bekommt ein Gebäude gereicht.
    //
    // 6. GEGENPROBE VON DER ANDEREN SEITE: welche Fenster lesen die
    //    Gebäudetafel überhaupt? Roher Dword-Abtast über den ganzen .text nach
    //    0xC06900…0xC069FF, jede Fundstelle über die Zeichnertafel @0x487888
    //    ihrer Fensterart zugeordnet: 11 der 48 Arten greifen hinein — 2, 5
    //    (Flughafen), 6 (Basis), 8, 11 (Hafen), 18, 20, 21, 23, 27 und 46 (371
    //    Stellen, der Verwalter). Die Art 9, der BEDIENBLOCK, ist NICHT darunter:
    //    in seinen 5048 Byte kommt keine einzige Adresse der Gebäudetafel vor.
    //    Der Bedienblock kennt Gebäude also überhaupt nicht — er kann ihnen
    //    darum auch kein Bild geben. Die Fenster, die Gebäudedaten zeigen UND
    //    ein Bild malen (5, 6, 11, 23), malen darin den ENTWURF bzw. den
    //    Schiffsrumpf bzw. das Flugzeug, das im Gebäude steht — nie das Gebäude.
    //
    // ⚠ Ein falsches Bild wäre schlimmer als keines: der Spieler sieht es
    // unmittelbar. Also bleibt das Feld für ein Gebäude leer — wie im Original.

    /// <summary>Die SECHS Fälle des Bildzeichners <c>0x4508A0</c>, in der
    /// Reihenfolge der Sprungtafel <c>@0x450C80</c> — für den Prüfstand, damit er
    /// sie abschreiten kann, ohne sie selbst zu wissen. Die Namen sind nicht
    /// gesetzt, sondern aus den Fehlermeldungen des Spiels selbst genommen
    /// (»Wrong index of infantry« @0x4FE96C, »Wrong ship chassis« @0x4FE954,
    /// »Wrong airplane type« @0x4FE93C).</summary>
    public static readonly string[] DrawerCases =
    {
        "0 Entwurf (Landeinheit/Fusssoldat, sec47)",
        "1 Schiffsrumpf (Folge 401)",
        "2 Flugzeugart (Folge 402)",
        "3 Flugzeugplatz (Folge 402)",
        "4 festes Bild " + FixedPicture + " der Folge 400",
        "5 rohe Bildnummer (Bauteil, " + UnknownFrom + " -> »?«)",
    };

    /// <summary>Das feste Bild, das Fall 4 malt (<c>0x450C1C</c>:
    /// <c>seq400.start + 55</c>) — das grosse Fahrzeug. Kein Gebäude.</summary>
    public const int FixedPicture = 55;

    /// <summary>Warum ein GEBÄUDE kein Bild bekommt — EINE Formulierung für
    /// Bedienblock und Prüfstand, wie <see cref="ShipTrouble"/> für die Schiffe.
    /// Der Grund ist keine Lücke unseres Codes, sondern der Bau des Originals;
    /// die Messung dazu steht im Kommentarblock über
    /// <see cref="DrawerCases"/>.</summary>
    public static string BuildingTrouble()
        => "Gebaeude — das Original hat kein Bild dafuer: der Zeichner 0x4508A0 " +
           "hat sechs Faelle (Tafel @0x450C80) und keiner nimmt ein Gebaeude, " +
           "und der Griff word[0x4FA0C8] kennt nur Landeinheit (<0x1F40), " +
           "Flugzeugplatz (0x4E20..0x4F4B), Gruppe (0x2710) und nichts (0xFFFF) " +
           "— Gebaeude stehen in 76·Platz + 0xC06914 und kommen darin nicht vor";

    /// <summary>
    /// Die fertige Einheit in einen Kasten malen: Mulde füllen, Fahrwerk, dann
    /// Aufbauteil darüber — gleicher Ursprung, wie <c>0x46D5DF</c>/<c>0x46D63A</c>
    /// beide auf (520,40) blitten.
    /// </summary>
    /// <param name="ci">Das Feld, in das gemalt wird. ⚠ Es muss
    /// <c>TextureFilter = Nearest</c> haben, sonst zerfliesst das 60×60-Bild.
    /// </param>
    /// <param name="box">Das Feld in dessen eigenen Koordinaten.</param>
    /// <param name="chassis">Fahrwerks-Bauteil (sec47 +0x18) — unten.</param>
    /// <param name="weapon">Aufbauteil (sec47 +0x17) — oben. 0 = keines.</param>
    /// <returns>wieviele Bilder wirklich gemalt wurden (0, 1 oder 2)</returns>
    public static int DrawUnit(CanvasItem ci, Rect2 box, int chassis, int weapon)
        => DrawPictures(ci, box, IconOfComponent(chassis), IconOfComponent(weapon));

    /// <summary>
    /// Dasselbe, aber mit BILDNUMMERN statt Bauteilzeilen — der Weg für eine
    /// Einheit auf der Karte.
    ///
    /// <para>⚠ Warum es diesen zweiten Weg gibt: der Einheitensatz trägt die
    /// Bildnummern schon selbst. <c>+0x0b</c> (das <c>spodek</c>) ist die
    /// Fahrwerksnummer 1…18 und <c>+0x0c</c> (der Aufsatz) die Nummer des
    /// Turms 21…39 bzw. eines Geräts 40…54 — gemessen über 968 Landeinheiten
    /// auf sieben Karten, siehe <c>MapEntityLayer.PanelPortrait</c>. Den Umweg
    /// über <c>component_stats +0x0D</c> braucht nur, wer von einem BAUTEIL
    /// kommt (die zwei Fenster).</para>
    /// </summary>
    public static int DrawPictures(CanvasItem ci, Rect2 box, int chassisPic, int turretPic)
    {
        ci.DrawRect(box, Fill);
        int n = 0;
        if (Blit(ci, box, Picture(chassisPic <= 0 ? -1 : chassisPic))) n++;
        if (Blit(ci, box, Picture(turretPic <= 0 ? -1 : turretPic))) n++;
        return n;
    }

    /// <summary>Ein einzelnes Bauteilbild in einen Kasten malen — das, was das
    /// Erstellungsfenster für jede der drei Listen zeigt.</summary>
    public static bool DrawComponent(CanvasItem ci, Rect2 box, int component)
    {
        ci.DrawRect(box, Fill);
        return Blit(ci, box, OfComponent(component));
    }

    /// <summary>Ein Bild formatfüllend, mittig, ohne das Seitenverhältnis zu
    /// verbiegen — die Bilder sind quadratisch (60×60), der Kasten muss es
    /// nicht sein.</summary>
    private static bool Blit(CanvasItem ci, Rect2 box, Texture2D? tex)
    {
        if (tex == null) return false;
        float s = Mathf.Min(box.Size.X / Box, box.Size.Y / Box);
        var size = new Vector2(Box * s, Box * s);
        var at = box.Position + (box.Size - size) * 0.5f;
        ci.DrawTextureRect(tex, new Rect2(at, size), false);
        return true;
    }

    /// <summary>Für einen Prüflauf, der nicht auf den Bildschirm sehen kann.
    /// </summary>
    public static string WatchLine()
    {
        Load();
        return Count > 0
            ? $"portraits: {Count} Bilder, Kasten {Box}x{Box}, " +
              $"Mulde #{Fill.ToHtml(false)}, Quelle {Core.Content.Path("UI/portraits/p00.png")}"
            : $"portraits: keine Bank — {Trouble}";
    }
}
