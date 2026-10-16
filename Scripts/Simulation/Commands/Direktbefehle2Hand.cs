namespace AkteEuropaReborn.Rendering;

using Godot;
using AkteEuropaReborn.Simulation.Commands;

/// <summary>
/// ⭐⭐ <b>PAKET »hand« — HANDSTEUERUNG BODEN UND LUFT, FIRE_AT, SELBSTZERSTÖRUNG
/// ÜBER DEN RING</b> (17.10.2026, Direktbefehle2, berichte/desync2-opus.md).
///
/// <para><b>Was vorher falsch war:</b> beide Handsteuerungen lasen die Tasten
/// der ÖRTLICHEN Maschine im Spieltakt und schrieben sofort in den Zustand
/// (Schritt, Rohr, Richtung/Stufe/Höhe des Flugzeugs, Schuss). Dazu hing
/// <c>uk 3</c> nur am örtlichen <see cref="HandLuftIdx"/> — auf der anderen
/// Maschine flog dieselbe Maschine nach ihrem alten Auftrag weiter.</para>
///
/// <para><b>Wie das Original es trennt</b> (gelesen in HandsteuerungBoden.cs /
/// HandsteuerungLuft.cs): der SENDER <c>0x433460</c> läuft im Taktrumpf
/// (@0x41685C), liest die Tasten und schickt Busbefehle — 1 (Schritt), 2
/// (Rohr), 8 (Hand control). Die ARME dahinter (0x4C273F, 0x4C277D,
/// 0x4C2B58) schreiben den Zustand. Übernehmen 4, Abgeben 5, Abwahl 30,
/// Klickschuss 9 (0x40B9A0 → 0x40BA86), Selbstzerstörung 13 (0x4487D9),
/// Eintritt am Flughafen 502 Modus 6 (0x449C1B). Genau so ist es jetzt gebaut:
/// <see cref="HandSenderTakt"/> ist der Sender, die <c>Apply*</c> unten die
/// Arme.</para>
///
/// <para>⚠ <b>SETZUNG:</b> ein Flugzeug trägt in P1 <c>20000 + Platz</c> (der
/// Griff, den das Original in <c>word[0x4FA0C8]</c> führt); der Satz des
/// Originals trägt den sec19-Index. ⚠ Die Bodensteuerung (UKOL 1) hat bei uns
/// keinen eigenen Zustand im Fahrtakt — Befehl 4/5 am Boden tragen nur
/// <see cref="AuftragLoeschen"/> (bug-417) bzw. nichts; wer steuert, steht
/// örtlich in <see cref="HandsteuerungIdx"/>.</para>
///
/// <para>Einzelspieler: Wirkung einen Takt nach der Taste (Sender im Takt t,
/// fällig t+1). Gegenschalter <c>--direktbefehle2-alt=hand</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Der Griff eines Flugzeugs in P1.</summary>
    private const int FlugGriff = 20000;

    private Special? FlugzeugAusGriff(int p1)
    {
        if (p1 < FlugGriff) return null;
        int slot = p1 - FlugGriff;
        foreach (var a in _special) if (a.Slot == slot && !a.Dead) return a;
        return null;
    }

    // ================= DER SENDER (0x433460), je Spieltakt ====================

    /// <summary>
    /// Steht in <c>SimTick</c> an der Stelle, wo bis heute die Arme unmittelbar
    /// liefen. Liest die Tasten, die die Oberfläche abgelegt hat, und schickt
    /// Sätze — schreibt selbst KEINEN Zustand ausser der Aufräumung unten, die
    /// nur aus dem Zustand folgt und darum auf jeder Maschine gleich läuft.
    /// </summary>
    private void HandSenderTakt()
    {
        // Zustandsfolge, auf JEDER Maschine gleich: Absturz, Tod und Landung
        // heben uk 3 auf (@0x425F62).
        foreach (var s in _special)
            if (s.Hand && (s.Dead || s.Stored || s.Absturz)) s.Hand = false;

        if (HandLuftIdx >= 0)
        {
            // ⚠ HandLuftIdx ist ein LISTENPLATZ in _special, und die Liste
            // verschiebt sich (gemessen 17.10.: Übernahme Platz 4 in Takt 107,
            // in Takt 109 stand dort eine andere Maschine). Über den Satzplatz
            // nachführen.
            int nk = _special.FindIndex(x => x.Slot == _handLuftSlot && !x.Dead);
            if (nk >= 0) HandLuftIdx = nk;
            if (HandLuftIdx >= _special.Count || !_special[HandLuftIdx].Hand)
            { HandsteuerungLuftBeenden("Absturz"); return; }     // nur noch örtlich
            var a = _special[HandLuftIdx];
            var (dDir, dSp, dAlt) = HandLuftDeltas(HandTasteLinks, HandTasteRechts, HandTasteHoch,
                                                   HandTasteRunter, HandTasteSteigen, HandTasteSinken);
            // Ein leerer Satz ändert nichts — das Original schickt ihn zwar
            // jeden Takt, wir sparen uns die Leitung (der Arm zählt dann nur
            // HandLuftTakte nicht mit; die Messzeile sagt es).
            if (dDir != 0 || dSp != 0 || dAlt != 0 || HandTasteSchuss)
                Emit(CommandRecord.Make(CommandOp.HandLuft, (byte)ViewPlayer, S(FlugGriff + a.Slot),
                                        S(dDir), S(dSp), S(dAlt), (short)(HandTasteSchuss ? 1 : 0)));
            return;
        }

        if (HandsteuerungIdx < 0 || HandsteuerungBodenAlt) return;
        int idx = HandsteuerungIdx;
        if (idx >= _entities.Count) { HandsteuerungIdx = -1; return; }
        var e = _entities[idx];
        if (e.Dead || !e.Mobile || e.Owner != ViewPlayer)
        { HandsteuerungIdx = -1; HandRohrRichtung = -1; return; }
        // 0x433497 — Befehl 2: das Rohr folgt der Maus.
        if (HandMausKarte is { } m && (m - e.Pos).LengthSquared() > 0.01f)
        {
            int r = DirToFacing(m - e.Pos);
            HandRohrRichtung = r;
            if (TurmEigen(e) && e.TurmWunsch != r)
                Emit(CommandRecord.Make(CommandOp.HandRohr, (byte)ViewPlayer, (short)idx, (short)r));
        }
        // 0x4335B7 — Befehl 1: ein Schritt.
        int k = HandBodenTastenRichtung(e);
        if (k >= 0) Emit(CommandRecord.Make(CommandOp.HandSchritt, (byte)ViewPlayer, (short)idx, (short)k));
    }

    // ================= DIE ABSENDER ===========================================

    /// <summary>Menüzeile »Handsteuerung« (Merker B): Befehl 4 bzw. 5.</summary>
    public string PostHandsteuerungUmschalten()
    {
        if (Alt2("hand")) return HandsteuerungUmschalten();
        int idx = MenueEinheit();
        if (idx < 0) return "keine eigene Einheit gewaehlt";
        if (HandsteuerungIdx == idx)
        {
            HandsteuerungIdx = -1;
            Emit(CommandRecord.Make(CommandOp.HandAbgeben, (byte)ViewPlayer, (short)idx));
            return "Handsteuerung abgegeben";
        }
        if (!_entities[idx].Mobile) return "diese Einheit faehrt nicht";
        HandsteuerungIdx = idx;
        Emit(CommandRecord.Make(CommandOp.HandUebernehmen, (byte)ViewPlayer, (short)idx));
        return "Handsteuerung: mit den Pfeiltasten fahren, nochmal druecken beendet sie";
    }

    /// <summary>Rechtsklick ohne Kamerabewegung (0x41449E): Befehl 5.</summary>
    public string PostHandBodenAbgeben()
    {
        if (Alt2("hand")) return HandsteuerungBodenAbgeben();
        int idx = HandsteuerungIdx;
        string r = HandsteuerungBodenAbgeben();
        if (idx >= 0) Emit(CommandRecord.Make(CommandOp.HandAbgeben, (byte)ViewPlayer, (short)idx));
        return r;
    }

    /// <summary>Linksklick unter Handsteuerung (0x40B9A0): die Zelle auf die
    /// Reichweite geklemmt, dann Befehl 9 (fire_at).</summary>
    public bool PostHandKlick(Vector2 mapPos)
    {
        if (Alt2("hand")) return HandsteuerungKlick(mapPos);
        if (HandsteuerungKlickAlt || HandsteuerungIdx < 0 || HandsteuerungIdx >= _entities.Count) return false;
        var e = _entities[HandsteuerungIdx];
        if (e.Dead) return false;
        if (CellAt(mapPos) is not { } z) return true;
        int dx = z.X - e.Col, dy = z.Y - e.Row;
        float d = Mathf.Sqrt(dx * 40f * dx * 40f + dy * 40f * dy * 40f);
        int weit = e.Range > 0 ? e.Range : Mathf.RoundToInt(RangeOf(e));
        float r = weit * 40f;
        int col = z.X, row = z.Y;
        if (d > r && d > 0f)
        {
            col = e.Col + (int)(dx * r / d);
            row = e.Row + (int)(dy * r / d);
            HandKlickGeklemmt++;
        }
        Emit(CommandRecord.Make(CommandOp.FireAt, (byte)ViewPlayer, (short)HandsteuerungIdx,
                                (short)col, (short)row));
        return true;
    }

    /// <summary>Menüzeile »Selbstzerstörung« (0x4487D9): Befehl 13.</summary>
    public string PostSelbstzerstoerung()
    {
        if (Alt2("hand")) return SelbstzerstoerungAusfuehren();
        int idx = MenueEinheit();
        if (idx < 0) return "keine eigene Einheit gewaehlt";
        var e = _entities[idx];
        if (e.HpMax > 0 && e.Hp * 100 / e.HpMax <= 15)
            return "zu schwer beschaedigt fuer die Selbstzerstoerung";
        Emit(CommandRecord.Make(CommandOp.Selbstzerstoerung, (byte)ViewPlayer, (short)idx));
        return $"{LabelOf(e)} zerstoert sich selbst";
    }

    /// <summary>Knopf »Handsteuerung« am Flughafen (Taste 9, 0x449C1B):
    /// Befehl 502 im Modus 6.</summary>
    public bool PostHandLuftBeginnen(int slot)
    {
        if (Alt2("hand")) return HandsteuerungLuftBeginnen(slot);
        if (HandsteuerungLuftAus) return false;
        var a = _special.Find(x => x.Slot == slot && !x.Dead);
        if (a == null) { _order = "Diese Maschine gibt es nicht mehr."; return false; }
        if (a.Absturz) { _order = "Diese Maschine stuerzt ab."; return false; }
        return Emit(CommandRecord.Make(CommandOp.Flugzeugstart, (byte)ViewPlayer, (short)slot,
                                       (short)ZielModusHand));
    }

    /// <summary>Taste H (0x433750): Befehl 4 (übernehmen) oder 5 (beenden).</summary>
    public bool PostHandLuftTasteH()
    {
        if (Alt2("hand")) return HandsteuerungLuftTasteH();
        if (HandsteuerungLuftAus) return false;
        if (HandLuftIdx >= 0)
        {
            if (HandLuftIdx < _special.Count)
                Emit(CommandRecord.Make(CommandOp.HandAbgeben, (byte)ViewPlayer,
                                        S(FlugGriff + _special[HandLuftIdx].Slot)));
            return true;
        }
        int idx = _selAir;
        if (idx < 0 || idx >= _special.Count) return false;
        var a = _special[idx];
        if (a.Dead || a.Stored || a.Absturz || a.IsSupply) return false;
        return Emit(CommandRecord.Make(CommandOp.HandUebernehmen, (byte)ViewPlayer, S(FlugGriff + a.Slot)));
    }

    /// <summary>Rechtsklick/ESC unter Handsteuerung Luft: Befehl 30 (die
    /// Abwahl 0x4330D4).</summary>
    public void PostHandLuftBeenden(string grund)
    {
        if (Alt2("hand")) { HandsteuerungLuftBeenden(grund); return; }
        if (HandLuftIdx < 0 || HandLuftIdx >= _special.Count) return;
        Emit(CommandRecord.Make(CommandOp.Abwahl, (byte)ViewPlayer, S(FlugGriff + _special[HandLuftIdx].Slot)));
    }

    /// <summary>Modus 6 von Befehl 502 — die Handsteuerung (0x4234B7).</summary>
    public const int ZielModusHand = 6;

    // ================= DIE ARME ===============================================

    /// <summary>1 — Schritt. P1 Einheit, P2 Richtungsindex.</summary>
    private bool ApplyHandSchritt(in CommandRecord c)
    {
        if (!EigenesObjekt(c, c.P1) || !_entities[c.P1].Mobile) return false;
        return HandBodenSchrittSetzen(c.P1, c.P2);
    }

    /// <summary>2 — Rohr. P1 Einheit, P2 Richtung 0..7.</summary>
    private bool ApplyHandRohr(in CommandRecord c)
    {
        if (!EigenesObjekt(c, c.P1) || c.P2 is < 0 or > 7) return false;
        var e = _entities[c.P1];
        if (!TurmEigen(e) || e.TurmWunsch == c.P2) return false;
        HandBodenRohrSetzen(e, c.P2);
        return true;
    }

    /// <summary>4 — übernehmen. Boden: der Zellangriff fällt (bug-417).
    /// Flugzeug (P1 ≥ 20000): uk 3, 0x4C28BA prüft typ &lt; 13.</summary>
    private bool ApplyHandUebernehmen(in CommandRecord c)
    {
        if (c.P1 >= FlugGriff)
        {
            var a = FlugzeugAusGriff(c.P1);
            if (a == null || a.Owner != c.Player || a.Stored || a.Absturz || a.IsSupply || a.Hand) return false;
            int idx = _special.IndexOf(a);
            bool eigen = c.Player == ViewPlayer;
            if (eigen) HandLuftPerTasteH++;
            return HandLuftUebernehmen(idx, "Handsteuerung uebernommen (H beendet sie)", eigen);
        }
        if (!EigenesObjekt(c, c.P1) || !_entities[c.P1].Mobile) return false;
        AuftragLoeschen(_entities[c.P1]);
        return true;
    }

    /// <summary>5 — abgeben; 30 — Abwahl. Flugzeug: air_back_to_airport
    /// (Heimkehr). Boden: UKOL := 0 — bei uns ohne Zustand.</summary>
    private bool ApplyHandAbgeben(in CommandRecord c)
    {
        if (c.P1 >= FlugGriff)
        {
            var a = FlugzeugAusGriff(c.P1);
            if (a == null || a.Owner != c.Player || !a.Hand) return false;
            int idx = _special.IndexOf(a);
            string grund = c.Op == CommandOp.Abwahl ? "Abwahl" : "H";
            if (HandLuftIdx == idx) { HandsteuerungLuftBeenden(grund); return true; }
            a.Hand = false;
            HandLuftBeendet++;
            if (!a.Dead && !a.Stored && !a.Absturz) a.Heimkehr = true;
            return true;
        }
        if (c.Op == CommandOp.Abwahl) return false;
        return EigenesObjekt(c, c.P1);
    }

    /// <summary>8 — Hand control. P1 = 20000 + Platz, P2..P5.</summary>
    private bool ApplyHandLuft(in CommandRecord c)
    {
        var a = FlugzeugAusGriff(c.P1);
        if (a == null || a.Owner != c.Player || !a.Hand || a.Stored || a.Absturz) return false;
        HandLuftArm(a, c.P2, c.P3, c.P4, c.P5 != 0);
        return true;
    }

    /// <summary>9 — fire_at. P1 Einheit, P2/P3 Zelle.</summary>
    private bool ApplyFireAtBefehl(in CommandRecord c)
    {
        if (!EigenesObjekt(c, c.P1)) return false;
        bool ok = FireAtAusfuehren(c.P1, c.P2, c.P3);
        if (ok) HandKlickSchuesse++;
        else if (FireAtGrund.Length > 0 && c.Player == ViewPlayer) Say("Handsteuerung: " + FireAtGrund);
        return ok;
    }

    /// <summary>13 — Selbstzerstörung. P1 Einheit.</summary>
    private bool ApplySelbstzerstoerung(in CommandRecord c)
    {
        if (!EigenesObjekt(c, c.P1)) return false;
        var e = _entities[c.P1];
        if (e.IsBuilding || e.IsProp) return false;
        if (e.HpMax > 0 && e.Hp * 100 / e.HpMax <= 15) return false;
        Kill(c.P1, e);
        Selbstzerstoert++;
        return true;
    }
}
