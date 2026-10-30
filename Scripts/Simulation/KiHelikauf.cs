namespace AkteEuropaReborn.Rendering;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

/// <summary>
/// ⭐ <b>KI-TAKT 10: DER VERSORGUNGSHELIKAUF</b> (30.10.2026, bug-675,
/// Spielerentscheidung »ja«). Lesung: berichte/ki-flugzeugkauf-opus.md §1,
/// Bau: berichte/bau-helikauf-0-6-12.md.
///
/// <para><b>Das Tor</b> (KI-Runde C <c>0x4BFB80</c>, Takt%50 = 10 → <c>0x4BFD2F</c>,
/// selbst nachgelesen):</para>
/// <code>
///   0x4BFD33  cl = byte[0x538BD8+p] (sec61 Betriebsart) ; == 10 -> raus
///   0x4BFD43  call 0x40222A (-> 0x4BAB40, test of life)
///   0x4BFD4B  Typbyte == 0xFF (ausgeschieden)           -> raus
///   0x4BFD59  cl = byte[0x538BB8+p] (sec131, set_ai)    ; == 0  -> raus
///   0x4BFD68  call 0x401339 (-> 0x4BE330(p))
/// </code>
/// <para><b>Der Rumpf <c>0x4BE330(p)</c></b> (C; F <c>0x4BDDF0</c> Befehl für Befehl
/// gleich bis auf Registerwahl, alle Konstanten 0x3E8/0xC8/0xD/0xE/0xFF/0x186A0/0x14/0x13
/// gleich, Gebäudetafel F <c>0xC05974</c>, Raster F <c>0xB3C3F0</c>):</para>
/// <code>
///   0x4BE34B  N = #{ i in p*1000 .. p*1000+999 : sec18[i].+0x09 != 0xFF und .+0x0A != 1 }
///             (78-B-Satz ab 0x6E26C8: +0x09 Besitzer/belegt, +0x0A Klassenbyte, 1 = Infanterie)
///   0x4BE3A3  T13/T14 = #{ sec19 (200 x 68 B): +0x01 == p und +0x00 == 13 bzw. 14 }
///   0x4BE3F4  Posten = alle g in 0..254 mit Typ (+0x04, 0xC06914) == 14  — KEINE Besitzerprüfung
///   0x4BE41F  kein Posten -> fertig
///   0x4BE44C  b = Posten mit kleinstem word[+4] - word[+0] im Satz
///             0xB3D390 + 12*(121p + 11*(x/24) + y/24) ; Feind - Eigen, Start 100000,
///             nur echt kleiner gewinnt (jle @0x4BE4BE) -> bei Gleichstand der ERSTE
///   0x4BE4D3  if (N/20 + 1 > T14)  0x4B2FB0(b, 1, p)   ; Munitionsheli
///   0x4BE502  if (N/19 + 1 > T13)  0x4B2FB0(b, 0, p)   ; Treibstoffheli
/// </code>
/// <para><b>Der Spawn <c>0x4B2FB0(posten, f, p)</c></b>: erster freier sec19-Satz (+0 == 0,
/// 200 Plätze), sonst nichts. Bezahlt wird NUR bei Typbyte 0 (Mensch, @0x4B2FF9) — der
/// Rechner zahlt nichts. Vorlage <c>20p + f + 5</c> (Tafel 0x51B030, 48 B), Art aus
/// Vorlage +0x1E. Ort (Posten.x + 4, Posten.y + 1) (@0x4B3089/0x4B3093). Danach sofort
/// die Kundensuche (f=0 <c>0x40185C</c>, f=1 <c>0x40178F</c>), sonst Heimkehr. Kein
/// Zähler sec128 (kein <c>inc</c> auf eine Globale im ganzen Rumpf).</para>
///
/// <para>Gegenschalter <c>--ki-helikauf-aus</c> (Stand vor dem 30.10.2026: Takt 10 fehlte,
/// sec131 wurde nur protokolliert). Prüfstand <c>--ki-helikauf-check[=modus]</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--ki-helikauf-aus</c> — der Stand vor bug-675: kein KI-Takt 10.</summary>
    public static bool KiHelikaufAus;

    /// <summary><c>--ki-flughafenheli-alt</c> — der Stand vor bug-675: die KI kauft auch in
    /// der KAMPAGNE am Flughafen Versorgungshelis für Geld (unsere Gefechtszutat in
    /// <c>AiProduce</c>). Im Gefecht bleibt dieser Kauf ohnehin.</summary>
    public static bool KiFlughafenheliAlt;

    /// <summary>Zähler für Prüfstand und Zeile.</summary>
    public int KiHelikaufRufe, KiHelikaufKaeufe, KiHelikaufVorlageErsatz;
    /// <summary>Wie oft der Geldkauf am Flughafen in der Kampagne gesperrt wurde bzw. lief.</summary>
    public int KiFlughafenheliGesperrt, KiFlughafenheliKaeufe;

    private readonly List<string> _kiHelikaufLog = new();
    private bool _kiHelikaufProtokoll;

    /// <summary>Die Zählung N von <c>0x4BE34B</c>. ⚠ UNSERE SETZUNG an zwei Stellen:
    /// (a) das Original zählt belegte Sätze des Spielerblocks — auch tote, solange der
    /// Satz noch nicht freigegeben ist (<c>ukol</c> 100 ohne Freigabe, @0x40B5A0); wir
    /// zählen lebende Einheiten mit Besitzer p. (b) Einheiten in Depot/Garage, die bei
    /// uns keine Kartenentität sind, fehlen.</summary>
    private int KiHelikaufN(int p)
    {
        int n = 0;
        foreach (var e in _entities)
            if (!e.IsBuilding && !e.IsProp && !e.Dead && e.Owner == p && e.GameUnitType != 1) n++;
        return n;
    }

    private int KiHelikaufT(int p, int art)
    {
        int n = 0;
        foreach (var s in _special) if (!s.Dead && s.Owner == p && s.Kind == art) n++;
        return n;
    }

    /// <summary>Takt 10 für einen KI-Spieler — Tor <c>0x4BFD2F</c> und Rumpf <c>0x4BE330</c>.
    /// Skriptsperre (<c>0x4BFBFA</c>) und »lebt« prüft der Aufrufer in der KI-Schleife.</summary>
    private void KiHelikauf(int p)
    {
        if (KiHelikaufAus || p is < 0 or > 7) return;
        // ⚠ sec61 == 10: SetAiBetriebsart klemmt auf 0..7, eine 10 aus dem Skript steht
        // bei uns als 7. Das Skript setzt 6..9 nie (SkirmishAiSectors.cs), also ist 7 hier
        // genau »10 im Original«. UNSERE SETZUNG: die Gleichsetzung 7 == 10.
        if (_aiSec61[p] >= 7) return;                                   // 0x4BFD3C
        if (_aiSec131[p] == 0) return;                                  // 0x4BFD61
        KiHelikaufRufe++;
        int n = KiHelikaufN(p);
        if (n > _kiHeliNMax[p]) _kiHeliNMax[p] = n;                     // nur fuer den Pruefstand
        int t13 = KiHelikaufT(p, 13), t14 = KiHelikaufT(p, 14);
        // die Posten in Satzreihenfolge 0..254, Typ != 0 heisst bei uns !Dead
        var posten = _entities.Where(e => e.IsBuilding && !e.Dead && e.BType == SupplyDepotType
                                          && e.Slot is >= 0 and < 255)
                              .OrderBy(e => e.Slot).ToList();
        if (posten.Count == 0) return;                                  // 0x4BE41F
        Entity? b = null;
        int best = 100000;                                              // 0x4BE425
        foreach (var g in posten)
        {
            var (sx, sy) = AiSektorVon(g.Col, g.Row);
            var s = _aiRaster[p][SektorIndex(sx, sy)];
            int wert = (s.Feind & 0xFFFF) - (s.Eigen & 0xFFFF);         // movzx-Woerter
            if (best > wert) { best = wert; b = g; }                    // jle @0x4BE4BE
        }
        if (b == null) return;   // im Original unmoeglich (Werte < 100000); Schutz
        if (n / 20 + 1 > t14) KiHeliSpawn(b, 1, p, n, t13, t14, best); // 0x4BE4FA
        if (n / 19 + 1 > t13) KiHeliSpawn(b, 0, p, n, t13, t14, best); // 0x4BE529
    }

    /// <summary>Das Gegenstück zu <c>0x4B2FB0(posten, f, p)</c> für den Rechner: ohne Geld,
    /// ohne Flughafen, ohne Lager.</summary>
    private void KiHeliSpawn(Entity posten, int f, int p, int n, int t13, int t14, int wert)
    {
        // erster freier sec19-Satz: 200 Plaetze (0x4B2FD3 cmp bl,0xC8)
        if (_special.Count(s => !s.Dead) >= 200) return;
        if (_airDesigns == null || _airDesigns.Count == 0) FillCampaignAirDesigns();
        if (_airDesigns == null) return;
        int art = 13 + f;
        var eigene = _airDesigns.Where(d => d.Player == p).ToList();
        // Vorlage 20p + f + 5 (0x4B312F); unsere Liste je Spieler steht in Vorlagenordnung.
        AirDesign? d = eigene.Count > f + 5 ? eigene[f + 5] : null;
        if (d == null || d.Kind != art)
        {
            // ⚠ UNSERE SETZUNG: passt die Vorlage f+5 nicht zur Art, die 0x4BE330 zaehlt,
            // nehmen wir die erste Vorlage dieser Art. Gezaehlt, damit es auffaellt.
            d = eigene.FirstOrDefault(x => x.Kind == art);
            KiHelikaufVorlageErsatz++;
            if (d == null) return;
        }
        int slot = 0;
        foreach (var s in _special) slot = Mathf.Max(slot, s.Slot + 1);
        int col = posten.Col + 4, row = posten.Row + 1;                 // 0x4B3089 / 0x4B3093
        var pos = CellCenter(col, row);
        var a = new Special
        {
            Slot = slot, Kind = d.Kind, Name = d.Name, TypeName = d.Name,
            Col = col, Row = row, Stored = false, Owner = p,
            // ⚠ UNSERE SETZUNG: Heimat = der Posten. Die Heimkehr der Arten 13/14
            // (0x42636E) sucht ohnehin ein beliebiges eigenes Gebaeude (AirHeadHome).
            HomeSlot = posten.Slot, Pos = pos,
            Footprint = new Rect2(pos.X - TileW / 2f, pos.Y - TileH / 2f, TileW, TileH),
            Speed = d.Speed, Hp = d.Hp, HpMax = d.Hp,
            StufeUnten = SpUntenVorlage(d.Kind),
            Ammo = d.Ammo, AmmoMax = d.Ammo, Fuel = d.Fuel, FuelMax = d.Fuel,
            Payload = d.Payload, Airframe = d.Airframe,
            Attack = d.Attack, Defence = d.Defence, Sight = d.Sight,
            // ⚠ UNSERE SETZUNG: voll beladen wie an jedem anderen Kaufweg; die Ladung
            // (+0x9B..) setzt 0x4B2FB0 auf 0, was die Ladung bei uns bedeutet, ist offen.
            Cargo = SupplyCargoFull,
        };
        _special.Add(a);
        KiHelikaufKaeufe++;
        // Kein _builtCount: 0x4B2FB0 zaehlt sec128 nicht.
        string z = $"KI-Helikauf Takt {_taktNr} P{p}: N {n}, T13 {t13}, T14 {t14} -> {d.Name} (Art {d.Kind}) " +
                   $"am Posten {posten.Slot} (Besitzer {posten.Owner}, Sektorwert {wert}) bei ({col},{row})";
        _kiHelikaufLog.Add($"{p};{d.Kind};{posten.Slot};{a.Slot}");
        if (_kiHelikaufProtokoll || _kiHelikaufLog.Count <= 16) GD.Print(z);
    }

    // ---- Spielstand -----------------------------------------------------------
    // Der Helikauf selbst traegt keinen Zustand (er zaehlt jedes Mal neu, die Phase
    // kommt aus dem Takt). Aber sein Tor sec131 speichert das Original mit
    // (Schreiber 0x41DD66, Leser 0x41EDF7) — siehe KiZustandSchreiben/-Lesen.
    private bool _sec131Geladen;
    /// <summary>Das groesste N je Spieler seit Pruefstandsbeginn (kein Spielzustand).</summary>
    private readonly int[] _kiHeliNMax = new int[8];

    // ---- Pruefstand -----------------------------------------------------------

    /// <summary>
    /// <c>--ki-helikauf-check[=modus]</c> auf <c>-- --campaign=27</c>. Modi:
    /// <c>standard</c> (Vorgabe), <c>sec131null</c> (sec131 aller Spieler := 0),
    /// <c>postentot</c> (alle Gebäude Typ 14 zerstört), <c>fabriken</c> (P2s Fabriken
    /// Platz 12/13/14 zerstört, danach Flugzeuge 1..12 aus dem Programm), <c>geld</c> (P2 bekommt
    /// Geld; der Geldkauf am Flughafen muss in der Kampagne gesperrt bleiben).
    /// </summary>
    public string KiHelikaufCheck(string modus, int sekunden)
    {
        int m = UI.SkirmishSetup.CampaignMission;
        var sb = new StringBuilder($"ki-helikauf-check K{m} Modus {modus}, --ki-helikauf-aus {KiHelikaufAus}, " +
                                   $"--ki-flughafenheli-alt {KiFlughafenheliAlt}\n");
        if (!InCampaign || m <= 0) return sb.Append("ki-helikauf-check: KEIN URTEIL (nur mit -- --campaign=N)").ToString();
        MissionScriptTick(0.001f);   // Setup (set_ai) sicher gelaufen
        if (modus == "sec131null") Array.Clear(_aiSec131);
        if (modus == "postentot")
            for (int i = 0; i < _entities.Count; i++)
                if (_entities[i].IsBuilding && !_entities[i].Dead && _entities[i].BType == SupplyDepotType)
                    Kill(i, _entities[i], -1, "Pruefstand ki-helikauf postentot");
        if (modus == "fabriken")
            for (int i = 0; i < _entities.Count; i++)
                if (_entities[i].IsBuilding && !_entities[i].Dead && _entities[i].Owner == 2 && _entities[i].Slot is 12 or 13 or 14)
                {
                    sb.AppendLine($"  zerstoert: P2 Platz {_entities[i].Slot} Typ {_entities[i].BType}");
                    Kill(i, _entities[i], -1, "Pruefstand ki-helikauf fabriken");
                }
        // Modus geld: P2 (Flughafen Platz 11) bekommt Geld, damit der Geldkauf am Flughafen
        // (AiProduce, unsere Gefechtszutat) ueberhaupt in Versuchung kommt.
        // ⚠ EINGRIFF des Pruefstands.
        if (modus == "geld") { _money[2] += 10 * HeliPrice; sb.AppendLine($"  ⚠ EINGRIFF: P2 Geld +{10 * HeliPrice}"); }
        var ki = _ai.Select(a => a.Player).Distinct().OrderBy(x => x).ToList();
        var geldVor = (int[])_money.Clone();
        var nVor = ki.ToDictionary(p => p, KiHelikaufN);
        Array.Clear(_kiHeliNMax);
        int postenZahl = _entities.Count(e => e.IsBuilding && !e.Dead && e.BType == SupplyDepotType);
        var postenSlots = _entities.Where(e => e.IsBuilding && !e.Dead && e.BType == SupplyDepotType)
                                   .Select(e => $"{e.Slot}:{e.Owner}");
        sb.AppendLine($"  KI-Spieler {string.Join(",", ki)}; sec131 {string.Join(",", _aiSec131)}; sec61 {string.Join(",", _aiSec61)}");
        sb.AppendLine($"  Posten Typ 14: {postenZahl} ({string.Join(" ", postenSlots)})");
        int flugVor = _special.Count(s => !s.Dead && s.Kind is >= 1 and <= 12 && s.Owner == 2);
        int fbVor = KiFlughafenheliKaeufe;
        _kiHelikaufProtokoll = true;
        int ersterFlieger = -1;
        int takte = sekunden * SimHz;
        for (int t = 0; t < takte; t++)
        {
            SimTickFuerProbe();
            if (ersterFlieger < 0 && _special.Count(s => !s.Dead && s.Kind is >= 1 and <= 12 && s.Owner == 2) > flugVor)
                ersterFlieger = t;
        }
        _kiHelikaufProtokoll = false;
        var postenSet = _entities.Where(e => e.IsBuilding && e.BType == SupplyDepotType).Select(e => e.Slot).ToHashSet();
        int gesamt = 0, falsch = 0, ortFalsch = 0, geldFalsch = 0;
        foreach (int p in ki)
        {
            int n = KiHelikaufN(p);
            int t13 = KiHelikaufT(p, 13), t14 = KiHelikaufT(p, 14);
            bool tor = _aiSec131[p] != 0 && _aiSec61[p] < 7 && postenZahl > 0 && !KiHelikaufAus;
            // Soll: je Runde hoechstens einer, nie einer zurueck — nach >= 3 Runden liegt T
            // zwischen der Formel am Ende (N jetzt) und der Formel beim groessten N des Laufs.
            int nMax = Math.Max(n, _kiHeliNMax[p]);
            int s13 = tor ? n / 19 + 1 : 0, s14 = tor ? n / 20 + 1 : 0;
            int s13o = tor ? nMax / 19 + 1 : 0, s14o = tor ? nMax / 20 + 1 : 0;
            var meine = _kiHelikaufLog.Select(l => l.Split(';')).Where(x => x[0] == p.ToString()).ToList();
            int amPosten = meine.Count(x => postenSet.Contains(int.Parse(x[2])));
            ortFalsch += meine.Count - amPosten;
            gesamt += meine.Count;
            bool ok = t13 >= s13 && t13 <= s13o && t14 >= s14 && t14 <= s14o;
            if (!ok) falsch++;
            if (_money[p] != geldVor[p]) geldFalsch++;
            string band13 = s13o == s13 ? $"{s13}" : $"{s13}..{s13o}", band14 = s14o == s14 ? $"{s14}" : $"{s14}..{s14o}";
            sb.AppendLine($"  P{p}: N {nVor[p]}->{n} (max {nMax}), Treibstoff {t13} (Soll N/19+1 = {band13}), Munition {t14} (Soll N/20+1 = {band14}), " +
                          $"gekauft {meine.Count}, davon am Posten Typ 14 {amPosten}, Geld {geldVor[p]}->{_money[p]} {(ok ? "ok" : "⚠ ABWEICHUNG")}");
        }
        int fbNeu = KiFlughafenheliKaeufe - fbVor;
        int flugNach = _special.Count(s => !s.Dead && s.Kind is >= 1 and <= 12 && s.Owner == 2);
        sb.AppendLine($"  Laufzeit {sekunden} s bis Takt {_taktNr}; Rufe {KiHelikaufRufe}, Kaeufe {KiHelikaufKaeufe}, " +
                      $"Vorlage ersetzt {KiHelikaufVorlageErsatz}; Geldkauf am Flughafen: {fbNeu} (gesperrt {KiFlughafenheliGesperrt})");
        sb.AppendLine($"  P2 Flugzeuge Art 1..12: {flugVor} -> {flugNach}" +
                      (ersterFlieger >= 0 ? $", erstes nach {ersterFlieger / (float)SimHz:0.00} s" : ", keines"));
        string urteil;
        if (modus == "geld")
            urteil = fbNeu == 0 && KiFlughafenheliGesperrt > 0 ? $"BESTANDEN (Geldkauf {KiFlughafenheliGesperrt}x gesperrt)"
                   : fbNeu > 0 ? $"DURCHGEFALLEN ({fbNeu} Geldkaeufe am Flughafen)" : "KEIN URTEIL (Geldkauf nie versucht)";
        else if (modus == "fabriken")
            urteil = flugNach > flugVor ? "BESTANDEN (P2 baut nach Wegfall der Fabriken)" : "DURCHGEFALLEN (kein Flugzeug)";
        else
        {
            bool erwartetNull = KiHelikaufAus || modus is "sec131null" or "postentot";
            bool mech = falsch == 0 && ortFalsch == 0 && geldFalsch == 0 && fbNeu == 0;
            urteil = !mech ? $"DURCHGEFALLEN (Formel {falsch}, Ort {ortFalsch}, Geld {geldFalsch}, Flughafen {fbNeu})"
                   : gesamt == 0 ? (erwartetNull ? "DURCHGEFALLEN (0 Helis — Nullmodell greift)" : "DURCHGEFALLEN (0 Helis)")
                   : "BESTANDEN";
        }
        sb.Append($"ki-helikauf-check K{m} {modus}: {gesamt} Helis, {urteil}");
        return sb.ToString();
    }
}
