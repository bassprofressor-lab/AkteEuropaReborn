namespace AkteEuropaReborn.Network;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;
using AkteEuropaReborn.UI;

/// <summary>
/// ⭐ 14.11.2026 — P7 Teil 2: <b>Stärke und Team je Platz im Netz.</b>
///
/// <para>Der Vermittler legt die Aufstellung fest und schickt sie mit dem Sitzungspaket
/// (Protokoll 5): je KARTENPLATZ 0..7 wer dort spielt (Mensch, Computer leicht/normal/schwer,
/// leer) und in welchem Team. Jede Maschine baut daraus dieselbe Gegnerliste, dieselben Stufen
/// und dieselbe Bündnistafel (Simulation/GefechtTeams.cs) — sonst liefen die Partien auseinander.</para>
///
/// <list type="bullet">
/// <item><b>LAN:</b> die Platzliste des Mehrspielerschirms (UI/MainMenuPlaetze.cs). Der Gastgeber
/// ist »Du«, die Mitspieler bekommen der Reihe nach die Zeilen »Mitspieler«. Damit bestimmt der
/// Gastgeber auch, WO sie starten.</item>
/// <item><b>Online:</b> der Raum. <b>Raumplatz n ist Startplatz n der Karte</b> (in der Reihenfolge
/// von MapPreview.Startplaetze) — wie in klassischen Echtzeitstrategiespielen, wo die Zeile im
/// Raum der Startpunkt ist. Bisher verteilte das Spiel die Kartenplätze beim Start selbst, und die
/// Reihenfolge im Raum sagte nichts. Mensch, KI (mit ihrer Stärke) oder frei kommen aus dem Raum,
/// das Team aus dem Feld <c>team</c> des Servers (set_slot / set_ai).</item>
/// </list>
/// <para>Gegenschalter <c>--gefecht-plaetze-alt</c>: keine Aufstellung, der Weg bis Protokoll 4
/// (eine Stufe für alle, Plätze nach der alten Regel).</para>
/// </summary>
public partial class NetworkManager
{
    /// <summary>Die Aufstellung in die Sitzung tragen und die Menschenplätze (<c>s.Slot</c>) danach
    /// richten. false = abgebrochen (mit Meldung).</summary>
    private bool AufstellungVerteilen(NetSession s, List<int> pl)
    {
        if (SkirmishSetup.PlaetzeAlt || pl.Count == 0) return true;
        if (Link!.IsRelay)
        {
            var room = Lobby?.MatchStart?["room"] as JsonObject ?? Lobby?.Room;
            return room == null || AufstellungAusRaum(s, pl, room);
        }
        return !SkirmishSetup.PlatzListe || AufstellungAusSchirm(s, pl);
    }

    /// <summary>LAN: aus der Platzliste des Gastgebers. Mitspieler-Zeilen werden in der Reihenfolge
    /// der Karte an die Mitspieler vergeben; übrige Mitspieler-Zeilen bleiben leer.</summary>
    private bool AufstellungAusSchirm(NetSession s, List<int> pl)
    {
        int du = SkirmishSetup.Human;
        if (!pl.Contains(du)) du = pl[0];
        var frei = pl.Where(p => p != du && SkirmishSetup.PlatzArt[p] == SkirmishSetup.ArtMitspieler).ToList();
        int n = s.Slot.Length;
        if (n - 1 > frei.Count)
        {
            Cancel($"Es sind {n - 1} Mitspieler da, aber nur {frei.Count} Plätze stehen auf »Mitspieler« — " +
                   "bitte in der Platzliste mehr Mitspieler-Plätze setzen.");
            return false;
        }
        for (int p = 0; p < 8; p++)
        {
            bool aufKarte = pl.Contains(p);
            int art = aufKarte ? SkirmishSetup.PlatzArt[p] : SkirmishSetup.ArtLeer;
            if (art is SkirmishSetup.ArtMitspieler or SkirmishSetup.ArtMensch) art = SkirmishSetup.ArtLeer;
            s.Art[p] = art;
            s.Team[p] = aufKarte ? SkirmishSetup.PlatzTeam[p] : 0;
        }
        s.Slot[0] = du;
        s.Art[du] = SkirmishSetup.ArtMensch;
        for (int k = 1; k < n; k++) { s.Slot[k] = frei[k - 1]; s.Art[frei[k - 1]] = SkirmishSetup.ArtMensch; }
        return AufstellungFertig(s);
    }

    /// <summary>Online: Raumplatz n = Startplatz pl[n].</summary>
    private bool AufstellungAusRaum(NetSession s, List<int> pl, JsonObject room)
    {
        if (room["slots"] is not JsonArray slots) return true;
        for (int p = 0; p < 8; p++) { s.Art[p] = SkirmishSetup.ArtLeer; s.Team[p] = 0; }
        // Fable D10: Zuschauer (Team 8) bekommen Plaetze ohne Besitz, nicht ihren Raumplatz
        var zuschauerPlatz = new Dictionary<int, int>();   // Raumplatz -> Kartenplatz
        var leer = new Queue<int>(MapPreview.LeerePlaetze(s.Map));
        if (!SkirmishSetup.ZuschauerAlt)
            foreach (var node in slots)
                if (node is JsonObject z && z["player"] != null && ((int?)z["team"] ?? 0) == SkirmishSetup.ZuschauerTeam)
                {
                    int rs = (int?)z["slot"] ?? -1;
                    if (leer.Count == 0)
                    {
                        Cancel("Auf dieser Karte ist kein Platz für einen Zuschauer frei (alle acht Plätze gehören " +
                               "jemandem) — eine Karte mit weniger Startplätzen wählen.");
                        return false;
                    }
                    zuschauerPlatz[rs] = leer.Dequeue();
                }
        foreach (var node in slots)
        {
            if (node is not JsonObject o) continue;
            int rs = (int?)o["slot"] ?? -1;
            bool mensch = o["player"] != null;
            string ai = (string?)o["ai"] ?? "";
            if (!mensch && ai.Length == 0) continue;
            if (zuschauerPlatz.TryGetValue(rs, out int zp)) { s.Art[zp] = SkirmishSetup.ArtZuschauer; continue; }
            if (rs < 0 || rs >= pl.Count)
            {
                Cancel($"Platz {rs + 1} im Raum hat auf dieser Karte keinen Startplatz (sie hat {pl.Count}) — " +
                       "bitte eine größere Karte wählen oder den Platz freigeben.");
                return false;
            }
            int p = pl[rs];
            s.Art[p] = mensch ? SkirmishSetup.ArtMensch : ai switch
            {
                "leicht" or "easy" => SkirmishSetup.ArtLeicht,
                "schwer" or "hard" => SkirmishSetup.ArtSchwer,
                _ => SkirmishSetup.ArtNormal,
            };
            s.Team[p] = System.Math.Clamp((int?)o["team"] ?? 0, 0, 8);
        }
        // die Menschen nach Spielernummer (RoomSlotsAtStart: Gastgeber zuerst, dann nach Raumplatz)
        for (int k = 0; k < s.Slot.Length && k < RoomSlotsAtStart.Count; k++)
        {
            int rs = RoomSlotsAtStart[k];
            if (zuschauerPlatz.TryGetValue(rs, out int zp)) s.Slot[k] = zp;
            else if (rs >= 0 && rs < pl.Count) s.Slot[k] = pl[rs];
        }
        return AufstellungFertig(s);
    }

    /// <summary><c>--net-team=N</c> (Prüfweg, Befehlszeile online): im Raum das eigene Team setzen.</summary>
    private bool _teamGesetzt;
    private void NetzTeamSetzen(LobbyClient l)
    {
        if (_teamGesetzt || !l.InRoom) return;
        foreach (string a in Core.CommandLine.Args)
            if (a.StartsWith("--net-team=") && int.TryParse(a["--net-team=".Length..], out int t))
            {
                _teamGesetzt = true;
                l.SetSlot(t, 0);
                GD.Print($"netz: im Raum Team {t} gesetzt");
            }
            else if (a.StartsWith("--net-platz=") && int.TryParse(a["--net-platz=".Length..], out int pz))
            {
                _teamGesetzt = true;
                l.MoveSlot(pz - 1);
                GD.Print($"netz: im Raum auf Platz {pz} gewechselt");
            }
    }

    private static bool AufstellungFertig(NetSession s)
    {
        s.Aufstellung = true;
        s.AiCount = s.Art.Count(SkirmishSetup.IstKi);
        GD.Print("netz: Aufstellung — " + string.Join(", ", Enumerable.Range(0, 8)
            .Where(p => s.Art[p] != SkirmishSetup.ArtLeer)
            .Select(p => $"Platz {p + 1} " + (s.Art[p] == SkirmishSetup.ArtZuschauer ? $"Zuschauer Nr. {System.Array.IndexOf(s.Slot, p)}"
                                              : s.Art[p] == SkirmishSetup.ArtMensch ? $"Mensch Nr. {System.Array.IndexOf(s.Slot, p)}"
                                                : $"KI {SkirmishSetup.StufeVon(s.Art[p])}") +
                         (s.Team[p] > 0 ? $" Team {s.Team[p]}" : ""))));
        return true;
    }
}
