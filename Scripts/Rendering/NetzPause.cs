namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// ⭐ 17.10.2026 (Direktbefehle2, Paket »pause«) — die ANZEIGE der gemeinsamen
/// Netzpause (Befehl 993, word[0x4FA280] = 1000). Das Original öffnet dafür
/// Fensterart 40 »Pause« mit dem Knopf »Weiter« (0x44D831 → 993). Bis zum 22.10.2026
/// stand hier nur eine Zeile in der Bildmitte; seit Paket N (bug-616) ist es Art 40
/// (UI/PausenFenster.cs), auch im Einzelspiel (Taste P). Die Zeile bleibt unter
/// <c>--pausenfenster-alt</c> und ohne eingelesene Kacheln.
/// </summary>
public partial class MapViewer
{
    private Label? _netzPauseZeile;
    private UI.PausenFenster? _pausenFenster;

    /// <summary>Auf und zu NUR hier, also nur über den 993-Behandler (F 0x4C41BE öffnet
    /// bei Wort 0, F 0x4C41E7 schliesst bei 1000) — im Netz damit auf JEDER Maschine im
    /// selben Takt. »Weiter«, Kreuz und Rechtsklick schicken 993
    /// (<see cref="MapEntityLayer.PostPause"/>).</summary>
    private void NetzPauseZeigen(bool an, int wer)
    {
        if (!UI.PausenFenster.Alt && UI.PausenFenster.Usable)
        {
            PausenFensterZeigen(an, wer);
            return;
        }
        if (_netzPauseZeile == null)
        {
            _netzPauseZeile = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                AnchorLeft = 0, AnchorRight = 1, AnchorTop = 0.4f, AnchorBottom = 0.5f,
            };
            (_panelLayer ?? (CanvasLayer)GetTree().Root.GetChild(0)).AddChild(_netzPauseZeile);
        }
        _netzPauseZeile.Text = $"PAUSE (Spieler {wer + 1}) — ESC oder P: weiter";
        _netzPauseZeile.Visible = an;
        GD.Print(an ? $"Netzpause an (Spieler {wer})" : $"Netzpause aus (Spieler {wer})");
    }

    private void PausenFensterZeigen(bool an, int wer)
    {
        if (an)
        {
            if (_pausenFenster == null || !IsInstanceValid(_pausenFenster))
            {
                _pausenFenster = new UI.PausenFenster { Visible = false };
                _pausenFenster.OnWeiter = () => _entities.PostPause();
                (_panelLayer ?? (CanvasLayer)GetTree().Root.GetChild(0)).AddChild(_pausenFenster);
            }
            // Ein Fenster, das noch zublendet, steht noch in der Liste — sonst griffe die
            // Doppelöffnungssperre und das neue bliebe aus.
            UI.WindowManager.Wegnehmen(UI.WindowManager.Offen(UI.PausenFenster.Art));
            _pausenFenster.Zuruecksetzen();
            _pausenFenster.Visible = true;
            UI.WindowManager.Oeffnen(UI.PausenFenster.Art, _pausenFenster);
            // ⚠ Fensterwelt-Regel: die Aufblende von Hand zu Ende drehen — der SimTick
            // steht jetzt (NetzPause), und an ihm hängt WindowManager.Takt().
            for (int t = 0; t <= UI.WindowManager.BilderAuf + 1; t++) UI.WindowManager.Takt();
            UI.PausenFenster.Offen = _pausenFenster;
        }
        else if (_pausenFenster != null)
        {
            // F 0x4C41E7 → 0x401B77: das erste Fenster der Art 40 schliessen. Der Takt läuft
            // ab hier wieder, die Zublende dreht WindowManager.Takt().
            UI.WindowManager.Schliessen(UI.PausenFenster.Art);
            UI.PausenFenster.Offen = null;
        }
        // Eine Zeile »Spieler n hat angehalten« gibt es im Original nicht (kein Textverweis
        // im Behandler) — die Nummer steht nur in der Konsole.
        GD.Print(an ? $"Pause an (Befehl 993 von Spieler {wer}), Fenster Art 40 auf" :
                      $"Pause aus (Befehl 993 von Spieler {wer}), Fenster Art 40 zu");
    }
}
