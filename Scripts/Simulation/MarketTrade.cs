namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;
using AkteEuropaReborn.Simulation;

/// <summary>
/// <b>DER HANDEL AM GESCHÄFTSZENTRUM — die Verkaufsseite.</b> Teildatei von
/// <see cref="MapEntityLayer"/>; der Kauf steht weiter in
/// <c>MapEntityLayer.MarketBuy</c>, weil er am Fenster hängt und nicht am Takt.
///
/// <para><b>Die ganze Kette ist am 18.08.2026 von Ende zu Ende gelesen worden</b>
/// — Stufe für Stufe, jede mit Adresse:</para>
/// <list type="number">
///   <item><b>Der Auslöser</b> ist Eintrag <b>4</b> der Befehlsliste
///   <c>0x4FD660</c> (30-Byte-Raster): <c>Angreifen · Bewegen · Beschützen ·
///   Selbstzerstörung · <b>Verkaufen</b> · Handsteuerung · …</c>. Der
///   Fensterverteiler @0x448746 nimmt den Aktionscode aus
///   <c>word[Fenster+0x0C+2·Knopf]</c> und springt für die 4 nach
///   <b>0x448800</b>. ⚠ <b>Verkaufen ist damit ein EINHEITENBEFEHL, kein Knopf
///   im Marktfenster</b> — und weder der Dialog noch der Behandler prüfen einen
///   Markt, eine Zelle oder eine Entfernung (beide Funktionen ganz gelesen).</item>
///   <item><b>Der Dialog</b> @0x446470 — das Spiel nennt ihn selbst
///   <c>»Sell window«</c> (0x4FE834). Er holt die <b>gewählte</b> Einheit aus
///   <c>word[0x4FA0C8]</c>, ruft die Wertfunktion 0x450F30 und rechnet
///   <b>3·Wert/10</b>; angezeigt wird <c>»Akzeptieren Sie $X für diese
///   Einheit?«</c> (0x4FC85C + 0x4FC8C0). <b>Kein Eingabefeld, keine
///   Plus/Minus-Tasten</b> — der Preis ist eine Ansage, keine Verhandlung.</item>
///   <item><b>Das Ja</b> @0x44B138 setzt Kommando <b>529</b> mit
///   P1 = Einheit, P2 = Preis ab. Siehe
///   <see cref="Simulation.Commands.CommandOp.Sell"/>.</item>
///   <item><b>Der Behandler</b> @0x4BFFF0 trägt {Einheit, Preis, Zustand 0xFF}
///   in die Angebotstafel <c>0xB4A0D0</c> ein (1000 × 6 Byte).</item>
///   <item><b>Der Markttick</b> @0x4C0260 macht den Rest — siehe
///   <see cref="MarketTradeTickOnce"/>.</item>
/// </list>
///
/// <para><b>⚠ WAS HIER UNSERE ENTSCHEIDUNG IST, und sie ist die des Spielers vom
/// 18.08.2026: der Handel läuft in Kampagne und Gefecht VERSCHIEDEN.</b>
/// Die Trennachse ist die bekannte, <c>UI.SkirmishSetup.CampaignMission &gt; 0</c>:</para>
/// <list type="bullet">
///   <item><b>Kampagne — originalgetreu.</b> Die verkaufte Einheit bleibt
///   stehen; ein Abholer fährt heran, und erst bei seiner Ankunft gibt es Geld.
///   Genau die Reihenfolge des Originals.</item>
///   <item><b>Gefecht — sofort.</b> Das Geld kommt im selben Takt, die Einheit
///   verschwindet im selben Takt. Ein Wettkampfmodus, in dem eine Einnahme
///   sechs Sekunden hinter der Entscheidung herläuft, ist schlechter zu spielen
///   und schlechter zu lesen; das Original war hier nie auf Wettkampf
///   ausgelegt.</item>
/// </list>
/// </summary>
public partial class MapEntityLayer
{
    // ================= die Angebotstafel (0xB4A0D0) ===========================

    /// <summary>Ein Verkaufsangebot — der Satz aus <c>0xB4A0D0</c>, 6 Byte:
    /// <c>{u16 Einheit, u16 Preis, u8 Zustand}</c>.</summary>
    public sealed class SellOffer
    {
        /// <summary>Satzindex der verkauften Einheit (im Original die
        /// Einheitsnummer; <c>0xFFFF</c> heisst »Platz frei«, was bei uns
        /// schlicht heisst: der Satz steht nicht in der Liste).</summary>
        public int Unit;

        /// <summary>Der Preis, den das SPIEL gerechnet hat (30 % des Werts).
        /// ⚠ Er wird beim Absetzen des Befehls festgeschrieben, nicht bei der
        /// Abholung — eine Einheit, die unterwegs beschossen wird, bringt
        /// trotzdem den vollen vereinbarten Preis.</summary>
        public int Price;

        /// <summary>Zustand. <b>0xFF</b> frisch eingetragen · <b>0</b> die
        /// Einheit steht und ist abholbereit · <b>1</b> ein Abholer ist
        /// unterwegs. Die drei Werte sind gelesen (@0x4C02C3, @0x4C035F).</summary>
        public int State;
    }

    private readonly List<SellOffer> _sellOffers = new();

    /// <summary>Die offenen Verkaufsangebote dieser Karte.</summary>
    public IReadOnlyList<SellOffer> SellOffers => _sellOffers;

    /// <summary>Wieviel verkauft und wieviel dafür bezahlt wurde — damit »der
    /// Handel läuft« eine Zahl hat und keine Behauptung ist. Zählt nur die
    /// abgeschlossenen Geschäfte, nicht die abgesetzten Befehle.</summary>
    public int SoldUnits, SoldMoney;

    /// <summary>Was zuletzt am Verkauf schiefging bzw. gelang — für Prüfstand
    /// und Statuszeile.</summary>
    public string SellNote = "";

    // ================= der Wert und die zwei Preise ===========================

    /// <summary>
    /// Der Wert einer Einheit auf der Karte — <see cref="UnitValue"/> mit den
    /// Feldern, die das Original dafür liest.
    ///
    /// <para>⚠ <b>Der Entwurf kommt über den PLATZ.</b> @0x450F69 rechnet
    /// <c>ent[+0x43] + 200·Spieler</c>, und genau das ist
    /// <see cref="Entity.Mark"/> plus <c>200·Owner</c>. Wer hier die
    /// Listenstelle nähme, bekäme den Preis eines fremden Entwurfs — dieselbe
    /// Falle, die am 17.08. an der Marktware elf von zwanzig Zeilen still
    /// verwechselt hat.</para>
    ///
    /// <para>Gibt <c>-1</c>, wenn der Entwurf nicht auffindbar ist. ⚠ Das ist
    /// ausdrücklich KEINE 0: eine 0 sähe wie »wertlos« aus und wäre ein Preis;
    /// −1 heisst »nicht rechenbar« und muss vom Aufrufer behandelt werden.</para>
    /// </summary>
    public int UnitValueOf(int idx)
    {
        if (idx < 0 || idx >= _entities.Count) return -1;
        var e = _entities[idx];
        if (e.IsBuilding || e.IsProp || e.Dead) return -1;
        LoadDesigns();
        if (e.Mark < 0) return -1;
        int owner = e.Owner is >= 0 and <= 7 ? e.Owner : 0;
        var d = DesignBySlot(e.Mark + 200 * owner);
        if (d == null) return -1;
        int hullMax = d.Value.Derived.Hp > 0 ? d.Value.Derived.Hp : e.HpMax;
        return UnitValue.Of(d.Value.Derived.CostW, d.Value.Derived.CostF,
                            d.Value.Derived.CostS, e.Hp, hullMax, e.Field28);
    }

    /// <summary>Was der Spieler für diese Einheit bekommt: 30 % des Werts,
    /// gerechnet wie im Dialog @0x4464D7. <c>-1</c> = nicht rechenbar.</summary>
    public int SellPriceOf(int idx)
    {
        int v = UnitValueOf(idx);
        return v < 0 ? -1 : UnitValue.SellPrice(v);
    }

    // ================= der Markttick ==========================================

    /// <summary>
    /// ⚠ <b>Der Markttick läuft auf dem TAKT DES ORIGINALS, nicht auf unserem.</b>
    ///
    /// <para>Das Original zählt <c>dword[0x4FA240]</c> bei <b>50 Hz</b>
    /// (<c>SetTimer 20 ms</c> @0x415BC5, siehe
    /// <see cref="OriginalTicksPerSecond"/>) und hängt seine drei Phasen an
    /// <c>%100 == 77</c>, <c>%300 == 111</c> und <c>%300 == 222</c>. Wir laufen
    /// mit <c>SimHz = 60</c>. Statt diese drei Zahlen umzurechnen — und damit
    /// aus gelesenen Zahlen gerechnete zu machen — führen wir <b>einen eigenen
    /// Zähler im Takt des Originals</b> und übernehmen die Bedingungen
    /// wörtlich.</para>
    ///
    /// <para>Die Umrechnung ist ganzzahlig und damit auf jeder Maschine
    /// dieselbe: je Simulationstakt kommen <c>50</c> Punkte dazu, bei
    /// <c>60</c> fällt ein Originaltakt an. Kein Fliesskomma, keine Rundung,
    /// kein Drift — das ist die Bedingung dafür, dass zwei Maschinen im
    /// Lockstep denselben Markt sehen.</para>
    /// </summary>
    private int _origAcc;

    /// <summary>Der Zähler des Originals, <c>dword[0x4FA240]</c>.
    ///
    /// <para>⚠ <b>Umbenannt am 18.08.2026 von <c>_marketTicks</c>.</b> Er ist
    /// nicht der Zähler des Marktes, sondern <b>der des Spiels</b> — beim
    /// Dock-Auslauf hängt eine zweite Mechanik daran (<c>%20 == 11</c>,
    /// @0x409C8E), und unter dem alten Namen hätte die nächste Mechanik einen
    /// zweiten Zähler bekommen. Zwei Zähler für dieselbe Uhr wären zwei
    /// Wahrheiten über die Zeit.</para></summary>
    private int _origTicks;

    /// <summary>Gehört in <c>SimTick</c>. Treibt den Takt des Originals und
    /// ruft alles, was daran hängt, so oft, wie dort Takte vergangen
    /// wären.</summary>
    private void OriginalTick()
    {
        _origAcc += (int)OriginalTicksPerSecond;      // 50
        while (_origAcc >= SimHz)                     // 60
        {
            _origAcc -= SimHz;
            _origTicks++;
            MarketTradeTickOnce();
            // Auftrag 52, der Auslauf aus dem Dock — @0x409C8E prüft
            // `[0x4FA240] % 20 == 11`, also eine feste Phase, nicht »irgendwann
            // in zwanzig Takten«.
            if (_origTicks % 20 == 11) ShipLeaveDockTick();
            // Die Stromabrechnung — @0x4161C4 prüft `% 50 == 13`, also einmal
            // je Sekunde des Originals. Siehe Simulation/Power.cs.
            if (_origTicks % PowerPeriod == PowerPhase) PowerTick();
            // Auftrag 0, der Leerlauf — dort holt ein Baufahrzeug seinen
            // vorgemerkten Bauauftrag ab (@0x407F38 → @0x40806A / @0x4082DD).
            // ⚠ JEDEN Takt und ohne Phase: der Leerlaufverteiler hängt an
            // keiner Modulo-Bedingung, er läuft, sobald der Fahrauftrag zu
            // Ende ist. Siehe Simulation/BuildOrders.cs.
            BuildArrivalTick();
        }
    }

    /// <summary>
    /// EIN Originaltakt des Markts — @0x4C0260, die Verkaufsphasen.
    ///
    /// <para><b>Phase A, jeden Takt</b> (@0x4C026B): jedes Angebot mit Zustand
    /// <c>0xFF</c> bekommt seinen Auftrag gelöscht (<c>byte[ent+0x14] = 0</c>);
    /// und <b>sobald die Einheit STEHT</b> — <c>byte[ent+0x04] == 0xFF</c>,
    /// also die Fahrtrichtung »keine« — geht der Zustand auf <b>0</b>. Das ist
    /// die Bedingung, an der das Original wartet: eine fahrende Einheit wird
    /// nicht abgeholt.</para>
    ///
    /// <para><b>Phase B, <c>%300 == 111</c></b> (@0x4C030F): das erste Angebot
    /// mit Zustand 0 bekommt Zustand <b>1</b>, und ein Abholer wird angelegt —
    /// <c>»New ship type 1 for:«</c> (0x5390A4), Art 1, bei Spalte <b>−10</b>
    /// auf der Zeile der Einheit, Ziel ihre Spalte.</para>
    ///
    /// <para>⚠ <b>Nicht gebaut, und der Grund gehört dazu</b> (Regel 26): die
    /// Phase <c>%100 == 77</c> ist der <b>Nachschub des Ladens</b> (@0x4C0E40)
    /// und <c>%300 == 222</c> die <b>Lieferung gekaufter Ware</b> (@0x4C03BD).
    /// Beide sind gelesen, beide sind eigene Bauwerke, und beide gehören nicht
    /// zum Verkauf. Sie stehen als eigener Posten an; hier wird nichts
    /// hingeschrieben, was sie halb täte.</para>
    /// </summary>
    private void MarketTradeTickOnce()
    {
        // Phase A — dieselbe Reihenfolge wie @0x4C026B.
        foreach (var o in _sellOffers)
        {
            if (o.State != 0xFF) continue;
            if (o.Unit < 0 || o.Unit >= _entities.Count) continue;
            var u = _entities[o.Unit];
            if (u.Dead) continue;
            // byte[ent+0x14] = 0 — der laufende Auftrag wird gelöscht. Bei uns
            // ist der Auftrag kein Byte, sondern Weg und Reihe.
            u.Path = null;
            u.Orders.Clear();
            u.Target = -1;
            // »Steht sie?« — im Original <c>byte[ent+0x04] == 0xFF</c>, das Feld
            // für die FAHRTRICHTUNG, das beim Anhalten auf 0xFF gesetzt wird
            // (@0x409b12, @0x407af8).
            //
            // ⚠ Hier stand zuerst zusätzlich <c>u.Path == null</c> — und das
            // ist zwei Zeilen über der Stelle, die Path gerade selbst auf null
            // gesetzt hat, also IMMER wahr. Ein Prüfstand hätte »wartet
            // korrekt, bis sie steht« gemeldet, ohne dass je gewartet worden
            // wäre (Regel EE). Was bei uns »fährt noch« heisst, ist die
            // reservierte Nachbarzelle: den angefangenen Zellschritt macht die
            // Einheit fertig, auch wenn ihr Weg weg ist.
            if (u.Reserved == null) o.State = 0;
        }

        // Phase B — @0x4C030F, alle 300 Originaltakte (6 Sekunden).
        if (_origTicks % 300 == 111)
        {
            foreach (var o in _sellOffers)
            {
                // Das Original überspringt leere Plätze und solche mit einem
                // anderen Zustand und nimmt den ERSTEN mit Zustand 0 — es hört
                // nicht auf zu suchen (@0x4C0317..0x4C0331).
                if (o.State != 0) continue;
                if (o.Unit < 0 || o.Unit >= _entities.Count) continue;
                var u = _entities[o.Unit];
                if (u.Dead) continue;

                // ⚠ ORIGINALVERHALTEN, und es sieht wie ein Fehler aus, weil es
                // einer ist: der Zustand geht auf 1, BEVOR der Abholer angelegt
                // wird (@0x4C035F vor @0x4C039C). Sind alle zwanzig Plätze
                // belegt, gibt `new_ship` @0x4C01ED nur eine Protokollzeile
                // zurück — und das Angebot bleibt für immer auf 1 stehen, also
                // unbezahlt. Nachgebaut wie es dasteht; blockiert keine Mission
                // (es braucht 21 gleichzeitige Verkäufe), und der Prüfstand
                // weist solche Angebote getrennt aus, statt sie zu verstecken.
                o.State = 1;
                SpawnCollector(o, u);
                break;                       // genau EINES je Takt, wie gelesen
            }
        }

        // Phase C — @0x4C02E6, alle 100 Originaltakte (2 Sekunden): der
        // NACHSCHUB des Ladens.
        if (_origTicks % 100 == 77) ShopRestock();

        // Phase D — @0x4C03B1, alle 300 Originaltakte: die LIEFERUNG gekaufter
        // Ware.
        if (_origTicks % 300 == 222) DeliveryPhase();

        CollectorTick();
    }

    // ================= DIE LIEFERUNG GEKAUFTER WARE ===========================
    //
    // @0x4C03BD, und sie ist die Umkehrung des Nachschubs:
    //
    //   suche den ersten Ladenplatz mit Preis == 0xFFFF          @0x4C03C8
    //   c   = byte[0xB49C88 + Platz]                             das ZIELGEBAEUDE
    //   new_ship(-10, word[bld+0x02], word[bld+0x00], 2)         @0x4C043A
    //   Ladung = alle weiteren Plaetze mit Preis 0xFFFF UND demselben Ziel,
    //            hoechstens 20                                   @0x4C045A..0x4C0493
    //
    // ⚠ `word[0xC06910 + 76*c + 0x00]` ist die SPALTE und `+0x02` die ZEILE des
    // Gebaeudes. Das sieht gegen GAMESTATE_RE falsch aus (dort steht »+0x00
    // typ«) und ist es nicht: der Gebaeudesatz traegt seine Position in den
    // ERSTEN VIER BYTES, der Typ sitzt bei +0x04. Das ist die bekannte
    // Vier-Byte-Verschiebung (Regel M), und Import/CwmData.cs sagt es im
    // Kopfkommentar bereits selbst.

    /// <summary>Ein Abholer nimmt höchstens so viele Stücke mit:
    /// <c>cmp al, 0x14</c> @0x4C0491.</summary>
    private const int CollectorCargo = 20;

    /// <summary>
    /// <b>Wohin geliefert wird</b> — im Original der dritte Parameter des
    /// Kaufbefehls 530, den das Marktfenster mitgibt (@0x44C6BD liest ihn aus
    /// dem Fensterfeld <c>+0x8C3CD8</c>).
    ///
    /// <para>⚠ <b>UNSERE SETZUNG, und sie ist eine Lücke mit Ansage:</b> das
    /// Original lässt den Käufer das Zielgebäude WÄHLEN; unser Marktfenster hat
    /// dafür keinen Platz, und wie das Original die Auswahl anbietet, ist nicht
    /// gelesen. Wir nehmen das <b>eigene Gebäude, das dem Markt am nächsten
    /// liegt</b> — die Wahl, die ein Spieler fast immer treffen würde, und die
    /// einzige, die ohne neue Oberfläche auskommt.</para>
    ///
    /// <para>⚠ <b>Warum überhaupt ein Gebäude und nicht der Markt selbst:</b>
    /// weil das Original an ein Gebäude liefert und der Markt keinem Spieler
    /// gehört (Besitzer 255 auf allen 41 Sätzen). Ohne eigenes Gebäude gibt es
    /// im Original niemanden, der die Ware annehmen könnte — der Kauf wird dann
    /// abgelehnt, statt die Ware ins Nichts zu schicken.</para>
    /// </summary>
    /// <returns>Der Satzindex des Zielgebäudes, oder −1.</returns>
    private int DeliveryTargetFor(int owner, Entity markt)
    {
        int best = -1;
        long bd = long.MaxValue;
        for (int i = 0; i < _entities.Count; i++)
        {
            var b = _entities[i];
            if (!b.IsBuilding || b.IsProp || b.Dead || b.Owner != owner) continue;
            long dx = b.Col - markt.Col, dy = b.Row - markt.Row;
            long d = dx * dx + dy * dy;
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    /// <summary>Die Lieferphase, @0x4C03BD. <b>Ein Abholer je Takt</b>, und er
    /// nimmt alles mit, was zum selben Ziel geht.</summary>
    private void DeliveryPhase()
    {
        if (_collectors.Count >= CollectorSlots) return;

        MarketOffer? erste = null;
        foreach (var o in _market)
            if (o.Sold && o.TargetBuilding >= 0) { erste = o; break; }
        if (erste == null) return;

        int ziel = erste.TargetBuilding;
        if (ziel < 0 || ziel >= _entities.Count) { _market.Remove(erste); return; }
        var b = _entities[ziel];
        if (b.Dead)
        {
            // ⚠ Das Ziel ist zerstoert. Das Original prueft das hier NICHT —
            // es liest die Zelle des Satzes, wie er dasteht. Wir brechen die
            // Lieferung ab, statt an eine Ruine zu fahren; das ist unsere
            // Abweichung und sie steht hier, weil sie sonst wie ein Befund
            // aussaehe.
            _market.Remove(erste);
            ShopNote = "das Zielgebaeude ist zerstoert — die Lieferung entfaellt";
            return;
        }

        var s = new Collector
        {
            Col = CollectorStartCol, Row = b.Row, Target = b.Col,
            Frac = 0, Kind = 2, Cargo = new List<MarketOffer>(), CargoTarget = ziel,
        };
        // Alles, was zum selben Ziel geht — bis zu zwanzig (@0x4C0491).
        foreach (var o in _market)
        {
            if (!o.Sold || o.TargetBuilding != ziel) continue;
            s.Cargo.Add(o);
            if (s.Cargo.Count >= CollectorCargo) break;
        }
        foreach (var o in s.Cargo) o.TargetBuilding = -2;   // vergeben, nicht doppelt laden
        _collectors.Add(s);
    }

    /// <summary>
    /// <b>Die Ankunft der Lieferung</b> — @0x4C067F, und je Stück
    /// <c>deliver_one</c> @0x4C1480.
    ///
    /// <para>Das Original sucht einen freien Platz neben dem Gebäude, nimmt
    /// einen freien Einheitensatz des KÄUFERS und <b>kopiert die 78 Byte des
    /// Ladensatzes hinüber</b> — die Ware IST ein fertiger Einheitensatz, kein
    /// Entwurf. Danach ist der Ladenplatz mit <b>Preis 0</b> wieder frei
    /// (@0x4C1526), und ein Effekt <c>0x60</c> läuft an der Stelle.</para>
    ///
    /// <para>⚠ Findet es keinen Platz, meldet es <c>»Incredible error ...no
    /// free place for new robot«</c> (0x539198) und das Stück ist <b>weg</b> —
    /// der Ladenplatz wird trotzdem geleert. Bezahlt ist es längst. Wir halten
    /// den Platz in dem Fall belegt und versuchen es beim nächsten Abholer
    /// wieder; <b>unsere Abweichung</b>, und zwar eine, die dem Spieler sein
    /// Geld nicht wegnimmt.</para></summary>
    private void DeliverCargo(Collector s)
    {
        if (s.Cargo == null || s.Cargo.Count == 0) return;
        int geliefert = 0, verschoben = 0;
        foreach (var o in s.Cargo)
        {
            int owner = o.Buyer is >= 0 and <= 7 ? o.Buyer : 0;
            var cell = _nav?.NearestFree(new Vector2I(s.Target, s.Row),
                                         NavGrid.MoveClass.Vehicle);
            if (cell == null || !MarketSpawn(o, cell.Value, owner))
            {
                o.TargetBuilding = s.CargoTarget;    // zurueck in die Schlange
                verschoben++;
                continue;
            }
            _market.Remove(o);
            geliefert++;
        }
        ShopNote = verschoben == 0
            ? $"Lieferung: {geliefert} Stueck an ({s.Target},{s.Row})"
            : $"Lieferung: {geliefert} Stueck an ({s.Target},{s.Row}), " +
              $"{verschoben} zurueckgestellt — kein freier Platz";
        MarketDelivered += geliefert;
        UpdatePanel();
        QueueRedraw();
    }

    /// <summary>Wieviele gekaufte Stücke wirklich angekommen sind. ⚠ Regel 33:
    /// ohne diese Zahl ist »die Lieferung läuft« nicht von »der Abholer fährt
    /// und liefert nichts ab« zu unterscheiden.</summary>
    public int MarketDelivered;

    // ================= DER NACHSCHUB DES LADENS ===============================
    //
    // @0x4C0E40, und alles daran ist gelesen. Der Einstieg im Markttick ist
    // `[0x4FA240] % 100 == 77`; von dort:
    //
    //   bl = shop_count()                    @0x4C0860 — wieviel liegt aus
    //   if (byte[0x53904c] <= bl) return     die Schwelle, und sie ist 15
    //   bl = rand() % 10                     @0x4C0E6B
    //   while (bl--) add_shop_item(0xFFFF)   @0x4C0E8F
    //
    // Das Spiel schreibt dabei seine eigenen Zeilen mit: »kolik:« (0x53917C,
    // tschechisch »wieviel«) und »add:« (0x539174).

    /// <summary>Der Laden hat <b>50</b> Plätze — <c>0x81A3A8</c>, 50 Wörter,
    /// und die Schleifen zählen alle gegen <c>0x32</c>.</summary>
    private const int ShopSlots = 50;

    /// <summary>Unter dieser Zahl wird nachgelegt: <c>byte[0x53904C] = 0x0F</c>.
    /// ⚠ Die Zahl steht als Byte in der EXE, nicht im Code — sie ist gemessen,
    /// nicht aus einem Vergleich erschlossen.</summary>
    private const int ShopStockTarget = 15;

    /// <summary>Wieviel EXOTISCHE Ware der Laden vorhalten will:
    /// <c>Schwelle / 6</c> @0x4C0A8E..0x4C0A97, also <b>2</b>. ⚠ Die 6 ist eine
    /// Konstante im Code und die 15 ein Datenbyte — dass beide zusammengehören,
    /// steht nur in dieser Division.</summary>
    private const int ShopExoticTarget = ShopStockTarget / 6;

    /// <summary>Höchstens so viele Stücke je Nachschub: <c>rand() % 10</c>.
    /// ⚠ Das heisst auch: in einem von zehn Fällen kommt <b>gar nichts</b>.</summary>
    private const int ShopRestockRoll = 10;

    /// <summary>Der Suchbereich der Entwürfe: <c>bx = 50; while (bx &lt; 200)</c>
    /// @0x4C0AA5/@0x4C0AF4, mit einer Lücke <c>100..109</c> @0x4C0AA9..0x4C0AB3.
    ///
    /// <para>⚠ <b>Ohne Spielerversatz</b>, und das ist kein Versehen: die
    /// Ladenwertfunktion @0x451010 teilt den <b>Ladenplatz</b> (0..49) durch
    /// 1000 und bekommt damit immer <b>Spieler 0</b>. Der Laden rechnet also
    /// durchweg mit den Entwürfen von Spieler 0, und genau deshalb sucht er
    /// auch dort.</para></summary>
    private const int ShopSlotFirst = 50, ShopSlotLast = 199;
    private const int ShopSlotSkipFrom = 100, ShopSlotSkipTo = 109;

    /// <summary>
    /// <c>byte[Entwurf+0x18] &gt;= 0xA0</c> @0x4C0AC7 — und <b>+0x18 ist das
    /// FAHRWERK</b>, gemessen an 601 von 601 Sätzen gegen unsere eigene
    /// Ausfuhr.
    ///
    /// <para><b>Was der Filter bedeutet:</b> er wirft die INFANTERIE hinaus.
    /// Fussoldaten tragen Fahrwerk 148/149, Fahrzeuge 160 und darüber. Der
    /// Laden verkauft also nie einen Soldaten.</para>
    ///
    /// <para><b>Gegenprobe an den Daten</b>, und sie ist eindeutig: die Ware,
    /// die auf den 13 Gefechtskarten wirklich liegt, erfüllt diesen Filter
    /// <b>225 von 225 Mal</b>, ohne ein einziges Gegenbeispiel. Die Karten sind
    /// mit genau diesem Generator bestückt worden.</para></summary>
    private const int ShopMinPropulsion = 0xA0;

    /// <summary>Drei Waffen, die die Erzeugung am Ende doch verwirft
    /// (@0x4C0F35..0x4C0F47): <b>65</b> Teleporter, <b>71</b> Transporter,
    /// <b>78</b> Terranium Finder.
    ///
    /// <para>⚠ Sie werden erst NACH der Auswahl geprüft. Fällt die Wahl auf so
    /// einen Entwurf, verpufft der Durchgang — der Laden bekommt nichts, und
    /// einer der <c>rand()%10</c> Versuche ist verbraucht. Nachgebaut wie es
    /// dasteht.</para></summary>
    private static readonly int[] ShopBannedWeapons = { 65, 71, 78 };

    /// <summary>Wieviel liegt aus — <c>shop_count()</c> @0x4C0860: die Plätze
    /// mit <c>Preis &gt; 0</c>. ⚠ <b>Vorzeichenbehaftet</b> verglichen
    /// (<c>jle</c>), also zählt ein verkaufter Platz (Preis 0xFFFF = −1)
    /// <b>nicht</b> mit — er ist weder leer noch im Angebot.
    ///
    /// <para>Das ist keine Spitzfindigkeit: <b>der Nachschub legt für gekaufte
    /// Ware sofort nach</b>, obwohl deren Platz noch belegt ist. Wer die
    /// verkauften mitzählte, bekäme einen Laden, der erst nach der Lieferung
    /// wieder auffüllt.</para></summary>
    private int ShopOnShelf()
    {
        int n = 0;
        foreach (var o in _market) if (!o.Sold) n++;
        return n;
    }

    /// <summary>Wieviel davon ist EXOTISCH — dieselbe Sperrprüfung, aber auf
    /// dem Ladensatz statt auf dem Entwurf (@0x4C0890, Felder +0x3d/+0x3e/+0x3f
    /// des Satzes). Bei uns steht der Entwurf im Angebot, also fragen wir ihn
    /// direkt.</summary>
    private int ShopExoticOnShelf()
    {
        int n = 0;
        foreach (var o in _market) if (!o.Sold && ShopDesignLocked(o.Design)) n++;
        return n;
    }

    /// <summary>Was der letzte Nachschub getan hat — für Prüfstand und
    /// Protokoll.</summary>
    public string ShopNote = "";
    public int ShopAdded;

    /// <summary>Der Nachschub selbst, @0x4C0E40.</summary>
    private void ShopRestock()
    {
        if (ShopOnShelf() >= ShopStockTarget) return;
        int n = Determinism.Roll(ShopRestockRoll);
        int vorher = _market.Count;
        for (int i = 0; i < n; i++) ShopAddRandom();
        int neu = _market.Count - vorher;
        ShopAdded += neu;
        ShopNote = $"Nachschub: {vorher} lagen aus, {n} versucht, {neu} dazu " +
                   $"(jetzt {_market.Count}, davon {ShopExoticOnShelf()} exotisch)";
    }

    /// <summary>
    /// <b>Ein zufälliges Stück in den Laden legen</b> — @0x4C0A70, der Weg, den
    /// <c>add_shop_item(0xFFFF)</c> nimmt.
    ///
    /// <para><b>Zwei Listen, und der Unterschied ist die Freigabe.</b> Das
    /// Original läuft über die Entwurfsplätze und fragt für jeden
    /// <c>design_locked()</c> @0x4C0980: hat eines der drei Bauteile
    /// (+0x17 Waffe, +0x18 Fahrwerk, +0x19 Aufbau) im Bauteilsatz 0x5045A0 die
    /// Freigabe <c>+0x00 == 0</c>, dann ist der Entwurf <b>gesperrt</b> und
    /// kommt nach <c>0xB49D88</c> — sonst nach <c>0xB49CC0</c>.</para>
    ///
    /// <para><b>Welche Liste gezogen wird:</b> die exotische, solange weniger
    /// als <see cref="ShopExoticTarget"/> gesperrte Stücke ausliegen; sonst die
    /// freie. Und dann zwei Notausgänge, die im Original ausdrücklich
    /// dastehen (@0x4C0AFB, @0x4C0B05): ist die eine Liste leer, wird die
    /// andere genommen. Sind beide leer, sagt es <c>»Create new market brush
    /// error A«</c> bzw. <c>»B«</c> und legt nichts hin.</para>
    ///
    /// <para><b>⚠ Warum das die MECHANIK des Marktes ist und nicht Beiwerk:</b>
    /// gemessen am Fahrplan der Kampagne hat Spieler 0 bei <b>Mission 1 null</b>
    /// freigegebene Bauteile — <b>alle 52</b> Kandidaten sind dort gesperrt, der
    /// Laden ist also früh die <b>einzige</b> Quelle für Fahrzeuge. Bei
    /// <b>Mission 32</b> sind es <b>51 frei / 1 gesperrt</b>, und er wird zur
    /// Bequemlichkeit. Das ist kein Zufallsgenerator, das ist ein
    /// Spannungsbogen.</para></summary>
    private void ShopAddRandom()
    {
        // »Cannot add new unit to market-store« (0x539148)
        if (_market.Count >= ShopSlots) { ShopNote = "der Laden ist voll"; return; }
        LoadDesigns();
        if (_designs == null) return;

        var gesperrt = new List<int>();
        var frei = new List<int>();
        for (int s = ShopSlotFirst; s <= ShopSlotLast; s++)
        {
            if (s >= ShopSlotSkipFrom && s <= ShopSlotSkipTo) continue;
            var d = DesignBySlot(s);
            if (d == null) continue;
            if (d.Value.Propulsion < ShopMinPropulsion) continue;
            (ShopDesignLocked(s) ? gesperrt : frei).Add(s);
        }

        bool exotisch = ShopExoticOnShelf() < ShopExoticTarget;
        if (gesperrt.Count == 0) exotisch = false;      // @0x4C0AFB
        if (frei.Count == 0) exotisch = true;           // @0x4C0B05
        var liste = exotisch ? gesperrt : frei;
        if (liste.Count == 0)
        {
            ShopNote = exotisch
                ? "Create new market brush error A — keine gesperrten Entwuerfe"
                : "Create new market brush error B — keine freien Entwuerfe";
            return;
        }

        int slot = liste[Determinism.Roll(liste.Count)];
        ShopCreate(slot);
    }

    /// <summary>
    /// <b>Ist dieser Entwurf gesperrt?</b> — @0x4C0980.
    ///
    /// <para><b>Im Original</b> ist die Frage einfach: hat eines der drei
    /// Bauteile im Bauteilsatz 0x5045A0 die Freigabe <c>+0x00 == 0</c>? Das
    /// Byte ist Laufzeitzustand, den die Missionsskripte über
    /// <c>set_part(Spieler, Teil, Wert)</c> setzen.</para>
    ///
    /// <para><b>Bei uns kommt es aus zwei verschiedenen Quellen</b>, und die
    /// Trennung ist benannt:</para>
    /// <list type="bullet">
    ///   <item><b>Kampagne</b> — aus dem FAHRPLAN, also genau dem, was
    ///   <c>set_part</c> bis zu dieser Mission geschaltet hat
    ///   (<c>CampaignManager.UnlocksFor(m).Parts</c>, Spieler 0). Das ist die
    ///   Eingabe des Originals, nur aus der Ausfuhr statt aus dem
    ///   Laufzeitbyte.</item>
    ///   <item><b>Gefecht</b> — ⚠ <b>UNSERE ERSATZQUELLE</b>: der
    ///   TECHSTANDARD gegen die Techstufe des Bauteils
    ///   (<c>DesignMath.TechLevel</c>, stats +0x24). Das Gefecht hat keinen
    ///   Fahrplan, und woher das Freigabebyte dort seinen Wert bekommt, ist
    ///   <b>nicht gelesen</b>. Der Techstandard ist die Schranke, die das
    ///   Original im Gefecht sonst benutzt (Tor @0x419F30,
    ///   <c>stats[Teil].+0x24 &lt;= Techstandard</c>) — also die nächstgelegene
    ///   gelesene Grösse. <b>Sie ist nicht dieselbe Zahl</b>, und das steht
    ///   hier, statt es gleich aussehen zu lassen.</item>
    /// </list>
    ///
    /// <para>Ist keine Quelle da, gilt <b>nichts als gesperrt</b>. Der Laden
    /// zieht dann nur aus der freien Liste — genau das, was das Original bei
    /// leerer Sperrliste auch tut (@0x4C0AFB). Eine Lücke, die sich wie ein
    /// gelesener Fall verhält, statt wie ein erfundener.</para></summary>
    private bool ShopDesignLocked(int slot)
    {
        var d = DesignBySlot(slot);
        if (d == null) return false;
        int w = d.Value.Weapon, p = d.Value.Propulsion, e = d.Value.Equip;

        int mission = UI.SkirmishSetup.CampaignMission;
        if (mission > 0)
        {
            var u = Campaign.CampaignManager.UnlocksFor(mission);
            if (!u.Known) return false;
            var mine = u.Components;
            foreach (int c in new[] { w, p, e })
                if (c != 0 && !mine.Contains(c)) return true;
            return false;
        }

        int tech = UI.SkirmishSetup.Techstandard;
        if (tech <= 0) return false;
        foreach (int c in new[] { w, p, e })
        {
            if (c == 0) continue;
            int lvl = DesignMath.TechLevel(c);
            if (lvl > 0 && lvl > tech) return true;    // -1 = unbekannt, zaehlt nicht
        }
        return false;
    }

    /// <summary>
    /// <b>Das Stück anlegen</b> — <c>create_shop_unit</c> @0x4C0EC0, auf das
    /// reduziert, was bei uns ein Angebot ausmacht.
    ///
    /// <para>Das Original füllt hier einen ganzen 78-Byte-Einheitensatz in
    /// sec94 (Blickrichtung 0xFF, zwei Zufallsbytes auf +0x02/+0x03, die
    /// Bauteile nach +0x3b..+0x3f, der Entwurf nach +0x43, die gewürfelte
    /// Erfahrung nach <b>+0x28</b> @0x4C0FF6). Wir führen ein Angebot als Satz
    /// mit den Werten, die wir daraus brauchen — der Rest entsteht beim Kauf
    /// aus dem Entwurf.</para>
    ///
    /// <para><b>Der Preis kommt zuletzt und aus derselben Formel wie überall:</b>
    /// @0x4C1194 ruft die Ladenwertfunktion und rechnet <c>25·Wert/10</c>. Weil
    /// der Wert die Erfahrung enthält, kostet derselbe Entwurf mit Stufe 6 das
    /// <b>Siebzigfache</b> von dem mit Stufe 0.</para></summary>
    private void ShopCreate(int slot)
    {
        var d = DesignBySlot(slot);
        if (d == null) return;

        // ⚠ Erst würfeln, DANN verwerfen — das Original tut es in dieser
        // Reihenfolge, und der verworfene Versuch ist trotzdem verbraucht.
        int exp = ShopRollExperience(d.Value.Weapon);
        foreach (int b in ShopBannedWeapons)
            if (d.Value.Weapon == b)
            {
                ShopNote = $"Entwurf {slot} \"{d.Value.Name}\" hat Waffe {b} — verworfen";
                return;
            }

        int hull = d.Value.Derived.Hp;
        int wert = UnitValue.Of(d.Value.Derived.CostW, d.Value.Derived.CostF,
                                d.Value.Derived.CostS, hull, hull, exp);
        _market.Add(new MarketOffer
        {
            Slot = -1,
            Price = UnitValue.ShopPrice(wert),
            Design = slot,
            UnitType = d.Value.Propulsion,
            Attack = d.Value.Attack, Defence = d.Value.Defence,
            Energie = hull, Speed = d.Value.Speed,
            Sight = d.Value.Sight, Range = d.Value.Range,
            Experience = exp,
            Name = d.Value.Name,
        });
    }

    /// <summary>
    /// <b>Die Erfahrung, die der Laden auswürfelt</b> — @0x4C0B97..0x4C0C48.
    ///
    /// <para><b>Erst die Frage, ob überhaupt gewürfelt wird.</b> Eine
    /// Sprungtafel über <c>Waffe − 3</c> (Bereich 0..16, Tafel 0x4C0C70 mit
    /// Index 0x4C0C84) schickt die Waffen <b>3, 9, 14, 15, 16, 19</b> auf
    /// »keine Erfahrung«, und <c>Waffe &gt;= 50</c> ebenfalls (@0x4C0BC9).
    /// Aufgeschlüsselt heisst das: <b>Kampfeinheiten werden erfahren,
    /// Nutzfahrzeuge nicht</b> — die stumme Gruppe sind Radar, Reparatur,
    /// Konstruktion, Minen- und Fallenleger, Teleporter, Muddinger.</para>
    ///
    /// <para><b>Dann der Wurf</b>, ein Byte 0..255:
    /// unter 150 nichts · 150..209 → <b>6</b> · 210..234 → <b>21</b> ·
    /// 235..249 → <b>42</b> · ab 250 ein zweiter Wurf <c>rand()%100</c>:
    /// unter 50 → <b>77</b>, 50..79 → <b>112</b>, 80..99 → <b>172</b>.</para>
    ///
    /// <para><b>⚠ Und hier steht der Beleg, dass diese Zahlen wirklich die
    /// Erfahrung sind:</b> die Stufenschwellen der Wertfunktion sind
    /// 5/20/40/75/110/170/254/255 — und <b>jeder</b> der sieben Werte liegt
    /// genau EINS über einer Schwelle (6&gt;5, 21&gt;20, 42&gt;40, 77&gt;75,
    /// 112&gt;110, 172&gt;170). Sieben von sieben, kein Ausreisser. Sie sind
    /// gesetzt worden, um je eine Stufe zu treffen.</para>
    ///
    /// <para>⚠ <b>TOTER CODE im Original:</b> ein vierter Zweig setzt
    /// <b>255</b> (Stufe 7, Faktor 10,00) für <c>rand()%100 &gt;= 100</c>
    /// @0x4C0C3A — was nie eintreten kann. <b>Die höchste Veteranenstufe
    /// erscheint im Laden also niemals.</b> Nachgebaut wie es dasteht, samt
    /// dem Grund.</para></summary>
    private static int ShopRollExperience(int weapon)
    {
        if (weapon >= 50) return 0;                                  // @0x4C0BC9
        if (weapon is 3 or 9 or 14 or 15 or 16 or 19) return 0;      // Sprungtafel 0x4C0C84

        int r = Determinism.Roll(256);                               // rand() & 0xFF
        if (r < 150) return 0;
        if (r < 210) return 6;
        if (r < 235) return 21;
        if (r < 250) return 42;
        int r2 = Determinism.Roll(100);                              // rand() % 100
        if (r2 < 50) return 77;
        if (r2 < 80) return 112;
        return 172;
        // ⚠ Der Zweig `r2 >= 100 -> 255` des Originals fehlt hier nicht, er ist
        // unerreichbar: rand()%100 gibt 0..99. Siehe Kopfkommentar.
    }

    // ================= der Abholer (0xB49E50, 20 Sätze zu 32 Byte) ============

    /// <summary>
    /// Der Abholer des Geschäftszentrums — der Satz aus <c>0xB49E50</c>.
    ///
    /// <para>Das Original nennt ihn in seiner eigenen Meldung <c>»New ship type
    /// 1«</c>. Er wird bei <b>Spalte −10</b> angelegt, also ausserhalb der
    /// Karte, und fährt auf der Zeile der verkauften Einheit nach rechts, bis er
    /// ihre Spalte erreicht (@0x4C0260, Zustandsautomat ab 0x4C049F).</para>
    ///
    /// <para>⚠ <b>Was wir NICHT bauen, und warum</b> (Regel 26): sein BILD.
    /// Ob und wie das Original ihn zeichnet, ist nicht gelesen — der Satz trägt
    /// kein Bauteil, keinen Entwurf und keine Blickrichtung, und ein Zeichner,
    /// der ihn liest, ist nicht nachgewiesen. Ihm hier ein Sprite zu geben wäre
    /// eine Erfindung an der sichtbarsten Stelle des Spiels. Er fährt also
    /// unsichtbar, und die Wirkung — die Wartezeit und die Auszahlung genau bei
    /// Ankunft — ist die gelesene. <b>Das ist eine Lücke, und sie steht hier,
    /// statt gefüllt zu werden.</b></para>
    /// </summary>
    public sealed class Collector
    {
        /// <summary>+0x00, die Spalte. Startet bei <b>−10</b>.</summary>
        public int Col;
        /// <summary>+0x02, die Zeile — die der verkauften Einheit.</summary>
        public int Row;
        /// <summary>+0x04, die Zielspalte.</summary>
        public int Target;
        /// <summary>+0x06, der Rest der Bruchrechnung beim Abbremsen.</summary>
        public int Frac;
        /// <summary>+0x07, die Art. <b>1</b> = holt einen Verkauf ab,
        /// <b>2</b> = liefert gekaufte Ware, 0 = Platz frei.
        ///
        /// <para>⚠ Es gibt auch eine <b>3</b> (@0x4C06C3), und die ist NICHT
        /// der Markt: sie ruft <c>space_in</c> @0x4C1600, also den
        /// Kampagnennachschub. Der ist bei uns längst gebaut
        /// (<c>SpawnReinforcement</c>) und geht seinen eigenen Weg — hier wäre
        /// er ein zweiter Ort für dieselbe Sache.</para></summary>
        public int Kind;

        /// <summary>+0x08, welches Angebot er bedient (Art 1).</summary>
        public SellOffer? Offer;

        /// <summary>Die Ladung (Art 2): <c>byte[+0x0B + i]</c>, Anzahl in
        /// <c>byte[+0x0A]</c>, höchstens zwanzig.</summary>
        public List<MarketOffer>? Cargo;

        /// <summary>Der Gebäudeplatz, zu dem die Ladung gehört — damit ein
        /// Stück, das keinen Platz fand, wieder in die Schlange kann.</summary>
        public int CargoTarget = -1;
    }

    private readonly List<Collector> _collectors = new();

    /// <summary>Die fahrenden Abholer — für den Prüfstand.</summary>
    public IReadOnlyList<Collector> Collectors => _collectors;

    /// <summary>Wieviele Plätze das Original hat: <c>cmp al, 0x14</c> @0x4C01E9
    /// und @0x4C071B.</summary>
    private const int CollectorSlots = 20;

    /// <summary>Ab hier fährt er los: <c>push -0xa</c> @0x4C039A.</summary>
    private const int CollectorStartCol = -10;

    private bool SpawnCollector(SellOffer o, Entity u)
    {
        if (_collectors.Count >= CollectorSlots) return false;   // @0x4C01ED
        _collectors.Add(new Collector
        {
            // ⚠ BERICHTIGT 18.08.2026: hier stand `Target = u.Col`. Das
            // Original uebergibt `Spalte − 2` (@0x4C0389, `sub cx, 2`), der
            // Abholer haelt also zwei Felder VOR der Einheit. Der Unterschied
            // ist klein und trotzdem einer: er verschiebt die Ankunft, und die
            // Ankunft ist der Takt, in dem das Geld kommt.
            Col = CollectorStartCol, Row = u.Row, Target = u.Col - 2,
            Frac = 0, Kind = 1, Offer = o,
        });
        return true;
    }

    /// <summary>
    /// Ein Takt für jeden Abholer — die Fahrt aus @0x4C04B3..0x4C0571, Befehl
    /// für Befehl.
    ///
    /// <para><b>Die Fahrt hat zwei Gänge, und das ist gelesen, nicht gesetzt:</b>
    /// solange mehr als <b>10</b> Spalten fehlen, geht es <b>eine Spalte je
    /// Takt</b> (@0x4C051D). Auf den letzten zehn <b>bremst er</b>: der Schritt
    /// ist <c>(Rest·4 + Rest davor)</c>, und erst wenn diese Summe <b>39</b>
    /// übersteigt, rückt er um <c>Summe/40</c> Spalten vor; der Rest bleibt
    /// stehen (@0x4C052D..0x4C0567). Er wird also immer langsamer, je näher er
    /// kommt.</para>
    ///
    /// <para>⚠ <b>Und er gibt auf.</b> <c>Spalte − 2 &gt; Kartenbreite</c>
    /// (@0x4C04C6, <c>dword[0x542DC4]</c>) setzt die Art auf 0 und der Platz ist
    /// wieder frei. Ohne diese Zeile bliebe ein Abholer, dessen Ziel
    /// verschwunden ist, für immer stehen und einen der zwanzig Plätze
    /// belegen.</para>
    /// </summary>
    private void CollectorTick()
    {
        if (_collectors.Count == 0) return;
        int width = _nav?.Width ?? 256;

        for (int i = _collectors.Count - 1; i >= 0; i--)
        {
            var s = _collectors[i];

            // @0x4C04C6 — über den Kartenrand hinaus: aufgeben.
            if (s.Col - 2 > width) { _collectors.RemoveAt(i); continue; }

            int d = s.Target - s.Col;
            if (d > 10)
            {
                s.Col++;                                  // @0x4C051F
                continue;
            }

            int step = d * 4;                             // @0x4C052D
            if (step < 1) step = 1;                       // @0x4C0535
            step += s.Frac;                               // @0x4C053B
            if (step > 0x27)                              // @0x4C0547
            {
                s.Col += step / 40;                       // @0x4C0556
                s.Frac = step % 40;                       // @0x4C0567
            }
            else s.Frac = step;

            if (s.Col != s.Target) continue;              // @0x4C0574

            _collectors.RemoveAt(i);
            switch (s.Kind)
            {
                case 1:                                   // Ankunft, @0x4C05A5
                    if (s.Offer != null) PayForSale(s.Offer);
                    break;
                case 2:                                   // Lieferung, @0x4C067F
                    DeliverCargo(s);
                    break;
            }
        }
    }

    /// <summary>
    /// <b>Die Auszahlung</b> — @0x4C0634, und drum herum, was das Original dort
    /// sonst noch tut.
    ///
    /// <para>⚠ <b>Der Empfänger wird nicht mitgeführt, er wird GERECHNET:</b>
    /// <c>Spieler = Einheitsnummer / 1000</c> (@0x4C060C). Die Einheitentafel
    /// des Originals ist damit — wie die Entwurfstafel mit ihren 200 Plätzen —
    /// <b>nach Spielern gefächert, 1000 Plätze je Spieler</b>. Bei uns steht der
    /// Besitzer im Satz; wir nehmen ihn von dort und schreiben die Rechnung des
    /// Originals hierhin, damit sie nicht verloren geht.</para>
    ///
    /// <para>Danach: Angebot frei (<c>0xFFFF</c>), Effekt <c>0x60</c> an der
    /// Stelle der Einheit (@0x4C05E3), die Zelle wird geräumt und die Einheit
    /// gelöscht (@0x4C0660).</para>
    /// </summary>
    private void PayForSale(SellOffer o)
    {
        int idx = o.Unit;
        _sellOffers.Remove(o);
        if (idx < 0 || idx >= _entities.Count) return;
        var u = _entities[idx];

        int owner = u.Owner is >= 0 and <= 7 ? u.Owner : 0;
        Money(owner, Money(owner) + o.Price);
        SoldUnits++;
        SoldMoney += o.Price;
        SellNote = $"verkauft fuer ${o.Price} — Kontostand ${Money(owner)}";

        RemoveSoldUnit(idx, u);
        UpdatePanel();
        QueueRedraw();
    }

    /// <summary>Die verkaufte Einheit von der Karte nehmen. ⚠ <b>Kein Wrack und
    /// keine Explosion</b> — sie wird abgeholt, nicht zerstört; das Original
    /// spielt hier den Effekt <c>0x60</c> und löscht den Satz
    /// (@0x4C05E3/@0x4C0660), nicht die Sterbefolge.</summary>
    private void RemoveSoldUnit(int idx, Entity u)
    {
        _nav?.ClearOccupant(u.Col, u.Row, idx);
        if (u.Reserved is { } rc) _nav?.ClearOccupant(rc.X, rc.Y, idx);
        _sel.Remove(idx);
        foreach (var other in _entities)
            if (other.Target == idx) other.Target = -1;
        if (_selected == idx) SetPrimary();
        u.Reserved = null;
        u.Path = null;
        u.Target = -1;
        u.Hp = 0;
        u.Dead = true;
        u.DeadTime = 999f;        // kein Wrack: sofort durch, siehe DrawWreck
    }

    // ================= der Weg für das Gefecht ================================

    /// <summary>
    /// <b>Sofort abrechnen</b> — der Gefechtsweg, und ausdrücklich UNSERE
    /// Abweichung (Entscheidung des Spielers, 18.08.2026). Dieselbe Auszahlung,
    /// derselbe Satzweg, nur ohne die sechs Sekunden und ohne den Abholer.
    /// </summary>
    private void SellAtOnce(int idx, int price)
    {
        var o = new SellOffer { Unit = idx, Price = price, State = 0 };
        _sellOffers.Add(o);
        PayForSale(o);
    }

    /// <summary>Läuft dieser Lauf originalgetreu (Kampagne) oder als
    /// Wettkampf (Gefecht)? Die Trennachse des Projekts.</summary>
    private static bool TradeLikeOriginal => UI.SkirmishSetup.CampaignMission > 0;

    // ================= der Prüfstand ==========================================

    /// <summary>Stufe des <c>--sell-check</c>. −1 = aus.</summary>
    private int _sellCheck = -1;
    private int _sellCheckUnit = -1, _sellCheckMoney0, _sellCheckPrice;
    private int _sellCheckTick0, _sellCheckUnits0;

    /// <summary>Der SIMULATIONStakt beim Absetzen. ⚠ Nicht der Originaltakt:
    /// der springt nur in 5 von 6 Simulationstakten weiter, »sofort« liesse
    /// sich an ihm nicht von »einen Takt später« unterscheiden. Der Ring
    /// braucht selbst einen Takt (der Befehl wirkt am nächsten Taktanfang) —
    /// das ist die Untergrenze, gegen die hier gemessen wird.</summary>
    private long _sellCheckSim0;
    private int _sellCheckSeenState = -1, _sellCheckColLast = int.MinValue;
    private readonly System.Text.StringBuilder _sellLog = new();

    /// <summary><c>--sell-check</c> anwerfen.</summary>
    public void SellCheckStart() => _sellCheck = 0;

    /// <summary>
    /// <c>--sell-shot</c> — nur AUSWÄHLEN, nicht verkaufen, damit
    /// <c>--shot</c> die Befehlsleiste im Bild hat.
    ///
    /// <para>⚠ Warum das einen eigenen Schalter braucht: <c>--sell-check</c>
    /// verkauft die Einheit sofort und beendet sich; auf dem Bild wäre dann
    /// nichts mehr gewählt und die Leiste wieder weg. Ein Bild, das den
    /// Gegenstand nicht enthält, ist kein Zeuge — das war am 13.08. der erste
    /// Schuss auf die Schiffswaffe, der ohne <c>--look</c> auf Land stand.</para>
    /// </summary>
    public static bool SellShotOnly;

    /// <summary>Die gewählte Einheit für <c>--sell-shot</c> — gibt ihre Zelle
    /// zurück, damit die Kamera hin kann.</summary>
    public Vector2I? SellShotSetup()
    {
        LoadDesigns();
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != ViewPlayer) continue;
            if (SellPriceOf(i) < 0) continue;
            _sel.Clear(); _sel.Add(i); _selected = i;
            UpdatePanel();
            QueueRedraw();
            GD.Print($"sell-shot: Einheit {i} \"{e.Name}\" auf ({e.Col},{e.Row}) gewaehlt, " +
                     $"Preis ${SellPriceOf(i)} — die Leiste muss unten mittig stehen");
            return new Vector2I(e.Col, e.Row);
        }
        GD.Print("sell-shot: keine verkaeufliche Einheit gefunden");
        return null;
    }

    /// <summary>
    /// <c>--sell-check</c> — <b>eine Einheit wirklich verkaufen und dabei
    /// zusehen.</b>
    ///
    /// <para><b>Er übt die Mechanik aus, er schreibt sie nicht hin</b>
    /// (Regel 11): er geht über <see cref="SellFromPanel"/>, also über
    /// denselben Weg wie der Knopf, und der geht über
    /// <c>PostSell</c> → Ring → <c>ApplySell</c>. Damit ist mitgeprüft, dass der
    /// Satz durch den Befehlsring passt — im Netzspiel hängt daran alles.</para>
    ///
    /// <para><b>⚠ Er nennt ZAHLEN, keine Häkchen.</b> Kosten, Hülle, Erfahrung,
    /// Stufe, Faktor, Wert und Preis stehen einzeln da. Ein »Verkauf
    /// funktioniert« sagt nichts darüber, ob 30 % gerechnet wurden oder 50 —
    /// und die Formel ist der eigentliche Gegenstand.</para>
    ///
    /// <para><b>⚠ Und er prüft die GEGENRICHTUNG</b> (Regel 9): in der Kampagne
    /// muss das Geld <b>ausbleiben</b>, solange der Abholer fährt. Ein
    /// Prüfstand, der nur »Geld gekommen« misst, würde eine sofortige
    /// Auszahlung für bestanden erklären — und genau die wäre der Fehler, den
    /// die Trennung zwischen Kampagne und Gefecht einbauen kann.</para>
    ///
    /// <para><b>⚠ Ohne Fall kein Urteil</b> (Regel EE): findet er keine
    /// verkäufliche Einheit, sagt er <c>KEIN URTEIL</c> und warum — statt
    /// »0 Fehler« zu melden, was bei null Verkäufen trivial wahr wäre.</para>
    /// </summary>
    private void PollSellCheck()
    {
        if (_sellCheck < 0) return;

        switch (_sellCheck)
        {
            case 0:
            {
                _sellLog.AppendLine("sell-check");
                _sellLog.AppendLine($"  Modus: {(TradeLikeOriginal ? "KAMPAGNE (originalgetreu, Abholer)" : "GEFECHT (sofort)")}" +
                                    $"  — CampaignMission = {UI.SkirmishSetup.CampaignMission}");
                LoadDesigns();
                int pick = -1;
                for (int i = 0; i < _entities.Count; i++)
                {
                    var e = _entities[i];
                    if (e.IsBuilding || e.IsProp || e.Dead) continue;
                    if (e.Owner != ViewPlayer) continue;
                    if (SellPriceOf(i) < 0) continue;
                    pick = i; break;
                }
                if (pick < 0)
                {
                    // ⚠ Und er sagt, WELCHES Glied fehlt — »keine Einheit« und
                    // »Einheit ohne auffindbaren Entwurf« sind zwei ganz
                    // verschiedene Befunde, und der zweite wäre unser Fehler.
                    int eigene = 0, ohneEntwurf = 0;
                    for (int i = 0; i < _entities.Count; i++)
                    {
                        var e = _entities[i];
                        if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != ViewPlayer) continue;
                        eigene++;
                        if (SellPriceOf(i) < 0) ohneEntwurf++;
                    }
                    _sellLog.AppendLine($"  KEIN URTEIL: {eigene} eigene Einheit(en), davon " +
                                        $"{ohneEntwurf} ohne auffindbaren Entwurf (Marke+200*Spieler in sec47)");
                    GD.Print(_sellLog.ToString());
                    _sellCheck = -1;
                    return;
                }

                var u = _entities[pick];
                int owner = u.Owner is >= 0 and <= 7 ? u.Owner : 0;
                var d = DesignBySlot(u.Mark + 200 * owner);
                int stufe = UnitValue.LevelOf(u.Field28);
                int wert = UnitValueOf(pick);
                _sellLog.AppendLine($"  Einheit {pick}: \"{u.Name}\", Spieler {owner}, " +
                                    $"Marke {u.Mark} -> sec47-Platz {u.Mark + 200 * owner}" +
                                    (d == null ? " (NICHT GEFUNDEN)" : $" = \"{d.Value.Name}\""));
                if (d != null)
                    _sellLog.AppendLine($"  Kosten W{d.Value.Derived.CostW} F{d.Value.Derived.CostF} " +
                                        $"S{d.Value.Derived.CostS} = {d.Value.Derived.CostW + d.Value.Derived.CostF + d.Value.Derived.CostS}" +
                                        $" · Huelle {u.Hp}/{d.Value.Derived.Hp}" +
                                        $" · Erfahrung {u.Field28} -> Stufe {stufe}" +
                                        $", Faktor {UnitValue.LevelFactor[stufe] / 100.0:0.00}");
                _sellLog.AppendLine($"  WERT {wert}  ->  Verkaufspreis 3*{wert}/10 = {UnitValue.SellPrice(wert)}" +
                                    $"  (Ladenpreis waere 25*{wert}/10 = {UnitValue.ShopPrice(wert)}, Spanne 8:1)");

                _sellCheckUnit = pick;
                _sellCheckMoney0 = Money(owner);
                _sellCheckUnits0 = LivingUnitsOf(owner);
                _sellCheckTick0 = _origTicks;
                _sellCheckSim0 = DebugTicks;
                _sel.Clear(); _sel.Add(pick); _selected = pick;

                bool ok = SellFromPanel();
                _sellCheckPrice = UnitValue.SellPrice(wert);
                _sellLog.AppendLine($"  Befehl ueber den Knopfweg abgesetzt: {(ok ? "ja" : "NEIN — " + SellNote)}");
                if (!ok) { GD.Print(_sellLog.ToString()); _sellCheck = -1; return; }
                _sellLog.AppendLine($"  Konto vorher ${_sellCheckMoney0}, lebende Einheiten {_sellCheckUnits0}");
                _sellCheck = 1;
                return;
            }

            case 1:
            {
                int owner = _sellCheckUnit >= 0 && _sellCheckUnit < _entities.Count
                          ? _entities[_sellCheckUnit].Owner : ViewPlayer;
                if (owner is < 0 or > 7) owner = ViewPlayer;

                // Zustandswechsel des Angebots mitschreiben — mit dem
                // ORIGINALTAKT daneben, damit »es hat gewartet« eine Zahl hat.
                SellOffer? mine = null;
                foreach (var o in _sellOffers) if (o.Unit == _sellCheckUnit) { mine = o; break; }
                if (mine != null && mine.State != _sellCheckSeenState)
                {
                    _sellCheckSeenState = mine.State;
                    _sellLog.AppendLine($"    Takt {_origTicks - _sellCheckTick0,5}: Angebot Zustand " +
                                        $"0x{mine.State:X2} ({StateWord(mine.State)}), Konto ${Money(owner)}");
                }
                foreach (var s in _collectors)
                    if (s.Offer == mine && s.Col != _sellCheckColLast)
                    {
                        _sellCheckColLast = s.Col;
                        if (s.Col % 20 == 0 || s.Target - s.Col <= 10)
                            _sellLog.AppendLine($"    Takt {_origTicks - _sellCheckTick0,5}: Abholer " +
                                                $"Spalte {s.Col} -> {s.Target} (Zeile {s.Row}), Rest {s.Frac}");
                    }

                if (SoldUnits > 0)
                {
                    int dauer = _origTicks - _sellCheckTick0;
                    long simDauer = DebugTicks - _sellCheckSim0;
                    int geld = Money(owner) - _sellCheckMoney0;
                    int leben = LivingUnitsOf(owner);
                    _sellLog.AppendLine($"  ABGESCHLOSSEN nach {simDauer} Simulationstakten " +
                                        $"({simDauer / (double)SimHz:0.00} s), das sind {dauer} Originaltakte");
                    _sellLog.AppendLine($"  Konto ${_sellCheckMoney0} -> ${Money(owner)} " +
                                        $"(+{geld}, erwartet +{_sellCheckPrice}) " +
                                        $"{(geld == _sellCheckPrice ? "RICHTIG" : "FALSCH")}");
                    _sellLog.AppendLine($"  lebende Einheiten {_sellCheckUnits0} -> {leben} " +
                                        $"{(leben == _sellCheckUnits0 - 1 ? "(eine weg, richtig)" : "(FALSCH)")}");
                    // ⚠ Die Gegenprobe, und sie ist der Kern: in der Kampagne
                    // MUSS gewartet worden sein.
                    //
                    // ⚠ Die Schwelle ist der Ring: ein Befehl wirkt EINEN
                    // Simulationstakt nach dem Absetzen. Alles bis 3 Takte ist
                    // also »sofort«; der Kampagnenweg braucht durch die
                    // 300-Takt-Phase mindestens einige hundert.
                    if (TradeLikeOriginal)
                        _sellLog.AppendLine(simDauer > 60
                            ? $"  Gegenprobe Kampagne BESTANDEN: es wurde gewartet ({simDauer} Takte, {simDauer / (double)SimHz:0.00} s)"
                            : $"  Gegenprobe Kampagne FEHLGESCHLAGEN: nach {simDauer} Takten bezahlt — das ist der Gefechtsweg");
                    else
                        _sellLog.AppendLine(simDauer <= 3
                            ? $"  Gegenprobe Gefecht BESTANDEN: sofort bezahlt ({simDauer} Takte = Ringlaufzeit)"
                            : $"  Gegenprobe Gefecht FEHLGESCHLAGEN: erst nach {simDauer} Takten — das ist der Kampagnenweg");
                    GD.Print(_sellLog.ToString());
                    _sellCheck = -1;
                    return;
                }

                // Abbruch, wenn nach 60 Sekunden nichts geschehen ist.
                if (_origTicks - _sellCheckTick0 > 60 * (int)OriginalTicksPerSecond)
                {
                    _sellLog.AppendLine($"  KEIN URTEIL: nach 60 s kein Abschluss. " +
                                        $"Offene Angebote: {_sellOffers.Count}, Abholer: {_collectors.Count}");
                    foreach (var o in _sellOffers)
                        _sellLog.AppendLine($"    Angebot Einheit {o.Unit}, ${o.Price}, " +
                                            $"Zustand 0x{o.State:X2} ({StateWord(o.State)})");
                    GD.Print(_sellLog.ToString());
                    _sellCheck = -1;
                }
                return;
            }
        }
    }

    private static string StateWord(int state) => state switch
    {
        0xFF => "frisch eingetragen",
        0 => "abholbereit",
        1 => "Abholer unterwegs",
        _ => "unbekannt",
    };

    /// <summary>Wieviele lebende, bewegliche Einheiten dieser Spieler hat —
    /// damit »eine ist weg« gezählt und nicht behauptet wird.</summary>
    private int LivingUnitsOf(int player)
    {
        int n = 0;
        foreach (var e in _entities)
            if (!e.IsBuilding && !e.IsProp && !e.Dead && e.Owner == player) n++;
        return n;
    }

    // ================= der Prüfstand des NACHSCHUBS ===========================

    private int _shopCheck = -1;
    private int _shopCheckTick0;
    private readonly System.Text.StringBuilder _shopLog = new();

    /// <summary><c>--shop-check</c> anwerfen.</summary>
    public void ShopCheckStart() => _shopCheck = 0;

    /// <summary>
    /// <c>--shop-check</c> — <b>legt der Laden nach, und legt er das Richtige
    /// nach?</b>
    ///
    /// <para><b>Der wertvollste Teil ist die PREISPROBE, und sie ist billig zu
    /// haben:</b> die Karten tragen den Ladensatz (sec94) <b>und</b> den Preis
    /// (sec95) nebeneinander. Der Sollwert kommt damit aus dem <b>Original
    /// selbst</b> und nicht aus unserer Ableitung — genau das, was Regel N
    /// verlangt. Rechnet unsere Formel <c>2,5 × Wert</c> aus dem Satz denselben
    /// Preis heraus, den die Datei nennt, dann stimmen Wertformel,
    /// Stufentafel, Faktoren und die 2,5 alle zusammen; und wenn nicht, sagt
    /// die Zeile, welches Angebot abweicht.</para>
    ///
    /// <para><b>Er übt die Mechanik aus</b> (Regel 11): er räumt das Regal leer
    /// — <b>und sagt in der Ausgabe, dass er es tut</b> —, lässt dann den
    /// echten Takt laufen und wartet, bis die Phase <c>%100 == 77</c> von
    /// selbst zuschlägt. Der Nachschub wird also nicht aufgerufen, er wird
    /// abgewartet.</para>
    ///
    /// <para><b>Und er prüft den Würfel gegen die gelesenen Anteile.</b> Zehn-
    /// tausend Würfe, aufgeschlüsselt nach Stufe. Ein Nachschub, der »etwas«
    /// hinlegt, sagt nichts darüber, ob die Erfahrungsverteilung die des
    /// Originals ist — und die ist der Grund, warum Preise um den Faktor 70
    /// auseinanderliegen.</para></summary>
    private void PollShopCheck()
    {
        if (_shopCheck < 0) return;

        if (_shopCheck == 0)
        {
            LoadDesigns();
            _shopLog.AppendLine("shop-check");
            int mission = UI.SkirmishSetup.CampaignMission;
            _shopLog.AppendLine(mission > 0
                ? $"  Modus: KAMPAGNE M{mission} — Sperrliste aus dem FAHRPLAN (set_part)"
                : $"  Modus: GEFECHT — Sperrliste aus dem TECHSTANDARD {UI.SkirmishSetup.Techstandard} " +
                  $"(⚠ unsere Ersatzquelle, das Original liest dort ein anderes Byte)");

            // ---- die zwei Listen ----
            var gesperrt = new List<int>();
            var frei = new List<int>();
            int ohneEntwurf = 0, infanterie = 0;
            for (int s = ShopSlotFirst; s <= ShopSlotLast; s++)
            {
                if (s >= ShopSlotSkipFrom && s <= ShopSlotSkipTo) continue;
                var d = DesignBySlot(s);
                if (d == null) { ohneEntwurf++; continue; }
                if (d.Value.Propulsion < ShopMinPropulsion) { infanterie++; continue; }
                (ShopDesignLocked(s) ? gesperrt : frei).Add(s);
            }
            _shopLog.AppendLine($"  ENTWUERFE {ShopSlotFirst}..{ShopSlotLast} ohne " +
                                $"{ShopSlotSkipFrom}..{ShopSlotSkipTo}: " +
                                $"{frei.Count + gesperrt.Count} Kandidaten " +
                                $"({frei.Count} frei / {gesperrt.Count} gesperrt), " +
                                $"{infanterie} als Infanterie ausgesiebt (Fahrwerk < {ShopMinPropulsion}), " +
                                $"{ohneEntwurf} Luecken");
            if (frei.Count + gesperrt.Count == 0)
            {
                _shopLog.AppendLine("  KEIN URTEIL: kein einziger Kandidat — hier ist nichts zu messen");
                GD.Print(_shopLog.ToString()); _shopCheck = -1; return;
            }

            // ---- die PREISPROBE gegen die Ware der Karte ----
            //
            // ⚠ Sie ist NICHT »stimmt / stimmt nicht«, und der Grund ist ein
            // gemessener Befund über die Kartendateien selbst: über alle 13
            // Gefechtskarten liegen die gespeicherten Preise in GENAU ZWEI
            // Gruppen, 2,5·Wert und 1,5·Wert — kein drittes Verhältnis, keine
            // Streuung. Die 2,5 ist die des Spielcodes (beide GAME.EXE, je zwei
            // Aufrufstellen, byteweise nachgesehen); map_DM_1 trägt sie auf
            // allen 18 Plätzen, die übrigen zwölf Karten durchweg die 1,5.
            //
            // Ein Prüfstand, der hier »0 von 21« meldet, zeigt deshalb auf den
            // falschen Täter: unsere Formel ist nicht falsch, die Kartendateien
            // sind uneinheitlich. Also wird das VERHAELTNIS ausgewiesen.
            _shopLog.AppendLine($"  PREISPROBE — {_market.Count} Angebote dieser Karte gegen " +
                                $"den Preis AUS DER DATEI (sec95). ⚠ Sollwert aus zweiter Quelle:");
            int g25 = 0, g15 = 0, sonst = 0, pruefbar = 0;
            var fremd = new List<string>();
            foreach (var o in _market)
            {
                var d = DesignBySlot(o.Design);
                if (d == null) continue;
                int hullMax = d.Value.Derived.Hp;
                if (hullMax <= 0) continue;
                pruefbar++;
                int wert = UnitValue.Of(d.Value.Derived.CostW, d.Value.Derived.CostF,
                                        d.Value.Derived.CostS, o.Energie, hullMax, o.Experience);
                if (wert <= 0) { sonst++; continue; }
                if (UnitValue.ShopPrice(wert) == o.Price) g25++;
                else if (15 * wert / 10 == o.Price) g15++;
                else
                {
                    sonst++;
                    fremd.Add($"    Entwurf {o.Design} \"{d.Value.Name}\": Kosten " +
                              $"{d.Value.Derived.CostW + d.Value.Derived.CostF + d.Value.Derived.CostS}, " +
                              $"Huelle {o.Energie}/{hullMax}, Erfahrung {o.Experience} -> Wert {wert}, " +
                              $"gespeichert ${o.Price} (Verhaeltnis {o.Price / (double)wert:0.000})");
                }
            }
            _shopLog.AppendLine($"    {g25} bei 2,5·Wert (die Formel des SPIELCODES), " +
                                $"{g15} bei 1,5·Wert, {sonst} weder noch — von {pruefbar}");
            _shopLog.AppendLine(sonst == 0
                ? "    kein drittes Verhaeltnis: Wertformel, Stufentafel und Faktoren gehen auf"
                : "    ⚠ ein drittes Verhaeltnis — DAS waere ein Fehler unserer Formel:");
            foreach (var z in fremd.GetRange(0, Mathf.Min(6, fremd.Count)))
                _shopLog.AppendLine(z);
            if (fremd.Count > 6)
                _shopLog.AppendLine($"    ... und {fremd.Count - 6} weitere");

            // ---- der Würfel gegen die gelesenen Anteile ----
            _shopLog.AppendLine("  ERFAHRUNGSWURF, 10000 Wuerfe auf eine Kampfwaffe (Waffe 2):");
            var zaehl = new Dictionary<int, int>();
            for (int i = 0; i < 10000; i++)
            {
                int e = ShopRollExperience(2);
                zaehl[e] = zaehl.GetValueOrDefault(e) + 1;
            }
            foreach (var (wert, soll) in new (int, double)[]
                     { (0, 150 / 256.0), (6, 60 / 256.0), (21, 25 / 256.0), (42, 15 / 256.0),
                       (77, 6 / 256.0 * 0.50), (112, 6 / 256.0 * 0.30), (172, 6 / 256.0 * 0.20) })
                _shopLog.AppendLine($"    Erfahrung {wert,3} (Stufe {UnitValue.LevelOf(wert)}, " +
                                    $"Faktor {UnitValue.LevelFactor[UnitValue.LevelOf(wert)] / 100.0:0.00}): " +
                                    $"{zaehl.GetValueOrDefault(wert),5} gemessen, {soll * 10000,7:0} erwartet");
            _shopLog.AppendLine($"    Erfahrung 255 (Stufe 7): {zaehl.GetValueOrDefault(255)} — " +
                                $"MUSS 0 sein, der Zweig des Originals ist unerreichbar");
            _shopLog.AppendLine($"    Nutzfahrzeug (Waffe 67): " +
                                $"{(ShopRollExperience(67) == 0 ? "0, richtig" : "NICHT 0 — falsch")}");

            // ---- das Regal räumen und den echten Takt abwarten ----
            _shopLog.AppendLine($"  ⚠ EINGRIFF DES PRUEFSTANDS: das Regal wird geraeumt " +
                                $"({_market.Count} Angebote weg). Ohne das liegt hier mehr als " +
                                $"{ShopStockTarget} aus und der Nachschub haette gar keinen Anlass.");
            _market.Clear();
            ShopAdded = 0; ShopNote = "";
            _shopCheckTick0 = _origTicks;
            _shopCheck = 1;
            return;
        }

        // Stufe 1 — auf die Phase %100 == 77 warten, sie NICHT aufrufen.
        if (ShopAdded > 0 || _market.Count > 0)
        {
            int dauer = _origTicks - _shopCheckTick0;
            _shopLog.AppendLine($"  NACHSCHUB nach {dauer} Originaltakten " +
                                $"({dauer / OriginalTicksPerSecond:0.00} s) — " +
                                $"{(dauer <= 100 ? "innerhalb der 100-Takt-Phase, richtig" : "ZU SPAET")}");
            _shopLog.AppendLine($"    {ShopNote}");
            foreach (var o in _market)
            {
                var d = DesignBySlot(o.Design);
                _shopLog.AppendLine($"    Entwurf {o.Design,3} \"{(d?.Name ?? "?")}\" " +
                                    $"Waffe {d?.Weapon,3} Fahrwerk {d?.Propulsion,3} " +
                                    $"Erfahrung {o.Experience,3} (Stufe {UnitValue.LevelOf(o.Experience)}) " +
                                    $"-> ${o.Price}" +
                                    $"{(ShopDesignLocked(o.Design) ? "  [exotisch]" : "")}");
            }
            GD.Print(_shopLog.ToString());
            _shopCheck = -1;
            return;
        }
        if (_origTicks - _shopCheckTick0 > 300)
        {
            _shopLog.AppendLine($"  KEIN URTEIL: nach 300 Originaltakten (6 s) kein Nachschub. " +
                                $"Die Phase %100 == 77 haette dreimal zuschlagen muessen." +
                                (ShopNote.Length > 0 ? $" Letzte Meldung: {ShopNote}" : ""));
            GD.Print(_shopLog.ToString());
            _shopCheck = -1;
        }
    }

    // ================= der Prüfstand der LIEFERUNG ============================

    private int _buyCheck = -1;
    private int _buyCheckTick0, _buyCheckMoney0, _buyCheckUnits0, _buyCheckPrice, _buyCheckTarget = -1;
    private long _buyCheckSim0;
    private readonly System.Text.StringBuilder _buyLog = new();

    /// <summary><c>--buy-check</c> anwerfen.</summary>
    public void BuyCheckStart() => _buyCheck = 0;

    /// <summary>
    /// <c>--buy-check</c> — <b>kaufen und der Ware beim Ankommen zusehen.</b>
    ///
    /// <para>Er geht über <see cref="BuildPanelPick"/>, also den <b>Klickweg des
    /// Fensters</b> und nicht an ihm vorbei. Und er misst beide Hälften
    /// getrennt, weil sie zwei verschiedene Dinge sind: <b>der Kauf</b> (Geld
    /// weg, Regal kürzer, Platz als verkauft markiert) und <b>die Lieferung</b>
    /// (Einheit da, Ladenplatz frei).</para>
    ///
    /// <para><b>⚠ Die Gegenprobe ist wieder der Kern:</b> in der Kampagne darf
    /// im Takt des Kaufs <b>keine Einheit</b> entstehen — sonst hätten wir den
    /// Gefechtsweg gebaut und es gemerkt hätte es niemand. Der Prüfstand liest
    /// die Einheitenzahl deshalb ZWEIMAL: direkt nach dem Kauf und nach der
    /// Ankunft.</para>
    ///
    /// <para>⚠ Und er sagt, wieviele Takte es gedauert hat. »Angekommen« ohne
    /// Dauer wäre von »sofort erschienen« nicht zu unterscheiden.</para>
    /// </summary>
    private void PollBuyCheck()
    {
        if (_buyCheck < 0) return;
        int owner = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;

        if (_buyCheck == 0)
        {
            int mi = -1;
            for (int i = 0; i < _entities.Count; i++)
                if (_entities[i].IsBuilding && !_entities[i].Dead && _entities[i].BType == 17) { mi = i; break; }

            // ⚠ ZUERST warten, DANN protokollieren. Andersherum schrieb der
            // Prüfstand seinen Kopf in JEDEM Takt neu, solange er wartete —
            // das Protokoll lief voll und die eigentliche Messung war darin
            // nicht mehr zu finden.
            if (mi >= 0 && MarketShelf().Count == 0 && _origTicks < 400) return;

            _buyLog.AppendLine("buy-check");
            _buyLog.AppendLine($"  Modus: {(TradeLikeOriginal ? "KAMPAGNE (originalgetreu, Lieferung)" : "GEFECHT (sofort)")}");
            if (mi < 0)
            {
                _buyLog.AppendLine("  KEIN URTEIL: kein Geschaeftszentrum auf dieser Karte");
                GD.Print(_buyLog.ToString()); _buyCheck = -1; return;
            }
            var markt = _entities[mi];
            // ⚠ Eine frische Karte hat LEERE Regale — die Ware der .DM-Dateien
            // ist angesammelter Spielstand, die Kampagnenkarten bringen keine
            // mit (13 von 19 Marktkarten der Kampagne: 0 Angebote). Also wird
            // auf den Nachschub GEWARTET statt aufzugeben; das prueft zugleich,
            // dass Nachschub und Kauf zusammenspielen.
            if (MarketShelf().Count == 0)
            {
                if (_origTicks < 400) return;             // 8 s = vier Nachschubphasen
                _buyLog.AppendLine($"  KEIN URTEIL: der Laden ist nach {_origTicks} " +
                                   $"Originaltakten immer noch leer — der Nachschub liefert nichts. " +
                                   (ShopNote.Length > 0 ? $"Letzte Meldung: {ShopNote}" : ""));
                GD.Print(_buyLog.ToString()); _buyCheck = -1; return;
            }
            if (_origTicks > 0)
                _buyLog.AppendLine($"  Der Laden war leer und wurde vom NACHSCHUB gefuellt " +
                                   $"({MarketShelf().Count} Stueck nach {_origTicks} Originaltakten)");

            int ziel = DeliveryTargetFor(owner, markt);
            _buyLog.AppendLine(ziel < 0
                ? $"  ⚠ Spieler {owner} hat KEIN eigenes Gebaeude — der Kauf muss abgelehnt werden"
                : $"  Ziel der Lieferung (UNSERE Wahl: naechstes eigenes Gebaeude): Satz {ziel}, " +
                  $"{BuildingTypeName(_entities[ziel].BType)} auf " +
                  $"({_entities[ziel].Col},{_entities[ziel].Row}); der Markt steht auf " +
                  $"({markt.Col},{markt.Row})");
            _buyCheckTarget = ziel;

            // ⚠ EINGRIFF DES PRUEFSTANDS, und er muss genannt werden: der Laden
            // ist nur offen, solange eine eigene Einheit auf einer der vier
            // Platten steht (@0x43E90C). Ohne das gibt `Producer()` gar nicht
            // den Markt zurueck — der erste Anlauf meldete deshalb einen
            // leeren Kaufweg und sah aus wie ein Fehler des Kaufs.
            bool offenVorher = MarketOpenFor(markt, owner);
            if (MarketShotSetup() == null)
            {
                _buyLog.AppendLine("  KEIN URTEIL: keine eigene Einheit da, um den Laden zu oeffnen");
                GD.Print(_buyLog.ToString()); _buyCheck = -1; return;
            }
            _buyLog.AppendLine($"  Laden: vorher {(offenVorher ? "offen" : "zu")}, " +
                               $"eine eigene Einheit auf eine Platte GESTELLT (nicht gefahren) " +
                               $"-> {(MarketOpenFor(markt, owner) ? "offen" : "ZU — die Plattenpruefung greift nicht")}");

            Money(owner, 99999);
            _buyCheckMoney0 = Money(owner);
            _buyCheckUnits0 = UnitRecords();
            _buyCheckPrice = MarketShelf()[0].Price;
            int regalVor = MarketShelf().Count;

            // ⚠ Ueber den KLICKWEG des Fensters, nicht ueber MarketBuy direkt.
            _selected = mi;
            _order = "";
            BuildPanelPick(0);

            _buyLog.AppendLine($"  Meldung des Kaufwegs: \"{_order}\"");
            _buyLog.AppendLine($"  KAUF: Preis ${_buyCheckPrice}; Konto ${_buyCheckMoney0} -> ${Money(owner)} " +
                               $"({(Money(owner) == _buyCheckMoney0 - _buyCheckPrice ? "genau abgezogen" : "FALSCH")}), " +
                               $"Regal {regalVor} -> {MarketShelf().Count} " +
                               $"({(MarketShelf().Count == regalVor - 1 ? "eines weg, richtig" : "FALSCH")})");
            int verkauft = 0;
            foreach (var x in _market) if (x.Sold) verkauft++;
            _buyLog.AppendLine($"  Plaetze als verkauft markiert: {verkauft} " +
                               $"(im Original waere das Preis 0xFFFF)");
            // ⚠ Die GEGENPROBE, im selben Takt gelesen.
            _buyLog.AppendLine(TradeLikeOriginal
                ? $"  Gegenprobe Kampagne: Einheitensaetze {_buyCheckUnits0} -> {UnitRecords()} " +
                  $"{(UnitRecords() == _buyCheckUnits0 ? "— unveraendert, richtig, die Ware faehrt erst" : "— SCHON DA, das waere der Gefechtsweg")}"
                : $"  Gegenprobe Gefecht: Einheitensaetze {_buyCheckUnits0} -> {UnitRecords()} " +
                  $"{(UnitRecords() == _buyCheckUnits0 + 1 ? "— sofort da, richtig" : "— NICHT abgesetzt")}");

            if (!TradeLikeOriginal) { GD.Print(_buyLog.ToString()); _buyCheck = -1; return; }
            _buyCheckTick0 = _origTicks;
            _buyCheckSim0 = DebugTicks;
            _buyCheck = 1;
            return;
        }

        // Stufe 1 — auf die Lieferung warten.
        if (MarketDelivered > 0)
        {
            int dauer = _origTicks - _buyCheckTick0;
            long simDauer = DebugTicks - _buyCheckSim0;
            _buyLog.AppendLine($"  GELIEFERT nach {simDauer} Simulationstakten " +
                               $"({simDauer / (double)SimHz:0.00} s), das sind {dauer} Originaltakte");
            _buyLog.AppendLine($"    {ShopNote}");
            _buyLog.AppendLine($"  Einheitensaetze {_buyCheckUnits0} -> {UnitRecords()} " +
                               $"({(UnitRecords() > _buyCheckUnits0 ? "angekommen, richtig" : "NICHTS ANGEKOMMEN")})");
            int verkauft = 0;
            foreach (var x in _market) if (x.Sold) verkauft++;
            _buyLog.AppendLine($"  noch offene Kaeufe: {verkauft} " +
                               $"({(verkauft == 0 ? "Ladenplatz wieder frei, richtig" : "einer blieb liegen")})");
            if (_buyCheckTarget >= 0 && _buyCheckTarget < _entities.Count)
            {
                var b = _entities[_buyCheckTarget];
                int nah = 0;
                foreach (var u in _entities)
                    if (!u.IsBuilding && !u.IsProp && !u.Dead && u.Owner == owner &&
                        Mathf.Abs(u.Col - b.Col) <= 3 && Mathf.Abs(u.Row - b.Row) <= 3) nah++;
                _buyLog.AppendLine($"  eigene Einheiten im Umkreis von 3 um das Ziel " +
                                   $"({b.Col},{b.Row}): {nah} — die Ware muss DORT stehen, " +
                                   $"nicht am Markt");
            }
            _buyLog.AppendLine($"  Gegenprobe Kampagne: {(simDauer > 60 ? $"es wurde gewartet ({simDauer} Takte)" : $"NUR {simDauer} Takte — das ist kein Warten")}");
            GD.Print(_buyLog.ToString());
            _buyCheck = -1;
            return;
        }
        if (_origTicks - _buyCheckTick0 > 600)
        {
            _buyLog.AppendLine($"  KEIN URTEIL: nach 600 Originaltakten (12 s) nichts geliefert. " +
                               $"Die Phase %300 == 222 haette zweimal zuschlagen muessen. " +
                               $"Abholer unterwegs: {_collectors.Count}." +
                               (ShopNote.Length > 0 ? $" Letzte Meldung: {ShopNote}" : ""));
            foreach (var s in _collectors)
                _buyLog.AppendLine($"    Abholer Art {s.Kind}: Spalte {s.Col} -> {s.Target}, " +
                                   $"Zeile {s.Row}, Ladung {s.Cargo?.Count ?? 0}");
            GD.Print(_buyLog.ToString());
            _buyCheck = -1;
        }
    }

    // ================= was die Oberfläche davon braucht =======================

    /// <summary>Ein Wort aus der Befehlsliste DES SPIELS (<c>0x4FD660</c>, im
    /// Import als <c>Maps/orders.json</c>). Damit trägt die Befehlsleiste die
    /// Namen des Originals und nicht unsere Übersetzung davon.</summary>
    public static string OrderWord(int index)
    {
        LoadOrders();
        return OrderName(index);
    }

    /// <summary>Die vier Nummern, die die Befehlsleiste braucht. Sie sind die
    /// Stellen der Liste des Originals, nicht unsere Zählung: <b>4 = Verkaufen</b>
    /// (der Aktionscode, den der Verteiler @0x448746 zum Verkaufsfenster
    /// schickt), 7/8 = Ein-/Ausgraben, 26 = Anhalten.</summary>
    public const int OrderSell = 4;

    /// <summary>Die Einheit, die ein »Verkaufen« gerade beträfe, mit ihrem
    /// Namen und dem Preis.</summary>
    public readonly struct SellChoice
    {
        public SellChoice(int index, string name, int price)
        { Index = index; Name = name; Price = price; }

        public int Index { get; }
        public string Name { get; }
        public int Price { get; }
    }

    /// <summary>
    /// Welche Einheit der Auswahl verkauft würde, und wofür — <c>null</c>, wenn
    /// keine in Frage kommt.
    ///
    /// <para>Es ist genau EINE, und das ist das Original: der Dialog @0x446470
    /// nimmt <c>word[0x4FA0C8]</c>, die <b>eine</b> gewählte Einheit, nicht die
    /// Auswahlliste <c>0x8320F8</c>, aus der der Fahrbefehl schöpft. Ein
    /// Sammelverkauf wäre unsere Zutat und ist keine.</para>
    /// </summary>
    public SellChoice? SellChoiceOfSelection()
    {
        foreach (int i in _sel)
        {
            if (i < 0 || i >= _entities.Count) continue;
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead) continue;
            if (e.Owner != ViewPlayer) continue;
            bool schon = false;
            foreach (var o in _sellOffers) if (o.Unit == i) { schon = true; break; }
            if (schon) continue;
            int p = SellPriceOf(i);
            if (p < 0) continue;
            return new SellChoice(i, e.Name.Length > 0 ? e.Name : "Einheit", p);
        }
        return null;
    }

    /// <summary>»Verkaufen« aus der Oberfläche — setzt den Befehl für die
    /// Einheit ab, die <see cref="SellChoiceOfSelection"/> nennt.</summary>
    /// <returns>true, wenn ein Satz abgesetzt wurde; sonst steht der Grund in
    /// <see cref="SellNote"/>.</returns>
    public bool SellFromPanel()
    {
        var w = SellChoiceOfSelection();
        if (w == null) { SellNote = "keine verkaeufliche Einheit gewaehlt"; return false; }
        bool ok = PostSell(w.Value.Index) >= 0;
        UpdatePanel();
        QueueRedraw();
        return ok;
    }

    /// <summary>Eine Zeile über den laufenden Handel — für Prüfstand und
    /// Statuszeile. ⚠ Sie nennt die Angebote NACH ZUSTAND getrennt: eine Summe
    /// könnte ein festgefahrenes Angebot (Zustand 1 ohne Abholer) nicht von
    /// einem fahrenden unterscheiden.</summary>
    public string SellLine()
    {
        int frisch = 0, bereit = 0, unterwegs = 0;
        foreach (var o in _sellOffers)
            switch (o.State) { case 0xFF: frisch++; break; case 0: bereit++; break; default: unterwegs++; break; }
        return $"Verkauf: {SoldUnits} abgeschlossen fuer ${SoldMoney}; offen " +
               $"{frisch} frisch / {bereit} abholbereit / {unterwegs} unterwegs, " +
               $"{_collectors.Count} Abholer" +
               (TradeLikeOriginal ? " (Kampagne: originalgetreu)" : " (Gefecht: sofort)");
    }
}
