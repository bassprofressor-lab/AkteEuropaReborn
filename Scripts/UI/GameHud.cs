namespace AkteEuropaReborn.UI;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>Die stehende Rohstoffanzeige des Gefechts</b> — Terranium, Waffen-,
/// Fahrwerk- und Spezialteile, der Kontostand, und je Sorte der ZUWACHS JE
/// SEKUNDE. Damit muss der Spieler nicht mehr erst eine Basis anklicken, um zu
/// sehen, ob seine Wirtschaft läuft (bisher ging das nur über
/// <see cref="BaseWindow"/>).
///
/// <para><b>⚠ DASS ES DIESE ANZEIGE ÜBERHAUPT GIBT, IST UNSERE ZUTAT.</b> Das
/// Original hat sie NICHT, und das ist gelesen, nicht vermutet. Die
/// Zeichenroutine des Bedienblocks (PANEL.DTA) ist
/// <c>panel_draw</c> @0x46FE10 und sie ist 5048 Bytes lang, bis zur
/// int3-Polsterung bei 0x4711C8 vollständig gelesen. Sie enthält GENAU 37
/// Zeichenaufrufe, und das ist die ganze stehende Anzeige des Originals:</para>
/// <list type="bullet">
/// <item>zwei Balkenpaare am unteren Rand: Grund 58×4 bei (140,148) und
/// (140,157), darüber der Wert 2 px hoch bei (141,149) bzw. (141,158), Breite
/// <c>Prozent·56/100</c> (@0x46FE66, 0x46FEB3, 0x46FEDB, 0x46FF20). Die zwei
/// Prozentwerte stehen in <c>byte[0x991710]</c> und <c>byte[0x991714]</c>, und
/// <b>gesetzt werden sie von der Stromabrechnung</b> @0x440270: aus
/// <c>word[0x87A580+4·Spieler]</c> und <c>word[0x87A582+4·Spieler]</c>.
/// ⚠⚠ <b>BERICHTIGT 18.08.2026:</b> hier stand »+0x00 Stromverbrauch, +0x02
/// Stromproduktion«, und das ist <b>vertauscht</b> — den BEDARF tragen die
/// Fabriken nach <c>+0x02</c> ein (@0x44032E), und nach <c>+0x00</c> schreibt
/// erst der zweite Durchgang die <b>erbrachte Leistung</b> (@0x440543). Die
/// Rechnung darunter war richtig gelesen, nur die zwei Namen standen über
/// Kreuz. Seit C10 sind die Balken gebaut — siehe
/// <c>Simulation/Power.cs</c> und <c>MapViewer.BuildPowerBars</c>.
/// — ist die Produktion
/// kleiner, wird Balken 1 auf 100 gesetzt und Balken 2 auf
/// <c>Produktion·100/Verbrauch</c> (@0x4405F8/0x440615), sonst umgekehrt
/// (@0x440623/0x440643). Im Bild <c>Assets/Legacy/UI/panel.png</c> sitzt genau
/// links davor der orange BLITZ — es sind die zwei STROMbalken.</item>
/// <item>die Missionsuhr als Text bei (23,148) (@0x4700B1).</item>
/// <item>alles Übrige zeichnet in den eingelassenen Kasten und gehört der
/// GEWÄHLTEN EINHEIT: Name (11,45), weitere Zeilen im 13-px-Schritt bei
/// (11,58/71/84/97) und drei Balken bei (91,66/86/107).</item>
/// </list>
/// <para><b>Keine Zahl für Terranium, Waffen, Fahrwerk, Spezial oder Geld
/// steht dauerhaft auf dem Schirm.</b> Diese Wörter kommen im Original nur in
/// GEBÄUDEFENSTERN vor — <c>"Terranium gelagert : "</c> (0x501BA8),
/// <c>"Waffen gelagert : ]"</c> (0x501C70), <c>"Fahrwerke gelagert : ["</c>
/// (0x501C54), <c>"Spezialteile gelagert : {"</c> (0x501C34) im Fabrikfenster
/// @0x46EDC0, <c>"Kontostand : $"</c> (0x50196C) im Basisfenster @0x46B3FE. Ein
/// Seitenpanel gibt es ohnehin nicht (siehe MapViewer.BuildLegacyPanel).</para>
///
/// <para><b>Was am Original abgeschrieben ist:</b> die SINNBILDER und die
/// Wörter. <c>]</c>, <c>[</c>, <c>{</c> und <c>$</c> sind in FONT.CWD keine
/// Klammern, sondern Waffenteil, Fahrwerksteil, Spezialteil und Geldsack — das
/// Original schreibt sie als gewöhnliche Zeichen in seine Zeilen, in der
/// Reihenfolge des Preisdruckers @0x46EE01/08/0F. Wir nehmen dieselben Zeichen
/// aus <see cref="BaseWindow.IconW"/> und bekommen dieselben Bilder.</para>
///
/// <para><b>⚠ Und UNSERE Setzungen im Einzelnen:</b></para>
/// <list type="bullet">
/// <item>Die STELLE: eine schmale Leiste oben mittig. Die vier Ecken sind
/// vergeben (Bedienblock unten links, Übersichtskarte unten rechts,
/// Basisfenster oben rechts, Statusplatte oben links), und im Original ist
/// oben nichts als Karte. Es verdrängt also nichts, was das Original dort
/// hätte — kostet aber Kartenfläche, wie jede Zutat.</item>
/// <item>Dass Bestände über ALLE Gebäude des Spielers ADDIERT werden. Das
/// Original führt keinen Spielervorrat: jede Fabrik hat ihre eigenen drei
/// Lager (+0x28/+0x2A/+0x2C) und ihr eigenes Terranium (+0x2E), und bezahlt
/// wird aus dem Lager DES Gebäudes, dessen Fenster offen ist (@0x44A6D8/ED/08).
/// Die Summe ist eine Auskunft, kein Konto.</item>
/// <item>Der ZUWACHS JE SEKUNDE selbst. Er ist gemessen, nicht gerechnet:
/// <see cref="Sample"/> nimmt bei jedem Wechsel der SIMULATIONSSEKUNDE eine
/// Probe und mittelt über <see cref="Window"/> Sekunden. ⚠ Die Zeit kommt aus
/// <c>MapEntityLayer.DebugClock</c>, also aus dem FESTEN TAKT — nicht aus der
/// Bildzeit. Mit Bildzeit stünde bei 30 und bei 144 Bildern/s eine andere Zahl
/// da, und das war am 15.08.2026 der teuerste Befund des Tages.</item>
/// <item>Die Fensterbreite von <see cref="Window"/> = 8 s. Kürzer zappelt: ein
/// Teil entsteht nur alle <c>ProdPeriod[V]/16</c> Sekunden (bei V=3 alle
/// 4,75 s), eine Ein-Sekunden-Messung zeigte also abwechselnd 0 und 1.</item>
/// <item>Die Farben und die Schrittweiten.</item>
/// </list>
///
/// <para><b>Die Bauzeile</b> (zweite Zeile, nur wenn gebaut wird) ist ebenfalls
/// unsere Zutat, und zwar eine doppelte: das Original führt <b>für eine
/// Einheit gar keine Bauzeit</b>. Alle drei Herstellungswege setzen die Einheit
/// im selben Takt in die Welt, in dem der Befehl abgearbeitet wird — Fahrzeuge
/// Befehl 503 (@0x44A726) → Handler @0x4B1840, Flugzeuge Befehl 501
/// (@0x449E33), Schiffe Befehl 508 (@0x44B369) → Handler @0x4B2B20. Der
/// Zähler <c>MapEntityLayer.Entity.BuildTime</c> ist unsere Erfindung
/// (<c>BuildSeconds = 6</c>); die Leiste zeigt ihn an, WEIL er da ist, und
/// sagt mit dem Zusatz »(unsere Zutat)« dazu, dass er nicht vom Original
/// kommt. Was das Original an dieser Stelle wirklich hat, ist das DEPOT: sechs
/// Plätze je Gebäude (<c>0x878E5C</c>, Schrittweite 16), Prüfung auf »voll«
/// @0x44A6B8 mit der Meldung »Es gibt keinen Platz mehr im Depot.« (0x4FBF60)
/// und die Obergrenze <c>cmp al,6</c> @0x467FBF.</para>
/// </summary>
public sealed partial class GameHud : Control
{
    /// <summary>Was die Leiste anzeigt. Alles vier sind SUMMEN über die
    /// Gebäude des Sichtspielers (⚠ unsere Zutat, siehe Klassenkopf); Geld ist
    /// der Kontostand aus sec73.</summary>
    public readonly struct Stocks
    {
        public Stocks(int t, int w, int f, int s, int money, bool valid, int buildings = 0)
        { T = t; W = w; F = f; S = s; Money = money; Valid = valid; Buildings = buildings; }

        /// <summary>Über wieviele Gebäude summiert wurde. ⚠ 17.08.2026, Fehler
        /// C1: »Die Ressourcen in der Basis stimmen nicht mit denen über ein,
        /// die es oben in der Ressourcenleiste steht.« Sie stimmen nie überein,
        /// sobald es mehr als ein Gebäude gibt — die Leiste ist eine SUMME, das
        /// Gebäudefenster zeigt EIN Lager. Das war nirgends zu sehen, deshalb
        /// steht die Zahl jetzt in der Leiste.</summary>
        public int Buildings { get; }
        public int T { get; }
        public int W { get; }
        public int F { get; }
        public int S { get; }
        public int Money { get; }
        /// <summary>false = es gab nichts zu lesen (kein Gebäude, keine Karte).
        /// Dann bleibt die Leiste weg, statt vier Nullen zu behaupten.</summary>
        public bool Valid { get; }
    }

    /// <summary>Die Bestände des Sichtspielers.</summary>
    public Func<Stocks>? Read;

    /// <summary>Die SIMULATIONSZEIT in Sekunden — <c>MapEntityLayer.DebugClock</c>.
    /// ⚠ NICHT die Bildzeit: der Zuwachs je Sekunde muss aus dem festen Takt
    /// kommen (siehe Klassenkopf).</summary>
    public Func<double>? SimClock;

    /// <summary>Was gerade gebaut wird, oder "" — ⚠ unsere Zutat, siehe
    /// Klassenkopf.</summary>
    public Func<string>? Building;

    // ---- Farben (⚠ unsere Werte, im Ton der übrigen Oberfläche) -------------
    private static readonly Color Plate = new(0.04f, 0.05f, 0.07f, 0.72f);
    private static readonly Color Edge = new(0.42f, 0.40f, 0.36f, 0.85f);
    private static readonly Color IconFg = new(0.85f, 0.20f, 0.14f);
    private static readonly Color ValueFg = new(0.92f, 0.91f, 0.85f);
    private static readonly Color RiseFg = new(0.55f, 0.85f, 0.50f);
    private static readonly Color FallFg = new(0.90f, 0.45f, 0.40f);
    private static readonly Color FlatFg = new(0.55f, 0.54f, 0.50f);
    private static readonly Color MoneyFg = new(0.90f, 0.85f, 0.45f);

    /// <summary>Über wieviele Simulationssekunden der Zuwachs gemittelt wird.
    /// ⚠ Unsere Setzung — siehe Klassenkopf, warum nicht über eine.</summary>
    public const int Window = 8;

    private Font? _font;
    private int _fontSize = 13;

    public GameHud()
    {
        // Die Leiste nimmt keine Maus an: sie liegt über der Karte, und ein
        // Klick dort MUSS weiter die Karte treffen.
        MouseFilter = MouseFilterEnum.Ignore;
        // ⚠ Always, wie beim Abschluss- und beim Basisfenster: bei angehaltenem
        // Baum sollen die Zahlen STEHEN, aber sichtbar bleiben.
        ProcessMode = ProcessModeEnum.Always;
    }

    public void SetFont(Font font, int size)
    {
        _font = font;
        _fontSize = size;
        QueueRedraw();
    }

    // ---- Messung ------------------------------------------------------------

    private readonly List<(double At, Stocks S)> _samples = new();
    private int _lastSecond = int.MinValue;
    private Stocks _now;
    private bool _have;

    /// <summary>Eine Probe je SIMULATIONSSEKUNDE. Wird aus dem Bildlauf
    /// gerufen, entscheidet aber allein nach der Simulationsuhr, ob eine neue
    /// Sekunde begonnen hat — damit dieselbe simulierte Zeit bei jeder Bildrate
    /// dieselbe Zahl ergibt.</summary>
    public void Sample()
    {
        if (Read == null) return;
        double t = SimClock?.Invoke() ?? 0.0;
        var s = Read();
        _now = s;
        _have = s.Valid;
        int sec = (int)Math.Floor(t);
        if (sec == _lastSecond) { QueueRedraw(); return; }
        _lastSecond = sec;
        if (!s.Valid) { _samples.Clear(); QueueRedraw(); return; }
        _samples.Add((t, s));
        while (_samples.Count > Window + 1) _samples.RemoveAt(0);
        QueueRedraw();
    }

    /// <summary>Zuwachs je Sekunde je Sorte, über das Messfenster gemittelt.
    /// (0,0,0,0), solange weniger als zwei Proben da sind — eine einzelne Probe
    /// gibt keine Rate her, und geschätzt wird hier nichts.</summary>
    public (float T, float W, float F, float S) Rates()
    {
        if (_samples.Count < 2) return (0f, 0f, 0f, 0f);
        var a = _samples[0];
        var b = _samples[^1];
        float dt = (float)(b.At - a.At);
        if (dt <= 0.001f) return (0f, 0f, 0f, 0f);
        return ((b.S.T - a.S.T) / dt, (b.S.W - a.S.W) / dt,
                (b.S.F - a.S.F) / dt, (b.S.S - a.S.S) / dt);
    }

    /// <summary>Über wieviele Sekunden gerade gemittelt wird — gehört in jede
    /// Meldung, sonst ist eine Rate nicht nachrechenbar.</summary>
    public float Span =>
        _samples.Count < 2 ? 0f : (float)(_samples[^1].At - _samples[0].At);

    // ---- Zeichnen -----------------------------------------------------------

    private float LineH => Mathf.Max(10f, _fontSize + 4f);

    public override void _Draw()
    {
        if (_font == null || !_have) return;
        var r = Rates();
        string build = Building?.Invoke() ?? "";

        // Erst messen, dann malen: die Platte muss so breit sein wie der Inhalt.
        var cells = new List<(string Icon, Color IconCol, string Val, float Rate)>
        {
            ("T", IconFg, _now.T.ToString(), r.T),
            (BaseWindow.IconW, IconFg, _now.W.ToString(), r.W),
            (BaseWindow.IconF, IconFg, _now.F.ToString(), r.F),
            (BaseWindow.IconS, IconFg, _now.S.ToString(), r.S),
        };

        // ⚠ 17.08.2026 — DIE VORSILBE. Fehler C1: die Leiste sah aus wie »mein
        // Vorrat« und ist eine SUMME über alle zahlenden Gebäude (Basis,
        // Fabriken, Flughafen, Werft-Station). Wer sie neben ein Basisfenster
        // hält, sieht zwei verschiedene Zahlen und hat keinen Grund, die eine
        // für richtig zu halten. Das Wort und die Gebäudezahl beantworten das
        // an Ort und Stelle, ohne dass jemand einen Kommentar lesen muss.
        string lead = _now.Buildings > 0 ? $"gesamt ({_now.Buildings})" : "gesamt";

        float gap = W(" ") * 2f;
        float x = gap;
        float total = gap + W(lead) + gap;
        foreach (var c in cells)
            total += W(c.Icon) + W(" ") + W(c.Val) + W(" ") + W(RateText(c.Rate)) + gap * 1.5f;
        total += W("$") + W(" ") + W(_now.Money.ToString()) + gap;

        float lines = build.Length > 0 ? 2f : 1f;
        float h = LineH * lines + 6f;
        float wide = Mathf.Max(total, build.Length > 0 ? gap * 2 + W(build) : 0f);
        Size = new Vector2(wide, h);

        DrawRect(new Rect2(Vector2.Zero, Size), Plate);
        DrawRect(new Rect2(Vector2.Zero, Size), Edge, false, 1f);

        float baseline = LineH - 2f;
        DrawString(_font, new Vector2(x, baseline), lead,
                   HorizontalAlignment.Left, -1, _fontSize, FlatFg);
        x += W(lead) + gap;
        foreach (var c in cells)
        {
            DrawString(_font, new Vector2(x, baseline), c.Icon,
                       HorizontalAlignment.Left, -1, _fontSize, c.IconCol);
            x += W(c.Icon) + W(" ");
            DrawString(_font, new Vector2(x, baseline), c.Val,
                       HorizontalAlignment.Left, -1, _fontSize, ValueFg);
            x += W(c.Val) + W(" ");
            string rt = RateText(c.Rate);
            DrawString(_font, new Vector2(x, baseline), rt,
                       HorizontalAlignment.Left, -1, _fontSize, RateColour(c.Rate));
            x += W(rt) + gap * 1.5f;
        }
        DrawString(_font, new Vector2(x, baseline), "$",
                   HorizontalAlignment.Left, -1, _fontSize, MoneyFg);
        x += W("$") + W(" ");
        DrawString(_font, new Vector2(x, baseline), _now.Money.ToString(),
                   HorizontalAlignment.Left, -1, _fontSize, MoneyFg);

        if (build.Length > 0)
            DrawString(_font, new Vector2(gap, baseline + LineH), build,
                       HorizontalAlignment.Left, Size.X - gap * 2, _fontSize, FlatFg);
    }

    /// <summary>»+1,2/s«. Eine Rate unter 0,05 heisst 0 — sonst stünde dort
    /// dauernd ein Messrauschen von +0,01/s.</summary>
    private static string RateText(float v)
    {
        if (Mathf.Abs(v) < 0.05f) return "0/s";
        return (v > 0 ? "+" : "") + v.ToString("0.0") + "/s";
    }

    private static Color RateColour(float v) =>
        Mathf.Abs(v) < 0.05f ? FlatFg : v > 0f ? RiseFg : FallFg;

    private float W(string s) =>
        _font == null ? 0f : _font.GetStringSize(s, HorizontalAlignment.Left, -1, _fontSize).X;

    /// <summary>Oben mittig, unter der Statusplatte durch. Wird bei jeder
    /// Fenstergrössenänderung neu gerufen.</summary>
    public void Place(Vector2 viewport, float topOffset)
    {
        Position = new Vector2(Mathf.Max(0f, (viewport.X - Size.X) * 0.5f), topOffset);
    }

    /// <summary>Für einen Prüflauf, der nicht auf den Bildschirm sehen kann —
    /// und für die GEGENPROBE gegen <c>--econ-check</c>: dieselbe Sekunde, hier
    /// die Summe und die Rate, dort die einzelnen Lager. Die Spanne steht
    /// dabei, damit die Rate nachrechenbar ist.</summary>
    public string WatchLine()
    {
        if (!_have) return "hud: nichts zu zeigen (kein Gebäude des Sichtspielers)";
        var r = Rates();
        return $"hud: T{_now.T} W{_now.W} F{_now.F} S{_now.S} ${_now.Money} " +
               $"(Summe ueber {_now.Buildings} Gebaeude) | " +
               $"Zuwachs/s T{r.T:0.00} W{r.W:0.00} F{r.F:0.00} S{r.S:0.00} " +
               $"(gemittelt über {Span:0.0}s, {_samples.Count} Proben)" +
               (Building?.Invoke() is { Length: > 0 } b ? $" | {b}" : "");
    }
}
