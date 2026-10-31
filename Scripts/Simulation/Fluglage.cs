namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// ⭐⭐ 30.10.2026, <b>bug-696</b> — <b>DIE FLACHE FLUGLAGE</b> (Original <c>0x42E340</c>,
/// F <c>0x42D510</c>; Lesung und Messung: <c>berichte/bau-fluglage.md</c>).
///
/// <para><b>Gelesen, C und F gleich:</b> der Listenbauer legt fuer ein Flugzeug den
/// Eintrag <c>x = (Spalte − Roll_x)·40 − FeinRoll_x</c>, <c>y = (Zeile − Roll_y)·20 −
/// FeinRoll_y</c> an (C <c>0x42E3F8..0x42E460</c>, F <c>0x42D5C8..0x42D62F</c>) — die
/// ZELLE, ohne Gelaendehoehe. Der Rumpfzeichner (C <c>0x42B8A6..0x42B8DF</c>, F
/// <c>0x42AA93..0x42AACC</c>) rechnet <c>x += FeinX − 43</c>, <c>y += FeinY/2 − alt − 35</c>
/// mit <c>alt</c> = Satz +0x0E (absolut, Gelaende·15 + Hoehe). Der Schatten (C
/// <c>0x42D6AA..0x42D74B</c>, F <c>0x42C88B..0x42C92E</c>) nimmt dieselbe Lage und zieht
/// statt <c>alt</c> die Bodenanhebung <c>0x4B5CE0(Spalte, Zeile, FeinX, 20 − FeinY/2)</c>
/// ab, bei gleichem Bildversatz (−43, −35). Geflogen wird auf <c>FeinX/FeinY</c> (je
/// 0..39, Ueberlauf schaltet die Zelle, C <c>0x425085..0x425125</c>).</para>
///
/// <para>Also: <b>die Lage ist flach</b> (Zelle + Feinlage, y = Zeile·20 + FeinY/2), der
/// Rumpf steht auf <c>Lage − alt</c>, der Schatten auf <c>Lage − Bodenanhebung</c>. Unser
/// <c>Special.Pos</c> lag dagegen im ANGEHOBENEN Bildraum (Flugziele = CellCenter − Hub)
/// — und die Zelle wurde als <c>Pos / Zellmass</c> gebildet, also OHNE <c>_ox/_oy</c>
/// und mit der Anhebung: auf K17 (<c>_oy</c> 165) lag <c>Row</c> eines Flugzeugs
/// dadurch 5…8 Zeilen suedlich der Zelle, ueber der es flog. Daran haengen Flak-Ring,
/// Sicht, Nebel, Bombentor und Absturzzelle. Gemessen in <c>--fluglage-check</c>.</para>
///
/// <para>Hier steht alles, was die flache Lage braucht, an EINER Stelle:</para>
/// <list type="bullet">
/// <item><see cref="FlugPunkt(Entity)"/> — der flache Punkt ueber einem Bodenpunkt
/// (Flugziele, Abstaende zu Einheiten und Gebaeuden).</item>
/// <item><see cref="AirZelleNachLage"/> — Zelle (und Feinlage) aus der Lage.</item>
/// <item><see cref="AirSchatten"/>, <see cref="AirRumpf"/> — die zwei Zeichenpunkte.</item>
/// <item><see cref="AirImRahmen"/> — die Lage, in den angehobenen Raum eines Ziels
/// geschoben (Schuss, Strahl, Bombe: deren Bahn laeuft im Bodenraum).</item>
/// </list>
///
/// <para>Gegenschalter <c>--fluglage-alt</c> (<see cref="FluglageAlt"/>): alles wie bis
/// 0.6.12 (angehobene Lage, <c>AirHubNachfuehren</c>). <c>--flughuepfen-alt</c> (Stand
/// 0.6.7) schliesst ihn ein — das Nullmodell des Huepfens gehoert in die alte Lage.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--fluglage-alt</c> — Stand bis 0.6.12: die Flugzeuglage liegt im
    /// angehobenen Bildraum, die Anhebung wird geschaetzt (<c>AirHubNachfuehren</c>),
    /// die Zelle ist <c>Pos / Zellmass</c>. bug-696.</summary>
    public static bool FluglageAlt;

    /// <summary>bug-696: gilt die flache Lage? (<c>--flughuepfen-alt</c> ist der Stand
    /// 0.6.7 und damit auch der alten Lage.)</summary>
    private static bool LageFlach => !FluglageAlt && !FlughuepfenAlt;

    /// <summary>bug-696: der flache Kartenpunkt ueber dem Bodenpunkt einer Einheit oder
    /// eines Gebaeudes — <c>Pos + Anhebung(Satzzelle)</c>. ⚠ UNSERE SETZUNG: das Original
    /// fliegt eine ZELLE an (Flugziel +0x14/+0x15), wir den Bodenpunkt der Einheit; fuer
    /// eine stehende 1×1-Einheit ist das die Zellmitte, fuer eine fahrende auf einer
    /// Schraege weicht er um deren Schraegenanteil ab (hoechstens 16 px).</summary>
    private Vector2 FlugPunkt(Entity e) => FlugPunkt(e.Pos, e.Col, e.Row);

    /// <summary>bug-696: ein Punkt im angehobenen Raum der Zelle (col, row), flach
    /// gemacht. In der alten Lage unveraendert.</summary>
    private Vector2 FlugPunkt(Vector2 gehoben, int col, int row)
        => LageFlach ? gehoben + new Vector2(0, HubOf(col, row)) : gehoben;

    /// <summary>bug-696: die flache Mitte einer Zelle (in der alten Lage
    /// <see cref="CellCenter"/>).</summary>
    private Vector2 FlugZelle(int col, int row)
        => LageFlach ? new Vector2(_ox + col * TileW + TileW / 2f, _oy + row * TileH + TileH / 2f)
                     : CellCenter(col, row);

    /// <summary>bug-696: wie <see cref="FlugZelle"/>, aber in der alten Lage
    /// <c>ZellMitte</c> (Kachelrechteck, ohne Schraege) — fuer die Rufer, die schon bisher
    /// diese Mitte nahmen (Staffel, Befehl 502, Flughafenwache).</summary>
    private Vector2 FlugZellmitte(int col, int row)
        => LageFlach ? FlugZelle(col, row) : ZellMitte(col, row);

    /// <summary>
    /// bug-696: Zelle und Feinlage aus der Lage, wie der Flugschritt des Originals
    /// (<c>0x425085..0x425125</c>: Feinlage 0..39, Ueberlauf schaltet die Zelle;
    /// <c>FeinY</c> laeuft in halben Bildpunkten, der Zeichner nimmt <c>FeinY/2</c>).
    /// In der alten Lage die alte Rechnung <c>Pos / Zellmass</c> (ohne <c>_ox/_oy</c>).
    /// </summary>
    private void AirZelleNachLage(Special a)
    {
        if (!LageFlach)
        {
            a.Col = Mathf.Clamp((int)(a.Pos.X / TileW), 0, 255);
            a.Row = Mathf.Clamp((int)(a.Pos.Y / TileH), 0, 255);
            return;
        }
        a.Col = Mathf.Clamp(Mathf.FloorToInt((a.Pos.X - _ox) / TileW), 0, 255);
        a.Row = Mathf.Clamp(Mathf.FloorToInt((a.Pos.Y - _oy) / TileH), 0, 255);
        a.FineX = Mathf.Clamp(Mathf.FloorToInt(a.Pos.X - _ox) - a.Col * TileW, 0, TileW - 1);
        a.FineY = Mathf.Clamp(Mathf.FloorToInt((a.Pos.Y - _oy - a.Row * TileH) * 2f), 0, 2 * TileH - 1);
    }

    /// <summary>bug-696: die flache Lage zu Zelle und Feinlage eines Satzes (Laden aus
    /// der Karte): <c>(Spalte·40 + FeinX, Zeile·20 + FeinY/2)</c>, wie Listenbauer und
    /// Rumpfzeichner sie zusammensetzen.</summary>
    private static Vector2 FlugLageAusSatz(int ox, int oy, int col, int row, int feinX, int feinY)
        => new(ox + col * TileW + Mathf.Clamp(feinX, 0, TileW - 1),
               oy + row * TileH + Mathf.Clamp(feinY, 0, 2 * TileH - 1) / 2);

    /// <summary>bug-696: die Bodenanhebung unter dem Flugzeug, wie der Schatten sie holt
    /// — <c>0x4B5CE0(Spalte, Zeile, FeinX, 20 − FeinY/2)</c> (C <c>0x42D6FA..0x42D72B</c>,
    /// F <c>0x42C8DD..0x42C90E</c>), samt <c>and eax, 0xFFFF00FF</c>.</summary>
    private int AirBodenHub(Special a)
    {
        int fx = Mathf.Clamp(Mathf.FloorToInt(a.Pos.X - _ox) - a.Col * TileW, 0, TileW - 1);
        int fy = Mathf.Clamp(Mathf.FloorToInt(a.Pos.Y - _oy) - a.Row * TileH, 0, TileH - 1);
        return HubOf(a.Col, a.Row, fx, TileH - fy);
    }

    /// <summary>bug-696: wo der Schatten liegt — flach: <c>Lage − Bodenanhebung</c>; alt:
    /// <c>Pos</c>.</summary>
    private Vector2 AirSchatten(Special s)
        => LageFlach ? s.Pos - new Vector2(0, AirBodenHub(s)) : s.Pos;

    /// <summary>bug-696: wo der Rumpf gezeichnet wird — flach: <c>Lage − alt</c>, ohne
    /// Klemme (das Original klemmt nicht, 0x42B8BD); alt: <c>Pos − AirHoehePx</c>. Mit
    /// <c>--flughoehe-bild-alt</c> fest 26 px ueber dem Schatten. Nachgefuehrt wird ueber
    /// <see cref="AirHoehePx"/> (Pruefzeile <c>flughuepfen</c>).</summary>
    private Vector2 AirRumpf(Special s)
    {
        float h = AirHoehePx(s);
        if (FlughoeheBildAlt || !LageFlach) return AirSchatten(s) - new Vector2(0, h);
        return s.Pos - new Vector2(0, s.Alt);
    }

    /// <summary>bug-696: die flache Lage, in den angehobenen Raum der Zelle (col, row)
    /// geschoben — <c>Lage − Anhebung(Zielzelle)</c>. Schuesse, Strahlen und Bomben laufen
    /// bei uns im Bodenraum (Ziel = angehobener Zielpunkt); so ist ihr Abstand der
    /// flache Kartenabstand, wie das Original ihn aus Zelle und Feinlage rechnet. In der
    /// alten Lage <c>Pos</c>.</summary>
    private Vector2 AirImRahmen(Special a, int col, int row)
        => LageFlach ? a.Pos - new Vector2(0, HubOf(col, row)) : a.Pos;

    /// <summary>bug-696: die Anhebung, mit der <see cref="AirImRahmen"/> schiebt (alt:
    /// der nachgefuehrte Hub <c>HubPx</c>).</summary>
    private float AirRahmenHub(Special a, int col, int row)
        => LageFlach ? HubOf(col, row)
                     : !FlughuepfenAlt && !float.IsNaN(a.HubPx) ? a.HubPx : ElevOf(a.Col, a.Row) * 15f;

    /// <summary>
    /// ⭐ bug-718: <c>--flugtempo-y-alt</c> — Stand bis 0.6.12: geflogen wird isotrop im
    /// BILDRAUM (gleich viele Bildpunkte je Takt in x und y). Das Original fliegt isotrop im
    /// FEINRAUM: Sollrichtung aus Zellendifferenzen (C 0x423F3E..0x423F6B, F
    /// 0x4230F7..0x42312D), Schritt <c>FeinX += sin·sp</c>, <c>FeinY += cos·sp</c>, 40 je
    /// Zelle in beiden Achsen (C 0x425085, F 0x424253), und der Zeichner nimmt
    /// <c>FeinY/2</c> (0x42B8C2). Im Bild ist y also HALB so schnell; eine Zelle nach Sueden
    /// dauert so lange wie eine nach Osten. Dazu misst die Steigregel »mehr als 6 Zellen«
    /// in Zellen, sqrt(dx² + dy²) (C 0x423F90..0x423FB8 → 0x424FF1 <c>cmp 6.0</c>).
    /// </summary>
    public static bool FlugtempoYAlt;

    /// <summary>bug-718: der Abstand zum Flugziel in ZELLEN, wie ihn das Original fuer
    /// die Steigregel (0x424FF1) UND die Tempostufe (0x424EA4) aus derselben Groesse
    /// <c>[esp+0x1C]</c> nimmt: <c>sqrt(dx² + dy²)</c> aus Zielzelle − Zelle (C
    /// 0x423F3E..0x423FB8). Alt (oder in der alten Lage): Bildabstand / 40, y also halb.
    /// Nur mit Flugziel aufrufen.
    /// <para>⚠ UNSERE SETZUNG: wir messen stufenlos im Feinraum (dx, 2·dy)/40 statt in
    /// ganzen Zellen. Unser Flugziel ist ein PUNKT (Ankunft bei 0,6…1,5 Zellen), das des
    /// Originals eine Zelle; mit ganzen Zellen faende die Tempostufe in der Zielzelle
    /// »0 → bremsen« (0x424EB1 jle) und koennte vor dem Punkt auf Stufe 0 stehen bleiben.
    /// Was gelesen ist und hier gilt: y zaehlt VOLL, nicht halb.</para></summary>
    private float FlugZellenZumZiel(Special a)
    {
        if (a.Goal is not { } g) return 0f;
        var d = g - a.Pos;
        if (FlugtempoYAlt) return d.Length() / TileW;
        return new Vector2(d.X, d.Y * 2f).Length() / TileW;
    }

    /// <summary>⭐ bug-717: <c>--flieger-klickkasten-alt</c> — Stand bis 0.6.12: 40 × 20
    /// mittig am Rumpf (UNSERE Erfindung).</summary>
    public static bool FliegerKlickkastenAlt;

    /// <summary>
    /// ⭐ bug-717: <b>der Klickkasten des Originals</b>, Treffersatz Art 30 — C
    /// <c>0x431F7F..0x431FC7</c>, F <c>0x4310C1..0x431109</c>, gleich:
    /// <c>sx − 30 &lt; mx &lt; sx + 40</c> und <c>sy − alt − 10 &lt; my &lt; sy − alt + 40</c>,
    /// alle vier STRENG, mit <c>(sx, sy)</c> = Eintrag des Listenbauers (Zellecke, OHNE
    /// Feinlage, 0x42E459/0x42E460) und <c>alt</c> = +0x0E. Der Kasten springt also mit
    /// der Zelle, nicht mit dem Bild.
    /// <para>Bezug zu unserem Bild: der Rumpf wird bei <c>sx + FeinX − 43</c>,
    /// <c>sy + FeinY/2 − alt − 35</c> geblittet (0x42B8A6..0x42B8DF), und unsere Leinwand
    /// beginnt an diesem Blitpunkt (Spalte <c>CanvasXPad</c>, Zeile 0; CwrFile). Unser
    /// Blitpunkt ist <c>AirRumpf − ComposedAnchor + (CanvasXPad, 0)</c>; daraus
    /// <c>(sx, sy − alt) = Blit + (43 − FeinX, 35 − FeinY/2)</c>. ⚠ UNSERE SETZUNG ist
    /// damit nur noch der Fahrzeuganker, ueber den unser Kartenraum geeicht ist (bug-727) —
    /// der Kasten sitzt so, wie das Bild sitzt.</para>
    /// <para>In der alten Lage (<c>--fluglage-alt</c>) gibt es keine Feinlage — dann gilt
    /// der alte Kasten.</para>
    /// </summary>
    private Rect2 AirKlickkasten(Special s)
    {
        var blit = AirBlit(s);   // bug-727
        int fx = Mathf.Clamp(Mathf.FloorToInt(s.Pos.X - _ox) - s.Col * TileW, 0, TileW - 1);
        int fy = Mathf.Clamp(Mathf.FloorToInt(s.Pos.Y - _oy) - s.Row * TileH, 0, TileH - 1);
        float sx = blit.X + 43 - fx, syAlt = blit.Y + 35 - fy;
        return new Rect2(sx - 30, syAlt - 10, 70, 50);
    }

    /// <summary>
    /// ⭐ bug-727: <b>der Bildanker der Flieger</b> — wo die Leinwand eines Rumpfbildes
    /// gegen den Rumpfpunkt <see cref="AirRumpf"/> (Lage − alt) liegt.
    /// <para>Gelesen, fuer ALLE Arten gleich (die Art waehlt nur den Bildsatz):
    /// Rumpf C <c>0x42B8A6..0x42B8DF</c> / F <c>0x42AA93..0x42AACC</c>:
    /// <c>Blit = (Spalte·40 + FeinX − 43, Zeile·20 + FeinY/2 − alt − 35)</c>; Schatten C
    /// <c>0x42D6AA..0x42D798</c> / F <c>0x42C88B..</c>: derselbe Versatz (−43, −35) um
    /// <c>Lage − Bodenanhebung</c>; Rotor (Art ≥ 10) C <c>0x42BAC9</c> / F <c>0x42ACB6</c>:
    /// <c>Rumpf-Blit + (0, −4)</c>.</para>
    /// <para>Unser Kartenraum ist der Bildschirm des Originals um <c>(_ox, _oy)</c>
    /// verschoben: das folgt aus dem Fahrzeuganker (24, 45) (bug-402, vom Spieler an Gelaende,
    /// Bruecke und Tor bestaetigt) gegen den Fahrzeug-Blit des Originals
    /// <c>(Spalte·40, Zeile·20 − 35 − Hub)</c>. Unsere Leinwand beginnt am Blitpunkt
    /// (Spalte <c>CanvasXPad</c>, Zeile 0, CwrFile). Also Leinwand = Rumpfpunkt
    /// − (43 + CanvasXPad, 35) = Rumpfpunkt − (47, 35).</para>
    /// <para><c>--flieger-anker-alt</c>: wie bis 0.6.12 <see cref="ComposedAnchor"/> (30, 55),
    /// der um (+17, −20) danebenlag (gemessen, <c>--flieger-anker-check</c>).</para>
    /// </summary>
    private static Vector2 FliegerAnker
        => FliegerAnkerAlt ? ComposedAnchor : new Vector2(43 + Import.CwrFile.CanvasXPad, 35);

    /// <summary>⭐ bug-728 (Spielerentscheidung 30.10.2026, »rotorschatten wie im original
    /// weg«): der Schattenzeichner des Originals (C 0x42D666, F 0x42C847) blittet fuer ein
    /// Flugzeug genau EIN Bild, den Rumpfschatten — keinen Rotorschatten. Teil 111 wird darum
    /// nicht mehr gezeichnet. <c>--rotorschatten-alt</c> zeichnet ihn wieder (Stand bis
    /// 0.6.12).</summary>
    public static bool RotorschattenAlt;

    /// <summary>bug-728: wie oft ein Rotorschatten gezeichnet wurde (Soll 0).</summary>
    public static int RotorschattenGezeichnet;

    /// <summary>bug-728: bekommt dieses Flugzeug einen Rotorschatten? Nur Hubschrauber
    /// (Rumpf 120) und nur mit <c>--rotorschatten-alt</c>.</summary>
    private static bool RotorschattenZeichnen(Special s) => RotorschattenAlt && s.Airframe == 120;

    /// <summary>bug-727: <c>--flieger-anker-alt</c>.</summary>
    public static bool FliegerAnkerAlt;

    /// <summary>bug-727: der Rotor sitzt 4 px ueber dem Rumpf-Blit (C 0x42BAC9
    /// <c>lea edx,[esi−4]</c>, F 0x42ACB6); alt: 0.</summary>
    private static Vector2 RotorVersatz => FliegerAnkerAlt ? Vector2.Zero : new Vector2(0, -4);

    /// <summary>bug-727: der Blitpunkt des Rumpfbildes in unserem Kartenraum (Leinwand +
    /// (CanvasXPad, 0)).</summary>
    private Vector2 AirBlit(Special s)
        => AirRumpf(s) - FliegerAnker + new Vector2(Import.CwrFile.CanvasXPad, 0);

    /// <summary>bug-717: trifft ein Klick an <paramref name="p"/> das Flugzeug? Streng auf
    /// allen vier Seiten wie 0x431FA8..0x431FC7.</summary>
    private bool AirKlickTrifft(Special s, Vector2 p)
    {
        if (FliegerKlickkastenAlt || !LageFlach) return AirRect(s).HasPoint(p);
        var k = AirKlickkasten(s);
        return p.X > k.Position.X && p.X < k.End.X && p.Y > k.Position.Y && p.Y < k.End.Y;
    }

    /// <summary>bug-696: ein Spielstand von vor der flachen Lage (ohne Schluessel
    /// <c>fluglage</c>) traegt Lage und Ziele im angehobenen Raum. Umgerechnet wird mit
    /// derselben Schaetzung, die bis 0.6.12 das Bild rechnete (<c>HubAnLage</c>: welche
    /// Zellmitte der Spalte liegt dem Punkt am naechsten). ⚠ UNSERE SETZUNG — eine
    /// Lesung gibt es dafuer nicht, es ist unser eigenes altes Format.</summary>
    private void FluglageAusAltemStand()
    {
        Vector2 Flach(Vector2 p) => p + new Vector2(0, HubAnLage(p) ?? 0f);
        foreach (var a in _special)
        {
            if (a.Dead || a.Stored) continue;
            a.Pos = Flach(a.Pos);
            if (a.Goal is { } g) a.Goal = Flach(g);
            if (a.PlayerGoal is { } pg) a.PlayerGoal = Flach(pg);
            if (a.HomePoint is { } hp) a.HomePoint = Flach(hp);
            if (a.TurnPoint is { } tp) a.TurnPoint = Flach(tp);
            AirZelleNachLage(a);
            FluglageUmgerechnet++;
        }
    }

    /// <summary>bug-696: wie viele Flugzeuge beim Laden aus einem alten Stand
    /// umgerechnet wurden.</summary>
    public static int FluglageUmgerechnet;

    /// <summary>bug-696: die Zelle unter einem flachen Punkt (ohne Anhebung).</summary>
    private Vector2I FlugZelleAn(Vector2 flach)
        => new(Mathf.FloorToInt((flach.X - _ox) / TileW), Mathf.FloorToInt((flach.Y - _oy) / TileH));
}
