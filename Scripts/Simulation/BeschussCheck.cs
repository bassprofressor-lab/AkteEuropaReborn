using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>`--beschuss-check` — WER SCHIESST AUF WEN?</b>
///
/// <para>⚠ 02.09.2026, gemeldet aus einem Spiellauf der Kampagne 3: »ich höre
/// fremde Einheiten im Fog of War kämpfen, als würde sich die KI selber
/// beballern«, und auf Nachfrage: »doch, habe es live gesehen wie die sich
/// beschießen«.</para>
///
/// <para>Die Diplomatie von Mission 3 sagt, dass das nicht sein darf: die
/// Spieler 1, 2, 5 und 6 sind ALLE untereinander verbündet
/// (<c>campaign_diplomacy.json</c>, aus <c>mission_init</c> @0x487c40
/// gelesen), und Spieler 7 ist mit jedem verbündet. Nur Spieler 0 steht
/// allein. Ein Schuss zwischen 1 und 2 ist also ein Fehler bei UNS.</para>
///
/// <para><b>Warum das gemessen und nicht gelesen wird:</b> die Schusskette hat
/// mehrere Stellen, an denen die Bündnisfrage gestellt werden müsste, und nur
/// eine davon ist <see cref="MapEntityLayer.IsHostile"/>. Ein befohlener
/// Angriff, ein Zellangriff, ein bewaffnetes Gebäude und der Splittereinschlag
/// gehen andere Wege. Die Tafel hier zählt am ENDE der Kette — an
/// <see cref="MapEntityLayer.Fire"/> und am Zellschuss —, also dort, wo der
/// Schuss wirklich fällt, statt dort, wo er hätte verhindert werden sollen.
/// Damit ist die Aussage »niemand beschießt einen Verbündeten« ein
/// Vollbefund und keine Stichprobe.</para>
///
/// <para>Nullmodell für den Befund: fiele die Bündnisprüfung ganz aus, wären
/// bei vier gegnerischen Spielern in einem Kartenviertel die meisten Paare
/// besetzt. Steht am Ende NUR die Zeile 0↔1/2/5/6 da, ist die Prüfung intakt.
/// </para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary>Schüsse je Paar (Schütze, Getroffener), nach Spielerplatz.
    /// Der Zellangriff trägt seinen Schuss unter dem Schützen und der Spalte
    /// <see cref="BeschussZelle"/> ein, weil er keinen Besitzer hat.</summary>
    private readonly int[,] _beschuss = new int[8, 8];

    /// <summary>Schüsse auf eine ZELLE statt auf eine Einheit (Strg-Angriff,
    /// Wald). Kein Opfer, darum eine eigene Spalte.</summary>
    public int BeschussZelle { get; private set; }

    /// <summary>Schüsse, deren Schütze oder Opfer keinen gültigen Spielerplatz
    /// trägt — die dürfen in der Tafel nicht stillschweigend verschwinden.
    /// </summary>
    public int BeschussOhnePlatz { get; private set; }

    /// <summary>Zählt einen gefallenen Schuss. Wird aus <c>Fire</c> gerufen,
    /// NACHDEM feststeht, dass wirklich geschossen wird.</summary>
    private void BeschussZaehlen(Entity schuetze, Entity opfer)
    {
        if (schuetze.Owner is < 0 or > 7 || opfer.Owner is < 0 or > 7)
        { BeschussOhnePlatz++; return; }
        _beschuss[schuetze.Owner, opfer.Owner]++;
    }

    /// <summary>Zählt einen Schuss auf eine Zelle (Bodenangriff).</summary>
    private void BeschussZelleZaehlen() => BeschussZelle++;

    /// <summary>Die Tafel. Jede Zeile ein Schütze, jede Spalte ein Getroffener;
    /// ein Paar, das laut Diplomatie verbündet ist, wird mit ⚠ ausgeworfen.
    /// </summary>
    public string BeschussCheckLine()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("beschuss-check: wer schiesst auf wen (Zeile = Schuetze)\n");
        sb.Append(_haveAllies
            ? $"   Buendnismatrix geladen (Mission {_diploMission})\n"
            : "   ⚠ KEINE Buendnismatrix — es gilt »jeder Fremde ist Feind«\n");
        // ⭐ Die URSACHE, getrennt vom Symptom: der Riegel in AiSend faengt
        // einen Angriffsbefehl auf einen Verbuendeten ab, BEVOR ein Schuss
        // faellt. Steht hier 0, hat gar kein Rufer ein falsches Ziel
        // ausgesucht; steht hier eine Zahl, waeren ohne den Riegel genau so
        // viele Angriffsbefehle durchgegangen — und die Tafel darunter waere
        // rot. Ohne diese Zeile misst der Pruefstand nur, ob der letzte Riegel
        // haelt, und nicht, ob die Ursache behoben ist.
        sb.Append("   Angriffsbefehle auf einen Verbuendeten, in AiSend " +
                  $"abgewehrt: {AiSendFreundAbgewehrt}" +
                  (AiSendFreundAbgewehrt > 0
                      ? "   ⚠ ein Rufer sucht weiterhin falsche Ziele aus\n"
                      : "   (kein Rufer sucht ein falsches Ziel aus)\n"));

        int gesamt = 0, verbuendet = 0;
        var zeilen = new System.Text.StringBuilder();
        for (int a = 0; a < 8; a++)
            for (int b = 0; b < 8; b++)
            {
                int n = _beschuss[a, b];
                if (n == 0) continue;
                gesamt += n;
                bool freund = a != b && _haveAllies && _allied[a, b];
                bool selbst = a == b;
                if (freund || selbst) verbuendet += n;
                zeilen.Append($"   {(freund || selbst ? "⚠ " : "  ")}" +
                              $"P{a} -> P{b}: {n,5} Schuss" +
                              (selbst ? "   AUF SICH SELBST" :
                               freund ? "   AUF EINEN VERBUENDETEN" : "") + "\n");
            }

        if (gesamt == 0 && BeschussZelle == 0)
        {
            sb.Append("   kein Schuss gefallen — der Lauf war zu kurz oder zu ruhig, " +
                      "die Tafel sagt NICHTS aus\n");
            return sb.ToString();
        }

        sb.Append(zeilen);
        if (BeschussZelle > 0)
            sb.Append($"      auf eine ZELLE (Strg-Angriff/Wald): {BeschussZelle} Schuss\n");
        if (BeschussOhnePlatz > 0)
            sb.Append($"   ⚠ {BeschussOhnePlatz} Schuss ohne gueltigen Spielerplatz\n");

        sb.Append(verbuendet == 0
            ? $"   bestanden — {gesamt} Schuss, keiner davon auf einen Verbuendeten.\n"
            : $"   ⚠⚠ DURCHGEFALLEN — {verbuendet} von {gesamt} Schuss trafen einen " +
              "VERBUENDETEN oder den eigenen Platz.\n");
        return sb.ToString();
    }

    /// <summary>Rueckgabewert des Laufs: 0, wenn kein Verbuendeter beschossen
    /// wurde, sonst 1. Ein Lauf ohne jeden Schuss gibt 2 — »nichts gemessen«
    /// ist nicht dasselbe wie »bestanden«.</summary>
    public int BeschussCheckRc()
    {
        int gesamt = 0, verbuendet = 0;
        for (int a = 0; a < 8; a++)
            for (int b = 0; b < 8; b++)
            {
                int n = _beschuss[a, b];
                if (n == 0) continue;
                gesamt += n;
                if (a == b || (_haveAllies && _allied[a, b])) verbuendet += n;
            }
        if (gesamt == 0 && BeschussZelle == 0) return 2;
        return verbuendet == 0 ? 0 : 1;
    }
}
