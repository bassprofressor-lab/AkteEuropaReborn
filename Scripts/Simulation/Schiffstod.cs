namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// ⭐⭐ <b>DAS GROSSE TODESBILD DER GATTUNGEN 3/4/5 — SCHIFFE UND RUMPF 138</b>
/// (04.10.2026, bug-433). Bis heute bekam ein versenktes Schiff dasselbe kleine
/// Muendungsfuenkchen »explosion« (ANIM 48, sieben Bilder) wie ein Raketenabschuss,
/// keine Teile, und seine Zellen blieben, wie sie waren.
///
/// <para><b>Gelesen (EXE C, selbst zerlegt).</b> Die Todesroutine <c>0x40B3C0</c>
/// verzweigt @0x40B5E0 ueber die Tafel <c>0x40B858</c> nach der Gattung (+0x0A):
/// <c>0 → 0x40B5E7</c> (Fahrzeug), <c>1 → 0x40B6A9</c>, <c>2 → 0x40B6C2</c> (nichts),
/// <c>3 und 4 → 0x40B6B1</c> → Sprungbrett <c>0x40128A → 0x4AEBA0</c>,
/// <c>5 → 0x40B6B9</c> → <c>0x401168 → 0x4AED80</c>. Die acht Nachbarn trifft nur
/// Gattung 0 (@0x40B6C2).</para>
///
/// <para>Beide Routinen lesen aus dem Einheitensatz (<c>0x6E26C8 + 78·id</c>)
/// RX (+0x00), RY (+0x01), POHYB (+0x04, Schrittrichtung, 0xFF = steht) und KOLIK
/// (+0x06, Schrittfortschritt) und legen Bilder ueber <c>0x401D5C → 0x4357F0</c> an.
/// <c>0x4357F0</c> ist <c>0x435950</c> (Fahrzeugwolke) mit zwei Zusatzargumenten:
/// Feinlage aus KOLIK/POHYB (<c>0x435BD0</c>, steht = Zellmitte 20/10) PLUS
/// <c>(dx, dy)</c>, Ueberlauf je Achse einmal bei 40 (@0x435895..0x4358D3).
/// Der Einsortierer <c>0x42E8D0</c> zeichnet bei <c>(RY+1)·20 − FeinY − Hoehe − 5</c>
/// (@0x42E907, @0x42E99D..0x42E9AB): <b>FeinY waechst nach OBEN</b>, ein <c>dy</c>
/// hebt das Bild also an.</para>
/// <code>
///   0x4AEBA0  (Gattung 3 und 4)                 Zelle (RX, RY+1)
///     2x  0x4357F0(.., 510 + rand%9,  rand%40, rand%20)   ; Sprengwolke
///     5x  0x4357F0(.., 310 + rand%6,  rand%60, rand%20)   ; Flamme
///     5x  0x4357F0(.., 230 + rand&amp;3, rand%60, rand%20)   ; Glut
///     rand%10 + 10  x  0x4AD7B0(RX, RY+1, .., Art 1, Streuung 8, Sorte 1)  ; Brocken
///     20            x  0x4AD7B0(RX, RY+1, .., Art 1, Streuung 6, Sorte 0)  ; Splitter
///   0x4AED80  (Gattung 5)        Bildzelle (RX + rand%3, RY + 2 + rand%3), je Bild neu
///     10x Sprengwolke (rand%40, rand%20) ; 20x Flamme (rand%60, rand%20) ;
///     30x Glut (rand%60, rand%20)
///     rand%30 + 40  x  0x4AD7B0(RX+1, RY+3, .., 1, Streuung 10, Sorte 1)
///     60            x  0x4AD7B0(RX+1, RY+3, .., 1, Streuung  7, Sorte 0)
/// </code>
/// <para>⚠ Der Vorbefund nannte die Teile »Art 8 / Art 6« — es sind die
/// STREUUNGEN; die Reihenfolge der Argumente ist am Fahrzeugtod belegt
/// (@0x40B63A <c>push 0 ; push 3 ; push 1</c> = Sorte, Streuung, Art).</para>
///
/// <para><b>Die Zellen danach</b> (»likvid typ« @0x406F1B, Tafel <c>0x40A048</c>,
/// nach den 6 Takten des stehenden Rumpfs @0x406EE6): Gattung 3 <c>0xFFFE</c> 2x2,
/// 4 <c>0xFFFC</c> 2x2, 5 <c>0xFFFC</c> 4x4 — an der Satzzelle UND, bei laufendem
/// Schritt (+0x04 != 0xFF, +0x06 &gt;= 0), an der Zielzelle (Tafel 0x4F5AF0).
/// Siehe <see cref="Simulation.NavGrid.UntergangsBoden"/>.</para>
///
/// <para>⚠ <b>UNSERE SETZUNGEN (V):</b> (1) der Bildanker wie beim Fahrzeugtod
/// (Zellmitte − 6 statt − 5, Pos-Konvention seit 24.08.); (2) die Satzzelle eines
/// fahrenden Schiffs wird aus <c>Pos</c> zurueckgerechnet (die Zelle, in der der
/// Ankerpunkt gerade liegt — das Original wechselt RX/RY auf halbem Weg); der
/// Ueberlauf von FeinY bei 40 (der nur bei fahrendem Schiff und grossem dy greift
/// und dann um 20 px versetzt) ist NICHT nachgebildet; (3) die Bildfolgen laufen
/// auf der einen Effektuhr (<see cref="EffektTakteJeSekunde"/>); (4) bei uns
/// sind Boden und Belegung getrennt, die Belegung ist schon im Todestakt frei
/// (wie beim Fahrzeug, Kopf von TruemmerCheck.cs).</para>
///
/// <para>Gegenschalter <c>--schiffstod-alt</c> (Stand 3b13948: »explosion«, keine
/// Teile, kein stehender Rumpf fuer 4/5, Zellen unberuehrt). Pruefstand
/// <c>--schiffstod-check</c>, mit <c>--shot=…png</c> ein Bildlauf.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--schiffstod-alt</c> — Stand 3b13948.</summary>
    public static bool SchiffstodAlt;

    /// <summary>Was der letzte Schiffstod angelegt hat — fuer den Pruefstand.</summary>
    public int SchiffstodWolken, SchiffstodFlammen, SchiffstodGlut, SchiffstodBrocken, SchiffstodSplitter;

    /// <summary>Wie viele Zellen nach einem Untergang ihren Boden gewechselt haben.</summary>
    public int UntergangZellen;

    /// <summary>Wer gerade untergeht: seine Anker (Satzzelle, ggf. Schrittziel) und Gattung.</summary>
    private readonly Dictionary<Entity, (List<Vector2I> Anker, int Gattung)> _untergang = new();

    /// <summary>Faellt dieser Tod unter den neuen Weg?</summary>
    private static bool SchiffstodNeu(Entity v)
        => !SchiffstodAlt && !v.IsBuilding && !v.IsProp && v.Infantry < 0
           && v.GameUnitType is 3 or 4 or 5;

    /// <summary>Seitenlaenge des Quadrats, das likvid typ schreibt.</summary>
    private static int UntergangSeite(int gattung) => gattung == 5 ? 4 : 2;

    /// <summary>VOR dem Austragen aus dem Gitter: welche Anker die Einheit haelt.</summary>
    private void UntergangMerken(int vi, Entity v)
    {
        var l = new List<Vector2I> { new(v.Col, v.Row) };
        if (_nav != null && vi >= 0)
            foreach (var a in _nav.AnkerVon(vi)) if (!l.Contains(a)) l.Add(a);
        if (v.Reserved is { } rc && !l.Contains(rc)) l.Add(rc);
        _untergang[v] = (l, v.GameUnitType);
    }

    /// <summary>likvid typ fuer 3/4/5 (@0x407026/@0x407094/@0x4070FF).</summary>
    private void UntergangAbschliessen(Entity v)
    {
        if (!_untergang.Remove(v, out var u) || _nav == null) return;
        var boden = u.Gattung == 3 ? Simulation.NavGrid.Ground.Free : Simulation.NavGrid.Ground.Water;
        int seite = UntergangSeite(u.Gattung);
        foreach (var a in u.Anker) UntergangZellen += _nav.UntergangsBoden(a.X, a.Y, seite, boden);
    }

    /// <summary>Ein Bild ueber 0x4357F0: Zellmitte + Schrittversatz + (dx, −dy).</summary>
    private void SchiffsBild(string kind, int c, int r, Vector2 schritt, int dx, int dy)
    {
        _effects.Add(new Effect
        {
            Pos = CellCenter(c, r) + schritt + new Vector2(dx, -dy) - new Vector2(0, 6),
            Kind = kind,
            FrameTime = 1f / EffektTakteJeSekunde,
        });
    }

    /// <summary>0x4AEBA0 (Gattung 3/4) bzw. 0x4AED80 (Gattung 5).</summary>
    private void SchiffsTodesbild(Entity v)
    {
        // Satzzelle: Pos ist bei uns die Mitte des Rumpfs, das Original rechnet
        // ab der Ankerzelle (RX, RY) plus Schrittfeinlage. Setzung (2), s. Kopf.
        var anker = v.Pos - (BodyCenterAt(v, v.Col, v.Row) - CellCenter(v.Col, v.Row));
        int hub = HubOf(v.Col, v.Row);
        int ac = Mathf.FloorToInt((anker.X - _ox) / TileW);
        int ar = Mathf.FloorToInt((anker.Y + hub - _oy) / TileH);
        var schritt = anker - CellCenter(ac, ar);

        _truemmerWurf++;
        int geworfen0 = TruemmerGeworfen, brocken0 = TruemmerBrockenGeworfen;
        SchiffstodWolken = SchiffstodFlammen = SchiffstodGlut = 0;
        static int R(int n) => Simulation.Determinism.Roll(n);

        if (v.GameUnitType == 5)
        {
            // 0x4AED80: je Bild rand%20, rand%(40|60), Folge, dann Zeile RY+2+rand%3
            // (@0x4AEDF4) und Spalte RX+rand%3 (@0x4AEE06)
            for (int k = 0; k < 10; k++)
            {
                int dy = R(20), dx = R(40), f = R(9), zr = ar + 2 + R(3), zc = ac + R(3);
                SchiffsBild("sprengung" + f, zc, zr, schritt, dx, dy); SchiffstodWolken++;
            }
            for (int k = 0; k < 20; k++)
            {
                int dy = R(20), dx = R(60), f = R(6), zr = ar + 2 + R(3), zc = ac + R(3);
                SchiffsBild("flamme" + f, zc, zr, schritt, dx, dy); SchiffstodFlammen++;
            }
            for (int k = 0; k < 30; k++)
            {
                int dy = R(20), dx = R(60), f = R(4), zr = ar + 2 + R(3), zc = ac + R(3);
                SchiffsBild("glut" + f, zc, zr, schritt, dx, dy); SchiffstodGlut++;
            }
            var quelle = new Entity { Col = ac + 1, Row = ar + 3 };          // @0x4AEF0C..0x4AEF1C
            int n = R(30) + 40;                                               // @0x4AEF04
            for (int k = 0; k < n; k++) EinTeil(quelle, sorte: 1, streuung: 10);
            for (int k = 0; k < 60; k++) EinTeil(quelle, sorte: 0, streuung: 7);
        }
        else
        {
            // 0x4AEBA0: alles auf der Zelle (RX, RY+1) (@0x4AEBD3 inc bl)
            int zc = ac, zr = ar + 1;
            for (int k = 0; k < 2; k++)
            {
                int dy = R(20), dx = R(40), f = R(9);
                SchiffsBild("sprengung" + f, zc, zr, schritt, dx, dy); SchiffstodWolken++;
            }
            for (int k = 0; k < 5; k++)
            {
                int dy = R(20), dx = R(60), f = R(6);
                SchiffsBild("flamme" + f, zc, zr, schritt, dx, dy); SchiffstodFlammen++;
            }
            for (int k = 0; k < 5; k++)
            {
                int dy = R(20), dx = R(60), f = R(4);
                SchiffsBild("glut" + f, zc, zr, schritt, dx, dy); SchiffstodGlut++;
            }
            var quelle = new Entity { Col = zc, Row = zr };
            int n = R(10) + 10;                                               // @0x4AECC3
            for (int k = 0; k < n; k++) EinTeil(quelle, sorte: 1, streuung: 8);
            for (int k = 0; k < 20; k++) EinTeil(quelle, sorte: 0, streuung: 6);
        }
        SchiffstodBrocken = TruemmerBrockenGeworfen - brocken0;
        SchiffstodSplitter = TruemmerGeworfen - geworfen0 - SchiffstodBrocken;
    }

    // ---- der Pruefstand --------------------------------------------------------

    /// <summary>
    /// <c>--schiffstod-check</c> — je Gattung 3/4/5 die erste lebende Einheit der
    /// Karte (sichtbare zuerst) versenken und messen:
    /// <list type="number">
    /// <item>Sprengwolken 510..518 (Soll 2 bzw. 10);</item>
    /// <item>Flammen 310..315 (5 bzw. 20) und Glut 230..233 (5 bzw. 30);</item>
    /// <item>Brocken (10..19 bzw. 40..69) und Splitter (20 bzw. 60);</item>
    /// <item>der Rumpf steht 6 Takte (Sterbend bis Takt 5, weg bei 6);</item>
    /// <item>die Zellen: vor Takt 6 unberuehrt, danach alle Wasser (4/5) bzw.
    /// frei (3), keine mehr von der Einheit belegt.</item>
    /// </list>
    /// ⚠ PRUEFSTAND-EINGRIFF: vor dem Tod wird der Boden des Rumpfquadrats auf
    /// »gesperrt« gesetzt — so steht im Original an seiner Stelle die Einheit
    /// selbst in der imap. Ohne das waere Zeile 5 unter Wasser immer erfuellt.
    /// Nullmodell <c>--schiffstod-alt</c> muss durchfallen.
    /// </summary>
    public string SchiffstodCheck()
    {
        var sb = new System.Text.StringBuilder("schiffstod-check (bug-433)\n");
        sb.Append($"  Schalter: schiffstod-alt {SchiffstodAlt}, sterbend-alt {SterbendAlt}\n");
        float dt = 1f / EffektTakteJeSekunde;
        bool ok = true;
        int geprueft = 0;
        BuildUnitDrawOrder();
        foreach (int g in new[] { 3, 4, 5 })
        {
            int vi = -1;
            foreach (bool nurSichtbare in new[] { true, false })
            {
                for (int i = 0; i < _entities.Count && vi < 0; i++)
                {
                    var e = _entities[i];
                    if (e.Dead || e.IsProp || e.IsBuilding || e.Infantry >= 0 || e.GameUnitType != g) continue;
                    if (nurSichtbare && !_unitDraw.Contains(i)) continue;
                    vi = i;
                }
                if (vi >= 0) break;
            }
            if (vi < 0) { sb.Append($"  Gattung {g}: keine auf dieser Karte\n"); continue; }
            geprueft++;
            var v = _entities[vi];
            string name = v.GameUnitType is 4 or 5 && _schiffsnamen.TryGetValue(v.UnitType, out var sn) ? sn : $"Rumpf {v.UnitType}";
            bool warSichtbar = _unitDraw.Contains(vi);

            // Eingriff: Rumpfquadrat an allen Ankern auf »gesperrt«
            var anker = new List<Vector2I> { new(v.Col, v.Row) };
            if (_nav != null) foreach (var a in _nav.AnkerVon(vi)) if (!anker.Contains(a)) anker.Add(a);
            if (v.Reserved is { } rc && !anker.Contains(rc)) anker.Add(rc);
            int seite = UntergangSeite(g);
            var zellen = new HashSet<Vector2I>();
            foreach (var a in anker)
                for (int dx = 0; dx < seite; dx++)
                    for (int dy = 0; dy < seite; dy++)
                        if (_nav != null && _nav.InBounds(a.X + dx, a.Y + dy)) zellen.Add(new Vector2I(a.X + dx, a.Y + dy));
            foreach (var z in zellen) _nav?.BodenFuerPruefstand(z.X, z.Y, Simulation.NavGrid.Ground.Blocked);

            int fx0 = _effects.Count, teile0 = TruemmerGeworfen, brocken0 = TruemmerBrockenGeworfen;
            Kill(vi, v, -1, "schiffstod-check");
            var neu = _effects.Skip(fx0).Select(x => x.Kind).ToList();
            int wolken = neu.Count(k => k.StartsWith("sprengung"));
            int flammen = neu.Count(k => k.StartsWith("flamme"));
            int glut = neu.Count(k => k.StartsWith("glut"));
            int kleine = neu.Count(k => k == "explosion");
            int brocken = TruemmerBrockenGeworfen - brocken0;
            int splitter = TruemmerGeworfen - teile0 - brocken;

            bool steht = v.Sterbend;
            int unberuehrt5 = 0;
            bool steht5 = false, weg6 = false;
            for (int t = 1; t <= SterbendTakte + 2; t++)
            {
                UpdateEffects(dt);
                if (t == SterbendTakte - 1)
                {
                    steht5 = v.Sterbend;
                    unberuehrt5 = zellen.Count(z => _nav!.GroundAt(z.X, z.Y) == Simulation.NavGrid.Ground.Blocked);
                }
                if (t == SterbendTakte) weg6 = !v.Sterbend;
            }
            var soll = g == 3 ? Simulation.NavGrid.Ground.Free : Simulation.NavGrid.Ground.Water;
            int richtig = zellen.Count(z => _nav!.GroundAt(z.X, z.Y) == soll && _nav.BesetztVon(z.X, z.Y) != vi);

            bool gross = g == 5;
            bool z1 = wolken == (gross ? 10 : 2);
            bool z2 = flammen == (gross ? 20 : 5) && glut == (gross ? 30 : 5);
            bool z3 = gross ? brocken is >= 40 and <= 69 && splitter == 60
                            : brocken is >= 10 and <= 19 && splitter == 20;
            bool z4 = steht && steht5 && weg6;
            bool z5 = zellen.Count > 0 && unberuehrt5 == zellen.Count && richtig == zellen.Count;
            bool gut = z1 && z2 && z3 && z4 && z5;
            ok &= gut;
            sb.Append($"  Gattung {g} {name} Platz {v.Slot} ({v.Col},{v.Row}){(warSichtbar ? "" : " [im Nebel]")}:\n");
            sb.Append($"    1 Sprengwolken 510..518: {wolken} (Soll {(gross ? 10 : 2)}){(kleine > 0 ? $", altes 'explosion' {kleine}x" : "")}  {(z1 ? "ja" : "NEIN")}\n");
            sb.Append($"    2 Flammen 310..315: {flammen} (Soll {(gross ? 20 : 5)}), Glut 230..233: {glut} (Soll {(gross ? 30 : 5)})  {(z2 ? "ja" : "NEIN")}\n");
            sb.Append($"    3 Brocken {brocken} (Soll {(gross ? "40..69" : "10..19")}), Splitter {splitter} (Soll {(gross ? 60 : 20)})  {(z3 ? "ja" : "NEIN")}\n");
            sb.Append($"    4 Rumpf steht: im Todestakt {steht}, Takt {SterbendTakte - 1} {steht5}, weg bei Takt {SterbendTakte} {weg6}  {(z4 ? "ja" : "NEIN")}\n");
            sb.Append($"    5 Zellen ({anker.Count} Anker, {seite}x{seite}, {zellen.Count} Zellen): Takt {SterbendTakte - 1} unberuehrt {unberuehrt5}/{zellen.Count}, "
                    + $"danach {(g == 3 ? "frei" : "Wasser")} und unbelegt {richtig}/{zellen.Count}  {(z5 ? "ja" : "NEIN")}\n");
        }
        int fehlend = Enumerable.Range(0, 6).Count(k => EffectFrames("flamme" + k).Count == 0)
                    + Enumerable.Range(0, 4).Count(k => EffectFrames("glut" + k).Count == 0)
                    + Enumerable.Range(0, 9).Count(k => EffectFrames("sprengung" + k).Count == 0);
        if (fehlend > 0) sb.Append($"  ⚠ {fehlend} von 19 Bildfolgen (sprengung/flamme/glut) fehlen — --reexport-effects=<Quelle>\n");
        if (geprueft == 0) { sb.Append("  keine Einheit der Gattung 3/4/5 — der Lauf sagt NICHTS\n"); ok = false; }
        sb.Append(ok ? $"  BESTANDEN ({geprueft} Gattungen)" : "  DURCHGEFALLEN");
        return sb.ToString();
    }

    // ---- Bildlauf ---------------------------------------------------------------

    /// <summary>Fuer den Bildlauf: das erste Schiff der Gattung (oder irgendeiner
    /// 3/4/5), sichtbare zuerst; Kamerapunkt.</summary>
    public Vector2? SchiffstodBildVorbereiten(int gattung, out int vi)
    {
        vi = -1;
        BuildUnitDrawOrder();
        foreach (bool nurSichtbare in new[] { true, false })
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (e.Dead || e.IsProp || e.IsBuilding || e.Infantry >= 0) continue;
                if (!(gattung > 0 ? e.GameUnitType == gattung : e.GameUnitType is 3 or 4 or 5)) continue;
                if (nurSichtbare && !_unitDraw.Contains(i)) continue;
                vi = i;
                return e.Pos + new Vector2(0, e.GameUnitType == 5 ? 10 : 0);
            }
        return null;
    }

    public string SchiffstodBildName(int vi)
    {
        if (vi < 0) return "";
        var v = _entities[vi];
        string name = _schiffsnamen.TryGetValue(v.UnitType, out var sn) ? sn : $"Rumpf {v.UnitType}";
        return $"Gattung {v.GameUnitType} {name} Platz {v.Slot} ({v.Col},{v.Row})";
    }
}
