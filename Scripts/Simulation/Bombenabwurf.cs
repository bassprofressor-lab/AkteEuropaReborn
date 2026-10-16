namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Text;
using Godot;

/// <summary>
/// ⭐⭐ <b>DER BOMBER WIRFT IM UEBERFLUG, UND DIE BOMBE FAELLT</b> (bug-551, KayelGee
/// 14.10.2026: »Im Original fliegt ein Bomber über das Ziel und die Bombe fällt vom
/// Bomber herunter.«)
///
/// <para><b>Original, gelesen in C und F</b> (<c>test_all_bomber</c> C 0x426A00, der
/// Wurf »bomber shoot« C 0x4273DA / F 0x4265CA):</para>
/// <code>
/// 0x426A25  Munition +0x16 == 0            -> nein
/// 0x426A41  Abklingzeit +0x2B != 0         -> nein
/// 0x426A4F  m_uk +0x11 != 7, uk +0x10 != 7 -> nein          (Anflug, nicht Abdrehen)
/// 0x426A7C  d = sqrt( (40·(X − Zielx))² + (Y − Ziely)² )     ⚠ Y OHNE ·40, wörtlich
/// 0x426AE4  d > 400                        -> nein
/// 0x426AF8  h = alt − 15·Gelaende(X,Y)                      (Feineinheiten)
/// 0x426B2C  P = Lage + (cos dir, sin dir)·h                  (VORAUS in Flugrichtung)
/// 0x426BAB  0x4517A0: Probegeschoss Art +0x2C von (Lage, alt) nach (P, Hoehe 0)
/// 0x426BC4  0x452190 Schritt fuer Schritt, bis es aufschlaegt (hoechstens 1000; > 900 -> nein)
/// 0x426C48  Aufschlagzelle [0x53C930/34] == Flugziel +0x14/+0x15  -> WERFEN
/// 0x4273F3  Munition −1 ; Abklingzeit := Nachladen (6) ; Klang 0x72
/// 0x42741E  derselbe Punkt P, dazu je Achse 20 − rand%40 ; 0x42752B Anleger, Zielhoehe 0
/// </code>
/// <para>Der Bomber wirft also VOR dem Ziel — so weit voraus, dass die sinkende Bombe
/// genau auf die Zielzelle faellt — und fliegt weiter. Abgedreht wird erst, wenn er auf
/// der Zielzelle ankommt (»Over target while attack« 0x424540 -> Wendepunkt, m_uk 6).
/// Die Bombe fliegt mit Tempo 3 Feineinheiten je Takt; der Bomber mit sp (4..10 in den
/// Demodateien DM_1/5/7) — er ist also schneller als seine Bombe, steht ueber dem Ziel,
/// bevor sie aufschlaegt, und die Bombe bleibt sichtbar unter und hinter ihm zurueck.
/// Gezeichnet wird sie in ihrer Hoehe (der Zeichner zieht die Hoehe vom Bild ab), sie
/// faellt also vom Bomber herunter.</para>
///
/// <para><b>Bei uns bis 0.6.9:</b> abgeworfen wurde erst 1,5 Zellen vor dem Ziel, die
/// Bombe flog AUF das Ziel zu (von hinten, nicht voraus), wurde in Bodenhoehe gezeichnet
/// — sie rutschte von der Schattenstelle uebers Gelaende — und der Bomber drehte im selben
/// Takt ab.</para>
///
/// <para>⚠ <b>UNSERE SETZUNGEN:</b> (1) die Flugrichtung ist die Richtung zum Ziel (unser
/// Zielflug fliegt geradlinig darauf zu, ein Feld <c>dir</c> fuehrt er nicht); (2) die
/// Probebahn ist mit unseren Hoehenregeln (<see cref="SchwelleAn"/>, Bodenhoehe der
/// Zellmitte) nachgerechnet, nicht mit 0x4517A0 selbst; (3) das TEMPO der Bombe steht
/// im Massstab der Flugzeuge (<c>AirPxPerSpeed</c>, auch eine Setzung), damit das
/// Verhaeltnis Bomber : Bombe = sp : 3 des Originals erhalten bleibt — im Massstab der
/// Bodengeschosse (50) waere die Bombe schneller als der Bomber und schoesse ihm voraus.
/// Dasselbe gilt fuer das NACHLADEN des Bombers (6 Takte, 0x427410): es zaehlt im selben
/// Lufttakt 1/AirPxPerSpeed s. Gegenschalter <c>--bombe-geschosstempo</c> (beides im Bodentakt).</para>
///
/// <para>Gegenschalter <c>--bombenabwurf-alt</c>: der ganze alte Weg. Pruefstand
/// <c>--bomber-abwurf-check</c> (map_DM_1, kopflos, beendet sich selbst).</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--bombenabwurf-alt</c> — Stand 0.6.9: Wurf 1,5 Zellen vor dem Ziel, auf
    /// das Ziel zu, in Bodenhoehe gezeichnet, Abdrehen sofort.</summary>
    public static bool BombenabwurfAlt;

    /// <summary><c>--bombe-geschosstempo</c> — die Bombe fliegt im Massstab der
    /// Bodengeschosse (Tempo·50 px/s) statt im Massstab der Flugzeuge.</summary>
    public static bool BombeGeschosstempo;

    public static bool BomberAbwurfCheck;

    /// <summary>Der Bombenwurf nach 0x426A00 (Bomber, nicht im Nullmodell).</summary>
    private bool BomberNachOriginal(Special a) => a.Kind == 2 && !BombenabwurfAlt && !LuftschussAlt;

    private sealed class AbwurfSatz
    {
        public int Slot, Stufe;
        public Vector2I Ziel;
        public float AbstandZellen, Startversatz;
        public Vector2I? Einschlag;
        public bool UeberZielVorAufschlag;
        public float Abwurfzeit, Aufschlagzeit;
        public Vector2 ZielPx;
        public float MinAbstandVorAufschlag = float.MaxValue;
        public bool ZielTotVorAufschlag;
        public int Zielgriff = -1;
        /// <summary>Munition NACH dem Wurf — 0 heisst: letzte Bombe, danach fliegt der Bomber
        /// heim (0x4236AE »air S« -> air_back_to_airport), ein Ueberflug ist dann nicht zu erwarten.</summary>
        public int RestMunition = -1;
    }
    private readonly List<AbwurfSatz> _abwuerfe = new();
    public int BomberUeberfluege, BombeProbeZuLang;
    private float BombeTempoPx;

    /// <summary>Ein Takt im Anflug (uk 7 / m_uk 7).</summary>
    private void BomberAnflug(Special a, Entity t, Vector2 aim)
    {
        if (CellAt(aim) is not { } ziel) return;
        if (a.Cooldown <= 0f && a.Ammo > 0 && BombeTrifftZiel(a, aim, ziel, out var punkt))
        {
            // 0x427410: Abklingzeit := 6 Takte. ⚠ SETZUNG »Lufttakt«: unsere Flugzeuge fliegen
            // sp·AirPxPerSpeed px/s, das Original sp Feineinheiten je Takt — ein Lufttakt ist bei
            // uns also 1/AirPxPerSpeed s. In diesem Takt rechnen Bombentempo UND Nachladen, sonst
            // wuerfe ein sp-4-Bomber alle vier Bomben in einem Anflug (gemessen: Abstand 3,91 ->
            // 3,48 Zellen, dann heim) statt einer je Ueberflug. --bombe-geschosstempo = Bodentakt.
            a.Cooldown = LuftnachladenAlt ? AirFireGap
                       : NachladeTakte(a.Kind) * (BombeGeschosstempo ? SimDt : 1f / AirPxPerSpeed);
            if (!(CheatAmmo && Cheated(a))) a.Ammo--;                                     // 0x4273F9
            if (BomberAbwurfCheck)
                _abwuerfe.Add(new AbwurfSatz
                {
                    Slot = a.Slot, Stufe = a.Stufe, Ziel = ziel, Abwurfzeit = _clock,
                    AbstandZellen = new Vector2((a.Pos.X - aim.X) / TileW, (a.Pos.Y - aim.Y) / TileH).Length(),
                    ZielPx = aim, Zielgriff = a.Target, RestMunition = a.Ammo,
                });
            LuftSalve(a, t, punkt, abwurf: true);
        }
        // »Over target while attack« (0x424540): erst AUF der Zielzelle wird abgedreht.
        if (CellAt(a.Pos) is { } hier && hier == ziel || a.Pos.DistanceTo(aim) < 1f)
        {
            a.TurnPoint = AirTurnPoint(a.Pos);
            BomberUeberfluege++;
            foreach (var s in _abwuerfe)
                if (s.Slot == a.Slot && s.Einschlag == null) s.UeberZielVorAufschlag = true;
        }
    }

    /// <summary>Flugrichtung in FEINEINHEITEN (beide Achsen 40 je Zelle): unser Bildvektor
    /// hat die y-Achse gestaucht (TileH 20), also dy·2.</summary>
    private static Vector2 FeinRichtung(Vector2 pxDelta)
    {
        var f = new Vector2(pxDelta.X, pxDelta.Y * 2f);
        return f.LengthSquared() < 0.0001f ? Vector2.Down : f.Normalized();
    }

    /// <summary>Feineinheiten -> Bildpunkte (y halb).</summary>
    private static Vector2 FeinZuPx(Vector2 fein) => new(fein.X, fein.Y * 0.5f);

    /// <summary>test_all_bomber 0x426A00 ab dem Abstandstor: trifft die vorausfallende
    /// Bombe die Zielzelle? <paramref name="punkt"/> ist P (ohne Streuung).</summary>
    private bool BombeTrifftZiel(Special a, Vector2 aim, Vector2I ziel, out Vector2 punkt)
    {
        punkt = aim;
        // 0x426A7C..0x426AE9 — wörtlich, Y ohne ·40
        float dx = (a.Col - ziel.X) * 40f, dy = a.Row - ziel.Y;
        if (Mathf.Sqrt(dx * dx + dy * dy) > 400f) return false;
        // 0x426AF8..0x426B55
        float h = a.Alt - 15f * ElevOf(a.Col, a.Row);
        var u = FeinRichtung(aim - a.Pos);
        punkt = a.Pos + FeinZuPx(u * h);
        // 0x426BAB..0x426BF5: die Probebahn, Tempo der Art in Feineinheiten je Takt
        int tempo = Mathf.Max(1, Audio.GameSounds.ProjectileSpeed(ArtBombe));
        int schritte = h <= 0f ? 0 : Mathf.CeilToInt(h / tempo);
        if (schritte > 900) { BombeProbeZuLang++; return false; }
        Vector2I? auf = null;
        for (int k = 1; k <= schritte && auf == null; k++)
        {
            float f = Mathf.Min(1f, k * tempo / h);
            var pos = a.Pos + FeinZuPx(u * (h * f));
            float hoehe = Mathf.Lerp(a.Alt, 0f, f);
            if (CellAt(pos) is not { } z) continue;
            int schwelle = SchwelleAn(z.X, z.Y);
            float boden = BodenHoehe(z.X, z.Y);
            if (schwelle == 0 ? hoehe <= boden : hoehe < boden + schwelle) auf = z;
        }
        auf ??= CellAt(punkt);
        return auf is { } aa && aa == ziel;                                // 0x426C48..0x426C6B
    }

    /// <summary>Der Wurf selbst (0x42741E..0x42752B): von der Lage des Bombers in seiner
    /// Hoehe nach P ± Streuung auf Hoehe 0.</summary>
    private void BombeAnlegen(Special a, int art, string? flug, int tempo, Vector2 punkt)
    {
        var streu = new Vector2(20 - Simulation.Determinism.Roll(40), 20 - Simulation.Determinism.Roll(40));
        var ziel = punkt + FeinZuPx(streu);
        var px = ziel - a.Pos;
        float fein = new Vector2(px.X, px.Y * 2f).Length();
        // Tempo = Feineinheiten je Takt; auf der Bildbahn umgerechnet (y gestaucht).
        float massstab = BombeGeschosstempo ? PxPerProjectileSpeed : AirPxPerSpeed;
        float schnell = (tempo > 0 ? tempo : 3) * massstab * (fein > 0.01f ? px.Length() / fein : 1f);
        _shots.Add(new Projectile
        {
            Pos = a.Pos, Aim = ziel, Target = -1, Shooter = -1,
            Damage = a.Attack,
            Facing = DirToFacing(px.LengthSquared() > 0.0001f ? px : Vector2.Down),
            Kind = flug, Art = art,
            Speed = LuftTempoPx = BombeTempoPx = schnell,
            Weite = px.Length(), Scheitel = 0f,
            HoeheStart = a.Alt, HoeheZiel = 0,
            Fallend = true,
            FallBoden0 = a.Alt - AirHoehePx(a),          // der Boden unter dem Bomberbild
            FallBoden1 = CellAt(ziel) is { } zz ? BodenHoehe(zz.X, zz.Y) : 0f,
            AbwurfNr = BomberAbwurfCheck ? _abwuerfe.Count : 0,
        });
        LuftGeschosse++;
        if (BomberAbwurfCheck && _abwuerfe.Count > 0)
            _abwuerfe[^1].Startversatz = Mathf.Abs(AirHoehePx(a) - BombeZeichenHoehe(_shots[^1]));
    }

    /// <summary>Wie hoch ueber ihrer Bodenspur die Bombe gezeichnet wird: Flughoehe minus
    /// Boden darunter (wie AirHoehePx beim Flugzeug). 0 fuer alles andere.</summary>
    private float BombeZeichenHoehe(Projectile p)
    {
        if (!p.Fallend) return 0f;
        // der Boden unter der Bahn, linear vom Bomber zum Ziel — so steht die Bombe im ersten
        // Bild genau am Bomber (AirHoehePx) und im letzten am Boden.
        float t = p.Weite <= 1f ? 1f : Mathf.Clamp(1f - p.Pos.DistanceTo(p.Aim) / p.Weite, 0f, 1f);
        return Mathf.Max(0f, GeschossHoehe(p) - Mathf.Lerp(p.FallBoden0, p.FallBoden1, t));
    }

    /// <summary>Nullmodell-Messung: der alte Wurf (LuftSalve ohne Abwurf) bekommt denselben
    /// Satz, damit die Pruefzeile beide Wege mit denselben Zahlen beschreibt.</summary>
    private int AlterWurfSatz(Special a, Vector2 aim)
    {
        if (!BomberAbwurfCheck || a.Kind != 2 || CellAt(aim) is not { } ziel) return 0;
        _abwuerfe.Add(new AbwurfSatz
        {
            Slot = a.Slot, Stufe = a.Stufe, Ziel = ziel, Abwurfzeit = _clock,
            AbstandZellen = new Vector2((a.Pos.X - aim.X) / TileW, (a.Pos.Y - aim.Y) / TileH).Length(),
            Startversatz = AirHoehePx(a),          // die alte Bombe steht in Bodenhoehe
            ZielPx = aim, Zielgriff = a.Target, RestMunition = System.Math.Max(0, a.Ammo - 1),
        });
        return _abwuerfe.Count;
    }

    private void BombeAufschlag(Projectile p)
    {
        if (p.AbwurfNr <= 0 || p.AbwurfNr > _abwuerfe.Count) return;
        var s = _abwuerfe[p.AbwurfNr - 1];
        s.Einschlag = CellAt(p.Aim);
        s.Aufschlagzeit = _clock;
    }

    // ---- --bomber-abwurf-check ---------------------------------------------------
    private bool _bacGestartet, _bacFertig;

    /// <summary>
    /// <c>--bomber-abwurf-check</c> (map_DM_1: zehn Bomber, alle Seiten von der KI
    /// gespielt). Nach 150 s Spielzeit je Abwurf: Abstand Bomber -> Zielzelle beim
    /// Abwurf (Soll ≈ h/40 = 3,4 Zellen bei Hoehe 135 ueber Stufe 0, sicher &gt; 1,5),
    /// Aufschlagzelle gegen Zielzelle (Soll gleich oder Nachbar — Streuung ±20 Fein), war
    /// der Bomber vor dem Aufschlag ueber dem Ziel (Soll ja), Startversatz Bombe gegen
    /// Bomberbild (Soll 0 px). Nullmodell <c>--bombenabwurf-alt</c>: Abstand ≤ 1,5,
    /// nie ueber dem Ziel, Startversatz = Flughoehe.
    /// </summary>
    public void BomberAbwurfTakt()
    {
        if (!BomberAbwurfCheck || _bacFertig) return;
        if (!_bacGestartet && _clock > 0.5f)
        {
            _bacGestartet = true;
            // die KI laeuft schon (--skirmish=map_DM_1,...)
        }
        foreach (var s in _abwuerfe)
        {
            if (s.Einschlag != null) continue;
            var a = _special.Find(x => x.Slot == s.Slot && !x.Dead);
            if (a != null)
                s.MinAbstandVorAufschlag = Mathf.Min(s.MinAbstandVorAufschlag,
                    new Vector2((a.Pos.X - s.ZielPx.X) / TileW, (a.Pos.Y - s.ZielPx.Y) / TileH).Length());
            if (s.Zielgriff >= 0 && s.Zielgriff < _entities.Count && _entities[s.Zielgriff].Dead) s.ZielTotVorAufschlag = true;
        }
        if (_clock < 150f) return;
        _bacFertig = true;
        var sb = new StringBuilder("bomber-abwurf-check:" + (BombenabwurfAlt ? "  ⚠ NULLMODELL --bombenabwurf-alt" : ""));
        int n = _abwuerfe.Count, gleich = 0, nachbar = 0, offen = 0, ueber = 0, mitRest = 0, totVorher = 0;
        float sum = 0f, min = float.MaxValue, max = 0f, versatz = 0f;
        foreach (var s in _abwuerfe)
        {
            sum += s.AbstandZellen; min = Mathf.Min(min, s.AbstandZellen); max = Mathf.Max(max, s.AbstandZellen);
            versatz = Mathf.Max(versatz, s.Startversatz);
            if (s.Einschlag is not { } e) { offen++; continue; }
            int d = Mathf.Max(Mathf.Abs(e.X - s.Ziel.X), Mathf.Abs(e.Y - s.Ziel.Y));
            if (d == 0) gleich++; else if (d == 1) nachbar++;
            if (s.RestMunition > 0 && !s.ZielTotVorAufschlag)
            {
                mitRest++;
                if (s.MinAbstandVorAufschlag < 1f) ueber++;   // auf der Zielzelle (Zelle 40x20, Zaehlung in Zellen)
            }
            if (s.ZielTotVorAufschlag) totVorher++;
        }
        int fertig = n - offen;
        sb.Append($"\n   Abwuerfe {n}, aufgeschlagen {fertig}, Ueberfluege {BomberUeberfluege}, Probebahn zu lang {BombeProbeZuLang}");
        if (n > 0)
            sb.Append($"\n   Abstand Bomber->Zielzelle beim Abwurf: Mittel {sum / n:0.00}, {min:0.00}..{max:0.00} Zellen (Soll > 1,5, ≈ 3,4 auf Stufe 0)" +
                      $"\n   Aufschlag auf der Zielzelle {gleich}, Nachbarzelle {nachbar}, weiter weg {fertig - gleich - nachbar} (Soll weiter weg 0)" +
                      $"\n   Bomber vor dem Aufschlag ueber dem Ziel (< 1 Zelle): {ueber}/{mitRest} der Wuerfe mit Restmunition und lebendem Ziel (Soll alle; letzte Bombe -> heim 0x4236AE, Ziel tot -> heim 0x4246A2: {fertig - mitRest})" +
                      $"\n   Startversatz Bombe gegen Bomberbild: hoechstens {versatz:0.0} px (Soll 0)" +
                      $"\n   Tempo Bombe {BombeTempoPx:0} px/s" + (BombeGeschosstempo ? " (⚠ --bombe-geschosstempo)" : ""));
        for (int k = 0; k < System.Math.Min(12, n); k++)
        {
            var s = _abwuerfe[k];
            sb.Append($"\n     #{k + 1} Bomber {s.Slot} sp {s.Stufe}: Abstand {s.AbstandZellen:0.00}, Ziel ({s.Ziel.X},{s.Ziel.Y}), " +
                      $"Aufschlag {(s.Einschlag is { } e ? $"({e.X},{e.Y}) nach {s.Aufschlagzeit - s.Abwurfzeit:0.00} s" : "offen")}, " +
                      $"Bomber dem Ziel am naechsten vor dem Aufschlag {s.MinAbstandVorAufschlag:0.00} Zellen, Restmunition {s.RestMunition}{(s.ZielTotVorAufschlag ? ", Ziel vorher tot" : "")}");
        }
        bool ok = n > 0 && fertig > 0 && fertig - gleich - nachbar == 0 && mitRest > 0 && ueber == mitRest && versatz < 0.5f;
        sb.Append(ok ? "\n   BESTANDEN" : "\n   DURCHGEFALLEN");
        GD.Print(sb.ToString());
        GetTree().Quit();
    }
}
