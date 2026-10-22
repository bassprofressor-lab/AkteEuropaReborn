namespace AkteEuropaReborn.Rendering;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

/// <summary>
/// ⭐ <b>KI-TAKT 14: DIE BAHNHOFLEERUNG</b> (22.10.2026, Paket W4, bug-637).
///
/// <para>Die KI-Runde <c>ai_tick</c> C <c>0x4BFB80</c> verteilt ihre Arbeit nach
/// <c>Takt % 50</c> über die Indextafel <c>0x4BFEA4</c> und die Sprungtafel
/// <c>0x4BFE50</c>: Platz 14 → Index 9 → <c>0x4BFD90 push ebx / call 0x4014D3</c>
/// (Thunk → <c>0x4BFA30</c>). Davor dieselben Tore wie für jeden KI-Zug: Kopf
/// <c>sec53[40p] == 1</c> (<c>0x4BFBDD</c>) und Skriptsperre sec106 == 0
/// (<c>0x4BFBFA</c>); KEIN sec61-Tor. Gelesen in C <c>0x4BFA30</c> und F
/// <c>0x4BF4E0</c> (Befehl für Befehl gleich, Gebäudetafel F <c>0xC05970</c>):</para>
/// <code>
///   0x4BFA34  call 0x4013ED (-> 0x4C5B30, Netzwuerfel)
///   0x4BFA39  ax % 10 (div cx, 16 Bit) ; != 0 -> raus
///   0x4BFA4C  for g = 0..254: Besitzer (+0x05) == p:
///               Typ (+0x04) == 1 -> Ziel := g, SCHLUSS (die erste Basis)
///               Typ == 9         -> Ziel := g, weiter  (sonst der LETZTE Flughafen)
///   0x4BFA91  kein Ziel -> raus
///   0x4BFA9A  for g = 0..254: Besitzer == p und Typ 6 oder 12 (Bahnhof, Feldbahnhof):
///               Ziel.F (+0x2E) += g.F ; Ziel.W (+0x2C) += g.W ; Ziel.S (+0x30) += g.S
///               g.F := g.W := g.S := 0           ; Terranium (+0x32) bleibt
/// </code>
/// <para>⚠ <b>UNSERE SETZUNG</b>: unser Würfel ist nicht der Generator
/// <c>0x4C5B30</c> des Originals; <c>Roll(10) == 0</c> trifft mit derselben
/// Wahrscheinlichkeit 1/10, aber nicht in denselben Takten. Die Lagerwörter laufen
/// im Original bei 65536 über (<c>add word</c>); unsere Lager sind int — bei
/// Kampagnenmengen ohne Folge.</para>
///
/// <para>Gegenschalter <c>--bahnhofleerung-aus</c> (Stand davor: der Takt fehlte,
/// Teilespende.cs nannte ihn fälschlich »auf den Kampagnenkarten ohne Wirkung«).
/// ⚠ Der Wurf geschieht in JEDEM Takt 14 je KI-Spieler, auch ohne Bahnhof — er
/// verschiebt den Würfelstrom, also auch Gefechtsprüfsummen.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--bahnhofleerung-aus</c> — der Stand vor Paket W4.
    /// ⚠⚠ VORERST STANDARD AUS (= true), Einschalter <c>--bahnhofleerung</c>
    /// (22.10.2026, Paket W4): die Lesung ist eindeutig, aber die unabhängige Gegenprobe
    /// widerspricht ihr. Mit <c>--bahnschalter-aus-datei</c> bildet unser K25-Lauf die
    /// Entwicklerstände NUR OHNE Takt 14 nach (Basis-S 0, Feldbahnhof 67 S 285, Wagen
    /// [3,3,3,3], Freigabe 0 in 4/4 — BESTAETIGT); mit Takt 14 wird der Feldbahnhof
    /// geleert und die Gegenprobe fällt (Wagen [5,10,10,10]). In 5→10→3→7.DM wächst
    /// Feldbahnhof 67 aber 285→360→435 und wird in 11 800 Takten nie geleert, obwohl
    /// Takt 14 bei 1/10 je 50 Takte rund 24-mal hätte treffen müssen. Entweder ein Tor
    /// ist ungelesen, oder die Stände kommen aus einer Entwicklerfassung ohne Takt 14 —
    /// bis das geklärt ist, bleibt er aus.</summary>
    public static bool BahnhofleerungAus = true;

    /// <summary>Umbuchungen (je Bahnhof) und die bewegte Menge — für die Prüfzeile.</summary>
    public int BahnhofleerungFaelle, BahnhofleerungUmbuchungen, BahnhofleerungWuerfe;
    public long BahnhofleerungMenge;
    private int _bhlMechanikFehler;

    /// <summary>Takt 14 für einen KI-Spieler — der Nachbau von <c>0x4BFA30</c>.</summary>
    private void Bahnhofleerung(int p)
    {
        if (BahnhofleerungAus) return;
        BahnhofleerungWuerfe++;
        if (Simulation.Determinism.Roll(10) != 0) return;          // 0x4BFA39
        var platz = new SortedDictionary<int, Entity>();
        foreach (var e in _entities)
            if (e.IsBuilding && !e.Dead && e.BType != 0 && e.Slot is >= 0 and < 255 && !platz.ContainsKey(e.Slot))
                platz[e.Slot] = e;
        Entity? ziel = null;
        foreach (var (_, e) in platz)
        {
            if (e.Owner != p) continue;
            if (e.BType == 1) { ziel = e; break; }                    // die erste Basis
            if (e.BType == 9) ziel = e;                               // sonst der letzte Flughafen
        }
        if (ziel == null) return;                                     // 0x4BFA94
        BahnhofleerungFaelle++;
        foreach (var (_, g) in platz)
        {
            if (g.Owner != p || g.BType is not (6 or 12)) continue;
            int w = g.StockW, f = g.StockF, s = g.StockS;
            int vorZiel = ziel.StockW + ziel.StockF + ziel.StockS;
            ziel.StockF += f; ziel.StockW += w; ziel.StockS += s;
            g.StockF = 0; g.StockW = 0; g.StockS = 0;
            if (ziel.StockW + ziel.StockF + ziel.StockS - vorZiel != w + f + s) _bhlMechanikFehler++;
            if (w + f + s > 0)
            {
                BahnhofleerungUmbuchungen++;
                BahnhofleerungMenge += w + f + s;
                if (_bhlProtokoll)
                    GD.Print($"Bahnhofleerung Takt {_taktNr} P{p}: Bahnhof {g.Slot} (Art {g.BType}) W{w} F{f} S{s} " +
                             $"-> {(ziel.BType == 1 ? "Basis" : "Flughafen")} {ziel.Slot}, dort jetzt {ziel.StockW}/{ziel.StockF}/{ziel.StockS}");
            }
        }
    }

    private bool _bhlProtokoll;

    /// <summary>
    /// <c>--bahnhofleerung-check[=takte]</c> (Standard 13 300 Originaltakte, der Takt
    /// von 7.DM) auf <c>-- --campaign=25</c>: Zahl der Würfe, der gelungenen Fälle
    /// (Basis gefunden), der Umbuchungen mit Inhalt und der bewegten Menge; dazu die
    /// Mechanik je Umbuchung (Basis + Bahnhof vorher == Basis nachher, Bahnhof 0).
    /// Erwartung: P2 hat in K25 vier Feldbahnhöfe → Umbuchungen > 0.
    /// Nullmodell <c>--bahnhofleerung-aus</c>: 0 Würfe, 0 Umbuchungen → DURCHGEFALLEN.
    /// </summary>
    public string BahnhofleerungCheck(int takte)
    {
        int m = UI.SkirmishSetup.CampaignMission;
        var sb = new StringBuilder($"bahnhofleerung-check K{m}, --bahnhofleerung-aus {BahnhofleerungAus}\n");
        if (!InCampaign || m <= 0) return sb.Append("bahnhofleerung-check: KEIN URTEIL (nur mit -- --campaign=N)").ToString();
        _bhlProtokoll = true;
        MissionScriptTick(0.001f);
        var bahnhoefe = _entities.Where(e => e.IsBuilding && !e.Dead && e.BType is 6 or 12 && e.Owner is >= 0 and <= 7)
                                 .GroupBy(e => e.Owner).Select(g => $"P{g.Key}:{g.Count()}");
        sb.AppendLine($"  Bahnhoefe je Spieler: {string.Join(" ", bahnhoefe)}");
        int wache = 0;
        var uhr = System.Diagnostics.Stopwatch.StartNew();
        while (_origTicks < takte && wache++ < 3 * takte) SimTickFuerProbe();
        _bhlProtokoll = false;
        string basis = string.Join(" ", _entities.Where(b => b.IsBuilding && !b.Dead && b.Owner == 2 && b.BType is 1 or 12)
                                                .Select(b => $"{(b.BType == 1 ? "Basis" : "FB")}{b.Slot} {b.StockW}/{b.StockF}/{b.StockS}"));
        sb.AppendLine($"  Lauf {uhr.ElapsedMilliseconds / 1000} s bis Takt {_origTicks}; P2 am Ende: {basis}");
        string urteil = BahnhofleerungUmbuchungen > 0 && _bhlMechanikFehler == 0 ? "BESTANDEN"
                      : _bhlMechanikFehler > 0 ? "DURCHGEFALLEN (Mechanik)" : "DURCHGEFALLEN (keine Umbuchung)";
        sb.Append($"bahnhofleerung-check K{m}: Wuerfe {BahnhofleerungWuerfe}, Faelle {BahnhofleerungFaelle} " +
                  $"(Soll ~Wuerfe/10), Umbuchungen {BahnhofleerungUmbuchungen}, Menge {BahnhofleerungMenge}, " +
                  $"Mechanik {_bhlMechanikFehler} Fehler — {urteil}");
        return sb.ToString();
    }
}
