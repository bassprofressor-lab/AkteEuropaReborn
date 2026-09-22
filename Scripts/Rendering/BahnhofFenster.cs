namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// ⭐⭐ <b>Bahnhofsfenster, Transportsystem und Einheiten-Transport</b> —
/// die Verdrahtung im Halter (22.09.2026, berichte/bahnhof-transport-fable.md).
///
/// <list type="bullet">
/// <item>Knopf »Transportsystem« (Druckarm 0x448C66) → Karte in Betriebsart 1,
///   an der Maus − 5 (0x444A30).</item>
/// <item>Karte Betriebsart 1: Maus über einer Linie mit zwei eigenen Enden →
///   hervorheben; Klick → Fensterart 4 an der Maus − 3 (0x4492A3 → 0x445D70).</item>
/// <item>Fensterart 4: Klick in die Tafel → Befehl 500.</item>
/// <item>Knopf »Transportieren« (0x448DB9) → Karte in Betriebsart 5 an der
///   Maus − 3 (0x4459F0); Klick auf ein Gebäude → Befehl 518 je Einheit, Karte zu
///   (0x4471A0).</item>
/// </list>
/// </summary>
public partial class MapViewer
{
    private UI.TransportlinieView? _transportlinie;
    private int _transportsystemLinie = -1;
    private List<int>? _transportGriffe;

    /// <summary>Für die Prüfstände.</summary>
    public UI.TransportlinieView? TransportlinieAnsicht => _transportlinie;
    public UI.KartenschirmView? KartenschirmAnsicht => _kartenschirm;

    private Vector2 Maus(int versatz)
        => GetViewport().GetMousePosition() - new Vector2(versatz, versatz);

    /// <summary>Aus <c>BuildBaseWindow</c>, nachdem Gebäudefenster und Karte stehen.</summary>
    private void BahnhofFensterVerdrahten(CanvasLayer layer)
    {
        if (_gebaeudeFenster == null || _kartenschirm == null) return;

        _transportlinie = new UI.TransportlinieView { Visible = false };
        layer.AddChild(_transportlinie);
        _transportlinie.OnClose = () => _transportlinie.Visible = false;
        _transportlinie.OnBefehl500 = (linie, ware, wert) =>
        {
            _entities.RailSetFlag(linie, ware, wert);
            TransportlinieNeu(linie);
        };
        _entities.BahnschalterGeaendert += TransportlinieNeu;     // 0x44FD70

        _gebaeudeFenster.OnMeldungText = t => MeldungOeffnen(t, "", 3, false);
        _gebaeudeFenster.OnTransportsystem = () =>
        {
            if (MapEntityLayer.TransportsystemAus) return;
            KartenschirmAuf(Maus(5), 0, -1, 1);                    // 0x444A30(MausX−5, MausY−5)
        };
        _gebaeudeFenster.OnTransportieren = griffe =>
        {
            _transportGriffe = new List<int>(griffe);
            KartenschirmAuf(Maus(UI.KartenschirmView.MausVersatz), 0, -1, 5);   // 0x4459F0
        };

        _kartenschirm.OnZelleUeber = (c, r) =>
        {
            int l = _entities.TransportsystemLinieBei(c, r);
            if (l == _transportsystemLinie) return;
            _transportsystemLinie = l;
            _kartenschirm.Hervorheben(_entities.LinienZellen(l));
        };
    }

    /// <summary>Ein Klick in die Karte, verteilt nach Betriebsart. Gibt false
    /// zurück, wenn es Betriebsart 2 ist (dann gilt der alte Weg).</summary>
    private bool KartenschirmKlickBahn(int c, int r)
    {
        if (_kartenschirm == null) return false;
        switch (_kartenschirm.Betriebsart)
        {
            case 1:
                // Die Karte bleibt offen; es geht Fensterart 4 auf (0x4492CC).
                int l = _entities.TransportsystemLinieBei(c, r);
                if (l >= 0) TransportlinieAuf(l, Maus(UI.KartenschirmView.MausVersatz));
                return true;
            case 5:
                int ziel = _entities.GebaeudeAufZelle(c, r);
                if (ziel < 0) return true;                          // 0xFF -> nichts
                var griffe = _transportGriffe ?? new List<int>();
                int n = _entities.TransportierenAusBahnhof(griffe, ziel);
                if (n > 0)
                {
                    _transportGriffe = null;
                    KartenschirmZu();                               // 0x4471A0
                    _gebaeudeFenster?.Refresh();
                }
                return true;
            default:
                return false;
        }
    }

    private void TransportlinieAuf(int linie, Vector2 stelle)
    {
        if (_transportlinie == null) return;
        TransportlinieNeu(linie, force: true);
        var schirm = GetViewport().GetVisibleRect().Size;
        _transportlinie.Position = new Vector2(
            Mathf.Clamp(stelle.X, 0, Mathf.Max(0, schirm.X - _transportlinie.Size.X)),
            Mathf.Clamp(stelle.Y, 0, Mathf.Max(0, schirm.Y - _transportlinie.Size.Y)));
        _transportlinie.Visible = true;
        _transportlinie.MoveToFront();
        TransportlinieAufgegangen++;
    }

    public int TransportlinieAufgegangen;

    private void TransportlinieNeu(int linie) => TransportlinieNeu(linie, false);

    private void TransportlinieNeu(int linie, bool force)
    {
        if (_transportlinie == null) return;
        if (!force && (!_transportlinie.Visible || _transportlinie.Linie != linie)) return;
        var (n1, n2) = _entities.LinienNamen(linie);
        _transportlinie.Zeige(linie, n1, n2, _entities.RailModeOf(linie) ?? new byte[] { 2, 2, 2, 2 });
    }
}
