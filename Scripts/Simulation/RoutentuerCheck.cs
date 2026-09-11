namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>--routentuer-check — kommt ein Wagen, der beim START auf Tuer 0 steht,
/// wieder los?</b> (11.09.2026, seine Meldung aus Kampagne 7: »diesmal stand der
/// transporter still und die tür von der fabrik war immer wie leicht geöffnet«).
///
/// <para><b>Der Verdacht, gelesen:</b> der Tuerarm des Originals
/// (<c>0x43D48F..0x43D57C</c>) prueft einen angemeldeten Wagen (UKOL 48) nur bei
/// Torzustand 1 und kennt drei Faelle — Fahrziel == dieses Gebaeude → 54;
/// Fahrziel == 0xFF → Fahrziel := dieses Gebaeude; sonst nichts. UKOL 48 hat im
/// Auftragsband KEINEN Arm (Index 21 → 0x409EEE, der Leerarm). Wir setzten bei
/// leerem Fahrziel UKOL 0 zurueck — und der Tuertakt (vor dem Routentakt) meldet
/// den Wagen im naechsten Takt wieder an. »Start« (0x410870) leert das Fahrziel.</para>
///
/// <para><b>Faelle</b> (je ein Fabrik-Basis-Paar, beide dem Spieler gegeben):
/// A der Wagen steht beim Start AUF Tuer 0 der Fabrik; B der Wagen steht vier
/// Zellen davor (Kontrolle). Soll fuer beide: eingefahren (UKOL 54) binnen 400
/// Takten, geladen &gt; 0, kein Pendeln 48↔0; nach &gt;= 1500 Takten auch abgeladen.</para>
/// <para>⚠ EINGRIFFE: Besitzer von Fabrik, Basis und Wagen; ein Fahrzeug wird zum
/// Transporter (+0x0E := 0x47, Waffe 0, Sprit 999); Lager der Fabrik W/F/S := 40.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    private bool _rtCheckAn;
    private int _rtStartTakt;

    private sealed class RtFall
    {
        public char Name;
        public string Titel = "", Notiz = "";
        public int Wagen = -1, Fabrik = -1, Basis = -1;
        public int Pendeln, Takte48, Eingefahren = -1, LetzteUkol = -1, StartC, StartR, Weiteste;
    }

    private readonly List<RtFall> _rtFaelle = new();

    public void RoutentuerCheckStart()
    {
        if (_nav == null) return;
        _rtCheckAn = true;
        _rtStartTakt = _taktNr;
        var benutzt = new HashSet<int>();

        // Paare Fabrik -> naechste Basis, die engsten zuerst
        var paare = new List<(int F, int B, int D)>();
        for (int fi = 0; fi < _entities.Count; fi++)
        {
            var f = _entities[fi];
            if (!f.IsBuilding || f.IsProp || f.Dead || f.BType is not (2 or 3 or 4) || f.DoorCells.Count == 0) continue;
            int best = -1, bestD = int.MaxValue;
            for (int bi = 0; bi < _entities.Count; bi++)
            {
                var b = _entities[bi];
                if (!b.IsBuilding || b.IsProp || b.Dead || b.BType != 1) continue;
                int dd = Mathf.Max(Mathf.Abs(b.Col - f.Col), Mathf.Abs(b.Row - f.Row));
                if (dd < bestD) { bestD = dd; best = bi; }
            }
            if (best >= 0 && bestD <= 50) paare.Add((fi, best, bestD));
        }
        paare.Sort((x, y) => x.D.CompareTo(y.D));

        int Fahrzeug()
        {
            for (int k = 0; k < _entities.Count; k++)
            {
                var x = _entities[k];
                if (benutzt.Contains(k) || x.Dead || x.IsProp || x.IsBuilding || !x.Mobile || x.Infantry >= 0
                    || x.GameUnitType != 0 || x.Ukol >= 50 || Untergestellt(x)) continue;
                benutzt.Add(k);
                return k;
            }
            return -1;
        }

        var genommen = new HashSet<int>();
        RtFall Fall(char name, string titel, bool aufTuer)
        {
            var c = new RtFall { Name = name, Titel = titel };
            _rtFaelle.Add(c);
            int p = paare.FindIndex(x => !genommen.Contains(x.F) && !genommen.Contains(x.B));
            if (p < 0) { c.Notiz = "kein Fabrik-Basis-Paar"; return c; }
            genommen.Add(paare[p].F); genommen.Add(paare[p].B);
            c.Fabrik = paare[p].F; c.Basis = paare[p].B;
            c.Wagen = Fahrzeug();
            if (c.Wagen < 0) { c.Notiz = "kein Fahrzeug"; return c; }
            var f = _entities[c.Fabrik];
            var b = _entities[c.Basis];
            var u = _entities[c.Wagen];
            f.Owner = ViewPlayer; b.Owner = ViewPlayer; u.Owner = ViewPlayer;
            f.StockW = System.Math.Max(f.StockW, 40); f.StockF = System.Math.Max(f.StockF, 40); f.StockS = System.Math.Max(f.StockS, 40);
            u.Part = TransporterTeil; u.Weapon = 0; u.FuelMax = System.Math.Max(u.FuelMax, 999); u.Fuel = u.FuelMax;
            u.Target = -1; u.Path = null; u.Orders.Clear(); u.Ordered = false; u.Ukol = UkolFrei;
            _sel.Remove(c.Wagen);
            var tuer = new Vector2I(f.Col + f.DoorCells[0].Col, f.Row + f.DoorCells[0].Row);
            var zelle = aufTuer ? tuer : (_nav.NearestFree(tuer + new Vector2I(0, 4), u.Move, c.Wagen) ?? tuer + new Vector2I(0, 4));
            if (aufTuer && _nav.OccupantAt(tuer.X, tuer.Y) >= 0) c.Notiz = $"⚠ Tuer 0 ({tuer.X},{tuer.Y}) war belegt; ";
            ProbeVersetzen(c.Wagen, zelle.X, zelle.Y);
            var r = RouteVon(u) ?? RouteAnlegen(u);
            if (r == null) { c.Notiz += "kein Umschlagsatz"; c.Wagen = -1; return c; }
            r.Ziel = b.Slot;
            r.Quelle[0] = f.Slot; r.Quelle[1] = r.Quelle[2] = r.Quelle[3] = -1;
            RouteStarten(u, r);
            c.StartC = u.Col; c.StartR = u.Row;
            c.Notiz += $"Fabrik Platz {f.Slot} Art {f.BType} ({f.Col},{f.Row}) Tuer 0 ({tuer.X},{tuer.Y}), "
                     + $"Basis Platz {b.Slot} ({b.Col},{b.Row}), Abstand {paare[p].D}, Wagen auf ({u.Col},{u.Row})";
            return c;
        }

        Fall('A', "Start AUF Tuer 0 der Fabrik", true);
        Fall('B', "Start vier Zellen davor    ", false);
        foreach (var c in _rtFaelle)
            GD.Print($"routentuer-check: {c.Name} {c.Titel.Trim()} — Wagen {c.Wagen}: {c.Notiz}");
    }

    /// <summary>Jeden Takt nach dem Routentakt.</summary>
    private void RoutentuerCheckTakt()
    {
        foreach (var c in _rtFaelle)
        {
            if (c.Wagen < 0) continue;
            var u = _entities[c.Wagen];
            if (c.LetzteUkol == UkolAngemeldet && u.Ukol == UkolFrei) c.Pendeln++;
            if (u.Ukol == UkolAngemeldet) c.Takte48++;
            if (u.Ukol == UkolImGebaeude && c.Eingefahren < 0) c.Eingefahren = _taktNr - _rtStartTakt;
            c.Weiteste = Mathf.Max(c.Weiteste, Mathf.Max(Mathf.Abs(u.Col - c.StartC), Mathf.Abs(u.Row - c.StartR)));
            c.LetzteUkol = u.Ukol;
        }
    }

    public string RoutentuerCheckLine()
    {
        var sb = new System.Text.StringBuilder("routentuer-check\n");
        if (!_rtCheckAn) return sb.Append("  nicht gestartet — der Lauf sagt NICHTS").ToString();
        int takte = _taktNr - _rtStartTakt;
        bool ok = _rtFaelle.Count > 0;
        foreach (var c in _rtFaelle)
        {
            if (c.Wagen < 0)
            {
                ok = false;
                sb.Append($"  {c.Name} {c.Titel}: ⚠ nicht herstellbar ({c.Notiz}) — sagt NICHTS\n");
                continue;
            }
            var u = _entities[c.Wagen];
            var r = RouteVon(u);
            int geladen = r?.GeladenGesamt ?? 0, abgeladen = r?.AbgeladenGesamt ?? 0;
            bool soll = c.Eingefahren is >= 0 and <= 400 && geladen > 0 && c.Pendeln < 20
                        && (takte < 1500 || abgeladen > 0);
            ok &= soll;
            sb.Append($"  {c.Name} {c.Titel}: eingefahren {(c.Eingefahren >= 0 ? $"nach {c.Eingefahren} Takten" : "NIE")}, "
                    + $"Pendeln 48->0 {c.Pendeln}x, {c.Takte48} Takte auf 48, geladen {geladen}, abgeladen {abgeladen}, "
                    + $"weitester Abstand vom Start {c.Weiteste}, jetzt ({u.Col},{u.Row}) UKOL {u.Ukol} "
                    + $"Fahrziel {(r?.Fahrziel ?? -1)}  {(soll ? "ja" : "NEIN")}\n");
        }
        sb.Append($"  nach {takte} Takten; Fahrziel nachgetragen {RouteTuerNachgetragen}x, "
                + $"angemeldeter Wagen auf 0 zurueckgesetzt {RouteAnmeldungZurueck}x; "
                + $"Gegenschalter --routentuer-alt: {RoutentuerAlt}\n");
        sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }

    /// <summary><c>--routentuer-alt</c> — der Stand bis zum 11.09.2026: ein
    /// angemeldeter Wagen ohne Fahrziel faellt auf UKOL 0 zurueck (und wird vom
    /// Tuertakt sofort wieder angemeldet).</summary>
    public static bool RoutentuerAlt;
}
