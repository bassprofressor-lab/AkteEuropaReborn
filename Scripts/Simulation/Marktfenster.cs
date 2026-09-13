using System.Collections.Generic;
using Godot;
using AkteEuropaReborn.Simulation.Commands;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>DAS GESCHÄFTSZENTRUM — die Spielseite der Fensterart 33</b> (13.09.2026).
/// Zeichner und Klicks stehen in <see cref="UI.MarktView"/>; hier stehen die
/// drei Stellen, an denen die Simulation mitredet. Lesung:
/// <c>berichte/geschaeftszentrum-fenster-fable.md</c>.
///
/// <list type="bullet">
/// <item><b>Öffnen</b> nur, wenn auf einer der vier Platten (0,0) (3,0) (0,3)
/// (3,3) ein <b>FAHRZEUG</b> des Betrachters steht — Zeigerwahl
/// <c>0x432569</c>: <c>Belegung / 1000 == Betrachter</c>; ein Infanterietrupp
/// trägt 10000+k und zählt nicht. Kein Besitzer, kein <c>built</c>.</item>
/// <item><b>Schliessen</b> im Takt, sobald keines mehr dort steht — Takt-Arm
/// der Art 17 <c>0x43E90C</c> → <c>0x4511D0</c>, ohne Klang.</item>
/// <item><b>Kauf</b> = Befehl <b>530</b> → Behandler <c>0x4C1360</c>: Preis &gt;
/// Konto oder &lt; 1 → still nichts; sonst Konto −= Preis, Käufer, verkauft
/// (0xFFFF), Zielmarkt, Fenster neu malen (<c>0x451370</c>). Keine Meldung, kein
/// Absageklang. Die Lieferung nimmt der Markttakt auf (Frachter.cs).</item>
/// </list>
///
/// <para>⚠ UNSERE Setzungen: das Regal ist unsere Liste (<c>_market</c>) in
/// ihrer Reihenfolge, nicht nach Regalplatz 0…49 sortiert; im GEFECHT geht der
/// Kauf wie bisher sofort (Entscheidung vom 18.08.2026) über
/// <see cref="MarketBuy"/>.</para>
///
/// <para>Gegenschalter <c>--marktfenster-alt</c>: wieder unser Basisfenster im
/// Marktmodus, geöffnet von jeder eigenen Einheit auf einer Platte.</para>
/// </summary>
public partial class MapEntityLayer
{
    public static bool MarktfensterAlt;

    /// <summary>Das offene Fenster dieses Marktes neu malen (0x451370).</summary>
    public System.Action<int>? OnMarktfensterNeu;

    /// <summary>Das Fenster dieses Marktes schliessen (0x4511D0).</summary>
    public System.Action<int>? OnMarktfensterZu;

    public int MarktKaeufe, MarktKaufStill, MarktfensterTaktZu;

    /// <summary>Zeigerwahl <c>0x432569</c> und Takt-Arm <c>0x43E90C</c>: steht auf
    /// einer der vier Platten ein Fahrzeug dieses Spielers?</summary>
    public bool MarktFahrzeugAufPlatte(Entity b, int player)
    {
        if (b.BType != 17 || _nav == null) return false;
        foreach (var c in MarketPads(b))
        {
            int who = _nav.OccupantAt(c.X, c.Y);
            if (who < 0 || who >= _entities.Count) continue;
            var u = _entities[who];
            if (u.IsBuilding || u.IsProp || u.Dead || u.Owner != player) continue;
            if (u.Infantry >= 0) continue;              // 10000+k / 1000 trifft keinen Spieler
            return true;
        }
        return false;
    }

    private Entity? MarktVonPlatz(int platz)
    {
        foreach (var e in _entities)
            if (e.IsBuilding && e.BType == 17 && e.Slot == platz) return e;
        return null;
    }

    /// <summary>Was das Fenster zeigt — über den Gebäudeplatz <c>+0xACA0</c>.</summary>
    public UI.BuildingWindow.Stand? MarktWindowData(int platz)
    {
        var b = MarktVonPlatz(platz);
        if (b == null) return null;
        var st = new UI.BuildingWindow.Stand
        {
            Name = "Geschäftszentrum",
            Hp = b.Hp, HpMax = b.HpMax,
            Geld = Money(ViewPlayer),                 // dword[0xA9C600 + 4·Betrachter]
        };
        foreach (var o in _market)
        {
            if (o.Sold || o.Price <= 0) continue;     // 0 frei, 0xFFFF verkauft
            st.MarktZeilen.Add(new UI.BuildingWindow.MarktZeile
            {
                Nr = o.Nr, Preis = o.Price, Werte = MarktWerte(o),
            });
        }
        return st;
    }

    /// <summary>Der Name nach 0x47E321: <c>typ = +0x3E</c>, &lt; 200 → sec47
    /// <c>typ + 200·Betrachter</c>. Ware des Nachschubs trägt ihren Namen schon;
    /// für <c>typ</c> ≥ 200 (sec36) fällt es auf das Bild-Byte +0x43 zurück.</summary>
    private string MarktName(MarketOffer o)
    {
        int owner = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;
        if (o.Typ3E is >= 0 and < 200 && DesignBySlot(o.Typ3E + 200 * owner) is { Name.Length: > 0 } dt)
            return dt.Name;
        if (o.Name.Length > 0) return o.Name;
        return DesignBySlot(o.Design) is { Name.Length: > 0 } dd ? dd.Name : $"Entwurf {o.Design}";
    }

    private UI.BuildingWindow.DepotZeile MarktWerte(MarketOffer o)
    {
        int owner = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;
        var d = DesignBySlot(o.Design);
        var u = MarketEinheit(o, Vector2I.Zero, owner);
        u.Reload = d?.Reload ?? 0;                    // +0x3D
        u.Comp0F = u.UnitType;                        // +0x0F
        var bild = BildDerEinheit(u);
        bool waffe = u.Weapon != 0 && !IsEquipmentMount(u.Weapon);
        return new UI.BuildingWindow.DepotZeile
        {
            Rang = o.Experience,
            Name = MarktName(o),
            Hp = u.Hp, HpMax = u.HpMax,
            ChassisPic = bild.ChassisPic, TurretPic = bild.TurretPic,
            HatWaffe = waffe,
            Waffe = waffe ? InfoBauteil(WeaponRowOf(u.Weapon)) : "",
            Nachladen = InfoNachladen(u),
            Verbesserung = u.Equipment > 0 ? InfoBauteil(u.Equipment) : "",
            Antrieb = u.Comp0F > 0 ? InfoBauteil(u.Comp0F) : "",
            Zwilling = InfoZwilling(u),
            Angriff = u.Attack, Verteidigung = u.Defence,
            Geschw = u.Speed, Sicht = u.Sight,
            Reichw = u.Range, MinReichw = u.RangeMin,
        };
    }

    /// <summary>»Bestellen« — Klickarm <c>0x44C6E1</c>: Befehl 530, keine
    /// Vorbedingung.</summary>
    public void MarktBestellen(int nr, int platz)
    {
        int spieler = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;
        Emit(CommandRecord.Make(CommandOp.MarktKauf, (byte)spieler,
                                (short)nr, (short)spieler, (short)platz));
    }

    /// <summary>Behandler <c>0x4C1360</c> (F <c>0x4C0E20</c>).</summary>
    private bool ApplyMarktKauf(in CommandRecord c)
    {
        int nr = c.P1, spieler = c.P2, platz = c.P3;
        if (spieler is < 0 or > 7) return false;
        MarketOffer? o = null;
        foreach (var q in _market) if (q.Nr == nr) { o = q; break; }
        var markt = MarktVonPlatz(platz);
        if (o == null || markt == null) { MarktKaufStill++; return false; }

        if (!TradeLikeOriginal)
        {
            // GEFECHT: unsere Abweichung vom 18.08.2026, derselbe Weg wie bisher.
            int zeile = MarketShelf().IndexOf(o);
            if (zeile >= 0) MarketBuy(markt, zeile);
            OnMarktfensterNeu?.Invoke(platz);
            return true;
        }

        int preis = o.Sold ? -1 : o.Price;            // 0xFFFF ist vorzeichenbehaftet −1
        if (preis > Money(spieler)) { MarktKaufStill++; return true; }   // @0x4C138A — still
        if (preis < 1) { MarktKaufStill++; return true; }                // @0x4C138E — still
        o.Buyer = spieler;                            // @0x4C1396
        o.Sold = true;                                // @0x4C139C
        Money(spieler, Money(spieler) - preis);       // @0x4C13A6
        o.TargetBuilding = _entities.IndexOf(markt);  // @0x4C13AD
        MarketBought++;
        MarktKaeufe++;
        OnMarktfensterNeu?.Invoke(platz);             // @0x4C13B3
        UpdatePanel();
        return true;
    }

    /// <summary>Takt-Arm der Art 17, <c>0x43E90C</c> — im Originaltakt.</summary>
    private void MarktfensterTakt()
    {
        if (MarktfensterAlt || OnMarktfensterZu == null) return;
        var f = UI.WindowManager.Offen((int)UI.BuildingWindow.Art.Geschaeftszentrum);
        if (f == null || f.ZuBild >= 0) return;          // schon beim Zugehen
        var b = MarktVonPlatz(f.Kennung);
        if (b != null && MarktFahrzeugAufPlatte(b, ViewPlayer)) return;
        MarktfensterTaktZu++;
        OnMarktfensterZu(f.Kennung);
    }

    // ---- für --marktfenster-check -------------------------------------------

    public int MarktIndex()
    {
        for (int i = 0; i < _entities.Count; i++)
            if (_entities[i].IsBuilding && !_entities[i].Dead && _entities[i].BType == 17) return i;
        return -1;
    }

    public int MarktRegalFuellen()
    {
        int t = 0;
        while (MarketShelf().Count < 2 && t < 4000) { SimTickFuerProbe(); t++; }
        return MarketShelf().Count;
    }

    public IReadOnlyList<MarketOffer> MarktRegal() => MarketShelf();

    public int MarktGeld { get => Money(ViewPlayer); set => Money(ViewPlayer, value); }

    /// <summary>EINGRIFF: ein eigenes Fahrzeug auf die Platte (0,0) versetzen bzw.
    /// wieder herunter (eine Zelle links neben die Platte).</summary>
    public string MarktFahrzeugSetzen(int markt, bool drauf)
    {
        if (_nav == null || markt < 0) return "keine Karte";
        var b = _entities[markt];
        int ziel = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var q = _entities[i];
            if (q.IsBuilding || q.IsProp || q.Dead || !q.Mobile || q.Owner != ViewPlayer || q.Infantry >= 0) continue;
            if (Untergestellt(q)) continue;
            if (!drauf && !(q.Col == b.Col && q.Row == b.Row)) continue;
            ziel = i; break;
        }
        if (ziel < 0) return "kein eigenes Fahrzeug";
        var u = _entities[ziel];
        var zelle = drauf ? new Vector2I(b.Col, b.Row) : _nav.NearestFree(new Vector2I(b.Col - 2, b.Row)) ?? new Vector2I(b.Col - 2, b.Row);
        _nav.ClearOccupant(u.Col, u.Row, ziel);
        if (u.Reserved is { } rc) { _nav.ClearOccupant(rc.X, rc.Y, ziel); u.Reserved = null; }
        u.Col = zelle.X; u.Row = zelle.Y; u.Path = null;
        u.Pos = BodyCenterAt(u, u.Col, u.Row);
        _nav.SetOccupant(u.Col, u.Row, ziel);
        return $"{LabelOf(u)} auf ({u.Col},{u.Row})";
    }

    public int MarktPlatz(int markt) => markt >= 0 ? _entities[markt].Slot : -1;

    public bool MarktFahrzeugDa(int markt) => markt >= 0 && MarktFahrzeugAufPlatte(_entities[markt], ViewPlayer);

    /// <summary>Wie ein Klick auf den Markt: anwählen, Fensterweg.</summary>
    public void MarktAnklicken(int idx) => PostenAnwaehlenWieKlick(idx);
}
