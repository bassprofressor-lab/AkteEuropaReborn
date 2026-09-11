namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>DER SELBSTVERTEIDIGER — wer getroffen wird, greift nach 20 Takten an</b>
/// (gebaut 11.09.2026 nach <c>berichte/selbstverteidiger.md</c>, seine Ansage
/// »bau die offenen punkte auch wie im original«).
///
/// <para><b>Gelesen:</b></para>
/// <code>
///   Zasah @0x40CA07  Angreifer &gt;= 8000 -> kein Eintrag (Ueberfahren, Skript, Luft, ...)
///         @0x40CA14  Ziel &gt;= 8000 -> kein Eintrag (Fussvolk, Gebaeude)
///         @0x40CA58  Ziel UKOL == 0, STRILI_NA == 0xFFFF, ZBRAN != 0 -> 0x411770(ziel, angreifer)
///   0x411770  Tafel 200 × (Opfer, Angreifer, Zaehler = 20); Opfer schon drin -> nichts;
///             voll -> »Cannot add more self-defenders«, nichts
///   0x411820  JEDEN Takt (Station 63): Zaehler −1; bei 0 und Opfer UKOL == 0 und
///             STRILI_NA == 0xFFFF -> 0x40FC90(opfer, angreifer, 0)
///   0x40FC90  der allgemeine ANGRIFFSBEFEHL: STRILI_NA == Angreifer -> nichts;
///             ZBRAN 8 -> nichts; Materialtransporter -> Route anhalten;
///             Angreifer ausser Reichweite (range·40 px) -> hinfahren;
///             UKOL := 4, UTOK_NA := Angreifer
/// </code>
/// <para>⭐ <b>Keine Buendnisfrage</b> in allen drei Funktionen (0 von 90
/// Zugriffen auf die Buendnistafel) — und die Kette schliesst sich: Schiessuhr
/// (@0x40DFBC) und Geschossflug (@0x45294F) lassen den Schuss auf einen
/// Verbuendeten genau bei <c>UKOL 4 &amp;&amp; UTOK_NA == Ziel</c> zu. Wer einen
/// Verbuendeten anschiesst, wird nach 0,4 s zurueck angegriffen.</para>
///
/// <para>⚠ UNSERE SETZUNGEN, benannt: UKOL 4 ist bei uns <c>Target</c> plus
/// <c>Ordered = true</c> (damit <c>UpdateCombat</c> verfolgt statt fallenlaesst);
/// der SPIEGELPUNKT (@0x40FDBE — bewaffnet und in Reichweite, aber nicht schiessend:
/// vom Angreifer weg) ist gelesen, aber nicht gebaut, weil seine Spielwirkung
/// ungemessen ist; ein toter Angreifer beim Ablauf wird uebersprungen (das
/// Original prueft es nicht). »Untaetig« ist <c>Ukol == 0 &amp;&amp; Path == null
/// &amp;&amp; Target &lt; 0 &amp;&amp; Orders.Count == 0</c>.</para>
///
/// <para>Gegenschalter <c>--gegenschuss-sofort</c> (der Stand vor dem 11.09.:
/// sofort zurueck, nur auf Feinde, auch Fussvolk, ohne Hinfahren). Pruefstand
/// <c>--selbstverteidiger-check</c>.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary><c>--gegenschuss-sofort</c></summary>
    public static bool GegenschussSofort;

    private const int SvTafelGroesse = 200, SvTakte = 20;
    private readonly List<(int Opfer, int Angreifer, int Zaehler, int Takt)> _svTafel = new();
    public int SvEintraege, SvAusgeloest, SvVoll;

    private static bool Untaetig(Entity e)
        => e.Ukol == 0 && e.Path == null && e.Target < 0 && e.Orders.Count == 0;

    /// <summary>@0x40CA58 → 0x411770.</summary>
    private void SelbstverteidigerEintrag(Entity? shooter, int si, Entity victim, int vi)
    {
        if (shooter == null || shooter.IsBuilding || shooter.IsProp) return;          // Angreifer < 8000
        if (victim.Dead || victim.IsBuilding || victim.IsProp || victim.Infantry >= 0) return;   // Ziel < 8000
        if (!Untaetig(victim) || !CanFight(victim)) return;                             // UKOL, STRILI_NA, ZBRAN
        foreach (var t in _svTafel)
            if (t.Opfer == vi && t.Zaehler > 0) return;                                  // @0x411793
        if (_svTafel.Count >= SvTafelGroesse)
        {
            SvVoll++;
            GD.Print("Cannot add more self-defenders (@0x4117B9)");
            return;
        }
        _svTafel.Add((vi, si, SvTakte, _taktNr));
        SvEintraege++;
        if (_svCheckAn) _svEingetragen.Add(vi);
    }

    /// <summary>0x411820 — jeden Takt.</summary>
    private void SelbstverteidigerTakt()
    {
        for (int k = 0; k < _svTafel.Count; k++)
        {
            var t = _svTafel[k];
            t.Zaehler--;
            if (t.Zaehler > 0) { _svTafel[k] = t; continue; }
            _svTafel.RemoveAt(k);
            k--;
            if (t.Opfer < 0 || t.Opfer >= _entities.Count) continue;
            var o = _entities[t.Opfer];
            if (o.Dead || !Untaetig(o)) continue;                                        // @0x411867/@0x411875
            if (t.Angreifer < 0 || t.Angreifer >= _entities.Count || _entities[t.Angreifer].Dead) continue;
            if (Angriffsbefehl(t.Opfer, o, t.Angreifer))
            {
                SvAusgeloest++;
                if (_svCheckAn) _svAusloesung[t.Opfer] = (_taktNr - t.Takt, t.Angreifer, o.Path != null);
            }
        }
    }

    /// <summary>Unser <c>0x40FC90(opfer, angreifer, 0)</c> — siehe Dateikopf.</summary>
    private bool Angriffsbefehl(int vi, Entity o, int ai)
    {
        if (o.Target == ai) return false;                                                // @0x40FD10
        if (WeaponRowOf(o.Weapon) == 8) return false;                                    // @0x40FD1D
        if (o.Route is Transportroute route && route.Gestartet) route.Gestartet = false; // @0x40FD3B
        var a = _entities[ai];
        o.Target = ai;
        o.Ordered = true;                                                                // UKOL 4
        int weit = o.Range > 0 ? o.Range : Mathf.RoundToInt(RangeOf(o));
        if (weit * 40 > o.Pos.DistanceTo(a.Pos)) return true;                           // @0x40FD67: in Reichweite
        if (!o.Mobile || _nav == null) return true;
        var ziel = _nav.NearestFree(new Vector2I(a.Col, a.Row), o.Move, vi) ?? new Vector2I(a.Col, a.Row);
        var weg = _nav.FindPath(new Vector2I(o.Col, o.Row), ziel, o.Move, vi);
        if (weg != null && weg.Count > 0) { o.Path = weg; o.PathIdx = 0; o.Goal = ziel; }
        return true;
    }

    // ================= der Pruefstand ==========================================

    private bool _svCheckAn;
    private readonly Dictionary<int, (int Takte, int Angreifer, bool Weg)> _svAusloesung = new();
    private readonly HashSet<int> _svEingetragen = new();

    private sealed class SvFall
    {
        public char Name;
        public string Titel = "", Notiz = "";
        public int Opfer = -1, Erster = -1, Zweiter = -1;
    }

    private readonly List<SvFall> _svFaelle = new();

    /// <summary>
    /// <c>--selbstverteidiger-check</c> — vier hergestellte Faelle, jeder auf
    /// einem freien Streifen, Opfer dem Spieler (keine KI), Angreifer weit
    /// ausserhalb der Reichweite des Opfers und ohne Waffe:
    /// 1 Feind trifft → nach 20 Takten Ziel und Weg; 2 zwei Angreifer
    /// nacheinander → der ERSTE bleibt; 3 ein VERBUENDETER trifft → nach 20
    /// Takten Ziel; 4 ein Fusssoldat wird getroffen → kein Eintrag, kein Ziel.
    /// <para>⚠ EINGRIFFE: Versetzen, Besitzer, Waffe der Angreifer. Nullmodell
    /// <c>--gegenschuss-sofort</c>.</para>
    /// </summary>
    public void SelbstverteidigerCheckStart()
    {
        if (_nav == null) return;
        _svCheckAn = true;
        var benutzt = new HashSet<int>();
        int feind = -1;
        for (int p = 0; p <= 7 && feind < 0; p++)
            if (!Allied(ViewPlayer, p)) feind = p;

        bool Frei(int k)
        {
            var x = _entities[k];
            return !benutzt.Contains(k) && !x.Dead && !x.IsProp && !x.IsBuilding && x.Mobile
                   && x.Ukol < 50 && !Untergestellt(x);
        }
        int Suche(System.Func<Entity, bool> art)
        {
            for (int k = 0; k < _entities.Count; k++)
                if (Frei(k) && art(_entities[k])) { benutzt.Add(k); return k; }
            return -1;
        }
        bool Fahrzeug(Entity x) => x.Infantry < 0 && x.GameUnitType == 0;
        bool BewaffnetesFahrzeug(Entity x) => Fahrzeug(x) && CanFight(x);

        int Weit(int opfer, int angreifer, int besitzer)
        {
            var o = _entities[opfer];
            var a = _entities[angreifer];
            a.Owner = besitzer; a.Weapon = 0; a.Target = -1; a.Path = null;
            int weit = o.Range > 0 ? o.Range : Mathf.RoundToInt(RangeOf(o));
            var z = _nav.NearestFree(new Vector2I(Mathf.Min(_nav.Width - 2, o.Col + weit + 6), o.Row),
                                     a.Move, angreifer);
            if (z == null) return -1;
            ProbeVersetzen(angreifer, z.Value.X, z.Value.Y);
            return angreifer;
        }

        SvFall Fall(char name, string titel, bool fussvolk)
        {
            var f = new SvFall { Name = name, Titel = titel };
            _svFaelle.Add(f);
            f.Opfer = fussvolk ? Suche(x => x.Infantry >= 0 && CanFight(x)) : Suche(BewaffnetesFahrzeug);
            if (f.Opfer < 0) { f.Notiz = "kein Opfer"; return f; }
            if (!FreierStreifen(out int c, out int r)) { f.Notiz = "kein freier Streifen"; f.Opfer = -1; return f; }
            var o = _entities[f.Opfer];
            o.Owner = ViewPlayer; o.Target = -1; o.Path = null; o.Ordered = false; o.Ukol = 0;
            o.Orders.Clear();
            ProbeVersetzen(f.Opfer, c, r);
            return f;
        }

        var f1 = Fall('1', "Feind weit ausserhalb       ", false);
        if (f1.Opfer >= 0)
        {
            f1.Erster = Suche(Fahrzeug);
            if (f1.Erster >= 0 && Weit(f1.Opfer, f1.Erster, feind) >= 0)
                ApplyHit(f1.Erster, f1.Opfer, _entities[f1.Opfer], 0);
            else f1.Notiz = "kein Angreifer";
        }
        var f2 = Fall('2', "zwei Angreifer nacheinander ", false);
        if (f2.Opfer >= 0)
        {
            f2.Erster = Suche(Fahrzeug);
            f2.Zweiter = Suche(Fahrzeug);
            if (f2.Erster >= 0 && f2.Zweiter >= 0 && Weit(f2.Opfer, f2.Erster, feind) >= 0
                && Weit(f2.Opfer, f2.Zweiter, feind) >= 0)
            {
                ApplyHit(f2.Erster, f2.Opfer, _entities[f2.Opfer], 0);
                ApplyHit(f2.Zweiter, f2.Opfer, _entities[f2.Opfer], 0);
            }
            else f2.Notiz = "keine zwei Angreifer";
        }
        var f3 = Fall('3', "verbuendeter Schuetze       ", false);
        if (f3.Opfer >= 0)
        {
            f3.Erster = Suche(Fahrzeug);
            if (f3.Erster >= 0 && Weit(f3.Opfer, f3.Erster, ViewPlayer) >= 0)
                ApplyHit(f3.Erster, f3.Opfer, _entities[f3.Opfer], 0);
            else f3.Notiz = "kein Angreifer";
        }
        var f4 = Fall('4', "Fusssoldat getroffen        ", true);
        if (f4.Opfer >= 0)
        {
            f4.Erster = Suche(Fahrzeug);
            if (f4.Erster >= 0 && Weit(f4.Opfer, f4.Erster, feind) >= 0)
                ApplyHit(f4.Erster, f4.Opfer, _entities[f4.Opfer], 0);
            else f4.Notiz = "kein Angreifer";
        }
        foreach (var f in _svFaelle)
            GD.Print($"selbstverteidiger-check: {f.Name} {f.Titel.Trim()} — Opfer {f.Opfer}, "
                   + $"Angreifer {f.Erster}/{f.Zweiter} {f.Notiz}");
    }

    public string SelbstverteidigerCheckLine()
    {
        var sb = new System.Text.StringBuilder("selbstverteidiger-check\n");
        if (!_svCheckAn) return sb.Append("  nicht gestartet — der Lauf sagt NICHTS").ToString();
        bool ok = _svFaelle.Count > 0;
        foreach (var f in _svFaelle)
        {
            if (f.Opfer < 0 || f.Erster < 0 || f.Notiz.Length > 0)
            {
                ok = false;
                sb.Append($"  {f.Name} {f.Titel}: ⚠ nicht herstellbar ({f.Notiz}) — sagt NICHTS\n");
                continue;
            }
            bool hat = _svAusloesung.TryGetValue(f.Opfer, out var a);
            bool eingetragen = _svEingetragen.Contains(f.Opfer);
            bool soll = f.Name switch
            {
                '1' => hat && a.Takte == SvTakte && a.Angreifer == f.Erster && a.Weg,
                '2' => hat && a.Takte == SvTakte && a.Angreifer == f.Erster,
                '3' => hat && a.Takte == SvTakte && a.Angreifer == f.Erster,
                _ => !hat && !eingetragen,
            };
            ok &= soll;
            var o = _entities[f.Opfer];
            sb.Append($"  {f.Name} {f.Titel}: {(eingetragen ? "eingetragen" : "kein Eintrag")}, "
                    + (hat ? $"Ziel nach {a.Takte} Takten auf {(a.Angreifer == f.Erster ? "den ersten" : "den ZWEITEN")} Angreifer, "
                           + $"Weg {(a.Weg ? "ja" : "nein")}"
                           : "kein Ziel gesetzt")
                    + $" (jetzt Ziel {o.Target}, auf ({o.Col},{o.Row}))  {(soll ? "ja" : "NEIN")}\n");
        }
        sb.Append($"  Tafel: {SvEintraege} Eintraege, {SvAusgeloest} ausgeloest, {SvVoll}x voll; "
                + $"Gegenschalter --gegenschuss-sofort: {GegenschussSofort}\n");
        sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
