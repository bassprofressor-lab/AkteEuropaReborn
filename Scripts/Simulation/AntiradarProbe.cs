namespace AkteEuropaReborn.Rendering;

using System.Text;
using Godot;

/// <summary>
/// <c>--antiradar-probe</c> — PRUEFSTAND fuer den Antiradar (22.09.2026).
///
/// <para>Stellt im ersten Nebelschritt einen feindlichen Stoerer (Bauteilzeile
/// 77) fuenf Zellen neben eine eigene Einheit und fragt am Ende des Laufs den
/// Nebel an drei Stellen:</para>
/// <list type="bullet">
/// <item>die Zelle der eigenen Einheit — MUSS offen sein (Radius 0, @0x420BA6)</item>
/// <item>die Zelle drei Schritte Richtung Stoerer — darf nicht gestempelt sein (im Radius 8)</item>
/// <item>die Zelle des Stoerers — darf nicht gestempelt sein (Saum zaehlt nicht)</item>
/// </list>
/// <para>Nullmodell <c>--antiradar-aus</c>: dann sind beide letzten Zellen offen
/// und der Pruefstand MUSS durchfallen.</para>
/// </summary>
public partial class MapEntityLayer
{
    public static bool AntiradarProbeAn;
    private bool _arpGesetzt;
    private int _arpEigen = -1, _arpStoerer = -1, _arpRunden = -1;
    private string? _arpUrteil;

    /// <summary>Vor jedem Nebelschritt: versetzen, sobald ein Stoerer da ist, einen Schritt spaeter urteilen.</summary>
    private void AntiradarProbeSetzen()
    {
        if (!AntiradarProbeAn || _nav == null) return;
        // Im naechsten Nebelschritt urteilen (der Nebel zeigt dann den Schritt mit
        // dem Stoerer) -- die Artillerie daneben schiesst ihn binnen 0,6 s ab.
        if (_arpGesetzt)
        {
            if (_arpUrteil == null && ++_arpRunden >= 0) _arpUrteil = Urteil();
            return;
        }
        _arpStoerer = -1;
        for (int i = 0; i < _entities.Count && _arpStoerer < 0; i++)
        {
            var j = _entities[i];
            if (!j.Dead && !j.IsProp && !j.IsBuilding && j.Part == AntiradarPart
                && j.Owner is >= 0 and <= 7 && !DecktAuf(j.Owner, ViewPlayer))
                _arpStoerer = i;
        }
        if (_arpStoerer < 0) return;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Dead || e.IsProp || e.IsBuilding || e.Owner != ViewPlayer) continue;
            if (Untergestellt(e) || e.Ukol >= 50) continue;
            foreach (int d in new[] { 5, -5 })
            {
                int c = e.Col + d;
                if (c < 0 || c >= _nav.Width) continue;
                if (_nav.OccupantAt(c, e.Row) >= 0) continue;
                _arpEigen = i;
                _arpGesetzt = true;
                ProbeVersetzen(_arpStoerer, c, e.Row);
                GD.Print($"antiradar-probe: Stoerer Platz {_entities[_arpStoerer].Slot} (P{_entities[_arpStoerer].Owner}) "
                       + $"auf ({c},{e.Row}) gestellt, 5 Zellen neben Platz {e.Slot} auf ({e.Col},{e.Row})");
                return;
            }
        }
    }

    public string AntiradarProbeZeile()
    {
        if (!AntiradarProbeAn) return "";
        return _arpUrteil ?? "--antiradar-probe\n  KEIN URTEIL -- kein feindlicher Stoerer erschienen (Lauf verlaengern) oder keine freie Nachbarzelle";
    }

    private string Urteil()
    {
        var sb = new StringBuilder("--antiradar-probe\n");
        var e = _entities[_arpEigen];
        var j = _entities[_arpStoerer];
        if (_fog == null) return "--antiradar-probe\n  KEIN URTEIL -- kein Nebel";
        int schritt = j.Col > e.Col ? 3 : -3;
        bool eigen = _fog.IsWatched(e.Col, e.Row);
        // ⚠ GESTEMPELT, nicht »sichtbar«: eine Zelle neben einer offenen gilt als
        // SAUM (0x41FF50, Eckenmarke) — steht eine eigene Einheit direkt neben dem
        // Stoerer, liegt er im Saum ihrer eigenen Zelle, auch im Original.
        bool mitte = _fog.IsStamped(e.Col + schritt, e.Row);
        bool stoer = _fog.IsStamped(j.Col, j.Row);
        sb.AppendLine($"  (Saum am Stoerer: {(_fog.IsWatched(j.Col, j.Row) && !stoer ? "ja" : "nein")})");
        sb.AppendLine($"  eigene Einheit ({e.Col},{e.Row}) offen: {(eigen ? "ja" : "NEIN")}   Soll ja");
        sb.AppendLine($"  Zelle ({e.Col + schritt},{e.Row}) gestempelt: {(mitte ? "JA" : "nein")}   Soll nein");
        sb.AppendLine($"  Stoerer ({j.Col},{j.Row}) {(j.Dead ? "TOT, " : "")}gestempelt: {(stoer ? "JA" : "nein")}   Soll nein");
        sb.AppendLine($"  {AntiradarZeile()}");
        if (AntiradarAus)
            sb.AppendLine("  ⚠ NULLMODELL --antiradar-aus: MUSS hier durchfallen");
        bool ok = eigen && !mitte && !stoer && AntiradarKlaenge == 1;
        return sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }
}
