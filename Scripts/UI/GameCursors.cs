namespace AkteEuropaReborn.UI;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>DIE MAUSZEIGER DES ORIGINALS</b> — statt der Systemzeiger von Godot.
///
/// <para>Gemeldet hatte der Spieler den Angriffszeiger: »das erscheint wenn man
/// über eine gegnerische Einheit kommt«. Er ist <b>kein Weltmarker</b>, sondern
/// der Mauszeiger selbst — ein Fadenkreuz, um das vier rote Dreiecke nach außen
/// wandern. Woher die Bilder kommen und wie sie im ROBO.CWR-Anhang liegen,
/// steht bei <see cref="Import.CwrFile.Cursors"/>.</para>
///
/// <para><b>WELCHER ZEIGER WANN — gelesen, nicht gewählt.</b> Das Original
/// wählt ihn in <c>0x4A9AB0</c> aus einem Modus <c>dword[0x502AD4]</c> über die
/// Sprungtafel <c>0x4A9BEC</c> (Modi 0..25). Die drei Fälle, die wir brauchen,
/// stehen dort ausgeschrieben:
/// <list type="bullet">
/// <item><b>Modus 0 und 4 → Typ 0</b> (<c>xor dl,dl</c> @0x4A9B15): der
/// gewöhnliche Pfeil.</item>
/// <item><b>Modus 1 → Typ 1, bei INFANTERIE Typ 5</b> (@0x4A9B1C). Der Arm
/// liest das Objekt unter dem Zeiger (<c>word[0x502AD8]</c>): ab 60000 (Gebäude)
/// und ab 20000 (Flugzeugplatz) sofort <c>mov dl,1</c>; sonst holt er das
/// Klassenbyte <c>byte[0x6E26D2 + 78*id]</c> und rechnet
/// <c>dec al; cmp al,1; sbb dl,dl; and dl,4; inc dl</c> — was 5 ergibt, wenn
/// die Klasse 1 ist (die Infanterie), und sonst 1.</item>
/// <item><b>Modus 2, 7 und 10 → Typ 2</b> (<c>mov dl,2</c> @0x4A9B5F): der
/// Angriffszeiger.</item>
/// </list>
/// Dass Modus 1 »der Zeiger steht auf etwas Eigenem« heißt, sagt eine zweite
/// Stelle: der Bedienblock prüft bei <c>0x4700CE</c> <c>dword[0x502AD4] == 1</c>
/// und zeigt dann das Objekt aus <c>word[0x502AD8]</c> im Feld an, sofern es dem
/// eigenen Spieler gehört.</para>
///
/// <para>⚠ <b>WAS UNSERES IST.</b> Wer den Modus <i>setzt</i>, ist ungelesen —
/// die 22 übrigen Modi sind darum nicht nachgebaut, und die drei oben hängen bei
/// uns an <see cref="Rendering.MapEntityLayer.CursorHintAt"/>, also an unserer
/// eigenen Prüfung »worauf zeigt die Maus«. Ebenfalls unser ist der
/// <see cref="FrameSeconds">Takt der Bildfolge</see>: das Original führt die
/// Phase in <c>byte[0x502AA0]</c> und setzt sie bei Gleichstand mit der Bildzahl
/// zurück (<c>0x4AA014</c>), aber wie schnell sie steigt, steht nicht dort.</para>
/// </summary>
public static class GameCursors
{
    /// <summary>Die vier Arten, die wir benutzen — die Zahlen sind die des
    /// Originals, siehe Klassenkopf.</summary>
    public const int Arrow = 0, Select = 1, Attack = 2, Foot = 5;

    /// <summary>⚠ UNSERE ZAHL: wie lange ein Bild der Folge steht. Das Original
    /// zählt die Phase, nennt aber keinen Takt.</summary>
    public const float FrameSeconds = 0.10f;

    private static readonly Dictionary<int, Texture2D[]> Bank = new();
    private static Vector2I _hot = new(32, 32);
    private static bool _tried;

    /// <summary>Sind die Bilder da? Ohne sie bleibt alles beim Systemzeiger —
    /// ein halb gesetzter Zeiger wäre schlimmer als gar keiner.</summary>
    public static bool Available
    {
        get { Load(); return Bank.Count > 0; }
    }

    private static void Load()
    {
        if (_tried) return;
        _tried = true;
        string idx = Core.Content.Path("UI/cursors/cursors_index.json");
        if (!FileAccess.FileExists(idx)) return;
        using var f = FileAccess.Open(idx, FileAccess.ModeFlags.Read);
        if (f == null) return;
        var json = Json.ParseString(f.GetAsText());
        if (json.VariantType != Variant.Type.Dictionary) return;
        var root = json.AsGodotDictionary();
        if (root.TryGetValue("hotspot_from_origin", out var hv))
            _hot = new Vector2I((int)hv, (int)hv);
        if (!root.TryGetValue("cursors", out var cv)) return;
        var list = cv.AsGodotDictionary();
        foreach (var key in list.Keys)
        {
            if (!int.TryParse(key.AsString(), out int typ)) continue;
            var rec = list[key].AsGodotDictionary();
            int n = rec.TryGetValue("frames", out var nv) ? (int)nv : 0;
            var bilder = new List<Texture2D>();
            for (int i = 0; i < n; i++)
            {
                string p = Core.Content.Path($"UI/cursors/t{typ:00}_f{i}.png");
                Texture2D? t = ResourceLoader.Exists(p) ? ResourceLoader.Load<Texture2D>(p) : null;
                if (t == null && FileAccess.FileExists(p))
                {
                    var img = Image.LoadFromFile(p);
                    if (img != null) t = ImageTexture.CreateFromImage(img);
                }
                if (t != null) bilder.Add(t);
            }
            if (bilder.Count > 0) Bank[typ] = bilder.ToArray();
        }
        // ⚠ Eine Meldung, weil ein NICHT geladener Zeiger sonst still
        // ausbleibt: es faellt auf die Systemzeiger zurueck, und das sieht aus
        // wie »nie gebaut« statt wie »Bilder fehlen«.
        GD.Print(Bank.Count > 0
            ? $"Mauszeiger: {Bank.Count} Arten geladen (Angriff={(Bank.ContainsKey(Attack) ? Bank[Attack].Length + " Bilder" : "FEHLT")})"
            : "Mauszeiger: keine Bilder gefunden - Systemzeiger bleiben");
    }

    private static int _shownType = -1, _shownFrame = -1;

    /// <summary>Den Zeiger setzen. Tut nichts, wenn schon dasselbe Bild steht —
    /// <c>SetCustomMouseCursor</c> baut den Zeiger sonst bei jedem Mausereignis
    /// neu.</summary>
    public static void Use(int typ, float zeit)
    {
        Load();
        if (!Bank.TryGetValue(typ, out var bilder))
        {
            // Kein Bild für diese Art: lieber den Pfeil des Originals als einen
            // Systemzeiger dazwischen.
            if (typ == Arrow || !Bank.TryGetValue(Arrow, out bilder)) return;
            typ = Arrow;
        }
        int bild = bilder.Length <= 1
            ? 0
            : Mathf.PosMod((int)(zeit / FrameSeconds), bilder.Length);
        if (typ == _shownType && bild == _shownFrame) return;
        _shownType = typ;
        _shownFrame = bild;
        Input.SetCustomMouseCursor(bilder[bild], Input.CursorShape.Arrow, _hot);
    }

    /// <summary>Zurück zum Systemzeiger — für die Menüs und für den Fall, dass
    /// der Spieler die Zeigerhilfen abschaltet.</summary>
    public static void Reset()
    {
        if (_shownType < 0) return;
        _shownType = _shownFrame = -1;
        Input.SetCustomMouseCursor(null, Input.CursorShape.Arrow);
    }
}
