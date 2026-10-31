namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <b>--fluglage-check</b> (30.10.2026, bug-696, <c>berichte/bau-fluglage.md</c>) — siehe
/// <see cref="FlugPunkt(Entity)"/> und den Kopf von <c>Simulation/Fluglage.cs</c>.
///
/// <para>Ein Probehubschrauber fliegt auf einer Zeile <c>r</c> geradeaus ueber die
/// unruhigste Strecke der Karte (meiste Hoehenstufen und Schraegen auf 24 Zellen), durch
/// den echten Flugtakt (<see cref="UpdateAircraft"/>, Spielerziel wie ein Flugbefehl).
/// Weil die Strecke waagerecht liegt, ist die WAHRE flache Lage in jedem Bildschritt
/// bekannt, unabhaengig von der Fassung: <c>(Pos.X, _oy + r·20 + 10)</c>. Daraus die
/// Sollwerte nach dem Original:</para>
/// <list type="bullet">
/// <item><b>Lage</b>: Zelle = <c>(⌊(x − _ox)/40⌋, r)</c> (Listenbauer 0x42E340: Zelle ohne
/// Gelaende).</item>
/// <item><b>Rumpf</b>: <c>y − alt</c> (0x42B8BD), Abweichung &lt; 1 px.</item>
/// <item><b>Schatten</b>: <c>y − 0x4B5CE0(Zelle, FeinX, 20 − FeinY/2)</c> (0x42D72B),
/// Abweichung &lt; 1 px.</item>
/// <item><b>kein Huepfen</b>: bei gleicher <c>alt</c> springt der Rumpf in keinem Schritt um
/// 5 px oder mehr.</item>
/// <item><b>Klickkasten</b>: <see cref="AirRect"/> steht mittig auf dem Rumpf, und
/// <see cref="PickAir"/> am Rumpfpunkt trifft die Probe.</item>
/// </list>
/// <para>Nullmodell <c>--fluglage-alt</c>: die Zelle liegt (_oy − Anhebung)/20 Zeilen zu weit
/// suedlich, der Schatten folgt nicht dem Boden → DURCHGEFALLEN. <c>--flughuepfen-alt</c>:
/// dazu springt der Rumpf an jeder Zellgrenze.</para>
/// <para>⚠ EINGRIFF: ein Probehubschrauber Art 10 des Betrachters (Platz &gt; alle), Huelle
/// 100000, ohne Munition, Hoehe/Soll fest 135. Er bleibt nach der Probe stehen — der Lauf
/// endet danach.</para>
/// </summary>
public partial class MapEntityLayer
{
    public string FluglageCheck()
    {
        var sb = new System.Text.StringBuilder("fluglage-check"
            + (FluglageAlt ? " — NULLMODELL --fluglage-alt" : "")
            + (FlughuepfenAlt ? " — NULLMODELL --flughuepfen-alt" : "")
            + (FlughoeheBildAlt ? " — --flughoehe-bild-alt" : "") + "\n");
        if (_nav == null) return sb.Append("  KEIN URTEIL — keine Karte").ToString();

        // 1. die unruhigste waagerechte Strecke (24 Zellen)
        const int Laenge = 24;
        int bestR = -1, bestC = -1, bestScore = -1;
        for (int r = 4; r < _nav.Height - 4; r++)
            for (int c0 = 3; c0 + Laenge < _nav.Width - 3; c0++)
            {
                int score = 0;
                for (int c = c0 + 1; c <= c0 + Laenge; c++)
                    score += (ElevOf(c, r) != ElevOf(c - 1, r) ? 2 : 0) + (HangArt(c, r) != 0 ? 1 : 0);
                if (score > bestScore) { bestScore = score; bestR = r; bestC = c0; }
            }
        if (bestR < 0 || bestScore <= 0) return sb.Append("  KEIN URTEIL — keine Strecke mit Hoehenunterschied").ToString();
        int emin = int.MaxValue, emax = int.MinValue, stufen = 0, hang = 0;
        for (int c = bestC; c <= bestC + Laenge; c++)
        {
            int e = ElevOf(c, bestR);
            emin = Mathf.Min(emin, e); emax = Mathf.Max(emax, e);
            if (c > bestC && e != ElevOf(c - 1, bestR)) stufen++;
            if (HangArt(c, bestR) != 0) hang++;
        }
        sb.AppendLine($"  Strecke Zeile {bestR}, Spalten {bestC}..{bestC + Laenge}: Gelaende {emin}..{emax}, "
                    + $"{stufen} Hoehenstufen, {hang} Schraegenzellen; _ox {_ox}, _oy {_oy}");

        // 2. der Probehubschrauber (⚠ EINGRIFF)
        int slot = 900;
        foreach (var x in _special) slot = Mathf.Max(slot, x.Slot + 1);
        var a = new Special
        {
            Slot = slot, Kind = 10, Name = "Probeheli", TypeName = "Probeheli",
            Owner = ViewPlayer, Stored = false, HomeSlot = -1,
            Col = bestC, Row = bestR, Pos = FlugZelle(bestC, bestR),
            Hp = 100000, HpMax = 100000, Attack = 0, Ammo = 0, AmmoMax = 0,
            Fuel = 99999, FuelMax = 99999, Speed = 5, Stufe = 5, Alt = 135, Sollhoehe = 135, Order = 1,
        };
        _special.Add(a);
        int idx = _special.Count - 1;
        var ziel = FlugZelle(bestC + Laenge, bestR);
        a.PlayerGoal = ziel; a.Goal = ziel;
        sb.AppendLine($"  ⚠ EINGRIFF: Probeheli Platz {slot} (Art 10, Spieler {ViewPlayer}, Hoehe 135) von Spalte {bestC} nach {bestC + Laenge}");

        // 3. fliegen und je Schritt messen
        float yZeile = _oy + bestR * TileH + TileH / 2f;    // die wahre flache Lage, y
        int schritte = 0, zellFalsch = 0, rowVersatzMin = int.MaxValue, rowVersatzMax = int.MinValue;
        int huepfer = 0, huepfSchritte = 0, pickFalsch = 0, kastenFalsch = 0;
        float rumpfMax = 0f, schattenMax = 0f, schattenMaxHang = 0f;
        float? letzteRumpfY = null; int letzteAlt = a.Alt;
        int hubMin = int.MaxValue, hubMax = int.MinValue;
        for (int t = 0; t < 4000 && a.PlayerGoal != null && !a.Dead; t++)
        {
            a.Hp = a.HpMax; a.Fuel = a.FuelMax; a.Sollhoehe = 135;
            UpdateAircraft(SimDt);
            if (a.Dead || a.Stored) break;
            schritte++;
            int sc = Mathf.FloorToInt((a.Pos.X - _ox) / TileW);
            int fx = Mathf.Clamp(Mathf.FloorToInt(a.Pos.X - _ox) - sc * TileW, 0, TileW - 1);
            int dRow = a.Row - bestR;
            rowVersatzMin = Mathf.Min(rowVersatzMin, dRow); rowVersatzMax = Mathf.Max(rowVersatzMax, dRow);
            if (a.Col != sc || a.Row != bestR) zellFalsch++;
            // Rumpf: y − alt
            var rumpf = AirRumpf(a);
            rumpfMax = Mathf.Max(rumpfMax, Mathf.Abs(rumpf.Y - (yZeile - a.Alt)));
            // Schatten: y − Bodenanhebung (FeinY/2 = 10 auf der Zeilenmitte -> Argument 20 − 10)
            int hub = HubOf(sc, bestR, fx, TileH - TileH / 2);
            hubMin = Mathf.Min(hubMin, hub); hubMax = Mathf.Max(hubMax, hub);
            float ds = Mathf.Abs(AirSchatten(a).Y - (yZeile - hub));
            schattenMax = Mathf.Max(schattenMax, ds);
            if (HangArt(sc, bestR) != 0) schattenMaxHang = Mathf.Max(schattenMaxHang, ds);
            // Huepfen: Rumpf-y bei gleicher Hoehe
            if (letzteRumpfY is { } ly && a.Alt == letzteAlt)
            {
                huepfSchritte++;
                if (Mathf.Abs(rumpf.Y - ly) >= 5f) huepfer++;
            }
            letzteRumpfY = rumpf.Y; letzteAlt = a.Alt;
            // Klickkasten
            // bug-717: der Kasten des Originals (0x431F7F) sitzt an der Zellecke, nicht mittig
            bool alterKasten = FliegerKlickkastenAlt || !LageFlach;
            var kasten = alterKasten ? AirRect(a) : AirKlickkasten(a);
            if ((alterKasten && kasten.GetCenter().DistanceTo(rumpf) > 0.5f) || !kasten.HasPoint(rumpf)) kastenFalsch++;
            if (PickAir(rumpf) != idx) pickFalsch++;
        }
        bool angekommen = a.PlayerGoal == null;
        if (schritte == 0) return sb.Append("  KEIN URTEIL — die Probe hat sich nicht bewegt").ToString();

        bool lageOk = zellFalsch == 0, rumpfOk = rumpfMax < 1f, schattenOk = schattenMax < 1f,
             huepfOk = huepfer == 0 && huepfSchritte > 0, kastenOk = kastenFalsch == 0 && pickFalsch == 0;
        sb.AppendLine($"  {schritte} Bildschritte, {(angekommen ? "angekommen" : "⚠ NICHT angekommen")}, "
                    + $"Bodenanhebung unterwegs {hubMin}..{hubMax} px");
        sb.AppendLine($"  Lage (0x42E340, Zelle ohne Gelaende): {zellFalsch} von {schritte} Schritten in falscher Zelle, "
                    + $"Zeilenversatz {rowVersatzMin}..{rowVersatzMax} -> {(lageOk ? "ja" : "NEIN")}");
        sb.AppendLine($"  Rumpf (y − alt, 0x42B8BD): groesste Abweichung {rumpfMax:0.00} px -> {(rumpfOk ? "ja" : "NEIN")}");
        sb.AppendLine($"  Schatten (y − 0x4B5CE0, 0x42D72B): groesste Abweichung {schattenMax:0.00} px "
                    + $"(auf Schraegen {schattenMaxHang:0.00}) -> {(schattenOk ? "ja" : "NEIN")}");
        sb.AppendLine($"  Huepfen: {huepfer} Spruenge >= 5 px in {huepfSchritte} Schritten bei gleicher Hoehe -> {(huepfOk ? "ja" : "NEIN")}");
        sb.AppendLine($"  Klickkasten: {kastenFalsch} Schritte Kasten ohne Rumpfpunkt (alter Kasten: nicht mittig), {pickFalsch} Schritte PickAir verfehlt -> {(kastenOk ? "ja" : "NEIN")}");
        sb.AppendLine("  " + FlughuepfenLine());
        _special.RemoveAt(idx);                        // die Probe wieder heraus
        if (_selAir == idx) _selAir = -1;
        sb.Append(lageOk && rumpfOk && schattenOk && huepfOk && kastenOk && angekommen ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
