namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <b><c>--kartenschirm-probe</c> — geht der Kartenschirm auf, und trifft sein
/// Klick die Zelle, die darunter liegt?</b> (20.09.2026, zu
/// <see cref="UI.KartenschirmView"/>.)
///
/// <para>⚠⚠ <b>Die Do-Not-Repeat vom 19.09.:</b> ein Prüfstand mit eigener
/// Formel prüft sich selbst. Deshalb steht hier <b>genau eine</b> eigene
/// Rechnung, und zwar bewusst: die Fenstergrösse wird in der
/// <b>Ganzzahlform des Originals</b> nachgerechnet (<c>(s·W + 19) / 20 + 2</c>,
/// @0x444E24 <c>add eax, 0x13</c> und @0x444E28 <c>idiv 20</c>), während
/// <see cref="UI.KartenschirmView.KachelMass"/> mit <c>CeilToInt</c> rechnet.
/// <b>Zwei verschieden geschriebene Formeln, die dasselbe ergeben müssen</b> —
/// das ist eine Prüfung. Alles andere (Versatz, Maßstab, Zellrechnung) fragt
/// den Zeichner und rechnet NICHT nach.</para>
///
/// <para>⚠ <b>Der Fall, der trennen muss:</b> die Rundung. Eine Karte, deren
/// Breite glatt durch 20 teilbar ist, gibt bei Aufrunden und Abschneiden
/// dasselbe — dort wäre die Formel nicht zu prüfen. Der Prüfstand rechnet
/// deshalb zusätzlich <b>drei erfundene Kartengrössen</b> durch, darunter
/// <c>W = 41</c> (krumm) und <c>W = 40</c> (glatt), und meldet beide.</para>
///
/// <para>Und der eigentliche Beweis ist der <b>Rundlauf</b>: Zelle → Bildpunkt
/// → Zelle. Wer nur die Fenstergrösse prüft, hat den Klick nicht geprüft —
/// und der Klick ist das, wofür es dieses Fenster gibt.</para>
/// </summary>
public partial class MapViewer : Node2D
{
    private string KartenschirmProbeLauf()
    {
        var sb = new System.Text.StringBuilder("kartenschirm-probe\n");
        if (_kartenschirm == null || _entities == null)
            return sb.Append("  kein Fenster — nicht stellbar").ToString();
        if (MapEntityLayer.ZielwahlMinimap)
            return sb.Append("  --zielwahl-minimap: der Kartenschirm bleibt zu "
                           + "(das ist das Nullmodell, und NICHTS ist sein Soll)").ToString();

        var zellen = _entities.MapZellSize();
        if (zellen.X <= 0) return sb.Append("  keine Karte — nicht stellbar").ToString();

        bool alles = true;

        // --- 1. DIE FENSTERGROESSE, zwei Formeln gegeneinander ---------------
        sb.Append($"  1. Groesse (Karte {zellen.X}x{zellen.Y} Zellen):\n");
        foreach (var fall in new[] { zellen, new Vector2I(41, 41),
                                     new Vector2I(40, 40), new Vector2I(1, 1) })
        {
            for (int z = 0; z < UI.KartenschirmView.Zoomtafel.Length; z++)
            {
                var k = UI.KartenschirmView.KachelMass(fall, z);
                var soll = OriginalKachelMass(fall, z);
                bool ok = k == soll;
                alles &= ok;
                if (!ok || fall == zellen)
                    sb.Append($"     {fall.X}x{fall.Y} Zellen, Zoom {z} "
                            + $"({UI.KartenschirmView.Zoomtafel[z]} px/Zelle): "
                            + $"{k.X}x{k.Y} Kacheln, Original-Rechnung {soll.X}x{soll.Y} "
                            + $"{Ja(ok)}\n");
            }
        }

        // --- 2. AUFGEHEN -----------------------------------------------------
        int aufVor = KartenschirmAufgegangen;
        KartenschirmAuf(new Vector2(60, 60), 0, 7);
        bool offen = _kartenschirm.Visible && KartenschirmAufgegangen == aufVor + 1;
        bool massOk = _kartenschirm.Massstab == UI.KartenschirmView.Zoomtafel[0];
        sb.Append($"  2. Aufgegangen {Ja(offen)}, Titel »{UI.KartenschirmView.Titel}«, "
                + $"Zoom {_kartenschirm.Zoomindex} = {_kartenschirm.Massstab} px/Zelle "
                + $"{Ja(massOk)}, Flughafen {_kartenschirm.Flughafen}\n");
        alles &= offen && massOk;

        // --- 3. DER RUNDLAUF: Zelle -> Bildpunkt -> Zelle --------------------
        // ⚠ DAS ist die eigentliche Pruefung. Die Ecken sind dabei die
        // interessanten Faelle: bei (0,0) und (W-1,H-1) faellt ein Fehler im
        // Versatz oder im Abschneiden sofort auf, in der Mitte nicht.
        alles &= Rundlauf(sb, zellen, "3.");

        // --- 4. NEBEN DIE KARTE GEKLICKT -> NICHTS ---------------------------
        // Das Original tut dann nichts (0x41D1D0 liefert 0). Der Rand IST der
        // Fall: eine halbe Kachel links vom Bild liegt im Fenster, aber neben
        // der Karte.
        var daneben = _kartenschirm.Bildversatz - new Vector2(4, 4);
        var ausserhalb = _kartenschirm.ZelleUnter(daneben);
        bool nixOk = ausserhalb == null;
        sb.Append($"  4. Klick auf den Rand (neben der Karte): "
                + (ausserhalb is { } q ? $"Zelle ({q.X},{q.Y}) — FALSCH" : "keine Zelle")
                + $" {Ja(nixOk)}\n");
        alles &= nixOk;

        // --- 5. ZOOM ---------------------------------------------------------
        var vorher = _kartenschirm.Size;
        // ⚠ ueber den ECHTEN Knopfweg, nicht ueber KartenschirmAuf: sonst
        // pruefte der Pruefstand einen Weg, den kein Spieler nimmt — und genau
        // an diesem Unterschied hing der Fehler, den die Buchfuehrung fand.
        _kartenschirm.OnZoom?.Invoke(1);
        bool zoomOk = _kartenschirm.Massstab == UI.KartenschirmView.Zoomtafel[1]
                      && _kartenschirm.Size.X > vorher.X;
        sb.Append($"  5. Zoom + : {UI.KartenschirmView.Zoomtafel[0]} -> "
                + $"{_kartenschirm.Massstab} px/Zelle, Fenster {vorher.X} -> "
                + $"{_kartenschirm.Size.X} px breit {Ja(zoomOk)}\n");
        alles &= zoomOk;
        // …und der Rundlauf muss auch im groesseren Massstab stimmen.
        alles &= Rundlauf(sb, zellen, "6.");

        // --- 7. ZUGEHEN ------------------------------------------------------
        int zuVor = KartenschirmGeschlossen;
        KartenschirmZu();
        bool zuOk = !_kartenschirm.Visible && KartenschirmGeschlossen == zuVor + 1;
        sb.Append($"  7. Zugegangen {Ja(zuOk)}\n");
        alles &= zuOk;

        bool buch = KartenschirmAufgegangen == KartenschirmGeschlossen;
        sb.Append($"  Buchfuehrung: {KartenschirmAufgegangen}x auf = "
                + $"{KartenschirmGeschlossen}x zu {Ja(buch)}\n");
        alles &= buch;

        sb.Append(alles ? "  -> BESTANDEN" : "  -> DURCHGEFALLEN");
        return sb.ToString();
    }

    /// <summary>Zelle → Bildpunkt → Zelle, für vier Ecken und die Mitte.</summary>
    private bool Rundlauf(System.Text.StringBuilder sb, Vector2I zellen, string nr)
    {
        if (_kartenschirm == null) return false;
        int s = _kartenschirm.Massstab * UI.KartenschirmView.Scale;
        var faelle = new[]
        {
            new Vector2I(0, 0), new Vector2I(zellen.X - 1, 0),
            new Vector2I(0, zellen.Y - 1), new Vector2I(zellen.X - 1, zellen.Y - 1),
            new Vector2I(zellen.X / 2, zellen.Y / 2),
        };
        int gut = 0;
        string schlecht = "";
        foreach (var c in faelle)
        {
            // Die MITTE der Zelle im Fenster — der Versatz kommt vom Zeichner.
            var punkt = _kartenschirm.Bildversatz + new Vector2(c.X * s + s / 2f,
                                                                c.Y * s + s / 2f);
            var zurueck = _kartenschirm.ZelleUnter(punkt);
            if (zurueck == c) gut++;
            else schlecht += $" ({c.X},{c.Y})->"
                           + (zurueck is { } r ? $"({r.X},{r.Y})" : "nichts");
        }
        bool ok = gut == faelle.Length;
        sb.Append($"  {nr} Rundlauf Zelle->Punkt->Zelle bei {_kartenschirm.Massstab} "
                + $"px/Zelle: {gut} von {faelle.Length}{schlecht} {Ja(ok)}\n");
        return ok;
    }

    /// <summary>
    /// Die Fenstergrösse in der <b>Ganzzahlform des Originals</b> —
    /// @0x444E21..0x444E2A: <c>eax = s·W; eax += 0x13; eax /= 20</c>, dann
    /// <c>+2</c> Kacheln (@0x444EED <c>add ax, 2</c>).
    ///
    /// <para>⚠ Absichtlich <b>anders geschrieben</b> als
    /// <see cref="UI.KartenschirmView.KachelMass"/>, damit die zwei sich
    /// gegenseitig prüfen können statt sich zu bestätigen.</para></summary>
    private static Vector2I OriginalKachelMass(Vector2I zellen, int zoomindex)
    {
        int s = UI.KartenschirmView.Zoomtafel[
            Mathf.Clamp(zoomindex, 0, UI.KartenschirmView.Zoomtafel.Length - 1)];
        return new Vector2I((s * zellen.X + 0x13) / 20 + 2,
                            (s * zellen.Y + 0x13) / 20 + 2);
    }

    private static string Ja(bool b) => b ? "ja" : "NEIN";
}
