using System.Collections.Generic;
using Godot;
using AkteEuropaReborn.Simulation;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>DIE MATERIALROUTE DES TRANSPORTERS</b> — der Wagen, der zwischen Mine,
/// Fabrik und Basis pendelt.
///
/// <para>Gebaut am 08.09.2026 nach <c>berichte/transporterroute-fable.md</c>
/// (Leselauf vom 07.09.). Sein Punkt 7 aus dem Prueflauf durch Kampagne 5: bei
/// uns fahren keine Transporter zwischen Basis und Fabriken, im Original tun
/// sie es von selbst — die KI legt die Routen sogar eigenstaendig an.</para>
///
/// <para><b>Der Satz</b> steht im Original in sec48 (<c>0x77AC50</c>, 400 Saetze
/// zu 18 Byte) und haengt ueber <c>+0x40</c> an der Einheit; angelegt wird er
/// von <c>0x436190</c>, aber nur fuer Einheiten mit Bauteilzeile
/// <c>+0x0E == 0x47</c> — das ist der Transporter. Wir fuehren dieselben Felder
/// als <see cref="Transportroute"/>, an der Einheit ueber ihren Platz.</para>
///
/// <para><b>Der Umlauf</b> ist eine Kette von Auftragszustaenden (UKOL):</para>
/// <code>
///   UKOL 0  (Arm 0x407F67)  Gebaeude waehlen (0x4106D0) und hinfahren
///   an der BASIS      UKOL 25 (0x40991B)  60 Takte warten, umladen, wegfahren
///   an FABRIK/MINE    UKOL 48 -> 54 (0x409E18, 100 Takte drinnen, unsichtbar)
///                        -> 53 (0x409D57, umladen, am Ausgang erscheinen)
///                        -> wieder UKOL 0
/// </code>
///
/// <para><b>Es gibt keine feste Menge je Fahrt</b> (<c>0x410940</c>): an der
/// Quelle wird je Ware je Runde EIN Stueck genommen, bis die Fassung 30 voll
/// oder die Quelle leer ist; am Ziel wird alles abgeladen. WELCHE Waren, sagt
/// die Flaggentafel <c>0x4FACD0</c> — immer die des ZIELgebaeudes: die Basis
/// nimmt Waffen/Fahrwerk/Spezial, eine Fabrik nur Terranium.</para>
///
/// <para><b>⚠ Was hier UNSERE Setzung ist</b>, und zwar ausdruecklich:</para>
/// <list type="bullet">
///   <item>Die <b>Ankunft</b>. Das Original erkennt sie an Zellbytes
///   (<c>0x4116DA</c>: <c>0x542E18[(x+2)·256 + (y−2)] == 0x63</c>) — eine
///   Karte, die wir nicht fuehren. Wir fragen stattdessen die LAGE: steht der
///   Wagen auf der Torzelle des angesteuerten Gebaeudes bzw. vor dem Basistor.</item>
///   <item>Die <b>Wegfahrt</b> nach dem Umladen wuerfelt im Original
///   (<c>x − 3 + rand%7</c>); wir nehmen dieselbe Formel, aber ueber
///   <see cref="Determinism.Roll"/>, damit der Strom gleich bleibt.</item>
///   <item>Am <b>Basistor</b> koennte auch die Einfahrt greifen (Typ 1 ist ein
///   Garagentyp). Der Bericht sagt ausdruecklich, welcher Takt im Original
///   zuerst laeuft, sei NICHT gelesen. Bei uns hat die Route Vorrang: ein
///   Wagen mit gestarteter Route wird nicht untergestellt.</item>
/// </list>
///
/// <para>Gegenschalter <c>--keine-transportrouten</c>.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary>Ein Satz aus sec48 — dieselben Felder, dieselben Namen.</summary>
    public sealed class Transportroute
    {
        /// <summary>+0x00..+0x03, vier Quellgebaeude (Platznummern), −1 = 0xFF.</summary>
        public readonly int[] Quelle = { -1, -1, -1, -1 };
        /// <summary>+0x04, das Zielgebaeude, −1 = 0xFF.</summary>
        public int Ziel = -1;
        /// <summary>+0x05..+0x08: Waffen, Fahrwerk, Spezial, Terranium.</summary>
        public readonly int[] Geladen = new int[4];
        /// <summary>+0x09, die Ladung insgesamt.</summary>
        public int Gesamt;
        /// <summary>+0x0A, die Fassung. 30 im Original (<c>0x436204</c>).</summary>
        public int Fassung = RouteFassung;
        /// <summary>+0x0C, der Griff der Einheit — bei uns ihr PLATZ.</summary>
        public int Einheit;
        /// <summary>+0x0E, gestartet (1) oder steht (0).</summary>
        public bool Gestartet;
        /// <summary>+0x0F, das gerade angesteuerte Gebaeude, −1 = keines.</summary>
        public int Fahrziel = -1;
        /// <summary>+0x10, der Reihum-Zeiger ueber die vier Quellen.</summary>
        public int Reihum;
        /// <summary>+0x1C der EINHEIT: der Wartezaehler in Originaltakten.</summary>
        public int Warten;

        /// <summary>Wieviel DIESER Wagen schon aufgenommen und abgeliefert hat.
        /// ⚠ Nicht im Original — aber ohne eine Zahl JE SATZ misst der
        /// Pruefstand die Summe aller Wagen und kann sie keinem Lager
        /// zuordnen.</summary>
        public int GeladenGesamt, AbgeladenGesamt;

        /// <summary>Wieviele Takte dieser Wagen noch ruht, weil sein letzter
        /// Fahrbefehl keinen Weg fand. ⚠ UNSERE Bremse: ohne sie fragt der
        /// Leerlauf FUENFZIGMAL je Sekunde die Wegsuche. Die Frist ist die des
        /// Originals fuer den Stau (<c>+0x1C := 0x28 + rand%20</c>,
        /// <c>0x408BDD</c>).</summary>
        public int Ruhe;
    }

    /// <summary>Fassung eines Wagens, <c>0x436204 mov byte[+0x0A], 0x1E</c>.</summary>
    public const int RouteFassung = 30;
    /// <summary>Wie lange der Wagen an der Basis wartet (<c>0x41171B</c>).</summary>
    public const int RouteWartenBasis = 60;
    /// <summary>Wie lange er in der Fabrik verschwindet (<c>0x43D51A</c>).</summary>
    public const int RouteWartenDrinnen = 100;
    /// <summary>Wieviele Saetze es gibt (sec48: 400).</summary>
    public const int RouteSaetze = 400;

    /// <summary>UKOL 25 — an der Basis, umladen (<c>0x40991B</c>).</summary>
    public const int UkolBasisUmschlag = 25;
    /// <summary>UKOL 53 (0x35) — heraustreten und umladen (<c>0x409D57</c>).</summary>
    public const int UkolAusgang = 0x35;
    /// <summary>UKOL 54 (0x36) — im Gebaeude, unsichtbar (<c>0x409E18</c>).</summary>
    public const int UkolImGebaeude = 0x36;

    /// <summary>Die Bauteilzeile, die einen Transporter ausmacht: <c>+0x0E ==
    /// 0x47</c>. Beide Erzeuger des Originals fragen genau das
    /// (<c>0x4B1D73</c>, <c>0x4B38DF</c>).</summary>
    public const int TransporterTeil = 0x47;

    /// <summary><c>--keine-transportrouten</c> — der Stand vor dem 08.09.2026:
    /// kein Wagen pendelt, die KI legt keine Routen an.</summary>
    public static bool KeineTransportrouten;

    /// <summary>Die Saetze, ueber den PLATZ der Einheit — im Original der
    /// Zeiger <c>+0x40</c> in die Tafel sec48.</summary>
    private readonly Dictionary<int, Transportroute> _routen = new();

    /// <summary>Wieviel schon umgeschlagen wurde, je Ware — fuer die Probe.
    /// ⚠ Ohne die Zahl ist »die Route laeuft« nicht von »der Wagen faehrt im
    /// Kreis« zu unterscheiden.</summary>
    public readonly int[] RouteGeladen = new int[4];
    public readonly int[] RouteAbgeladen = new int[4];
    public int RouteAngelegt, RouteFahrten, RouteKiRouten;

    /// <summary>Wie oft ein Wagen nicht aus dem Gebaeude herauskam, und warum
    /// beim ersten Mal.</summary>
    public int RouteAusgangGesperrt;
    public string RouteAusgangGrund = "";

    /// <summary>Wie oft ein Fahrbefehl der Route keinen Weg fand. ⚠ Ohne die
    /// Zahl sieht »der Wagen steht« genauso aus wie »er hat nichts zu tun«.
    /// </summary>
    public int RouteOhneWeg;

    /// <summary>Traegt diese Einheit einen Umschlagsatz? <c>+0x0E == 0x47</c>.</summary>
    public static bool IstTransporter(Entity e)
        => !e.IsBuilding && !e.IsProp && e.Part == TransporterTeil;

    /// <summary>Der Satz dieser Einheit, oder <c>null</c>.</summary>
    public Transportroute? RouteVon(Entity e)
        => _routen.TryGetValue(e.Slot, out var r) ? r : null;

    /// <summary>Fuer die Pruefstaende.</summary>
    public IReadOnlyDictionary<int, Transportroute> RoutenFuerProbe => _routen;

    /// <summary>
    /// <b>Einen Satz anlegen</b> — <c>0x436190</c>. Der erste freie Satz wird
    /// genommen; sind alle 400 belegt, bekommt der Wagen keinen.
    /// </summary>
    public Transportroute? RouteAnlegen(Entity e)
    {
        if (KeineTransportrouten || !IstTransporter(e)) return null;
        if (_routen.TryGetValue(e.Slot, out var da)) return da;
        if (_routen.Count >= RouteSaetze) return null;
        var r = new Transportroute { Einheit = e.Slot };
        _routen[e.Slot] = r;
        RouteAngelegt++;
        return r;
    }

    /// <summary>Den Satz freigeben — <c>0x436260</c>.</summary>
    public void RouteFreigeben(Entity e) => _routen.Remove(e.Slot);

    /// <summary>Nach dem Kartenaufbau: jeder Transporter bekommt seinen Satz.
    /// ⚠ Das Original legt ihn beim ERZEUGEN an; eine Karteneinheit ist beim
    /// Laden schon da, also hier.</summary>
    private void RoutenAufbauen()
    {
        _routen.Clear();
        RouteAngelegt = 0; RouteFahrten = 0; RouteKiRouten = 0;
        for (int k = 0; k < 4; k++) { RouteGeladen[k] = 0; RouteAbgeladen[k] = 0; }
        if (KeineTransportrouten) return;
        foreach (var e in _entities)
            if (!e.Dead && IstTransporter(e)) RouteAnlegen(e);
    }

    /// <summary>
    /// <b>Welche Waren dieses Gebaeude annimmt</b> — die Flaggentafel
    /// <c>0x4FACD0</c>, vier Byte je Gebaeudeart, in der Reihenfolge Waffen,
    /// Fahrwerk, Spezial, Terranium.
    ///
    /// <para>⚠ Sie stammt aus dem Baumwissen (GAMESTATE_RE 3.2237) und ist
    /// nicht neu gelesen. Alles, was nicht genannt ist, nimmt nichts.</para>
    /// </summary>
    public static bool[] WarenTafelFuerProbe(int bType) => WarenTafel(bType);

    private static bool[] WarenTafel(int bType) => bType switch
    {
        1  => new[] { true,  true,  true,  false },   // Basis
        2 or 3 or 4 => new[] { false, false, false, true },   // Fabriken
        6  => new[] { true,  true,  true,  true  },   // Bahnhof
        9  => new[] { true,  true,  true,  false },   // Flughafen
        12 => new[] { true,  true,  true,  true  },   // Feldbahnhof
        16 => new[] { true,  true,  true,  false },   // Hafen
        _  => new[] { false, false, false, false },
    };

    /// <summary>Das Lager eines Gebaeudes als vier Felder — +0x2C/+0x2E/+0x30/
    /// +0x32 in der Reihenfolge der Warentafel.</summary>
    private static int LagerLesen(Entity b, int k) => k switch
    {
        0 => b.StockW, 1 => b.StockF, 2 => b.StockS, _ => b.StockT,
    };

    private static void LagerSchreiben(Entity b, int k, int wert)
    {
        switch (k)
        {
            case 0: b.StockW = wert; break;
            case 1: b.StockF = wert; break;
            case 2: b.StockS = wert; break;
            default: b.StockT = wert; break;
        }
    }

    /// <summary>Das Gebaeude mit dieser Platznummer, oder <c>null</c>.</summary>
    private Entity? GebaeudePlatz(int slot)
    {
        if (slot < 0) return null;
        foreach (var b in _entities)
            if (b.IsBuilding && !b.Dead && b.Slot == slot) return b;
        return null;
    }

    /// <summary>
    /// <b>Der Vorrat einer Quelle</b> — <c>0x410510</c>: die Summe der Lager,
    /// aber nur ueber die Waren, die das ZIEL annimmt.
    /// </summary>
    private int RouteVorrat(Transportroute r, Entity quelle)
    {
        var ziel = GebaeudePlatz(r.Ziel);
        if (ziel == null) return 0;
        var f = WarenTafel(ziel.BType);
        int s = 0;
        for (int k = 0; k < 4; k++) if (f[k]) s += LagerLesen(quelle, k);
        return s;
    }

    /// <summary>
    /// <b>Das naechste Gebaeude waehlen</b> — <c>0x4106D0</c>, Schritt fuer
    /// Schritt: voll heisst zum Ziel; sonst den Reihum-Zeiger auf die naechste
    /// belegte Quelle drehen und ihren Vorrat gegen das Maximum aller vier
    /// halten.
    /// </summary>
    private int RouteNaechstesGebaeude(Transportroute r)
    {
        if (r.Gesamt >= r.Fassung) return r.Ziel;             // voll -> Ziel

        // Reihum auf die naechste BELEGTE Quelle drehen (mod 4).
        int versuche = 0;
        while (versuche < 4 && r.Quelle[r.Reihum] < 0)
        { r.Reihum = (r.Reihum + 1) % 4; versuche++; }
        if (r.Quelle[r.Reihum] < 0) return r.Ziel;            // gar keine Quelle

        var q = GebaeudePlatz(r.Quelle[r.Reihum]);
        int vorrat = q == null ? 0 : RouteVorrat(r, q);

        int max = 0, maxPlatz = -1;
        for (int i = 0; i < 4; i++)
        {
            var qq = GebaeudePlatz(r.Quelle[i]);
            if (qq == null) continue;
            int v = RouteVorrat(r, qq);
            if (v > max) { max = v; maxPlatz = r.Quelle[i]; }
        }

        if (vorrat > 5) return r.Quelle[r.Reihum];            // 0x410781
        if (max > vorrat && maxPlatz >= 0) return maxPlatz;   // 0x4107A5
        return r.Gesamt != 0 ? r.Ziel : r.Quelle[r.Reihum];   // 0x4107EE
    }

    /// <summary>
    /// <b>Umladen</b> — <c>0x410940</c>. Am ZIEL wird alles abgeladen, an einer
    /// QUELLE wird Runde um Runde je ein Stueck genommen, bis die Fassung voll
    /// oder nichts mehr da ist. Die erlaubten Waren sind IMMER die des Ziels.
    /// </summary>
    private void RouteUmladen(Entity u, Transportroute r, Entity b)
    {
        var ziel = GebaeudePlatz(r.Ziel);
        if (ziel == null) return;
        var f = WarenTafel(ziel.BType);

        // 0x4109DE: der Wagen fuellt beim Umladen seinen eigenen Vorrat auf.
        if (u.AmmoMax > 0) u.Ammo = u.AmmoMax;

        if (b.Slot == r.Ziel)
        {
            for (int k = 0; k < 4; k++)
            {
                if (!f[k] || r.Geladen[k] <= 0) continue;
                LagerSchreiben(b, k, LagerLesen(b, k) + r.Geladen[k]);
                RouteAbgeladen[k] += r.Geladen[k];
                r.AbgeladenGesamt += r.Geladen[k];
                r.Geladen[k] = 0;
            }
            r.Gesamt = 0;
            return;
        }

        // an einer Quelle: je Runde ein Stueck je erlaubter Ware
        while (r.Gesamt < r.Fassung)
        {
            bool genommen = false;
            for (int k = 0; k < 4 && r.Gesamt < r.Fassung; k++)
            {
                if (!f[k] || LagerLesen(b, k) <= 0) continue;
                LagerSchreiben(b, k, LagerLesen(b, k) - 1);
                r.Geladen[k]++; r.Gesamt++; RouteGeladen[k]++; r.GeladenGesamt++;
                genommen = true;
            }
            r.Reihum = (r.Reihum + 1) % 4;          // 0x410C42
            if (!genommen) break;
        }
    }

    /// <summary>
    /// Einen Fahrauftrag setzen — dieselben SECHS Felder wie
    /// <c>ApplyMove</c>. ⚠ Genau das war die Do-Not-Repeat-Regel vom
    /// 08.09.2026: ein halb gesetzter Weg ist schlimmer als keiner.
    /// </summary>
    private bool RouteFahrt(int idx, Entity u, Vector2I zelle)
    {
        if (_nav == null || u.Dead || !u.Mobile || u.DugIn) return false;
        if (u.FuelMax > 0 && u.Fuel <= 0) return false;
        var ziel = _nav.NearestFree(zelle, u.Move, idx);
        if (ziel == null) return false;
        var weg = _nav.FindPath(new Vector2I(u.Col, u.Row), ziel.Value, u.Move, idx);
        if (weg == null || weg.Count == 0) return false;
        if (u.Reserved is { } v) _nav.ClearOccupant(v.X, v.Y, idx);
        u.Path = weg;
        u.PathIdx = 0;
        u.Goal = ziel.Value;
        u.Reserved = null;
        u.WaitTime = 0;
        u.RetryIn = 0;
        u.Target = -1;
        u.Ordered = false;                 // die Route ist kein Spielerbefehl
        u.Block = BlockEnter + Determinism.Roll(BlockEnterSpread);
        return true;
    }

    /// <summary>Wohin der Wagen faehrt, um dieses Gebaeude zu erreichen —
    /// <c>0x407FF7</c>: die Basis ueber <c>(x+2, y+4)</c>, jedes andere ueber
    /// seine Torzelle 0.</summary>
    private Vector2I RouteAnfahrt(Entity b)
        => b.BType == 1
            ? new Vector2I(b.Col + 2, b.Row + 4)
            : b.DoorCells.Count > 0
                ? new Vector2I(b.Col + b.DoorCells[0].Col, b.Row + b.DoorCells[0].Row)
                : new Vector2I(b.Col, b.Row);

    /// <summary>Ist der Wagen an diesem Gebaeude angekommen? ⚠ UNSERE Frage —
    /// siehe den Kopfkommentar.</summary>
    private static bool RouteAngekommen(Entity u, Entity b, Vector2I anfahrt)
        => Mathf.Max(Mathf.Abs(u.Col - anfahrt.X), Mathf.Abs(u.Row - anfahrt.Y)) <= 1;

    /// <summary>
    /// <b>Der Takt der Routen.</b> Er haengt im Leerlaufverteiler des Originals
    /// (Auftrag 0, Tafel <c>0x40A16C</c>, Arm <c>0x47 → 0x407F67</c>) — also
    /// genau dort, wo auch das Baufahrzeug seinen Auftrag abholt.
    /// </summary>
    private void TransportrouteTakt()
    {
        if (KeineTransportrouten || _nav == null) return;

        for (int i = 0; i < _entities.Count; i++)
        {
            var u = _entities[i];
            if (u.Dead || u.IsBuilding || u.IsProp) continue;
            var r = RouteVon(u);
            if (r == null || !r.Gestartet) continue;

            switch (u.Ukol)
            {
                case UkolBasisUmschlag:                       // 25 — 0x40991B
                {
                    if (--r.Warten > 0) break;
                    var b = GebaeudePlatz(r.Fahrziel);
                    if (b != null) RouteUmladen(u, r, b);
                    RouteWegfahren(i, u, b);
                    u.Ukol = UkolFrei;
                    r.Fahrziel = -1;
                    break;
                }

                case UkolImGebaeude:                          // 54 — 0x409E18
                    if (--r.Warten <= 0) u.Ukol = UkolAusgang;
                    break;

                case UkolAusgang:                             // 53 — 0x409D57
                {
                    var b = GebaeudePlatz(r.Fahrziel);
                    if (b == null) { u.Ukol = UkolFrei; r.Fahrziel = -1; break; }
                    var aus = RouteAusgangSuchen(i, u, b);
                    if (aus == null)
                    {
                        // ⚠ Ohne den Grund ist »er kommt nicht heraus« nicht von
                        // »er will gar nicht« zu unterscheiden.
                        RouteAusgangGesperrt++;
                        if (RouteAusgangGrund.Length == 0 && b.DoorCells.Count > 0)
                        {
                            var t = new Vector2I(b.Col + b.DoorCells[^1].Col,
                                                 b.Row + b.DoorCells[^1].Row);
                            RouteAusgangGrund = $"Platz {u.Slot} bei ({u.Col},{u.Row}), Ausgang "
                                + $"({t.X},{t.Y}): "
                                + (!_nav.ImapFrei(t.X, t.Y)
                                   ? $"da steht Nr. {_nav.BesetztVon(t.X, t.Y)}"
                                   : $"die Zelle darunter ({t.X},{t.Y + 1}) ist nicht frei — "
                                     + _nav.WarumGesperrt(t.X, t.Y + 1, u.Move, i));
                        }
                        break;
                    }
                    RouteUmladen(u, r, b);
                    u.Col = aus.Value.X; u.Row = aus.Value.Y;
                    u.Elev = ElevOf(u.Col, u.Row);
                    u.Pos = CellCenter(u.Col, u.Row);
                    u.Footprint = CellRect(_ox, _oy, u.Col, u.Row, u.Elev);
                    _nav.SetOccupant(u.Col, u.Row, i, u.Infantry >= 0);
                    u.Ukol = UkolFrei;
                    RouteWegfahren(i, u, b);
                    r.Fahrziel = -1;
                    break;
                }

                case UkolAngemeldet:                          // 48 — 0x43D48F
                {
                    // ⚠⚠ 08.09.2026, VOM PRUEFSTAND GEFUNDEN. Der Wagen fuhr
                    // los, kam an der Fabrik an — und stand dann fuer immer auf
                    // UKOL 48. Die Ursache ist keine der Route: der TUERTAKT
                    // meldet jede Einheit mit UKOL 0 auf der Torzelle an
                    // (@0x43D5AB, Simulation/Einfahrt.cs), und mein Takt sah
                    // danach nur noch UKOL 0 vor. Das Original geht genau
                    // diesen Weg: @0x43D48F prueft UKOL == 0x30 UND
                    // satz+0x0F == b und macht daraus 54.
                    var bb = GebaeudePlatz(r.Fahrziel);
                    if (bb == null) { u.Ukol = UkolFrei; r.Fahrziel = -1; break; }
                    if (RouteAngekommen(u, bb, RouteAnfahrt(bb))) RouteAnkunft(i, u, r, bb);
                    break;
                }

                default:                                      // UKOL 0 — 0x407F67
                {
                    if (u.Ukol != UkolFrei || u.Path != null) break;

                    var b = GebaeudePlatz(r.Fahrziel);
                    if (b != null && RouteAngekommen(u, b, RouteAnfahrt(b)))
                    {
                        RouteAnkunft(i, u, r, b);
                        break;
                    }

                    if (r.Ruhe > 0) { r.Ruhe--; break; }

                    int naechstes = RouteNaechstesGebaeude(r);
                    var g = GebaeudePlatz(naechstes);
                    if (g == null) break;
                    r.Fahrziel = naechstes;
                    if (RouteFahrt(i, u, RouteAnfahrt(g))) RouteFahrten++;
                    else { r.Ruhe = 40 + Determinism.Roll(20); RouteOhneWeg++; }
                    break;
                }
            }
        }
    }

    /// <summary>
    /// <b>Wo der Wagen wieder herauskommt</b> — <c>0x409D57</c>, woertlich:
    /// Ausgang ist die <b>letzte Torzelle</b> (<c>b[+0x32+3n]</c> mit
    /// <c>n = b[+0x34]</c>), und heraus geht es nur, wenn
    /// <c>imap[aus] &gt;= 0xFFFE</c> UND <c>imap[aus.x, aus.y+1] == 0xFFFE</c>.
    ///
    /// <para>⚠⚠ 08.09.2026, ZWEIMAL BERICHTIGT und beide Male vom Pruefstand.
    /// Der erste Anlauf fragte <c>Ask(...) == Free</c> — das schliesst die
    /// <b>geschlossene Tuer</b> (0xFFFF) mit ein und liess den Wagen ewig
    /// drinnen (4884 vergebliche Versuche in 120 s). Der zweite baute sich eine
    /// Ausweichliste (letzte Tuer, erste Tuer, eigene Zelle) — bequem, aber
    /// erfunden. Massgeblich ist die gelesene Bedingung, und sie sagt genau:
    /// <b>eine zugestellte Tuer haelt niemanden fest, eine BESETZTE schon.</b>
    /// Siehe <see cref="Simulation.NavGrid.ImapFrei"/>.</para>
    /// </summary>
    private Vector2I? RouteAusgangSuchen(int i, Entity u, Entity b)
    {
        if (_nav == null || b.DoorCells.Count == 0) return null;
        var aus = new Vector2I(b.Col + b.DoorCells[^1].Col, b.Row + b.DoorCells[^1].Row);
        if (!_nav.ImapFrei(aus.X, aus.Y)) return null;
        if (!_nav.ImapGanzFrei(aus.X, aus.Y + 1, u.Move)) return null;
        return aus;
    }

    /// <summary>Die Ankunft: an der Basis wird gewartet, an Fabrik und Mine
    /// faehrt der Wagen hinein.</summary>
    private void RouteAnkunft(int i, Entity u, Transportroute r, Entity b)
    {
        if (b.BType == 1)                                   // 0x41171B
        {
            u.Ukol = UkolBasisUmschlag;
            r.Warten = RouteWartenBasis;
            return;
        }
        // 0x43D51A: hinein, unsichtbar, 100 Takte.
        _nav?.ClearOccupant(u.Col, u.Row, i);
        if (u.Reserved is { } v) _nav?.ClearOccupant(v.X, v.Y, i);
        u.Reserved = null;
        u.Path = null;
        u.Ukol = UkolImGebaeude;
        r.Warten = RouteWartenDrinnen;
        if (_sel.Contains(i)) { _sel.Remove(i); UpdatePanel(); }   // 0x43D55F
    }

    /// <summary>Nach dem Umladen ein Stueck wegfahren — <c>0x40995A</c> bzw.
    /// <c>0x40AFE0</c>. Die Wuerfe sind die des Originals, gezogen aus unserem
    /// Strom.</summary>
    private void RouteWegfahren(int i, Entity u, Entity? b)
    {
        int x = u.Col - 3 + Determinism.Roll(7);
        int y = u.Row + 2 + Determinism.Roll(3);
        if (b != null && b.BType != 1)
        {
            x = u.Col + 2 - Determinism.Roll(5);
            y = u.Row + 5 - Determinism.Roll(3);
        }
        RouteFahrt(i, u, new Vector2I(x, y));
    }

    // ---- Die KI: 0x4BB7D0 ---------------------------------------------------

    /// <summary>
    /// <b>Die KI legt Routen selbst an</b> — <c>0x4BB7D0(spieler)</c>, gerufen
    /// aus der Sektormaschine (Takt-Platz 2, <c>0x4BFC55</c>).
    ///
    /// <para>Je Fabrik ZWEI Routen: Fabrik → Basis/Flughafen (<c>0x4BB570</c>)
    /// und Mine → Fabrik (<c>0x4BB6A0</c>), jede mit genau EINER Quelle. Eine
    /// Route, die STEHT, zaehlt nicht — dann bekaeme die Fabrik einen zweiten
    /// Wagen.</para>
    ///
    /// <para>⚠ Nicht gebaut ist der Rueckfall »kein freier Wagen, also einen
    /// bauen lassen« (<c>0x4BB1E0</c> mit 200 Takten Sperre): das haengt am
    /// Bauprogramm, und in der Kampagne baut die KI nur aus dem Programm der
    /// Mission (bug-090). Ohne freien Wagen passiert hier also nichts.</para>
    /// </summary>
    private void KiTransportrouten(int spieler)
    {
        if (KeineTransportrouten || _nav == null) return;

        int basis = KiRouteZiel(spieler);
        if (basis < 0) return;                                // »no target found«

        foreach (var g in _entities)
        {
            if (!g.IsBuilding || g.Dead || g.Owner != spieler) continue;
            if (g.BType is not (2 or 3 or 4)) continue;       // nur Fabriken

            if (!KiRouteMitQuelle(spieler, g.Slot))           // 0x4BB570
                KiRouteAnlegen(spieler, ziel: basis, quelle: g.Slot);

            if (!KiRouteMitZiel(spieler, g.Slot))             // 0x4BB6A0
            {
                int mine = KiRouteMine(spieler);
                if (mine >= 0) KiRouteAnlegen(spieler, ziel: g.Slot, quelle: mine);
            }
        }
    }

    /// <summary>Ein eigenes Gebaeude der Art 1 oder 9 — <c>0x4BAF50</c>.
    /// ⚠ Das Original wuerfelt eines aus; wir nehmen das erste, damit der Lauf
    /// wiederholbar bleibt. Das ist eine Setzung.</summary>
    private int KiRouteZiel(int spieler)
    {
        foreach (var b in _entities)
            if (b.IsBuilding && !b.Dead && b.Owner == spieler && b.BType is 1 or 9)
                return b.Slot;
        return -1;
    }

    /// <summary>Eine eigene Mine — <c>0x4BAFE0</c> (Art 10 oder 15).</summary>
    private int KiRouteMine(int spieler)
    {
        foreach (var b in _entities)
            if (b.IsBuilding && !b.Dead && b.Owner == spieler && b.BType is 10 or 15)
                return b.Slot;
        return -1;
    }

    /// <summary>Gibt es eine GESTARTETE Route dieses Spielers mit dieser
    /// Quelle? <c>0x4BADB0</c>.</summary>
    private bool KiRouteMitQuelle(int spieler, int gebaeude)
    {
        foreach (var kv in _routen)
        {
            if (!kv.Value.Gestartet) continue;
            var u = EinheitPlatz(kv.Key);
            if (u == null || u.Owner != spieler) continue;
            foreach (int q in kv.Value.Quelle) if (q == gebaeude) return true;
        }
        return false;
    }

    /// <summary>Dasselbe fuer das Ziel — <c>0x4BAE40</c>.</summary>
    private bool KiRouteMitZiel(int spieler, int gebaeude)
    {
        foreach (var kv in _routen)
        {
            if (!kv.Value.Gestartet) continue;
            var u = EinheitPlatz(kv.Key);
            if (u == null || u.Owner != spieler) continue;
            if (kv.Value.Ziel == gebaeude) return true;
        }
        return false;
    }

    /// <summary>Die Einheit mit dieser Platznummer.</summary>
    private Entity? EinheitPlatz(int slot)
    {
        foreach (var e in _entities)
            if (!e.IsBuilding && !e.IsProp && e.Slot == slot) return e;
        return null;
    }

    /// <summary>Einen STEHENDEN Transporter dieses Spielers — <c>0x4BAEB0</c>:
    /// <c>+0x0E == 0x47</c>, UKOL 0 und eine Route, die nicht laeuft.</summary>
    private int KiRouteWagen(int spieler)
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Dead || e.IsBuilding || e.IsProp) continue;
            if (e.Owner != spieler || !IstTransporter(e) || e.Ukol != UkolFrei) continue;
            var r = RouteVon(e) ?? RouteAnlegen(e);
            if (r != null && !r.Gestartet) return i;
        }
        return -1;
    }

    /// <summary>Fächer setzen (<c>0x4362E0</c>) und starten
    /// (<c>0x410870</c>).</summary>
    private void KiRouteAnlegen(int spieler, int ziel, int quelle)
    {
        int idx = KiRouteWagen(spieler);
        if (idx < 0) return;                     // kein freier Wagen — s.o.
        var u = _entities[idx];
        var r = RouteVon(u);
        if (r == null) return;

        r.Ziel = ziel;
        r.Quelle[0] = quelle;
        r.Quelle[1] = r.Quelle[2] = r.Quelle[3] = -1;
        // 0x4362E0: eine Quelle, die dasselbe Gebaeude ist wie das Ziel, faellt
        // heraus.
        if (r.Quelle[0] == r.Ziel) r.Quelle[0] = -1;
        RouteStarten(u, r);
        RouteKiRouten++;
    }

    /// <summary>Start — <c>0x410870</c>: UKOL 0, kein Fahrziel, Route laeuft.
    /// </summary>
    public void RouteStarten(Entity u, Transportroute r)
    {
        u.Ukol = UkolFrei;
        r.Gestartet = true;
        r.Fahrziel = -1;
        r.Warten = 0;
    }

    /// <summary>Stop — <c>0x4108D0</c>. ⚠ Und JEDER Handbefehl stoppt die
    /// Route (<c>0x40B13B</c>, Protokoll »STOP TRANSPORT«).</summary>
    public void RouteAnhalten(Entity u)
    {
        var r = RouteVon(u);
        if (r != null) r.Gestartet = false;
    }

    /// <summary>Die Meldezeile — sie gehoert in jeden Lauf.</summary>
    public string RouteWatchLine()
    {
        int laufend = 0;
        foreach (var kv in _routen) if (kv.Value.Gestartet) laufend++;
        return $"transportroute: {_routen.Count} Saetze, {laufend} laufend, "
             + $"{RouteKiRouten} von der KI angelegt, {RouteFahrten} Fahrten; "
             + $"geladen W{RouteGeladen[0]} F{RouteGeladen[1]} S{RouteGeladen[2]} "
             + $"T{RouteGeladen[3]}, abgeladen W{RouteAbgeladen[0]} F{RouteAbgeladen[1]} "
             + $"S{RouteAbgeladen[2]} T{RouteAbgeladen[3]}"
             + (RouteOhneWeg > 0 ? $"; {RouteOhneWeg}x kein Weg" : "")
             + (RouteAusgangGesperrt > 0
                ? $"; ⚠ {RouteAusgangGesperrt}x kam ein Wagen nicht aus dem Gebaeude — "
                  + RouteAusgangGrund
                : "")
             + (_routen.Count == 0 ? "   ⚠ kein Transporter auf dieser Karte — die 0 sagt nichts"
                                   : "");
    }
}
