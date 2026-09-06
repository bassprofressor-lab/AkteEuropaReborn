using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>DIE VIER KNÖPFE AM BEDIENBLOCK</b> — drei oben, einer rechts unten.
/// Gelesen und gebaut am 06.09.2026.
///
/// <para><b>Seine Meldung:</b> »Die kleinen Kästchen drum herum (4 Stück, 3 oben
/// und eins rechts unten) haben noch Funktionen, die wir bisher nicht
/// bedienen.«</para>
///
/// <para><b>Sie zeigen nichts.</b> Der Zeichner des Bedienblocks
/// (<c>0x46FE10..0x4711C8</c>) malt in <c>y &lt; 43</c> und im Feld unten rechts
/// gar nichts — dort steht nur der feste Hintergrund aus PANEL.DTA. Es sind
/// reine Knöpfe.</para>
///
/// <para><b>Die Rechtecke</b> stehen in der Trefferprüfung der Fensterart 9,
/// <c>0x45E541..0x45E6A2</c>. Sie fragt viermal <c>0x401cf8</c>
/// (Punkt-in-Rechteck) mit dem Fensterursprung <c>word[0x8B903A]</c> /
/// <c>word[0x8B903C]</c> plus einem Versatz. Zwei davon habe ich selbst
/// nachgeschlagen:</para>
/// <code>
///   @0x45E571  x + 0x50, y + 1, 0x20 x 0x20  -> 1, word[0x8B5488] := 0x24
///   @0x45E5AC  x + 0x29, y + 1, 0x20 x 0x20  -> 2, ...            := 0x25
///              x + 0x02, y + 1, 0x20 x 0x20  -> 3,                := 0x26
///              x + 0xA7, y + 0x6B, 0x23 x 0x21 -> 4,              := 0x27
/// </code>
///
/// <para><b>Und was ein Klick tut</b> — die Kette läuft über den
/// Elementbehandler <c>0x4485D0</c> und die Tafel <c>0x44DF04</c> in die vier
/// Öffner:</para>
/// <list type="number">
/// <item>Feld 1 (80, 1) → <c>0x444740</c> — die <b>Einsatzkarte</b>, Fensterart
/// <b>3</b>. ⭐ Von ihm am Original bestätigt: »das rechte den oberen 3en hat
/// die Minimap angezeigt oder ausgeschaltet« — und das rechte der drei oberen
/// ist genau dieses Feld (x = 80; die anderen liegen bei 41 und 2). Bei uns
/// schaltet es die Übersichtskarte um, siehe <c>PanelfeldOeffnen</c>.</item>
/// <item>Feld 2 (41, 1) → <c>0x442C70</c> — <b>Gruppieren</b>, Art <b>25</b>.</item>
/// <item>Feld 3 (2, 1) → <c>0x442D40</c> — der <b>Lokator</b>, Art <b>24</b>.</item>
/// <item>Feld 4 (167, 107) → <c>0x443C40</c> — das <b>Spielmenü</b>, Art
/// <b>17</b>.</item>
/// </list>
///
/// <para>⭐⭐ <b>Zwei davon bestätigt unser eigener Baum</b>, unabhängig und
/// Monate früher gelesen: <c>0x442C70</c> steht bei uns seit dem 20.08. als
/// Öffner des Gruppenfensters (<c>Strg+Zahl</c>), <c>0x442D40</c> als Öffner des
/// Lokators (<c>Strg+F5..F8</c>). Und die Fensterarten stimmen ebenfalls mit
/// <c>WindowManager.ArtGruppen = 25</c>, <c>ArtMerkpunkte = 24</c> und
/// <c>ArtKarte = 3</c> überein — Zahlen, die dort noch als ungelesen
/// gekennzeichnet waren.</para>
///
/// <para>⚠ <b>Was wir NICHT nachbauen, und warum:</b> die vier Hilfezeilen
/// (<c>word[0x8B5488] := 0x24..0x27</c>, einziger Leser <c>0x447920</c>, Texte
/// bei <c>0x4F0280 + 75·n</c>: »Karte des Einsatzgebietes zeigen«, »Zum
/// Gruppen-Menü«, »Zum Locator-Menü«, »Öffnen des Spiel-Menüs«). Das ist eine
/// eigene Textquelle, die wir noch gar nicht führen — sie gehört mit der
/// Statuszeile zusammen und nicht in diesen Bau.</para>
///
/// <para>⚠ <b>UNSERES:</b> das Original hat für den Block keine
/// Fensterelemente — der Anleger <c>0x458000</c> legt keine an, alles ist Code.
/// Wir hängen die Trefferprüfung darum ebenfalls an den Block selbst, nicht an
/// die Fenstermaschine. Und der Druckzustand wird nicht gezeichnet: der
/// Zeichner liest die Druckflagge nie (roher Abtast über
/// <c>0x46FE10..0x4711C8</c> findet keinen Zugriff auf ihren Bereich).</para>
///
/// <para>Gegenschalter <c>--panelknoepfe-alt</c>, Prüfstand
/// <c>--panelknoepfe-probe</c>.</para>
/// </summary>
public partial class MapViewer
{
    /// <summary>Ein Feld des Bedienblocks: Rechteck im 204x170-Block und das,
    /// was ein Klick öffnet.</summary>
    public readonly record struct Panelfeld(int X, int Y, int W, int H,
                                            int Fensterart, string Name);

    /// <summary>Die vier Felder, aus <c>0x45E541..0x45E6A2</c>. ⚠ Reihenfolge
    /// wie die Trefferprüfung sie abfragt — sie gibt 1, 2, 3, 4 zurück.</summary>
    public static readonly Panelfeld[] Panelfelder =
    {
        new(0x50, 1, 0x20, 0x20, UI.WindowManager.ArtKarte,      "Uebersichtskarte"),
        new(0x29, 1, 0x20, 0x20, UI.WindowManager.ArtGruppen,    "Gruppieren"),
        new(0x02, 1, 0x20, 0x20, UI.WindowManager.ArtMerkpunkte, "Lokator"),
        new(0xA7, 0x6B, 0x23, 0x21, ArtSpielmenue,               "Spielmenue"),
    };

    /// <summary>Fensterart 17 — das Spielmenü, Öffner <c>0x443C40</c>.</summary>
    public const int ArtSpielmenue = 17;

    /// <summary><c>--panelknoepfe-alt</c> — die vier Felder tun wieder
    /// nichts.</summary>
    public static bool PanelknoepfeAlt;

    /// <summary>Wie oft ein Feld gedrückt wurde, je Feld. Für den
    /// Prüfstand.</summary>
    public readonly int[] PanelfeldGedrueckt = new int[4];

    /// <summary>
    /// Welches Feld liegt unter diesem Punkt? 0..3, sonst -1.
    /// <paramref name="lokal"/> ist der Punkt IM Block, also schon durch die
    /// Vergrößerung geteilt.
    /// </summary>
    public static int PanelfeldAn(Vector2 lokal)
    {
        if (PanelknoepfeAlt) return -1;
        for (int i = 0; i < Panelfelder.Length; i++)
        {
            var f = Panelfelder[i];
            if (lokal.X >= f.X && lokal.X < f.X + f.W &&
                lokal.Y >= f.Y && lokal.Y < f.Y + f.H)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Ein Klick auf den Bedienblock. Gibt zurück, ob er auf einem Feld lag.
    ///
    /// <para>⚠ Der Klang ist derselbe, den jedes Fensterelement des Originals
    /// spielt (<c>push 0x132</c> @<c>0x448603</c>, also 306) — siehe
    /// <see cref="UI.WindowManager.Elementklang"/>.</para>
    /// </summary>
    public bool PanelKlick(Vector2 lokal)
    {
        int f = PanelfeldAn(lokal);
        if (f < 0) return false;
        PanelfeldGedrueckt[f]++;
        UI.WindowManager.Elementklang();
        PanelfeldOeffnen(f);
        return true;
    }

    /// <summary>
    /// <c>--panelknoepfe-probe</c> — <b>treffen die vier Rechtecke, und tut
    /// jedes das Seine?</b>
    ///
    /// <para>⚠ Gemessen wird auch, was NICHT treffen darf: ein Punkt mitten im
    /// Anzeigefeld gehört keinem Knopf. Ohne diese Gegenprobe wäre ein
    /// Rechteck, das den halben Block verschluckt, grün.</para>
    /// </summary>
    public string PanelknoepfeProbe()
    {
        var sb = new System.Text.StringBuilder("panelknoepfe-probe\n");
        bool alles = true;
        for (int i = 0; i < Panelfelder.Length; i++)
        {
            var f = Panelfelder[i];
            var mitte = new Vector2(f.X + f.W / 2f, f.Y + f.H / 2f);
            int t = PanelfeldAn(mitte);
            bool ok = t == i;
            alles &= ok;
            sb.AppendLine($"  Feld {i + 1} »{f.Name}« ({f.X},{f.Y},{f.W}x{f.H}), "
                        + $"Fensterart {f.Fensterart}: Mitte trifft {t + 1}: "
                        + $"{(ok ? "richtig" : "FALSCH")}");
        }

        // Gegenprobe 1: mitten im Anzeigefeld (8,43,153,94) ist KEIN Knopf
        int leer = PanelfeldAn(new Vector2(80, 90));
        bool leerOk = leer < 0;
        alles &= leerOk;
        sb.AppendLine($"  Punkt (80,90) mitten im Anzeigefeld: {(leer < 0 ? "kein Knopf" : $"Feld {leer + 1}")}: "
                    + $"{(leerOk ? "richtig" : "FALSCH — ein Rechteck ist zu gross")}");

        // Gegenprobe 2: die Felder duerfen sich nicht ueberlappen
        bool frei = true;
        for (int i = 0; i < Panelfelder.Length && frei; i++)
            for (int k = i + 1; k < Panelfelder.Length && frei; k++)
            {
                var a = Panelfelder[i]; var b = Panelfelder[k];
                frei = a.X + a.W <= b.X || b.X + b.W <= a.X
                    || a.Y + a.H <= b.Y || b.Y + b.H <= a.Y;
            }
        alles &= frei;
        sb.AppendLine($"  keine zwei Felder ueberlappen: {(frei ? "richtig" : "FALSCH")}");

        // Gegenprobe 3: der Gegenschalter macht alle vier still
        PanelknoepfeAlt = true;
        int aus = PanelfeldAn(new Vector2(Panelfelder[0].X + 4, Panelfelder[0].Y + 4));
        PanelknoepfeAlt = false;
        bool altOk = aus < 0;
        alles &= altOk;
        sb.AppendLine($"  mit --panelknoepfe-alt: {(aus < 0 ? "kein Knopf" : $"Feld {aus + 1}")}: "
                    + $"{(altOk ? "richtig" : "SCHALTER WIRKT NICHT")}");

        sb.Append(alles ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }

    private void PanelfeldOeffnen(int f)
    {
        switch (f)
        {
            case 0:
                // ⭐⭐ 06.09.2026 — SEINE BEOBACHTUNG AM ORIGINAL, und sie deckt
                // sich mit der Lesung: »das rechte den oberen 3en hat die
                // Minimap angezeigt oder ausgeschaltet«. Das rechte der drei
                // oberen Felder ist genau dieses hier (x = 0x50 = 80, die
                // anderen zwei liegen bei 41 und 2), und der Öffner dahinter
                // ist 0x444740 — die EINSATZKARTE, Fensterart 3.
                //
                // ⚠ UNSERE ERSETZUNG: ein eigenes Kartenfenster haben wir
                // nicht, wohl aber die Übersichtskarte im Block. Der Knopf
                // schaltet sie also um — dieselbe Wirkung, die er beschreibt,
                // auf unserem Mittel.
                //
                // ⚠ Und ein Unterschied, der benannt gehört: der gelesene
                // Öffner holt ein schon offenes Fenster nur NACH VORN, er
                // schliesst es nicht. Dass es ein Umschalter ist, steht auf
                // SEINER Beobachtung, nicht auf der Lesung.
                _showMinimap = !_showMinimap;
                if (_minimap != null) _minimap.Visible = _showMinimap;
                break;
            case 1: ZeigeGruppen(-1); break;
            case 2: ZeigeLokator(-1); break;
            default: TogglePause(); break;
        }
    }
}
