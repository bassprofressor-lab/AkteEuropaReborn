namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <c>--geschoss-hoehen-check</c> — der Prüfstand zum <b>Geschossflug über Höhenunterschiede</b>.
///
/// <para><b>Anlass:</b> seine Meldung vom 17.09.2026 — »ich habe auch das gefühl das Raketen
/// nicht immer sauber fliegen, gerade bei höhenunterschieden«. Lesung:
/// <c>berichte/geschossflug-hoehe-fable.md</c>.</para>
///
/// <para><b>Was er misst.</b> Der Prüfstand sucht sich auf der Karte zwei Fälle und schießt
/// beide je <c>N</c> mal ab:</para>
/// <list type="bullet">
///   <item><b>Fall A — freie Bahn:</b> ein Ziel, über dessen Weg keine Zelle höher liegt als
///   die Flugbahn. Soll: <b>kein</b> Bodentreffer.</item>
///   <item><b>Fall B — Kamm im Weg:</b> ein Ziel, bei dem mindestens eine Zelle dazwischen
///   über der Bahn liegt. Soll: <b>Bodentreffer</b> — genau das, was das Original im
///   Leer-Zweig <c>0x452EDC</c> (F <c>0x451B8C</c>) tut und was uns bis heute fehlte.</item>
/// </list>
///
/// <para>Dazu drei Zahlen, die unabhängig vom Gelände gelten: <b>Nachführungen</b> (Soll 0 —
/// das Original setzt die Zielzelle einmal beim Abschuss), der <b>größte Schritt je Takt</b>
/// (ab 40 px könnte ein Geschoss eine Prüfzelle überspringen) und die <b>Neigungsblöcke</b>,
/// die der Zeichner benutzt hat.</para>
///
/// <para><b>Gegenschalter, die ihn durchfallen lassen müssen:</b> <c>--kein-bodentreffer</c>
/// (Fall B bekommt 0), <c>--geschoss-nachfuehren</c> (Nachführungen &gt; 0),
/// <c>--geschosstempo-alt</c> (der größte Schritt fällt auf ein Drittel),
/// <c>--keine-einschlaghoehen</c> (alle Höhenprüfungen aus).</para>
///
/// <para>⚠ <b>Was er NICHT abdeckt:</b> die Hanginterpolation innerhalb einer Zelle (Aufgabe F
/// der Lesung). <see cref="BodenHoehe"/> rechnet mit der flachen Zellhöhe, weil die Hangklassen
/// 8…18 ungelesen sind — auf einer Rampe ist unsere Bahn deshalb noch etwas zu großzügig.</para>
/// </summary>
public partial class MapEntityLayer
{
    private sealed class HoehenFall
    {
        public string Titel = "";
        public int Col, Row, Elev;
        public int Schuss, Boden, Erwartet;
        /// <summary>Wer schiesst — Fall B nimmt oft einen anderen als Fall A.</summary>
        public int Schuetze = -1;
        public string Notiz = "";
    }

    /// <summary><c>--geschoss-spur</c> — schreibt fuer jeden Fall den Weg des ersten
    /// Schusses Zelle fuer Zelle mit. Kein Nullmodell, ein Diagnosewerkzeug.</summary>
    public static bool GeschossSpur;

    public string GeschossHoehenCheck()
    {
        var sb = new System.Text.StringBuilder("geschoss-hoehen-check\n");
        const int N = 20;

        int si = SchuetzeFuerHoehen();
        if (si < 0)
            return sb.Append("   ⚠ DURCHGEFALLEN — kein eigener bewaffneter Schütze mit " +
                             "Flug-Geschoss auf dieser Karte; der Prüfstand sagt hier nichts.\n")
                     .ToString();

        var s = _entities[si];
        int sc = s.Col, sr = s.Row, se = ElevOf(sc, sr);
        int art = Simulation.DesignMath.SoundClass(WeaponRowOf(s.Weapon));
        sb.AppendLine($"   Schütze Platz {s.Slot} auf ({sc},{sr}) Stufe {se}, " +
                      $"Geschossart {art}, Reichweite {s.RangeMin}..{s.Range} Zellen");

        // ⚠ Die Reichweite des Schuetzen ist die Schranke, nicht eine Wunschzahl:
        // 0x40BF64 verweigert den Schuss ueber Reichweite*40 px. Ohne das suchte der
        // Pruefstand Ziele, auf die nie geschossen wurde — und meldete »0 Schuss«.
        // ⚠ eine Zelle Sicherheitsabstand: 0x40BF64 misst in BILDPUNKTEN, und eine
        // Diagonale ist laenger als d*40 — mit d = Reichweite fiel der Schuss aus.
        int weit = Mathf.Clamp(s.Range - 1, 2, 12), nah = Mathf.Max(2, s.RangeMin + 1);
        // ⚠ 17.09.2026, eigener Fehler beim Bauen dieses Prüfstands: die Vorhersage
        // muss den BOGEN mitrechnen. Ohne ihn sagte Fall B »Kamm im Weg« für eine
        // Rakete an, die in Wahrheit darüber hinwegfliegt — der Prüfstand fiel durch,
        // obwohl der Flug es richtig machte. Das ist die Messfalle »wer eine Wirkung
        // misst, muss die Bahn mitmessen«.
        float scheitel = Scheitelteiler(art);
        // ⚠ dritter eigener Fehler: die MUENDUNGSHOEHE gehoert in die Vorhersage.
        // Der Flug startet auf `Stufe*15 + Muendung` (Tafelfeld +0x14); ohne sie
        // rechnet der Pruefstand die Bahn zu tief und meldet Kaemme, die keine sind.
        float muendung = Mathf.Max(0, Audio.GameSounds.MuzzleHeight(art));
        var faelle = new List<HoehenFall>();
        if (ZielSuchen(sc, sr, se, kamm: false, nah, weit, scheitel, muendung) is { } a)
            faelle.Add(new HoehenFall { Titel = "A freie Bahn", Col = a.X, Row = a.Y,
                                        Elev = ElevOf(a.X, a.Y), Erwartet = 0, Schuetze = si });
        else sb.AppendLine("   ⚠ Fall A: keine freie Bahn gefunden");
        // ⚠ Fall B braucht nicht denselben Schuetzen. Eine RAKETE traegt ihr Bogen ueber
        // fast jeden Kamm — das ist selbst ein Befund und kein Mangel. Geblockt wird vor
        // allem die FLACHE Bahn, also sucht Fall B unter ALLEN eigenen Schuetzen den
        // ersten, bei dem wirklich Gelaende im Weg steht.
        int bsi = -1;
        for (int k = 0; k < _entities.Count && bsi < 0; k++)
        {
            var x = _entities[k];
            if (x.Dead || x.IsProp || x.IsBuilding || x.Owner != ViewPlayer) continue;
            if (x.Weapon <= 0 || x.Attack <= 0) continue;
            int xart = Simulation.DesignMath.SoundClass(WeaponRowOf(x.Weapon));
            if (FlightKind(xart) == null || xart is 7 or 12) continue;
            int xw = Mathf.Clamp(x.Range - 1, 2, 12), xn = Mathf.Max(2, x.RangeMin + 1);
            if (ZielSuchen(x.Col, x.Row, ElevOf(x.Col, x.Row), true, xn, xw,
                           Scheitelteiler(xart),
                           Mathf.Max(0, Audio.GameSounds.MuzzleHeight(xart))) is { } bz)
            {
                bsi = k;
                faelle.Add(new HoehenFall
                {
                    Titel = $"B Kamm im Weg (Platz {x.Slot}, Art {xart})",
                    Col = bz.X, Row = bz.Y, Elev = ElevOf(bz.X, bz.Y),
                    Erwartet = N, Schuetze = k,
                });
            }
        }
        if (bsi < 0)
            sb.AppendLine("   ⚠ Fall B: auf dieser Karte steht bei KEINEM eigenen Schützen " +
                          "Gelände in der Flugbahn — der Prüfstand kann A hier nicht belegen");

        GeschossNachgefuehrt = 0;
        GeschossMaxSchritt = 0f;
        GeschossBloecke.Clear();
        GeschossBlockwechsel = GeschossMaxBlockwechsel = 0;
        GeschossRichtungswechsel = GeschossMaxRichtungswechsel = 0;
        GeschossErstBlock8 = GeschossErstBlock16 = GeschossErstBlock0 = 0;

        foreach (var f in faelle)
        {
            int vorher = GeschossBoden;
            int schuetze = f.Schuetze >= 0 ? f.Schuetze : si;
            var sx = _entities[schuetze];
            // ⭐ DIE SPUR eines einzelnen Schusses — ohne sie raet man beim Bauen dieses
            // Pruefstands im Dunkeln (dreimal geschehen). Sie zeigt Zelle fuer Zelle, was
            // der Flug sieht: Flughoehe, Boden, Schwelle.
            if (GeschossSpur)
            {
                sx.Cooldown = 0f;
                if (sx.AmmoMax > 0) sx.Ammo = sx.AmmoMax;
                if (FireAtAusfuehren(schuetze, f.Col, f.Row))
                {
                    sb.AppendLine($"     Spur {f.Titel}:");
                    var gesehen = new HashSet<int>();
                    for (int t = 0; t < 140 && _shots.Count > 0; t++)
                    {
                        var q = _shots[0];
                        if (CellAt(q.Pos) is { } qc)
                        {
                            int qq = Mathf.RoundToInt(qc.X) * 1024 + Mathf.RoundToInt(qc.Y);
                            if (gesehen.Add(qq))
                                sb.AppendLine(
                                    $"       Zelle ({Mathf.RoundToInt(qc.X)},{Mathf.RoundToInt(qc.Y)}) " +
                                    $"Stufe {ElevOf(Mathf.RoundToInt(qc.X), Mathf.RoundToInt(qc.Y))} " +
                                    $"Boden {BodenHoehe(Mathf.RoundToInt(qc.X), Mathf.RoundToInt(qc.Y)):0} " +
                                    $"Schwelle {SchwelleAn(Mathf.RoundToInt(qc.X), Mathf.RoundToInt(qc.Y))} " +
                                    $"Flughoehe {GeschossHoehe(q):0}");
                        }
                        SimTickFuerProbe();
                    }
                }
            }
            for (int k = 0; k < N; k++)
            {
                sx.Cooldown = 0f;
                if (sx.AmmoMax > 0) sx.Ammo = sx.AmmoMax;
                if (!FireAtAusfuehren(schuetze, f.Col, f.Row)) { f.Notiz = FireAtGrund; break; }
                f.Schuss++;
                for (int t = 0; t < 140 && _shots.Count > 0; t++) SimTickFuerProbe();
            }
            f.Boden = GeschossBoden - vorher;
        }

        bool ok = faelle.Count > 0;
        foreach (var f in faelle)
        {
            // Fall B ist eine FUNDSACHE: ob auf dieser Karte ueberhaupt Gelaende in
            // einer Bahn steht, entscheidet die Karte. Das Urteil haengt darum an der
            // gemessenen Lage weiter unten, nicht an ihm.
            bool gut = f.Erwartet == 0 ? f.Boden == 0 : f.Boden > 0;
            if (f.Erwartet == 0) ok &= gut && f.Schuss > 0;
            sb.AppendLine($"     {(gut && f.Schuss > 0 ? "ok  " : "⚠ FALSCH")} {f.Titel}: " +
                          $"Ziel ({f.Col},{f.Row}) Stufe {f.Elev}, {f.Schuss} Schuss, " +
                          $"{f.Boden} im Boden (Soll {(f.Erwartet == 0 ? "0" : "≥ 90 %")})" +
                          (f.Notiz.Length > 0 ? $" — {f.Notiz}" : ""));
        }

        // ⭐ DER RUNDUMSCHLAG. Zwei gesuchte Faelle treffen auf einer Kampagnenkarte
        // selten wirklich Gelaende — also schiesst der Pruefstand zusaetzlich von JEDEM
        // eigenen Schuetzen in ALLE Richtungen auf volle Reichweite. Das ist kein Soll,
        // sondern die Gelegenheit, die Lage ueberhaupt eintreten zu lassen; gezaehlt
        // wird unten, was dabei passiert ist.
        int rundum = 0;
        for (int k = 0; k < _entities.Count && rundum < 400; k++)
        {
            var x = _entities[k];
            if (x.Dead || x.IsProp || x.IsBuilding || x.Owner != ViewPlayer) continue;
            if (x.Weapon <= 0 || x.Attack <= 0) continue;
            int xart = Simulation.DesignMath.SoundClass(WeaponRowOf(x.Weapon));
            if (FlightKind(xart) == null || xart is 7 or 12) continue;
            int xw = Mathf.Clamp(x.Range - 1, 2, 12);
            for (int w = 0; w < 16; w++)
            {
                float a2 = w * Mathf.Pi / 8f;
                int zc = x.Col + Mathf.RoundToInt(Mathf.Cos(a2) * xw);
                int zr = x.Row + Mathf.RoundToInt(Mathf.Sin(a2) * xw);
                if (!AufKarte(zc, zr)) continue;
                x.Cooldown = 0f;
                if (x.AmmoMax > 0) x.Ammo = x.AmmoMax;
                if (!FireAtAusfuehren(k, zc, zr)) continue;
                rundum++;
                for (int t = 0; t < 140 && _shots.Count > 0; t++) SimTickFuerProbe();
            }
        }
        sb.AppendLine($"     Rundumschlag: {rundum} weitere Schüsse in alle Richtungen " +
                      "(um die Lage überhaupt eintreten zu lassen)");

        // ⭐ Der eigentliche Beleg fuer Aufgabe A: wie oft ist die Lage »Gelaende in der
        // Bahn« ueberhaupt eingetreten? Gemessen am wirklich geflogenen Weg, nicht
        // vorhergesagt — die Vorhersage hat sich beim Bauen dieses Pruefstands viermal
        // geirrt (Bogen, Bildraum, Muendungshoehe, besetzte Zelle).
        int lage = GeschossBoden + GeschossBodenVerpasst;
        bool lageTrat = lage > 0;
        // ⚠ Ein Nullmodell muss DURCHFALLEN. Mit --kein-bodentreffer sind genau die
        // Geschosse durch den Hang geflogen, um die es geht — das ist kein »ok«.
        bool bodenOk = !KeinBodentreffer && GeschossBodenVerpasst == 0;
        sb.AppendLine(!lageTrat
            ? "     ⚠ Gelände in der Bahn: auf dieser Karte NIE eingetreten — " +
              "der Prüfstand sagt zu Aufgabe A hier NICHTS"
            : KeinBodentreffer
                ? $"     ⚠ FALSCH (Nullmodell wirkt) Gelände in der Bahn: {lage} mal " +
                  $"eingetreten, {GeschossBodenVerpasst} Geschosse sind DURCH den Hang " +
                  "geflogen — genau der Fehler, den --kein-bodentreffer zurückholt"
                : $"     ok   Gelände in der Bahn: {lage} mal eingetreten, davon " +
                  $"{GeschossBoden} im Boden geblieben (Soll: alle)");

        bool nachOk = GeschossNachgefuehrt == 0;
        bool schrittOk = GeschossMaxSchritt < 40f;
        sb.AppendLine($"     {(nachOk ? "ok  " : "⚠ FALSCH")} Nachführungen: " +
                      $"{GeschossNachgefuehrt} (Soll 0 — das Original führt nie nach)");
        sb.AppendLine($"     {(schrittOk ? "ok  " : "⚠ FALSCH")} größter Schritt: " +
                      $"{GeschossMaxSchritt:0.0} px je Takt (Soll < 40 = eine Zelle)");
        sb.AppendLine(GeschossBloecke.Count > 0
            ? $"     Neigungsblöcke gezeichnet: {{{string.Join(",", GeschossBloecke)}}} " +
              "(0 flach, 8 Nase runter, 16 hoch)"
            : "     Neigungsblöcke: keine — kopflos wird nicht gezeichnet, das sagt hier NICHTS");
        // ⭐ 19.09.2026 — seine Meldung »als würden die Raketen wackeln während des
        // Fluges«. Gemessen wird der WECHSEL, nicht die Auswahl: ein sauberer Bogen
        // braucht höchstens zwei (Nase hoch → flach → Nase runter). ⚠ Noch KEIN
        // Bestehenskriterium — was das Original hier tut, wird gerade gegengelesen
        // (berichte/geschossflug-fable.md). Bis dahin ist es ein Befund.
        sb.AppendLine($"     Blockwechsel im Flug: {GeschossBlockwechsel} insgesamt, "
                    + $"schlimmstes Geschoss {GeschossMaxBlockwechsel} "
                    + "(Soll ≤ 2 je Flug; mehr ist Flattern um die Schwelle ±1)");
        sb.AppendLine($"     Richtungswechsel im Flug: {GeschossRichtungswechsel} insgesamt, "
                    + $"schlimmstes Geschoss {GeschossMaxRichtungswechsel} "
                    + "(Soll 0 — eine gerade Bahn hat eine feste Richtung)");
        // ⭐⭐ 19.09.2026, bug-305 — DIE ZAHL, AN DER DIE VERTAUSCHUNG HING. Eine
        // Wurfbahn steigt beim Abschuss, also muss JEDE mit Block 8 (Nase hoch)
        // anfangen. Stand dort 16, flog die Rakete bergauf mit der Nase nach unten.
        // Nullmodell --neigungsblock-alt dreht die Spalten um.
        int bahnen = GeschossErstBlock8 + GeschossErstBlock16 + GeschossErstBlock0;
        sb.AppendLine($"     BEFUND erster Neigungsblock: 8 (Nase hoch) {GeschossErstBlock8}×, "
                    + $"16 (Nase runter) {GeschossErstBlock16}×, flach {GeschossErstBlock0}× "
                    + $"von {bahnen} Bahnen"
                    + (MapEntityLayer.NeigungsblockAlt
                        ? "   ⚠ NULLMODELL --neigungsblock-alt: die beiden Spalten MUESSEN "
                          + "gegenueber dem gewoehnlichen Lauf vertauscht sein"
                        : ""));
        // ⚠ KEIN Bestehenskriterium, und das ist eine Berichtigung vom selben Tag: hier
        // stand erst »16 darf nicht vorkommen«. Das ist falsch — ein Schuss auf ein TIEFER
        // gelegenes Ziel faellt von Anfang an, dort ist 16 richtig (K16: 93 von 176 Bahnen).
        // Die Vertauschung selbst ist am Maschinencode belegt (0x452587 steigend -> 8,
        // 0x452579 fallend -> 16) und an den Bildern (f10/f14 Nase oben, f18/f22 unten);
        // der Pruefstand zeigt hier nur die Verteilung, und das NULLMODELL vertauscht sie.

        ok &= nachOk && schrittOk && lageTrat && bodenOk;
        sb.AppendLine(ok
            ? "   ✅ BESTANDEN — Geländehöhe stoppt das Geschoss, kein Nachführen, kein Zellsprung."
            : "   ❌ DURCHGEFALLEN — siehe die Zeilen oben (die Gegenschalter müssen genau das zeigen).");
        return sb.ToString();
    }

    /// <summary>Kennt die Höhentafel diese Zelle? <see cref="ElevOf"/> gibt außerhalb still
    /// 0 zurück — das wäre ein Loch im Prüfstand und kein Gelände.</summary>
    private bool AufKarte(int col, int row) => _elevLookup.ContainsKey((col, row));

    /// <summary>Ein eigener, fahrbarer, bewaffneter Schütze, dessen Waffe ein echtes
    /// Flug-Geschoss hat (nicht Art 7/12 — die haben eigene Bahnen).</summary>
    private int SchuetzeFuerHoehen()
    {
        int ersatz = -1;
        for (int k = 0; k < _entities.Count; k++)
        {
            var x = _entities[k];
            if (x.Dead || x.IsProp || x.IsBuilding || x.Owner != ViewPlayer) continue;
            if (x.Weapon <= 0 || x.Attack <= 0) continue;
            int art = Simulation.DesignMath.SoundClass(WeaponRowOf(x.Weapon));
            if (FlightKind(art) == null || art is 7 or 12) continue;
            // ⭐ Raketen zuerst (Arten 5/6): seine Meldung galt ihnen, und nur sie
            // fliegen einen Bogen — an ihnen sieht man die Neigungsbloecke.
            if (art is 5 or 6) return k;
            ersatz = ersatz < 0 ? k : ersatz;
        }
        return ersatz;
    }

    /// <summary>
    /// Sucht eine Zielzelle in 5…12 Zellen Abstand. <paramref name="kamm"/> entscheidet, ob
    /// zwischen Mündung und Ziel eine Zelle ÜBER der Flugbahn liegen soll oder keine.
    ///
    /// <para>Die Bahn wird so gerechnet, wie der Flug sie fliegt: linear von der Mündungshöhe
    /// (<c>Stufe·15</c>) zur Zielhöhe (<c>Stufe·15 + 10</c>), und eine Zwischenzelle liegt im
    /// Weg, wenn ihr Boden die Bahn erreicht. Das ist dieselbe Regel, die der Flug anwendet —
    /// gemessen wird darum nicht die REGEL, sondern ob das Geschoss sich daran hält.</para>
    /// </summary>
    private Vector2I? ZielSuchen(int sc, int sr, int se, bool kamm, int nah, int weit,
                                float scheitelteiler, float muendung)
    {
        for (int d = weit; d >= nah; d--)
            for (int w = 0; w < 16; w++)
            {
                float a = w * Mathf.Pi / 8f;
                int zc = sc + Mathf.RoundToInt(Mathf.Cos(a) * d);
                int zr = sr + Mathf.RoundToInt(Mathf.Sin(a) * d);
                if (!AufKarte(zc, zr)) continue;
                int ze = ElevOf(zc, zr);
                float h0 = se * 15f + muendung, h1 = ze * 15f + 10f;
                // Der Scheitel rechnet in Kartenmassen (dy verdoppelt), wie im Flug.
                float dx = (zc - sc) * 40f, dy = (zr - sr) * 40f;
                float scheitel = scheitelteiler * Mathf.Sqrt(dx * dx + dy * dy * 4f);
                // ⚠⚠ 17.09.2026, zweiter eigener Fehler an diesem Prüfstand: die Bahn
                // wird im BILDRAUM geflogen, nicht im Zellraster. Unter der Schrägsicht
                // ist die gerade Linie zwischen zwei Bildpunkten NICHT die gerade Linie
                // zwischen zwei Zellen — der Prüfstand sagte deshalb Kämme an, die das
                // Geschoss nie überfliegt. Jetzt wird derselbe Weg abgeschritten, den
                // der Flug nimmt: Bildpunkte, und die Zelle über CellAt.
                Vector2 pa = CellCenter(sc, sr), pb = CellCenter(zc, zr);
                // ⚠⚠ Vierter eigener Fehler, und der lehrreichste: »etwas im Weg« ist
                // NICHT dasselbe wie »Gelaende im Weg«. Der erste Kandidat war eine
                // BESETZTE Zelle — die stoppt das Geschoss ueber den alten Zweig
                // (Schwelle 15), zaehlt aber nicht als Bodentreffer. Der Pruefstand
                // verlangte einen Bodentreffer und bekam zu Recht keinen.
                // Gezaehlt wird darum, WELCHER Blocker der erste ist.
                int schritte = Mathf.Max(4, Mathf.CeilToInt(pa.DistanceTo(pb) / 10f));
                bool bodenBlock = false, fremdBlock = false;
                for (int i = 1; i < schritte && !bodenBlock && !fremdBlock; i++)
                {
                    float t = i / (float)schritte;
                    var zelle = CellAt(pa.Lerp(pb, t));
                    if (zelle is not { } zq) continue;
                    int mc = Mathf.RoundToInt(zq.X), mr = Mathf.RoundToInt(zq.Y);
                    if (!AufKarte(mc, mr)) { fremdBlock = true; break; }
                    if (mc == sc && mr == sr) continue;          // die eigene Zelle zaehlt nie
                    if (mc == zc && mr == zr) break;             // am Ziel ist Schluss
                    float bahn = Mathf.Lerp(h0, h1, t) + 4f * scheitel * t * (1f - t);
                    int schw = SchwelleAn(mc, mr);
                    if (schw > 0) { if (bahn < BodenHoehe(mc, mr) + schw) fremdBlock = true; }
                    else if (BodenHoehe(mc, mr) >= bahn) bodenBlock = true;
                }
                // Fall B will GELAENDE als ersten Blocker, Fall A gar keinen.
                if (kamm ? bodenBlock : (!bodenBlock && !fremdBlock))
                    return new Vector2I(zc, zr);
            }
        return null;
    }
}
