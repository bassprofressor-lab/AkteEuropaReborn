namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <c>--flug-check</c> — <b>was tut ein gekauftes Kampfflugzeug wirklich?</b>
///
/// <para>Gemeldet: »wenn ich Hubschrauber oder Flieger baue, fliegen die
/// geradlinig Richtung Norden, sogar außerhalb der Map. Außerdem kann ich die
/// Einheiten nicht anwählen.« Der zweite Teil ist offensichtlich; der erste
/// nicht — ein Flugzeug ohne Ziel fliegt geradeaus, aber WARUM es keines
/// bekommt, ist die Frage, und dafür gibt es fünf Kandidaten
/// (<c>Owner</c>, <c>Armed</c>, <c>Attack</c>, <c>AmmoMax</c>, <c>Sight</c>).</para>
///
/// <para>Der Prüfstand geht den <b>Kaufweg des Spiels</b> (<c>AirMenu</c> →
/// <c>BuyAircraft</c> → <c>LaunchAircraft</c>), nicht über einen selbstgebauten
/// Satz: ein Flugzeug, das hier anders entsteht als im Spiel, misst sich selbst.
/// Danach nennt er jeden der fünf Werte einzeln, statt nur »kein Ziel«.</para>
/// </summary>
public partial class MapEntityLayer
{
    private int _flugCheck = -1;
    private long _flugSim;
    private System.Text.StringBuilder? _flugLog;
    private Vector2 _flugStart;
    private Special? _flugProbe;

    /// <summary><c>--flug-check</c> anwerfen.</summary>
    public void FlugCheckStart() => _flugCheck = 0;

    /// <summary>Zehn Sekunden bei 60 Bildern — lang genug, dass ein Flugzeug
    /// ohne Ziel sichtbar davonfliegt, und kurz genug fuer einen Prueflauf.</summary>
    private const int FlugWartetakte = 600;

    private void PollFlugCheck()
    {
        if (_flugCheck < 0) return;
        if (_flugCheck == 0) { FlugStufe1(); return; }
        if (DebugTicks - _flugSim < FlugWartetakte) return;
        if (_flugCheck == 1) { FlugStufe2(); return; }
        FlugStufe3();
    }

    private void FlugStufe1()
    {
        _flugCheck = -1;
        var sb = new System.Text.StringBuilder("flug-check\n");
        int me = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;

        Entity? ap = null;
        string uebernommen = "";
        foreach (var e in _entities)
            if (e.IsBuilding && !e.Dead && e.BType == 9 && e.Owner == me) { ap = e; break; }
        if (ap == null)
            foreach (var e in _entities)
                if (e.IsBuilding && !e.Dead && e.BType == 9)
                {
                    ap = e; ap.Owner = ap.Team = ap.ShownOwner = me;
                    uebernommen = "(neutraler Flughafen fuer die Probe uebernommen) ";
                    break;
                }
        if (ap == null)
        {
            // ⚠⚠ OHNE FLUGHAFEN GEHT DER KAUFWEG NICHT — aber die
            // KAMPAGNENPROBE braucht ihn gar nicht. Geprüft wird dort, dass der
            // Behandler den Flugbefehl VERWIRFT, und dafür genügt ein Flugzeug,
            // das dasteht. Keine der Kampagnenkarten, die ich angesehen habe,
            // hat einen Flughafen; ohne diesen Zweig wäre die Sperre, die die
            // Kampagne originaltreu hält, schlicht NIE gemessen worden — und
            // eine ungemessene Sperre ist eine Behauptung.
            if (UI.SkirmishSetup.CampaignMission > 0)
            { FlugKampagneProbe(sb, me); return; }
            sb.Append("  KEIN URTEIL: kein Flughafen auf dieser Karte"); GD.Print(sb); return;
        }

        // ⚠ Der Kauf braucht Einzelteile — sonst misst der Prueflauf die
        // Kaufsperre und nicht den Flug.
        ap.StockW = Mathf.Max(ap.StockW, 99);
        ap.StockF = Mathf.Max(ap.StockF, 99);
        ap.StockS = Mathf.Max(ap.StockS, 99);

        // einen KAMPFtyp kaufen (Art 1 Jagdflieger, 2 Bomber, 10 Kampfhubschrauber)
        int vorher = _special.Count;
        var menu = AirMenu(ap);
        for (int i = 0; i < menu.Count; i++)
        {
            if (menu[i].Kind is not (1 or 2 or 10)) continue;
            ap.MenuIndex = i;
            if (BuyAircraft(ap)) break;
        }
        if (_special.Count == vorher)
        { sb.Append($"  KEIN URTEIL: kein Kampfflugzeug kaufbar ({_order})"); GD.Print(sb); return; }

        var a = _special[^1];
        _flugProbe = a;
        sb.AppendLine($"  {uebernommen}gekauft: {a.TypeName} (Art {a.Kind}) am Flughafen " +
                      $"Platz {ap.Slot}, Besitzer {a.Owner}");
        sb.AppendLine($"  die fuenf Werte, an denen die Zielsuche haengt:");
        sb.AppendLine($"    Owner={a.Owner} {(a.Owner is >= 0 and <= 7 ? "ok" : "⚠ AUSSERHALB 0..7 — Zielsuche faellt aus")}");
        sb.AppendLine($"    Attack={a.Attack}  AmmoMax={a.AmmoMax}  -> Armed={a.Armed} " +
                      $"{(a.Armed ? "ok" : "⚠ UNBEWAFFNET — Zielsuche faellt aus")}");
        sb.AppendLine($"    Ammo={a.Ammo} {(a.Ammo > 0 ? "ok" : "⚠ LEER — Zielsuche faellt aus")}");
        sb.AppendLine($"    Sight={a.Sight} {(a.Sight > 0 ? "ok" : "⚠ NULL — der Suchkreis ist leer")}");
        sb.AppendLine($"    Fuel={a.Fuel}/{a.FuelMax}, Dir={a.Dir}");

        int launched = LaunchAircraft(me);
        sb.Append($"  gestartet: {launched}");
        _flugStart = a.Pos;
        _flugLog = sb;
        _flugSim = DebugTicks;
        _flugCheck = 1;
    }

    private void FlugStufe2()
    {
        _flugCheck = -1;
        var sb = _flugLog ?? new System.Text.StringBuilder();
        var a = _flugProbe;
        sb.AppendLine();
        if (a == null) { sb.Append("  KEIN URTEIL: die Probe ist weg"); GD.Print(sb); return; }

        float w = _nav != null ? _nav.Width * TileW : 0, h = _nav != null ? _nav.Height * TileH : 0;
        bool drin = _nav != null && a.Pos.X >= _ox && a.Pos.X <= _ox + w &&
                                    a.Pos.Y >= _oy && a.Pos.Y <= _oy + h;
        var weg = a.Pos - _flugStart;

        sb.AppendLine($"  nach {FlugWartetakte} Simulationsschritten:");
        sb.AppendLine($"    geflogen: {weg.X:0} / {weg.Y:0} Pixel " +
                      $"({(Mathf.Abs(weg.X) < 1 ? "keine" : "")} Seitwaertsbewegung)");
        sb.AppendLine($"    auf der Karte: {(drin ? "JA" : "⚠ NEIN — hinausgeflogen")}");
        sb.AppendLine($"    Ziel={a.Target} " +
                      $"{(a.Target >= 0 ? "hat eines" : "⚠ KEINES — fliegt geradeaus")}");
        sb.AppendLine($"    Goal={(a.Goal == null ? "keins" : a.Goal.ToString())}, " +
                      $"eingelagert={a.Stored}, Sprit={a.Fuel}");
        sb.AppendLine($"    Geradeausschritte insgesamt: {AirDrifted}, " +
                      $"Umkehrungen am Rand: {AirTurnedBack}");

        if (!drin)
        { sb.Append("  flug-check: FEHLER — verlaesst die Karte"); GD.Print(sb); return; }
        sb.AppendLine("  bleibt auf der Karte — ok");

        // ---- Stufe 3: ANWAEHLEN UND BEFEHLEN, ueber den echten Klickweg ----
        //
        // ⚠ Nicht `_selAir = i` von Hand: gemeldet war »ich kann die Einheiten
        // nicht anwaehlen«, und genau das Anwaehlen ist der Gegenstand. Ein
        // Pruefstand, der es umgeht, misst es nicht.
        SelectAt(a.Pos - new Vector2(0, AirShadowDrop));
        bool gewaehlt = _selAir >= 0 && _special[_selAir] == a;
        sb.AppendLine($"  angewaehlt: {(gewaehlt ? "JA" : "⚠ NEIN — SelectAt trifft das Flugzeug nicht")}");
        if (!gewaehlt) { sb.Append("  flug-check: FEHLER — nicht anwaehlbar"); GD.Print(sb); return; }

        // ein Ziel weit weg und QUER zur bisherigen Richtung, damit »es waere
        // sowieso dorthin geflogen« als Erklaerung ausscheidet
        int zc = Mathf.Clamp((int)(a.Pos.X / TileW) + (a.Pos.X < _ox + _nav!.Width * TileW / 2 ? 20 : -20),
                             2, _nav.Width - 3);
        int zr = Mathf.Clamp((int)(a.Pos.Y / TileH), 2, _nav.Height - 3);
        _flugZiel = new Vector2I(zc, zr);
        _flugVor = a.Pos.DistanceTo(CellCenterFor(_flugZiel.Value));
        int n = PostAirMove(CellCenterFor(_flugZiel.Value));
        sb.AppendLine($"  Flugbefehl auf ({zc},{zr}), Entfernung {_flugVor / TileW:0.0} Felder: " +
                      $"{n} Satz/Saetze abgesetzt, Kampagne={UI.SkirmishSetup.CampaignMission}");
        if (n == 0) { sb.Append("  flug-check: FEHLER — kein Flugbefehl abgesetzt"); GD.Print(sb); return; }

        _flugLog = sb;
        _flugSim = DebugTicks;
        _flugCheck = 2;
    }

    /// <summary>
    /// <b>Die Gegenprobe für die KAMPAGNE</b>: der Flugbefehl darf dort nicht
    /// wirken.
    ///
    /// <para>Der Spieler hat es am 18.08.2026 noch einmal ausdrücklich gesagt:
    /// »Du darfst Kampagne nicht mit Gefecht vermischen. Kampagne bleibt
    /// Original getreu, nur Gefecht weicht ab.« Diese Probe ist der Beleg
    /// dafür — sie setzt einen Flugbefehl ab und misst, dass er NICHT
    /// ankommt.</para>
    ///
    /// <para>⚠ Das Flugzeug wird hier von Hand gesetzt statt gekauft. Für die
    /// Kaufprobe wäre das falsch (siehe Kopf), für die SPERRE ist es richtig:
    /// geprüft wird der Behandler, nicht der Weg dorthin.</para></summary>
    private void FlugKampagneProbe(System.Text.StringBuilder sb, int me)
    {
        if (_nav == null) { sb.Append("  KEIN URTEIL: keine Karte"); GD.Print(sb); return; }
        int slot = 0;
        foreach (var s in _special) slot = Mathf.Max(slot, s.Slot + 1);
        var a = new Special
        {
            Slot = slot, Kind = 1, Name = "Pruefstand", TypeName = "Pruefstand",
            Col = 10, Row = 10, Stored = false, Owner = me, HomeSlot = -1,
            Pos = CellCenter(10, 10), Speed = 20,
            Hp = 100, HpMax = 100, Ammo = 10, AmmoMax = 10, Fuel = 999, FuelMax = 999,
            Attack = 10, Defence = 1, Sight = 10,
        };
        _special.Add(a);

        SelectAt(a.Pos - new Vector2(0, AirShadowDrop));
        bool gewaehlt = _selAir >= 0 && _special[_selAir] == a;
        int vorher = AirOrdersGiven;
        int n = PostAirMove(CellCenterFor(new Vector2I(30, 10)));

        sb.AppendLine($"  KAMPAGNE (Mission {UI.SkirmishSetup.CampaignMission}) — " +
                      "Gegenprobe ohne Flughafen, Flugzeug von Hand gesetzt");
        sb.AppendLine($"    angewaehlt: {(gewaehlt ? "JA" : "nein")}");
        sb.AppendLine($"    Saetze abgesetzt: {n}   beim Behandler ANGEKOMMEN: " +
                      $"{AirOrdersGiven - vorher}");
        sb.AppendLine($"    PlayerGoal: {(a.PlayerGoal == null ? "leer — richtig" : "⚠ GESETZT")}");
        bool ok = AirOrdersGiven == vorher && a.PlayerGoal == null;
        sb.Append(ok
            ? "  flug-check: IN ORDNUNG — in der KAMPAGNE wird der Flugbefehl verworfen"
            : "  flug-check: FEHLER — der Flugbefehl wirkt in der KAMPAGNE");
        GD.Print(sb);
    }

    private Vector2I? _flugZiel;
    private float _flugVor;

    private void FlugStufe3()
    {
        _flugCheck = -1;
        var sb = _flugLog ?? new System.Text.StringBuilder();
        var a = _flugProbe;
        sb.AppendLine();
        if (a == null || _flugZiel == null || _nav == null)
        { sb.Append("  KEIN URTEIL: die Probe ist weg"); GD.Print(sb); return; }

        float jetzt = a.Pos.DistanceTo(CellCenterFor(_flugZiel.Value));
        sb.AppendLine($"  nach weiteren {FlugWartetakte} Schritten:");
        sb.AppendLine($"    Entfernung zum befohlenen Ziel: {_flugVor / TileW:0.0} -> " +
                      $"{jetzt / TileW:0.0} Felder");
        sb.AppendLine($"    Befehle abgesetzt: {AirOrdersGiven}, davon ZIEL ERREICHT: " +
                      $"{AirOrdersReached}");
        sb.AppendLine($"    PlayerGoal={(a.PlayerGoal == null ? "leer" : "noch gesetzt")}, " +
                      $"Pos({a.Pos.X:0},{a.Pos.Y:0})");

        // ⚠ Der Massstab ist die ANNAEHERUNG, nicht die Ankunft: wie weit es in
        // zehn Sekunden kommt, haengt an seiner Geschwindigkeit, und die ist
        // eine Eigenschaft des Entwurfs, keine des Befehls.
        // ⚠⚠ ZWEI ERWARTUNGEN, je nach Modus — und das ist der eigentliche
        // Zweck dieses Pruefstands. Der Flugbefehl ist eine BEWUSSTE Abweichung
        // im Gefecht; in der Kampagne muss er ABGELEHNT werden. Ein Pruefstand,
        // der nur den Erfolgsfall kennt, wuerde eine Abweichung, die in die
        // Kampagne durchsickert, nie bemerken.
        bool kampagne = UI.SkirmishSetup.CampaignMission > 0;
        bool ok;
        if (kampagne)
        {
            ok = AirOrdersGiven == 0 && AirOrdersReached == 0;
            sb.Append(ok
                ? "  flug-check: IN ORDNUNG — in der KAMPAGNE wird der Flugbefehl abgelehnt"
                : "  flug-check: FEHLER — der Flugbefehl wirkt in der KAMPAGNE");
        }
        else
        {
            // Angekommen ZAEHLT als Erfolg, auch wenn es danach weitergeflogen
            // ist — sonst haenge das Urteil daran, wie schnell der Entwurf ist
            // und wann zufaellig gemessen wurde.
            ok = AirOrdersReached > 0 || jetzt < _flugVor - TileW;
            sb.Append(ok ? "  flug-check: IN ORDNUNG — der Flugbefehl wirkt"
                         : "  flug-check: FEHLER — es fliegt nicht zum befohlenen Ziel");
        }
        GD.Print(sb);
    }
}
