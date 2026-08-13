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
        var json = new Json();
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
        if (Count <= 0) Trouble = "portraits_index.json nennt 0 Bilder";
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
            var img = Image.LoadFromFile(path);
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
