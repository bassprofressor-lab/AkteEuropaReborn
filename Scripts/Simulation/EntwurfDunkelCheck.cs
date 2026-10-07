namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// <c>--entwurf-dunkel-check</c> — der Prüfstand zur <b>Dunkelstufe des
/// Erstellungsfensters</b> (bug-474, 07.10.2026, <c>berichte/dunkelstufe-0x23-fable.md</c> §8).
///
/// <para>Er rechnet nur auf der Bauteiltafel (<c>component_stats.json</c>, +0x23 =
/// Tragklasse) und auf <see cref="UI.DesignWindow.Traegt"/> — derselben Funktion, die
/// das Fenster färbt und die Sperre setzt. Die Sollmengen stammen aus §4.1 des
/// Berichts (aus der EXE-Tafel abgelesen, C und F byteweise gleich), nicht aus dem
/// Code: T1 Reifen (161), T2 Ketten (164), T3 Panzerreifen (162), T4
/// Mittelstreckenrakete (8), T5 Leichte Bordkanone (1), T6 Trägt-Paare, T7
/// Verbesserungen nie. Dazu die drei Dunkelfarben gegen <c>01.PAL</c>.</para>
///
/// <para><b>Nullmodell <c>--entwurf-dunkel-aus</c>:</b> alles hell, alles trägt —
/// T1, T2, T4, T5, T6 MÜSSEN durchfallen.</para>
///
/// <para>⭐ 07.10.2026, bug-477/478: dazu <b>T8 Listeninhalt</b> (Aufbau 1..79,
/// Verbesserung 81..88 — Listenaufbau <c>0x46C490</c>; Nullmodell
/// <c>--entwurf-listen-alt</c>) und der <b>Tastenpfad Enter</b>, der im Original
/// ERSTELLT (Klickcode 1 @0x413F96, F 0x413DAB; Nullmodell
/// <c>--entwurf-enter-sperre</c> = die alte Setzung von bug-474).</para>
/// </summary>
public partial class MapEntityLayer
{
    public string EntwurfDunkelCheck()
    {
        var sb = new System.Text.StringBuilder("entwurf-dunkel-check\n");
        UI.UnitStatBook.Load();
        bool alles = true;

        // ---- die Tafel selbst: liegt +0x23 vor? ----------------------------------
        int kReifen = UI.DesignWindow.Klasse(161), kPanzer = UI.DesignWindow.Klasse(162), kMsr = UI.DesignWindow.Klasse(8);
        bool tafel = kReifen == 1 && kPanzer == 3 && kMsr == 3;
        sb.AppendLine($"   Tafel +0x23: Reifen {kReifen} (Soll 1), Panzerreifen {kPanzer} (Soll 3), " +
                      $"Mittelstreckenrakete {kMsr} (Soll 3) {(tafel ? "ok" : "⚠ FALSCH")}");
        alles &= tafel;

        var waffen = Enumerable.Range(1, 19).ToList();
        var spezial = Enumerable.Range(65, 15).ToList();
        var fahrwerke = Enumerable.Range(160, 16).ToList();
        var verbesserung = Enumerable.Range(80, 9).ToList();

        HashSet<int> DunkleAufbauten(int fahrwerk, IEnumerable<int> liste)
            => liste.Where(id => !UI.DesignWindow.Traegt(fahrwerk, id)).ToHashSet();
        HashSet<int> DunkleFahrwerke(int aufbau)
            => fahrwerke.Where(id => !UI.DesignWindow.Traegt(id, aufbau)).ToHashSet();

        bool Fall(string titel, HashSet<int> ist, int[] soll)
        {
            var s = soll.ToHashSet();
            bool ok = ist.SetEquals(s);
            sb.AppendLine($"   {(ok ? "ok  " : "⚠ FALSCH")} {titel}: dunkel {{{string.Join(",", ist.OrderBy(x => x))}}} " +
                          $"(Soll {{{string.Join(",", s.OrderBy(x => x))}}})");
            return ok;
        }

        // T1 Reifen (161, Klasse 1) gewaehlt
        alles &= Fall("T1 Reifen: Waffen", DunkleAufbauten(161, waffen),
                      new[] { 2, 3, 6, 7, 8, 9, 11, 12, 13, 14, 17, 18 });
        alles &= Fall("T1 Reifen: Spezial", DunkleAufbauten(161, spezial),
                      new[] { 65, 66, 71, 72, 73, 74, 75, 76, 77, 78 });
        // T2 Ketten (164, Klasse 2)
        alles &= Fall("T2 Ketten: Waffen", DunkleAufbauten(164, waffen), new[] { 3, 7, 8, 13 });
        alles &= Fall("T2 Ketten: Spezial", DunkleAufbauten(164, spezial), new[] { 65 });
        // T3 Panzerreifen (162, Klasse 3): nichts dunkel
        alles &= Fall("T3 Panzerreifen: Waffen+Spezial", DunkleAufbauten(162, waffen.Concat(spezial)),
                      System.Array.Empty<int>());
        // T4 Mittelstreckenrakete (8, Klasse 3): hell genau {162,165,168,170,171}
        var hellT4 = fahrwerke.Where(id => !DunkleFahrwerke(8).Contains(id)).ToHashSet();
        bool t4 = hellT4.SetEquals(new[] { 162, 165, 168, 170, 171 });
        sb.AppendLine($"   {(t4 ? "ok  " : "⚠ FALSCH")} T4 Mittelstreckenrakete: helle Fahrwerke " +
                      $"{{{string.Join(",", hellT4.OrderBy(x => x))}}} (Soll {{162,165,168,170,171}})");
        alles &= t4;
        // T5 Leichte Bordkanone (1, Klasse 1): dunkel nur 172
        alles &= Fall("T5 Leichte Bordkanone: Fahrwerke", DunkleFahrwerke(1), new[] { 172 });
        // T6 Traegt
        var paare = new (int F, int A, bool Soll)[]
            { (161, 1, true), (161, 2, false), (164, 8, false), (162, 8, true), (172, 1, false) };
        int t6ok = 0;
        var t6 = new System.Text.StringBuilder();
        foreach (var (f, a, soll) in paare)
        {
            bool ist = UI.DesignWindow.Traegt(f, a);
            if (ist == soll) t6ok++;
            t6.Append($" ({f},{a})={(ist ? "ja" : "nein")}{(ist == soll ? "" : "⚠")}");
        }
        sb.AppendLine($"   {(t6ok == paare.Length ? "ok  " : "⚠ FALSCH")} T6 Traegt:{t6} " +
                      $"({t6ok}/{paare.Length}; Erstellen/Vorschau sichtbar <=> Traegt)");
        alles &= t6ok == paare.Length;
        // T7 Verbesserung nie dunkel: die Verbesserungsliste wird im Original nicht
        // verglichen (0x46D498 hat keine Dunkelstufe) — bei uns tragen 80..88 die
        // Klasse 0 und werden in keiner Liste als Gegenstueck gefuehrt.
        var kVerb = verbesserung.Select(UI.DesignWindow.Klasse).ToList();
        bool t7 = kVerb.All(k => k is 0 or -1);
        sb.AppendLine($"   {(t7 ? "ok  " : "⚠ FALSCH")} T7 Verbesserung 80..88: Klasse " +
                      $"{{{string.Join(",", kVerb)}}} (Soll 0, nie verglichen)");
        alles &= t7;

        // ---- die drei Dunkelfarben gegen die Palette ------------------------------
        byte[]? pal = PaletteLesen(out string woher);
        if (pal == null) { sb.AppendLine($"   ⚠ keine Palette ({woher}) — Farben NICHT geprueft"); alles = false; }
        else
        {
            foreach (var (was, platz, farbe) in UI.DesignWindow.DunkelFarben())
            {
                int o = 8 + 3 * platz;
                int r = pal[o], g = pal[o + 1], b = pal[o + 2];
                int ur = Mathf.RoundToInt(farbe.R * 255f), ug = Mathf.RoundToInt(farbe.G * 255f), ub = Mathf.RoundToInt(farbe.B * 255f);
                bool gleich = r == ur && g == ug && b == ub;
                alles &= gleich;
                sb.AppendLine($"   {(gleich ? "ok  " : "⚠ FALSCH")} {was}: {woher} ({r},{g},{b}) — bei uns ({ur},{ug},{ub})");
            }
        }

        // ---- der Tastenpfad (bug-477): Enter ERSTELLT wie im Original ------------
        // 0x413F21 -> 0x487A56 -> Klickcode 1 @0x413F96 -> 0x44AA05, ohne +0x23-
        // Pruefung (F 0x413D36 / 0x413DAB). Soll: Entwurf angelegt, nicht gesperrt.
        // Unter --entwurf-enter-sperre (die alte Setzung von bug-474) MUSS der
        // Fall durchfallen: gesperrt 1x, nichts angelegt.
        int gesperrtVor = EntwurfGesperrt;
        bool tastenpfad = true;
        bool listen = true;
        {
            // ⚠ EINGRIFF, gesagt: beim Start einer Kampagne hat das Skript den
            // Besitz noch nicht verteilt (bug-460), die Listen waeren leer. Fuer
            // den Tastenpfad werden sie einmal mit ALLEN Teilen gefuellt und
            // danach wieder wie im Spiel.
            bool alleVor = EntwurfAlleTeile;
            EntwurfAlleTeile = true;
            LoadDesignParts();

            // ---- T8 (bug-478): Listeninhalt wie der Listenaufbau 0x46C490 ----------
            // Aufbauliste = 1..79 (Spezial 65..79 dabei), Verbesserung = 81..88,
            // keine Spezialaufbauten in der Verbesserungsliste. Nullmodell
            // --entwurf-listen-alt: 65..79 in der Verbesserung, 81..88 fehlen.
            var wIds = Designer.Weapons.Select(p => p.Id).ToHashSet();
            var eIds = Designer.Equipment.Select(p => p.Id).ToHashSet();
            bool t8a = Enumerable.Range(1, 19).All(wIds.Contains) && Enumerable.Range(65, 15).All(wIds.Contains);
            bool t8b = Enumerable.Range(81, 8).All(eIds.Contains) && !eIds.Any(id => id is >= 50 and <= 79)
                       && !eIds.Any(id => id >= 100);
            listen = t8a && t8b;
            sb.AppendLine($"   {(listen ? "ok  " : "⚠ FALSCH")} T8 Listen (0x46C490): Aufbau " +
                          $"[{string.Join(",", wIds.OrderBy(x => x))}] (Soll 1..19 + 65..79{(t8a ? "" : " ⚠")}), " +
                          $"Verbesserung [{string.Join(",", eIds.OrderBy(x => x))}] (Soll 81..88, nichts aus 50..79/100+{(t8b ? "" : " ⚠")})");

            int pi = -1, wi = -1;
            for (int a = 0; a < Designer.Propulsions.Count && pi < 0; a++)
                for (int b = 0; b < Designer.Weapons.Count; b++)
                    if (!UI.DesignWindow.Traegt(Designer.Propulsions[a].Id, Designer.Weapons[b].Id))
                    { pi = a; wi = b; break; }
            if (!Designer.Ready || _designs == null)
                sb.AppendLine("   Tastenpfad: keine Entwurfslisten — nicht gestellt");
            else if (pi < 0)
                sb.AppendLine($"   Tastenpfad: unter den Teilen ({Designer.Propulsions.Count} Fahrwerke, " +
                              $"{Designer.Weapons.Count} Aufbauten) traegt jedes Paar — nicht stellbar" +
                              (UI.DesignWindow.DunkelAus ? " (unter --entwurf-dunkel-aus traegt alles)" : ""));
            else
            {
                int fId = Designer.Propulsions[pi].Id, wId = Designer.Weapons[wi].Id;
                bool aktiv = Designer.Active;
                if (!aktiv) Designer.Toggle();
                while (Designer.Cursor != DesignScreen.Row.Propulsion) Designer.Move(1);
                int at = Designer.Propulsions.IndexOf(Designer.CurrentPropulsion!);
                Designer.Change(pi - at);
                Designer.Move(1);
                at = Designer.Weapons.IndexOf(Designer.CurrentWeapon!);
                Designer.Change(wi - at);
                int vorher = _designs.Count;
                string name = Designer.ProposedName();
                _entwurfProbe = true;                       // designs.cfg bleibt unberuehrt
                DesignerInput(0, 0, true);
                _entwurfProbe = false;
                int angelegt = _designs.Count - vorher;
                int gesperrt = EntwurfGesperrt - gesperrtVor;
                // den Probeentwurf wieder wegnehmen (nur der eigene, Platz -1)
                _designs.RemoveAll(d => d.Slot < 0 && d.Name == name && angelegt > 0);
                tastenpfad = angelegt == 1 && gesperrt == 0;
                if (!aktiv) Designer.Close();
                sb.AppendLine($"   {(tastenpfad ? "ok  " : "⚠ FALSCH")} Tastenpfad Enter (Listen mit allen Teilen: " +
                              $"{Designer.Propulsions.Count} Fahrwerke, {Designer.Weapons.Count} Aufbauten) Fahrwerk {fId} (Klasse " +
                              $"{UI.DesignWindow.Klasse(fId)}) + Aufbau {wId} (Klasse {UI.DesignWindow.Klasse(wId)}): " +
                              $"angelegt {angelegt}x, gesperrt {gesperrt}x (Soll 1x / 0x — Klickcode 1 @0x413F96, ohne +0x23-Pruefung)" +
                              (Designer.Message.Length > 0 ? $" »{Designer.Message}«" : ""));
            }
            EntwurfAlleTeile = alleVor;
            LoadDesignParts();
        }
        alles &= tastenpfad && listen;

        sb.Append(alles
            ? "   ✅ BESTANDEN — Dunkelstufe nach Tragklasse +0x23 wie gelesen (T1..T8, Farben, Tastenpfad Enter)"
            : "   ❌ DURCHGEFALLEN — mindestens ein Fall weicht ab");
        if (UI.DesignWindow.DunkelAus)
            sb.Append("   [Nullmodell --entwurf-dunkel-aus: T1/T2/T4/T5/T6 MUESSEN durchfallen]");
        if (EntwurfEnterSperre)
            sb.Append("   [Nullmodell --entwurf-enter-sperre: der Tastenpfad MUSS durchfallen (gesperrt statt angelegt)]");
        if (EntwurfListenAlt)
            sb.Append("   [Nullmodell --entwurf-listen-alt: T8 MUSS durchfallen]");
        return sb.ToString();
    }
}
