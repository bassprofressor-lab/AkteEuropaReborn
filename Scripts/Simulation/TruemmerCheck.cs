namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// ⭐⭐ <b>PAKET 4 GRAFIK — DIE EXPLOSION EINES FAHRZEUGS WIE IM ORIGINAL</b>
/// (04.10.2026, bug-426). KayelGee: »Explosionen von Einheiten sehen im Original
/// besser aus. Trümmer fliegen im Original in einer Parabel aus der Explosion
/// heraus.« Lesung <c>berichte/nebel-explosionen-fable.md</c> (Fable, EXE C).
///
/// <para>Hier: was zur Todesroutine <c>0x40B3C0</c> und zum Wrack noch fehlte
/// — der <b>Todesklang</b>, der <b>stehende Rumpf</b>, das <b>alternde
/// Wrack</b> — und der Pruefstand <c>--truemmer-check</c>. Bogen, Uhr und
/// Landung der Teile stehen in Simulation/Truemmer.cs.</para>
/// </summary>
public partial class MapEntityLayer
{
    // ---- Todesklang (@0x40B3E3, @0x40B500..0x40B525) -----------------------

    /// <summary><c>--todesklang-alt</c> — Stand feec479: ein Fahrzeug stirbt
    /// stumm (nur der Einschlagklang des Geschosses), keine Veteranenstimme.</summary>
    public static bool TodesklangAlt;

    /// <summary>Gespielte Todesklaenge 400..403 und Veteranenstimmen 143.</summary>
    public int TodesklangGespielt, VeteranenstimmeGespielt;
    public int TodesklangLetzter = -1;

    /// <summary>Gesetzte Explosionsbilder des Fahrzeugtods (Pruefstand).</summary>
    public int TodSprengbilder;

    /// <summary>
    /// Die zwei Klaenge der Todesroutine, in ihrer Reihenfolge:
    /// <code>
    ///   0x40B3E3  Rang (+0x28) &gt; 50 -> Klang 0x8F (143) OHNE Ort  ; Veteranenstimme
    ///   0x40B500  Gattung (+0x0A) != 1 -> 0x4047E0(400 + rand&amp;3, 1, einheit, 0)  ; AM ORT
    ///             Gattung == 1 -> 0x40B2E0 (Fussvolk, bei uns InfantryDiesPick)
    /// </code>
    /// <para>Der Fahrzeugklang kommt ZUSAETZLICH zum Einschlagklang des
    /// Geschosses (410+400, Audio.GameSounds.Explosion). ⚠ Die Stimme 143 gilt im
    /// Original fuer JEDE Einheit mit Rang &gt; 50, auch fremde — so gelesen,
    /// so gebaut.</para>
    /// </summary>
    private void TodesKlang(Entity v)
    {
        if (TodesklangAlt) return;
        if (v.Rating28 > 50)
        {
            Audio.GameSounds.Play(143);
            VeteranenstimmeGespielt++;
        }
        if (v.Infantry >= 0) return;                     // Gattung 1: eigener Weg
        int k = 400 + (Simulation.Determinism.Roll(4));  // rand & 3
        Audio.GameSounds.PlayAt(k, v.Col, v.Row);
        TodesklangGespielt++;
        TodesklangLetzter = k;
    }

    // ---- der stehende Rumpf (@0x406EE6..0x406F63) ---------------------------

    /// <summary><c>--sterbend-alt</c> — Stand feec479: der Rumpf ist im
    /// Todestakt weg, das Wrack liegt sofort.</summary>
    public static bool SterbendAlt;

    /// <summary>
    /// <b>Sechs Takte unter der Wolke.</b> Die Todesroutine setzt UKOL := 100,
    /// AKCE := 0 (@0x40B5A0); <c>move units</c> zaehlt AKCE je Takt hoch und
    /// laesst die Einheit stehen, solange <c>&lt; 6</c> (@0x406EE6..0x406EF0),
    /// dann <c>likvid typ</c> (@0x406F1B) -> Wrack <c>0x4A97C0(…, rand&amp;15)</c>
    /// (@0x406F3D..0x406F63).
    /// <para>⚠ NUR DAS BILD: bei uns ist die Einheit schon tot (nicht waehlbar,
    /// kein Ziel, Zelle frei). Im Original haelt sie ihre Zelle noch 6 Takte
    /// (0xFFFE erst @0x406F6B) — das ist nicht nachgebaut.</para>
    /// </summary>
    private void SterbendBeginnen(Entity v)
    {
        v.Sterbend = true;
        _sterbend.Add((v, 0f));
    }

    public const int SterbendTakte = 6;

    private readonly List<(Entity E, float Zeit)> _sterbend = new();

    private void SterbendTakt(float dt)
    {
        for (int i = _sterbend.Count - 1; i >= 0; i--)
        {
            var (e, z) = _sterbend[i];
            z += dt;
            if (z * EffektTakteJeSekunde >= SterbendTakte - 1e-3f)   // AKCE 0..5 steht, bei 6 likvid
            {
                e.Sterbend = false;
                _sterbend.RemoveAt(i);
                WrackAnlegen(e);
                continue;
            }
            _sterbend[i] = (e, z);
        }
    }

    // ---- das Wrack sec41 (@0x4A97C0, @0x4A9860, Zeichner @0x42D0AD) --------

    /// <summary><c>--wrack-alt</c> — Stand feec479: ANIM 0, Bild nach Zellage,
    /// kein Altern, bleibt fuer immer.</summary>
    public static bool WrackAlt;

    /// <summary>
    /// <b>Das Wrack wie gelesen:</b> Variante <c>rand&amp;15</c> (@0x406F47) ist
    /// die ANIM-Folge 0..15 (<c>word[0x7A404A + 4·folge]</c> @0x42D0E2) — in
    /// ANIM.CWA haben die Folgen 0..15 je VIER Bilder (selbst nachgezaehlt).
    /// Alter +1 alle 10 Takte (@0x4A9860), Bild = erstes + Alter/90
    /// (<c>div dl, 0x5A</c> @0x42D0DA -> 3 Verfallsstufen), davor die
    /// Bodenmarke = Bild +3 (@0x42D0F0), Ueberlauf bei 255 -> frei (2550 Takte).
    /// <para>⚠ Fehlen die Folgen <c>wrack0..15</c> (Import aelter als dieser
    /// Bau), faellt es auf das alte Wrack zurueck — Abhilfe
    /// <c>--reexport-effects</c> oder Neuimport.</para></summary>
    private void WrackAnlegen(Entity v)
    {
        string k = "wrack" + Simulation.Determinism.Roll(16);
        if (WrackAlt || EffectFrames(k).Count < 4)
        {
            // the rubble variant is picked from where it fell, so two wrecks side
            // by side do not look stamped from the same mould — see DrawWreck
            _effects.Add(new Effect { Pos = v.Pos, Kind = "wreck",
                                      FrameTime = 0.25f, Hold = true,
                                      Variant = v.Col * 3 + v.Row });
            return;
        }
        _effects.Add(new Effect { Pos = v.Pos, Kind = k, FrameTime = 1f, Hold = true });
        WrackGelegt++;
    }

    public int WrackGelegt;

    public const int WrackAlterTakte = 10, WrackStufeAlter = 90, WrackLebenTakte = 2550;

    /// <summary>Verfallsstufe 0..2 nach dem Alter (Takte seit dem Anlegen).</summary>
    private static int WrackStufe(float zeit)
    {
        int alter = Mathf.Min(254, (int)(zeit * EffektTakteJeSekunde) / WrackAlterTakte);
        return Mathf.Min(2, alter / WrackStufeAlter);
    }

    private static bool WrackAbgelaufen(in Effect fx)
        => fx.Kind.StartsWith("wrack") && fx.Time * EffektTakteJeSekunde >= WrackLebenTakte;

    /// <summary>Bodenmarke zuerst (Bild 3, bei uns weiter abdunkelnd mit 45 % —
    /// das Original nimmt dafuer einen eigenen Blitter <c>0x402329</c>, ⚠ die 45 %
    /// sind die alte Setzung aus <see cref="DrawWreck"/>), dann das Truemmerbild
    /// der Stufe.</summary>
    private void DrawWrack(Effect fx, List<Texture2D> frames)
    {
        var anchor = _fxAnchor[fx.Kind];
        if (frames.Count > 3) DrawTexture(frames[3], fx.Pos - anchor, new Color(1, 1, 1, 0.45f));
        DrawTexture(frames[Mathf.Min(WrackStufe(fx.Time), frames.Count - 1)], fx.Pos - anchor);
    }

    // ---- der Pruefstand --------------------------------------------------------

    /// <summary>
    /// <c>--truemmer-check</c> — sprengt drei Fahrzeuge der Karte nacheinander
    /// und laesst die Effekte auf der Effektuhr ablaufen. Gemessen wird die
    /// WIRKUNG, nicht die Formel:
    /// <list type="number">
    /// <item>Scheitel/Strecke der GEFLOGENEN Teile (gemessener hoechster Bogen
    /// je Teil durch d) — Soll 1/2…1/4, Median zwischen 0,20 und 0,52;</item>
    /// <item>Flugdauer der Brocken gegen die Dauer der Wolke — Soll ≈ 1
    /// (Median 0,5…2,0);</item>
    /// <item>Aufschlagbilder = geworfene Brocken;</item>
    /// <item>Todesklang 400..403 genau einmal je Fahrzeug;</item>
    /// <item>der Rumpf steht nach dem Tod im Zeilendurchgang und ist nach 6
    /// Takten weg, an seiner Stelle liegt ein Wrack wrack0..15;</item>
    /// <item>das Wrack altert: Stufe 0/1/2 bei 0/900/1800 Takten, weg bei 2550.</item>
    /// </list>
    /// Nullmodelle sind die sechs Gegenschalter — jeder muss genau seine Zeile
    /// fallen lassen.
    /// </summary>
    public string TruemmerCheck()
    {
        var sb = new System.Text.StringBuilder("truemmer-check (bug-426)\n");
        sb.Append($"  Schalter: bogen-alt {TruemmerBogenAlt}, uhr-alt {TruemmerUhrAlt}, aufschlag-aus {TruemmerAufschlagAus}, "
                + $"todesklang-alt {TodesklangAlt}, sterbend-alt {SterbendAlt}, wrack-alt {WrackAlt}\n");
        // drei Fahrzeuge, die man SIEHT, zuerst (sonst kann Zeile 5 nichts sagen)
        var opfer = new List<int>();
        BuildUnitDrawOrder();
        foreach (bool nurSichtbare in new[] { true, false })
            for (int i = 0; i < _entities.Count && opfer.Count < 3; i++)
            {
                var e = _entities[i];
                if (e.Dead || e.IsProp || e.IsBuilding || e.Infantry >= 0 || e.GameUnitType != 0) continue;
                if (opfer.Contains(i) || (nurSichtbare && !_unitDraw.Contains(i))) continue;
                opfer.Add(i);
            }
        if (opfer.Count == 0) return sb.Append("  kein Fahrzeug auf dieser Karte — der Lauf sagt NICHTS\n  DURCHGEFALLEN").ToString();

        TruemmerMitschreiben = true;
        TruemmerGelandet.Clear();
        // ⚠ die Druckwelle auf die acht Nachbarn bleibt aus: sie toetete sonst
        // Nachbarn mit, und deren Klang zaehlte mit (Pruefstand-Eingriff).
        bool ohneNachbarn = SprengungOhneNachbarn;
        SprengungOhneNachbarn = true;
        bool ok = true;
        int klang0 = TodesklangGespielt, brocken0 = TruemmerBrockenGeworfen, aufschlag0 = TruemmerAufschlaege;
        float dt = 1f / EffektTakteJeSekunde;
        var wolken = new List<float>();
        int rumpfDa = 0, rumpfWeg = 0, wrackNeu = 0, sichtbar = 0;
        var wrackArten = new List<string>();
        foreach (int vi in opfer)
        {
            var v = _entities[vi];
            int fx0 = _effects.Count;
            int wrack0 = _effects.Count(x => x.Kind.StartsWith("wrack") || x.Kind == "wreck");
            // nur wer VOR dem Tod gezeichnet wurde, kann als Rumpf stehen
            // bleiben (eine fremde Einheit im Nebel wird gar nicht gezeichnet)
            BuildUnitDrawOrder();
            bool warSichtbar = _unitDraw.Contains(vi);
            if (warSichtbar) sichtbar++;
            Kill(vi, v, -1, "truemmer-check");
            // die Wolke dieses Todes
            for (int j = fx0; j < _effects.Count; j++)
                if (_effects[j].Kind.StartsWith("sprengung"))
                    wolken.Add(EffectFrames(_effects[j].Kind).Count * _effects[j].FrameTime);
            BuildUnitDrawOrder();
            if (warSichtbar && _unitDraw.Contains(vi)) rumpfDa++;
            // ablaufen lassen, bis alle Teile gelandet sind (hoechstens 20 s)
            bool geprueft = false;
            for (int t = 0; t < 20 * EffektTakteJeSekunde && (_truemmer.Count > 0 || _sterbend.Count > 0 || !geprueft); t++)
            {
                UpdateEffects(dt);
                if (t + 1 == SterbendTakte)
                {
                    // nach genau 6 Takten: Rumpf weg, Wrack da
                    BuildUnitDrawOrder();
                    if (warSichtbar && !_unitDraw.Contains(vi)) rumpfWeg++;
                    geprueft = true;
                }
            }
            int wrack1 = _effects.Count(x => x.Kind.StartsWith("wrack") || x.Kind == "wreck");
            if (wrack1 > wrack0) wrackNeu++;
            var w = _effects.LastOrDefault(x => x.Kind.StartsWith("wrack") || x.Kind == "wreck");
            if (w.Kind != null) wrackArten.Add(w.Kind);
        }
        TruemmerMitschreiben = false;
        SprengungOhneNachbarn = ohneNachbarn;

        // 1. Scheitel / Strecke
        var verh = TruemmerGelandet.Where(x => x.Strecke > 1f).Select(x => x.Scheitel / x.Strecke).OrderBy(x => x).ToList();
        float med = verh.Count > 0 ? verh[verh.Count / 2] : 0f;
        bool z1 = verh.Count > 0 && med >= 0.20f && med <= 0.52f;
        ok &= z1;
        sb.Append($"  1 Scheitel/Strecke: {verh.Count} Teile, Median {med:0.00} (Spanne {(verh.Count > 0 ? verh[0] : 0):0.00}..{(verh.Count > 0 ? verh[^1] : 0):0.00}), "
                + $"Soll 1/4..1/2  {(z1 ? "ja" : "NEIN")}\n");
        // 2. Flugdauer gegen Wolke
        var flug = TruemmerGelandet.Where(x => x.Brocken).Select(x => x.Sekunden).OrderBy(x => x).ToList();
        float wolke = wolken.Count > 0 ? wolken.Average() : 0f;
        float fmed = flug.Count > 0 ? flug[flug.Count / 2] : 0f;
        float r2 = wolke > 0 ? fmed / wolke : 0f;
        bool z2 = flug.Count > 0 && r2 >= 0.5f && r2 <= 2.0f;
        ok &= z2;
        sb.Append($"  2 Flug der Brocken: Median {fmed:0.00} s gegen Wolke {wolke:0.00} s = {r2:0.00}, Soll ≈ 1 (0,5..2)  {(z2 ? "ja" : "NEIN")}\n");
        // 3. Aufschlagbilder
        int brocken = TruemmerBrockenGeworfen - brocken0, aufschlag = TruemmerAufschlaege - aufschlag0;
        bool z3 = brocken > 0 && aufschlag == brocken;
        ok &= z3;
        sb.Append($"  3 Aufschlagbilder 230..233: {aufschlag} bei {brocken} Brocken  {(z3 ? "ja" : "NEIN")}"
                + (EffectFrames("glut0").Count == 0 ? "   ⚠ Folge glut0 fehlt (--reexport-effects)" : "") + "\n");
        // 4. Todesklang
        int klang = TodesklangGespielt - klang0;
        bool z4 = klang == opfer.Count && TodesklangLetzter is >= 400 and <= 403;
        ok &= z4;
        sb.Append($"  4 Todesklang 400..403: {klang}x bei {opfer.Count} Fahrzeugen (zuletzt {TodesklangLetzter}), "
                + $"Veteranenstimme 143: {VeteranenstimmeGespielt}x  {(z4 ? "ja" : "NEIN")}\n");
        // 5. Rumpf und Wrack
        bool z5 = sichtbar > 0 && rumpfDa == sichtbar && rumpfWeg == sichtbar && wrackNeu == opfer.Count
                  && wrackArten.All(k => k.StartsWith("wrack"));
        ok &= z5;
        sb.Append($"  5 Rumpf steht nach dem Tod: {rumpfDa}/{sichtbar} sichtbaren, nach {SterbendTakte} Takten weg: {rumpfWeg}/{sichtbar}, "
                + $"Wrack danach: {wrackNeu}/{opfer.Count} ({string.Join(" ", wrackArten)})  {(z5 ? "ja" : "NEIN")}\n");
        // 6. das Wrack altert
        string stufen = "";
        bool z6 = false;
        int wi = _effects.FindLastIndex(x => x.Kind.StartsWith("wrack"));
        if (wi >= 0 && !WrackAlt)
        {
            var fx = _effects[wi];
            int[] proben = { 0, 899, 900, 1799, 1800, 2549 };
            var st = proben.Select(t => WrackStufe(t / EffektTakteJeSekunde)).ToArray();
            fx.Time = WrackLebenTakte / EffektTakteJeSekunde;
            bool weg = WrackAbgelaufen(fx);
            stufen = $"Stufe bei 0/899/900/1799/1800/2549 Takten: {string.Join("/", st)}, weg bei {WrackLebenTakte}: {weg}";
            z6 = st.SequenceEqual(new[] { 0, 0, 1, 1, 2, 2 }) && weg && EffectFrames(fx.Kind).Count == 4;
        }
        else stufen = "kein neues Wrack (wrack0..15) — altert nicht";
        ok &= z6;
        sb.Append($"  6 Wrack: {stufen}  {(z6 ? "ja" : "NEIN")}\n");
        int fehlend = Enumerable.Range(0, 16).Count(k => EffectFrames("wrack" + k).Count < 4);
        if (fehlend > 0) sb.Append($"  ⚠ {fehlend} von 16 Wrackfolgen fehlen — --reexport-effects=<Quelle> schreibt sie\n");
        sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }

    // ---- Bildlauf (Fenster) ----------------------------------------------------

    /// <summary>Fuer den Bildlauf: ein Fahrzeug im Bild waehlen, Kamerapunkt.</summary>
    public Vector2? TruemmerBildVorbereiten(out int vi)
    {
        vi = -1;
        BuildUnitDrawOrder();                       // nur ein Fahrzeug, das man sieht
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Dead || e.IsProp || e.IsBuilding || e.Infantry >= 0 || e.GameUnitType != 0) continue;
            if (!_unitDraw.Contains(i)) continue;
            vi = i;
            return e.Pos - new Vector2(0, 30);
        }
        return null;
    }

    public void TruemmerBildSprengen(int vi)
    {
        if (vi >= 0) Kill(vi, _entities[vi], -1, "truemmer-check Bildlauf");
        QueueRedraw();
    }

    /// <summary>n Effekttakte weiterlaufen lassen (Baum angehalten).</summary>
    public void TruemmerBildTakte(int n)
    {
        for (int i = 0; i < n; i++) UpdateEffects(1f / EffektTakteJeSekunde);
        _clock += n / EffektTakteJeSekunde;
        QueueRedraw();
    }
}
