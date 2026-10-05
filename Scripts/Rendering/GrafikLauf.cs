using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// ⭐ <b>PAKET 4 GRAFIK — die Kommandozeile</b> (04.10.2026, bug-426/427).
/// <list type="bullet">
/// <item><c>--truemmer-check</c> (Simulation/TruemmerCheck.cs); mit
/// <c>--shot=…png</c> ein Bildlauf: EINE Explosion zu acht Zeitpunkten
/// (<c>…_t00.png</c> … <c>…_t60.png</c>, Effekttakte).</item>
/// <item>Gegenschalter <c>--truemmer-bogen-alt</c>, <c>--truemmer-uhr-alt</c>,
/// <c>--truemmer-aufschlag-aus</c>, <c>--todesklang-alt</c>,
/// <c>--sterbend-alt</c>, <c>--wrack-alt</c>, <c>--wrack-gattung-alt</c>
/// (bug-432: auch Gattung 2/3 legt ein Wrack).</item>
/// <item>⭐ bug-433: <c>--schiffstod-check</c> (Simulation/Schiffstod.cs), mit
/// <c>--shot=…png</c> ein Bildlauf eines Untergangs (<c>…_vorher.png</c>,
/// <c>…_t00.png</c> … <c>…_t60.png</c>, <c>…_nachher.png</c>); waehlbar
/// <c>--schiffstod-gattung=3|4|5</c>. Gegenschalter <c>--schiffstod-alt</c>.</item>
/// <item>⭐ bug-434: <c>--todeslicht-check</c> (Simulation/Todeslicht.cs), mit
/// <c>--shot=…png</c> ein Bildlauf (<c>…_vorher.png</c>, <c>…_t00.png</c> …
/// <c>…_t80.png</c>, Spieltakte). Gegenschalter <c>--todeslicht-aus</c>.</item>
/// <item><c>--nebelkante-check[=c,r]</c> (Rendering/NebelKante.cs); mit
/// <c>--shot=…png</c> das Bild an seiner Stelle (K1: 11,47, Zoom 1,6).</item>
/// <item>Gegenschalter <c>--nebel-rampe-alt</c>;
/// Nullmodell <c>--nebel-flach</c> (besteht seit dem 11.09.2026).</item>
/// </list>
/// ⚠ Beide Pruefstaende rufen selbst <c>Quit</c> — kein <c>--quit-after</c>.
/// </summary>
public partial class MapViewer
{
    private bool _truemmerCheck, _nebelkanteCheck, _schiffstodCheck, _todeslichtCheck, _kopfCheck;
    private int _schiffstodGattung;
    private Vector2I? _nebelkanteOrt;

    /// <summary>true = Schalter erkannt.</summary>
    private bool GrafikSchalter(string a)
    {
        switch (a)
        {
            case "--truemmer-check": _truemmerCheck = true; return true;
            case "--truemmer-bogen-alt": MapEntityLayer.TruemmerBogenAlt = true; return true;
            case "--truemmer-uhr-alt": MapEntityLayer.TruemmerUhrAlt = true; return true;
            case "--truemmer-aufschlag-aus": MapEntityLayer.TruemmerAufschlagAus = true; return true;
            case "--todesklang-alt": MapEntityLayer.TodesklangAlt = true; return true;
            case "--sterbend-alt": MapEntityLayer.SterbendAlt = true; return true;
            case "--wrack-alt": MapEntityLayer.WrackAlt = true; return true;
            case "--wrack-gattung-alt": MapEntityLayer.WrackGattungAlt = true; return true;   // bug-432
            case "--nebel-rampe-alt": MapEntityLayer.NebelRampeAlt = true; return true;
            case "--nebelkante-check": _nebelkanteCheck = true; return true;
            case "--schiffstod-check": _schiffstodCheck = true; return true;              // bug-433
            case "--schiffstod-alt": MapEntityLayer.SchiffstodAlt = true; return true;   // bug-433
            case "--todeslicht-check": _todeslichtCheck = true; return true;             // bug-434
            case "--todeslicht-aus": MapEntityLayer.TodeslichtAus = true; return true;   // bug-434
            case "--nebeltakt-alt": MapEntityLayer.NebeltaktAlt = true; return true;     // bug-435
            case "--kopf-alt": MapEntityLayer.KopfAlt = true; return true;               // bug-436
            case "--kopf-check": _kopfCheck = true; return true;                         // bug-436
        }
        if (a.StartsWith("--schiffstod-gattung=") && int.TryParse(a["--schiffstod-gattung=".Length..], out int sg))
        { _schiffstodGattung = sg; return true; }
        if (a.StartsWith("--nebelkante-check="))
        {
            var q = a["--nebelkante-check=".Length..].Split(',');
            if (q.Length == 2 && int.TryParse(q[0], out int c) && int.TryParse(q[1], out int r))
                _nebelkanteOrt = new Vector2I(c, r);
            _nebelkanteCheck = true;
            return true;
        }
        return false;
    }

    /// <summary>Nach dem Laden: true = ein Pruefstand lief (und beendet).</summary>
    private bool GrafikPruefstand()
    {
        if (_truemmerCheck)
        {
            if (_shotPath.Length > 0) { _ = TruemmerBildLauf(); return true; }
            GD.Print(_entities.TruemmerCheck());
            GetTree().Quit(0);
            return true;
        }
        if (_schiffstodCheck)
        {
            if (_shotPath.Length > 0) { _ = SchiffstodBildLauf(); return true; }
            GD.Print(_entities.SchiffstodCheck());
            GetTree().Quit(0);
            return true;
        }
        if (_todeslichtCheck)
        {
            if (_shotPath.Length > 0) { _ = TodeslichtBildLauf(); return true; }
            GD.Print(_entities.TodeslichtCheck());
            GetTree().Quit(0);
            return true;
        }
        if (_kopfCheck)                                                  // bug-436
        {
            GD.Print(_entities.KopfCheck());
            GetTree().Quit(0);
            return true;
        }
        if (_nebelkanteCheck)
        {
            if (_shotPath.Length > 0) { _ = NebelkanteBildLauf(); return true; }
            GD.Print(_entities.NebelkanteCheck(_nebelkanteOrt));
            GetTree().Quit(0);
            return true;
        }
        return false;
    }

    /// <summary>Eine Explosion zu mehreren Zeitpunkten (Effekttakte 0, 2, 4, 6,
    /// 10, 16, 24, 40, 60), Baum angehalten.</summary>
    private async System.Threading.Tasks.Task TruemmerBildLauf()
    {
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetTree().Paused = true;
        var p = _entities.TruemmerBildVorbereiten(out int vi);
        if (p != null) { _camera.Position = p.Value; _camera.Zoom = new Vector2(2.5f, 2.5f); }
        for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _entities.TruemmerBildSprengen(vi);
        int stand = 0;
        foreach (int t in new[] { 0, 2, 4, 6, 10, 16, 24, 40, 60 })
        {
            _entities.TruemmerBildTakte(t - stand);
            stand = t;
            for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            string pfad = _shotPath.Replace(".png", $"_t{t:00}.png");
            GetViewport().GetTexture().GetImage().SavePng(pfad);
            GD.Print($"truemmer-bild: {pfad}");
        }
        GetTree().Quit(0);
    }

    /// <summary>bug-433: ein Untergang zu mehreren Zeitpunkten, Baum angehalten.
    /// »vorher« vor dem Tod, »t06« = Rumpf eben weg, »nachher« nach 150 Takten.</summary>
    private async System.Threading.Tasks.Task SchiffstodBildLauf()
    {
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetTree().Paused = true;
        var p = _entities.SchiffstodBildVorbereiten(_schiffstodGattung, out int vi);
        if (p == null) { GD.Print("schiffstod-bild: keine Einheit der Gattung 3/4/5"); GetTree().Quit(0); return; }
        _camera.Position = p.Value; _camera.Zoom = new Vector2(2f, 2f);
        GD.Print($"schiffstod-bild: {_entities.SchiffstodBildName(vi)}");
        for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng(_shotPath.Replace(".png", "_vorher.png"));
        _entities.TruemmerBildSprengen(vi);
        int stand = 0;
        foreach (int t in new[] { 0, 2, 4, 6, 10, 16, 24, 40, 60, 150 })
        {
            _entities.TruemmerBildTakte(t - stand);
            stand = t;
            for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            string pfad = _shotPath.Replace(".png", t == 150 ? "_nachher.png" : $"_t{t:00}.png");
            GetViewport().GetTexture().GetImage().SavePng(pfad);
            GD.Print($"schiffstod-bild: {pfad}");
        }
        GetTree().Quit(0);
    }

    /// <summary>bug-434: die Lichtquelle eines eigenen Fahrzeugs (80 Takte) zu
    /// mehreren Spieltakten, Baum angehalten. »vorher« = lebend, t00 = im
    /// Todestakt, t79 = letzter offener Takt, t80 = zu.</summary>
    private async System.Threading.Tasks.Task TodeslichtBildLauf()
    {
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetTree().Paused = true;
        var p = _entities.TodeslichtBildVorbereiten(out int vi);
        if (p == null) { GD.Print("todeslicht-bild: kein eigenes Fahrzeug / keine abseitige Zelle"); GetTree().Quit(0); return; }
        _camera.Position = p.Value; _camera.Zoom = new Vector2(1.5f, 1.5f);
        GD.Print($"todeslicht-bild: {_entities.TodeslichtBildName(vi)}");
        for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng(_shotPath.Replace(".png", "_vorher.png"));
        _entities.TodeslichtBildToeten(vi);
        int stand = 0;
        foreach (int t in new[] { 0, 20, 40, 72, 76, 79, 80 })
        {
            if (t > stand) _entities.TodeslichtBildTakte(t - stand);
            stand = t;
            for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            string pfad = _shotPath.Replace(".png", $"_t{t:00}.png");
            GetViewport().GetTexture().GetImage().SavePng(pfad);
            GD.Print($"todeslicht-bild: {pfad}");
        }
        GetTree().Quit(0);
    }

    /// <summary>Das Bild an seiner Stelle: Einheit auf (11,47), Kamera wie sein
    /// Bild (Kartenbildpunkt 104/904 links oben, Zoom 1,6), dann die Zeile.</summary>
    private async System.Threading.Tasks.Task NebelkanteBildLauf()
    {
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetTree().Paused = true;
        var p = _entities.NebelkanteBildVorbereiten(_nebelkanteOrt);
        // sein Bild: Bildmitte 24 px rechts und 8 px unter der Zellmitte des Panzers
        _camera.Position = p + new Vector2(24, 8);
        _camera.Zoom = new Vector2(1.6f, 1.6f);
        for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng(_shotPath);
        GD.Print($"nebelkante-bild: {_shotPath}");
        GD.Print(_entities.NebelkanteCheck(_nebelkanteOrt));
        GetTree().Quit(0);
    }
}
