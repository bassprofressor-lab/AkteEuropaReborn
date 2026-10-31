namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Text;
using Godot;

/// <summary>
/// ⭐⭐ <b>DAS MUENDUNGSFEUER AUS SHOOT.CWT</b> — bug-689, 30.10.2026
/// (berichte/pruefung-rest-b-opus.md §7, Bau berichte/bau-muendungsfeuer.md).
///
/// <para><b>Bis heute</b> legte <c>Fire</c> einen WELTFESTEN Effekt an
/// Turmdrehpunkt + Turmrichtung × 14 px (<c>MuzzleReach</c>, UNSERE SETZUNG):
/// ein Punkt je Schuss, der stehen blieb, wenn Turm oder Fahrzeug weiterzogen,
/// und der immer ÜBER allen Einheiten lag.</para>
///
/// <para><b>Im Original</b> ist das Feuer ein ZUSTAND DER EINHEIT, selbst gelesen in
/// C und F (Fundstellen C / F):</para>
/// <list type="number">
/// <item><b>Der Zähler +0x42</b> (C <c>0x6E270A</c>, F <c>0x6E176A</c>). Der Schuss
/// setzt ihn auf 1: Kampftakt <c>0x40C5AC</c> → <c>0x40231F</c> → <c>0x436420</c>
/// <c>mov byte [..+0x6E270A],1</c> (F <c>0x435580</c>, Byte für Byte gleich). Der
/// einzige andere Schreiber ist die Flak-Station <c>0x428600</c>: <c>+0x42 :=
/// Rohr + 1</c> (<c>0x42878E/0x428791</c>).</item>
/// <item><b>Der Zeichner</b> <c>0x429900</c> (F <c>0x428AF0</c>) liest ihn
/// (<c>0x429B54</c> / F <c>0x428D44</c>): bei 0 nichts; sonst Bild <c>Zähler − 1</c>
/// der Folge aus <c>0x4FA4F8</c>, danach <c>Zähler++</c>, und ist der Zähler größer
/// als die Bildzahl der Folge (Byte <c>0x7A4048 + 4·Folge</c>), wieder 0
/// (<c>0x429BC2..0x429BD4</c>). Bei Folge 0xFFFF sofort 0 (<c>0x429B87</c>).</item>
/// <item><b>Der Ort</b> ist der SHOOT.CWT-Satz <c>(Waffe·48 + Bild)·4 + Punkt</c>,
/// Waffe = ZBRAN − 1 (<c>0x429B8E</c>), Bild = Turmrichtung +0x03 + Blockversatz
/// <c>0x4FA4D8</c> (<c>0x429BA8..0x429BBE</c>, dasselbe wie beim Turmbild
/// <c>0x42A252..0x42A260</c>, aber OHNE die Werfergruppe +0x30). Gezeichnet am
/// ZEICHENPUNKT DES TURMES + <c>(x − 0x19, y − 0x44)</c>.</item>
/// <item><b>Die Fahne</b>: 0 → VOR dem Turm gezeichnet (<c>0x42A15F..0x42A1EA</c>,
/// <c>test al,al / jne</c>), liegt also UNTER ihm; 1 → NACH dem Turm
/// (<c>0x42A654..0x42A6DF</c>, <c>cmp al,1</c>); 2 → nichts. Alle vier Punkte eines
/// Bildes werden gezeichnet — daher zwei Feuer beim Doppelrohr.</item>
/// <item><b>Die Flak</b> (ZBRAN 18, <c>0x42A0EC</c>/<c>0x42A5E0</c>): NUR Punkt
/// <c>Zähler − 1</c> (also das Rohr, das die Station gewählt hat) und immer Bild 0
/// der Folge 45.</item>
/// </list>
///
/// <para><b>Die Lage in unseren Koordinaten:</b> der Turm-Blitpunkt des Originals
/// ist bei uns die linke obere Ecke der Turmleinwand + <c>CanvasXPad</c> (4, siehe
/// <see cref="Import.CwrFile"/>); eine Effektleinwand beginnt dagegen genau am
/// Blitpunkt (InterfaceExporter.Canvas: <c>x</c> ab 0, <c>y</c> ab YOffset). Also
/// <c>Leinwand(mzf) = Leinwand(Turm) + (4 + x − 25, y − 68)</c>. Gegenprobe an den
/// Bildern: Turm 21 Bild 0, Rohr unten bei (23, 54) der Turmleinwand, die
/// sichtbare Mitte des Feuers landet bei (22,5; 52).</para>
///
/// <para>⚠ <b>UNSERE SETZUNGEN</b>:</para>
/// <list type="bullet">
/// <item>Wie oft der Zeichner des Originals je Sekunde läuft, ist nicht lesbar
/// (Flip auf den Rücklauf, siehe <c>BuildingAnimFps</c>). Der Zähler schreitet
/// darum je <see cref="MuendungsSchritt"/> = 0,035 s fort — derselbe Takt, den der
/// weltfeste Effekt seit bug-454 hatte.</item>
/// <item>Gebaut nur am gewöhnlichen Rumpf-und-Turm-Zweig (Fahrzeuge, Gattung ≤ 2,
/// keine Schiffe): Schiffe, Bertha und Ersatzbilder behalten den weltfesten Effekt —
/// ihre Zeichner sind nicht gelesen.</item>
/// <item>Die Sonderklasse <c>+0x0B == 0x11</c> nimmt im Original Block 0
/// (<c>0x429AF4</c>); wir nehmen immer den Block der Hangklasse wie beim Turmbild.</item>
/// </list>
///
/// <para>⚠ Reiner Zeichnerzustand: Zähler und Uhr stehen an der Einheit, gehen aber
/// in keine Prüfsumme und keinen Spielstand, und nichts hier würfelt. Die
/// NET07-Summe bleibt.</para>
///
/// <para>Die Datei liegt nicht in der EXE, sondern lose neben ihr (F 28 800 B, C hat
/// keine). Der Import legt sie nach <c>DATA/SHOOT.CWT</c>
/// (<see cref="Import.ContentBuilder"/>, Teil »oberflaeche« Stand 2). Fehlt sie,
/// bleibt der alte weltfeste Effekt — gemeldet, nicht verschwiegen.</para>
///
/// <para>Gegenschalter <c>--muendungsfeuer-alt</c>, Prüfstand
/// <c>--muendungsfeuer-check</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--muendungsfeuer-alt</c> — der Stand bis bug-689: ein weltfester
    /// Effekt je Schuss an Turmdrehpunkt + 14 px.</summary>
    public static bool MuendungsfeuerAlt;

    /// <summary>Wo der Import die Tafel ablegt.</summary>
    public const string ShootTafelDatei = "DATA/SHOOT.CWT";

    /// <summary>9 600 Sätze zu 3 Byte — der Lader 0x4544F0 liest 0x7080 Byte.</summary>
    public const int ShootTafelLaenge = 0x7080;

    /// <summary>⚠ UNSERE SETZUNG: ein Zählerschritt je 0,035 s (siehe Kopf).</summary>
    private const float MuendungsSchritt = 0.035f;

    private static byte[]? _shootTafel;
    private static bool _shootVersucht;
    /// <summary>Gezeichnete Feuerpunkte (für den Prüfstand und die Ablaufzeile).</summary>
    public int MzfGezeichnet;

    /// <summary>Die Tafel, einmal gelesen. <c>null</c>, wenn sie fehlt oder die Länge
    /// nicht stimmt.</summary>
    private static byte[]? ShootTafel()
    {
        if (_shootVersucht) return _shootTafel;
        _shootVersucht = true;
        string p = Core.Content.Path(ShootTafelDatei);
        if (!FileAccess.FileExists(p))
        {
            GD.Print($"muendungsfeuer: {ShootTafelDatei} fehlt — weltfester Effekt wie bis bug-689 (bitte neu von CD einlesen)");
            return null;
        }
        var b = FileAccess.GetFileAsBytes(p);
        if (b == null || b.Length != ShootTafelLaenge)
        {
            GD.Print($"muendungsfeuer: {p} hat {b?.Length ?? 0} Byte statt {ShootTafelLaenge} — weltfester Effekt");
            return null;
        }
        _shootTafel = b;
        return b;
    }

    /// <summary>Hängt das Feuer dieser Einheit am Turm (neu) oder als Effekt in der
    /// Welt (alt bzw. ungelesene Zeichner)?</summary>
    private bool MuendungsfeuerAmTurm(Entity e)
        => !MuendungsfeuerAlt && e.Infantry < 0 && !e.IsBuilding
           && e.Move != Simulation.NavGrid.MoveClass.Ship && e.GameUnitType <= 2
           && ShootTafel() != null;

    /// <summary>Aus <c>Fire</c>: der Schuss löst das Feuer aus — neu der Zähler,
    /// alt der weltfeste Effekt (wortgleich mit dem Stand vor bug-689).</summary>
    private void MuendungsfeuerAusloesen(Entity shooter, Vector2 dir, int folge)
    {
        var mzfRichtung = MuendungsRichtung(shooter, dir);   // zählt für --fahrziel-check
        if (MuendungsfeuerAmTurm(shooter)) { MuendungsfeuerStart(shooter, 1); return; }
        _effects.Add(new Effect
        {
            Pos = ShotOrigin(shooter) + mzfRichtung * MuzzleReach,
            Kind = $"mzf{folge}", FrameTime = 0.035f,
        });
    }

    /// <summary>Den Zähler +0x42 setzen: 1 beim Schuss (0x436420), Rohr + 1 an der
    /// Flak-Station (0x428791).</summary>
    private void MuendungsfeuerStart(Entity e, int zaehler)
    {
        if (!MuendungsfeuerAmTurm(e)) return;
        e.Muendungsbild = zaehler;
        e.MuendungsUhr = 0f;
    }

    /// <summary>Die Bildzahl einer Folge — die ausgegebenen Bilder, das sind die aus
    /// ANIM.CWA (45: 1, 46: 5, 47: 7, 48: 7).</summary>
    private int MzfBildzahl(int folge) => folge < 0 ? 0 : EffectFrames($"mzf{folge}").Count;

    /// <summary>Ein Zeichendurchlauf des Originals: Zähler++ und bei Überlauf 0
    /// (0x429BC2..0x429BD4), bei Folge 0xFFFF sofort 0 (0x429B87).</summary>
    private void MuendungsZaehlerWeiter(Entity e)
    {
        int folge = MuendungsFolge(e.Weapon);
        if (folge < 0) { e.Muendungsbild = 0; return; }
        e.Muendungsbild++;
        if (e.Muendungsbild > MzfBildzahl(folge)) e.Muendungsbild = 0;
    }

    /// <summary>Je Spieltakt: die Uhr jeder feuernden Einheit weiter. Kein Würfel,
    /// keine Simulationsgröße.</summary>
    private void MuendungsfeuerTakt(float dt)
    {
        foreach (var e in _entities)
        {
            if (e.Muendungsbild <= 0) continue;
            e.MuendungsUhr += dt;
            while (e.Muendungsbild > 0 && e.MuendungsUhr >= MuendungsSchritt)
            {
                e.MuendungsUhr -= MuendungsSchritt;
                MuendungsZaehlerWeiter(e);
            }
        }
    }

    /// <summary>Ein Feuerpunkt: linke obere Ecke der Effektleinwand, Fahne, Bild der
    /// Folge, Punkt im Satz.</summary>
    private readonly record struct MzfPunkt(Vector2 LinksOben, int Fahne, int Bild, int Punkt, int Folge);

    /// <summary>
    /// Die Punkte, die der Zeichner für diesen Zählerstand setzt — eine Rechnung für
    /// Zeichnen UND Prüfstand. <paramref name="turmLo"/> ist die linke obere Ecke der
    /// Turmleinwand, genau dort, wo <c>DrawUnitBody</c> den Turm zeichnet.
    /// </summary>
    private List<MzfPunkt> MuendungsPunkte(Entity e, Vector2 turmLo, int aim, int slope, int zaehler)
    {
        var l = new List<MzfPunkt>(4);
        var t = ShootTafel();
        if (t == null || zaehler <= 0) return l;
        int folge = MuendungsFolge(e.Weapon);
        if (folge < 0) return l;
        int waffe = e.Weapon - 20 - 1;                     // ZBRAN − 1 (0x429B8E)
        if (waffe < 0 || waffe >= ShootTafelLaenge / (48 * 4 * 3)) return l;
        int bild = ((aim % TurmBilder) + TurmBilder) % TurmBilder + 8 * SlopeBlock(slope);
        int satz0 = (waffe * 48 + bild) * 4;
        var ecke = turmLo + new Vector2(Import.CwrFile.CanvasXPad - 0x19, -0x44);
        if (waffe == 17)
        {
            // ZBRAN 18, die Flak: nur der Punkt des gewählten Rohres, Bild 0 (0x42A0EC ff.)
            int k = zaehler - 1;
            if (k > 3) return l;
            int i = (satz0 + k) * 3;
            if (t[i + 2] <= 1) l.Add(new MzfPunkt(ecke + new Vector2(t[i], t[i + 1]), t[i + 2], 0, k, folge));
            return l;
        }
        for (int k = 0; k < 4; k++)
        {
            int i = (satz0 + k) * 3;
            if (t[i + 2] > 1) continue;                    // Fahne 2: kein Punkt
            l.Add(new MzfPunkt(ecke + new Vector2(t[i], t[i + 1]), t[i + 2], zaehler - 1, k, folge));
        }
        return l;
    }

    /// <summary>Aus <c>DrawUnitBody</c>: die Punkte einer Fahne zeichnen — Fahne 0 vor
    /// dem Turm, Fahne 1 nach ihm.</summary>
    private void MuendungsfeuerZeichnen(Entity e, Vector2 turmLo, int aim, int slope, int fahne)
    {
        if (e.Muendungsbild <= 0 || !MuendungsfeuerAmTurm(e)) return;
        foreach (var p in MuendungsPunkte(e, turmLo, aim, slope, e.Muendungsbild))
        {
            if (p.Fahne != fahne) continue;
            var frames = EffectFrames($"mzf{p.Folge}");
            if (p.Bild < 0 || p.Bild >= frames.Count) continue;
            DrawTexture(frames[p.Bild], p.LinksOben);
            MzfGezeichnet++;
        }
    }

    // ------------------------------------------------------------------------
    //  --muendungsfeuer-check
    // ------------------------------------------------------------------------

    private readonly Dictionary<Texture2D, Vector2> _mzfSichtMitte = new();

    /// <summary>Die Mitte der undurchsichtigen Fläche eines Bildes, in seiner Leinwand.</summary>
    private Vector2 SichtMitte(Texture2D tex)
    {
        if (_mzfSichtMitte.TryGetValue(tex, out var m)) return m;
        var img = tex.GetImage();
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        if (img != null)
            for (int y = 0; y < img.GetHeight(); y++)
                for (int x = 0; x < img.GetWidth(); x++)
                    if (img.GetPixel(x, y).A > 0.1f)
                    {
                        if (x < x0) x0 = x; if (y < y0) y0 = y;
                        if (x > x1) x1 = x; if (y > y1) y1 = y;
                    }
        m = x1 < 0 ? tex.GetSize() / 2f : new Vector2((x0 + x1 + 1) / 2f, (y0 + y1 + 1) / 2f);
        _mzfSichtMitte[tex] = m;
        return m;
    }

    /// <summary>Wo das Feuer dieser Einheit JETZT zu sehen ist — Mitte und Schicht
    /// (0 unter dem Turm, 1 über dem Turm, 2 Effektdurchgang über allen Einheiten).
    /// Neu aus dem Zähler, alt aus den weltfesten Effekten ab <paramref name="abEffekt"/>.</summary>
    private List<(Vector2 Mitte, int Schicht)> MzfOrte(Entity e, int abEffekt)
    {
        var l = new List<(Vector2, int)>();
        if (MuendungsfeuerAmTurm(e))
        {
            if (e.Muendungsbild <= 0) return l;
            int aim = TurmRichtung(e);
            int slope = SlopeClassOf(e);
            var turmLo = PictureAnchor(e) - EinheitenAnker(e) + new Vector2(0, -e.Zittern)
                       + TurretOffset(e.UnitType, e.Col, e.Row, e.Facing, e);
            foreach (var p in MuendungsPunkte(e, turmLo, aim, slope, e.Muendungsbild))
            {
                var frames = EffectFrames($"mzf{p.Folge}");
                if (p.Bild >= frames.Count) continue;
                l.Add((p.LinksOben + SichtMitte(frames[p.Bild]), p.Fahne));
            }
            return l;
        }
        for (int i = abEffekt; i < _effects.Count; i++)
        {
            var fx = _effects[i];
            if (!fx.Kind.StartsWith("mzf")) continue;
            var frames = EffectFrames(fx.Kind);
            if (frames.Count == 0) continue;
            int f = Mathf.Min((int)(fx.Time / fx.FrameTime), frames.Count - 1);
            l.Add((fx.Pos - _fxAnchor[fx.Kind] + SichtMitte(frames[f]), 2));
        }
        return l;
    }

    /// <summary>
    /// <c>--muendungsfeuer-check</c>, kopflos, auf jeder Karte mit einem Fahrzeug, das
    /// einen eigenen Turm hat. Gemessen wird über DIESELBEN Wege wie im Spiel
    /// (<see cref="MuendungsfeuerAusloesen"/>, Flak-Start, <see cref="MuendungsfeuerTakt"/>):
    /// <list type="number">
    /// <item><b>Tafel</b>: 9 600 Sätze, Fahnen 413 / 756 / 8 431.</item>
    /// <item><b>Lage relativ zum Turm</b> für alle acht Turmbilder von Turm 21, 25, 26,
    /// 37: Feuermitte − Turmleinwand gegen das Soll aus den ROHEN Bytes
    /// <c>(4 + x − 25, y − 68) + Sichtmitte</c>. Soll 0 px.</item>
    /// <item><b>Doppelrohr</b>: Turm 25 zeigt je Bild 2 Feuer, Soll 8/8.</item>
    /// <item><b>Flak</b>: Turm 38, Rohr 0..3 → genau Punkt 0..3, vier verschiedene Orte.</item>
    /// <item><b>Mitfahren</b>: nach dem Schuss 40/20 px versetzt → das Feuer um genau
    /// dasselbe; Turm auf Bild 2 gedreht → das Feuer am Soll von Bild 2.</item>
    /// <item><b>Fahnen</b>: Turm 21, Bilder 3/4/5 unter dem Turm, die übrigen über ihm.</item>
    /// <item><b>Ablauf</b>: Turm 21 (Folge 46, 5 Bilder) zeigt 0,1,2,3,4 und erlischt.</item>
    /// </list>
    /// Nullmodell <c>--muendungsfeuer-alt</c>: weltfester Effekt, 1 Punkt, steht still,
    /// über allem — die Punkte 2..6 fallen durch.
    /// <para>Zusätzlich (ohne Urteil): Abstand der Feuermitte zum nächsten
    /// undurchsichtigen Turmpunkt, neu gegen alt.</para>
    /// </summary>
    public string MuendungsfeuerCheck()
    {
        var sb = new StringBuilder("muendungsfeuer-check (bug-689)\n");
        if (MuendungsfeuerAlt) sb.AppendLine("  ⚠ NULLMODELL --muendungsfeuer-alt: hier MUSS es durchfallen");
        var t = ShootTafel();
        if (t == null) return sb.Append($"muendungsfeuer-check: KEIN URTEIL — {ShootTafelDatei} fehlt, bitte neu einlesen").ToString();

        // (1) die Tafel
        int f0 = 0, f1 = 0, f2 = 0, fx = 0;
        for (int i = 0; i < t.Length / 3; i++)
            switch (t[i * 3 + 2]) { case 0: f0++; break; case 1: f1++; break; case 2: f2++; break; default: fx++; break; }
        bool okTafel = t.Length / 3 == 9600 && f0 == 413 && f1 == 756 && f2 == 8431 && fx == 0;
        sb.AppendLine($"  Tafel: {t.Length / 3} Saetze, Fahne 0 {f0}x, 1 {f1}x, 2 {f2}x, sonst {fx}x (Soll 9600: 413/756/8431) {(okTafel ? "ok" : "FALSCH")}");

        // das Messfahrzeug
        int si = -1;
        for (int i = 0; i < _entities.Count && si < 0; i++)
        {
            var c = _entities[i];
            if (c.Dead || c.IsBuilding || c.IsProp || c.Infantry >= 0 || c.GameUnitType > 2) continue;
            if (c.Move != Simulation.NavGrid.MoveClass.Vehicle || !TurmEigen(c) || IstBertha(c)) continue;
            if (GetHullTexture(c.UnitType, c.Facing, PoseOf(c), SlopeClassOf(c)) == null) continue;
            si = i;
        }
        if (si < 0) return sb.Append("muendungsfeuer-check: KEIN URTEIL — kein Fahrzeug mit eigenem Turm auf dieser Karte").ToString();
        var e = _entities[si];
        int altWaffe = e.Weapon, altAim = e.AimFacing, altBild = e.Muendungsbild, altZittern = e.Zittern;
        var altPos = e.Pos;
        float altUhr = e.MuendungsUhr;
        int slope = SlopeClassOf(e);
        e.Zittern = 0;
        sb.AppendLine($"  Messfahrzeug Platz {e.Slot} Typ {e.UnitType} bei ({e.Col},{e.Row}), Hangklasse {slope} (Block {SlopeBlock(slope)})");

        Vector2 TurmLo() => PictureAnchor(e) - EinheitenAnker(e) + new Vector2(0, -e.Zittern)
                            + TurretOffset(e.UnitType, e.Col, e.Row, e.Facing, e);
        // Soll aus den ROHEN Bytes — eigene Rechnung, nicht MuendungsPunkte
        List<(Vector2 Mitte, int Fahne)> Soll(int teil, int aim, int zaehler)
        {
            var l = new List<(Vector2, int)>();
            int w = teil - 21, bild = aim + 8 * SlopeBlock(slope);
            int folge = MuendungsFolge(teil);
            var frames = EffectFrames($"mzf{folge}");
            for (int k = 0; k < 4; k++)
            {
                if (w == 17 && k != zaehler - 1) continue;
                int i = ((w * 48 + bild) * 4 + k) * 3;
                if (t[i + 2] > 1) continue;
                int b = w == 17 ? 0 : zaehler - 1;
                var mitte = SichtMitte(frames[b]);
                l.Add((TurmLo() + new Vector2(4 + t[i] - 25, t[i + 1] - 68) + mitte, t[i + 2]));
            }
            return l;
        }
        // ein Schuss ueber den Spielweg
        int Schuss(int aim)
        {
            e.AimFacing = aim;
            e.Muendungsbild = 0; e.MuendungsUhr = 0;
            int ab = _effects.Count;
            int folge = MuendungsFolge(e.Weapon);
            var dir = new Vector2(Mathf.Cos(Mathf.DegToRad(90f + 45f * aim)), Mathf.Sin(Mathf.DegToRad(90f + 45f * aim)));
            if (folge >= 0) MuendungsfeuerAusloesen(e, dir, folge);
            return ab;
        }
        float NaechsterTurmpunkt(Vector2 p, int aim)
        {
            var tex = GetTurretTexture(e.Weapon, aim, slope, TurmGruppe(e));
            var img = tex?.GetImage();
            if (img == null) return -1;
            var lo = TurmLo();
            float best = float.MaxValue;
            for (int y = 0; y < img.GetHeight(); y++)
                for (int x = 0; x < img.GetWidth(); x++)
                    if (img.GetPixel(x, y).A > 0.5f)
                        best = Mathf.Min(best, p.DistanceTo(lo + new Vector2(x + 0.5f, y + 0.5f)));
            return best;
        }

        // (2) Lage relativ zum Turm, (3) Doppelrohr, (6) Fahnen
        bool okLage = true, okDoppel = true, okFahne = true;
        int lageN = 0, doppelBilder = 0, nordUnter = 0, nordN = 0, suedUeber = 0, suedN = 0;
        float lageMax = 0, lageSumme = 0, randNeu = 0, randMax = 0; int randN = 0, lageFehlt = 0;
        foreach (int teil in new[] { 21, 25, 26, 37 })
        {
            e.Weapon = teil;
            var zeile = new StringBuilder($"  Turm {teil} (ZBRAN {teil - 20}, Folge {MuendungsFolge(teil)}):");
            for (int aim = 0; aim < 8; aim++)
            {
                int ab = Schuss(aim);
                var ist = MzfOrte(e, ab);
                var soll = Soll(teil, aim, 1);
                if (ist.Count != soll.Count) okLage = false;
                float abw = 0;
                for (int k = 0; k < soll.Count; k++)
                {
                    float d = k < ist.Count ? ist[k].Mitte.DistanceTo(soll[k].Mitte) : 999f;
                    if (k >= ist.Count) { lageFehlt++; okLage = false; continue; }
                    abw = Mathf.Max(abw, d); lageSumme += d; lageN++;
                    if (d > 0.01f) okLage = false;
                }
                lageMax = Mathf.Max(lageMax, abw);
                foreach (var o in ist)
                {
                    float r = NaechsterTurmpunkt(o.Mitte, aim);
                    if (r >= 0) { randNeu += r; randMax = Mathf.Max(randMax, r); randN++; }
                }
                if (teil == 25 && ist.Count == 2) doppelBilder++;
                if (teil == 21)
                {
                    bool nord = aim is 3 or 4 or 5;
                    foreach (var o in ist)
                    {
                        if (nord) { nordN++; if (o.Schicht == 0) nordUnter++; }
                        else { suedN++; if (o.Schicht == 1) suedUeber++; }
                    }
                    if (ist.Count == 0) { if (nord) nordN++; else suedN++; }
                }
                zeile.Append($" B{aim}:{ist.Count}P/{abw:0.#}px");
                // aufraeumen: der alte Weg legt Effekte an
                if (_effects.Count > ab) _effects.RemoveRange(ab, _effects.Count - ab);
            }
            sb.AppendLine(zeile.ToString());
        }
        okDoppel = doppelBilder == 8;
        okFahne = nordN > 0 && nordUnter == nordN && suedN > 0 && suedUeber == suedN;
        sb.AppendLine($"  (2) Lage gegen die rohen Bytes: {lageN} Punkte gezeichnet, {lageFehlt} fehlen, Abweichung mittel {(lageN > 0 ? lageSumme / lageN : 0):0.##} px, "
                      + $"hoechstens {lageMax:0.##} px (Soll 0) {(okLage ? "ok" : "FALSCH")}");
        sb.AppendLine($"      ohne Urteil: Abstand Feuermitte -> naechster Turmpunkt mittel {(randN > 0 ? randNeu / randN : 0):0.#} px, hoechstens {randMax:0.#} px ({randN} Punkte)");
        sb.AppendLine($"  (3) Doppelrohr Turm 25: {doppelBilder}/8 Bilder mit 2 Feuern {(okDoppel ? "ok" : "FALSCH")}");
        sb.AppendLine($"  (6) Fahnen Turm 21: Bilder 3/4/5 unter dem Turm {nordUnter}/{nordN}, uebrige ueber dem Turm {suedUeber}/{suedN} {(okFahne ? "ok" : "FALSCH")}");

        // (4) Flak: Rohr r -> Punkt r
        e.Weapon = FlakAufsatz;
        var flakOrte = new List<Vector2>();
        bool okFlak = true;
        for (int r = 0; r < 4; r++)
        {
            e.AimFacing = 0; e.Muendungsbild = 0;
            int ab = _effects.Count;
            MuendungsfeuerStart(e, r + 1);                 // derselbe Ruf wie in der Station
            var ist = MzfOrte(e, ab);
            var soll = Soll(FlakAufsatz, 0, r + 1);
            if (ist.Count != 1 || soll.Count != 1 || ist[0].Mitte.DistanceTo(soll[0].Mitte) > 0.01f) okFlak = false;
            if (ist.Count == 1) flakOrte.Add(ist[0].Mitte);
        }
        int verschieden = 0;
        for (int a = 0; a < flakOrte.Count; a++)
        {
            bool neu = true;
            for (int b = 0; b < a; b++) if (flakOrte[a].DistanceTo(flakOrte[b]) < 0.5f) neu = false;
            if (neu) verschieden++;
        }
        okFlak = okFlak && verschieden == 4;
        sb.AppendLine($"  (4) Flak Turm {FlakAufsatz}: Rohr 0..3 -> {flakOrte.Count} Feuer an {verschieden} verschiedenen Orten (Soll 4/4) {(okFlak ? "ok" : "FALSCH")}");

        // (5) Mitfahren
        e.Weapon = 21;
        int ab5 = Schuss(0);
        var vor = MzfOrte(e, ab5);
        var versatz = new Vector2(40, 20);
        e.Pos = altPos + versatz;
        var nach = MzfOrte(e, ab5);
        float fahrt = vor.Count == 1 && nach.Count == 1 ? (nach[0].Mitte - vor[0].Mitte).DistanceTo(versatz) : 999f;
        e.AimFacing = 2;
        var gedreht = MzfOrte(e, ab5);
        var sollGedreht = Soll(21, 2, 1);
        float dreh = gedreht.Count == 1 && sollGedreht.Count == 1 ? gedreht[0].Mitte.DistanceTo(sollGedreht[0].Mitte) : 999f;
        e.Pos = altPos;
        if (_effects.Count > ab5) _effects.RemoveRange(ab5, _effects.Count - ab5);
        bool okFahrt = fahrt < 0.01f && dreh < 0.01f;
        sb.AppendLine($"  (5) Mitfahren: nach Versatz {versatz} Abweichung {fahrt:0.##} px, nach Drehung auf Bild 2 Abweichung {dreh:0.##} px (Soll 0/0) {(okFahrt ? "ok" : "FALSCH")}");

        // (7) Ablauf
        int ab7 = Schuss(0);
        var gezeigt = new List<int>();
        for (int n = 0; n < 20 && e.Muendungsbild > 0; n++)
        {
            gezeigt.Add(e.Muendungsbild - 1);
            MuendungsfeuerTakt(MuendungsSchritt);
        }
        int bilder46 = MzfBildzahl(46);
        bool okAblauf = MuendungsfeuerAmTurm(e) && gezeigt.Count == bilder46 && e.Muendungsbild == 0;
        for (int k = 0; k < gezeigt.Count; k++) if (gezeigt[k] != k) okAblauf = false;
        sb.AppendLine($"  (7) Ablauf Turm 21: Bilder [{string.Join(",", gezeigt)}] dann aus (Soll 0..{bilder46 - 1}) {(okAblauf ? "ok" : "FALSCH")}");
        if (_effects.Count > ab7) _effects.RemoveRange(ab7, _effects.Count - ab7);

        e.Weapon = altWaffe; e.AimFacing = altAim; e.Muendungsbild = altBild; e.MuendungsUhr = altUhr;
        e.Zittern = altZittern; e.Pos = altPos;

        bool ok = okTafel && okLage && okDoppel && okFlak && okFahrt && okFahne && okAblauf;
        sb.Append(ok ? "muendungsfeuer-check: BESTANDEN" : "muendungsfeuer-check: DURCHGEFALLEN");
        return sb.ToString();
    }
}
