using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>ANGRIFF AUF EINE ZELLE — »Strg drücken, dann ballern die auf alles«.</b>
///
/// <para>Gebaut am 31.08.2026 nach dem Fable-Leselauf
/// (<c>berichte/kampagne3-fable.md</c>, Teil 2) und dem Spielerbericht: in
/// Kampagne 3 muss man Bäume zerstören, um zur Nebenmission zu kommen, und im
/// Let's Play wechselt dabei der Mauszeiger vom Wege- auf das Angriffssymbol.
/// </para>
///
/// <para><b>Was das Original tut</b> (in beiden Bauten belegt, die erste
/// Sequenz habe ich selbst nachgelesen):</para>
/// <code>
///   0x43201A   al = byte[0xA182F9]                 ; STRG gehalten?
///   0x43201F   nein                       -> Zeigerart 0
///   0x432023   word[0x4FA0C8] == 0xFFFF   -> Zeigerart 0   ; nichts angewählt
///   0x43202E   dword[0x502AD4] := 2                ; ANGRIFFSZEIGER
///   0x43204F   word[0x502AD8] := Griff unter dem Zeiger
/// </code>
/// <b>Ohne jede Prüfung des Ziels</b> — darum geht es auch auf leeren Boden.
/// Der Klick landet über die Zeigerart im Verteilerarm 2 (<c>0x437417</c>) und
/// setzt <b>Busbefehl 11</b> ab; das Zielfeld <c>UTOK_NA</c> trägt dann nicht
/// eine Einheitennummer, sondern <b>30000 + Spalte</b>, die Zeile daneben.
/// (Gebäude wären 60000er, Brücke/Rampe 40000er.)
///
/// <para>⭐ <b>Ein Baum ist also gar kein Ziel</b>, sondern Bewohner der Zelle:
/// das Waldband der imap (50000…55999) fällt in keine Sonderprüfung des
/// Absenders und landet im Bodenzellen-Zweig. Geschossen wird auf die ZELLE,
/// und was darauf steht, nimmt den Schaden über die schon gebauten
/// Schadensbänder (<see cref="MapEntityLayer.WaldTreffer"/>).</para>
///
/// <para>⚠ <b>UNSERE ABWEICHUNG, und sie ist bewusst.</b> Im Original erscheint
/// der Angriffszeiger bei gehaltenem Strg IMMER. Bei uns ist Strg seit dem
/// 17.08.2026 mit »einnehmen« belegt (Fehler C9 und C11: ohne diese Weiche
/// gewinnt bei einem feindlichen Gebäude immer der Angriff, und die Tür war nie
/// erreichbar). Damit dieser behobene Fehler nicht wieder aufgeht, hat das
/// Einnehmen hier <b>Vortritt</b>: Strg auf ein einnehmbares Gebäude nimmt ein,
/// Strg auf alles andere greift die Zelle an. Die Abweichung sitzt damit genau
/// dort, wo unsere eigene Erfindung ohnehin schon saß.</para>
///
/// <para>⚠ <b>Was NICHT gebaut ist:</b> die Abbruchbedingung des
/// Bodenangriffs im Original (ungelesen, siehe Bericht) — bei uns hört eine
/// Einheit auf, wenn auf der Zelle nichts Beschädigbares mehr steht. Und das
/// Zeigerbild ist unser vorhandenes Angriffssymbol; welche Zeigerart im
/// Original welches Bild zeichnet, ist ebenfalls ungelesen.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary>Das untere Ende des Bodenzellen-Bands aus <c>UTOK_NA</c>
    /// (@0x437500…0x43755B): <c>30000 + Spalte</c>, die Zeile steht daneben.
    /// </summary>
    public const int UtokBodenzelle = 30000;

    /// <summary><c>--kein-bodenangriff</c> — die Gegenprobe.</summary>
    public static bool BodenangriffAn = true;

    /// <summary>Wie oft ein Bodenangriff befohlen, wie oft darauf geschossen
    /// und wie oft er mangels Ziel beendet wurde. ⚠ Ohne die drei Zahlen ist
    /// »der Bodenangriff tut nichts« nicht von »er wurde nie befohlen« zu
    /// unterscheiden (CF.3).</summary>
    public int BodenBefohlen, BodenSchuesse, BodenFertig;

    /// <summary>
    /// <b>Der Kampfarm für ein Bodenziel.</b> Wird aus
    /// <see cref="UpdateCombat"/> gerufen, wenn keine Zieleinheit gesetzt ist.
    /// </summary>
    /// <returns>true, wenn dieser Takt vom Bodenangriff behandelt wurde.</returns>
    private bool BodenKampf(int i, Entity e, float dt)
    {
        if (e.AngriffsZelle is not { } z) return false;
        if (!BodenangriffAn || _nav == null) { e.AngriffsZelle = null; return false; }

        // Steht dort nichts Beschädigbares mehr, ist der Auftrag zu Ende.
        // ⚠ UNSERE SETZUNG — die Abbruchbedingung des Originals ist ungelesen.
        if (!ZelleHatZiel(z.X, z.Y))
        {
            e.AngriffsZelle = null;
            e.AimFacing = -1;
            BodenFertig++;
            return true;
        }

        var w = WeaponOf(e.Weapon);
        var mitte = ZellMitte(z.X, z.Y);
        float dist = Mathf.Max(Mathf.Abs(e.Col - z.X), Mathf.Abs(e.Row - z.Y));
        e.AimFacing = DirToFacing(mitte - e.Pos);

        if (dist <= w.RangeTiles && dist >= RangeMinOf(e))
        {
            e.Path = null;                       // in Reichweite: stehen und feuern
            if (e.Weapon == 0) e.Facing = e.AimFacing;
            if (e.Cooldown <= 0 && HasAmmo(e))
            {
                e.Cooldown = ReloadOf(e);
                if (e.AmmoMax > 0 && !(CheatAmmo && Cheated(e))) e.Ammo--;
                DebugShots++; BodenSchuesse++;
                BeschussZelleZaehlen();          // `--beschuss-check`
                BodenSchuss(i, e, z, mitte, w.Damage);
            }
            return true;
        }

        // Zu weit: hinfahren. (Zu NAH gibt es hier nicht — eine Zelle laeuft
        // nicht weg, und wegzufahren waere unsere Erfindung.)
        if (!e.Mobile) { e.AngriffsZelle = null; return true; }
        if (e.Path == null)
        {
            var ziel = _nav.NearestFree(z, e.Move, i);
            if (ziel == null) { e.AngriffsZelle = null; BodenFertig++; return true; }
            var weg = _nav.FindPath(new Vector2I(e.Col, e.Row), ziel.Value, e.Move, i);
            if (weg == null || weg.Count == 0) { e.AngriffsZelle = null; BodenFertig++; return true; }
            e.Path = weg; e.PathIdx = 0; e.Goal = ziel.Value;
        }
        return true;
    }

    /// <summary>Steht auf der Zelle etwas, worauf zu schiessen sich lohnt?
    /// Wald, ein zerstoerbares Objekt oder eine Einheit.</summary>
    private bool ZelleHatZiel(int col, int row)
    {
        if (IstWaldZelle(col, row) || IstObjektZelle(col, row)) return true;
        int wer = _nav?.OccupantAt(col, row) ?? -1;
        return wer >= 0 && wer < _entities.Count && !_entities[wer].Dead;
    }

    /// <summary>Der Schuss auf eine Zelle. Wie <see cref="Fire"/>, nur ohne
    /// Zieleinheit: das Geschoss traegt <c>Target = -1</c> und schlaegt auf dem
    /// Zielpunkt ein — den Weg dahin kann der Einschlag schon (er ist fuer
    /// Geschosse gebaut, deren Ziel unterwegs stirbt).</summary>
    private void BodenSchuss(int si, Entity shooter, Vector2I zelle, Vector2 mitte, int schaden)
    {
        shooter.FireUntil = _clock + FirePoseSeconds;
        Vector2 dir = (mitte - shooter.Pos).Normalized();
        SchussKlang(shooter);

        int art = Simulation.DesignMath.SoundClass(WeaponRowOf(shooter.Weapon));
        string? flug = FlightKind(art);
        var muendung = ShotOrigin(shooter) + dir * MuzzleReach;

        if (flug == null)
        {
            // Waffe ohne Flugbild: der Treffer sitzt sofort.
            ZellSchaden(zelle.X, zelle.Y, schaden, mitte, art);
            // ⭐ 11.09.2026 — und er trifft auch, was auf der Zelle STEHT
            // (Simulation/FireAt.cs). Art 12 (Blitzschleuder) ohne Buendnisfrage.
            if (!ZellEinschlagAlt) ZellEinschlag(si, zelle.X, zelle.Y, schaden, art);
            return;
        }

        int tempo = Audio.GameSounds.ProjectileSpeed(art);
        float schnell = (tempo > 0 ? tempo : 12) * PxPerProjectileSpeed;

        // ⭐ 11.09.2026 — DIE ZWILLINGSLAFETTE auch hier. Der Zellschuss ist
        // dieselbe Schussroutine 0x40BB00 wie der Schuss auf eine Einheit: ist
        // Feld +0x15 der Geschosstafel gesetzt, zwei Saetze im selben Takt
        // (0x40C35E / 0x40C449), quer versetzt, jeder mit EIGENER Streuung.
        // Siehe Fire. Gegenschalter --zellschuss-ohne-zwilling.
        int zwilling = Audio.GameSounds.TwinOffset(art);
        var quer = new Vector2(-dir.Y, dir.X * 0.5f).Normalized();

        void Anlegen(float seite)
        {
            var start = muendung + quer * seite;
            // Dieselbe Zielstreuung wie beim Schuss auf eine Einheit (0x40C288).
            var ziel = mitte + new Vector2(Simulation.Determinism.Roll(20) - 9,
                                           Simulation.Determinism.Roll(10) - 4);
            _shots.Add(new Projectile
            {
                Pos = start,
                Aim = ziel,
                Target = -1,                       // ⭐ kein Zielgriff — die ZELLE
                Shooter = si, Damage = schaden,
                Facing = DirToFacing(dir), Kind = flug, Art = art,
                Speed = schnell,
                Weite = start.DistanceTo(ziel),
                Scheitel = Scheitelteiler(art) * start.DistanceTo(ziel),
                HoeheStart = ElevOf(shooter.Col, shooter.Row) * 15
                             + Mathf.Max(0, Audio.GameSounds.MuzzleHeight(art)),
                HoeheZiel = ElevOf(zelle.X, zelle.Y) * 15,
            });
        }

        if (zwilling > 0 && !ZellschussOhneZwilling) { Anlegen(zwilling); Anlegen(-zwilling); }
        else Anlegen(0f);
    }

    /// <summary><c>--zellschuss-ohne-zwilling</c> — der Stand vor dem
    /// 11.09.2026: ein Zellschuss legt immer nur EIN Geschoss an.</summary>
    public static bool ZellschussOhneZwilling;

    /// <summary>
    /// <b>Was ein Einschlag der ZELLE antut.</b> Wald und zerstoerbare Objekte
    /// haben ihre gelesenen Baender; eine Einheit auf der Zelle nimmt die
    /// Haelfte ihrer Huelle, wie im Missionsbeschuss.
    ///
    /// <para>⚠ Das gilt fuer JEDEN Einschlag, nicht nur fuer befohlene
    /// Bodenangriffe — im Original macht `Zasah` die Baender an der
    /// getroffenen Zelle, gleich woher der Schuss kam. Ein danebengegangener
    /// Schuss zuendet also auch Wald an. <c>--kein-zellschaden</c> nimmt es
    /// zurueck.</para></summary>
    public static bool ZellSchadenAn = true;

    private void ZellSchaden(int col, int row, int schaden, Vector2 wo, int art)
    {
        ZellWirkung(col, row, schaden);
        string? schlag2 = ImpactKind(art);
        if (schlag2 != null)
            _effects.Add(new Effect { Pos = wo, Kind = schlag2, FrameTime = 0.06f });
        Audio.GameSounds.Explosion(col, row);
    }

    /// <summary>Nur die WIRKUNG am Boden, ohne Klang und Bild — der Einschlag
    /// eines Geschosses bringt beides schon selbst mit.</summary>
    public void ZellWirkung(int col, int row, int schaden)
    {
        if (!ZellSchadenAn) return;
        switch (WaldTreffer(col, row, schaden))
        {
            case Waldfolge.Feuer: BodenWaldFeuer++; break;
            case Waldfolge.Weg:   BodenWaldWeg++;   break;
        }
        if (ObjektTreffer(col, row, schaden)) BodenObjekt++;
    }

    /// <summary>Ist ueberhaupt etwas angewaehlt? Das Original fragt an
    /// derselben Stelle <c>word[0x4FA0C8] != 0xFFFF</c> (@0x432023), bevor es
    /// den Angriffszeiger setzt.</summary>
    public bool HasSelection => _sel.Count > 0;

    /// <summary>Die Mitte einer Zelle in Kartenpunkten — der Zielpunkt eines
    /// Bodenschusses.</summary>
    private Vector2 ZellMitte(int col, int row)
        => CellRect(_ox, _oy, col, row, ElevOf(col, row)).GetCenter();

    /// <summary>Steht auf der Zelle unverbrannter Wald? (imap-Band
    /// 50000…55999, siehe Kartenobjekt.IstWald.)</summary>
    private bool IstWaldZelle(int col, int row)
    {
        foreach (var o in _objDraw)
            if (o.Col == col && o.Row == row && o.IstWald && !o.Abgebrannt) return true;
        return false;
    }

    /// <summary>Steht auf der Zelle ein zerstoerbares Objekt?
    ///
    /// <para>⚠⚠ <b>01.09.2026 — hier fehlte <c>Abgebrannt</c>, und das war der
    /// Grund fuer »wenn ich Einheiten mit Strg befehle wohin zu schiessen,
    /// brechen sie das nicht mehr ab«.</b> Der Waldzweig eine Zeile darueber
    /// fragt <c>!o.Abgebrannt</c>, dieser fragte gar nichts: ein
    /// heruntergebranntes Objekt blieb fuer <see cref="ZelleHatZiel"/> ein
    /// Ziel, und <see cref="ObjektTreffer"/> gibt fuer dasselbe Objekt
    /// <c>false</c> zurueck. Die Einheit schoss also weiter auf etwas, an dem
    /// sie nichts mehr aendern konnte — bis in alle Ewigkeit.</para>
    ///
    /// <para>⚠ Die Abbruchbedingung des Originals bleibt ungelesen; das hier
    /// ist weiterhin UNSERE Setzung, jetzt aber wenigstens dieselbe wie beim
    /// Wald.</para></summary>
    private bool IstObjektZelle(int col, int row)
    {
        var o = ObjektAn(col, row);
        return o != null && !o.Abgebrannt;
    }

    /// <summary>Was der Zellschaden bewirkt hat — Wald angezuendet, Wald weg,
    /// Objekt getroffen.</summary>
    public int BodenWaldFeuer, BodenWaldWeg, BodenObjekt;

    /// <summary>Die Meldezeile. Leer, solange nie befohlen wurde.</summary>
    public string BodenangriffLine()
        => BodenBefohlen == 0 && BodenWaldFeuer == 0 && BodenWaldWeg == 0 ? ""
         : $"bodenangriff: {BodenBefohlen}x befohlen, {BodenSchuesse} Schuesse, "
         + $"{BodenFertig}x mangels Ziel beendet; Wirkung am Boden: "
         + $"{BodenWaldFeuer}x Wald angezuendet, {BodenWaldWeg}x Wald sofort weg, "
         + $"{BodenObjekt}x Objekt getroffen; "
         // ⭐ 01.09.2026 — und die Zahl, an der seine Meldung haengt: wieviele
         // Zellen der Brand dem WEGEGITTER zurueckgegeben hat. Siehe
         // NavGrid.ZelleFreigeben.
         + $"{ZellenFreigegeben} Zellen wieder befahrbar";
}
