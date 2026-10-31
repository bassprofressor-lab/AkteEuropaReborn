namespace AkteEuropaReborn.Rendering;

using System.Text;
using Godot;
using GDict = Godot.Collections.Dictionary<string, Godot.Variant>;

/// <summary>
/// ⭐ <b>MINEN UND FALLEN IM SPIELSTAND</b> (30.10.2026, bug-699).
///
/// <para><b>Befund:</b> der Spielstand trug weder das Minenfeld noch (seit bug-697) das
/// Fallenfeld — <c>grep</c> auf SaveState.cs: 0 Treffer. Schlimmer als »fehlt«: beim
/// Laden laeuft unser Skriptstart wieder durch <see cref="MissionsMinenLegen"/>, und
/// damit lagen nach dem Laden von K15 wieder ALLE 458 Minen des Aufbaus da, auch die
/// laengst geraeumten und hochgegangenen. Das Original speichert beide Felder als
/// sec84 (Minen, 0x552E18) und sec85 (Fallen, 0x688B58) — belegt an den .DM-Dateien:
/// 3/5/6/7/10.DM tragen 140..207 Minen und je 6 Fallen (dmm.py).</para>
///
/// <para><b>Gebaut:</b> zwei neue Schluessel <c>minen</c> und <c>fallen</c>, je
/// <c>[[platz, spalte, zeile, leger, +2, +3], ...]</c> fuer die BELEGTEN Plaetze (der
/// Platz geht mit, weil <see cref="MineLegen"/> den ersten freien nimmt). Dazu je
/// Einheit <c>gefangen</c> (bug-697) und <c>lege</c> = [+0x40 Spalte, Zeile, +0x48,
/// +0x49] (bug-698). Das Format bleibt 5: ein Stand ohne die Schluessel laedt wie
/// bisher (der Aufbau legt neu). Ein Stand MIT ihnen sperrt den Aufbau
/// (<see cref="_minenStandGeladen"/>).</para>
///
/// <para>Gegenschalter <c>--minen-spielstand-alt</c>: nichts schreiben, nichts lesen.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--minen-spielstand-alt</c> — der Stand bis 30.10.2026.</summary>
    public static bool MinenSpielstandAlt;

    /// <summary>Der Stand hat die Felder gesetzt — der Skriptstart legt dann nichts
    /// darueber (<see cref="MissionsMinenLegen"/>, <see cref="MissionsFallenLegen"/>).</summary>
    private bool _minenStandGeladen;

    /// <summary>Wie viele Minen/Fallen der letzte Ladevorgang gesetzt hat (Pruefstand).</summary>
    public static int MinenGeladen = -1, FallenGeladen = -1;

    private static string FeldText(Mine[] feld)
    {
        var sb = new StringBuilder("[");
        bool erst = true;
        for (int i = 0; i < feld.Length; i++)
        {
            var m = feld[i];
            if (!m.Aktiv) continue;
            if (!erst) sb.Append(',');
            erst = false;
            sb.Append('[').Append(i).Append(',').Append(m.Col).Append(',').Append(m.Row).Append(',')
              .Append(m.Player).Append(',').Append(m.LageX).Append(',').Append(m.LageY).Append(']');
        }
        return sb.Append(']').ToString();
    }

    /// <summary>Aus <c>SaveStateJson</c>.</summary>
    private void MinenFallenInSpielstand(Core.SaveGame.Writer w)
    {
        if (MinenSpielstandAlt) return;
        w.Raw("minen", FeldText(_minen));
        w.Raw("fallen", FeldText(_fallen));
    }

    private static int FeldLesen(GDict root, string k, Mine[] feld)
    {
        if (!root.TryGetValue(k, out var v) || v.VariantType != Variant.Type.Array) return -1;
        System.Array.Clear(feld);
        int n = 0;
        foreach (var e in v.AsGodotArray())
        {
            if (e.VariantType != Variant.Type.Array) continue;
            var q = e.AsGodotArray();
            if (q.Count < 6) continue;
            int i = q[0].AsInt32();
            if (i < 0 || i >= feld.Length) continue;
            feld[i] = new Mine
            {
                Col = q[1].AsInt32(), Row = q[2].AsInt32(), Player = q[3].AsInt32(), Aktiv = true,
                LageX = q[4].AsInt32(), LageY = q[5].AsInt32(),
            };
            n++;
        }
        return n;
    }

    /// <summary>Aus <c>ApplySaveState</c>. Ohne Schluessel (alter Stand) bleibt alles,
    /// wie es war.</summary>
    private void MinenFallenAusSpielstand(GDict root)
    {
        MinenGeladen = FallenGeladen = -1;
        if (MinenSpielstandAlt) return;
        MinenGeladen = FeldLesen(root, "minen", _minen);
        FallenGeladen = FeldLesen(root, "fallen", _fallen);
        if (MinenGeladen >= 0 || FallenGeladen >= 0)
        {
            _minenStandGeladen = true;
            GD.Print($"Spielstand: {System.Math.Max(0, MinenGeladen)} Minen und {System.Math.Max(0, FallenGeladen)} Fallen gelesen (bug-699)");
        }
    }

    /// <summary>Je Einheit, aus <c>SchreibeEinheit</c>: nur, was vom Vorgabewert abweicht.</summary>
    private static void FallenEinheitSchreiben(Core.SaveGame.Writer w, Entity e)
    {
        if (MinenSpielstandAlt) return;
        if (e.Gefangen) w.Bool("gefangen", true);
        if (e.LegeB != 0) w.Raw("lege", $"[{e.LegeX},{e.LegeY},{e.LegeB},{e.LegeH}]");
    }

    /// <summary>Je Einheit, aus dem Einheitenleser von <c>ApplySaveState</c>.</summary>
    private static void FallenEinheitLesen(GDict d, Entity e)
    {
        if (MinenSpielstandAlt) return;
        e.Gefangen = d.TryGetValue("gefangen", out var g) && g.VariantType != Variant.Type.Nil && g.AsBool();
        e.LegeX = e.LegeY = e.LegeB = e.LegeH = 0;
        if (d.TryGetValue("lege", out var l) && l.VariantType == Variant.Type.Array)
        {
            var a = l.AsGodotArray();
            if (a.Count >= 4) { e.LegeX = a[0].AsInt32(); e.LegeY = a[1].AsInt32(); e.LegeB = a[2].AsInt32(); e.LegeH = a[3].AsInt32(); }
        }
    }

    /// <summary>Ein Teil fuer <c>SpielstandFingerabdruck</c>.</summary>
    public string MinenFallenFingerabdruck()
    {
        long Pruef(Mine[] feld, out int n)
        {
            long p = 0; n = 0;
            for (int i = 0; i < feld.Length; i++)
            {
                var m = feld[i];
                if (!m.Aktiv) continue;
                n++;
                p += (i + 1) * 7L + m.Col * 31L + m.Row * 37L + (m.Player + 1) * 41L + m.LageX * 3L + m.LageY * 5L;
            }
            return p;
        }
        long pm = Pruef(_minen, out int nm), pf = Pruef(_fallen, out int nf);
        int gef = 0, lege = 0; long lp = 0;
        foreach (var e in _entities)
        {
            if (e.IsProp || e.Dead) continue;
            if (e.Gefangen) gef++;
            if (e.LegeB != 0) { lege++; lp += e.LegeX * 3L + e.LegeY * 5L + e.LegeB * 7L + e.LegeH * 11L; }
        }
        return $"Minen {nm} (Pruefzahl {pm}), Fallen {nf} (Pruefzahl {pf}), gefangen {gef}, Legeauftraege {lege} (Pruefzahl {lp})";
    }

    /// <summary>Fuer <c>SpielstandProbeVorbereiten</c>: etwas hinlegen, das verloren
    /// gehen KANN — je eine feindliche Mine und Falle auf freien Zellen, eine
    /// geraeumte Missionsmine (falls welche liegen), ein Legeauftrag und eine
    /// gefangene Einheit. ⚠ EINGRIFF.</summary>
    public string SpielstandProbeMinenFallen()
    {
        if (_nav == null) return "Minen/Fallen: keine Karte";
        var sb = new StringBuilder("Minen/Fallen:");
        int fremd = ViewPlayer == 1 ? 2 : 1;
        int gelegt = 0;
        for (int r = 4; r < _nav.Height - 4 && gelegt < 2; r += 3)
            for (int c = 4; c < _nav.Width - 4 && gelegt < 2; c += 3)
            {
                if (_nav.GroundAt(c, r) != Simulation.NavGrid.Ground.Free || _nav.BesetztVon(c, r) >= 0
                    || GebaeudeAufZelle(c, r) >= 0 || MineOderFalleAufZelle(c, r)) continue;
                bool ok = gelegt == 0 ? MineLegen(c, r, fremd) : FalleLegen(c, r, fremd);
                if (ok) { sb.Append($" {(gelegt == 0 ? "Mine" : "Falle")} P{fremd} ({c},{r})"); gelegt++; }
            }
        // eine Mine wieder weg — der alte Fehler legte sie beim Laden zurueck
        for (int i = 0; i < _minen.Length; i++)
            if (_minen[i].Aktiv) { _minen[i].Aktiv = false; sb.Append($", Mine Platz {i} entfernt"); break; }
        int k = 0;
        foreach (var e in _entities)
        {
            if (e.IsProp || e.IsBuilding || e.Dead || e.Owner != ViewPlayer || e.Infantry >= 0) continue;
            if (k == 0 && e.LegeB == 0) { e.LegeX = e.Col; e.LegeY = e.Row; e.LegeB = 3; e.LegeH = 2; sb.Append($", Legeauftrag an Platz {e.Slot}"); k++; }
            else if (k == 1 && !e.Gefangen) { e.Gefangen = true; e.Speed = 0; sb.Append($", Platz {e.Slot} gefangen"); k++; }
            if (k == 2) break;
        }
        return sb.ToString();
    }

    // ---- bug-709: die Felder der ORIGINAL-Spielstaende (.DM) ---------------------

    /// <summary><c>--dm-minen-alt</c> — der Stand bis 30.10.2026: ein .DM-Stand kommt
    /// ohne seine Minen (sec84) und Fallen (sec85).</summary>
    public static bool DmMinenAlt;

    public const string DmMinenPfad = "res://Data/dm_minen.json";
    private static Godot.Collections.Dictionary? _dmMinen;

    /// <summary>Wie viele Minen/Fallen der Kartenlader aus dem .DM gesetzt hat (−1 = keine
    /// .DM-Karte bzw. kein Eintrag).</summary>
    public int DmMinenGesetzt = -1, DmFallenGesetzt = -1;
    private int _dmMinenSoll = -1, _dmFallenSoll = -1;

    private static Godot.Collections.Dictionary? DmEintrag(string karte)
    {
        if (_dmMinen == null)
        {
            _dmMinen = new Godot.Collections.Dictionary();
            using var f = FileAccess.Open(DmMinenPfad, FileAccess.ModeFlags.Read);
            if (f != null)
            {
                var j = new Json();
                if (j.Parse(f.GetAsText()) == Error.Ok && j.Data.VariantType == Variant.Type.Dictionary
                    && j.Data.AsGodotDictionary().TryGetValue("staende", out var st) && st.VariantType == Variant.Type.Dictionary)
                    _dmMinen = st.AsGodotDictionary();
            }
        }
        string k = karte.StartsWith("map_") ? karte[4..] : karte;
        return _dmMinen.TryGetValue(k, out var v) && v.VariantType == Variant.Type.Dictionary ? v.AsGodotDictionary() : null;
    }

    private static int DmFeld(Godot.Collections.Dictionary e, string k, Mine[] feld)
    {
        System.Array.Clear(feld);
        if (!e.TryGetValue(k, out var v) || v.VariantType != Variant.Type.Array) return 0;
        int n = 0;
        foreach (var q0 in v.AsGodotArray())
        {
            if (q0.VariantType != Variant.Type.Array) continue;
            var q = q0.AsGodotArray();
            if (q.Count < 6) continue;
            int i = q[0].AsInt32();
            if (i < 0 || i >= feld.Length) continue;
            feld[i] = new Mine { Col = q[1].AsInt32(), Row = q[2].AsInt32(), Player = q[3].AsInt32(), Aktiv = true,
                                 LageX = q[4].AsInt32(), LageY = q[5].AsInt32() };
            n++;
        }
        return n;
    }

    /// <summary>Aus dem Kartenlader (<c>Load</c>): eine .DM-Karte bekommt ihre Minen und
    /// Fallen so, wie die Datei sie traegt — Platz fuer Platz, ohne Legetor und ohne
    /// Wurf (der Lader des Originals liest die Bloecke roh, 0x41D210/0x41E070).
    /// ⚠ UNSERE SETZUNG: die Felder kommen aus Data/dm_minen.json (aekernel-tools/
    /// dm_minen.py), nicht aus dem Kartenimport — so braucht es keinen neuen Import.</summary>
    private void DmMinenLaden(string karte)
    {
        DmMinenGesetzt = DmFallenGesetzt = _dmMinenSoll = _dmFallenSoll = -1;
        var e = DmEintrag(karte);
        if (e == null) return;
        _dmMinenSoll = e.TryGetValue("minen", out var mv) && mv.VariantType == Variant.Type.Array ? mv.AsGodotArray().Count : 0;
        _dmFallenSoll = e.TryGetValue("fallen", out var fv) && fv.VariantType == Variant.Type.Array ? fv.AsGodotArray().Count : 0;
        if (DmMinenAlt) { DmMinenGesetzt = DmFallenGesetzt = 0; return; }
        DmMinenGesetzt = DmFeld(e, "minen", _minen);
        DmFallenGesetzt = FallenAus ? 0 : DmFeld(e, "fallen", _fallen);
        GD.Print($"DM-Stand {karte}: {DmMinenGesetzt} Minen (sec84) und {DmFallenGesetzt} Fallen (sec85) gesetzt (bug-709)");
    }

    /// <summary><c>--dm-minen-check</c> — auf einer .DM-Karte: liegen genau die Minen und
    /// Fallen der Datei? Nullmodell <c>--dm-minen-alt</c>: 0.</summary>
    public string DmMinenCheck(string karte)
    {
        var sb = new StringBuilder($"dm-minen-check {karte}\n");
        if (_dmMinenSoll < 0) return sb.Append("  kein Eintrag in Data/dm_minen.json fuer diese Karte\n  DURCHGEFALLEN").ToString();
        if (DmMinenAlt) sb.AppendLine("  ⚠ NULLMODELL --dm-minen-alt: hier MUSS der Lauf durchfallen");
        var e = DmEintrag(karte)!;
        int Treffer(string k, Mine[] feld)
        {
            int t = 0;
            if (!e.TryGetValue(k, out var v) || v.VariantType != Variant.Type.Array) return 0;
            foreach (var q0 in v.AsGodotArray())
            {
                var q = q0.AsGodotArray(); int i = q[0].AsInt32();
                var m = feld[i];
                if (m.Aktiv && m.Col == q[1].AsInt32() && m.Row == q[2].AsInt32() && m.Player == q[3].AsInt32()
                    && m.LageX == q[4].AsInt32() && m.LageY == q[5].AsInt32()) t++;
            }
            return t;
        }
        int tm = Treffer("minen", _minen), tf = Treffer("fallen", _fallen);
        bool ok = MinenAktiv() == _dmMinenSoll && tm == _dmMinenSoll && FallenAktiv() == _dmFallenSoll && tf == _dmFallenSoll;
        sb.AppendLine($"  Datei: {_dmMinenSoll} Minen, {_dmFallenSoll} Fallen; im Spiel: {MinenAktiv()} Minen ({tm} Platz fuer Platz gleich), "
                    + $"{FallenAktiv()} Fallen ({tf} gleich); --dm-minen-alt {DmMinenAlt}");
        return sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }
}
