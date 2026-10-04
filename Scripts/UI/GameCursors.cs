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

    /// <summary>
    /// <b>Der EINFAHRZEIGER, Bild 11.</b> Gemeldet am 06.09.2026: »es gibt ein
    /// Icon, aehnlich wie das Angreifen Icon, wenn man Einheiten auf die Tuer
    /// einfahren laesst ins Depot, das fehlt bei uns noch«.
    ///
    /// <para>Das Original setzt dafuer die ZEIGERART 5 (<c>dword[0x502AD4] :=
    /// 5</c> @<c>0x4323E6</c>), und zwar nur, wenn das Gebaeude dem Betrachter
    /// gehoert (<c>byte[0x7AD495 + 4*i]</c> gegen <c>byte[0x4FA284]</c>,
    /// @<c>0x432398</c>) und seine Art ueber die Tafel <c>0x432A7C</c> auf
    /// einen der drei Einfahr-Arme faellt — die Arten <b>1, 5, 6, 12</b>.</para>
    ///
    /// <para>⚠ <b>Nicht mit <see cref="Foot"/> verwechseln.</b> Die 5 dort ist
    /// eine Zeiger-TYPnummer INNERHALB der Zeigerart 1; die 5 hier ist die
    /// Zeiger-ART selbst. Zwei verschiedene Zahlenreihen, und sie treffen sich
    /// unglücklicherweise auf derselben Ziffer.</para></summary>
    public const int Einfahrt = 11;

    /// <summary><b>Der EINNAHMEZEIGER</b> — Zeigerart 6 des Originals
    /// (<c>@0x432478</c>) fuehrt ueber die Tafel <c>@0x4A9BEC</c> auf
    /// <c>@0x4A9B89 mov dl,0xA</c>, also auf <b>Zeigerbild 10</b>.
    /// Gemeldet am 08.09.2026: »es gibt sogar ein Einnahme Icon fuer
    /// Gebaeude«.</summary>
    public const int Einnahme = 10;

    /// <summary>
    /// Der ENTLADEZEIGER ueber einer Rampe — <b>Bild 16</b>.
    ///
    /// <para>⚠⚠ BERICHTIGT am 07.09.2026, noch am selben Abend. Hier stand
    /// <c>= 12</c>, und seine Meldung war sofort da: »Das Entladen Icon sieht
    /// aus wie ein Fahrzeugteil, anstatt das originale entladen schiffs
    /// icon«. Der Fehler ist genau der, vor dem der Kommentar bei
    /// <see cref="Einfahrt"/> warnt: <c>@0x432771</c> setzt die ZEIGERART
    /// (<c>dword[0x502AD4] := 0xC</c>), nicht die Bildnummer.</para>
    ///
    /// <para>⭐ Die Umrechnung steht in <c>0x4A9AB0</c>, Sprungtafel
    /// <c>0x4A9BEC</c> — jetzt roh gelesen statt erschlossen:</para>
    /// <code>
    ///   Modus  5 -> 0x4A9B85  mov dl, 0x0B   Bild 11   (einfahren)
    ///   Modus  6 -> 0x4A9B89  mov dl, 0x0A   Bild 10
    ///   Modus  8 -> 0x4A9B8D  mov dl, 0x0C   Bild 12   ← das war zu sehen
    ///   Modus  9 -> 0x4A9B91  mov dl, 0x0D   Bild 13
    ///   Modus 11 -> 0x4A9B95  mov dl, 0x0B   Bild 11
    ///   Modus 12 -> 0x4A9B99  mov dl, 0x10   Bild 16   ← ENTLADEN
    /// </code>
    /// <para>Dass Modus 5 auf Bild 11 fuehrt, ist die Gegenprobe: genau die
    /// Zahl, die der Einfahrzeiger seit dem 06.09. traegt und die im Spiel
    /// bestaetigt ist.</para></summary>
    public const int Entladen = 16;

    /// <summary><b>Der SCHRAUBENSCHLUESSEL</b> ueber zerschossenem Gleis — Zeigerart
    /// 22 (@0x431B93) fuehrt ueber die Tafel <c>0x4A9BEC</c> auf <c>0x4A9BA1
    /// mov dl,0x11</c>, also <b>Bild 17</b> (fuenf Bilder, der Schluessel mit den
    /// vier gelben Pfeilen). Selbst gelesen am 23.09.2026, bug-368.</summary>
    public const int Reparatur = 17;

    /// <summary><b>»TERRANIUM SUCHEN«</b> — Modus 3 setzt in der Zeigerwahl
    /// <c>0x4317C9</c> OHNE Pruefung die Zeigerart <c>0x0D</c>, und die Tafel
    /// <c>0x4A9BEC[13]</c> fuehrt auf <c>0x4A9B9D mov dl,0x13</c>, also
    /// <b>Bild 19</b>. Selbst gelesen am 01.10.2026, bug-381.</summary>
    public const int TerraSuche = 19;

    /// <summary><b>Handsteuerung Boden</b> — Zustand 1000 fuehrt @0x4A9AF3 auf
    /// @0x4A9BC9 <c>mov dl,3</c>: Bild 3 (ein Bild, kleines Kreuz). bug-415.</summary>
    public const int Handsteuerung = 3;

    /// <summary><b>FAHRT</b> — Zustand 3 (Fahrbefehl ueber freiem Boden, einem
    /// Verbuendeten, einem tuerlosen fremden Gebaeude): @0x4A9B81 <c>mov dl,4</c>,
    /// Bild 4 (fuenf Bilder, vier Pfeile nach innen). bug-415.</summary>
    public const int Fahrt = 4;

    /// <summary><b>FORMATIONSFAHRT</b> — Zustand 3 bei einer GRUPPE
    /// (<c>word[0x4FA0C8] == 10000</c> @0x4A9B63) und Einstellung 16 ≠ Shift
    /// (@0x4A9B6E…0x4A9B79): @0x4A9B7D <c>mov dl,0x1B</c>, Bild 27 (vier Bilder).
    /// bug-418.</summary>
    public const int Formation = 27;

    /// <summary>
    /// ⭐ 04.10.2026, bug-415 — <b>DER BILDTAKT DES ORIGINALS</b>: der Zeichner
    /// <c>0x4A9F90</c> zaehlt die Phase <c>byte[0x502AA0]</c> bei JEDEM Aufruf um
    /// eins weiter (@0x4A9FE8…0x4A9FF9, Ruecksprung auf 0 bei der Bildzahl aus
    /// 0xA31AA0 @0x4AA014/1C), und gerufen wird er aus der Hauptschleife 0x415CF0
    /// @0x416CE1 AUSSERHALB des Taktblocks — also einmal je BILD. Der Bildzaehler
    /// laeuft mit 50/s (O(B), meldungsfenster-fable: word 0x4FA248) ⇒ 0,02 s je
    /// Bild, eine Fuenferfolge dreht in 0,1 s.
    /// <para>⚠ (V): die 50 Bilder/s sind dort gelesen, nicht hier gemessen; das
    /// Original haengt an seiner tatsaechlichen Bildrate, wir an der Uhr.</para>
    /// <para>⭐ 04.10.2026 — <b>VORGABE IST 0,10 s je Bild</b>, die Entscheidung des
    /// Spielers nach Augenmass im Vergleich (»das alte tempo war besser, mach das als
    /// standard«): die 50 Bilder/s sind ungemessen, das Original lief auf der Hardware
    /// von 1998 vermutlich deutlich langsamer. UNSERE Setzung, nicht gelesen.</para>
    /// <para>Schalter <c>--zeigertakt-schnell</c>: 0,02 s je Bild (50 Bilder/s wie
    /// oben gelesen). <c>--zeigertakt-alt</c> bleibt als gleichbedeutender Name der
    /// Vorgabe angenommen.</para></summary>
    public static float FrameSeconds => ZeigertaktSchnell ? 0.02f : 0.10f;

    /// <summary><c>--zeigertakt-alt</c> (seit 04.10.2026 die Vorgabe, wirkungslos).</summary>
    public static bool ZeigertaktAlt = System.Array.IndexOf(Core.CommandLine.Args, "--zeigertakt-alt") >= 0;

    /// <summary><c>--zeigertakt-schnell</c> — 0,02 s je Bild (50 Bilder/s).</summary>
    public static bool ZeigertaktSchnell = System.Array.IndexOf(Core.CommandLine.Args, "--zeigertakt-schnell") >= 0;

    /// <summary><c>--zeiger-ohne-nachzug</c> — der Stand vor dem 04.10.2026
    /// (bug-415): der Spielerimport schreibt keine Zeiger, und beim Start wird
    /// nichts nachgezogen.</summary>
    public static bool OhneNachzug = System.Array.IndexOf(Core.CommandLine.Args, "--zeiger-ohne-nachzug") >= 0;

    /// <summary>Was beim Start nachgezogen wurde (leer = nichts) — fuer das
    /// Protokoll und den Pruefstand.</summary>
    public static string Nachgezogen = "";

    private static readonly Dictionary<int, Texture2D[]> Bank = new();
    private static Vector2I _hot = new(32, 32);
    private static bool _tried;

    /// <summary>Sind die Bilder da? Ohne sie bleibt alles beim Systemzeiger —
    /// ein halb gesetzter Zeiger wäre schlimmer als gar keiner.</summary>
    public static bool Available
    {
        get { Load(); return Bank.Count > 0; }
    }

    /// <summary>Die Entscheidung des Nachzugs (bug-415), fuer <see cref="Load"/> und
    /// <c>--zeigerbank-check</c> dieselbe: steht unter <paramref name="datenRoot"/>
    /// (Betriebssystempfad des Datenordners) schon <c>UI/cursors/cursors_index.json</c>,
    /// geschieht nichts (Rueckgabe ""); mit <c>--zeiger-ohne-nachzug</c> auch nicht
    /// (null); sonst wird aus dem Original nachgezogen (Quelle oder null).</summary>
    public static string? NachzugWennNoetig(string datenRoot)
    {
        string root = datenRoot.TrimEnd('/', '\\');
        if (System.IO.File.Exists(root + "/UI/cursors/cursors_index.json")) return "";
        if (OhneNachzug) { GD.Print("Mauszeiger: Zeigerbank fehlt - kein Nachzug (--zeiger-ohne-nachzug)"); return null; }
        string? quelle = Import.ContentBuilder.ZeigerbankNachziehen(root, t => GD.Print("Mauszeiger-Nachzug: " + t));
        GD.Print(quelle != null
            ? $"Mauszeiger: Zeigerbank fehlte - aus {quelle} nachgezogen"
            : "Mauszeiger: Zeigerbank fehlt, ROBO.CWR nicht gefunden - kein Nachzug");
        return quelle;
    }

    private static void Load()
    {
        if (_tried) return;
        _tried = true;
        string idx = Core.Content.Path("UI/cursors/cursors_index.json");
        // ⭐⭐ 04.10.2026, bug-415 — FEHLT DIE BANK, WIRD SIE EINMAL NACHGEZOGEN.
        // Bis heute schrieb sie nur der Entwicklerweg --reexport-effects; wer
        // »nur« importiert hatte (KayelGee, jeder Spieler), sah ueberall den
        // Systempfeil. Jetzt schreibt sie der Import selbst (ContentBuilder.
        // OberflaecheSchreiben), und wer vorher importiert hat, bekommt sie hier
        // aus dem Original nach, sofern ROBO.CWR auffindbar ist.
        if (!FileAccess.FileExists(idx))
        {
            string? quelle = NachzugWennNoetig(ProjectSettings.GlobalizePath(Core.Content.UserRoot));
            Nachgezogen = quelle ?? "";
            idx = Core.Content.Path("UI/cursors/cursors_index.json");
        }
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

    /// <summary><c>--zeigerbank-check</c>: liest eine Zeigerbank aus einem
    /// BELIEBIGEN Ordner (Betriebssystempfad), ohne die Bank des Spiels anzufassen.
    /// Gibt je Art die Zahl der wirklich ladbaren Bilder.</summary>
    public static Dictionary<int, int> ProbeLesen(string ordner)
    {
        var raus = new Dictionary<int, int>();
        string idx = ordner.TrimEnd('/', '\\') + "/cursors_index.json";
        if (!System.IO.File.Exists(idx)) return raus;
        var json = Json.ParseString(System.IO.File.ReadAllText(idx));
        if (json.VariantType != Variant.Type.Dictionary) return raus;
        var root = json.AsGodotDictionary();
        if (!root.TryGetValue("cursors", out var cv)) return raus;
        var list = cv.AsGodotDictionary();
        foreach (var key in list.Keys)
        {
            if (!int.TryParse(key.AsString(), out int typ)) continue;
            var rec = list[key].AsGodotDictionary();
            int n = rec.TryGetValue("frames", out var nv) ? (int)nv : 0;
            int gut = 0;
            for (int i = 0; i < n; i++)
            {
                var img = Image.LoadFromFile($"{ordner}/t{typ:00}_f{i}.png");
                if (img != null && img.GetWidth() > 0) gut++;
            }
            raus[typ] = gut;
        }
        return raus;
    }

    /// <summary>Hat die Bank ueberhaupt ein Bild fuer diese Art? ⚠ Fuer
    /// Pruefstaende: ein fehlendes Bild faellt still auf den Systempfeil
    /// zurueck und sieht aus wie »nie gebaut«.</summary>
    public static bool HatBild(int art)
    {
        Load();
        return Bank.TryGetValue(art, out var b) && b.Length > 0;
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
