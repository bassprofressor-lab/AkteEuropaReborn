using System.Linq;
using Godot;
namespace AkteEuropaReborn.Core;

/// <summary>
/// ⭐⭐ <b>DER PRÜFSCHUTZ</b> — kopflose Läufe und Prüfstände schreiben NIE in die
/// Dateien des Spielers (bug-577, 16.10.2026).
///
/// <para>Gemessen: <c>--campaign=2 --no-briefing --no-movie --feind-naht-check=m2</c>
/// änderte <c>campaign.cfg</c> (76126e11… → f8d506e1…), ebenso
/// <c>--bertha-nachladen-check</c> und <c>--demo-runden-check</c>. Der Benutzerordner
/// ist mit dem Spieler geteilt: jeder Prüflauf schrieb in SEINEN Kampagnenstand.</para>
///
/// <para><b>Regel:</b> läuft das Spiel kopflos (<c>DisplayServer.GetName() ==
/// "headless"</c>) ODER steht unter den Benutzerschaltern einer mit <c>-check</c>,
/// dann werden <c>campaign.cfg</c>, <c>settings.cfg</c> und <c>designs.cfg</c> beim
/// Start nach <c>user://pruefstand/</c> KOPIERT, und alles Lesen und Schreiben geht
/// auf die Kopie; Spielstände gehen nach <c>user://saves/pruefstand_kopflos</c>.
/// Der Lauf sieht also den echten Stand, verändert ihn aber nicht. Im Fenster ohne
/// Prüfschalter bleibt alles wie bisher.</para>
///
/// <para>Gegenschalter <c>--pruefschutz-aus</c> (Stand bis 0.6.9: der Lauf schreibt
/// in die echten Dateien — nur als Nullmodell, danach die Datei zurückholen).</para>
/// </summary>
public static class Pruefschutz
{
    public const string Ordner = "user://pruefstand";
    public const string SaveOrdner = "user://saves/pruefstand_kopflos";

    private static bool? _aktiv;
    private static string _grund = "";

    /// <summary>Wie oft ein Schreiber in eine Kopie umgelenkt wurde.</summary>
    public static int Umgelenkt;

    public static bool Aktiv => _aktiv ??= Bestimmen();

    private static bool Bestimmen()
    {
        var args = OS.GetCmdlineUserArgs();
        if (args.Contains("--pruefschutz-aus"))
        {
            GD.Print("Pruefschutz: AUS (--pruefschutz-aus) — dieser Lauf schreibt in die ECHTEN Dateien des Spielers");
            return false;
        }
        bool kopflos = DisplayServer.GetName() == "headless";
        string? pruef = args.FirstOrDefault(a => a.Contains("-check"));
        if (!kopflos && pruef == null) return false;
        _grund = kopflos ? "kopflos" : $"Pruefschalter {pruef}";

        DirAccess.MakeDirRecursiveAbsolute(Ordner);
        foreach (var name in new[] { "campaign.cfg", "settings.cfg", "designs.cfg" })
        {
            string echt = "user://" + name, kopie = Ordner + "/" + name;
            if (FileAccess.FileExists(echt)) DirAccess.CopyAbsolute(echt, kopie);
            else if (FileAccess.FileExists(kopie)) DirAccess.RemoveAbsolute(kopie);
        }
        DirAccess.MakeDirRecursiveAbsolute(SaveOrdner);
        GD.Print($"Pruefschutz: {_grund} — campaign.cfg, settings.cfg, designs.cfg als Kopie in {Ordner}/, " +
                 $"Spielstaende in {SaveOrdner}; die Dateien des Spielers bleiben unberuehrt (Gegenschalter --pruefschutz-aus)");
        return true;
    }

    /// <summary>Der Pfad, unter dem gelesen und geschrieben wird.</summary>
    public static string Pfad(string echt)
        => Aktiv && echt.StartsWith("user://") ? Ordner + "/" + echt.Substring(7) : echt;

    /// <summary>Protokolliert einen umgelenkten Schreibzugriff mit dem Aufrufer —
    /// damit sichtbar bleibt, WER geschrieben hätte.</summary>
    public static void Gemeldet(string datei, string wer)
    {
        if (!Aktiv) return;
        Umgelenkt++;
        var st = new System.Diagnostics.StackTrace(2, false);
        string kette = string.Join(" <- ", st.GetFrames().Take(4)
            .Select(f => f.GetMethod() is { } m ? $"{m.DeclaringType?.Name}.{m.Name}" : "?"));
        GD.Print($"Pruefschutz: {datei} geschrieben von {wer} ({kette}) — in die Kopie umgelenkt");
    }
}
