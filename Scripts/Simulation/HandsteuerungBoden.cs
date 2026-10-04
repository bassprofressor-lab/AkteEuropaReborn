namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// ⭐⭐ <b>DIE HANDSTEUERUNG BODEN, je SPIELTAKT</b> (03.10.2026, bug-414,
/// Meldung C von KayelGee, <c>berichte/maus-tab-handsteuerung-fable.md</c> §3).
///
/// <para><b>Original (O):</b> <c>0x433460</c> läuft im Taktblock der
/// Hauptschleife (<c>0x41685C</c>) und liest das <b>Tastenzustandsfeld</b>
/// <c>byte[0xA182E8+VK]</c> (gesetzt WM_KEYDOWN <c>0x412FD2</c>, gelöscht
/// WM_KEYUP <c>0x413E49</c>) — eine GEHALTENE Taste wirkt also jeden Takt:</para>
/// <code>
///   0x433497  r := Richtung Maus→Einheit ; r != OT_HLAV → Befehl 2 (0x4C277D):
///             +0x17 OTOC_HLAVEN := r                 → das ROHR folgt der Maus
///   0x433535  POHYB != 0xFF → raus                   → fährt gerade: nichts Neues
///   0x43354C  dx := R−L ; dy := D−U ; beide 0 → raus
///   0x433576  Index in der Tafel 0x4F5AF0 (= NavGrid.UrDirs)
///   0x4335B7  Befehl 1 → AKCE := Index ; Auftragsband UKOL 1 0x408A28:
///             Zelle frei → drehen, dann EIN Schritt ; sonst nichts
/// </code>
/// <para>Kein Klang, keine Wegsuche. Bei uns wird der Schritt wie der
/// Ausweichschritt (<see cref="AusweichTakt"/>) als <b>Weg von genau einer
/// Zelle</b> gelegt; Drehung, Vormerkung und Fortschritt macht der
/// gewöhnliche Fahrer — kein eigener Eingriff in den Fahrtakt.</para>
///
/// <para>⚠ <b>UNSERE Setzungen / (V):</b> »Zelle frei« ist unser
/// <c>NavGrid.IsFree</c> für die eigene Bewegungsklasse statt <c>Can_go</c>
/// mit seinen drei Antworten (frei / jemand muss weichen / nein); wer im Weg
/// steht, wird NICHT um Platz gebeten. Die Rohrrichtung ist
/// <c>DirToFacing(Maus − Einheit)</c> auf der Karte; der Bezugspunkt
/// <c>word[0x7AEC30/34]</c> des Originals ist (V). Das Rohr dreht der
/// Turmtakt (bug-406, nur Einheiten mit eigenem Turm, nicht unter
/// <c>--turm-alt</c>). Zeigerbild 1000 fehlt. ESC beendet die
/// Bodensteuerung NICHT (<c>0x44FE10</c> ungelesen).</para>
///
/// <para>Gegenschalter <c>--handsteuerung-boden-alt</c>: der Stand vor dem
/// 03.10.2026 — alle 0,25 s ein Fahrbefehl eine Zelle weiter über
/// <see cref="PostMove"/> (Wegsuche, Befehlsklang), Rohr nicht an der Maus,
/// Rechtsklick wählt ab statt die Steuerung abzugeben.
/// Prüfstand <c>--handsteuerung-boden-check</c> (MausTabLauf.cs).</para>
/// </summary>
public partial class MapEntityLayer
{
    public static bool HandsteuerungBodenAlt;
    public static bool HandsteuerungBodenCheckAn;

    /// <summary>Die Maus auf der Karte, je Bild von der Oberfläche abgelegt
    /// (Rohrrichtung, 0x433497).</summary>
    public Vector2? HandMausKarte;

    /// <summary><c>+0x17 OTOC_HLAVEN</c> der gesteuerten Einheit — in Achteln,
    /// −1 = keine.</summary>
    public int HandRohrRichtung = -1;

    /// <summary>Zähler: Schritte gelegt, Takte vor besetzter Zelle,
    /// Rohrbefehle (Befehl 2), und Aufrufe des ALTEN Wegs über PostMove.</summary>
    public int HandBodenSchritte, HandBodenBlockiert, HandBodenRohr, HandBodenPostMove;

    /// <summary>Ein Spieltakt der Handsteuerung Boden (<c>0x433460</c>, Bodenteil).
    /// Die Tasten liegen in <see cref="HandTasteLinks"/> … (dieselben Felder wie
    /// die Luft; beide schliessen sich aus).</summary>
    public void HandsteuerungBodenTakt()
    {
        int idx = HandsteuerungIdx;
        if (idx < 0 || idx >= _entities.Count) return;
        var e = _entities[idx];
        if (e.Dead || !e.Mobile) { HandsteuerungIdx = -1; HandRohrRichtung = -1; return; }

        // 0x433497 / 0x4334E0 — Befehl 2: das Rohr folgt der Maus.
        // Der Wunsch geht in +0x17 (Entity.TurmWunsch, Simulation/TurmDrehung.cs);
        // gedreht wird vom Turmtakt, eine Stufe je zwei Takte.
        // ⚠ (V) Der Wunsch wird JEDEN Takt gehalten, auch wenn das Rohr schon
        // steht: im Original ist die Einheit UKOL 1, also greift 0x409F2E
        // (»UKOL == 2 → Wunsch zum Endziel«) nicht; unser Turmtakt kennt UKOL 1
        // nicht und fragt nur »hat einen Weg«. Ohne das Halten zöge er das Rohr
        // bei jedem Handschritt zur Fahrtrichtung.
        if (HandMausKarte is { } m && (m - e.Pos).LengthSquared() > 0.01f)
        {
            int r = DirToFacing(m - e.Pos);
            HandRohrRichtung = r;
            if (TurmEigen(e))
            {
                if (r != e.AimFacing && e.TurmWunsch != r) HandBodenRohr++;   // Befehl 2
                if (e.TurmWunsch != r) e.TurmWunsch = r;
            }
        }

        // 0x433535 — POHYB != 0xFF: der laufende Schritt (oder Weg) geht vor.
        bool steht = (e.Path == null || e.PathIdx >= e.Path.Count) && e.SchrittAnteil < 0f;
        if (!steht) return;

        // 0x43354C — dx := R−L, dy := D−U (Tastenzustand, nicht Tastendruck).
        int dx = (HandTasteRechts ? 1 : 0) - (HandTasteLinks ? 1 : 0);
        int dy = (HandTasteRunter ? 1 : 0) - (HandTasteHoch ? 1 : 0);
        if (dx == 0 && dy == 0) return;
        // 0x433576 — die Tafel 0x4F5AF0; ihr Index wäre AKCE. Die Zelle folgt
        // aus demselben Eintrag.
        int k = System.Array.IndexOf(Simulation.NavGrid.UrDirs, new Vector2I(dx, dy));
        if (k < 0 || _nav == null) return;
        var d = Simulation.NavGrid.UrDirs[k];
        int c = e.Col + d.X, w = e.Row + d.Y;
        // 0x408A28 — Zelle nicht frei → in diesem Takt nichts.
        if (!_nav.InBounds(c, w) || !_nav.IsFree(c, w, e.Move, idx)) { HandBodenBlockiert++; return; }

        // Wie der Ausweichschritt: EIN Schritt als Weg, gefahren vom Fahrer.
        if (e.Reserved is { } rc) _nav.ClearOccupant(rc.X, rc.Y, idx);
        e.Reserved = null;
        e.Path = new List<Vector2I> { new(c, w) };
        e.PathIdx = 0;
        e.Goal = new Vector2I(c, w);
        e.StepCost = 0;
        HandBodenSchritte++;
    }

    /// <summary>Befehl 5 (<c>0x4C29C3</c>): UKOL := 0 — gesendet vom
    /// Rechtsklick ohne Kamerabewegung (<c>0x41449E</c>).</summary>
    public string HandsteuerungBodenAbgeben()
    {
        HandsteuerungIdx = -1;
        HandRohrRichtung = -1;
        return "Handsteuerung abgegeben";
    }

    // ---- --handsteuerung-boden-check ---------------------------------------

    /// <summary>Ein eigenes Landfahrzeug suchen, das in einer der vier
    /// Hauptrichtungen drei freie Zellen vor sich hat, und unter Handsteuerung
    /// stellen. Rückgabe: (Einheit, Richtung) oder null.</summary>
    public (int idx, Vector2I richtung)? HandBodenProbeStart()
    {
        if (_nav == null) return null;
        var richtungen = new[] { new Vector2I(1, 0), new Vector2I(-1, 0), new Vector2I(0, 1), new Vector2I(0, -1) };
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Dead || e.IsBuilding || e.IsProp || e.Owner != ViewPlayer || !e.Mobile) continue;
            if (e.Infantry >= 0 || e.Move != Simulation.NavGrid.MoveClass.Vehicle) continue;
            foreach (var r in richtungen)
            {
                bool frei = true;
                for (int s = 1; s <= 3 && frei; s++)
                {
                    int c = e.Col + r.X * s, w = e.Row + r.Y * s;
                    frei = _nav.InBounds(c, w) && _nav.IsFree(c, w, e.Move, i);
                }
                if (!frei) continue;
                e.Path = null; e.PathIdx = 0; e.Target = -1;
                _sel.Clear(); _sel.Add(i); SetPrimary();
                HandsteuerungIdx = i;
                HandBodenSchritte = HandBodenBlockiert = HandBodenRohr = HandBodenPostMove = 0;
                return (i, r);
            }
        }
        return null;
    }

    public Vector2I HandBodenProbeZelle(int idx) => new(_entities[idx].Col, _entities[idx].Row);
    public Vector2 HandBodenProbePos(int idx) => _entities[idx].Pos;
    public Vector2 ProbePosVon(int idx) => _entities[idx].Pos;
    public int ProbeTurmVon(int idx) => _entities[idx].AimFacing;
}
