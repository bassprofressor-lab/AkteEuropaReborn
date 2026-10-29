namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// ⭐⭐ 29.10.2026, bug-645/646 (KayelGee R8 K3/K4: »die Gegner-KI fokussiert sich sehr
/// stark auf die erste gesehene Einheit«, »wehrt sich teils nicht«) — <b>DAS
/// GELEGENHEITSFEUER</b>. Lesung <c>berichte/ki-zielwahl-r8-opus.md</c> §1, Bau
/// <c>berichte/bau-p4-0-6-12.md</c> §1; Spielerentscheidung 29.10.: wie im Original fuer
/// ALLE (Spielerbefehle, Kampagne, Gefecht, Netz).
///
/// <para><b>Original (selbst nachgelesen, C):</b> der 78-Byte-Satz traegt ZWEI Zielwoerter,
/// das Auftragsziel <c>UTOK_NA +0x36</c> und das Schussziel <c>STRILI_NA +0x34</c>. Der
/// Angriffsbefehl <c>0x40FC90</c> schreibt nur <c>+0x36</c> (Schreiber <c>0x410058</c>; von
/// den 24 Schreibern auf <c>+0x34</c> liegt keiner darin). Die Schiessuhr der Fahrzeuge
/// <c>0x40DDB0</c> entscheidet, worauf geschossen wird:</para>
/// <code>
///   0x40DEA7  bx = STRILI_NA ; eax = Takt [0x4FA240] ; ecx = Einheit
///   0x40DEB3  STRILI_NA == 0xFFFF:
///   0x40DEC2     (Takt + Einheit) % 30 != 0 -> raus        ; mov ebx,0x1E / idiv
///              Ringsuche nach ABSTAND im Waffenradius: Einheit, Infanteriezelle,
///              Gebaeude NUR wenn == UTOK_NA                -> STRILI_NA
///   0x40E240  STRILI_NA gesetzt: (Takt + Einheit) % 10 == 0 -> 0x40E54B (nur halten)
///   0x40E252     UKOL != 4 -> 0x40E54B
///   0x40E267     UTOK_NA == STRILI_NA -> 0x40E54B
///              UTOK_NA im Reichweitenquadrat und Probeschuss trifft -> STRILI_NA := UTOK_NA
///   0x40E54B  halten: Ziel tot (0xFF) / sterbend (UKOL &gt;= 100) / Probeschuss 0x401A82
///              trifft nicht -> STRILI_NA := 0xFFFF (0x40E5B7); sonst Schuss 0x4013CF
/// </code>
/// <para>F: <c>0x40DCF2</c> (Suchtor), <c>0x40E070…0x40E097</c> (Rueckschaltung,
/// byteidentisch <c>mov esi,0xa / cmp al,4 / cmp si,bx</c>), <c>0x40E3E7</c> (Raeumung) —
/// aus dem Lesebericht, im F-Code nachgeschlagen dort.</para>
///
/// <para><b>Bei uns bis heute:</b> <see cref="Entity.Target"/> war Auftrags- UND Schussziel.
/// Eine Einheit mit Angriffsbefehl (<c>Ordered</c>) schoss unterwegs auf nichts anderes
/// (<c>AutoAcquire</c> ueberspringt sie) — sie fuhr an Gegnern vorbei, die auf sie
/// schossen. Jetzt fuehrt <see cref="Entity.Schussziel"/> das <c>+0x34</c> fuer jede
/// Einheit mit Auftrag; <c>Target</c>/<c>Ordered</c> bleiben das Auftragsziel. Die Fahrt
/// zum Auftragsziel laeuft weiter — die Schiessuhr schreibt weder UKOL noch POHYB.</para>
///
/// <para>⚠ <b>Nicht geaendert (originaltreu):</b> der Selbstverteidiger (nur UKOL 0,
/// <c>0x40CA60</c>) — ein Angreifer AUSSERHALB der eigenen Reichweite bleibt bei einer
/// Einheit im Angriff unbeantwortet (K4-Rest). Die Wahl des Gruppenziels der KI.</para>
///
/// <para>⚠ <b>Fussvolk</b> hat eine eigene Uhr <c>0x40F0A0</c> (selbst gelesen): sie laeuft
/// nur im Stand, sucht JEDEN Takt (kein 30er-Tor) die naechste Zelle im Ring
/// (<c>0x4F5B38</c>), schaltet NICHT aufs Auftragsziel zurueck und haelt die Einheit an
/// (<c>POHYB := 0xFF</c> @0x40F244), wenn das gefundene Ziel das Auftragsziel ist. Unser
/// Fussvolk geht zellweise ohne Zwischenhalt; es behaelt darum den bisherigen Weg (im
/// Gehen kein Schuss, im Stand das Auftragsziel). UNSERE SETZUNG, benannt.</para>
///
/// <para>⚠ <b>UNSERE SETZUNGEN:</b> (1) die »Einheit« im Taktversatz ist unsere
/// Listenstelle (ein gebauter Satz traegt bei uns Platz −1); (2) der Probeschuss
/// <c>0x454300</c>/<c>0x401A82</c> ist bei uns das Schussfenster (Reichweite und
/// Mindestreichweite); (3) die Ringsuche ist die naechste Entfernung wie in
/// <c>AutoAcquire</c> (gleiche Tore: Gebaeude nur als Auftragsziel, Plasma, KI-Sicht).</para>
///
/// <para><b>Der Restauftrag (selbst gelesen, im Lesebericht offen):</b> der UKOL-4-Arm
/// <c>0x409060…0x409173</c> ruft bei <c>POHYB == 0xFF</c> (steht) den Angriffsbefehl neu
/// (<c>0x409090</c>/<c>0x4090B9</c>/<c>0x409124</c> → Thunk <c>0x4014D8</c> → <c>0x40FC90</c>);
/// der prueft den Tod des Ziels NICHT (Kopf <c>0x40FC90…0x40FD4E</c>: nur UKOL 0x16/0x17,
/// <c>STRILI_NA == Ziel</c>, Waffe 8), und das Entfernen <c>0x410E60</c> schreibt am toten
/// Satz nur <c>+0x09 := 0xFF</c> und <c>+0x24</c> — Spalte/Zeile bleiben. Der Auftrag
/// endet also NICHT mit dem Tod: die Einheit faehrt zur Zelle, an der das Ziel starb, und
/// bleibt dort mit UKOL 4 (Schiessuhr weiter, Selbstverteidiger nicht). Gebaut als
/// <see cref="Entity.RestZiel"/>. ⚠ Vorbehalt: dass der tote Satz spaeter fuer eine neue
/// Einheit wiederverwendet wird (dann griffe der Restauftrag sie an), bilden wir nicht ab;
/// Gebaeudeziele behalten den alten Weg.</para>
///
/// <para>Gegenschalter <c>--gelegenheitsfeuer-alt</c> (Stand 0.6.12: ein Ziel, kein
/// Restauftrag). Pruefstand <c>--gelegenheitsfeuer-check</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--gelegenheitsfeuer-alt</c> — Auftragsziel = einziges Schussziel, der
    /// Auftrag endet mit dem Tod des Ziels (Stand bis 29.10.2026).</summary>
    public static bool GelegenheitsfeuerAlt;

    /// <summary>Zaehler fuer Pruefstand und Zeile.</summary>
    public int GfAufnahmen, GfSchuesse, GfRueckschaltungen, GfGeraeumt, GfRestauftraege;

    /// <summary>Gilt das getrennte Schussziel fuer diese Einheit? Fahrzeuge und Schiffe mit
    /// Angriffsauftrag; Fussvolk nicht (siehe Kopf).</summary>
    private static bool GfAn(Entity e) => !GelegenheitsfeuerAlt && e.Ordered && e.Infantry < 0;

    /// <summary>
    /// Die Schiessuhr fuer eine Einheit mit Auftrag. <paramref name="zielImFenster"/>: das
    /// Auftragsziel ist im Schussfenster (Probeschuss). Gibt das Schussziel dieses Takts
    /// zurueck (−1 = keins, sonst Listenstelle; == Target heisst Auftragsziel).
    /// </summary>
    private int GfSchiessuhr(int i, Entity e, bool zielImFenster)
    {
        int takt = _taktNr + i;
        if (e.Schussziel < 0)
        {
            // 0x40DEC2: gesucht wird nur jeden 30. Takt, versetzt um die Einheit
            if (takt % 30 != 0) return -1;
            e.Schussziel = GfSuche(i, e, zielImFenster);
            if (e.Schussziel >= 0 && e.Schussziel != e.Target) GfAufnahmen++;
        }
        else if (e.Schussziel != e.Target && takt % 10 != 0 && zielImFenster)
        {
            // 0x40E240…0x40E3BC: in 9 von 10 Takten Vorrang des Auftrags
            e.Schussziel = e.Target;
            GfRueckschaltungen++;
        }
        // 0x40E54B: halten oder raeumen
        int s = e.Schussziel;
        if (s < 0) return -1;
        bool gut;
        if (s == e.Target) gut = zielImFenster;
        else
        {
            gut = s < _entities.Count;
            if (gut)
            {
                var t = _entities[s];
                gut = !t.Dead && !t.IsProp && !t.IsBuilding && IsHostile(e, t)
                      && !PlasmaSelbstzielVerboten(e, t)
                      && !(KiAufklaerungAn && IstKi(e.Owner) && !KiKenntRoh(e.Owner, t))
                      && InFiringWindow(e, CellDistance(e, t));
            }
        }
        if (!gut) { e.Schussziel = -1; GfGeraeumt++; return -1; }       // 0x40E5B7
        return s;
    }

    /// <summary>Die Ringsuche (0x40DED4…0x40E232): das naechste treffbare Feindziel im
    /// Waffenradius; ein Gebaeude nur, wenn es das Auftragsziel ist. Dieselben Tore wie
    /// <c>AutoAcquire</c>.</summary>
    private int GfSuche(int i, Entity e, bool zielImFenster)
    {
        if (!RaketeSelbstziel && WeaponRowOf(e.Weapon) == 8)
            return zielImFenster ? e.Target : -1;           // die Rakete sucht nie selbst (0x40DDF6)
        float best = float.MaxValue;
        int bi = -1;
        bool kiSicht = KiAufklaerungAn && IstKi(e.Owner);
        if (zielImFenster && e.Target >= 0 && e.Target < _entities.Count)
        {
            var t0 = _entities[e.Target];
            // ⚠ ein Gebaeude als Auftragsziel zaehlt mit dem Abstand zu seiner Schusszelle
            // — wir kennen ihn hier nicht und setzen es VOR jede Einheit gleichen Abstands.
            best = t0.IsBuilding ? 0f : CellDistance(e, t0);
            bi = e.Target;
        }
        for (int j = 0; j < _entities.Count; j++)
        {
            if (j == i || j == e.Target) continue;
            var t = _entities[j];
            if (t.IsBuilding || !IsHostile(e, t)) continue;
            if (PlasmaSelbstzielVerboten(e, t)) continue;
            float d = CellDistance(e, t);
            if (!InFiringWindow(e, d) || d >= best) continue;
            if (kiSicht && !KiKenntRoh(e.Owner, t)) { KiAutoVerworfen++; continue; }
            best = d; bi = j;
        }
        return bi;
    }

    /// <summary>
    /// Der Schuss auf ein GELEGENHEITSZIEL — ohne den Weg anzufassen. Eine Einheit ohne
    /// Turm (<c>Weapon == 0</c>) dreht den ganzen Rumpf und schiesst darum nur im Stand
    /// (unsere Setzung von 01.09., siehe <c>imFahren</c> in UpdateCombat).
    /// </summary>
    private void GfFeuer(int i, Entity e, int s)
    {
        var t = _entities[s];
        var richtung = DirToFacing(t.Pos - e.Pos);
        if (TurmEigen(e)) TurmZielWunsch(e, richtung);
        else e.AimFacing = richtung;
        if (e.Weapon == 0 && e.Path != null) return;
        if (Schussgruende)
            e.Schussgrund = $"GELEGENHEIT: Platz {t.Slot} statt Auftrag {(e.Target >= 0 ? _entities[e.Target].Slot : -1)}";
        if (e.Cooldown > 0 || !HasAmmo(e) || GaswerferSchweigt(e)) return;
        e.Cooldown = NachladenFuer(e, t.Col, t.Row);
        if (e.AmmoMax > 0 && !(CheatAmmo && Cheated(e))
            && (Art7Einzeltreffer || WeaponRowOf(e.Weapon) != 8))
            e.Ammo--;
        DebugShots++;
        GfSchuesse++;
        if (WeaponRowOf(e.Weapon) == 8) RaketenKlang(e, original: false);
        Fire(i, e, s, t, WeaponOf(e.Weapon));
    }

    /// <summary>Das Auftragsziel ist tot: Restauftrag statt Ende (siehe Kopf). true = der
    /// Restauftrag ist angelegt.</summary>
    private bool GfRestauftrag(int i, Entity e, Entity tot)
    {
        // ⚠ nur Fahrzeuge/Schiffe: der Bewegungsarm des Fussvolks ist dafuer nicht gelesen
        if (GelegenheitsfeuerAlt || !e.Ordered || tot.IsBuilding || !e.Mobile || e.Infantry >= 0 || _nav == null) return false;
        if (e.Orders.Count > 0) return false;          // UNSERE Befehlsschlange geht vor (das Original hat keine)
        var zelle = new Vector2I(tot.Col, tot.Row);
        e.RestZiel = zelle;
        GfRestauftraege++;
        var goal = _nav.NearestFree(zelle, e.Move, i);
        e.RestGoal = goal ?? zelle;
        if (goal is { } g && (g.X != e.Col || g.Y != e.Row))
        {
            var weg = WegAbVormerkung(i, e, g);
            if (weg != null && weg.Count > 0) { e.Path = weg; e.PathIdx = 0; e.Goal = g; }
        }
        return true;
    }

    // ================= P4-Nacharbeit 29.10.2026: Weg waehrend eines Schritts =================

    /// <summary><c>--weg-vormerkung-alt</c> — ein neuer Weg beginnt wieder an der Zelle der
    /// Einheit, auch wenn sie gerade einen Schritt in die vorgemerkte Zelle macht.</summary>
    public static bool WegVormerkungAlt;

    /// <summary>Wie oft ein neuer Weg an der vorgemerkten Zelle angesetzt wurde.</summary>
    public int WegAbVormerkungZahl;

    /// <summary>
    /// ⭐ <b>Ein neuer Weg WAEHREND eines Schritts.</b> Gefunden am <c>--befehle-laden-check</c>
    /// K5 (P4-Nacharbeit): eine Einheit bekam mitten im Schritt (Reserved gesetzt) einen Weg
    /// ab ihrer ALTEN Zelle. Der Schritt lief zu Ende und zaehlte <c>PathIdx++</c> — die
    /// Einheit stand auf (32,31), ihr naechster Wegpunkt (29,28) lag drei Zellen weg, sie
    /// merkte ihn vor und kam nie an (gemessen: 29 Takte reglos, der Lader verwarf den Weg).
    /// Der Weg beginnt darum an der VORGEMERKTEN Zelle und traegt sie als ersten Punkt —
    /// die Ankunft zaehlt genau ihn weg. Gilt fuer Verfolgung, Restauftrag und Neuruf des
    /// Angriffs. ⚠ UNSERE Setzung (die Schrittmechanik ist unsere); Gegenschalter
    /// <c>--weg-vormerkung-alt</c>.
    /// </summary>
    private System.Collections.Generic.List<Vector2I> WegAbVormerkung(int i, Entity e, Vector2I goal)
    {
        if (WegVormerkungAlt || e.Reserved is not { } r || (r.X == e.Col && r.Y == e.Row))
            return _nav!.FindPath(new Vector2I(e.Col, e.Row), goal, e.Move, i);
        WegAbVormerkungZahl++;
        if (r == goal) return new System.Collections.Generic.List<Vector2I> { r };
        var weg = _nav!.FindPath(r, goal, e.Move, i);
        if (weg == null || weg.Count == 0) return weg;
        if (weg[0] != r) weg.Insert(0, r);
        return weg;
    }

    // ================= P4-Zusatz 29.10.2026: der Angriff faehrt bis zur Zielzelle =================

    /// <summary><c>--angriff-halt-alt</c> — ein befohlener Angriff haelt wieder bei
    /// Reichweite an (Stand bis 29.10.2026).</summary>
    public static bool AngriffHaltAlt;

    /// <summary>Wie oft der Angriffsbefehl am Wegende neu gerufen wurde.</summary>
    public int AngriffNeuGerufen;

    /// <summary>
    /// ⭐ <b>Der befohlene Angriff faehrt bis an die Zielzelle</b> (Spielerentscheidung 29.10.,
    /// »wie im Original«). Selbst gelesen (C):
    /// <code>
    ///   0x40FD58..0x40FD90  Abstand &gt;= Mindestreichweite -> Fahrziel = Spalte/Zeile des Ziels
    ///   0x410031  UKOL := 4 ; 0x41003C/0x410042 CX/CY (+0x18/+0x19) := Zielzelle
    ///   0x41004A  POHYB := 0xFF ; 0x410058 UTOK_NA := Ziel ; 0x41006A Weiche je Klasse -> 0x401726
    ///   0x409060  UKOL-4-Arm: POHYB == 0xFF (Wegende) -> 0x4014D8 -> 0x40FC90 neu
    ///   0x40FD10  STRILI_NA == Ziel -> nichts (die Einheit bleibt, solange sie aufs Ziel schiesst)
    /// </code>
    /// <para>Kein Halt bei Reichweite: die Schiessuhr schreibt weder UKOL noch POHYB, die
    /// Einheit schiesst im Fahren. Erst am Wegende (an der Zielzelle bzw. der naechsten
    /// freien daneben) bleibt sie stehen; schiesst sie dann nicht aufs Ziel (es ist
    /// weitergefahren, oder die Uhr wartet auf ihren 30er-Takt), ruft der Arm den Befehl
    /// neu und sie faehrt der Zielzelle nach.</para>
    /// <para>⚠ UNSERE SETZUNGEN: Fahrzeuge ohne Turm (<c>Weapon == 0</c>) halten weiter bei
    /// Reichweite (sie schiessen bei uns nur im Stand, Setzung 01.09.); Fussvolk haelt
    /// weiter (seine Uhr 0x40F0A0 setzt POHYB := 0xFF beim Auftragsziel, @0x40F244); der
    /// Spiegelpunkt bei »zu nah« (0x40FD95..0x40FE34) bleibt nicht gebaut. Gegenschalter
    /// <c>--angriff-halt-alt</c>, Pruefstand <c>--angriff-fahrt-check</c>.</para>
    /// </summary>
    private static bool AngriffFaehrtWeiter(Entity e)
        => !AngriffHaltAlt && e.Ordered && e.Infantry < 0 && e.Weapon != 0 && e.Mobile;

    /// <summary>0x409060 -> 0x40FC90: neuer Weg zur Zielzelle (naechste freie), wenn die
    /// Einheit nicht schon dort steht.</summary>
    private void AngriffNeuRufen(int i, Entity e, Entity t)
    {
        if (_nav == null || e.ChaseRetry > 0) return;
        var goal = _nav.NearestFree(new Vector2I(t.Col, t.Row), e.Move, i);
        if (goal is not { } g || (g.X == e.Col && g.Y == e.Row)) return;
        var weg = WegAbVormerkung(i, e, g);
        if (weg == null || weg.Count == 0) { e.ChaseRetry = 25; return; }
        e.Path = weg; e.PathIdx = 0; e.Goal = g;
        AngriffNeuGerufen++;
    }

    /// <summary>Der Restauftrag endet mit jedem neuen Auftrag: ein neues Auftragsziel, eine
    /// Fahrt woanders hin, eine Schlange. Jeden Takt aus UpdateCombat.</summary>
    private static void GfRestPruefen(Entity e)
    {
        if (e.RestZiel == null) return;
        if (e.Ordered || e.Orders.Count > 0 || e.AngriffsZelle != null
            || (e.Path != null && e.PathIdx < e.Path.Count && e.Goal != e.RestGoal))
            e.RestZiel = null;
    }

    /// <summary><c>--gelegenheitsfeuer-check</c>: die Zaehler als Zeile (fuer Laeufe).</summary>
    public string GelegenheitsfeuerLine()
        => $"gelegenheitsfeuer{(GelegenheitsfeuerAlt ? " (NULLMODELL --gelegenheitsfeuer-alt)" : "")}: "
         + $"Aufnahmen {GfAufnahmen}, Schuesse auf Gelegenheitsziele {GfSchuesse}, "
         + $"Rueckschaltungen aufs Auftragsziel {GfRueckschaltungen}, geraeumt {GfGeraeumt}, "
         + $"Restauftraege {GfRestauftraege}";
}
