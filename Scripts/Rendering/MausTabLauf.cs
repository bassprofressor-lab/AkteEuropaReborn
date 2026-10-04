namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// ⭐⭐ <b>ROLLEN MIT DER RECHTEN MAUSTASTE</b> (03.10.2026, bug-411, Meldung A von
/// KayelGee, <c>berichte/maus-tab-handsteuerung-fable.md</c> §1) — und die drei
/// Prüfstände für bug-411/412/414.
///
/// <para><b>Original (O):</b></para>
/// <code>
///   0x414328  Druck: nur wenn byte[0x8B7250]==0 (Einstellung 19 »Scrollen« AUS)
///             Rollmerker word[0x502AC0]:=1, Kameralage gemerkt [0x54077C],
///             SetCursorPos(300,200), SetCursor(0)   → Zeiger FEST und UNSICHTBAR
///   0x4B4AD0  je BILD: feinx += MausX − Anker.x ; feiny += MausY − Anker.y
///             → 1:1 Bildpunkte, SCHUBrichtung (Maus rechts → Kamera rechts),
///             Zeiger zurück auf den Anker (320,200)
///   0x414409  Loslassen: Kameralage ≠ gemerkt → NUR Rollen beenden, Zeiger an
///             die Ausgangslage (0x4144D1); gleich → Befehlsart aufheben /
///             Befehl 5 / Abwahl 0x433010. Die Schwelle ist KEINE Zahl.
/// </code>
/// <para>Bei uns: <c>Input.MouseMode = Captured</c> hält und versteckt den Zeiger
/// und liefert die Bewegung als <c>Relative</c> (das »Maus − Anker« des
/// Originals); Kamera <c>+= Relative / Zoom</c>, also 1:1 Bildschirmpunkte.
/// Beim Loslassen <c>Visible</c> und <c>WarpMouse(Ausgangslage)</c>.</para>
///
/// <para><b>Einstellung 19:</b> das Original koppelt »Randscrollen EIN ⇔ rechts
/// rollt NICHT«. Unsere passende Einstellung ist <c>Settings.RightDragPan</c>
/// (»Rechte Maustaste gedrückt halten schiebt die Karte«) — sie schaltet das
/// Rollen. ⚠ OFFEN: das RANDSCROLLEN selbst (<c>0x4B4A60</c>) gibt es bei uns
/// nicht; wer RightDragPan ausschaltet, bekommt also kein Randscrollen dafür.</para>
///
/// <para>Gegenschalter <c>--rollen-alt</c>: der Stand vor dem 03.10.2026 —
/// Schwenk ab 5 px Zugweg in GREIFrichtung mit sichtbarem Zeiger, und die
/// mittlere Taste schwenkt wieder (die hat im Original keinen Zweig).</para>
/// </summary>
public partial class MapViewer
{
    /// <summary><c>--rollen-alt</c> — altes Schwenken (Greifrichtung, 5 px,
    /// mittlere Taste schwenkt).</summary>
    public static bool RollenAlt;

    /// <summary><c>--rollen-check</c>.</summary>
    public static bool RollenCheckAn;

    /// <summary>word[0x502AC0] — es wird gerollt.</summary>
    private bool _rollen;
    /// <summary>[0x54077C] — Kameralage beim Druck.</summary>
    private Vector2 _rollKamera;
    /// <summary>[0xA31A94/98] — Zeigerlage beim Druck.</summary>
    private Vector2 _rollAusgang;

    private void RollenBeginnen(Vector2 zeiger)
    {
        _rollen = true;
        _rollKamera = _camera.Position;
        _rollAusgang = zeiger;
        // Im Prüfstand nicht: dort gibt es keinen echten Zeiger zu fangen.
        if (!RollenCheckAn) Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    /// <summary>0x4B4B05: Kamera += (Maus − Anker), 1:1 Bildpunkte.</summary>
    private void RollenBewegen(Vector2 relativ)
    {
        _camera.Position += relativ / _camera.Zoom;
        ClampCamera();
    }

    /// <summary>0x41441C/0x4144D1: Rollen beenden. Rückgabe: hat sich die
    /// Kameralage geändert (dann KEIN Abbruch, keine Abwahl)?</summary>
    private bool RollenBeenden()
    {
        bool bewegt = _camera.Position != _rollKamera;
        _rollen = false;
        if (!RollenCheckAn)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            GetViewport().WarpMouse(_rollAusgang);
        }
        return bewegt;
    }

    /// <summary>Sicherung: geht das Loslassen verloren (Fokuswechsel, Fenster
    /// darüber), darf der Zeiger nicht gefangen bleiben. ⚠ Unsere Zutat.</summary>
    private void RollenWache()
    {
        // ⚠ Nicht kopflos: dort kommen die Mausereignisse der Prüfstände über
        // PushInput und erreichen Godots Tastenzustand nie (--maus-check fiel
        // daran durch — der Rechtsklick wurde zwischen Druck und Loslassen
        // »verloren« gemeldet und wählte nicht mehr ab).
        if (_rollen && !RollenCheckAn && DisplayServer.GetName() != "headless"
            && !Input.IsMouseButtonPressed(MouseButton.Right))
        {
            RollenBeenden();
            _rightDown = false; _rightDrag = false;
        }
    }

    // ---- --rollen-check ----------------------------------------------------

    /// <summary><c>--rollen-check</c>: echte Mausereignisse an _UnhandledInput.
    /// Soll neu: Zug rechts → Kamera nach RECHTS um genau Zug/Zoom, Auswahl
    /// bleibt; 1-px-Wackler → Kamera bewegt, KEINE Abwahl; ruhiger Klick →
    /// Abwahl; mittlere Taste → Kamera steht. Nullmodell <c>--rollen-alt</c>:
    /// Kamera nach LINKS, Wackler wählt ab, mittlere Taste schwenkt.</summary>
    private async System.Threading.Tasks.Task RollenLauf()
    {
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var sb = new System.Text.StringBuilder($"rollen-check{(RollenAlt ? " (--rollen-alt)" : "")}"
                                             + $" RightDragPan={UI.Settings.RightDragPan}\n");
        int a = _entities.TabBalkenProbeWaehle();
        if (a < 0) { GD.Print(sb.Append("  keine eigene Einheit — DURCHGEFALLEN")); GetTree().Quit(0); return; }
        // Kamera auf die eigene Einheit, damit die Randklammer nicht greift.
        var mitte = MitteDerKarte();
        _camera.Position = mitte;
        ClampCamera();
        for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var p = GetViewport().GetVisibleRect().Size / 2f;

        async System.Threading.Tasks.Task Taste(MouseButton k, bool druck, Vector2 wo)
        {
            GetViewport().PushInput(new InputEventMouseButton
                { ButtonIndex = k, Pressed = druck, Position = wo, GlobalPosition = wo }, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        async System.Threading.Tasks.Task Zug(Vector2 von, Vector2 rel, MouseButtonMask maske)
        {
            var nach = von + rel;
            GetViewport().PushInput(new InputEventMouseMotion
                { Position = nach, GlobalPosition = nach, Relative = rel, ButtonMask = maske }, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        string Kam() => $"({_camera.Position.X:0.##},{_camera.Position.Y:0.##})";

        // 1. Zug rechts: 3 × 20 px
        _entities.TabBalkenProbeWaehle();
        var k0 = _camera.Position;
        await Taste(MouseButton.Right, true, p);
        var q = p;
        for (int s = 0; s < 3; s++) { await Zug(q, new Vector2(20, 0), MouseButtonMask.Right); q += new Vector2(20, 0); }
        await Taste(MouseButton.Right, false, q);
        float dx = _camera.Position.X - k0.X;
        float soll = 60f / _camera.Zoom.X;
        bool schub = Mathf.Abs(dx - soll) < 0.01f;
        bool bleibt1 = _entities.Selection.Count > 0;
        sb.Append($"  1. Zug rechts 60 px: Kamera {k0.X:0.##} -> {_camera.Position.X:0.##} (dx {dx:0.##}, "
                + $"Soll Schub +{soll:0.##}), Auswahl [{string.Join(",", _entities.Selection)}]\n");

        // 2. Wackler 1 px
        _entities.TabBalkenProbeWaehle();
        var k1 = _camera.Position;
        await Taste(MouseButton.Right, true, p);
        await Zug(p, new Vector2(1, 0), MouseButtonMask.Right);
        await Taste(MouseButton.Right, false, p + new Vector2(1, 0));
        bool wackBewegt = _camera.Position != k1;
        bool bleibt2 = _entities.Selection.Count > 0;
        sb.Append($"  2. Wackler 1 px: Kamera bewegt {(wackBewegt ? "ja" : "nein")}, "
                + $"Auswahl [{string.Join(",", _entities.Selection)}]\n");

        // 3. ruhiger Rechtsklick
        _entities.TabBalkenProbeWaehle();
        var k2 = _camera.Position;
        await Taste(MouseButton.Right, true, p);
        await Taste(MouseButton.Right, false, p);
        bool abwahl = _entities.Selection.Count == 0;
        sb.Append($"  3. ruhiger Klick: Kamera {(k2 == _camera.Position ? "steht" : "BEWEGT")}, "
                + $"Auswahl [{string.Join(",", _entities.Selection)}]\n");

        // 4. mittlere Taste
        var k3 = _camera.Position;
        await Taste(MouseButton.Middle, true, p);
        await Zug(p, new Vector2(30, 0), MouseButtonMask.Middle);
        await Taste(MouseButton.Middle, false, p + new Vector2(30, 0));
        bool mitteSchwenkt = _camera.Position != k3;
        sb.Append($"  4. mittlere Taste 30 px: Kamera {(mitteSchwenkt ? "schwenkt " + Kam() : "steht")}\n");

        // Dieselbe Soll-Zeile für beide Läufe: das Nullmodell muss an den Zahlen
        // durchfallen (Greifrichtung, Wackler wählt ab, Mitte schwenkt).
        bool ok = schub && bleibt1 && wackBewegt && bleibt2 && abwahl && !mitteSchwenkt;
        sb.Append($"  Gegenschalter --rollen-alt: {RollenAlt}\n");
        sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
        GD.Print(sb.ToString());
        GetTree().Quit(0);
    }

    /// <summary>Die Mitte des Kartenbildes — dort greift die Randklammer in
    /// keiner Richtung (sonst schluckt sie den Zug des Nullmodells).</summary>
    private Vector2 MitteDerKarte()
        => _sprite?.Texture != null ? _sprite.Texture.GetSize() / 2f : _camera.Position;

    // ---- --tabbalken-check -------------------------------------------------

    /// <summary><c>--tabbalken-check</c>: Tab als echtes Tastenereignis, viermal.
    /// Je Stellung gezählt: Balken über eigenen/fremden Einheiten, Sprit über
    /// eigenen, Fußvolk mit Sprit/Munition, Formelfehler (Breite/Füllung/Höhe
    /// gegen die Zeilen des Berichts), 2-px-Stummel. Eine eigene Einheit ist
    /// angewählt, damit das Nullmodell in Stellung 0 sichtbar wird.
    /// Nullmodelle: <c>--balkenmodus-alt</c> und <c>--tab-alt</c> → Stellung 0
    /// zeigt Balken (die angewählte Einheit) → DURCHGEFALLEN.</summary>
    private async System.Threading.Tasks.Task TabbalkenLauf()
    {
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var sb = new System.Text.StringBuilder("tabbalken-check"
            + (MapEntityLayer.BalkenmodusAlt ? " (--balkenmodus-alt)" : "")
            + (MapEntityLayer.TabAlt ? " (--tab-alt)" : "") + "\n");
        int a = _entities.TabBalkenProbeWaehle();
        sb.Append($"  angewaehlt: {a}, Startstellung {_entities.Balkenmodus}\n");
        bool ok = a >= 0;
        for (int schritt = 0; schritt <= 4; schritt++)
        {
            int st = _entities.Balkenmodus;
            var z = _entities.TabBalkenZaehlen();
            int erwartet = schritt % 4;
            bool gut = st == erwartet && z.formelFehler == 0 && erwartet switch
            {
                0 => z.eigene + z.fremde == 0,
                1 => z.eigene > 0 && z.fremde > 0,
                2 => z.eigene > 0 && z.fremde == 0 && z.fussMitSprit == 0 && z.eigeneSprit > 0,
                _ => z.eigene > 0 && z.fremde == 0 && z.fussMitSprit == 0,
            };
            ok &= gut;
            sb.Append($"  Stellung {st} (Soll {erwartet}): eigene {z.eigene}, fremde {z.fremde}, "
                    + $"Fussvolk mit Sprit/Mun {z.fussMitSprit}, Formelfehler {z.formelFehler}, "
                    + $"2-px-Stummel {z.stummel} -> {(gut ? "ja" : "NEIN")}\n");
            if (schritt == 4) break;
            GetViewport().PushInput(new InputEventKey
                { Keycode = Key.Tab, PhysicalKeycode = Key.Tab, Pressed = true }, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().PushInput(new InputEventKey
                { Keycode = Key.Tab, PhysicalKeycode = Key.Tab, Pressed = false }, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
        GD.Print(sb.ToString());
        GetTree().Quit(0);
    }

    // ---- --handsteuerung-boden-check ---------------------------------------

    /// <summary>Probe-Haken: gilt als gehaltene Pfeiltaste, falls Godots
    /// Tastenzustand im kopflosen Lauf nicht mitkommt (wird gemeldet).</summary>
    private Key _handProbeTaste = Key.None;

    /// <summary>Gehaltene Pfeiltaste: Godots Tastenzustand (= byte[0xA182E8+VK]),
    /// im Prüfstand notfalls der Probe-Haken.</summary>
    private bool HandTaste(Key k) => Input.IsKeyPressed(k) || _handProbeTaste == k;

    /// <summary><c>--handsteuerung-boden-check</c>: ein eigenes Fahrzeug unter
    /// Handsteuerung, eine Pfeiltaste GEHALTEN (ein Druck, kein Wiederholen),
    /// die Maus quer daneben. Gezählt über die Bilder: Zellwechsel, Schritte des
    /// Takts, Aufrufe des alten PostMove-Wegs, Rohrbefehle. Soll neu: ≥ 2 Zellen,
    /// 0 × PostMove. Nullmodell <c>--handsteuerung-boden-alt</c>: PostMove &gt; 0.</summary>
    private async System.Threading.Tasks.Task HandsteuerungBodenLauf()
    {
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var sb = new System.Text.StringBuilder("handsteuerung-boden-check"
            + (MapEntityLayer.HandsteuerungBodenAlt ? " (--handsteuerung-boden-alt)" : "") + "\n");
        var st = _entities.HandBodenProbeStart();
        if (st == null) { GD.Print(sb.Append("  kein eigenes Landfahrzeug mit 3 freien Zellen — DURCHGEFALLEN")); GetTree().Quit(0); return; }
        var (idx, r) = st.Value;
        Key taste = r.X > 0 ? Key.Right : r.X < 0 ? Key.Left : r.Y > 0 ? Key.Down : Key.Up;
        var z0 = _entities.HandBodenProbeZelle(idx);
        // Maus quer zur Fahrt, drei Zellen daneben: das Rohr soll dorthin.
        var quer = new Vector2(-r.Y, r.X) * 120f;
        _mausProbePunkt = _entities.HandBodenProbePos(idx) + quer;
        int aimVor = _entities.ProbeTurmVon(idx);
        Input.ParseInputEvent(new InputEventKey { Keycode = taste, PhysicalKeycode = taste, Pressed = true });
        for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        bool godotTaste = Input.IsKeyPressed(taste);
        if (!godotTaste) _handProbeTaste = taste;
        int wechsel = 0;
        var letzte = z0;
        int bilder = 0;
        for (; bilder < 60 * 12; bilder++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            _mausProbePunkt = _entities.HandBodenProbePos(idx) + quer;
            var z = _entities.HandBodenProbeZelle(idx);
            if (z != letzte) { wechsel++; letzte = z; }
            if (wechsel >= 3) break;
        }
        Input.ParseInputEvent(new InputEventKey { Keycode = taste, PhysicalKeycode = taste, Pressed = false });
        _handProbeTaste = Key.None;
        // nach dem Loslassen: der laufende Schritt endet, dann steht sie
        for (int i = 0; i < 120; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var zEnde = _entities.HandBodenProbeZelle(idx);
        int nachher = Mathf.Abs(zEnde.X - letzte.X) + Mathf.Abs(zEnde.Y - letzte.Y);
        int aimNach = _entities.ProbeTurmVon(idx);
        _mausProbePunkt = null;
        var e = _entities;
        sb.Append($"  Einheit {idx} bei ({z0.X},{z0.Y}), Taste {taste} gehalten "
                + $"(Godot-Tastenzustand {(godotTaste ? "ja" : "NEIN — Probe-Haken")}), {bilder} Bilder\n");
        sb.Append($"  Zellwechsel {wechsel} -> ({letzte.X},{letzte.Y}), nach dem Loslassen noch {nachher} Zelle(n) "
                + $"-> ({zEnde.X},{zEnde.Y})\n");
        sb.Append($"  Takt: {e.HandBodenSchritte} Schritte gelegt, {e.HandBodenBlockiert} Takte blockiert, "
                + $"{e.HandBodenRohr} Rohrbefehle (Turm {aimVor} -> {aimNach}, Maus {e.HandRohrRichtung}); "
                + $"alter Weg PostMove {e.HandBodenPostMove}x\n");
        // Dieselbe Bedingung für beide Läufe — das Nullmodell muss an den ZAHLEN
        // durchfallen (PostMove > 0), nicht an einer festen Zeile.
        bool ok = wechsel >= 2 && e.HandBodenPostMove == 0 && e.HandBodenSchritte >= 2 && nachher <= 1;
        sb.Append($"  Gegenschalter --handsteuerung-boden-alt: {MapEntityLayer.HandsteuerungBodenAlt}"
                + (MapEntityLayer.HandsteuerungBodenAlt ? " (Nullmodell: PostMove > 0 erwartet -> DURCHGEFALLEN)" : "") + "\n");
        sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
        GD.Print(sb.ToString());
        GetTree().Quit(0);
    }
}
