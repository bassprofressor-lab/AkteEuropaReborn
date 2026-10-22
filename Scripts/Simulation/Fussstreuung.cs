namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <b>FUSSVOLK: VERSATZ JE SOLDAT, NEUNERZELLE, FUSSANKER, KLICKKASTEN UND
/// GUMMIBAND</b> — bug-596 (KayelGee R7 #4, 18.10.2026: »im Original verteilt sich
/// die Infanterie mehr ... bilden nicht eine perfekte Reihe«) und bug-602
/// (Fussvolk-Klickkasten). Lesungen <c>berichte/fussvolk-verteilung-opus.md</c> und
/// <c>berichte/fussvolk-klickkasten-opus.md</c>, gebaut am 22.10.2026 (Testerrunde
/// 0.6.11 Paket E).
///
/// <para><b>Versatz (Original).</b> Jeder Soldat traegt im Satz einen festen
/// Unterzellen-Versatz: <c>+0x1D</c> dx = 18 − rand%37, <c>+0x1E</c> dy = 7 − rand%15.
/// Geschrieben nur von den zwei Erzeugern, Aufsteller <c>0x4B1D37/0x4B1D50</c>
/// (Netzwuerfel 0x4C5B30) und create_unit <c>0x4B38BA/0x4B38D1</c> (CRT-rand 0x4D6C70).
/// Gelesen vom Infanteriearm des Zeichners <c>0x4302A0</c>: x = Spalte·40 + 5 + dx
/// (0x43032D), y = Zeile·20 − 24 (0x430332) − H(verschobener Punkt, 0x4303CF) − dy.</para>
///
/// <para><b>Versatz (wir).</b> Kartensoldaten nehmen die Rohwerte der Karte (510 von
/// 512 Saetzen ≠ 0). ⚠ <b>UNSERE SETZUNG:</b> alle anderen Soldaten bekommen den
/// Versatz NICHT aus dem Wuerfelstrom (<c>Determinism.Roll</c> wuerde jeden spaeteren
/// Wurf verschieben, Netz-Gleichschritt und Pruefsumme kippen), sondern aus einem
/// festen Hash von Spielkeim, Einheitennummer (Listenstelle) und Takt
/// (<see cref="StreuHash"/>). Wertebereich und Gleichverteilung sind die des
/// Originals, die einzelnen Zahlen nicht (das Original selbst ist dort nicht
/// wiederholbar). Vergeben wird am Taktanfang und vor dem Zeichnen
/// (<see cref="FussStreuungZuteilen"/>) — beide sehen denselben Taktzaehler, das
/// Ergebnis haengt also nicht davon ab, wer zuerst kommt. Der Versatz geht in den
/// Spielstand (<c>streu_x/streu_y</c>), nicht in die Pruefsumme: gelesen wird er nur
/// von Bild, Klickkasten und Gummiband (oertliche Eingabe).</para>
///
/// <para>⚠ <b>UNSERE SETZUNGEN beim Zeichnen:</b> die Hoehe am verschobenen Punkt
/// wird nur fuer STEHENDE Soldaten nachgerechnet (beim Gehen die Hoehe der laufenden
/// Lage); die Tiefensortierung nach der verschobenen Zeile (0x430380…0x4303AD) und
/// <c>PickWieOriginal</c> nach der verschobenen Zeile sind nicht gebaut (stehend gibt
/// es keinen Zelluebertrag: fx+dx ∈ [2, 38], fy+dy ∈ [3, 17]).</para>
///
/// <para><b>Neunerzelle.</b> <c>0x433DF0</c>: <c>@0x433E38 al := byte[0x7847E8 + 22·Z]</c>
/// (Mannzahl der Infanteriezelle), <c>@0x433E3E cmp al,9 ; jae → 0</c>. Ein Soldat
/// geht nur in eine Zelle, in der weniger als neun Mann stehen. ⚠ UNSERE SETZUNG:
/// die Mannzahl zaehlen wir aus den Ankern der Soldaten (Satzzelle UND vorgemerkte
/// Zelle), die Tuerzelle (Sonderfall 0x63) ist ausgenommen, und der zehnte bekommt
/// <c>Blocked</c> (Can_go 0) — er plant nach der Geduld neu, die volle Zelle ist dann
/// kein zulaessiges Ziel mehr (NavGrid.ZielZulaessig).</para>
///
/// <para>Gegenschalter: <c>--fussstreuung-aus</c>, <c>--neunerzelle-aus</c>,
/// <c>--fussanker-tief-alt</c>, <c>--fussvolk-klickkasten-alt</c>,
/// <c>--gummiband-kasten-alt</c>, <c>--gummiband-transporter-alt</c>,
/// <c>--gummiband-schwelle-alt</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--fussstreuung-aus</c>: kein Versatz je Soldat (Stand bis 0.6.10).</summary>
    public static bool FussstreuungAus;

    /// <summary><c>--neunerzelle-aus</c>: beliebig viele Soldaten je Zelle (Stand bis 0.6.10).</summary>
    public static bool NeunerzelleAus;

    /// <summary><c>--fussanker-tief-alt</c>: Leinwand aus der Unterkante des
    /// Stehbildes + <see cref="FussTiefer"/>, 15 px tiefer als das Original.</summary>
    public static bool FussankerTiefAlt;

    /// <summary><c>--fussvolk-klickkasten-alt</c>: der mittige 14×25-Kasten um Pos.</summary>
    public static bool FussvolkKlickkastenAlt;

    /// <summary><c>--gummiband-kasten-alt</c>: der Rahmen schneidet den Klickkasten.</summary>
    public static bool GummibandKastenAlt;

    /// <summary><c>--gummiband-transporter-alt</c>: Materialtransporter wie alle anderen.</summary>
    public static bool GummibandTransporterAlt;

    /// <summary><c>--gummiband-schwelle-alt</c>: Rahmen ab 5 Bildschirmpunkten euklidisch.</summary>
    public static bool GummibandSchwelleAlt;

    /// <summary>Die Bauteilzeile des Materialtransporters, Satz <c>+0x0E</c> (C 0x43496D).</summary>
    public const int MaterialtransporterTeil = 0x47;

    /// <summary>Wie viele Soldaten ihren Versatz aus dem Hash bekamen (Pruefstand).</summary>
    public static int StreuGehasht;

    /// <summary>Wie oft die Neunerzelle einen Schritt abgewiesen hat (Pruefstand).</summary>
    public static int NeunerzelleAbgewiesen;

    /// <summary>
    /// ⚠ UNSERE SETZUNG — der Versatz eines NICHT von der Karte stammenden Soldaten:
    /// ein stromfreier Hash (murmur3-Endmischung) aus Spielkeim, Listenstelle und Takt.
    /// dx = 18 − h%37 ∈ [−18, 18], dy = 7 − (h/37)%15 ∈ [−7, 7] — die Wertebereiche
    /// von 0x4B38BA/0x4B38D1.
    /// </summary>
    public static (sbyte Dx, sbyte Dy) StreuHash(uint seed, int nummer, int takt)
    {
        uint h = seed * 0x9E3779B1u;
        h ^= (uint)nummer * 0x85EBCA77u;
        h = (h << 13) | (h >> 19);
        h ^= (uint)takt * 0xC2B2AE3Du;
        h ^= h >> 16; h *= 0x85EBCA6Bu;
        h ^= h >> 13; h *= 0xC2B2AE35u;
        h ^= h >> 16;
        return ((sbyte)(18 - (int)(h % 37u)), (sbyte)(7 - (int)(h / 37u % 15u)));
    }

    /// <summary>Jedem Soldaten ohne Versatz einen geben — am Taktanfang und vor dem
    /// Zeichnen. Zieht nichts aus <c>Determinism</c>.</summary>
    private void FussStreuungZuteilen()
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.StreuGesetzt || e.Infantry < 0 || e.IsBuilding) continue;
            var (dx, dy) = StreuHash(Simulation.Determinism.Seed, i, _taktNr);
            e.StreuX = dx; e.StreuY = dy; e.StreuGesetzt = true;
            StreuGehasht++;
        }
    }

    /// <summary>Der wirksame Versatz (dx, dy) — 0 unter <c>--fussstreuung-aus</c>.</summary>
    private static (int Dx, int Dy) StreuWirksam(Entity e)
        => FussstreuungAus || e.Infantry < 0 || !e.StreuGesetzt ? (0, 0) : (e.StreuX, e.StreuY);

    /// <summary>Der Versatz in Bildkoordinaten (dx nach rechts, dy nach OBEN).</summary>
    private static Vector2 StreuBild(Entity e)
    {
        var (dx, dy) = StreuWirksam(e);
        return new Vector2(dx, -dy);
    }

    /// <summary>
    /// ⭐ <b>DER ZEICHENPUNKT (sx, sy) EINES FUSSSOLDATEN</b> — Infanteriearm
    /// <c>0x4302A0</c>: sx = Spalte·40 + 5 + dx, sy = Zeile·20 − 24 − H − dy, mit H am
    /// verschobenen Punkt (fx+dx, fy+dy) (0x4303CF). Bild (<see cref="FussvolkOrt"/>),
    /// Klickkasten (<see cref="BodyRect"/>, 0x431DD9) und Gummiband (0x4349AA) kommen
    /// aus DIESER Funktion — Regel Y: Bild und Kasten aus einer Quelle.
    /// ⚠ UNSERE SETZUNG: beim Gehen (Vormerkung gesetzt) keine Hoehe am verschobenen
    /// Punkt, die laufende Lage <c>Pos</c> traegt die Hoehe der Zwischenlage.
    /// </summary>
    public Vector2 FussvolkBlit(Entity e)
    {
        var (dx, dy) = StreuWirksam(e);
        var p = PictureAnchor(e);
        float korr = 0f;
        if (e.Reserved == null && (dx != 0 || dy != 0))
            korr = HubOf(e.Col, e.Row)
                 - HubOf(e.Col, e.Row, Simulation.Hang.MitteX + dx, Simulation.Hang.MitteY + dy);
        return new Vector2(p.X - 15f + dx, p.Y - 34f - dy + korr);
    }

    /// <summary>
    /// bug-602 E — <b>ab wann ein Zug ein Rahmen ist</b>: Zeigerwahl
    /// <c>0x431BD6</c>/<c>0x431BF0</c>, |Δx| &gt; 10 ODER |Δy| &gt; 10 Spielpunkte (bei uns
    /// Kartenpunkte). Der Aufrufer haelt den Rahmen dann bis zum Loslassen
    /// (<c>0x431C04</c>). <c>--gummiband-schwelle-alt</c>: euklidisch mehr als
    /// <paramref name="slop"/> Bildschirmpunkte.
    /// </summary>
    public static bool IstRahmen(Vector2 startKarte, Vector2 jetztKarte,
                                 Vector2 startSchirm, Vector2 jetztSchirm, float slop = 5f)
    {
        if (GummibandSchwelleAlt) return (jetztSchirm - startSchirm).Length() > slop;
        return Mathf.Abs(jetztKarte.X - startKarte.X) > 10f || Mathf.Abs(jetztKarte.Y - startKarte.Y) > 10f;
    }

    /// <summary>
    /// bug-602 C — <b>faengt der Rahmen diese Einheit?</b> (Rahmenauswahl
    /// <c>0x434700</c>, Verteiltafel <c>0x434EE0</c>, alles streng):
    /// <list type="bullet">
    /// <item>Fussvolk <c>0x4349AA</c>: der PUNKT (sx+20, sy+20) (0x434AFE/0x434B02).</item>
    /// <item>Fahrzeug <c>0x434854</c>: px = sx+5, py = sy+40 (0x43492B): links &lt; px+30,
    /// rechts &gt; px, oben &lt; py &lt; unten.</item>
    /// </list>
    /// ⚠ UNSERE SETZUNG: Klasse 3/4/5 (Bertha, Schiffe) und alles ohne Klasse bleiben
    /// beim Schnitt mit dem Klickkasten. <c>--gummiband-kasten-alt</c>: Schnitt fuer alle.
    /// </summary>
    private bool GummibandFaengt(Entity e, Rect2 norm)
    {
        if (GummibandKastenAlt || e.IsBuilding || e.IsProp) return norm.Intersects(BodyRect(e));
        float l = norm.Position.X, o = norm.Position.Y, r = norm.End.X, u = norm.End.Y;
        if (e.Infantry >= 0)
        {
            var b = FussvolkBlit(e) + new Vector2(20, 20);
            return l < b.X && b.X < r && o < b.Y && b.Y < u;
        }
        if (e.GameUnitType is 0 or -1 && e.Move != Simulation.NavGrid.MoveClass.Ship)
        {
            // stehend: sx = Spalte·40 = Pos.x − 20, sy = Zeile·20 − 35 − HUB = Pos.y − 45
            var p = PictureAnchor(e);
            float px = p.X - 15f, py = p.Y - 5f;
            return l < px + 30f && r > px && o < py && py < u;
        }
        return norm.Intersects(BodyRect(e));
    }

    /// <summary>
    /// bug-596 — <b>hat die Zelle Platz fuer noch einen Soldaten?</b> (0x433DF0,
    /// <c>@0x433E3E cmp al,9</c>). Gezaehlt werden lebende Soldaten ausser
    /// <paramref name="mover"/>, deren Satzzelle oder Vormerkung die Zelle ist.
    /// Die Tuerzelle eines Gebaeudes ist ausgenommen (Sonderfall 0x63, UNSERE SETZUNG).
    /// </summary>
    private bool NeunerzelleVoll(int c, int r, int mover)
    {
        if (NeunerzelleAus) return false;
        int n = 0;
        for (int j = 0; j < _entities.Count; j++)
        {
            if (j == mover) continue;
            var o = _entities[j];
            if (o.Infantry < 0 || o.Dead || o.IsBuilding || o.InGebaeude != null) continue;
            bool hier = (o.Col == c && o.Row == r) || (o.Reserved is { } rv && rv.X == c && rv.Y == r);
            if (!hier || Untergestellt(o)) continue;
            if (++n >= 9) break;
        }
        if (n < 9) return false;
        foreach (var b in _entities)
        {
            if (!b.IsBuilding || b.Dead) continue;
            foreach (var d in b.DoorCells)
                if (b.Col + d.Col == c && b.Row + d.Row == r) return false;
        }
        NeunerzelleAbgewiesen++;
        return true;
    }
}
