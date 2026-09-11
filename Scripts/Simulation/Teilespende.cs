namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>TAKT 48 — DIE TEILESPENDE DER KI</b> (<c>0x4BE5C0</c>, gebaut 11.09.2026).
///
/// <para><b>Warum es sie braucht.</b> Seit dem 11.09. faehrt der Nahweg in der
/// Kampagne nicht mehr (<see cref="NahwegFaehrt"/>). Die KI hat auf 13 von 15
/// Kampagnenkarten Fabriken, aber weder Linie noch Wagen, und sie baut in der
/// Kampagne keine Wagen (bug-090). Ohne diesen Weg bekaeme ihre Basis nie wieder
/// Teile. Das Original hat dafuer genau diesen fahrzeuglosen Weg — und NUR fuer
/// die KI (berichte/nahweg-original.md, 3.3a).</para>
///
/// <para><b>Gelesen</b> (11.09. selbst gegengelesen, deckt sich mit dem Bericht
/// und OFFENE_FRAGEN AX.2):</para>
/// <code>
///   ai_tick 0x4BFB80:
///     0x4BFBA6  Bild % 50 == 48 ?  -> byte[0x538BC0]++ ; bei 150 -> 0
///     0x4BFBDD  sec53[40p] == 1 (Rechner), sonst nur mit byte[0x538BA8]
///     0x4BFBFA  byte[0xB38D38+p] == 0   (unsere Skriptsperre, AiGesperrt)
///     Takt 48 (Tafel 0x4BFE50) -> 0x4BE5C0(p)
///
///   0x4BE5C0(p):
///     0x4BE5DE  byte[0x538BC0] != 0 -> zurueck
///     Schleife 1 ueber 255 Gebaeude mit Besitzer p (Tafel 0x4BE6E0/0x4BE6F8):
///       Art 2 -> a += 0x50 · Art 3 -> b += 0x50 · Art 4 -> c += 0x50
///       Art 10, 15 -> d += 0x50                ; a..d sind BYTES (add al,0x50)
///     Schleife 2 (Tafel 0x4BE708/0x4BE71C):
///       Art 1, 9, 16 -> [+0x2C] += a ; [+0x2E] += b ; [+0x30] += c
///       Art 2, 3, 4  -> [+0x32] += 2·d
/// </code>
/// <para>Also: <b>80 Teile je Fabrik in jede Basis, jeden Flughafen, jede
/// Werft; 160 Terranium je Mine in jede Fabrik; alle 150·50 = 7500 Takte</b>
/// (150 s bei 50 Takten). ⚠ Der Bytesammler ist ein Fehler des Originals: vier
/// Fabriken einer Art geben 64 statt 320. Nachgebaut, abschaltbar mit
/// <c>--teilespende-wort</c>. Gegenschalter <c>--teilespende-aus</c>.</para>
///
/// <para>⚠ Nicht gebaut: <c>byte[0x538BA8]</c> (»KI uebernimmt leere Plaetze«,
/// dann auch fuer Menschenplaetze) — wann es gesetzt ist, ist ungelesen; bei uns
/// spendet nur, wer in der KI-Liste steht. Und Takt 14 (<c>0x4BFA30</c>,
/// Bahnhof → Basis) — auf den Kampagnenkarten 1–15 hat niemand einen Bahnhof.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary><c>--teilespende-aus</c> — der Stand vor dem 11.09.2026.</summary>
    public static bool TeilespendeAus;

    /// <summary><c>--teilespende-wort</c> — der Sammler ohne den Bytefehler.</summary>
    public static bool TeilespendeWort;

    /// <summary><c>byte[0x538BC0]</c> — zaehlt bei <c>Takt % 50 == 48</c> hoch
    /// und springt bei 150 auf 0.</summary>
    private int _spendeZaehler;

    /// <summary>Wie viele Spenden gezahlt wurden — fuer die Pruefzeile.</summary>
    public int SpendenGezahlt;

    /// <summary>Der 150er-Zaehler, einmal je ai_tick. Gibt zurueck, ob in
    /// diesem Takt gespendet wird.</summary>
    private bool TeilespendeZaehlen()
    {
        if (_taktNr % 50 != 48) return false;
        if (++_spendeZaehler >= 150) _spendeZaehler = 0;
        return _spendeZaehler == 0 && !TeilespendeAus;
    }

    private static int Sammeln(int wert) => TeilespendeWort ? wert : wert & 0xFF;

    private void Teilespende(int spieler)
    {
        int a = 0, b = 0, c = 0, d = 0;
        foreach (var e in _entities)
        {
            if (!e.IsBuilding || e.Dead || e.Owner != spieler) continue;
            switch (e.BType)
            {
                case 2: a = Sammeln(a + 0x50); break;
                case 3: b = Sammeln(b + 0x50); break;
                case 4: c = Sammeln(c + 0x50); break;
                case 10 or 15: d = Sammeln(d + 0x50); break;
            }
        }

        int vorher = 0, nachher = 0, empfaenger = 0;
        foreach (var e in _entities)
        {
            if (!e.IsBuilding || e.Dead || e.Owner != spieler) continue;
            switch (e.BType)
            {
                case 1 or 9 or 16:
                    vorher += e.StockW + e.StockF + e.StockS;
                    e.StockW += a; e.StockF += b; e.StockS += c;
                    nachher += e.StockW + e.StockF + e.StockS;
                    empfaenger++;
                    break;
                case 2 or 3 or 4:
                    vorher += e.StockT;
                    e.StockT += 2 * d;
                    nachher += e.StockT;
                    break;
            }
        }
        SpendenGezahlt++;
        _spendenIst[spieler] = (_taktNr, a, b, c, 2 * d, nachher - vorher, empfaenger);
        GD.Print($"Teilespende P{spieler} (Takt {_taktNr}): je Basis W+{a} F+{b} S+{c}, "
               + $"je Fabrik T+{2 * d}, {empfaenger} Empfaenger, zusammen +{nachher - vorher}");
    }

    private readonly Dictionary<int, (int Takt, int A, int B, int C, int T, int Summe, int Empf)> _spendenIst = new();

    // ================= der Pruefstand ==========================================

    private bool _spendeCheck;
    private int _spendeCheckTakt, _nahwegStart, _nahwegGesperrtStart;
    private readonly Dictionary<int, (int A, int B, int C, int T, int Soll, bool Gesperrt)> _spendeSoll = new();

    /// <summary>
    /// <c>--teilespende-check</c> — <b>spendet die KI nach Takt 48, und faehrt der
    /// Nahweg in der Kampagne nicht mehr?</b>
    ///
    /// <para>⚠ EINGRIFF: der Zaehler wird auf 149 gesetzt, damit die Spende beim
    /// naechsten <c>Takt % 50 == 48</c> faellt statt nach 7500 Takten. Das Soll
    /// wird HIER aus den Gebaeuden gezaehlt, das Ist schreibt die Spende selbst
    /// als Unterschied der Lagerfelder ihrer Empfaenger.</para>
    ///
    /// <para>Nullmodelle: <c>--teilespende-aus</c> (keine Spende) und
    /// <c>--nahweg</c> (in der Kampagne wird wieder gefahren).</para>
    /// </summary>
    public void TeilespendeCheckStart()
    {
        _spendeCheck = true;
        _spendeCheckTakt = _taktNr;
        _spendeZaehler = 149;
        SpendenGezahlt = 0;
        _spendenIst.Clear();
        _spendeSoll.Clear();
        _nahwegStart = _econMovedW + _econMovedF + _econMovedS + _econMovedT;
        _nahwegGesperrtStart = NahwegGesperrt;
        foreach (var ai in _ai)
        {
            int n2 = 0, n3 = 0, n4 = 0, nm = 0, basen = 0, fabriken = 0;
            foreach (var e in _entities)
            {
                if (!e.IsBuilding || e.Dead || e.Owner != ai.Player) continue;
                if (e.BType == 2) n2++;
                if (e.BType == 3) n3++;
                if (e.BType == 4) n4++;
                if (e.BType is 10 or 15) nm++;
                if (e.BType is 1 or 9 or 16) basen++;
                if (e.BType is 2 or 3 or 4) fabriken++;
            }
            int a = Sammeln(80 * n2), b = Sammeln(80 * n3), c = Sammeln(80 * n4), t = 2 * Sammeln(80 * nm);
            // ⚠ Sammeln(80·n) ist fuer das Byte dasselbe wie n-mal Sammeln(x+80): (80·n) mod 256
            _spendeSoll[ai.Player] = (a, b, c, t, basen * (a + b + c) + fabriken * t, AiGesperrt(ai.Player));
        }
        GD.Print($"teilespende-check: Eingriff bei Takt {_taktNr} — Zaehler auf 149, "
               + $"{_spendeSoll.Count} KI-Spieler");
    }

    public string TeilespendeCheckLine()
    {
        var sb = new System.Text.StringBuilder("teilespende-check\n");
        if (!_spendeCheck) return sb.Append("  nicht gestartet — der Lauf sagt NICHTS").ToString();
        bool ok = _spendeSoll.Count > 0;
        sb.Append($"  Eingriff bei Takt {_spendeCheckTakt}: Zaehler auf 149 statt 7500 Takte zu warten; "
                + $"jetzt Takt {_taktNr}, {SpendenGezahlt} Spenden\n");
        foreach (var (p, soll) in _spendeSoll)
        {
            sb.Append($"  P{p}: Soll je Basis W+{soll.A} F+{soll.B} S+{soll.C}, je Fabrik T+{soll.T}, "
                    + $"zusammen +{soll.Soll}");
            if (soll.Gesperrt)
            {
                bool still = !_spendenIst.ContainsKey(p);
                ok &= still;
                sb.Append($" — vom Skript gesperrt, {(still ? "keine Spende, richtig" : "GESPENDET, falsch")}\n");
                continue;
            }
            if (!_spendenIst.TryGetValue(p, out var ist))
            {
                ok = false;
                sb.Append(" — KEINE SPENDE\n");
                continue;
            }
            bool treffer = ist.A == soll.A && ist.B == soll.B && ist.C == soll.C && ist.T == soll.T
                           && ist.Summe == soll.Soll && ist.Takt % 50 == 48 && ist.Takt > _spendeCheckTakt;
            ok &= treffer;
            sb.Append($"\n      Ist bei Takt {ist.Takt} (% 50 = {ist.Takt % 50}): W+{ist.A} F+{ist.B} S+{ist.C} "
                    + $"T+{ist.T}, Lagerfelder +{ist.Summe} auf {ist.Empf} Empfaenger  {(treffer ? "ja" : "NEIN")}\n");
        }

        int gefahren = _econMovedW + _econMovedF + _econMovedS + _econMovedT - _nahwegStart;
        int gesperrt = NahwegGesperrt - _nahwegGesperrtStart;
        bool kampagne = UI.SkirmishSetup.CampaignMission > 0;
        sb.Append($"  Nahweg ({(kampagne ? "Kampagne" : "Gefecht")}, faehrt: {NahwegFaehrt}): "
                + $"gefahren {gefahren} Teile, {gesperrt}x gesperrt\n");
        if (kampagne)
        {
            ok &= gefahren == 0;
            if (gesperrt == 0 && !NahwegFaehrt)
                sb.Append("  ⚠ nie ein Lager am gesperrten Nahweg — dieser Teil sagt NICHTS\n");
            ok &= gesperrt > 0;
        }
        sb.Append($"  Gegenschalter --teilespende-aus: {TeilespendeAus}, --teilespende-wort: {TeilespendeWort}, "
                + $"--nahweg: {NahwegKampagne}, --nahweg-aus: {NahwegAus}\n");
        sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
