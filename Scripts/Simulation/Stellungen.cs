namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// ⭐⭐ 02.10.2026 (bug-399) — <b>DIE AUFGEBAUTE ABWEHRSTELLUNG</b> (Bauteil 171,
/// Chassis 14, faze 1, ANIM_SPODEK 7). Gemeldet in K24: »Diverse Einheiten, die
/// scheinbar eingegraben sind, ragen so komisch aus dem Boden heraus.«
/// Gelesen in berichte/eingegraben-fable.md; gebaut sind die Vorschläge A und C:
/// <list type="bullet">
///   <item><b>A</b> — das Bild der Gruppen 1…7 ist richtungslos (nur f3 ≡ f7):
///   fehlt der Blick, nimmt <see cref="GetHullTexture"/> f7/f3 DERSELBEN Gruppe
///   statt Gruppe 0 (unsere Setzung für die 40 unbestimmten Blicke von K24).
///   Gegenschalter <c>--eingegraben-bild-alt</c>.</item>
///   <item><b>C</b> — der Turmsitz <c>Mount · (ANIM_SPODEK + 10) / 10</c> bei
///   <c>faze == 1 &amp;&amp; Chassis == 14</c> (@0x42A02D..0x42A089), flach (0,−15)
///   statt (0,−9). Gegenschalter <c>--stellungsturm-alt</c>.</item>
/// </list>
/// ⚠ Vorschlag B (der senkrechte Fahrzeuganker, »alle Fahrzeuge 10 px zu hoch«)
/// ist NICHT gebaut — dazu läuft eine unabhängige Gegenlesung.
///
/// <para>Prüfstand <c>--stellung-check</c> (kopflos, K24): je Stellung das gewählte
/// Rumpfbild und der Turmsitz. Soll 83/83 Gruppe 7, Turm flach (0,−15).
/// Nullmodelle: <c>--eingegraben-bild-alt</c> → 43/83, <c>--stellungsturm-alt</c>
/// → (0,−9). Mit <c>--shot=…</c> (Fenster) zwei Bilder: eine Stellung mit Blick ≠ 7
/// (<c>_blickN</c>) und eine mit Blick 7.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--stellung-check</c> — siehe Klassenkommentar.</summary>
    public string StellungCheck()
    {
        int m = UI.SkirmishSetup.CampaignMission;
        var sb = new System.Text.StringBuilder(
            $"stellung-check K{m} (--eingegraben-bild-alt {EingegrabenBildAlt}, --stellungsturm-alt {StellungsturmAlt})\n");
        int n = 0, gruppeGleich = 0, gruppe0 = 0, ohneBild = 0, fremd = 0, flach = 0, turmSoll = 0;
        int blick7 = 0, blick37 = 0;
        var turmFlach = new Dictionary<Vector2, int>();
        foreach (var e in _entities)
        {
            if (e.Dead || e.IsBuilding || e.IsProp || e.UnitType != 171) continue;
            if (e.Faze != 1) continue;
            n++;
            if (e.Facing == 7) blick7++;
            if (e.Facing is 3 or 7) blick37++;
            int slope = SlopeClassOf(e.Col, e.Row);
            int pose = PoseOf(e);
            var tex = GetHullTexture(171, e.Facing, pose, slope);
            var t0 = GetHullTexture(171, e.Facing, 0, slope);
            if (tex == null) ohneBild++;
            else if (tex == t0) gruppe0++;
            else if (pose > 0 && IstRichtungslos(tex, pose, slope)) gruppeGleich++;
            else fremd++;   // ein eigenes Blickbild der Gruppe — das gibt es in ROBO.CWR nicht
            var off = TurretOffset(171, e.Col, e.Row, e.Facing, e);
            if (slope == 0)
            {
                flach++;
                turmFlach[off] = turmFlach.GetValueOrDefault(off) + 1;
                // Soll @0x42A053: (0,−9)·(7+10)/10 = (0,−15)
                if (Mathf.IsEqualApprox(off.X, 0f) && Mathf.IsEqualApprox(off.Y, -15f)) turmSoll++;
            }
        }
        sb.AppendLine($"  {n} aufgebaute Stellungen (171, faze 1), davon Blick 7: {blick7}");
        sb.AppendLine($"  Rumpfbild: {gruppeGleich}x Gruppe der Karte (eingegraben), {gruppe0}x Gruppe 0 (aufgepackt), {fremd}x Blickbild ≠ 3/7 (alter Export, leer), {ohneBild}x ohne Bild" +
                      $"  [Soll {n}/{n}; Nullmodell --eingegraben-bild-alt: {blick37}/{n} (nur Blick 3/7)]");
        sb.AppendLine($"  Turm flach ({flach} Stellungen): " +
                      string.Join(", ", turmFlach.Select(kv => $"({kv.Key.X:0},{kv.Key.Y:0})x{kv.Value}")) +
                      $"  [Soll (0,-15) @0x42A053; Nullmodell --stellungsturm-alt: (0,-9)]");
        sb.AppendLine($"  {DebugSpriteInfo()}");
        bool ok = n > 0 && gruppeGleich == n && turmSoll == flach && flach > 0;
        sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }

    /// <summary>Ist <paramref name="tex"/> das richtungslose f7/f3 der Gruppe (mit oder ohne Hangblock)?</summary>
    private bool IstRichtungslos(Texture2D tex, int pose, int slope)
    {
        string dir = $"171/g{pose}";
        foreach (int f in new[] { 7, 3 })
            if (tex == LoadUnitPart("hull", dir, f)
                || (slope > 0 && tex == LoadUnitPart("hull", $"{dir}/s{slope}", f))) return true;
        return false;
    }

    /// <summary>Für den Bildlauf: die Stellung bei (46,125) (Blick 0, fiel bisher auf
    /// Gruppe 0) und die bei (72,84) (Blick 7) — sonst die erste mit Blick ≠ 7 bzw. 7.</summary>
    public List<(Vector2 pos, string name)> StellungBildZiele()
    {
        var l = new List<(Vector2, string)>();
        Entity? Suche(System.Func<Entity, bool> f)
            => _entities.FirstOrDefault(e => !e.Dead && e.UnitType == 171 && e.Faze == 1 && f(e));
        var a = Suche(e => e.Col == 46 && e.Row == 125) ?? Suche(e => e.Facing != 7);
        var b = Suche(e => e.Col == 72 && e.Row == 84) ?? Suche(e => e.Facing == 7);
        foreach (var e in new[] { a, b })
            if (e != null)
                l.Add((e.Pos, $"blick{e.Facing}_{e.Col}_{e.Row}"));
        return l;
    }

    /// <summary>Eine Zeile zur Stellung unter <paramref name="pos"/> für den Bildlauf.</summary>
    public string StellungBildZeile(Vector2 pos)
    {
        var e = _entities.FirstOrDefault(x => !x.Dead && x.UnitType == 171 && x.Pos == pos);
        if (e == null) return "keine Stellung";
        int slope = SlopeClassOf(e.Col, e.Row), pose = PoseOf(e);
        var tx = GetHullTexture(171, e.Facing, pose, slope);
        bool g = tx != null && pose > 0 && IstRichtungslos(tx, pose, slope);
        return $"({e.Col},{e.Row}) Blick {e.Facing} Gruppe {pose} faze {e.Faze} Hang {slope}: " +
               $"Rumpf {(g ? $"g{pose} (richtungslos)" : tx == GetHullTexture(171, e.Facing, 0, slope) ? "g0 (aufgepackt)" : "Blickbild der Gruppe (alter Export, leer)")}, Turm {TurretOffset(171, e.Col, e.Row, e.Facing, e)}";
    }
}
