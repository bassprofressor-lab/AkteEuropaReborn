using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>`--forscher-probe` — REICHT DER SPRIT BIS ZUM FORSCHER?</b>
///
/// <para>⚠ 02.09.2026, gemeldet aus einem Spiellauf der Kampagne 3: »Ich habe
/// das Gefühl ich verbrauche viel zu schnell Sprit. Ich komm teilweise gar
/// nicht bis zu dem Forscher gefahren, da ist mein Sprit vorher alle bei den
/// Einheiten.«</para>
///
/// <para><b>Warum das nicht mit <c>--sprit-check</c> zu messen ist:</b> der
/// schickt jede Einheit 40 Zellen schräg weg und sagt danach, OB der Abzug
/// überhaupt greift. Die Frage hier ist eine andere — sie lautet nicht »greift
/// er«, sondern »kostet die STRECKE mehr, als sie lang ist«. Dafür braucht es
/// drei Zahlen nebeneinander, und keine davon allein genügt:</para>
///
/// <list type="number">
///   <item><b>Luftlinie</b> — die Zellen, die die Fahrt mindestens kostet.</item>
///   <item><b>gefahrene Zellen</b> — was wirklich betreten wurde. Liegt das
///   weit über der Luftlinie, fährt die Einheit Umwege oder pendelt, und dann
///   ist der Sprit nicht zu teuer, sondern die WEGSUCHE zu teuer.</item>
///   <item><b>verbrauchter Sprit</b> — geteilt durch die gefahrenen Zellen
///   ergibt den Satz je Zelle. Das Original zieht bei <c>0x407AA7</c> genau
///   EINS ab; steht hier 2,00, ist ein zweiter Abzug zurückgekehrt (das war
///   der Fehler vom 25.08.2026).</item>
/// </list>
///
/// <para>⭐ <b>Erst diese drei zusammen trennen die drei möglichen Ursachen:</b>
/// zu hoher Satz je Zelle · zu langer Weg · Tank zu klein. Eine einzelne Zahl
/// (»der Sprit ist alle«) trennt sie nicht, und danach zu bauen hiesse raten.
/// </para>
///
/// <para><b>Nullmodell.</b> Start (42,4), Forscher (125,18): Luftlinie 83
/// Zellen. Bei 1,00 je Zelle und einem sauberen Weg kostet die Fahrt rund
/// 85–120 Sprit. Die drei Tanks der Mission tragen 300, 400 und 440 — die
/// Fahrt müsste also mit gut zwei Dritteln Rest ankommen. Bleibt eine Einheit
/// vorher trocken, ist entweder der Satz oder die Wegzahl um ein Vielfaches
/// zu hoch, und die Tafel sagt, welches von beidem.</para>
///
/// <para>⚠ Die Probe fährt NICHT den Auftrag der Mission — sie schickt jede
/// eigene fahrende Einheit auf die Nachbarzelle des Forschers und misst. Was
/// dabei an Gegnern dazwischenkommt, gehört zur Messung: im Spiel kommt es
/// auch dazwischen.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    private bool _forscherProbeLaeuft;
    private int _forscherZielX = -1, _forscherZielY = -1;

    /// <summary><c>--forscher-probe=&lt;tank&gt;</c> setzt jeder eigenen Einheit
    /// diesen Starttank, bevor sie losfährt. 0 heisst »lassen wie geladen«.
    ///
    /// <para>⚠ Warum das nötig ist: der Nulldurchgang ist das EINZIGE Ereignis,
    /// an dem die »kein Sprit«-Meldung hängt (<c>0x407AB6 jne</c>), und mit den
    /// Tanks der Mission (300…440) tritt er in einem Prüflauf schlicht nicht
    /// ein — vier Minuten Fahrt auf <c>map_DM_4</c> liessen 86 % Sprit übrig
    /// und <b>null</b> Übergänge. Ein Prüfstand, der das Ereignis nie auslöst,
    /// belegt weder dass die Meldung kommt noch dass sie fehlt. Mit einem
    /// kleinen Tank kommt sie sicher, und die Zahl daneben sagt, ob sie so oft
    /// kam wie es Übergänge gab.</para></summary>
    public static int ForscherTank;

    /// <summary>Die Zelle, auf die die Probe wirklich befiehlt: die des
    /// Forschers, wenn er erreichbar ist — sonst die naechstgelegene Zelle,
    /// die es ist. Von <see cref="ErreichbarkeitVonStart"/> gesetzt.</summary>
    private int _forscherFahrzielX = -1, _forscherFahrzielY = -1;

    /// <summary>Je Einheitenplatz: Sprit und Zelle beim Start, gefahrene Zellen,
    /// und die Luftlinie, die zu schaffen war.</summary>
    private readonly System.Collections.Generic.Dictionary<int, ForscherLauf> _forscher = new();

    private sealed class ForscherLauf
    {
        public int Slot, SpritStart, Luftlinie;
        public int Col, Row;              // letzte gesehene Zelle
        public int Zellen;                // betretene Zellen
        public bool Angekommen, Trocken;
    }

    /// <summary>Sucht den Forscher — die Einheit des NEUTRALEN Spielers 7 — und
    /// schickt jede eigene fahrende Einheit zu ihm. Gibt die Startzeile aus.
    /// </summary>
    public string ForscherProbeStart()
    {
        int ziel = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead) continue;
            if (e.Owner != 7) continue;                 // der neutrale Platz
            ziel = i; break;
        }
        if (ziel < 0)
            return "forscher-probe: ⚠ kein Forscher gefunden (keine Einheit des " +
                   "neutralen Spielers 7) — die Probe misst NICHTS";

        var z = _entities[ziel];
        _forscherZielX = z.Col; _forscherZielY = z.Row;
        _forscherProbeLaeuft = true;

        var sb = new System.Text.StringBuilder(
            $"forscher-probe: Forscher auf ({z.Col},{z.Row})\n");

        // ⚠ ERST die Erreichbarkeit, DANN der Befehl. Der erste Bau fragte in
        // der falschen Reihenfolge und schickte sechs Einheiten auf ein Ziel,
        // das die Wegsuche nicht kennt: `ApplyMove` schluckt das still, und die
        // Tafel meldete 0 gefahrene Zellen — ununterscheidbar von »der Sprit
        // ist alle«. Ist der Forscher hinter der Waldsperre, fahren sie SO WEIT
        // WIE MAN KOMMT, denn genau das tut der Spieler auch.
        sb.Append(ErreichbarkeitVonStart(z.Col, z.Row));
        int zx = _forscherFahrzielX, zy = _forscherFahrzielY;

        int n = 0;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || !e.Mobile || e.Owner != 0) continue;
            if (ForscherTank > 0 && e.FuelMax > 0)
                e.Fuel = System.Math.Min(ForscherTank, e.FuelMax);
            int luft = System.Math.Max(System.Math.Abs(e.Col - z.Col),
                                       System.Math.Abs(e.Row - z.Row));
            _forscher[e.Slot] = new ForscherLauf
            {
                Slot = e.Slot, SpritStart = e.Fuel, Luftlinie = luft,
                Col = e.Col, Row = e.Row,
            };
            MissionOrderAt(e.Slot, zx, zy, -1);
            sb.Append($"   Platz {e.Slot} ({e.Col},{e.Row}) Tank {e.Fuel}, " +
                      $"Luftlinie {luft} -> faehrt nach ({zx},{zy})\n");
            n++;
        }
        return n == 0
            ? sb + "   ⚠ keine eigene fahrende Einheit — die Probe misst NICHTS"
            : sb.ToString();
    }

    /// <summary>
    /// ⭐⭐ <b>IST DER FORSCHER ÜBERHAUPT ERREICHBAR?</b>
    ///
    /// <para>⚠ Der erste Lauf der Probe (02.09.2026) meldete <b>0 gefahrene
    /// Zellen</b> bei sechs angenommenen Fahrbefehlen. <c>ApplyMove</c>
    /// schluckt einen Weg, den es nicht gibt, STILL: es setzt nur
    /// <c>Entity.RetryIn</c> und gibt <c>false</c> zurück. Damit sah ein
    /// »es gibt keinen Weg« genauso aus wie ein »der Sprit ist alle« — und
    /// eine Messung, die zwei verschiedene Ursachen nicht auseinanderhält,
    /// ist keine.</para>
    ///
    /// <para>Diese Flutfüllung beantwortet die Frage vor der Fahrt: sie geht
    /// vom Start aus über dieselbe Karte, die <c>FindPathUr</c> aufbaut
    /// (<see cref="Simulation.NavGrid.PfadOffen"/> — bewegliche Einheiten
    /// sperren die Planung nicht), und sagt, ob die Zelle des Forschers darin
    /// liegt. Liegt sie es nicht, ist der Sprit unschuldig und die Frage
    /// lautet ab dort »warum ist die Karte zerschnitten« (Verdacht: die
    /// Brücke).</para>
    /// </summary>
    private string ErreichbarkeitVonStart(int zx, int zy)
    {
        if (_nav == null) return "   ⚠ kein Navigationsgitter — nichts zu sagen";
        // ⚠⚠ 02.09.2026 — DIE FLUTFUELLUNG MUSS DIESELBE FRAGE STELLEN WIE
        // DER FAHRER. Die erste Fassung flutete mit `PfadOffen(x, y, mc, -1)`:
        // Rumpf 1, Zelle fuer Zelle, ohne die Eckenregel. `FindPathUr` fragt
        // dagegen `CanStep`, und das prueft den ganzen RUMPF der Einheit, die
        // beiden anliegenden Geraden einer Diagonale und die Hoehenstufe.
        // Ergebnis: die Flutfuellung meldete (117,21) als erreichbar, die
        // Wegsuche fand keinen Weg dorthin — und ich habe daraus voreilig auf
        // die Planungskarte geschlossen. Eine Probe, die BEQUEMER fragt als
        // der Fahrer, erfindet Wege.
        int mover = System.Array.FindIndex(_entities.ToArray(),
                                           e => e.Owner == 0 && e.Mobile && !e.Dead && !e.IsProp);
        if (mover < 0) return "   ⚠ keine eigene fahrende Einheit";
        var start = _entities[mover];

        string gebiet = "";
        int w = _nav.Width, h = _nav.Height;
        var gesehen = new bool[w * h];
        var rand = new System.Collections.Generic.Queue<Vector2I>();
        rand.Enqueue(new Vector2I(start.Col, start.Row));
        gesehen[start.Row * w + start.Col] = true;
        int erreicht = 0, frei = 0, nah = int.MaxValue;
        var nahZelle = new Vector2I(-1, -1);

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (_nav.PfadOffen(x, y, start.Move, mover)) frei++;

        while (rand.Count > 0)
        {
            var c = rand.Dequeue();
            erreicht++;
            int d = System.Math.Max(System.Math.Abs(c.X - zx), System.Math.Abs(c.Y - zy));
            if (d < nah) { nah = d; nahZelle = c; }
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = c.X + dx, ny = c.Y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    if (gesehen[ny * w + nx]) continue;
                    if (!_nav.CanStepFor(c, new Vector2I(nx, ny), start.Move, mover)) continue;
                    gesehen[ny * w + nx] = true;
                    rand.Enqueue(new Vector2I(nx, ny));
                }
        }

        bool da = nah == 0 ||
                  (zx >= 0 && zy >= 0 && zx < w && zy < h && gesehen[zy * w + zx]);
        // Erreichbar: die NACHBARzelle des Forschers (seine eigene ist besetzt).
        // Sonst: so weit wie man kommt.
        _forscherFahrzielX = da ? System.Math.Max(0, zx - 1) : nahZelle.X;
        _forscherFahrzielY = da ? zy : nahZelle.Y;

        // ⚠⚠ 02.09.2026 — UND JETZT DIE WEGSUCHE SELBST FRAGEN.
        //
        // Die Flutfüllung oben nimmt `PfadOffen`; `FindPathUr` baut seine Karte
        // aber nur unter `--neue-pfadkarte` daraus, sonst aus `IsFree` — und
        // das sperrt auch an BEWEGLICHEN Einheiten. Zwei verschiedene
        // Durchlässigkeitsbegriffe nebeneinander: die Flutfüllung sagte
        // »erreichbar«, die Einheit fuhr trotzdem keine Zelle. Eine Probe, die
        // eine andere Karte befragt als der Fahrer benutzt, misst die falsche
        // Karte. Also steht hier die ECHTE Auskunft daneben.
        var weg = _nav.FindPathUr(
            new Vector2I(start.Col, start.Row),
            new Vector2I(_forscherFahrzielX, _forscherFahrzielY),
            start.Move, mover);
        string wegzeile = weg == null
            ? $"   ⚠⚠ FindPathUr findet KEINEN Weg nach " +
              $"({_forscherFahrzielX},{_forscherFahrzielY}) — die Einheit bekommt " +
              "gar keinen Fahrbefehl, und `ApplyMove` schluckt das still.\n"
            : $"   FindPathUr: Weg ueber {weg.Count} Zellen nach " +
              $"({_forscherFahrzielX},{_forscherFahrzielY})" +
              (weg.Count >= Simulation.NavGrid.UrWegLaenge
                  ? $" (⚠ am Puffer von {Simulation.NavGrid.UrWegLaenge} abgeschnitten, " +
                    "wird unterwegs fortgesetzt)\n"
                  : "\n");
        gebiet += wegzeile;
        gebiet += $"   erreichbar vom Start ({start.Col},{start.Row}): {erreicht} von {frei} " +
               $"befahrbaren Zellen ({100.0 * erreicht / System.Math.Max(1, frei):0}%)\n" +
               (da
                  ? $"   ✔ die Zelle des Forschers ({zx},{zy}) LIEGT im erreichbaren Gebiet — " +
                    "wenn niemand ankommt, liegt es nicht an der Karte\n"
                  : $"   ⚠⚠ die Zelle des Forschers ({zx},{zy}) liegt NICHT im erreichbaren " +
                    $"Gebiet. Am naechsten kommt ({nahZelle.X},{nahZelle.Y}), noch {nah} " +
                    "Zellen entfernt. Dann ist NICHT der Sprit die Ursache, sondern die " +
                    "Karte ist zerschnitten.\n");
        return gebiet;
    }

    /// <summary>Je Takt: betretene Zellen mitzaehlen. Muss laufen, BEVOR die
    /// Einheit ihre naechste Zelle betritt, darum haengt sie am Zellwechsel und
    /// nicht am Weg — ein Weg wird neu gesucht, eine betretene Zelle nicht.
    /// </summary>
    public void ForscherProbeTick()
    {
        if (!_forscherProbeLaeuft) return;
        foreach (var e in _entities)
        {
            if (!_forscher.TryGetValue(e.Slot, out var f)) continue;
            if (e.Owner != 0 || e.IsProp) continue;
            if (e.Col != f.Col || e.Row != f.Row)
            {
                f.Zellen++;
                f.Col = e.Col; f.Row = e.Row;
            }
            if (e.Fuel <= 0) f.Trocken = true;
            if (System.Math.Max(System.Math.Abs(e.Col - _forscherZielX),
                                System.Math.Abs(e.Row - _forscherZielY)) <= 1)
                f.Angekommen = true;
        }
    }

    /// <summary>Die Tafel.</summary>
    public string ForscherProbeLine()
    {
        if (!_forscherProbeLaeuft) return "forscher-probe: nicht gestartet";
        var sb = new System.Text.StringBuilder(
            $"forscher-probe: Ziel ({_forscherZielX},{_forscherZielY})\n" +
            "   Platz  Luftlinie  gefahren  Umweg   Sprit    je Zelle  Stand\n");
        int an = 0, trocken = 0;
        foreach (var f in _forscher.Values)
        {
            var e = System.Array.Find(_entities.ToArray(), x => x.Slot == f.Slot);
            int rest = e?.Fuel ?? 0;
            int weg = f.SpritStart - rest;
            double proZelle = f.Zellen > 0 ? (double)weg / f.Zellen : 0;
            double umweg = f.Luftlinie > 0 ? (double)f.Zellen / f.Luftlinie : 0;
            if (f.Angekommen) an++;
            if (f.Trocken || rest <= 0) trocken++;
            sb.Append($"   {f.Slot,5}  {f.Luftlinie,9}  {f.Zellen,8}  " +
                      $"{umweg,5:0.00}x  {weg,4}/{f.SpritStart,-4} {proZelle,8:0.00}  " +
                      (f.Trocken || rest <= 0 ? "TROCKEN" :
                       f.Angekommen ? "angekommen" : $"unterwegs, {rest} Rest") + "\n");
        }
        sb.Append($"   {an} von {_forscher.Count} angekommen, {trocken} trocken\n");
        sb.Append("   ⭐ »je Zelle« muss 1,00 sein (Original @0x407AA7 zieht genau EINS ab).\n" +
                  "     »Umweg« ist gefahrene Zellen durch Luftlinie: 1,0–1,5 ist ein\n" +
                  "     normaler Weg um Gelaende herum, ab etwa 2,5 faehrt die Einheit\n" +
                  "     Schleifen, und dann ist nicht der Sprit zu teuer, sondern der Weg.\n");
        return sb.ToString();
    }
}
