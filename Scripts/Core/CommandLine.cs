namespace AkteEuropaReborn.Core;

using System.Collections.Generic;
using Godot;

/// <summary>
/// Die Schalter DIESES Spiels — ob der Aufrufer den Trenner <c>--</c> gesetzt
/// hat oder nicht.
///
/// <para>⚠ <b>Der Fehler, gegen den es diese Datei gibt (11.08.2026).</b> Godot
/// gibt einem Programm über <c>OS.GetCmdlineUserArgs()</c> ausschliesslich das
/// weiter, was HINTER einem <c>--</c> steht; alles davor behält die Engine für
/// sich und schweigt über das, was sie nicht kennt. Im Entwicklungslauf fällt
/// das nicht auf, weil dort ohnehin <c>--path .</c> steht und der Trenner
/// deshalb immer mitgetippt wird:</para>
/// <code>
///   Godot_console.exe --headless --path . -- --campaign=2 --no-briefing   (geht)
/// </code>
/// <para>In der AUSGELIEFERTEN Fassung tippt niemand den Trenner, und dann
/// kommt kein einziger Schalter an — ohne Meldung, ohne Rückgabewert:</para>
/// <code>
///   AkteEuropaReborn.exe --headless --campaign=2 --no-briefing            (tat nichts)
///   AkteEuropaReborn.exe --headless -- --campaign=2 --no-briefing         (ging)
/// </code>
/// <para>Gemessen am 11.08.2026 auf <c>build/windows/AkteEuropaReborn.exe</c>:
/// beide Läufe melden »campaign: 33 Missionen aus …«, aber nur der zweite
/// meldet »Kampagne: starte 03 — Wood raiders«. Der Rückgabewert war in beiden
/// Fällen 0.</para>
///
/// <para>Die Abhilfe ist bewusst eng: <b>solange der Trenner da ist, ändert
/// sich gar nichts</b> — dann sind das die Schalter, und die Liste wird
/// unverändert durchgereicht. Nur wenn NICHTS hinter dem Trenner steht, wird
/// die volle Befehlszeile nach <c>--</c>-Schaltern durchgesehen. Dass dabei
/// auch Godots eigene Schalter (<c>--headless</c> …) mit in der Liste landen,
/// ist harmlos: jeder Leser im Spiel fragt nach seinen eigenen Namen und
/// überliest den Rest — dasselbe, was er mit einem getippten <c>--</c> ohnehin
/// tut.</para>
/// </summary>
public static class CommandLine
{
    private static string[]? _args;

    /// <summary>Die Schalter, die dieses Spiel auswerten soll.</summary>
    public static string[] Args => _args ??= Build();

    private static string[] Build()
    {
        var user = OS.GetCmdlineUserArgs();
        if (user.Length > 0) return user;          // mit »--«: alles wie bisher

        var list = new List<string>();
        foreach (string a in OS.GetCmdlineArgs())
            if (a.StartsWith("--")) list.Add(a);
        if (list.Count > 0)
            GD.Print($"cmdline: kein »--« angegeben — {list.Count} Schalter aus der " +
                     $"vollen Befehlszeile uebernommen: {string.Join(" ", list)}");
        return list.ToArray();
    }

    /// <summary>Nur für den Prüfstand: die Merkliste vergessen, damit ein Test
    /// die Zusammenstellung noch einmal auslösen kann.</summary>
    public static void Forget() => _args = null;
}
