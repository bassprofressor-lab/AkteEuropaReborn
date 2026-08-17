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
    private int _marketAcc;

    /// <summary>Der Zähler des Originals, <c>dword[0x4FA240]</c>.</summary>
    private int _marketTicks;

    /// <summary>Gehört in <c>SimTick</c>. Treibt den Originaltakt und ruft
    /// <see cref="MarketTradeTickOnce"/> so oft, wie im Original Takte
    /// vergangen wären.</summary>
    private void MarketTradeTick()
    {
        _marketAcc += (int)OriginalTicksPerSecond;      // 50
        while (_marketAcc >= SimHz)                     // 60
        {
            _marketAcc -= SimHz;
            _marketTicks++;
            MarketTradeTickOnce();
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
        if (_marketTicks % 300 == 111)
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

        CollectorTick();
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
        /// <summary>+0x07, die Art. 1 = holt einen Verkauf ab. 0 = Platz
        /// frei.</summary>
        public int Kind;
        /// <summary>+0x08, welches Angebot er bedient.</summary>
        public SellOffer? Offer;
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
            Col = CollectorStartCol, Row = u.Row, Target = u.Col,
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

            // ---- Ankunft, Art 1 — @0x4C05A5 ----
            _collectors.RemoveAt(i);
            var o = s.Offer;
            if (o == null) continue;
            PayForSale(o);
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
                _sellCheckTick0 = _marketTicks;
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
                    _sellLog.AppendLine($"    Takt {_marketTicks - _sellCheckTick0,5}: Angebot Zustand " +
                                        $"0x{mine.State:X2} ({StateWord(mine.State)}), Konto ${Money(owner)}");
                }
                foreach (var s in _collectors)
                    if (s.Offer == mine && s.Col != _sellCheckColLast)
                    {
                        _sellCheckColLast = s.Col;
                        if (s.Col % 20 == 0 || s.Target - s.Col <= 10)
                            _sellLog.AppendLine($"    Takt {_marketTicks - _sellCheckTick0,5}: Abholer " +
                                                $"Spalte {s.Col} -> {s.Target} (Zeile {s.Row}), Rest {s.Frac}");
                    }

                if (SoldUnits > 0)
                {
                    int dauer = _marketTicks - _sellCheckTick0;
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
                if (_marketTicks - _sellCheckTick0 > 60 * (int)OriginalTicksPerSecond)
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
