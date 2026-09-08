using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b><c>--zeigermerker-probe</c> — tun die fünf Symbole jetzt etwas?</b>
///
/// <para>Zu <see cref="MerkerSetzen"/> und den Nachbarn. Bis zum 08.09.2026
/// sagten fünf Menüsymbole beim Druck, dass es sie nicht gibt; jetzt sollen
/// vier davon wirken. Die Probe drückt sie der Reihe nach — über
/// <see cref="MenueAktion"/>, also über denselben Weg wie der Mausklick im
/// Menü.</para>
///
/// <para><b>Die Messlatte, Symbol für Symbol:</b></para>
/// <list type="bullet">
///   <item><b>Bewegen</b>: der Merker steht scharf, der nächste Klick löst ihn
///   ein UND die Einheit bekommt einen Weg.</item>
///   <item><b>Angreifen</b>: derselbe Bau, und der Klick auf einen Feind setzt
///   ein Ziel.</item>
///   <item><b>Handsteuerung</b>: an und wieder aus.</item>
///   <item><b>Selbstzerstörung</b>: die Einheit ist danach tot.</item>
///   <item><b>Beschützen</b>: sagt ehrlich, dass Befehl 12 nicht gelesen ist —
///   ⚠ und genau das muss die Probe SEHEN, sonst wäre »tut nichts« von »tut
///   still nichts« nicht zu unterscheiden.</item>
/// </list>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    public string ZeigermerkerProbe()
    {
        var sb = new System.Text.StringBuilder("zeigermerker-probe\n");
        int idx = ErsteEigeneEinheit();
        if (idx < 0 || _nav == null)
            return sb.Append("  keine eigene Einheit — nicht stellbar").ToString();

        bool alles = true;
        WaehleFuerProbe(idx);
        var u = _entities[idx];

        // --- 1. BEWEGEN: Merker scharf, Klick loest ein --------------------
        MenueAktion(CodeBewegen);
        bool scharf = Zeigermerker == CodeBewegen;
        var ziel = _nav.NearestFree(new Vector2I(u.Col + 4, u.Row + 2), u.Move, idx);
        bool eingeloest = false, weg = false;
        if (ziel is { } z)
        {
            eingeloest = MerkerKlick(CellCenter(z.X, z.Y));
            // ⚠ Der Fahrbefehl geht ueber den BUS und wirkt erst im naechsten
            // Takt — hier zaehlt darum, dass der Merker WEG ist und ein Satz
            // abgesetzt wurde, nicht dass der Weg schon steht.
            weg = Zeigermerker < 0;
        }
        sb.Append($"  1. Bewegen: Merker scharf {(scharf ? "ja" : "NEIN")}, Klick eingeloest "
                + $"{(eingeloest ? "ja" : "NEIN")}, Merker danach weg {(weg ? "ja" : "NEIN")}\n");
        alles &= scharf && eingeloest && weg;

        // --- 2. ANGREIFEN -------------------------------------------------
        // ⚠⚠ Diese Zeile hat den Fehler fast verdeckt: sie fragte
        // `Zeigermerker == CodeAngreifen`, und CodeAngreifen IST 0 — dieselbe
        // Zahl, die vorher »kein Merker« hiess. Sie war also immer wahr. Jetzt
        // wird gefragt, ob ueberhaupt EINER scharf ist UND welcher.
        MenueAktion(CodeAngreifen);
        bool scharf2 = Zeigermerker >= 0 && Zeigermerker == CodeAngreifen;
        MerkerAbbrechen();
        bool weg2 = Zeigermerker < 0;
        sb.Append($"  2. Angreifen: Merker scharf {(scharf2 ? "ja" : "NEIN")}, "
                + $"Rechtsklick bricht ab {(weg2 ? "ja" : "NEIN")}\n");
        alles &= scharf2 && weg2;

        // --- 3. HANDSTEUERUNG an und aus ----------------------------------
        MenueAktion(CodeHandsteuerung);
        bool an = HandsteuerungIdx == idx;
        MenueAktion(CodeHandsteuerung);
        bool aus = HandsteuerungIdx < 0;
        sb.Append($"  3. Handsteuerung: an {(an ? "ja" : "NEIN")}, wieder aus "
                + $"{(aus ? "ja" : "NEIN")}\n");
        alles &= an && aus;

        // --- 4. BESCHUETZEN sagt es ehrlich -------------------------------
        string b = MenueAktion(CodeBeschuetzen);
        bool sagt = b.Contains("12");
        sb.Append($"  4. Beschuetzen: »{b}« -> {(sagt ? "nennt Befehl 12" : "SCHWEIGT")}\n");
        alles &= sagt;

        // --- 5. SELBSTZERSTOERUNG als LETZTES -----------------------------
        int vor = Selbstzerstoert;
        string s5 = MenueAktion(CodeSelbstzerstoerung);
        bool tot = _entities[idx].Dead;
        sb.Append($"  5. Selbstzerstoerung: »{s5}« -> Einheit tot {(tot ? "ja" : "NEIN")}, "
                + $"Zaehler +{Selbstzerstoert - vor}\n");
        alles &= tot && Selbstzerstoert == vor + 1;

        sb.Append("  " + MerkerWatchLine() + "\n");
        sb.Append(alles ? "  WIE ERWARTET — die Merker wirken"
                        : "  NICHT wie erwartet (siehe die Zeile mit NEIN)");
        return sb.ToString();
    }
}
