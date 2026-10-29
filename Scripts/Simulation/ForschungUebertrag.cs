namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;
using GDict = Godot.Collections.Dictionary<string, Godot.Variant>;

/// <summary>
/// ⭐⭐ 29.10.2026, bug-665 (Reaper R6, »Kampagne: zu Beginn der naechsten Mission alle
/// Forschungsstufen auf 0«, Bild <c>29102026forschungzurückgesetzt.png</c>) — <b>DIE
/// BAUTEILBLOECKE UEBER DEN MISSIONSWECHSEL</b>. Lesung:
/// <c>berichte/forschung-kampagne-r8-opus.md</c> §1 und Bauvorschlag A; Bau:
/// <c>berichte/bau-p2-0-6-12.md</c>.
///
/// <para><b>Original (C):</b> die Tafel <c>0x5045A0</c> (8 × 200 × 58 B) bleibt im Speicher.
/// <c>0x4CFD80</c> kopiert sie beim Sieg nur in den Schatten sec100 (<c>0x4D00E6</c>), der
/// Lader <c>0x41E070</c> liest sec46 bei einer .CWM nicht (<c>0x41E6AC jne 0x41EE09</c>). Die
/// Stufe <c>+0x01</c> hat genau zwei Schreiber (<c>0x4AAACA</c> Aufwertung, <c>0x4AB198</c>
/// Erfindung) — zwischen zwei Missionen fasst sie niemand an. Der Missionsstart
/// <c>0x4B23C0</c> (gerufen aus »LEVEL0 A« <c>0x4882EC</c>, gemeinsamer Schwanz aller
/// Missionen) nullt NUR den Besitz <c>+0x00</c> von Block 0 fuer jede Zeile mit
/// <c>+0x24 != 10</c> (<c>@0x4B23E4</c>), setzt Zeile 0x50 (<c>0x4B23F9</c>) und kopiert dann
/// Block 0 ganz auf die Bloecke 1…7 (<c>0x4B2451 → 0x4B2290</c>, 58 B je Zeile) — die
/// Computerspieler ERBEN damit Stufe und Werte des Menschen. Danach verteilt der
/// Missionsblock den Besitz mit <c>set_part</c> (<c>0x4D0543</c>).</para>
///
/// <para><b>Bei uns bis 0.6.12:</b> jede Mission ist eine neue Szene mit neuer Ebene;
/// <c>BauteileVorbereiten</c> legte die acht Bloecke aus dem Grundstand der EXE an — jede
/// Forschung war weg.</para>
///
/// <para><b>Jetzt:</b> beim Sieg <see cref="ForschungUebertragAblegen"/> (alle acht Bloecke
/// nach <c>campaign.cfg [bauteile]</c>), beim Missionsstart <see cref="ForschungMissionsstart"/>
/// (Bloecke aufnehmen, 0x4B23C0 nachbilden, 0x4B2290, set_part nach dem Fahrplan, Entwuerfe
/// neu rechnen). Der Spielstand traegt die Bloecke ebenfalls (sec46 im .DM, <c>0x41D6DC</c>).</para>
///
/// <para>⚠ <b>UNSERE SETZUNGEN</b> (im Code an Ort und Stelle benannt): der Ablageort
/// (<c>[bauteile]</c>, gepackt); Mission 1 beginnt beim Grundstand (= neue Kampagne);
/// eine Mission OHNE Fahrplan behaelt den Grundbesitz statt ihn zu nullen; wer eine
/// FRUEHERE Mission nachspielt, bekommt den Stand vom Ende der zuletzt gewonnenen
/// (das Original haette dann den Stand im Speicher — ebenfalls den letzten).
/// ⭐ Seit 29.10.2026 (bug-667) gilt das nur noch unter <c>--forschung-anfangsstand-alt</c>:
/// eine geschaffte Mission beginnt mit ihrem beim ersten Erreichen festgehaltenen
/// Anfangsstand (<c>[forschung_anfang]</c>, wie das Geld ueber <c>[mission_balance]</c>);
/// Pruefstand <c>--forschung-anfangsstand-check</c> (Rendering/NeueKampagneLauf.cs).</para>
///
/// <para>Gegenschalter: <c>--forschung-uebertrag-alt</c> (jede Mission vom Grundstand,
/// Stand 0.6.11), <c>--ki-erbt-forschung-alt</c> (ohne die Kopie Block 0 → 1…7),
/// <c>--spielstand-bauteile-alt</c> (der Spielstand ohne Bloecke).
/// Pruefstand <c>--forschung-uebertrag-check</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--forschung-uebertrag-alt</c> — bug-665: jede Mission beginnt beim
    /// Grundstand der EXE, nichts wird abgelegt, nichts genullt (Stand 0.6.11).</summary>
    public static bool ForschungUebertragAlt;

    /// <summary><c>--ki-erbt-forschung-alt</c> — bug-665: ohne die Kopie Block 0 → 1…7
    /// beim Missionsstart (0x4B2290); die Bloecke 1…7 bleiben, wie sie abgelegt waren.</summary>
    public static bool KiErbtForschungAlt;

    /// <summary><c>--spielstand-bauteile-alt</c> — bug-665: der Spielstand schreibt und
    /// liest die Bauteilbloecke nicht (nach dem Laden der Grundstand, Stand 0.6.11).</summary>
    public static bool SpielstandBauteileAlt;

    /// <summary>58 B × 200 Zeilen = ein Spielerblock.</summary>
    public const int BauteilBlockBytes = 200 * 58;

    /// <summary>Was der letzte Missionsstart getan hat — fuer die Pruefzeile.</summary>
    public static string ForschungUebertragZeile = "";

    /// <summary>Hat der zuletzt geladene Spielstand die Bloecke getragen?</summary>
    public bool BauteileAusSpielstand { get; private set; }

    /// <summary>Die acht Bloecke als ein Feld von 92 800 Byte, in der Reihenfolge des
    /// Originals (<c>0x5045A0 + 58·(Bauteil + 200·Spieler)</c>).</summary>
    public byte[] BauteileAbbild()
    {
        BauteileVorbereiten();
        var roh = new byte[8 * BauteilBlockBytes];
        for (int p = 0; p < 8; p++)
            for (int b = 0; b < 200; b++)
            {
                var c = _bauteile![p][b];
                System.Array.Copy(c, 0, roh, (p * 200 + b) * 58, System.Math.Min(58, c.Length));
            }
        return roh;
    }

    /// <summary>Bloecke aus einem Abbild setzen — <paramref name="bis"/> Bloecke ab 0.</summary>
    private void BauteileAusAbbild(byte[] roh, int bis = 8)
    {
        BauteileVorbereiten();
        for (int p = 0; p < bis && p < 8; p++)
            for (int b = 0; b < 200; b++)
            {
                var z = new byte[58];
                System.Array.Copy(roh, (p * 200 + b) * 58, z, 0, 58);
                _bauteile![p][b] = z;
            }
    }

    /// <summary>⚠ UNSERE SETZUNG: deflate + Base64 mit Kennung »z1:« — die acht Bloecke sind
    /// zu sieben Achteln Kopien von Block 0 und packen auf wenige Kilobyte (92 800 B roh
    /// waeren 124 KB Base64 in einer Datei, die jeder Kampagnenzugriff ganz liest).</summary>
    public static string BauteileKodieren(byte[] roh)
    {
        using var ms = new System.IO.MemoryStream();
        using (var z = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionLevel.Optimal, true))
            z.Write(roh, 0, roh.Length);
        return "z1:" + System.Convert.ToBase64String(ms.ToArray());
    }

    public static byte[]? BauteileDekodieren(string s)
    {
        if (s == null || !s.StartsWith("z1:")) return null;
        try
        {
            var gepackt = System.Convert.FromBase64String(s[3..]);
            using var ein = new System.IO.MemoryStream(gepackt);
            using var z = new System.IO.Compression.DeflateStream(ein, System.IO.Compression.CompressionMode.Decompress);
            using var aus = new System.IO.MemoryStream();
            z.CopyTo(aus);
            var roh = aus.ToArray();
            return roh.Length == 8 * BauteilBlockBytes ? roh : null;
        }
        catch (System.Exception) { return null; }
    }

    /// <summary>
    /// <b>Beim Sieg</b> — der Ort, an dem <c>0x4CFD80</c> den Schatten sec100 schreibt
    /// (<c>0x4D00E6</c>). Aus <c>MapViewer.ShowEnd</c>, NACH »alle abfeuern«
    /// (<c>ForschungMissionsende</c>, 0x4AB950), damit eine bezahlte, laufende
    /// Aufwertung schon in der Stufe steht. ⚠ Waehrend der Mission wird NIE geschrieben:
    /// ein Neustart nimmt den Stand vom Ende der vorigen Mission (0x4D0290).
    /// </summary>
    public void ForschungUebertragAblegen(int mission)
    {
        if (ForschungUebertragAlt || mission <= 0) return;
        string daten = BauteileKodieren(BauteileAbbild());
        Campaign.CampaignManager.Bauteile = (daten, mission);
        GD.Print($"forschung-uebertrag: Mission {mission} gewonnen — 8 Bauteilbloecke nach " +
                 $"{Campaign.CampaignManager.SavePath} [bauteile] ({daten.Length} Zeichen, 0x4CFD80 -> sec100)");
    }

    /// <summary>
    /// <b>Beim Start einer Kampagnenmission</b> (MapViewer, VOR StartCampaign, Mitnahme
    /// und Nachziehen — die lesen die Bloecke schon). Ablauf wie im Original:
    /// <list type="number">
    /// <item>Bloecke aus <c>[bauteile]</c> (der Speicher des Originals). Mission 1 oder kein
    /// Abschnitt: der Grundstand.</item>
    /// <item><c>0x4B23C0</c>: Block 0, jede Zeile mit <c>+0x24 != 10</c>: <c>+0x00 := 0</c>
    /// (<c>@0x4B23E4</c>), Zeile 0x50 := 1 (<c>0x4B23F9</c>).</item>
    /// <item><c>0x4B2290</c>: Block 0 ganz auf 1…7 (<c>--ki-erbt-forschung-alt</c> aus).</item>
    /// <item><c>set_part</c> des Missionsblocks (<c>0x4D0543</c>): jedes (Spieler, Bauteil)
    /// des Fahrplans <c>+0x00 := 1</c>.</item>
    /// <item>Die Entwuerfe der Spieler, deren Block vom Grundstand abweicht, neu rechnen
    /// (<c>0x4B24B0</c>, »die fuenf weiteren Rufe« hinter 0x4B2451).</item>
    /// </list>
    /// </summary>
    public void ForschungMissionsstart(int mission, bool neueKampagne = false)
    {
        if (ForschungUebertragAlt)
        {
            ForschungUebertragZeile = $"Mission {mission}: --forschung-uebertrag-alt — Grundstand, nichts genullt";
            GD.Print("forschung-uebertrag: " + ForschungUebertragZeile);
            return;
        }
        BauteileVorbereiten();
        string quelle;
        // ⚠ UNSERE SETZUNG: Mission 1 IST der Kampagnenbeginn (»Neues Spiel« des
        // Originals) — wer sie (wieder) spielt, beginnt beim Grundstand.
        if (mission <= 1) quelle = "Grundstand (Mission 1 = Kampagnenbeginn, unsere Setzung)";
        // --fresh-campaign: CampaignManager.Reset laeuft erst NACH diesem Aufruf (MapViewer,
        // Kampagnenzweig) — die neue Kampagne darf den alten Abschnitt nicht mehr lesen.
        else if (neueKampagne) quelle = "Grundstand (neue Kampagne, --fresh-campaign)";
        // ⭐ 29.10.2026, bug-667 — eine SCHON GESCHAFFTE Mission (aus der Missionsuebersicht
        // nachgespielt, UI.CampaignScreen = unsere Zutat) beginnt mit den Bloecken, mit denen
        // sie zum ERSTEN MAL begonnen wurde — wie das Geld ueber [mission_balance].
        // ⚠ UNSERE SETZUNG (das Original kennt kein Nachspielen). Der Weiterweg und »Mission
        // wiederholen« der laufenden Mission (mission = Completed+1) bleiben beim Stand vom
        // Ende der vorigen (0x4D0290). Gegenschalter --forschung-anfangsstand-alt.
        else if (!Campaign.CampaignManager.ForschungAnfangsstandAlt
                 && mission <= Campaign.CampaignManager.Completed
                 && Campaign.CampaignManager.ForschungAnfangsstandOf(mission) is { Length: > 0 } anfang
                 && BauteileDekodieren(anfang) is { } rohAnfang)
        {
            BauteileAusAbbild(rohAnfang);
            quelle = $"campaign.cfg [{Campaign.CampaignManager.ForschungAnfangAbschnitt}] (Anfangsstand Mission {mission}, bug-667)";
        }
        else
        {
            var (daten, von) = Campaign.CampaignManager.Bauteile;
            var roh = daten.Length > 0 ? BauteileDekodieren(daten) : null;
            if (roh != null)
            {
                BauteileAusAbbild(roh);
                quelle = $"campaign.cfg [bauteile] (Sieg in Mission {von})";
            }
            else quelle = daten.Length > 0 ? "Grundstand (⚠ [bauteile] unlesbar)" : "Grundstand (kein [bauteile])";
            // bug-667: beim ERSTEN Erreichen im Kampagnenfluss (mission = Completed+1) die
            // Bloecke festhalten, wie sie VOR 0x4B23C0 aufgenommen wurden; Riegel und
            // Nie-Ueberschreiben in CampaignManager.NoteForschungAnfangsstand.
            if (mission >= 2 && Campaign.CampaignManager.NoteForschungAnfangsstand(mission, BauteileKodieren(BauteileAbbild())))
                quelle += $"; als Anfangsstand Mission {mission} festgehalten (bug-667)";
        }

        var b0 = _bauteile![0];
        var u = Campaign.CampaignManager.UnlocksFor(mission);
        int genullt = 0;
        if (u.Known)
        {
            for (int r = 0; r < 200; r++)
            {
                var c = b0[r];
                if (c.Length < 58 || c[0x24] == 10) continue;   // erfunden: ueberlebt (Techstufe 10)
                if (c[0x00] != 0) genullt++;
                c[0x00] = 0;                                     // @0x4B23E4
            }
            if (b0[0x50].Length >= 58) b0[0x50][0x00] = 1;       // 0x4B23F9 byte[0x5057C0] := 1
        }
        else
        {
            // ⚠ UNSERE SETZUNG: ohne Fahrplan (alter Datenstand) gibt es keinen set_part,
            // der den Besitz zurueckgaebe — dann der Besitz des Grundstands.
            for (int r = 0; r < 200; r++)
            {
                var c = b0[r];
                if (c.Length < 58 || c[0x24] == 10) continue;
                c[0x00] = GrundBauteil(r) is { Length: >= 58 } g ? g[0x00] : (byte)0;
            }
        }
        // 0x4B2290: Block 0 → 1…7, ganze Zeilen (rep movsd 14 + movsw = 58 B)
        if (!KiErbtForschungAlt)
            for (int p = 1; p < 8; p++)
                for (int r = 0; r < 200; r++)
                    _bauteile[p][r] = (byte[])b0[r].Clone();
        // set_part des Missionsblocks (0x4D0520 → 0x4D0543 schreibt genau +0x00)
        int gesetzt = 0;
        if (u.Known)
            foreach (var (p, b) in u.Parts)
            {
                if (p is < 0 or > 7 || b is < 0 or >= 200) continue;
                var c = _bauteile[p][b];
                if (c.Length < 58) continue;
                c[0x00] = 1;
                gesetzt++;
            }
        // Entwuerfe neu rechnen — nur, wo der Block vom Grundstand abweicht (Besitz +0x00
        // ausgenommen, er geht in die Entwurfsrechnung nicht ein); sonst bliebe
        // _entwuerfeJeSpieler null und es gilt der rohe sec47-Schwanz wie bisher.
        LoadDesigns();
        var neu = new List<int>();
        for (int p = 0; p < 8; p++)
            if (BlockWeichtAb(p)) { EntwuerfeNachziehen(p); neu.Add(p); }
        int stufen = 0;
        for (int r = 0; r < 200; r++) if (b0[r].Length >= 58 && b0[r][0x01] > 0) stufen++;
        ForschungUebertragZeile = $"Mission {mission}: {quelle}; 0x4B23C0 {(u.Known ? $"Besitz genullt ({genullt} Zeilen)" : "ohne Fahrplan — Grundbesitz")}, "
            + $"{(KiErbtForschungAlt ? "⚠ --ki-erbt-forschung-alt: KEINE Kopie 0 -> 1..7" : "Block 0 -> 1..7 (0x4B2290)")}, "
            + $"set_part {gesetzt}; Spieler 0: {stufen} Zeilen mit Stufe > 0; Entwuerfe neu fuer [{string.Join(",", neu)}]";
        GD.Print("forschung-uebertrag: " + ForschungUebertragZeile);
    }

    /// <summary>Weicht ein Block (ausser im Besitzbyte) vom Grundstand ab?</summary>
    private bool BlockWeichtAb(int p)
    {
        for (int r = 0; r < 200; r++)
        {
            var c = _bauteile![p][r];
            var g = GrundBauteil(r);
            if (g == null) { for (int k = 1; k < c.Length; k++) if (c[k] != 0) return true; continue; }
            for (int k = 1; k < 58 && k < c.Length && k < g.Length; k++)
                if (c[k] != g[k]) return true;
        }
        return false;
    }

    // ---- der Spielstand (sec46 im .DM, Speicherer 0x41D6DC) ----------------------

    /// <summary>Schreibt die acht Bloecke in den Spielstand (Format 4).</summary>
    private void BauteileInSpielstand(Core.SaveGame.Writer w)
    {
        if (SpielstandBauteileAlt) return;
        w.Str("bauteile", BauteileKodieren(BauteileAbbild()));
    }

    /// <summary>Liest sie zurueck; ein Stand ohne Bloecke (Format 1..3) laesst den
    /// Grundstand stehen — MapViewer nimmt dann den Missionsstart (siehe dort).</summary>
    private void BauteileAusSpielstandLesen(GDict root)
    {
        BauteileAusSpielstand = false;
        if (SpielstandBauteileAlt || !root.TryGetValue("bauteile", out var v) || v.VariantType != Variant.Type.String) return;
        var roh = BauteileDekodieren(v.AsString());
        if (roh == null) { GD.PrintErr("Spielstand: Bauteilbloecke unlesbar — Grundstand"); return; }
        BauteileAusAbbild(roh);
        LoadDesigns();
        for (int p = 0; p < 8; p++)
            _entwuerfeJeSpieler[p] = null;
        for (int p = 0; p < 8; p++)
            if (BlockWeichtAb(p)) EntwuerfeNachziehen(p);
        BauteileAusSpielstand = true;
    }

    /// <summary>Pruefzahl ueber Stufe und Werte (ohne Besitz) aller acht Bloecke — fuer
    /// den Spielstand-Fingerabdruck.</summary>
    public long BauteilePruefzahl()
    {
        BauteileVorbereiten();
        long s = 0;
        for (int p = 0; p < 8; p++)
            for (int r = 0; r < 200; r++)
            {
                var c = _bauteile![p][r];
                for (int k = 1; k < c.Length; k++) s += c[k] * (long)((k * 7 + r * 13 + p * 31) % 97 + 1);
            }
        return s;
    }
}
