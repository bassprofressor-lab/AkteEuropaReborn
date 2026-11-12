namespace AkteEuropaReborn.Editor;

using System;
using System.Linq;
using Godot;
using AkteEuropaReborn.Import;

/// <summary><c>--map-edit-start-check</c> — Pruefstand fuer das Startplatz-Werkzeug (P6/D5,
/// 12.11.2026). Geht die Mauswege (StrichBeginnen/Anwenden/StrichEnden).</summary>
public partial class MapEditOverlay
{
    private void StartCheck()
    {
        if (Array.IndexOf(Core.CommandLine.Args, "--map-edit-start-check") < 0) return;
        var sb = new System.Text.StringBuilder("map-edit-start-check\n");
        ulong Abdruck()
        {
            ulong h = 14695981039346656037UL;
            foreach (var a in _map.Sections) foreach (byte b in a) { h ^= b; h *= 1099511628211UL; }
            return h;
        }
        int Basen(int p) => GebaeudeSaetze().Values.Count(g => g.Owner == p && g.Typ == MapGenerator.BaseType);
        int Werke(int p) => GebaeudeSaetze().Values.Count(g => g.Owner == p && g.Typ == MapGenerator.FactoryType);
        int Einheiten(int p)
        {
            var s = _map.Sec(5); int n = 0;
            if (s == null) return 0;
            for (int o = 0; o + CwmData.EntityStride <= s.Length; o += CwmData.EntityStride)
                if (s[o + 0x09] != 0xFF && o / CwmData.EntityStride / CwmData.EntitiesPerPlayer == p) n++;
            return n;
        }
        (int Col, int Row)? BasisVon(int p)
        {
            foreach (var g in GebaeudeSaetze().Values)
                if (g.Owner == p && g.Typ == MapGenerator.BaseType) return (g.Col, g.Row);
            return null;
        }

        ulong h0 = Abdruck();
        int b0 = Basen(0), w0 = Werke(0), e0 = Einheiten(0);
        var alt = BasisVon(0);
        sb.AppendLine($"  vorher Spieler 1: Basen {b0}, Werke {w0}, Einheiten {e0}, Basis bei {alt}");

        // 1. Fehlschlag: auf Zelle (0,0) passt nichts -> Karte unveraendert, kein Schritt mehr
        _brush = Brush.Startplatz; _owner = 0;
        int schritte0 = _zurueck.Count;
        StrichBeginnen(true); Anwenden(new Vector2I(0, 0), true); StrichEnden();
        bool fehlOk = Abdruck() == h0 && _zurueck.Count == schritte0;
        sb.AppendLine($"  Fehlschlag (0,0): {(fehlOk ? "Karte BYTEGLEICH, kein Schritt" : "VERAENDERT")} — {_say}");

        // 2. Verschieben an die erste Stelle, an der das Paket passt (weit weg vom alten)
        bool gesetzt = false;
        for (int r = 4; r < _map.Height - 14 && !gesetzt; r += 2)
            for (int c = 4; c < _map.Width - 18 && !gesetzt; c += 2)
            {
                if (alt is { } a && Math.Abs(a.Col - c) < 30 && Math.Abs(a.Row - r) < 30) continue;
                StrichBeginnen(true); Anwenden(new Vector2I(c, r), true); StrichEnden();
                if (_say.StartsWith("Startplatz Spieler")) gesetzt = true;
            }
        var neu = BasisVon(0);
        sb.AppendLine($"  verschoben: {gesetzt} — {_say}");
        sb.AppendLine($"  nachher Spieler 1: Basen {Basen(0)}, Werke {Werke(0)}, Einheiten {Einheiten(0)}, Basis bei {neu}");
        bool verschiebOk = gesetzt && Basen(0) == b0 && Werke(0) == w0 && neu != alt && Einheiten(0) == StartTruppe;

        // 3. ein Rueckgaengig -> bytegleich zum Anfang
        Rueckgaengig();
        bool zurueckOk = Abdruck() == h0;
        sb.AppendLine($"  1x Rueckgaengig: Karte {(zurueckOk ? "BYTEGLEICH zum Anfang" : "ANDERS")}, Basis bei {BasisVon(0)}");

        bool ok = fehlOk && verschiebOk && zurueckOk;
        sb.Append(ok ? "map-edit-start-check: BESTANDEN" : "map-edit-start-check: DURCHGEFALLEN");
        GD.Print(sb.ToString());
        GetTree().Quit(ok ? 0 : 1);
    }
}
