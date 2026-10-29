namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// ⭐ 29.10.2026 — <b>Paket P4 (Kampf) der Testerrunde 0.6.12</b> (KayelGee R8 K3/K4/K16,
/// Reaper R5; <c>berichte/testerrunde-0-6-12.md</c>, Bau: <c>berichte/bau-p4-0-6-12.md</c>):
/// die Schalter, die Pruefstaende und der Bildlauf an einer Stelle.
/// </summary>
public partial class MapViewer
{
    private bool _p4Gf, _p4Sr, _p4Bs, _p4Af;
    private string _p4BsBild = "";

    /// <summary>Die Schalter von P4; true = verbraucht.</summary>
    private bool P4Schalter(string a)
    {
        switch (a)
        {
            case "--gelegenheitsfeuer-alt": MapEntityLayer.GelegenheitsfeuerAlt = true; return true;   // bug-645/646
            case "--sichtradius-alt": MapEntityLayer.SichtradiusAlt = true; return true;               // bug-669
            case "--sichtradius-bloss": MapEntityLayer.SichtradiusBloss = true; return true;           // bug-669 Nullmodell
            case "--bodenstrahl-alt": MapEntityLayer.BodenstrahlAlt = true; return true;               // bug-658
            case "--angriff-halt-alt": MapEntityLayer.AngriffHaltAlt = true; return true;              // P4-Zusatz
            case "--weg-vormerkung-alt": MapEntityLayer.WegVormerkungAlt = true; return true;          // P4-Nacharbeit
            case "--angriff-fahrt-check": _p4Af = true; return true;
            case "--gelegenheitsfeuer-check": _p4Gf = true; return true;
            case "--sichtradius-check": _p4Sr = true; return true;
            case "--bodenstrahl-check": _p4Bs = true; return true;
        }
        if (a.StartsWith("--bodenstrahl-bild=")) { _p4BsBild = a["--bodenstrahl-bild=".Length..]; UI.HelpWindow.Suppressed = true; return true; }
        return false;
    }

    /// <summary>Die synchronen Pruefstaende von P4; true = einer lief (und beendet).</summary>
    private bool P4Pruefstaende()
    {
        if (_p4BsBild.Length > 0)
        {
            // Bildlauf: aufbauen, die Kamera auf den Schuetzen; P4BildTakt fotografiert
            var log = new System.Text.StringBuilder();
            var z = _entities.BodenstrahlAufbau(4, log);
            GD.Print(log.ToString().TrimEnd());
            if (z is { } p) { _camera.Position = p; _camera.Zoom = new Vector2(3, 3); ClampCamera(); }
            else { GetTree().Quit(0); return true; }
            return false;
        }
        string zeile = null;
        if (_p4Gf) zeile = _entities.GelegenheitsfeuerCheck();
        else if (_p4Sr) zeile = _entities.SichtradiusCheck();
        else if (_p4Bs) zeile = _entities.BodenstrahlCheck();
        else if (_p4Af) zeile = _entities.AngriffFahrtCheck();
        if (zeile == null) return false;
        GD.Print(zeile);
        GetTree().Quit(0);
        return true;
    }

    /// <summary>Je Bild: der Bildlauf <c>--bodenstrahl-bild=</c> fotografiert, sobald ein
    /// Bodenstrahl im dritten Takt ist (mitten im Flug).</summary>
    private void P4BildTakt()
    {
        if (_p4BsBild.Length == 0 || !_entities.BodenstrahlBildBereit()) return;
        RenderingServer.ForceDraw();
        GetViewport().GetTexture().GetImage().SavePng(_p4BsBild);
        GD.Print($"bodenstrahl-bild: {_p4BsBild} ({_entities.BodenstrahlBildText()})");
        _p4BsBild = "";
        GetTree().Quit(0);
    }
}
