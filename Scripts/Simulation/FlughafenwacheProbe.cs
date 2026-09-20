namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <b><c>--flughafenwache-probe</c> — verwertet Recycle richtig, und steigt die
/// Wache wirklich auf?</b> (20.09.2026, zu <see cref="MapEntityLayer.Wache"/>.)
///
/// <para>⚠⚠ <b>Die Do-Not-Repeat vom 20.09.:</b> zwei Schwellen, die fast
/// dieselbe Zahl sind, SIND ein Fall. Das Original nimmt <c>d² &lt; 3601</c> an
/// und verwirft danach <c>min ≥ 3600</c>; wirksam ist <c>d² ≤ 3599</c>. Diese
/// Probe stellt darum <b>drei Abstände nebeneinander</b>:</para>
/// <list type="bullet">
///   <item><b>59 Zellen auf einer Achse</b> (d² = 3481) — muss starten.</item>
///   <item><b>60 Zellen auf einer Achse</b> (d² = 3600) — darf <b>NICHT</b>
///   starten. Das ist der Fall, an dem sich »Radius 60« und die gelesene
///   Schwelle unterscheiden, und ohne ihn wäre die Lesung nicht geprüft.</item>
///   <item><b>61 Zellen</b> (d² = 3721) — darf nicht starten.</item>
/// </list>
///
/// <para>⚠ Und die zweite vom 19.09.: der Prüfstand rechnet die Schwelle
/// <b>nicht selbst</b> nach, er stellt Fälle und zählt, wer aufsteigt.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    public string FlughafenwacheProbe()
    {
        var sb = new System.Text.StringBuilder("flughafenwache-probe\n");
        var feld = _entities.Find(x => x.IsBuilding && !x.Dead && x.BType == 9);
        if (feld == null) return sb.Append("  kein Flughafen — nicht stellbar").ToString();
        if (_nav == null) return sb.Append("  keine Karte — nicht stellbar").ToString();

        bool alles = true;

        // ---- Kulisse: ein Jagdflieger im Hangar und ein Feind in der Luft --
        // ⚠ ANGESAGT, damit niemand die Zahlen fuer die eines echten Spiels haelt.
        int jaeger = ProbeMaschine(feld, art: 1, stored: true, owner: feld.Owner);
        int feind = ProbeMaschine(feld, art: 1, stored: false,
                                  owner: feld.Owner == 0 ? 1 : 0);
        sb.Append($"  0. ⚠ KULISSE: Flughafen {feld.Slot} auf ({feld.Col},{feld.Row}), "
                + $"ein Jagdflieger im Hangar (Platz {_special[jaeger].Slot}) und ein "
                + $"fremdes Flugzeug in der Luft. Gewaehlte Werte, nicht gelesen.\n");

        // ---- 1. RECYCLE ----------------------------------------------------
        var r = _special[jaeger];
        var ent = EntwurfVon(r);
        r.Hp = r.HpMax / 2;                      // halb zerschossen
        int w0 = feld.StockW, f0 = feld.StockF, s0 = feld.StockS;
        int vorher = RecycleVerwertet;
        bool getan = RecycleAmFlughafen(feld.Slot, r.Slot);
        int dW = feld.StockW - w0, dF = feld.StockF - f0, dS = feld.StockS - s0;
        if (RecycleAus)
        {
            bool ok = !getan && RecycleVerwertet == vorher && dW == 0 && dF == 0 && dS == 0;
            sb.Append($"  1. Recycle: [--recycle-aus] nichts geschehen {Ja(ok)}\n");
            alles &= ok;
            // Ohne Recycle steht der Jaeger noch -- gut, die Wache braucht ihn.
        }
        else if (ent == null)
        {
            sb.Append("  1. Recycle: kein Entwurf zu dieser Art — NICHT GEMESSEN\n");
        }
        else
        {
            // ⚠ Das SOLL ist hier von Hand gerechnet, und das ist Absicht: es ist
            // die gelesene Formel (Kosten * pct / 100), anders geschrieben als
            // der Code sie rechnet. Zwei Formulierungen, die sich pruefen.
            int pct = 100 * r.Hp / r.HpMax;
            int sw = ent.CostW * pct / 100, sf = ent.CostF * pct / 100,
                ss = ent.CostS * pct / 100;
            bool weg = r.Dead && !(feld.Hangar?.Contains(r.Slot) ?? false);
            bool ok = getan && dW == sw && dF == sf && dS == ss && weg;
            sb.Append($"  1. Recycle bei {pct}%: Lager +W{dW} +F{dF} +S{dS} "
                    + $"(Soll +W{sw} +F{sf} +S{ss} aus {ent.CostW}/{ent.CostF}/{ent.CostS}), "
                    + $"Maschine weg {Ja(weg)} {Ja(ok)}\n");
            alles &= ok;
            // Fuer die Wache braucht es wieder einen bereiten Jaeger.
            jaeger = ProbeMaschine(feld, art: 1, stored: true, owner: feld.Owner);
        }

        // ---- 2. DIE WACHE: der Grenzfall ----------------------------------
        var f = _special[feind];
        feld.Patrouille = true;
        alles &= WacheFall(sb, feld, f, jaeger, 59, 0, true,  "2a", "59 Zellen (d² 3481)");
        alles &= WacheFall(sb, feld, f, jaeger, 60, 0, false, "2b",
                           "60 Zellen (d² 3600) — DER TRENNFALL");
        alles &= WacheFall(sb, feld, f, jaeger, 61, 0, false, "2c", "61 Zellen (d² 3721)");
        alles &= WacheFall(sb, feld, f, jaeger, 42, 42, true, "2d",
                           "42/42 schraeg (d² 3528) — muss starten");

        // ---- 3. EIN VERBUENDETER LOCKT NICHT ------------------------------
        int alt = f.Owner;
        f.Owner = feld.Owner;
        alles &= WacheFall(sb, feld, f, jaeger, 10, 0, false, "3",
                           "eigenes Flugzeug 10 Zellen weg — darf nicht");
        f.Owner = alt;

        // ---- 4. EIN EINGELAGERTES ZIEL LOCKT NICHT ------------------------
        f.Stored = true;
        alles &= WacheFall(sb, feld, f, jaeger, 10, 0, false, "4",
                           "Feind steht im HANGAR (uk 0) — darf nicht");
        f.Stored = false;

        // ---- 5. KONTROLLZAHL ----------------------------------------------
        int enden = WacheOhneJaeger + WacheOhneZiel + WacheAuftraege;
        bool buch = enden == WacheLaeufe;
        sb.Append($"  5. Buchfuehrung: {WacheLaeufe} Laeufe = {WacheOhneJaeger} ohne "
                + $"Jaeger + {WacheOhneZiel} ohne Ziel + {WacheAuftraege} Auftraege "
                + $"{Ja(buch)}\n");
        alles &= buch;

        feld.Patrouille = false;
        sb.Append($"  Gestellt: {WacheGestellt} Faelle, {WacheNichtStellbar} von der "
                + "Karte nicht hergegeben"
                + (WacheNichtStellbar > 0
                    ? " ⚠ — darunter moeglicherweise der TRENNFALL"
                    : "") + "\n");
        sb.Append(alles && WacheNichtStellbar == 0
                    ? "  -> BESTANDEN"
                    : alles ? "  -> BESTANDEN, ABER UNVOLLSTAENDIG"
                            : "  -> DURCHGEFALLEN");
        return sb.ToString();
    }

    /// <summary>Einen Fall stellen: Feind auf Abstand setzen, den Jäger wieder
    /// einlagern, einen Wachetakt auslösen und zählen, ob gestartet wurde.</summary>
    private bool WacheFall(System.Text.StringBuilder sb, Entity feld, Special f,
                           int jaeger, int dc, int dr, bool soll, string nr, string wort)
        // ⚠ `soll` wird unten unter dem Nullmodell auf false gezogen.
    {
        // Den Jaeger zurueck in den Hangar — jeder Fall beginnt gleich.
        var j = _special[jaeger];
        j.Stored = true; j.Dead = false; j.Absturz = false;
        j.Col = feld.Col; j.Row = feld.Row; j.Pos = feld.Pos;
        j.Target = -1; j.Goal = null; j.PlayerGoal = null; j.Angriffsauftrag = false;
        feld.Hangar ??= new System.Collections.Generic.List<int>();
        if (!feld.Hangar.Contains(j.Slot)) feld.Hangar.Add(j.Slot);

        // ⚠⚠ 20.09.2026 — HIER WURDE GEKLEMMT, UND DER PRUEFSTAND MASS DEN
        // FALSCHEN FALL. Der Flughafen auf K20 liegt bei Spalte 212 einer 254
        // breiten Karte; +59, +60 und +61 landeten alle drei auf 253, also bei
        // d² = 1681. Die Zeile sagte das ehrlich (»tatsaechlich d² =«), aber
        // GESTELLT war der Fall nicht — und ein Fall, den man nicht stellen
        // kann, ist kein bestandener Fall.
        //
        // Jetzt: erst in die eine Richtung, sonst in die andere. Passt keine,
        // heisst es NICHT STELLBAR, statt eine Zahl zu melden, die etwas
        // anderes misst.
        if (!Stelle(feld.Col, dc, _nav!.Width, out int zc) ||
            !Stelle(feld.Row, dr, _nav.Height, out int zr))
        {
            sb.Append($"  {nr}. {wort}: ⚠ AUF DIESER KARTE NICHT STELLBAR "
                    + $"(Flughafen {feld.Col},{feld.Row} auf {_nav.Width}x{_nav.Height}) "
                    + "— NICHT GEMESSEN\n");
            WacheNichtStellbar++;
            return true;
        }
        f.Col = zc; f.Row = zr;
        int echtC = f.Col - feld.Col, echtR = f.Row - feld.Row;
        int d2 = echtC * echtC + echtR * echtR;

        int vor = WacheGestartet;
        Wache(feld);
        int n = WacheGestartet - vor;
        // Unter dem Nullmodell steigt NIE einer auf — dann ist jedes Soll 0.
        bool erwartet = soll && !FlughafenwacheAus;
        bool ok = (n > 0) == erwartet;
        soll = erwartet;
        WacheGestellt++;
        sb.Append($"  {nr}. {wort}: d² = {d2}, {n} Jaeger gestartet "
                + $"(Soll {(soll ? "mindestens 1" : "0")}) {Ja(ok)}\n");
        return ok;
    }

    /// <summary>Den Abstand von der Basis aus unterbringen — erst nach oben,
    /// sonst nach unten. false heisst: auf dieser Karte geht es nicht.</summary>
    private static bool Stelle(int basis, int d, int weite, out int ziel)
    {
        ziel = basis + d;
        if (ziel >= 0 && ziel < weite) return true;
        ziel = basis - d;
        return ziel >= 0 && ziel < weite;
    }

    /// <summary>Wie viele Fälle wirklich gestellt werden konnten und wie viele
    /// die Karte nicht hergab. ⚠ Die zweite Zahl gehört in die Meldung: sonst
    /// sieht ein Lauf, in dem der TRENNFALL gar nicht lief, aus wie ein
    /// bestandener.</summary>
    public int WacheGestellt, WacheNichtStellbar;

    /// <summary>Eine Prüfstandsmaschine. ⚠ KULISSE — die Werte sind gewählt.</summary>
    private int ProbeMaschine(Entity feld, int art, bool stored, int owner)
    {
        int slot = 800;
        while (_special.Exists(x => x.Slot == slot)) slot++;
        var a = new Special
        {
            Slot = slot, Kind = art, Name = "Probejaeger",
            Owner = owner, HomeSlot = feld.Slot,
            Col = feld.Col, Row = feld.Row, Pos = feld.Pos,
            Stored = stored,
            Speed = 10, StufeUnten = 2, Stufe = stored ? 0 : 5,
            Hp = 100, HpMax = 100, Ammo = 4, AmmoMax = 4, Fuel = 800, FuelMax = 800,
            Attack = 10, Defence = 10, Sight = 6,
        };
        _special.Add(a);
        if (stored)
        {
            (feld.Hangar ??= new System.Collections.Generic.List<int>()).Add(slot);
        }
        return _special.Count - 1;
    }

    private static string Ja(bool b) => b ? "ja" : "NEIN";
}
