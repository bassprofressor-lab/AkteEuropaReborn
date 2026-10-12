namespace AkteEuropaReborn.Rendering;

using System.Linq;
using Godot;

/// <summary>
/// ⭐ 12.10.2026 — <c>--k26-ende-check=FALL</c>, der Prüfstand zum Endblock von
/// Mission 26 »Scandinavia« (berichte/k26-ende.md).
///
/// <para>Gemeldet: »alle Gegner zerstört, K26 endet nicht«. Gelesen: der Endblock
/// C <c>0x4A2468…0x4A250B</c> (F Dateiversatz <c>0xA1181</c>, dieselbe Bytefolge)
/// hat vier Endstellen, unsere Datei führte davon nur die Einzelglieder:</para>
/// <code>
///   0x4A2483  obj_owner(13) == 3                                   -> SIEG
///   0x4A24C5  v0 == 0 UND ( (units(0,0)==0 UND objects(1,0)==0)
///                           ODER objects(1,3) == 0 )               -> NIEDERLAGE
///   0x4A24E5  v0 != 0 UND units(0,0) == 0                          -> SIEG
///   0x4A2506  obj_owner(13) == 0 ODER obj_owner(13) == 12          -> NIEDERLAGE
/// </code>
/// <para>Fälle (je ein Lauf):</para>
/// <list type="bullet">
///   <item><b>spieler</b>: nach 2 s nimmt der SPIELER Myre (Platz 13) ein
///   (<c>CaptureDone</c>). Soll: Niederlage. Nullmodell <c>--m26-endregeln-alt</c>:
///   die Mission läuft weiter — genau die Meldung — und der Prüfstand FÄLLT DURCH.</item>
///   <item><b>leer</b>: v0 := 1 (wie nach Text 361), dann alle Einheiten von P0
///   entfernt. Soll: Sieg ausgelöst (Nachfrist zählt). Nullmodell: kein Ende, FÄLLT DURCH.</item>
///   <item><b>p3</b>: P3 nimmt Myre ein. Soll in BEIDEN Fassungen: Sieg (Gegenprobe,
///   die Siegregel 0x4A2483 war schon da).</item>
///   <item><b>start</b>: nichts tun. Soll in beiden Fassungen: kein Ende (keine der
///   neuen Regeln darf beim Missionsstart feuern).</item>
/// </list>
/// </summary>
public partial class MapEntityLayer
{
    public string K26EndeCheck(string fall, int sekunden)
    {
        int m = UI.SkirmishSetup.CampaignMission;
        bool alt = Campaign.MissionScript.SetzungAus.Contains("--m26-endregeln-alt");
        var sb = new System.Text.StringBuilder(
            $"k26-ende-check K{m}, Fall {fall}, {sekunden} s, --m26-endregeln-alt {alt}\n");
        if (!InCampaign || m != 26) return sb.Append("  keine Kampagne 26 — ungeprueft\n  DURCHGEFALLEN").ToString();
        MissionScriptTick(0.001f);
        if (_mscript == null) return sb.Append("  kein Skript\n  DURCHGEFALLEN").ToString();

        int myre = _entities.FindIndex(e => e.IsBuilding && !e.IsProp && e.Slot == 13);
        if (myre < 0) return sb.Append("  Myre (Platz 13) nicht gefunden\n  DURCHGEFALLEN").ToString();
        sb.AppendLine($"  Start: obj_owner(13)={_mscript.ObjOwner!(13)}, v0={_mscript.Var(0)}, " +
                      $"Einheiten P0={UnitClassCount(0, 0)}, Basen P0={_mscript.ObjectCount!(1, 0)}, Basen P3={_mscript.ObjectCount!(1, 3)}");

        int takte = sekunden * SimHz, eingriff = 2 * SimHz;
        int endeTakt = -1, graceTakt = -1;
        for (int t = 0; t < takte; t++)
        {
            if (t == eingriff)
            {
                switch (fall)
                {
                    case "spieler": CaptureDone(myre, _entities[myre], 0); break;
                    case "p3": CaptureDone(myre, _entities[myre], 3); break;
                    case "leer":
                        _mscript.SetVarFuerProbe(0, 1);
                        for (int i = 0; i < _entities.Count; i++)
                        {
                            var e = _entities[i];
                            if (!e.IsBuilding && !e.IsProp && !e.Dead && e.Owner == 0) Kill(i, e, -1, "k26-ende-check");
                        }
                        break;
                }
                sb.AppendLine($"  Eingriff bei {t / SimHz} s: obj_owner(13)={_mscript.ObjOwner!(13)}, v0={_mscript.Var(0)}, " +
                              $"Einheiten P0={UnitClassCount(0, 0)}");
            }
            SimTickFuerProbe();
            if (graceTakt < 0 && _mscript.Grace >= 0) graceTakt = t;
            if (_mscript.Ended) { endeTakt = t; break; }
        }

        string ergebnis = _mscript.Ended ? (_mscript.Success ? "SIEG" : "NIEDERLAGE")
                        : graceTakt >= 0 ? "SIEG (Nachfrist laeuft)" : "kein Ende";
        sb.AppendLine($"  Ergebnis: {ergebnis}" +
                      (endeTakt >= 0 ? $" bei {(float)endeTakt / SimHz:0.0} s" : "") +
                      (graceTakt >= 0 ? $", Nachfrist ab {(float)graceTakt / SimHz:0.0} s" : "") +
                      $"; obj_owner(13)={_mscript.ObjOwner!(13)}, v0={_mscript.Var(0)}");
        sb.AppendLine($"  P3-Ausstiege AiMissionAttack: {AmGruendeZeile()}; " +
                      $"Zielwahl uebersprang verbuendetes Ziel {ZielwahlVerbuendetUebersprungen}x, " +
                      $"Tuergriff-Gruppen {TuergriffGruppen}, -Fahrten {TuergriffFahrten}");
        sb.AppendLine("  " + _mscript.Line());

        bool sieg = ergebnis.StartsWith("SIEG"), niederlage = ergebnis == "NIEDERLAGE", keins = ergebnis == "kein Ende";
        string soll = fall switch
        {
            "spieler" => "NIEDERLAGE",
            "leer" => "SIEG",
            "p3" => "SIEG",
            "start" => "kein Ende",
            _ => "?",
        };
        bool ok = soll switch
        {
            "NIEDERLAGE" => niederlage,
            "SIEG" => sieg,
            "kein Ende" => keins,
            _ => false,
        };
        sb.AppendLine($"  Soll (Original): {soll}" +
                      (alt ? " — Nullmodell --m26-endregeln-alt: muss bei spieler/leer DURCHFALLEN" : ""));
        sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
