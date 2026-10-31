namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Text;
using Godot;

/// <summary>
/// ⭐ 30.10.2026, bug-695 — <b>DIE PIONIERBRUECKE LIEGT EINE STUFE HOEHER, MIT GELAENDERSTEMPEL.</b>
/// Spielerentscheidung 30.10.: der grosse Punkt kommt vor 0.6.13
/// (berichte/pruefung-rest-b-opus.md Punkt 4, Bau berichte/bau-pionierbruecke.md).
///
/// <para><b>GELESEN, C und F</b> (Brueckenbau <c>0x4CC280</c>, F <c>0x4CBE30</c>):</para>
/// <list type="number">
/// <item><b>Zellhoehe +1 der Fahrbahn ueber dem Wasser</b>, je Mittelzelle p = 1…k:
///   senkrecht <c>Hoehe(x0+1, y0+p) := Hoehe(x0+1, y0+p) + 1</c> (C <c>0x4CC63D</c>→<c>0x41D0E0</c>,
///   <c>0x4CC64A</c>→<c>0x41D170</c>; F <c>0x4CC1E7</c>/<c>0x4CC1F4</c>), waagerecht
///   <c>Hoehe(x0+p, y0+1) := Hoehe(x0+p, y0) + 1</c> — gelesen wird die OBERE Randzelle
///   (C <c>0x4CC9C0</c>/<c>0x4CC9D1</c>, F <c>0x4CC56A</c>/<c>0x4CC57B</c>).</item>
/// <item><b>Brueckenbyte +3 der Fahrbahnkoepfe</b>: senkrecht <c>(x0+1, y0) := 2</c>,
///   <c>(x0+1, y0+k+1) := 4</c> (C <c>0x4CC6DC</c>, Sprung nach <c>0x4CCA6E</c>); waagerecht
///   <c>(x0, y0+1) := 1</c>, <c>(x0+k+1, y0+1) := 3</c> (C <c>0x4CCA5C</c>/<c>0x4CCA6E</c>,
///   F <c>0x4CC606</c>/<c>0x4CC618</c>), alles ueber <c>0x41D1A0</c> (F <c>0x41C360</c>).</item>
/// <item><b>Der Gelaenderstempel</b> <c>0x421F90</c> @<c>0x4CCA8A</c> (F <c>0x421150</c>
///   @<c>0x4CC634</c>), gleich nach <c>+0x12 := 1</c>, <c>+0x16 := 500</c>.</item>
/// <item>Lage (sec20 = 100 + i), Kachel und Suchkarte (sec6 <c>0xFFFE</c>/<c>0xFFFF</c>)
///   aendern sich dadurch NICHT — die standen schon seit 0.6.3 richtig.</item>
/// <item><b>Die Einheiten</b> fragen nichts Brueckeneigenes: der Einheitenzeichner liest die
///   Zellhoehe der Ankerzelle (C <c>0x42C841</c> → <c>(9 − h)·15</c>, F <c>0x42BA23</c>
///   gleich) — wer auf der gehobenen Fahrbahn steht, sitzt 15 Punkte hoeher, auf dem Kopf
///   mit dem Hanganteil der Art 1…4. Bei uns ist das <see cref="HubOf"/> aus
///   <see cref="ElevOf"/> und <see cref="HangArt"/>.</item>
/// <item><b>Der Abriss</b> <c>0x4CB0A0</c> setzt die Hoehe der Mittelzellen auf die des
///   Kopfes zurueck (C <c>0x4CB320</c>/<c>0x4CB333</c>, F <c>0x4CAF03</c>/<c>0x4CB275</c>) und das
///   Byte +3 aller Satzzellen auf 0 (schon seit bug-643, <c>BrueckenbyteLoeschen</c>).</item>
/// </list>
///
/// <para><b>Daten als Nullmodell:</b> alle <b>74</b> Bruecken der 33 Kampagnenkarten
/// tragen genau diese Form — Mitte = Kopf + 1 = Rand + 1 (74/74), Kopfbyte waagerecht (1, 3)
/// 42/42, senkrecht (2, 4) 32/32, Mittelbyte 0 (74/74). Waere die Anhebung ein Fehler der
/// Lesung, staende in den Karten eine flache Mitte.</para>
///
/// <para>⚠ <b>UNSERE SETZUNGEN:</b></para>
/// <list type="bullet">
/// <item>Die Hoehe steht bei uns DREIMAL: Zeichenhoehe <c>_elevLookup</c> (Zeichnen, Sicht,
///   Geschosse), Gitterhoehe <c>NavGrid._elev</c> (unsere Steig- und Wegkosten) und die beim
///   Laden festgehaltene Nebelhoehe <c>_fogElev</c>. Alle drei werden geschrieben, damit die
///   Pionierbruecke sich verhaelt wie eine Kartenbruecke, deren Hoehe aus der Datei kommt.</item>
/// <item>Das Byte +3 steht bei uns ZWEIMAL: <c>NavGrid._flag</c> (Nebelmaske, Gleiter,
///   Bertha) und die Posentafel <c>_flagLookup</c>. Die Koepfe gehen in beide; der Stempel
///   wie beim Kartenladen nur ins Gitter (auf einem Gelaender steht nie eine Einheit).</item>
/// <item><c>0x421F90</c> stempelt ALLE belegten Saetze neu; wir stempeln nur den neuen —
///   die uebrigen tragen schon dieselben Werte, der Stempel ist wiederholbar.</item>
/// <item>Die Nebelhoehe wird nur bis zur hoechsten Stufe beim Laden geschrieben (das
///   Nebelrechteck ist dafuer bemessen); eine Fahrbahn darueber bleibt dort stehen.</item>
/// </list>
///
/// <para>Gegenschalter <c>--pionierbruecke-flach-alt</c> (Stand 0.6.3…0.6.12: keine Anhebung,
/// keine Kopfbytes, kein Stempel). Messschalter <c>--pionierbruecke-nur-stempel</c> (Stempel
/// und Kopfbytes ohne Anhebung — die »doppelte Abdunklung« der Lesung) und
/// <c>--pionierbruecke-abriss-alt</c> (Abriss ohne die Rueckstellung von Gitter-/Nebelhoehe
/// und Posentafel). Pruefstaende <c>--pionierbruecke-check</c> (kopflos, K9) und
/// <c>--bruecken-nebel-check</c> (zaehlt jetzt auch Pionierbruecken).</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--pionierbruecke-flach-alt</c> — Nullmodell zu bug-695.</summary>
    public static bool PionierbrueckeFlachAlt;

    /// <summary><c>--pionierbruecke-nur-stempel</c> — Messschalter: Kopfbytes und Stempel, die
    /// Fahrbahn bleibt flach.</summary>
    public static bool PionierbrueckeNurStempel;

    /// <summary><c>--pionierbruecke-abriss-alt</c> — Messschalter: der Abriss stellt nur die
    /// Zeichenhoehe zurueck (wie vor bug-695 fuer alle Bruecken).</summary>
    public static bool PionierbrueckeAbrissAlt;

    /// <summary><c>--kartenbruecke-abriss-alt</c> — Nullmodell zu bug-700: der Abriss einer
    /// Kartenbruecke stellt nur die Zeichenhoehe zurueck (Stand bis 30.10.2026).</summary>
    public static bool KartenbrueckeAbrissAlt;

    /// <summary>Fuer die Pruefstaende: die festgehaltene Nebelhoehe einer Zelle (−1 = kein Nebel).</summary>
    private int NebelhoeheFuerProbe(int c, int r)
        => _fog != null && c >= 0 && r >= 0 && c < _fog.Width && r < _fog.Height
           && _fogElev.Length == _fog.Width * _fog.Height ? _fogElev[r * _fog.Width + c] : -1;

    /// <summary>Wurde die Bruecke beim Bau angehoben (bug-695)?</summary>
    private static bool Angehoben(Steg s) => !s.Karte && s.Vorher != null;

    /// <summary>Eine Zellhoehe an allen drei Stellen schreiben (siehe Kopf, Setzung 1).</summary>
    private void ZellhoeheSetzen(int c, int r, int h)
    {
        h &= 0xFF;                                  // 0x41D170 schreibt ein Byte
        _elevLookup[(c, r)] = h;
        _nav?.HoeheSetzen(c, r, h);
        if (_fog != null && c >= 0 && r >= 0 && c < _fog.Width && r < _fog.Height
            && _fogElev.Length == _fog.Width * _fog.Height && h <= _fogElevMax)
        {
            _fogElev[r * _fog.Width + c] = h;
            _fogDrawn = -1;
        }
    }

    /// <summary>Das Byte +3 eines Fahrbahnkopfs — Gitter und Posentafel.</summary>
    private void KopfbyteSetzen(int c, int r, int v)
    {
        _nav?.FlagSetzen(c, r, v);
        if (_flagLookup != null) _flagLookup[(c, r)] = v;
    }

    /// <summary>Die beiden Fahrbahnkoepfe einer Bruecke (Anfang, Ende ab der oberen linken Ecke).</summary>
    private static (Vector2I A, Vector2I B) Fahrbahnkoepfe(Steg s)
    {
        var e = BrueckenEcke(s);
        int k = s.Wasser;
        return IstSenkrecht(s)
            ? (new Vector2I(e.X + 1, e.Y), new Vector2I(e.X + 1, e.Y + k + 1))
            : (new Vector2I(e.X, e.Y + 1), new Vector2I(e.X + k + 1, e.Y + 1));
    }

    /// <summary>Eine Mittelzelle p (1…k) und die Zelle, deren Hoehe das Original dafuer liest.</summary>
    private static (Vector2I Ziel, Vector2I Quelle) Mittelzelle(Steg s, int p)
    {
        var e = BrueckenEcke(s);
        return IstSenkrecht(s)
            ? (new Vector2I(e.X + 1, e.Y + p), new Vector2I(e.X + 1, e.Y + p))
            : (new Vector2I(e.X + p, e.Y + 1), new Vector2I(e.X + p, e.Y));   // obere Randzelle
    }

    /// <summary>Das Ende von <c>0x4CC280</c> fuer die Pionierbruecke — siehe Kopf. Gerufen von
    /// <see cref="BrueckeAnlegen"/>, nachdem sec6/sec20 aller Satzzellen stehen.</summary>
    private void PionierbrueckeAnheben(Steg s)
    {
        if (_nav == null || s.Karte || PionierbrueckeFlachAlt) return;
        s.Vorher = new();
        foreach (var (z, _, _) in s.Zellen)
            s.Vorher.Add((z, ElevOf(z.X, z.Y), _nav.ElevAt(z.X, z.Y), _nav.FlagAt(z.X, z.Y), HangArt(z.X, z.Y)));
        // 0x4CC63D/0x4CC9C0: Hoehe +1 je Mittelzelle.
        if (!PionierbrueckeNurStempel)
            for (int p = 1; p <= s.Wasser; p++)
            {
                var (ziel, quelle) = Mittelzelle(s, p);
                ZellhoeheSetzen(ziel.X, ziel.Y, ElevOf(quelle.X, quelle.Y) + 1);
            }
        // 0x4CC6DC/0x4CCA5C/0x4CCA6E: die Koepfe.
        bool senk = IstSenkrecht(s);
        var (a, b) = Fahrbahnkoepfe(s);
        KopfbyteSetzen(a.X, a.Y, senk ? 2 : 1);
        KopfbyteSetzen(b.X, b.Y, senk ? 4 : 3);
        // 0x4CCA8A: der Gelaenderstempel.
        var ecke = BrueckenEcke(s);
        s.Gestempelt = _nav.GelaenderStempeln(ecke.X, ecke.Y, senk, s.Wasser);
        _fogDrawn = -1;
    }

    /// <summary>Der Rest von <c>0x4CB0A0</c> fuer eine angehobene Pionierbruecke: die
    /// Zeichenhoehe stellt <see cref="BrueckeAbreissen"/> selbst zurueck (seit 13.09.), hier
    /// kommen Gitter- und Nebelhoehe und die Posentafel dazu (Byte +3 := 0).</summary>
    /// <para>⭐ bug-700 (30.10.2026): dasselbe fuer die KARTENbruecke — ihre gehobene Mitte kommt
    /// aus der Datei und stand nach dem Abriss bis dahin in Gitter- und Nebelhoehe weiter auf +1,
    /// die Koepfe in der Posentafel auf 1…4 (Original C <c>0x4CB333</c>, F <c>0x4CAF03</c>/<c>0x4CB275</c>:
    /// EINE Zellhoehe, Byte +3 := 0). Gegenschalter <c>--kartenbruecke-abriss-alt</c>.</para>
    private void PionierbrueckeAbsenken(Steg s, int kopfHoehe)
    {
        if (s.Karte ? KartenbrueckeAbrissAlt : !Angehoben(s) || PionierbrueckeAbrissAlt) return;
        for (int p = 1; p <= s.Wasser; p++)
        {
            var (ziel, _) = Mittelzelle(s, p);
            ZellhoeheSetzen(ziel.X, ziel.Y, kopfHoehe);
        }
        if (_flagLookup != null)
            foreach (var (z, _, _) in s.Zellen) _flagLookup[(z.X, z.Y)] = 0;
        _fogDrawn = -1;
    }

    /// <summary><c>--bauwerke-unzerstoerbar</c>-Weg (<see cref="BrueckeTrifft"/>): alles genau so
    /// zurueck, wie es vor dem Bau stand.</summary>
    private void PionierbrueckeZuruecksetzen(Steg s)
    {
        if (!Angehoben(s) || _nav == null) return;
        foreach (var (z, elev, navElev, flag, pose) in s.Vorher!)
        {
            ZellhoeheSetzen(z.X, z.Y, elev);
            _nav.HoeheSetzen(z.X, z.Y, navElev);
            _nav.FlagSetzen(z.X, z.Y, flag);
            if (_flagLookup != null) _flagLookup[(z.X, z.Y)] = pose;
        }
    }

    // ---- --pionierbruecke-check ----------------------------------------------------

    /// <summary><c>--pionierbruecke-check</c> (kopflos, <c>-- --campaign=9</c>): eine
    /// Pionierbruecke anlegen (EINGRIFF, wie <c>--bruecke-check</c>), dann (1) Hoehe der
    /// Fahrbahn, Kopfbytes, Stempel; (2) ein Fahrzeug faehrt darueber, seine Anhebung auf der
    /// Mitte; (3) der Nebelschleier (unverdeckte Zeilen, Soll 0); (4) Abriss: Hoehen, Bytes,
    /// Posentafel, Satz, Lage — nichts bleibt. Nullmodell <c>--pionierbruecke-flach-alt</c>
    /// (1 faellt), Messschalter <c>--pionierbruecke-abriss-alt</c> (4 faellt).</summary>
    public string PionierbrueckeCheck()
    {
        var sb = new StringBuilder("pionierbruecke-check"
            + (PionierbrueckeFlachAlt ? " — NULLMODELL --pionierbruecke-flach-alt" : "")
            + (PionierbrueckeNurStempel ? " — MESSSCHALTER --pionierbruecke-nur-stempel" : "")
            + (PionierbrueckeAbrissAlt ? " — MESSSCHALTER --pionierbruecke-abriss-alt" : "") + "\n");
        if (_nav == null) return sb.Append("  keine Karte").ToString();
        bool ok = true;
        void Soll(bool b, string was) { sb.Append($"  {(b ? "ok  " : "⚠ FALSCH")} {was}\n"); ok &= b; }

        // Ein Fahrzeug des Betrachters mit Rumpf 1.
        int v = -1;
        for (int i = 0; i < _entities.Count && v < 0; i++)
        {
            var o = _entities[i];
            if (!o.Dead && !o.IsBuilding && !o.IsProp && o.Mobile && !Untergestellt(o) && o.Owner == ViewPlayer
                && o.Infantry < 0 && (o.Move == Simulation.NavGrid.MoveClass.Vehicle || o.Move == Simulation.NavGrid.MoveClass.Walker)
                && _nav.HullOf(i) == 1) v = i;
        }
        if (v < 0) return sb.Append("  kein Fahrzeug des Betrachters — NICHT GEMESSEN").ToString();
        var V = _entities[v];

        // Bauplatz: gueltig, zwei freie Anfahrtsfelder davor, ein freies Zielfeld dahinter;
        // weit weg von fremden Waffen (wie --bruecke-baustelle-check), sonst nah am Fahrzeug.
        var feinde = new List<Vector2I>();
        foreach (var f in _entities)
            if (!f.Dead && !f.IsProp && f.Owner != ViewPlayer && f.Weapon != 0) feinde.Add(new Vector2I(f.Col, f.Row));
        bool Frei(Vector2I z) => _nav.InBounds(z.X, z.Y) && _nav.CanEnter(z.X, z.Y, V.Move) && _nav.OccupantAt(z.X, z.Y) < 0;
        Vector2I P = new(-1, -1), T0 = default, Z = default;
        int best = int.MaxValue, feindAbstand = 0;
        for (int r = 0; r < _nav.Height; r++)
            for (int c = 0; c < _nav.Width; c++)
            {
                if (!BrueckePlatzOk(c, r)) continue;
                int d = BrueckenRichtung(c, r), k = BrueckenWeite(c, r, d);
                var l = BrueckenLauf[d]; var pz = new Vector2I(c, r);
                var t0 = pz - l * 2; var zz = pz + l * (k + 2);
                if (!Frei(t0) || !Frei(pz - l) || !Frei(zz)) continue;
                int feind = int.MaxValue;
                foreach (var f in feinde) feind = System.Math.Min(feind, System.Math.Abs(c - f.X) + System.Math.Abs(r - f.Y));
                int dist = (feind >= 20 ? 0 : 100000 - feind * 1000) + System.Math.Abs(c - V.Col) + System.Math.Abs(r - V.Row);
                if (dist < best) { best = dist; P = pz; T0 = t0; Z = zz; feindAbstand = feind; }
            }
        if (P.X < 0) return sb.Append("  kein geeigneter Bauplatz — NICHT GEMESSEN").ToString();

        // Vorher festhalten (Rechteck aus Richtung und Weite, so wie BrueckeAnlegen es baut).
        int dir = BrueckenRichtung(P.X, P.Y), kw = BrueckenWeite(P.X, P.Y, dir);
        var vorher = new Dictionary<Vector2I, (int Elev, int Nav, int Flag, int Pose)>();
        foreach (var (z, _) in BrueckenZellen(P.X, P.Y, dir, kw))
            vorher[z] = (ElevOf(z.X, z.Y), _nav.ElevAt(z.X, z.Y), _nav.FlagAt(z.X, z.Y), HangArt(z.X, z.Y));
        sb.AppendLine($"  ⚠ EINGRIFF: Bruecke direkt angelegt bei ({P.X},{P.Y}) (Richtung {dir}, {kw} Wasserzellen, "
                    + $"naechste fremde Waffe {feindAbstand} Zellen); Fahrzeug Platz {V.Slot} ({LabelOf(V)}) versetzt");
        if (!BrueckeAnlegen(-1, P.X, P.Y)) return sb.Append($"  Bau abgelehnt: {BrueckeNote} — NICHT GEMESSEN").ToString();
        var st = _stege[^1];
        bool senk = IstSenkrecht(st);

        // 1) Hoehe, Kopfbytes, Stempel.
        int hochOk = 0, navOk = 0, nebelOk = 0, hubOk = 0;
        var hoehen = new List<string>();
        for (int p = 1; p <= st.Wasser; p++)
        {
            var (ziel, quelle) = Mittelzelle(st, p);
            int soll = vorher[quelle].Elev + 1;
            int fogH = _fog != null && _fogElev.Length == _fog.Width * _fog.Height ? _fogElev[ziel.Y * _fog.Width + ziel.X] : -1;
            if (ElevOf(ziel.X, ziel.Y) == soll) hochOk++;
            if (_nav.ElevAt(ziel.X, ziel.Y) == vorher[ziel].Nav + 1) navOk++;
            if (fogH == soll) nebelOk++;
            if (HubOf(ziel.X, ziel.Y) == Simulation.Hang.HubStehend(soll, 0)) hubOk++;
            hoehen.Add($"({ziel.X},{ziel.Y}) {vorher[ziel].Elev}->{ElevOf(ziel.X, ziel.Y)} Hub {HubOf(ziel.X, ziel.Y)}");
        }
        sb.AppendLine($"  1. Fahrbahn: {string.Join(", ", hoehen)}");
        Soll(hochOk == st.Wasser, $"Zeichenhoehe der {st.Wasser} Mittelzellen = Quelle + 1 ({hochOk}/{st.Wasser})");
        Soll(navOk == st.Wasser, $"Gitterhoehe +1 ({navOk}/{st.Wasser})");
        Soll(nebelOk == st.Wasser, $"Nebelhoehe +1 ({nebelOk}/{st.Wasser})");
        Soll(hubOk == st.Wasser, $"Anhebung einer stehenden Einheit = (Hoehe+1)·15 ({hubOk}/{st.Wasser})");
        var (ka, kb) = Fahrbahnkoepfe(st);
        int sa = senk ? 2 : 1, sbb = senk ? 4 : 3;
        Soll(_nav.FlagAt(ka.X, ka.Y) == sa && _nav.FlagAt(kb.X, kb.Y) == sbb && HangArt(ka.X, ka.Y) == sa && HangArt(kb.X, kb.Y) == sbb,
             $"Kopfbytes ({ka.X},{ka.Y})={_nav.FlagAt(ka.X, ka.Y)}/{HangArt(ka.X, ka.Y)} ({kb.X},{kb.Y})={_nav.FlagAt(kb.X, kb.Y)}/{HangArt(kb.X, kb.Y)} "
             + $"(Soll {sa}/{sbb}, Gitter/Posentafel; vorher {vorher[ka].Flag}/{vorher[kb].Flag})");
        int randGestempelt = 0;
        foreach (var (z, mitte, _) in st.Zellen) if (!mitte && _nav.FlagAt(z.X, z.Y) != 0) randGestempelt++;
        Soll(randGestempelt == 2 * (st.Wasser + 2), $"Gelaenderstempel: {randGestempelt} Randzellen mit Byte != 0 (Soll {2 * (st.Wasser + 2)})");

        // 2) Ein Fahrzeug faehrt hinueber.
        P3Versetze(v, T0);
        int fahrt = 0, aufMitte = 0, hubMitte = -1;
        if (MoleGehZu(v, Z.X, Z.Y))
            while (fahrt < 1500 && !V.Dead && !(V.Col == Z.X && V.Row == Z.Y))
            {
                SimTickFuerProbe(); fahrt++;
                foreach (var (z, mitte, _) in st.Zellen)
                    if (mitte && z.X == V.Col && z.Y == V.Row && z != ka && z != kb)
                    { aufMitte++; hubMitte = System.Math.Max(hubMitte, HubOf(V.Col, V.Row)); }
            }
        bool angekommen = V.Col == Z.X && V.Row == Z.Y;
        Soll(angekommen, $"2. Fahrzeug ({T0.X},{T0.Y}) -> ({Z.X},{Z.Y}): "
                       + (angekommen ? $"angekommen nach {fahrt} Takten" : $"NICHT angekommen ({fahrt} Takte, steht auf ({V.Col},{V.Row}))")
                       + $", {aufMitte} Takte auf der Mitte, groesste Anhebung dort {hubMitte} px");

        // 3) Der Nebelschleier.
        if (_nebelDeckung == null) sb.AppendLine("  3. keine Nebelmasken geladen — NICHT GEMESSEN");
        else
        {
            int luecke = NebelLueckeZeilen(st, out int spalten, out int doppelt);
            Soll(luecke == 0, $"3. Nebel: {luecke} unverdeckte Zeilen in {spalten} Spalten (Soll 0); doppelt gedeckte Punkte {doppelt}");
        }

        // 4) Der Abriss (EINGRIFF: direkt, der Trefferweg steht in --bruecke-treffer-check).
        int kopfH = vorher[ka].Elev;
        BrueckeAbreissen(st);
        int hoeheZurueck = 0, navZurueck = 0, nebelZurueck = 0, byteNull = 0, poseNull = 0, lageLeer = 0, satzWeg = 0;
        foreach (var (z, mitte, _) in st.Zellen)
        {
            var w = vorher[z];
            if (ElevOf(z.X, z.Y) == w.Elev) hoeheZurueck++;
            if (_nav.ElevAt(z.X, z.Y) == w.Nav) navZurueck++;
            int fogH = _fog != null && _fogElev.Length == _fog.Width * _fog.Height ? _fogElev[z.Y * _fog.Width + z.X] : w.Elev;
            if (fogH == w.Elev) nebelZurueck++;
            if (_nav.FlagAt(z.X, z.Y) == 0) byteNull++;
            if (PionierbrueckeFlachAlt ? HangArt(z.X, z.Y) == w.Pose : HangArt(z.X, z.Y) == 0) poseNull++;
            if (!_rampen.ContainsKey(z.X * 1024 + z.Y)) lageLeer++;
            if (StegAn(z.X, z.Y) == null) satzWeg++;
        }
        int n = st.Zellen.Count;
        sb.AppendLine($"  4. Abriss (Kopfhoehe {kopfH}):");
        Soll(hoeheZurueck == n, $"   Zeichenhoehe wie vorher {hoeheZurueck}/{n}");
        Soll(navZurueck == n, $"   Gitterhoehe wie vorher {navZurueck}/{n}");
        Soll(nebelZurueck == n, $"   Nebelhoehe wie vorher {nebelZurueck}/{n}");
        Soll(byteNull == n, $"   Byte +3 = 0 (0x4CB0A0) {byteNull}/{n}");
        Soll(poseNull == n, $"   Posentafel {(PionierbrueckeFlachAlt ? "unberuehrt" : "= 0")} {poseNull}/{n}");
        Soll(lageLeer == n && satzWeg == n, $"   Lage leer {lageLeer}/{n}, kein Satz {satzWeg}/{n}");
        sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
