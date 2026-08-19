namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <c>--schiff-waffe-check</c> — <b>wo verlässt ein Schuss ein SCHIFF?</b>
///
/// <para>Gemeldet: »ich hatte einen Kreuzer mit einer Langstreckenrakete oder
/// so, die Rakete wird irgendwo außerhalb vom Boot abgeschossen paar Felder
/// entfernt im Wasser, und diese Waffe sehe ich auch nicht auf dem
/// Schiff.«</para>
///
/// <para>Zwei Behauptungen, und beide sind messbar, statt sie zu erraten:</para>
/// <list type="number">
///   <item><b>Wie weit</b> liegt der Mündungspunkt von der Zelle des Schiffes?
///   Gerechnet wird er als <c>Pos + TurretOffset + Richtung × MuzzleReach</c>;
///   jeder der drei Summanden wird einzeln genannt, damit nicht die Summe
///   erklärt werden muss.</item>
///   <item><b>Trägt der Rumpf überhaupt einen Aufsatzpunkt?</b> In
///   <c>parts_index.json</c> haben die Rümpfe 150..152 einen (mit <c>x = 16</c>,
///   also seitlich!), die Rümpfe 153..158 <b>gar keinen</b> — dann ist der
///   Versatz Null und der Schuss kommt aus der Rumpfmitte.</item>
/// </list>
///
/// <para>⚠ Der Prüfstand URTEILT nicht darüber, wo der Punkt liegen SOLL — dafür
/// fehlt die gelesene Zahl (sie stünde in SHOOT.CWT, siehe <c>Fire</c>). Er
/// stellt fest, wie weit es ist. Ein Urteil ohne Sollwert wäre eine Meinung.</para>
/// </summary>
public partial class MapEntityLayer
{
    private bool _schiffCheck;

    /// <summary><c>--schiff-waffe-check</c> anwerfen.</summary>
    public void SchiffWaffeCheckStart() => _schiffCheck = true;

    private void PollSchiffCheck()
    {
        if (!_schiffCheck) return;
        _schiffCheck = false;
        var sb = new System.Text.StringBuilder("schiff-waffe-check\n");

        int n = 0, ohneAufsatz = 0;
        float schlimmst = 0f;
        for (int i = 0; i < _entities.Count && n < 12; i++)
        {
            var e = _entities[i];
            if (e.Dead || e.IsProp || e.IsBuilding) continue;
            if (!NavalTypes.Contains(e.UnitType)) continue;
            n++;

            // ⚠ Gemessen wird gegen den BILDpunkt, nicht gegen die Zellmitte:
            // das Schiff steht dort, wo es gezeichnet wird, und genau darauf ging
            // die Meldung. Die Zellmitte steht daneben, weil sie zeigt, wie weit
            // Bild und Grundriss auseinanderliegen.
            var mitte = CellCenter(e.Col, e.Row);
            var bild = PictureAnchor(e);
            var versatz = TurretOffset(e.UnitType, e.Col, e.Row);
            // eine Richtung annehmen, wie sie ein Schuss nach Osten hätte
            var dir = new Vector2(1, 0);
            var muendung = ShotOrigin(e) + dir * MuzzleReach;

            if (versatz == Vector2.Zero) ohneAufsatz++;

            string waffe = e.Weapon == 0 ? "keine" : WeaponOf(e.Weapon).Name;
            // 19.08.2026: RocketKind ist weg, die Geschosstafel entscheidet.
            // Der Pruefstand nennt jetzt die ART mit, denn sie ist es, an der
            // sich Flugbild, Tempo und Einschlag aufhaengen.
            int art = Simulation.DesignMath.SoundClass(WeaponRowOf(e.Weapon));
            string rak = FlightKind(art) is { } r ? $", Art {art}, Flugbild \"{r}\"" : $", Art {art}, ohne Flugbild";
            sb.AppendLine($"  Rumpf {e.UnitType} \"{e.Name}\" Zelle ({e.Col},{e.Row}) " +
                          $"Waffe {e.Weapon} = {waffe}{rak}");
            sb.AppendLine($"    Zellmitte ({mitte.X:0},{mitte.Y:0})  " +
                          $"Pos ({e.Pos.X:0},{e.Pos.Y:0})  " +
                          $"Pos-Zellmitte = ({e.Pos.X - mitte.X:0},{e.Pos.Y - mitte.Y:0})");
            sb.AppendLine($"    Aufsatzversatz ({versatz.X:0},{versatz.Y:0})" +
                          $"{(versatz == Vector2.Zero ? "  ⚠ KEINER — parts_index.json kennt diesen Rumpf nicht" : "")}" +
                          $"  Rohr {MuzzleReach:0}");
            sb.AppendLine($"    Bildpunkt ({bild.X:0},{bild.Y:0})  " +
                          $"Grundriss {Mathf.Max(1, e.FootW)}x{Mathf.Max(1, e.FootH)}");
            sb.AppendLine($"    -> Muendung ({muendung.X:0},{muendung.Y:0})");

            // ---- DIE EIGENTLICHE FRAGE: liegt der Punkt AUF dem Rumpfbild? ----
            //
            // ⚠ Nicht gerechnet, sondern an den PIXELN gemessen. Der Nachtrag
            // vom 13.08.2026 hat das fuer das Schlachtschiff (157) getan; der
            // Kreuzer (158) steht in keiner der Tabellen, und eine Uebertragung
            // waere eine Annahme. Gesucht wird die undurchsichtige Flaeche des
            // Rumpfbildes in Weltkoordinaten — mit DEMSELBEN Anker, mit dem
            // DrawUnitBody es absetzt (`picC - ComposedAnchor`).
            var hull = GetHullTexture(e.UnitType, e.Facing, PoseOf(e), SlopeClassOf(e.Col, e.Row));
            if (hull == null) { sb.AppendLine("    (kein Rumpfbild — nicht messbar)"); continue; }
            var img = hull.GetImage();
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
            for (int yy = 0; yy < img.GetHeight(); yy++)
                for (int xx = 0; xx < img.GetWidth(); xx++)
                    if (img.GetPixel(xx, yy).A > 0.5f)
                    {
                        if (xx < x0) x0 = xx; if (xx > x1) x1 = xx;
                        if (yy < y0) y0 = yy; if (yy > y1) y1 = yy;
                    }
            if (x0 > x1) { sb.AppendLine("    (Rumpfbild ganz durchsichtig)"); continue; }
            var ecke = bild - new Vector2(30, 55);      // ComposedAnchor
            var sicht = new Rect2(ecke + new Vector2(x0, y0), new Vector2(x1 - x0 + 1, y1 - y0 + 1));
            sb.AppendLine($"    sichtbarer Rumpf: ({sicht.Position.X:0},{sicht.Position.Y:0}) " +
                          $"bis ({sicht.End.X:0},{sicht.End.Y:0})");
            bool posDrauf = sicht.HasPoint(e.Pos);
            bool ankerDrauf = sicht.HasPoint(bild);
            sb.AppendLine($"    Pos liegt auf dem Rumpf: {(posDrauf ? "JA" : "⚠ NEIN")}   " +
                          $"Bildanker liegt darauf: {(ankerDrauf ? "JA" : "nein")}");
            if (!posDrauf) schlimmst = Mathf.Max(schlimmst, 1f);
        }

        if (n == 0) { sb.Append("  KEIN URTEIL: kein Schiff auf dieser Karte"); GD.Print(sb); return; }

        // ---- DIE ZWEITE FRAGE: bleibt das so, wenn das Schiff FAEHRT? -------
        //
        // ⚠⚠ Hier stand erst eine NACHRECHNUNG (»der Schritt setzt Pos auf
        // CellCenter«). Die war wertlos: sie bildete den Code ab, statt ihn
        // laufen zu lassen, und meldete darum nach der Reparatur denselben
        // Fehler wie davor. Jetzt faehrt das Schiff wirklich — ueber den
        // Befehlsring, wie ein Klick des Spielers.
        int gross = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Dead || e.IsProp || e.IsBuilding) continue;
            if (!NavalTypes.Contains(e.UnitType)) continue;
            if (Mathf.Max(1, e.FootW) < 2 && Mathf.Max(1, e.FootH) < 2) continue;
            gross = i; break;
        }
        if (gross < 0) { sb.Append("  (kein mehrzelliges Schiff fuer die Fahrprobe)"); GD.Print(sb); return; }

        var g = _entities[gross];
        g.Owner = g.Team = g.ShownOwner = ViewPlayer;     // damit der Befehl angenommen wird
        _sel.Clear(); _sel.Add(gross); _selected = gross;
        _schiffIdx = gross;
        _schiffPos0 = g.Pos;
        int saetze = PostMove(CellCenterFor(new Vector2I(g.Col + 3, g.Row)));
        sb.AppendLine();
        sb.AppendLine($"  FAHRPROBE an Rumpf {g.UnitType} " +
                      $"({Mathf.Max(1, g.FootW)}x{Mathf.Max(1, g.FootH)}), Zelle ({g.Col},{g.Row})");
        sb.AppendLine($"    steht: Pos ({g.Pos.X:0},{g.Pos.Y:0}), " +
                      $"BodyCenter ({BodyCenter(g).X:0},{BodyCenter(g).Y:0}) — " +
                      $"Abweichung {(g.Pos - BodyCenter(g)).Length() / TileW:0.00} Felder");
        sb.AppendLine($"    Fahrbefehl 3 Zellen nach rechts: {saetze} Satz/Saetze");
        if (saetze == 0) { sb.Append("    KEIN URTEIL: der Befehl kam nicht durch"); GD.Print(sb); return; }
        _schiffLog = sb;
        _schiffSim = DebugTicks;
        _schiffStufe = 1;
    }

    private int _schiffIdx = -1, _schiffStufe;
    private Vector2 _schiffPos0;
    private long _schiffSim;
    private System.Text.StringBuilder? _schiffLog;

    private void PollSchiffFahrt()
    {
        if (_schiffStufe != 1 || DebugTicks - _schiffSim < 600) return;
        _schiffStufe = 0;
        var sb = _schiffLog ?? new System.Text.StringBuilder();
        var g = _entities[_schiffIdx];
        var soll = BodyCenter(g);
        float ab = (g.Pos - soll).Length() / TileW;
        sb.AppendLine();
        sb.AppendLine($"    nach 600 Schritten: Zelle ({g.Col},{g.Row}), " +
                      $"Pos ({g.Pos.X:0},{g.Pos.Y:0}), BodyCenter ({soll.X:0},{soll.Y:0})");
        sb.AppendLine($"    gefahren: {(g.Pos - _schiffPos0).Length() / TileW:0.00} Felder");
        // ⚠ Waehrend eines Schrittes liegt Pos ZWISCHEN zwei Zellen — dann ist
        // eine Abweichung von der Zellmitte richtig. Gemessen wird deshalb gegen
        // BodyCenter DERSELBEN Zelle, und toleriert wird ein Schritt.
        bool ok = ab < 1.0f;
        sb.Append($"    Abweichung Pos gegen BodyCenter: {ab:0.00} Felder " +
                  (ok ? "— in Ordnung" : "— ⚠ FEHLER: das Schiff und sein Bild stehen auseinander"));
        GD.Print(sb);
        GD.Print(sb);
    }
}
