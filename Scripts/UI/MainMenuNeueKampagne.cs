namespace AkteEuropaReborn.UI;

using System.Collections.Generic;
using System.Text;
using Godot;

/// <summary>
/// ⭐ 29.10.2026, bug-668 — <b><c>--neue-kampagne-check</c></b>: der Weg des Knopfs
/// »Neue Kampagne« (Missionsuebersicht, <see cref="CampaignScreen"/>) — GENAU der Aufruf
/// hinter »Ja«, <see cref="Campaign.CampaignManager.NeueKampagne"/>, ohne die Rueckfrage.
/// <list type="number">
/// <item>Die KOPIE <c>user://pruefstand/campaign.cfg</c> mit allen Abschnitten fuellen
/// (completed, balance, vars, carried, [mission_balance], [entwuerfe], [bauteile],
/// [forschung_anfang]).</item>
/// <item>Fingerabdruecke: die Kopien von designs-gefecht.cfg / designs.cfg / settings.cfg,
/// die ECHTEN Dateien des Spielers (campaign.cfg, designs-gefecht.cfg, designs.cfg,
/// settings.cfg) und die Liste von <c>user://saves</c> samt Groessen.</item>
/// <item><c>NeueKampagne()</c>, dann: alle Kampagnenabschnitte leer, alles andere gleich.</item>
/// </list>
/// Nullmodell: mit <c>--entwuerfe-global</c> bleibt <c>[entwuerfe]</c> stehen (so wie es
/// Reset seit bug-594 vorsieht) — dann DURCHGEFALLEN. ⚠ Nur unter dem Pruefschutz.
/// </summary>
public partial class MainMenu
{
    private void NeueKampagneCheck()
    {
        var sb = new StringBuilder("neue-kampagne-check");
        foreach (string a in OS.GetCmdlineUserArgs())
            if (a == "--entwuerfe-global") { Rendering.MapEntityLayer.EntwuerfeGlobal = true; sb.Append(" — NULLMODELL --entwuerfe-global"); }
        string pfad = Campaign.CampaignManager.SavePath;
        if (!Core.Pruefschutz.Aktiv || !pfad.StartsWith(Core.Pruefschutz.Ordner + "/"))
        {
            GD.Print(sb + "\n   KEIN URTEIL: nur unter dem Pruefschutz (kopflos) — der Lauf schriebe sonst in die echte campaign.cfg");
            GetTree().Quit(0);
            return;
        }
        // 1. die Kopie mit allem fuellen
        string bloecke = Rendering.MapEntityLayer.BauteileKodieren(new byte[8 * Rendering.MapEntityLayer.BauteilBlockBytes]);
        using (var c = new ConfigFile())
        {
            c.Load(pfad);
            c.SetValue("campaign", "completed", 7);
            c.SetValue("campaign", "balance", 4321);
            c.SetValue("campaign", "vars", "10:1 15:5 300:1");
            c.SetValue("campaign", "carried", new Godot.Collections.Array { "95|91|Probe|1" });
            c.SetValue("mission_balance", "2", 320);
            c.SetValue("mission_balance", "3", 470);
            c.SetValue("entwuerfe", "liste", new Godot.Collections.Array { new Godot.Collections.Array { "Probe", 1, 2, 3 } });
            c.SetValue("bauteile", "bloecke", bloecke);
            c.SetValue("bauteile", "mission", 7);
            c.SetValue(Campaign.CampaignManager.ForschungAnfangAbschnitt, "3", bloecke);
            c.SetValue(Campaign.CampaignManager.ForschungAnfangAbschnitt, "4", bloecke);
            c.Save(pfad);
        }
        sb.Append($"\n   vorher {pfad}: {Abschnitte(pfad)}");

        // 2. Fingerabdruecke von allem, was bleiben muss
        var bleiben = new List<string>
        {
            Rendering.MapEntityLayer.GefechtPfad,
            Core.Pruefschutz.Pfad("user://designs.cfg"), Core.Pruefschutz.Pfad("user://settings.cfg"),
            Campaign.CampaignManager.EchterPfad, Rendering.MapEntityLayer.GefechtEchterPfad,
            "user://designs.cfg", "user://settings.cfg",
        };
        var vorher = new Dictionary<string, string>();
        foreach (var p in bleiben) vorher[p] = Abdruck(p);
        string savesVorher = SavesListe("user://saves");

        // 3. der Weg des Knopfs
        Campaign.CampaignManager.NeueKampagne();

        bool ok = true;
        void Zeile(bool g, string t) { ok &= g; sb.Append($"\n   {(g ? "  ok " : "⚠ FALSCH ")} {t}"); }
        using (var c = new ConfigFile())
        {
            c.Load(pfad);
            sb.Append($"\n   nachher {pfad}: {Abschnitte(pfad)}");
            Zeile((int)c.GetValue("campaign", "completed", -1) == 0, $"completed = {c.GetValue("campaign", "completed", "fehlt")} (Soll 0)");
            Zeile((int)c.GetValue("campaign", "balance", -1) == 0, $"balance = {c.GetValue("campaign", "balance", "fehlt")} (Soll 0)");
            Zeile((string)c.GetValue("campaign", "vars", "?") == "", $"vars = »{c.GetValue("campaign", "vars", "fehlt")}« (Soll leer)");
            Zeile(!c.HasSectionKey("campaign", "carried"), $"carried {(c.HasSectionKey("campaign", "carried") ? "STEHT NOCH" : "weg")}");
            foreach (var s in new[] { "mission_balance", "entwuerfe", "bauteile", Campaign.CampaignManager.ForschungAnfangAbschnitt })
                Zeile(!c.HasSection(s), $"[{s}] {(c.HasSection(s) ? "STEHT NOCH" : "weg")}");
        }
        foreach (var p in bleiben)
        {
            string n = Abdruck(p);
            Zeile(n == vorher[p], $"{p} unveraendert ({n})");
        }
        string savesNach = SavesListe("user://saves");
        Zeile(savesNach == savesVorher, $"user://saves unveraendert ({savesNach.Split('\n').Length} Eintraege)");
        sb.Append($"\n   {(ok ? "BESTANDEN" : "DURCHGEFALLEN")}");
        GD.Print(sb.ToString());
        GetTree().Quit(0);
    }

    private static string Abschnitte(string pfad)
    {
        using var c = new ConfigFile();
        if (c.Load(pfad) != Error.Ok) return "nicht lesbar";
        var l = new List<string>();
        foreach (var s in c.GetSections()) l.Add($"[{s}] {string.Join(",", c.GetSectionKeys(s))}");
        return string.Join("  ", l);
    }

    private static string Abdruck(string pfad)
        => FileAccess.FileExists(pfad) ? $"{FileAccess.GetFileAsBytes(pfad).Length} B md5 {FileAccess.GetMd5(pfad)[..12]}" : "fehlt";

    private static string SavesListe(string ordner)
    {
        var l = new List<string>();
        void Lauf(string d)
        {
            using var da = DirAccess.Open(d);
            if (da == null) return;
            foreach (var f in da.GetFiles()) l.Add($"{d}/{f} {FileAccess.GetMd5(d + "/" + f)}");
            foreach (var u in da.GetDirectories()) Lauf(d + "/" + u);
        }
        Lauf(ordner);
        l.Sort(System.StringComparer.Ordinal);
        return string.Join("\n", l);
    }
}
