namespace AkteEuropaReborn.Editor;

using System;
using System.IO;

/// <summary><c>--map-verwaltung-check</c> — D8 (12.11.2026): erzeugt eine kleine Karte
/// zvw_a, kopiert, benennt um, versucht Verbotenes und loescht wieder. Am Ende darf
/// keine zvw_-Datei uebrig sein.</summary>
public static class MapVerwaltungCheck
{
    public static int Run(Action<string> say)
    {
        int bad = 0;
        void Soll(bool ok, string was) { say($"map-verwaltung-check: {(ok ? "ok  " : "FEHLER")} {was}"); if (!ok) bad++; }
        string dir = MapOpen.MapsDir;
        foreach (string f in Directory.GetFiles(dir, "map_zvw_*")) File.Delete(f);

        int rc = UI.MainMenu.MapNew("zvw_a,64,64,47,spieler=2,samen=5", _ => { });
        Soll(rc == 0 && MapVerwaltung.Dateien("zvw_a").Count == 4, $"zvw_a erzeugt ({MapVerwaltung.Dateien("zvw_a").Count} Dateien)");

        Soll(MapVerwaltung.Kopieren("zvw_a", "zvw_b") == null && MapVerwaltung.Dateien("zvw_b").Count == 4, "Kopie zvw_b angelegt");
        Soll(MapVerwaltung.Kopieren("zvw_a", "zvw_b") != null, "Kopie auf vorhandenen Namen abgewiesen");
        Soll(MapVerwaltung.Kopieren("zvw_a", "01") != null, "Kopie auf mitgelieferten Namen abgewiesen");
        bool hatte01 = File.Exists($"{dir}/map_01.json");
        Soll(MapVerwaltung.Loeschen("map_01") != null && File.Exists($"{dir}/map_01.json") == hatte01,
             $"mitgelieferte Karte loeschen abgewiesen (map_01 {(hatte01 ? "liegt weiter da" : "gab es nicht")})");

        Soll(MapVerwaltung.Umbenennen("zvw_b", "zvw_c") == null && !MapVerwaltung.Gibt("zvw_b")
             && MapVerwaltung.Dateien("zvw_c").Count == 4, "zvw_b in zvw_c umbenannt");
        string j = File.ReadAllText($"{dir}/map_zvw_c.json"), e = File.ReadAllText($"{dir}/map_zvw_c.entities.json");
        Soll(j.StartsWith("{\"map\":\"map_zvw_c\"") && e.StartsWith("{\"map\":\"zvw_c\""), "Name im Kopf beider Dateien neu");
        Soll(j.Contains("Editor map_zvw_c ") && !j[..400].Contains("zvw_a"), "Missionstitel neu, kein alter Name im Kopf");
        // der Rest der Datei Byte fuer Byte gleich: Laenge = alte Laenge (gleich lange Namen)
        Soll(new FileInfo($"{dir}/map_zvw_c.json").Length == new FileInfo($"{dir}/map_zvw_a.json").Length
             && new FileInfo($"{dir}/map_zvw_c.png").Length == new FileInfo($"{dir}/map_zvw_a.png").Length, "Dateilaengen unveraendert");

        Soll(MapVerwaltung.TitelSetzen("zvw_c", "Meine \"Testkarte\"") == null
             && MapVerwaltung.Titel("zvw_c") == "Meine Testkarte"
             && File.ReadAllText($"{dir}/map_zvw_c.entities.json").Contains("\"mission\":\"Meine Testkarte\""),
             $"Titel gesetzt und gelesen (»{MapVerwaltung.Titel("zvw_c")}«, Anfuehrungszeichen entfernt)");
        int chk = MapCheck.Run("map_zvw_c", _ => { });
        Soll(chk == MapCheck.Run("map_zvw_a", _ => { }), $"map-check der Kopie wie das Original (Urteil {chk})");

        Soll(MapVerwaltung.Loeschen("zvw_a") == null && MapVerwaltung.Loeschen("zvw_c") == null, "beide geloescht");
        Soll(Directory.GetFiles(dir, "map_zvw_*").Length == 0, "keine zvw_-Datei uebrig");
        say(bad == 0 ? "map-verwaltung-check: BESTANDEN" : $"map-verwaltung-check: DURCHGEFALLEN ({bad})");
        return bad == 0 ? 0 : 1;
    }
}
