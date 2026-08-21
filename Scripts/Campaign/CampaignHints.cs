namespace AkteEuropaReborn.Campaign;

using System.Collections.Generic;
using Godot;
using GDict = Godot.Collections.Dictionary<string, Godot.Variant>;

/// <summary>
/// <b>DIE KONTEXTHILFE DER KAMPAGNE</b> — der gemeinsame Vorspann des
/// Originals, Block C <c>0x497540…0x49814D</c>, gelesen am 21.08.2026.
///
/// <para><b>Was es ist:</b> wer eine Einheit anwählt, die ein bestimmtes
/// Bauteil trägt, bekommt <b>einmal in der ganzen Kampagne</b> den Hilfetext
/// dazu. Kein Missionsskript, kein Auslöser in der Karte — es hängt allein
/// daran, was der Spieler anklickt.</para>
///
/// <para>Jedes der 34 Tore ist derselbe Zwölfzeiler:</para>
/// <code>
///   if (v[n] != 0)                      raus     ; schon gezeigt
///   if (word[0x4FA0C8] >= 0x1F40)       raus     ; nichts angewählt
///   al = byte[gewaehlte*78 + 0x6E26C8 + FELD]
///   if (al != WERT)                     raus
///   v[n]++
///   show_text2(100, 200, TEXT, 0)
/// </code>
///
/// <para>⭐ <b>Die Selbstprobe der Auslesung:</b> <c>Text = v − 300</c> gilt für
/// <b>32 von 34</b> Toren; nur v[346]→67 und v[347]→19 fallen heraus.</para>
///
/// <para><b>Warum es bei uns nie erschienen ist</b>, hat zwei Gründe, und beide
/// sind belegt: der Vorspann ist ein <i>gemeinsamer</i> Block vor den 33
/// Missionsblöcken, den unser Regelleser nie gesehen hat — und selbst mit den
/// Regeln käme jedes Fenster in JEDER Mission wieder, weil v[346…381] über 300
/// liegen und genau die im Original in die nächste Mission übergehen
/// (<c>0x4CFD80</c> nullt nur v[0…299]).</para>
///
/// <para>⚠ <b>UNSERE Umsetzung des Übertrags:</b> statt v[300…499] zu
/// verschleppen, führen wir die gezeigten Tore als eigene Menge — sie ist
/// kampagnenweit und geht in den Spielstand. Das ist gleichwertig, solange
/// niemand sonst diese Variablen liest, und im Original tut das niemand: die
/// 34 Tore sind ihre einzigen Leser und Schreiber.</para>
///
/// <para>⚠ <b>NEUN Tore fehlen</b> — v[346], v[372…378] und v[381]. ⚠ Hier
/// stand »fünf«: das zählte nur die Tore ganz ohne Wert. Vier weitere
/// (v[346], 373, 376, 378) tragen zwar einen Wert, aber <b>kein Feld</b> —
/// sie prüfen etwas anderes als ein Bauteilbyte der angewählten Einheit, und
/// was, ist nicht gelesen. Alle neun werden übergangen und <b>gezählt</b>
/// (<see cref="OhneBedingung"/>), nicht geraten.</para>
/// </summary>
public static class CampaignHints
{
    /// <summary>Ein Tor: welches Satzfeld auf welchen Wert geprüft wird, und
    /// welcher Hilfetext dann erscheint.</summary>
    public sealed class Tor
    {
        public int Var;             // v[n] des Originals — unser Merkerschlüssel
        public int Feld;            // Versatz im Einheitensatz (0x0D, 0x0E, 0x0F, 0x10)
        public int[] Werte = System.Array.Empty<int>();
        public int Text;            // die HELPG-Nummer
    }

    private static readonly List<Tor> _tore = new();
    private static bool _geladen;

    /// <summary>Die Tore, die eine Bedingung tragen und damit auswertbar sind.
    /// </summary>
    public static IReadOnlyList<Tor> Tore { get { Laden(); return _tore; } }

    /// <summary>Wie viele Tore die Datei führt und wie viele davon ohne
    /// Bedingung sind — ⚠ ohne diese zwei Zahlen ist »es erscheint nichts«
    /// nicht von »die Datei ist leer« zu unterscheiden.</summary>
    public static int ToreInDatei { get; private set; }
    public static int OhneBedingung { get; private set; }

    /// <summary>Welche Tore schon gefeuert haben. **Kampagnenweit** — sie
    /// überlebt den Missionswechsel, und der Spielstand trägt sie mit.</summary>
    private static readonly HashSet<int> _gezeigt = new();

    public static int GezeigtCount => _gezeigt.Count;
    public static IEnumerable<int> Gezeigt => _gezeigt;

    /// <summary>Alles vergessen — ein neuer Kampagnenanfang.</summary>
    public static void Vergiss() => _gezeigt.Clear();

    /// <summary>Aus dem Spielstand zurückholen.</summary>
    public static void Merke(int varNr) => _gezeigt.Add(varNr);

    private static void Laden()
    {
        if (_geladen) return;
        _geladen = true;
        string pfad = Core.Content.Path("campaign_hints_raw.json");
        if (!FileAccess.FileExists(pfad)) pfad = "res://Data/campaign_hints_raw.json";
        if (!FileAccess.FileExists(pfad))
        {
            GD.Print("kontexthilfe: campaign_hints_raw.json fehlt — keine Tore");
            return;
        }
        using var f = FileAccess.Open(pfad, FileAccess.ModeFlags.Read);
        if (f == null) return;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok ||
            json.Data.VariantType != Variant.Type.Dictionary) return;
        var root = json.Data.AsGodotDictionary<string, Variant>();
        if (!root.TryGetValue("tore", out var tv) || tv.VariantType != Variant.Type.Array) return;

        foreach (var item in tv.AsGodotArray())
        {
            if (item.VariantType != Variant.Type.Dictionary) continue;
            var d = item.AsGodotDictionary<string, Variant>();
            ToreInDatei++;
            // ⚠ Ein Tor ohne Feld oder ohne Werte ist NICHT auswertbar. Es wird
            // uebergangen und gezaehlt, nicht geraten.
            if (!d.TryGetValue("feld", out var fv) || fv.VariantType == Variant.Type.Nil)
            { OhneBedingung++; continue; }
            if (!d.TryGetValue("werte", out var wv) || wv.VariantType != Variant.Type.Array)
            { OhneBedingung++; continue; }
            var werte = new List<int>();
            foreach (var w in wv.AsGodotArray()) werte.Add(w.AsInt32());
            if (werte.Count == 0) { OhneBedingung++; continue; }

            _tore.Add(new Tor
            {
                Var = d.TryGetValue("var", out var vv) ? vv.AsInt32() : -1,
                Feld = fv.AsInt32(),
                Werte = werte.ToArray(),
                Text = d.TryGetValue("text", out var txv) ? txv.AsInt32() : -1,
            });
        }
        GD.Print($"kontexthilfe: {_tore.Count} von {ToreInDatei} Toren auswertbar " +
                 $"({OhneBedingung} ohne Bedingung, siehe OFFENE_FRAGEN.md Abschnitt AE)");
    }

    /// <summary>
    /// <b>Welcher Hilfetext gehört zu dieser Einheit</b> — oder −1.
    ///
    /// <para>Die vier Satzfelder des Originals, und wie sie bei uns heissen:
    /// <c>+0x0D ZBRAN</c> ist die Waffe (⚠ unser <c>Weapon</c> hält den
    /// AUFSATZ <c>+0x0C</c>, also <c>Weapon − 20</c>), <c>+0x0E top_spec</c>
    /// ist <c>Part</c>, <c>+0x0F l_engine</c> ist <c>UnitType</c>,
    /// <c>+0x10</c> ist <c>Equipment</c>.</para>
    ///
    /// <para>⚠ Das erste passende Tor gewinnt, und zwar in der Reihenfolge der
    /// Datei — das ist die Reihenfolge des Originalblocks. Dort steht ein Tor
    /// hinter dem anderen, und das erste, dessen Bedingung greift, zeigt seinen
    /// Text; die späteren kommen im selben Aufruf nicht mehr dran, weil das
    /// Fenster das Spiel anhält.</para></summary>
    public static Tor? Passend(int zbran, int part, int engine, int equip)
    {
        Laden();
        foreach (var t in _tore)
        {
            if (_gezeigt.Contains(t.Var)) continue;
            int wert = t.Feld switch
            {
                0x0D => zbran,
                0x0E => part,
                0x0F => engine,
                0x10 => equip,
                _ => int.MinValue,
            };
            if (wert == int.MinValue) continue;
            foreach (int w in t.Werte)
                if (w == wert) return t;
        }
        return null;
    }

    /// <summary>Ein Tor als gefeuert vermerken.</summary>
    public static void Gefeuert(Tor t) => _gezeigt.Add(t.Var);
}
