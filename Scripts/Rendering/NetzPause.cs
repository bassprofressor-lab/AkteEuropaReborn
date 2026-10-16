namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// ⭐ 17.10.2026 (Direktbefehle2, Paket »pause«) — die ANZEIGE der gemeinsamen
/// Netzpause (Befehl 993, word[0x4FA280] = 1000). Das Original öffnet dafür
/// Fensterart 40 »Pause« mit dem Knopf »Weiter« (0x44D831 → 993). ⚠ SETZUNG:
/// Art 40 ist nicht gebaut; es steht eine Zeile in der Bildmitte, ESC oder P
/// hebt die Pause auf (beide schicken 993).
/// </summary>
public partial class MapViewer
{
    private Label? _netzPauseZeile;

    private void NetzPauseZeigen(bool an, int wer)
    {
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
}
