namespace AkteEuropaReborn.Rendering;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

/// <summary>
/// <c>--ki-tafeln-check</c> (22.10.2026, Paket W2, bug-626/627) — der Prüfstand zu den
/// KI-Sektortafeln aus <c>AIxx.CWI</c> und ihrer Brückennachführung
/// (Simulation/KiMarschwege.cs, <see cref="KiSektorTafelnBauen"/>).
///
/// <para><b>T1/T2 — Tafeln gleich der Datei.</b> Der Sollwert kommt UNABHÄNGIG von der
/// Laufzeit aus der Originaldatei auf CD bzw. Installation (<see cref="KiCwiLesen"/>,
/// nicht über <c>user://data</c>): Anker = Datei, Verbindungen = Sicherungshälfte der
/// Datei plus die Brückensätze, deren Zelle JETZT Lage 100..199 trägt (C
/// <c>0x41CAD0</c>/<c>0x41CA40</c>). Gezählt wird Byte für Byte über 0x1E4 + 0x1E4.</para>
///
/// <para><b>T3 — Brückenfall.</b> Eine stehende Brücke, unter der ein Brückensatz eine
/// Verbindung öffnet, die die Grundtafel geschlossen hat und kein anderer stehender Satz
/// öffnet, wird über den ECHTEN Weg abgerissen (<c>BrueckeAbreissen</c>, »Erase bridge«
/// <c>0x4CB0A0</c>): Soll die Verbindung danach ZU und die ganze Tafel wieder gleich dem
/// Sollwert. Danach wird an derselben Stelle über <c>BrueckeAnlegen</c> (<c>0x4CC280</c>)
/// neu gebaut: Soll wieder OFFEN, sobald die Satzzelle Lage 100..199 trägt.</para>
///
/// <para>Nullmodelle: <c>--ki-tafeln-gerechnet</c> (Laufzeitrechnung → T1/T2 weichen ab,
/// Brückensätze fehlen) und <c>--ki-brueckennachfuehrung-aus</c> (nach dem Abriss bleibt
/// die Verbindung offen → T3 ROT).</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Während <c>--ki-tafeln-check</c>: Nachführungen protokollieren.</summary>
    public static bool KiTafelnPruefAn;

    /// <summary>Der Sollwert der Verbindungen: Sicherung der Datei + stehende Brücken.</summary>
    private byte[] KiTafelSollVerb(byte[] cwi)
    {
        var soll = new byte[0x1E4];
        Array.Copy(cwi, 0x7D0 + 0x1E4, soll, 0, 0x1E4);
        Array.Copy(soll, 0xF2, soll, 0, 0xF2);
        for (int i = 0; i < 400; i++)
        {
            int c = cwi[5 * i];
            if (c == 0) continue;
            int lage = _rampen.TryGetValue(c * 1024 + cwi[5 * i + 1], out int l) ? l : 0;
            if (lage < 100 || lage >= 200) continue;
            int off = 2 * (11 * cwi[5 * i + 2] + cwi[5 * i + 3]) + cwi[5 * i + 4];
            if (off >= 0 && off < soll.Length) soll[off] = 1;
        }
        return soll;
    }

    private static int KiGleicheBytes(byte[] a, int aOff, byte[] b, int bOff, int n)
    {
        int g = 0;
        for (int i = 0; i < n; i++) if (a[aOff + i] == b[bOff + i]) g++;
        return g;
    }

    public string KiTafelnCheck()
    {
        int m = UI.SkirmishSetup.CampaignMission;
        var sb = new StringBuilder($"ki-tafeln-check K{m}\n");
        sb.AppendLine($"  Schalter: --ki-tafeln-gerechnet {KiTafelnGerechnet}, --ki-brueckennachfuehrung-aus {KiBrueckennachfuehrungAus}, " +
                      $"--ki-sektoranker-alt {KiSektorankerAlt}");
        if (!InCampaign) return sb.Append("ki-tafeln-check: KEIN URTEIL (kein Kampagnenlauf)").ToString();
        KiTafelnSicher();
        sb.AppendLine($"  Quelle: {KiTafelQuelle}; Nachfuehrungen bisher {KiBrueckenNachgefuehrt}, beim Laden geoeffnet {KiBrueckenGeoeffnet}");

        var cwi = KiCwiLesen(m);
        if (cwi == null)
            return sb.Append($"ki-tafeln-check K{m}: KEIN URTEIL (keine AI{m:00}.CWI auf CD/Installation — " +
                             "das Original rechnet diese Karte zur Laufzeit)").ToString();

        // ---- T1/T2 ----
        int ankG = KiGleicheBytes(_kiAnker, 0, cwi, 0x7D0, 0x1E4);
        var soll = KiTafelSollVerb(cwi);
        int verG = KiGleicheBytes(_kiVerb, 0, soll, 0, 0x1E4);
        int saetze = 0, stehend = 0, oeffnend = 0;
        var grund = new byte[0xF2];
        Array.Copy(cwi, 0x7D0 + 0x1E4 + 0xF2, grund, 0, 0xF2);
        for (int i = 0; i < 400; i++)
        {
            int c = cwi[5 * i];
            if (c == 0) continue;
            saetze++;
            int lage = _rampen.TryGetValue(c * 1024 + cwi[5 * i + 1], out int l) ? l : 0;
            if (lage is >= 100 and < 200)
            {
                stehend++;
                int off = 2 * (11 * cwi[5 * i + 2] + cwi[5 * i + 3]) + cwi[5 * i + 4];
                if (off is >= 0 and < 0xF2 && grund[off] == 0) oeffnend++;
            }
        }
        int abw = 0;
        var abwText = new List<string>();
        for (int i = 0; i < 0xF2; i++)
            if (_kiVerb[i] != soll[i])
            {
                abw++;
                if (abwText.Count < 8) abwText.Add($"({i / 2 / 11},{i / 2 % 11}){(i % 2 == 0 ? "->x+1" : "->y+1")} uns {_kiVerb[i]} soll {soll[i]}");
            }
        sb.AppendLine($"  T1 Anker {ankG}/484 Byte gleich der Datei");
        sb.AppendLine($"  T2 Verbindungen {verG}/484 Byte gleich Soll (Datei + stehende Bruecken); Brueckensaetze {saetze}, " +
                      $"davon auf stehender Bruecke {stehend}, oeffnen gegen die Grundtafel {oeffnend}" +
                      (abwText.Count > 0 ? $"; anders: {string.Join("; ", abwText)}" : ""));
        bool tafelOk = ankG == 0x1E4 && verG == 0x1E4;

        // ---- T3: Brueckenfall ----
        string fall = "", bau = "";
        bool? fallOk = null, bauOk = null;
        KiTafelnPruefAn = true;
        foreach (var st in _stege.Where(s => s.Karte).ToList())
        {
            var zellen = new HashSet<int>(SatzZellen(st).Select(z => z.Zelle.X * 1024 + z.Zelle.Y));
            int satz = -1, off = -1;
            for (int i = 0; i < 400 && satz < 0; i++)
            {
                int c = cwi[5 * i];
                if (c == 0 || !zellen.Contains(c * 1024 + cwi[5 * i + 1])) continue;
                int o = 2 * (11 * cwi[5 * i + 2] + cwi[5 * i + 3]) + cwi[5 * i + 4];
                if (o is < 0 or >= 0xF2 || grund[o] != 0) continue;
                // kein anderer stehender Satz ausserhalb dieser Bruecke oeffnet dieselbe Verbindung
                bool andere = false;
                for (int j = 0; j < 400; j++)
                {
                    int c2 = cwi[5 * j];
                    if (c2 == 0 || zellen.Contains(c2 * 1024 + cwi[5 * j + 1])) continue;
                    int o2 = 2 * (11 * cwi[5 * j + 2] + cwi[5 * j + 3]) + cwi[5 * j + 4];
                    int lage2 = _rampen.TryGetValue(c2 * 1024 + cwi[5 * j + 1], out int l2) ? l2 : 0;
                    if (o2 == o && lage2 is >= 100 and < 200) andere = true;
                }
                if (andere) continue;
                satz = i; off = o;
            }
            if (satz < 0) continue;

            int vorher = _kiVerb[off];
            int nachf0 = KiBrueckenNachgefuehrt;
            var ecke = BrueckenEcke(st);
            bool senk = IstSenkrecht(st);
            int wasser = st.Wasser;
            BrueckeAbreissen(st);
            var soll2 = KiTafelSollVerb(cwi);
            int verG2 = KiGleicheBytes(_kiVerb, 0, soll2, 0, 0x1E4);
            int nachher = _kiVerb[off];
            fallOk = vorher == 1 && nachher == 0 && verG2 == 0x1E4;
            fall = $"Bruecke Platz {st.Slot} Ecke ({ecke.X},{ecke.Y}) {(senk ? "senkrecht" : "waagerecht")}, Satz {satz} " +
                   $"Zelle ({cwi[5 * satz]},{cwi[5 * satz + 1]}) -> Sektor ({cwi[5 * satz + 2]},{cwi[5 * satz + 3]}) " +
                   $"{(cwi[5 * satz + 4] == 0 ? "x+1" : "y+1")}: vor dem Abriss {vorher}, danach {nachher}, " +
                   $"Tafel {verG2}/484 gleich Soll, Nachfuehrungen +{KiBrueckenNachgefuehrt - nachf0}";

            // ---- der Bau an derselben Stelle (0x4CC280): die Fahrbahnkoepfe probieren
            var koepfe = senk
                ? new[] { new Vector2I(ecke.X + 1, ecke.Y), new Vector2I(ecke.X + 1, ecke.Y + wasser + 1) }
                : new[] { new Vector2I(ecke.X, ecke.Y + 1), new Vector2I(ecke.X + wasser + 1, ecke.Y + 1) };
            foreach (var k in koepfe)
            {
                if (!BrueckePlatzOk(k.X, k.Y)) continue;
                int n0 = KiBrueckenNachgefuehrt;
                if (!BrueckeAnlegen(-1, k.X, k.Y)) continue;
                int lageSatz = _rampen.TryGetValue(cwi[5 * satz] * 1024 + cwi[5 * satz + 1], out int ls) ? ls : 0;
                var soll3 = KiTafelSollVerb(cwi);
                int verG3 = KiGleicheBytes(_kiVerb, 0, soll3, 0, 0x1E4);
                bool deckt = lageSatz is >= 100 and < 200;
                bauOk = verG3 == 0x1E4 && (!deckt || _kiVerb[off] == 1) && KiBrueckenNachgefuehrt > n0;
                bau = $"Pionierbruecke ab ({k.X},{k.Y}): Satzzelle Lage {lageSatz} ({(deckt ? "ueberdeckt" : "NICHT ueberdeckt")}), " +
                      $"Verbindung {_kiVerb[off]}, Tafel {verG3}/484 gleich Soll, Nachfuehrungen +{KiBrueckenNachgefuehrt - n0}";
                break;
            }
            if (bau.Length == 0) bau = "kein Bauplatz an den Fahrbahnkoepfen (BrueckePlatzOk) — KEIN URTEIL";
            break;
        }
        KiTafelnPruefAn = false;
        if (fall.Length == 0) fall = "keine stehende Kartenbruecke mit eigenem oeffnendem Satz — KEIN URTEIL";
        sb.AppendLine($"  T3 Abriss: {fall}");
        sb.AppendLine($"  T3 Bau:    {bau}");

        string urteil = !tafelOk || fallOk == false || bauOk == false ? "DURCHGEFALLEN" : "BESTANDEN";
        sb.Append($"ki-tafeln-check K{m}: Quelle {(KiTafelQuelle.StartsWith("AI") ? "CWI" : "gerechnet")}, Anker {ankG}/484, " +
                  $"Verbindungen {verG}/484 (Brueckensaetze {saetze}, stehend {stehend}, oeffnend {oeffnend}), " +
                  $"Abriss {(fallOk == null ? "KEIN URTEIL" : fallOk == true ? "nachgefuehrt" : "NICHT nachgefuehrt")}, " +
                  $"Bau {(bauOk == null ? "KEIN URTEIL" : bauOk == true ? "nachgefuehrt" : "NICHT nachgefuehrt")} — {urteil}");
        return sb.ToString();
    }
}
