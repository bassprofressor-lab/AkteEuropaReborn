namespace AkteEuropaReborn.UI
{
    using System.Collections.Generic;
    using Godot;

    /// <summary>
    /// ⭐ 16.10.2026 — <b><c>--demo-runden-check[=R]</c></b>: LAUFEN DIE MENÜ-DEMOS NACH
    /// VIELEN RUNDEN NOCH WIE IN DER ERSTEN? (KayelGee: »Wenn man das Game eine lange Zeit
    /// laufen lässt, dann buggen irgendwann die Demos.«)
    ///
    /// <para>Spielt R Umläufe (Vorgabe 3) aller importierten Demos hintereinander. Je Demo
    /// fährt er nach dem Laden GENAU <see cref="RundenCheckTakte"/> Bilder (Vorgabe 1000 =
    /// 20 s bei 50 Takten/s) über den echten <c>MapEntityLayer._Process</c> mit fester
    /// Bildzeit 1/50 s — also mit Bildzähler, Wind, KI, allem — und schreibt eine Zeile:
    /// Prüfsumme, Würfe, Einheiten, Gebäude, Flieger, Geld, dazu Knoten, Objekte, Waisen,
    /// verwalteter Speicher und Zahl der <c>process_frame</c>-Abos. Ein Demo in Runde n muss
    /// in Prüfsumme und Würfen GENAU Runde 1 desselben Demos gleichen; Knoten und Objekte
    /// dürfen von Umlauf 2 an nicht mehr wachsen.</para>
    ///
    /// <para>Nullmodell: Runde 1 ist der Vergleich selbst — fällt ein Zustand nicht zurück,
    /// weicht Runde 2 ab. Gegenschalter des Fixes: <c>--demo-runden-alt</c> (siehe
    /// <see cref="MenuBackdrop.DemoRundenAlt"/>).</para>
    /// </summary>
    public partial class MenuBackdrop
    {
        /// <summary><c>--demo-runden-check=R</c>: Umläufe; 0 = aus.</summary>
        public static int RundenCheckUmlaeufe;

        /// <summary><c>--demo-runden-takte=T</c>: Bilder je Demo im Prüfstand.</summary>
        public static int RundenCheckTakte = 1000;

        private readonly Dictionary<int, (ulong Summe, long Wuerfe, string Zahlen)> _rundeEins = new();
        private readonly List<string> _rundenZeilen = new();
        private int _rundenGelaufen, _rundenAbweichungen;
        private long _knotenUmlauf2 = -1, _objekteUmlauf2 = -1, _texUmlauf2 = -1;

        private void RundenCheckDemo(int demoNr)
        {
            if (_entities == null) return;
            _entities.SetProcess(false);          // nur unsere festen Bilder zählen
            int runde = _rundenGelaufen / Mathf.Max(1, _demos.Count) + 1;
            int bz0 = Rendering.MapEntityLayer.Bildzaehler;
            for (int i = 0; i < RundenCheckTakte; i++) _entities._Process(1.0 / 50.0);
            ulong summe = _entities.DeterminismChecksum();
            long wuerfe = Simulation.Determinism.Draws;
            string zahlen = _entities.DemoKennzahlen();
            // verwaltete Huellen erst einsammeln: sonst zaehlt das Objektregister,
            // wann der GC zufaellig lief, und nicht, was wirklich festgehalten wird
            System.GC.Collect(); System.GC.WaitForPendingFinalizers(); System.GC.Collect();
            long knoten = (long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
            long objekte = (long)Performance.GetMonitor(Performance.Monitor.ObjectCount);
            long waisen = (long)Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
            long res = (long)Performance.GetMonitor(Performance.Monitor.ObjectResourceCount);
            long texMb = (long)Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed) >> 20;
            long vidMb = (long)Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) >> 20;
            long mb = System.GC.GetTotalMemory(false) >> 20;
            int abos = GetTree().GetSignalConnectionList("process_frame").Count;

            string urteil;
            // bug-554: 1.DM gleich nach dem Start ist die EINZIGE Demo, in der die KI auch
            // Spieler 0 spielt (WinMain 0x41511B) — ab Umlauf 2 ist Runde 2 ihr Bezug.
            bool ersteMitKi = demoNr == 1 && runde == 2 && !DemoKiAlle;
            if (!_rundeEins.TryGetValue(demoNr, out var eins) || ersteMitKi)
            {
                _rundeEins[demoNr] = (summe, wuerfe, zahlen);
                urteil = ersteMitKi ? "Bezug ab Runde 2 (1.DM nach dem Start mit KI fuer Spieler 0, bug-554)" : "Bezug";
            }
            else if (eins.Summe == summe && eins.Wuerfe == wuerfe && eins.Zahlen == zahlen)
                urteil = "= Runde 1";
            else
            {
                urteil = $"⚠ ABWEICHUNG gegen Runde 1 (Summe {eins.Summe:X16}, Wuerfe {eins.Wuerfe}, {eins.Zahlen})";
                _rundenAbweichungen++;
            }
            string z = $"   Runde {runde} Demo {demoNr,2}: Summe {summe:X16} Wuerfe {wuerfe,7} {zahlen} | "
                     + $"Bildz {bz0,4}->{Rendering.MapEntityLayer.Bildzaehler,4} Knoten {knoten} Objekte {objekte} Ressourcen {res} Textur {texMb} MB Video {vidMb} MB "
                     + $"Waisen {waisen} ParteiTex {Rendering.MapEntityLayer._kennParteiTex} KoerperMitte {Rendering.MapEntityLayer._kennKoerper} frei {Rendering.MapEntityLayer.ParteiTexFreigegeben}/{KartenFreigegeben} Speicher {mb} MB Abos {abos} — {urteil}";
            GD.Print("demo-runden-check:" + z);
            _rundenZeilen.Add(z);
            _rundenGelaufen++;
            // Knotenstand am ENDE von Umlauf 2 und am Ende des letzten Umlaufs
            if (_rundenGelaufen == 2 * _demos.Count) { _knotenUmlauf2 = knoten; _objekteUmlauf2 = objekte; _texUmlauf2 = texMb; }

            if (_rundenGelaufen >= RundenCheckUmlaeufe * _demos.Count)
            {
                var sb = new System.Text.StringBuilder("demo-runden-check\n");
                foreach (var l in _rundenZeilen) sb.AppendLine(l);
                bool wachsen = _knotenUmlauf2 >= 0 && RundenCheckUmlaeufe > 2
                               && (knoten > _knotenUmlauf2 + 20 || objekte > _objekteUmlauf2 + 200
                                   // bug-545: Grafikspeicher (nur im Fenster gezaehlt, kopflos 0)
                                   || texMb > _texUmlauf2 + 32);
                sb.AppendLine($"   Knoten Ende Umlauf 2 {_knotenUmlauf2} / Ende Umlauf {RundenCheckUmlaeufe} {knoten}; "
                            + $"Objekte {_objekteUmlauf2} / {objekte}; Textur {_texUmlauf2} / {texMb} MB"
                            + (texMb == 0 ? " (kopflos: der Renderer zaehlt keinen Grafikspeicher)" : ""));
                bool ok = _rundenAbweichungen == 0 && !wachsen;
                sb.Append(ok
                    ? $"   ✅ BESTANDEN — {_rundenGelaufen} Demos in {RundenCheckUmlaeufe} Umlaeufen, jede Runde = Runde 1"
                    : $"   ❌ DURCHGEFALLEN — {_rundenAbweichungen} Abweichungen gegen Runde 1"
                      + (wachsen ? ", Knoten/Objekte wachsen je Umlauf" : ""));
                if (DemoRundenAlt) sb.Append("   [Nullmodell --demo-runden-alt: MUSS durchfallen]");
                GD.Print(sb);
                if (waisen > 0) { GD.Print("demo-runden-check: Waisenknoten:"); Node.PrintOrphanNodes(); }
                GetTree().Quit(ok ? 0 : 1);
                return;
            }
            _left = 0f;                             // gleich das nächste Demo
        }
    }
}

namespace AkteEuropaReborn.Rendering
{
    public partial class MapEntityLayer
    {
        /// <summary>Kennzahlen des Spielstands für <c>--demo-runden-check</c>:
        /// lebende Einheiten, Gebäude, Flieger, Gesamtgeld, Taktzähler.</summary>
        /// <summary>Groesse der statischen Bildspeicher nach dem letzten <see cref="DemoKennzahlen"/>.</summary>
        public static int _kennParteiTex, _kennKoerper;
        public string DemoKennzahlen()
        {
            int einheiten = 0, gebaeude = 0, tot = 0;
            foreach (var e in _entities)
            {
                if (e.IsProp) continue;
                if (e.Dead) { tot++; continue; }
                if (e.IsBuilding) gebaeude++; else einheiten++;
            }
            int flieger = 0;
            foreach (var a in _special) if (!a.Dead) flieger++;
            long geld = 0;
            foreach (var m in _money) geld += m;
            _kennParteiTex = ParteiTexAlt ? _parteiTexAlt.Count : _parteiTex.Count; _kennKoerper = _koerperMitte.Count;
            return $"Einh {einheiten,4} Geb {gebaeude,3} tot {tot,4} Flieger {flieger,3} Geld {geld,7} Takt {_taktNr}";
        }
    }
}
