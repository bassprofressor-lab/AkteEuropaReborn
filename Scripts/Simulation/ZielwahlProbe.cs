namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <b><c>--zielwahl-check</c> wird AKTIV</b> (22.10.2026, bug-600 Fortsetzung,
/// berichte/pruefstaende-ring-opus.md §2).
///
/// <para>Bis heute war <c>--zielwahl-check</c> nur eine Messzeile am Ausstieg:
/// eine Zielwahl sah man erst, wenn einer »Angriff« drückte und auf die Karte
/// klickte — ein kopfloser Lauf tut das nie, und auf K22 gehört dem Spieler der
/// Flughafen zu Beginn gar nicht (Hangar leer). Das Ergebnis war immer »NICHT
/// GEMESSEN«.</para>
///
/// <para>Jetzt stellt der Prüfstand den Fall selbst her, über die Spielerwege:</para>
/// <list type="number">
///   <item>⚠ EINGRIFF: Flughafen übergeben und Lager füllen (wie
///   <c>--ring-nacharbeit-check</c> und <c>--flughafenfenster-bild</c>).</item>
///   <item>Kauf über den Fensterknopf (Satz 501) und <c>RingLeeren</c>.</item>
///   <item>Fall Z (Modus 1): Taste 5 »Angriff« (<see cref="ZielwahlBeginnen"/>, im
///   Original KEIN Busbefehl @0x449A66), Klick auf eine leere Zelle
///   (<see cref="ZielwahlKlick"/> → Satz 502 je Maschine), Ring leeren. Gestartet
///   wird am HANGAR gezählt (vorher − nachher), nicht an den Sätzen.</item>
///   <item>Warten, bis der Auftrag mit der Heimkehr endet und die Maschine wieder
///   im Hangar steht.</item>
///   <item>Fall F (Modus 7): dieselbe Kette auf den nächsten FEIND.</item>
/// </list>
/// <para><b>Urteil</b> (in <see cref="ZielwahlProbeUrteil"/>, am Ausstieg): jede
/// begonnene Zielwahl endet genau einmal, beide Modi kamen vor,
/// <c>ZielwahlMaschinen</c> = am Hangar gezählte Starts, mindestens eine
/// Heimkehr und mindestens eine gelandete Maschine.</para>
/// <para>Nullmodelle: <c>--zielwahl-aus</c> (nichts beginnt → DURCHGEFALLEN),
/// <c>--angriff-ohne-heimkehr</c> (keine Heimkehr, nichts landet → DURCHGEFALLEN).</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    private int _zwpStufe = -1;
    private int _zwpFlughafen = -1, _zwpGestartet, _zwpHeimVor, _zwpTakte;
    private bool _zwpKeinUrteil;
    private int _zwpZHeim = -1, _zwpZGelandet = -1;
    private readonly System.Text.StringBuilder _zwpLog = new();

    /// <summary>Die Wartezeit je Fall in Bildtakten (60/s): 90 s.</summary>
    private const int ZwpWarten = 90 * 60;

    /// <summary>Je Bild aus dem MapViewer, solange <c>--zielwahl-check</c> läuft.
    /// Gibt true zurück, wenn die Probe fertig ist (dann darf beendet werden).</summary>
    public bool ZielwahlProbeTakt()
    {
        if (_zwpStufe == -1)
        {
            _zwpStufe = 0;
            return ZwpAufbauUndFallZ();
        }
        if (_zwpStufe == 1)
        {
            // auf die Heimkehr aus Fall Z warten
            if (++_zwpTakte < ZwpWarten && !ZwpZurueck()) return false;
            _zwpZHeim = AngriffHeimkehr - _zwpHeimVor;
            _zwpZGelandet = ZwpImHangar();
            _zwpLog.AppendLine($"  Fall Z: nach {_zwpTakte} Bildern Heimkehr {_zwpZHeim}, "
                             + $"im Hangar {_zwpZGelandet}");
            return ZwpFallF();
        }
        if (_zwpStufe == 2)
        {
            if (++_zwpTakte < ZwpWarten && !ZwpZurueck()) return false;
            _zwpLog.AppendLine($"  Fall F: nach {_zwpTakte} Bildern Heimkehr {AngriffHeimkehr - _zwpHeimVor}, "
                             + $"im Hangar {ZwpImHangar()}");
            _zwpStufe = 3;
            return true;
        }
        return _zwpStufe == 3;
    }

    private int ZwpImHangar()
    {
        if (_zwpFlughafen < 0 || _zwpFlughafen >= _entities.Count) return 0;
        return _entities[_zwpFlughafen].Hangar?.Count ?? 0;
    }

    /// <summary>Ist der Auftrag dieses Falls heimgekehrt und steht keine Maschine
    /// des Flughafens mehr in der Luft?</summary>
    private bool ZwpZurueck()
    {
        if (AngriffHeimkehr <= _zwpHeimVor) return false;
        var e = _entities[_zwpFlughafen];
        foreach (var a in _special)
            if (!a.Dead && !a.Absturz && a.HomeSlot == e.Slot && !a.Stored) return false;
        return true;
    }

    private bool ZwpAufbauUndFallZ()
    {
        _zwpLog.AppendLine("zielwahl-probe (aktiv, bug-600)");
        int fh = FlughafenIndex();
        if (fh < 0 || _nav == null)
        {
            _zwpKeinUrteil = true;
            _zwpLog.AppendLine("  KEIN URTEIL: kein Flughafen auf dieser Karte");
            _zwpStufe = 3;
            return true;
        }
        _zwpFlughafen = fh;
        var e = _entities[fh];
        if (e.Owner != ViewPlayer)
        {
            FlughafenUebergeben(fh, ViewPlayer);                  // ⚠ EINGRIFF
            _zwpLog.AppendLine($"  ⚠ EINGRIFF: Flughafen Platz {e.Slot} auf ({e.Col},{e.Row}) an Spieler {ViewPlayer} uebergeben");
        }
        e.StockW += 500; e.StockF += 500; e.StockS += 500;          // ⚠ EINGRIFF
        _zwpLog.AppendLine("  ⚠ EINGRIFF: Lager +500/+500/+500 fuer den Kauf");
        if ((e.Hangar?.Count ?? 0) == 0)
        {
            PostenAnwaehlenWieKlick(fh);
            var st = BuildingWindowData();
            int kauf = -1;
            if (st != null)
                for (int k = 0; k < st.Angebote.Count; k++)
                    if (st.Angebote[k].Kaufen != null && st.Angebote[k].Bezahlbar) { kauf = k; break; }
            if (kauf < 0)
            {
                _zwpKeinUrteil = true;
                _zwpLog.AppendLine("  KEIN URTEIL: kein bezahlbares Angebot im Flughafenfenster");
                _zwpStufe = 3;
                return true;
            }
            st!.Angebote[kauf].Kaufen!();                           // Satz 501 ueber den Fensterknopf
            int t = RingLeeren();
            _zwpLog.AppendLine($"  Kauf »{st.Angebote[kauf].Name}« ueber den Fensterknopf (Ring {t} Takt): Hangar {e.Hangar?.Count ?? 0}");
        }
        if ((e.Hangar?.Count ?? 0) == 0)
        {
            _zwpKeinUrteil = true;
            _zwpLog.AppendLine("  KEIN URTEIL: nichts im Hangar");
            _zwpStufe = 3;
            return true;
        }

        // Fall Z: eine leere Zelle ein paar Felder vom Flughafen (kurze Flugzeit),
        // und zwar eine RUHIGE: kein Feind im doppelten Sichtkreis. ⚠ Eine
        // bewaffnete Maschine sucht sich unterwegs selbst Ziele im Sichtkreis
        // (Zielsuche vor dem Auftragsende), und dann endet der Auftrag erst mit
        // leerer Munition — ohne die Heimkehr, die dieser Fall messen soll.
        int sicht = 0;
        foreach (var a in _special)
            if (!a.Dead && a.HomeSlot == e.Slot) sicht = System.Math.Max(sicht, a.Sight);
        int ruhe = 2 * System.Math.Max(sicht, 6) + 2;
        bool Ruhig(int c, int r)
        {
            foreach (var q in _entities)
                if (!q.Dead && !q.IsProp && q.Owner is >= 0 and <= 7 && !Allied(ViewPlayer, q.Owner)
                    && System.Math.Max(System.Math.Abs(q.Col - c), System.Math.Abs(q.Row - r)) <= ruhe) return false;
            return true;
        }
        Vector2I? zelle = null;
        for (int d = 6; d < 40 && zelle == null; d++)
            foreach (var (dc, dr) in new[] { (d, 0), (-d, 0), (0, d), (0, -d), (d, d), (-d, -d), (d, -d), (-d, d) })
            {
                int c = e.Col + dc, r = e.Row + dr;
                if (!_nav.InBounds(c, r) || ZielAufZelle(c, r) >= 0 || !Ruhig(c, r)) continue;
                zelle = new Vector2I(c, r); break;
            }
        if (zelle == null)
            _zwpLog.AppendLine($"  (keine ruhige Zelle im Umkreis 40 — Feind im Umkreis {ruhe} ueberall)");
        for (int d = 6; d < 20 && zelle == null; d++)
            foreach (var (dc, dr) in new[] { (d, 0), (-d, 0), (0, d), (0, -d) })
            {
                int c = e.Col + dc, r = e.Row + dr;
                if (!_nav.InBounds(c, r) || ZielAufZelle(c, r) >= 0) continue;
                zelle = new Vector2I(c, r); break;
            }
        if (zelle == null)
        {
            _zwpKeinUrteil = true;
            _zwpLog.AppendLine("  KEIN URTEIL: keine leere Zelle nahe dem Flughafen");
            _zwpStufe = 3;
            return true;
        }
        ZwpZielwahl('Z', zelle.Value.X, zelle.Value.Y);
        if (_zwpStufe == 3) return true;
        _zwpStufe = 1;
        return false;
    }

    private bool ZwpFallF()
    {
        var e = _entities[_zwpFlughafen];
        int feind = -1; float best = float.MaxValue;
        for (int i = 0; i < _entities.Count; i++)
        {
            var q = _entities[i];
            if (q.Dead || q.IsProp || q.IsBuilding || Untergestellt(q)) continue;
            if (q.Owner is < 0 or > 7 || Allied(ViewPlayer, q.Owner)) continue;
            float d = q.Pos.DistanceSquaredTo(e.Pos);
            if (d < best) { best = d; feind = i; }
        }
        if (feind < 0)
        {
            _zwpLog.AppendLine("  Fall F: kein Feind auf der Karte — Modus 7 NICHT GEMESSEN");
            _zwpStufe = 3;
            return true;
        }
        var f = _entities[feind];
        ZwpZielwahl('F', f.Col, f.Row);
        if (_zwpStufe == 3) return true;
        _zwpStufe = 2;
        return false;
    }

    /// <summary>Taste 5 und der Kartenklick, dann Ring leeren; gezählt am Hangar.</summary>
    private void ZwpZielwahl(char fall, int col, int row)
    {
        var e = _entities[_zwpFlughafen];
        int imHangar = e.Hangar?.Count ?? 0;
        int zielVor = ZielwahlAufZiel, zelleVor = ZielwahlAufZelle;
        _zwpHeimVor = AngriffHeimkehr;
        _zwpTakte = 0;
        bool beg = ZielwahlBeginnen(e.Slot, 0xFF);
        if (!beg)
        {
            _zwpLog.AppendLine($"  Fall {fall}: Zielwahl NICHT begonnen »{_order}«"
                             + (ZielwahlAus ? " [--zielwahl-aus]" : ""));
            _zwpStufe = 3;
            return;
        }
        ZielwahlKlick(col, row);
        int t = RingLeeren();
        // Gezaehlt am ZUSTAND: Maschinen der Staffel, die jetzt DIESEN Auftrag
        // tragen (Angriffsauftrag, Flugziel = Mitte der Klickzelle). Das sind die
        // aus dem Hangar gestarteten UND die in der Luft umgelenkten (uk 1 →
        // retarget, 0x426020) — beides zaehlt ZielwahlMaschinen.
        var mitte = ZellMitte(col, row);
        int auftrag = 0;
        foreach (var a in StaffelMaschinen(e.Slot, 0xFF))
            if (!a.Dead && !a.Stored && a.Angriffsauftrag && a.Goal is { } g && g.IsEqualApprox(mitte)) auftrag++;
        int los = imHangar - (e.Hangar?.Count ?? 0);
        _zwpGestartet += auftrag;
        string modus = ZielwahlAufZiel > zielVor ? "Modus 7 (ZIEL)" : ZielwahlAufZelle > zelleVor ? "Modus 1 (ZELLE)" : "?";
        _zwpLog.AppendLine($"  Fall {fall}: Klick auf ({col},{row}) -> {modus}, Ring {t} Takt, "
                         + $"aus dem Hangar gestartet {los} (Hangar {imHangar} -> {e.Hangar?.Count ?? 0}), "
                         + $"mit diesem Auftrag {auftrag}, »{_order}«");
    }

    /// <summary>Das Urteil am Ausstieg (nach <see cref="ZielwahlAuskunft"/>).</summary>
    public string ZielwahlProbeUrteil()
    {
        var sb = new System.Text.StringBuilder(_zwpLog.ToString());
        if (_zwpKeinUrteil) return sb.Append("  zielwahl-check: KEIN URTEIL").ToString();
        int enden = ZielwahlAufZiel + ZielwahlAufZelle + ZielwahlAbgebrochen;
        // ⚠ Die tragende Zahl ist Fall Z: der Auftrag auf die ruhige Zelle endet
        // mit der Heimkehr, und die Maschine steht DANACH im Hangar, binnen der
        // Wartezeit. Am Ende des ganzen Laufs waere eine Landung nichts wert —
        // mit leerer Munition oder Sprit fliegt jede Maschine heim, auch unter
        // --angriff-ohne-heimkehr.
        bool ok = ZielwahlBegonnen >= 2 && enden == ZielwahlBegonnen
                  && ZielwahlAufZiel >= 1 && ZielwahlAufZelle >= 1
                  && _zwpGestartet > 0 && ZielwahlMaschinen == _zwpGestartet
                  && _zwpZHeim >= 1 && _zwpZGelandet >= 1;
        sb.Append($"  Urteil: begonnen {ZielwahlBegonnen} (Soll >= 2) = Enden {enden}, Modus 7 {ZielwahlAufZiel}x / Modus 1 {ZielwahlAufZelle}x, "
                + $"gezaehlt {ZielwahlMaschinen} = mit Auftrag losgeschickt {_zwpGestartet}, Fall Z Heimkehr {_zwpZHeim} (Soll >= 1), "
                + $"Fall Z danach im Hangar {_zwpZGelandet} (Soll >= 1)"
                + (ZielwahlAus ? " [Nullmodell --zielwahl-aus]" : "")
                + (AngriffOhneHeimkehr ? " [Nullmodell --angriff-ohne-heimkehr]" : "")
                + $" — {(ok ? "BESTANDEN" : "DURCHGEFALLEN")}");
        return sb.ToString();
    }
}
