using Godot;
using System.Text;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>`--erfindung-probe` — FÄLLT »HIFF-64« HERAUS?</b>
///
/// <para>Zu <see cref="Erfinden"/> (Simulation/Erfindung.cs). Das ist der
/// seltene Fall, in dem sich ein Nachbau gegen etwas prüfen lässt, das nicht von
/// uns stammt und auch nicht aus einer Lesung, sondern aus einem <b>echten
/// Spielstand</b>:</para>
///
/// <para>⭐⭐ Drei Spielstände des Originals (<c>4.DM</c> »The Dam«, <c>5.DM</c>,
/// <c>7.DM</c> »Chanel Tunnel«) tragen in Bauteilzeile 20 eine <b>erfundene</b>
/// Waffe namens <b>Hiff-64</b>. Wenn unser Mischlauf aus <b>Losnummer 0,
/// Index 2, Technikstufe 5</b> exakt dieselben 58 Byte erzeugt, ist die ganze
/// Kette belegt: der MSVC-Würfel, die Reihenfolge der acht Würfe, die
/// Stufenrechnung, die Rezeptwahl, die Feldmischung samt der doppelt gezählten
/// dritten Zeile, die Namensvariante, die angehängten Ziffern und die
/// Reichweiten-Nachkorrektur.</para>
///
/// <para><b>Das Nullmodell:</b> 58 Byte sind 464 Bit. Dass die zufällig
/// zusammenfallen, ist nicht ernsthaft zu diskutieren — aber auch jedes
/// EINZELNE Feld wäre ein Treffer: 21 belegte Felder, darunter vier Wörter.
/// Trifft nur eines nicht, sagt die Probe welches.</para>
///
/// <para>Aufruf:</para>
/// <code>
///   --skirmish=map_DM_4 --erfindung-probe --quit-after=10
///   --campaign=3 --erfindung-probe --quit-after=10
/// </code>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary>Bauteilzeile 20 aus <c>4.DM</c> — »Hiff-64«, Byte für Byte.
    /// ⚠ Das ist ein PRÜFMUSTER aus einem Originalspielstand, keine Zahl von
    /// uns. Dieselben 58 Byte stehen auch in <c>5.DM</c> und <c>7.DM</c>.</summary>
    private const string HiffMuster =
        "0109486966662d36340000000014690001030a03030000003200000038003400" +
        "240000020a486966662d36340000000000000000000000000000";

    private int _erfindungProbeRc = 2;
    public int ErfindungProbeRc() => _erfindungProbeRc;

    public string ErfindungProbeLine()
    {
        var sb = new StringBuilder("erfindung-probe:\n");
        sb.Append($"   Rezepte geladen: {Rezepte.Count} (erwartet 40)\n");
        if (Rezepte.Count == 0)
        {
            sb.Append("   ⚠ keine Rezepttafel — Maps/weapon_recipes.json fehlt\n");
            _erfindungProbeRc = 1;
            return sb.ToString();
        }

        // ---- der Prüfstand: Hiff-64 aus Losnummer 0 -----------------------------
        var lauf = Erfinden(0, 2, 0, techProbe: 5);
        if (lauf == null)
        {
            sb.Append("   ⚠ der Mischlauf hat nichts geliefert\n");
            _erfindungProbeRc = 1;
            return sb.ToString();
        }
        var l = lauf.Value;
        sb.Append($"   Los 0, Index 2 (Grosse Forschung), Techstufe 5:\n");
        sb.Append($"      h={l.H} h1={l.H1} h2={l.H2} h3={l.H3} | " +
                  $"Rezepte r1={l.R1} r2={l.R2} r3={l.R3} r4={l.R4}, " +
                  $"Namenswahl {l.Namenswahl}\n");
        sb.Append($"      Name: \"{l.Name}\" in Bauteilzeile {l.Zeile}\n");

        var soll = HexBytes(HiffMuster);
        var ist = BauteilFuer(0, l.Zeile)!;
        int gleich = 0;
        var abweichung = new StringBuilder();
        for (int i = 0; i < soll.Length && i < ist.Length; i++)
        {
            if (soll[i] == ist[i]) { gleich++; continue; }
            if (abweichung.Length < 120)
                abweichung.Append($" +0x{i:x2}: soll {soll[i]}, ist {ist[i]};");
        }
        bool ok = gleich == soll.Length;
        sb.Append($"   ⭐ gegen 4.DM/5.DM/7.DM Zeile 20 »Hiff-64«: " +
                  $"{gleich} von {soll.Length} Byte gleich {(ok ? "✓" : "⚠ WEICHT AB")}\n");
        if (!ok) sb.Append($"      {abweichung}\n");
        if (ok)
            sb.Append("      (damit sind Wuerfel, Wurfreihenfolge, Stufenrechnung, " +
                      "Rezeptwahl,\n       Feldmischung, Namensziffern und die " +
                      "Reichweitenkorrektur belegt)\n");

        // ---- die zweite Frage: ist die Waffe brauchbar? -------------------------
        sb.Append($"      Stufe {ist[0x01]} (erwartet 9 — nie aufwertbar), " +
                  $"Techstufe {ist[0x24]} (erwartet 10 — ueberlebt den Missionsstart), " +
                  $"Besitz {ist[0x00]}\n");
        bool merkerOk = ist[0x01] == 9 && ist[0x24] == 10 && ist[0x00] == 1;

        // ---- die dritte: erscheint sie im Angebot NICHT? ------------------------
        // Stufe 9 heisst: die erfundene Waffe wird nie zur Aufwertung angeboten.
        int inAngebot = 0;
        foreach (var a in ForschungAngebote(0))
            if (a.Bauteil == l.Zeile) inAngebot++;
        sb.Append($"      im Aufwertungsangebot: {inAngebot} (erwartet 0, weil Stufe 9)" +
                  $" {(inAngebot == 0 ? "✓" : "⚠")}\n");

        // ---- vierte: der DURCHSTICH — an einer Basis kaufen und abwarten -------
        bool durchstichOk = true;
        Entity? basis = null;
        foreach (var e in _entities)
            if (e.IsBuilding && e.BType == 1 && !e.Dead && e.Owner == 0) { basis = e; break; }
        if (basis == null)
        {
            sb.Append("   (kein Durchstich: keine BASIS des Spielers 0 auf dieser Karte)\n");
        }
        else
        {
            var angebot = ForschungAngebote(0);
            int zeile = -1;
            for (int i = 0; i < angebot.Count; i++)
                if (angebot[i].Erfindung && angebot[i].ErfindungIndex == 0) { zeile = i; break; }
            if (zeile < 0)
            {
                sb.Append("   ⚠ die Kleine Forschung steht nicht im Angebot\n");
                durchstichOk = false;
            }
            else
            {
                _money[0] = 60000;                       // Messwerkzeug
                int vorher = ErfindungFertig;
                int preis = angebot[zeile].Preis;
                bool gekauft = ForschungKaufen(basis, zeile);
                int takte = 0;
                while (ErfindungFertig == vorher && takte < preis + 10) { ForschungTick(); takte++; }
                durchstichOk = gekauft && ErfindungFertig == vorher + 1 && takte == preis;
                sb.Append($"   Durchstich: »{ErfindungAngebotName(0)}« fuer ${preis} gekauft, " +
                          $"nach {takte} Takten fertig (erwartet {preis})\n");
                sb.Append($"      neue Waffe: \"{ErfindungLetzterName}\" in Zeile " +
                          $"{ErfindungLetzteZeile} {(durchstichOk ? "✓" : "⚠")}\n");
                // ⭐ Und lässt sie sich verbauen? Eine erfundene Waffe, die im
                // Entwurfsschirm nicht auftaucht, wäre eine Zahl ohne Spiel.
                LoadDesignParts();
                ErfundeneWaffenNachtragen();
                bool imSchirm = false;
                foreach (var wp in Designer.Weapons)
                    if (wp.Id == ErfindungLetzteZeile) { imSchirm = true; break; }
                sb.Append($"      im Entwurfsschirm waehlbar: {(imSchirm ? "ja ✓" : "NEIN ⚠")}" +
                          $" ({Designer.Weapons.Count} Waffen in der Liste)\n");
                durchstichOk &= imSchirm;
            }
        }

        bool alles = ok && merkerOk && inAngebot == 0 && durchstichOk;
        sb.Append($"   → {(alles ? "BESTANDEN" : "⚠ DURCHGEFALLEN")}\n");
        _erfindungProbeRc = alles ? 0 : 1;
        return sb.ToString();
    }
}
