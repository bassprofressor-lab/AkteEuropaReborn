namespace AkteEuropaReborn.Rendering;

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Godot;

/// <summary>
/// ⭐ 30.10.2026, bug-684 — <b><see cref="Entity.Klon"/> als TIEFE Kopie</b>
/// (berichte/pruefung-rest-a-opus.md §1, Bau: berichte/bau-spielstand-ki-klon.md).
///
/// <para><b>Befund:</b> <c>Klon()</c> war <c>MemberwiseClone</c>. Die Kopie teilte damit jedes
/// Referenzfeld mit der Vorlage — <c>Orders</c>, <c>BuildQueue</c>, <c>Depot</c>,
/// <c>Garage</c>, <c>TorZustand</c>, <c>DoorCells</c>, <c>Hangar</c>, <c>Path</c>,
/// <c>SpritWeg</c>, <c>Route</c> (dazu dessen Felder <c>Quelle</c>/<c>Geladen</c>) und
/// <c>InGebaeude</c>. Am schärfsten beim Netz-Neustart (Neustart.cs:
/// <c>NetzStartstandMerken</c> legt <c>V = L.Klon()</c> ab): was die lebende Einheit L danach
/// tat, stand auch in der Vorlage V. Im Regelweg leert der Lader (SaveState.cs
/// <c>BaueEinheit</c>) jedes geteilte Feld — eine latente Falle für jedes neue Feld.</para>
///
/// <para><b>Bei uns jetzt:</b> nach <c>MemberwiseClone</c> geht dieser Nachzieher ÜBER ALLE
/// Referenzfelder (Reflexion, einmal gesammelt) — damit fängt er auch ein Feld, das jemand
/// künftig anlegt. Listen/Mengen/Tafeln → neue Sammlung mit demselben Inhalt, Felder
/// (<c>int[]</c>) → <c>Clone()</c>, der Routensatz → eigene Kopie samt Feldern.</para>
///
/// <para>⚠ <b>UNSERE SETZUNG:</b> Verweise auf ANDERE Einheiten (<c>InGebaeude</c>) setzt die
/// Kopie auf <c>null</c> — eine Vorlage zeigt nicht auf ein lebendes Gebäude, der Lader
/// verknüpft neu (SaveState.cs, »Garage und in welchem Gebäude«). Die <c>Garage</c> ist
/// eine eigene Liste, ihre MITGLIEDER bleiben dieselben Einheiten (sie sind Verweise, kein
/// Eigentum der Einheit; der Lader leert die Liste ohnehin). Zeichenketten sind
/// unveränderlich und bleiben geteilt. Ein Referenzfeld eines unbekannten Typs bleibt
/// geteilt und meldet sich im Prüfstand mit Namen.</para>
///
/// <para>Gegenschalter <c>--klon-flach</c> (die flache Kopie bis 30.10.2026). Prüfstand
/// <c>--klon-check</c>, siehe unten.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--klon-flach</c> — der Stand bis bug-684: <c>Klon()</c> = <c>MemberwiseClone</c>.</summary>
    public static bool KlonFlach;

    private static FieldInfo[]? _klonFelder;
    private static readonly MethodInfo KlonRoh =
        typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>Alle Referenzfelder eines Typs ausser Zeichenketten (auch readonly, auch privat).</summary>
    private static FieldInfo[] KlonFelderVon(System.Type t)
    {
        var l = new List<FieldInfo>();
        foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var ft = f.FieldType;
            if (ft.IsValueType || ft == typeof(string)) continue;
            l.Add(f);
        }
        return l.ToArray();
    }

    private static FieldInfo[] EntityKlonFelder => _klonFelder ??= KlonFelderVon(typeof(Entity));

    /// <summary>Die tiefe Kopie eines Feldwerts — oder derselbe Wert, wo es keine gibt.</summary>
    private static object? KlonWert(object? v)
    {
        switch (v)
        {
            case null: return null;
            case string: return v;
            case Entity: return null;                                   // UNSERE SETZUNG, siehe Kopf
            case Transportroute r:
            {
                var k = (Transportroute)KlonRoh.Invoke(r, null)!;
                foreach (var f in KlonFelderVon(typeof(Transportroute)))
                    f.SetValue(k, KlonWert(f.GetValue(r)));
                return k;
            }
            case System.Array a: return a.Clone();                      // nur Zahlen/Strukturen darin
            case GodotObject: return v;                                 // Godot-Ressourcen: geteilt, gemeldet
        }
        var t = v.GetType();
        if (t.IsGenericType && v is IEnumerable)
        {
            var g = t.GetGenericTypeDefinition();
            if (g == typeof(List<>) || g == typeof(HashSet<>) || g == typeof(Dictionary<,>)
                || g == typeof(Queue<>) || g == typeof(SortedSet<>))
                return System.Activator.CreateInstance(t, v);           // Kopierkonstruktor (IEnumerable/IDictionary)
        }
        return v;                                                       // unbekannt: bleibt geteilt, --klon-check nennt es
    }

    /// <summary>Aus <see cref="Entity.Klon"/>: die frische flache Kopie <paramref name="k"/> tief machen.</summary>
    private static void KlonTiefNachziehen(Entity k)
    {
        foreach (var f in EntityKlonFelder)
        {
            var alt = f.GetValue(k);
            if (alt == null) continue;
            var neu = KlonWert(alt);
            if (!ReferenceEquals(neu, alt)) f.SetValue(k, neu);
        }
    }

    /// <summary>Wie viele veränderliche Objekte teilen sich <paramref name="a"/> und <paramref name="b"/>?
    /// Zählt jedes Referenzfeld (ausser Zeichenketten) mit demselben Objekt, beim Routensatz auch
    /// dessen Felder. Die Namen landen in <paramref name="je"/>.</summary>
    private static int KlonGeteilt(object a, object b, Dictionary<string, int> je, string vor = "")
    {
        int n = 0;
        foreach (var f in KlonFelderVon(a.GetType()))
        {
            var va = f.GetValue(a);
            if (va == null) continue;
            var vb = f.GetValue(b);
            if (ReferenceEquals(va, vb))
            {
                n++;
                je[vor + f.Name] = je.GetValueOrDefault(vor + f.Name) + 1;
            }
            else if (va is Transportroute && vb is Transportroute)
                n += KlonGeteilt(va, vb, je, f.Name + ".");
        }
        return n;
    }

    // ---- --klon-check --------------------------------------------------------------

    /// <summary>
    /// <c>--klon-check</c> (kopflos; gedacht für <c>--campaign=27</c> mit Transportern, geht
    /// überall). Drei Aussagen:
    /// <list type="number">
    /// <item><b>Teilung:</b> je lebende Einheit L eine Kopie V = L.Klon(), gezählt werden die
    /// geteilten Referenzen. Soll 0; Nullmodell <c>--klon-flach</c> &gt; 0.</item>
    /// <item><b>Unabhängigkeit:</b> die Kopien werden verändert (Bauliste, Depot, Tor, Tür,
    /// Hangar, Weg, Routensatz <c>Warten = 77</c>, <c>Quelle[0]</c>), L muss unverändert bleiben.
    /// Soll 0 veränderte; Nullmodell &gt; 0. Danach werden die Änderungen zurückgenommen.</item>
    /// <item><b>Zwei Neustarts</b> wie der Netz-Behandler (Neustart.cs): Startstand merken,
    /// 300 Takte, einspielen, 300 Takte, einspielen. <c>SpielstandFingerabdruck</c> nach beiden
    /// gleich dem beim Merken. Nur Aussage, nicht Teil des Urteils (siehe Bericht).</item>
    /// </list>
    /// </summary>
    public string KlonCheck()
    {
        var sb = new StringBuilder("klon-check" + (KlonFlach ? " — NULLMODELL --klon-flach" : "") +
                                   (LadenBefehleAlt ? " --laden-befehle-alt" : ""));
        // 1. Teilung
        var je = new Dictionary<string, int>();
        var paare = new List<(Entity L, Entity V)>();
        int geteilt = 0, einheiten = 0, routen = 0;
        foreach (var e in _entities)
        {
            if (e.IsProp) continue;
            einheiten++;
            if (e.Route != null) routen++;
            var v = e.Klon();
            paare.Add((e, v));
            geteilt += KlonGeteilt(e, v, je);
        }
        sb.Append($"\n   1 Teilung: {einheiten} Einheiten ({routen} mit Routensatz), geteilte Referenzen {geteilt}" +
                  (je.Count > 0 ? " — " + string.Join(", ", JeText(je)) : ""));
        // 2. Unabhängigkeit: die KOPIE verändern, das Original muss bleiben
        int veraendert = 0, geprueft = 0;
        foreach (var (l, v) in paare)
        {
            string vorher = KlonZustand(l);
            v.BuildQueue.Add(-4711); v.Depot.Add(-4711); v.TorZustand.Add(-4711);
            v.DoorCells.Add((-4711, -4711));
            v.Hangar?.Add(-4711);
            v.Path?.Add(new Vector2I(-4711, -4711));
            int wartenAlt = 0, quelleAlt = 0;
            if (v.Route is Transportroute r) { wartenAlt = r.Warten; quelleAlt = r.Quelle[0]; r.Warten = 77; r.Quelle[0] = -4711; }
            geprueft++;
            if (KlonZustand(l) != vorher) veraendert++;
            // zuruecknehmen — unter --klon-flach hängt es sonst an der lebenden Einheit
            v.BuildQueue.RemoveAt(v.BuildQueue.Count - 1); v.Depot.RemoveAt(v.Depot.Count - 1);
            v.TorZustand.RemoveAt(v.TorZustand.Count - 1); v.DoorCells.RemoveAt(v.DoorCells.Count - 1);
            v.Hangar?.RemoveAt(v.Hangar.Count - 1);
            v.Path?.RemoveAt(v.Path.Count - 1);
            if (v.Route is Transportroute r2) { r2.Warten = wartenAlt; r2.Quelle[0] = quelleAlt; }
        }
        sb.Append($"\n   2 Unabhaengigkeit: Kopie veraendert, Original mitveraendert bei {veraendert} von {geprueft}");
        // 3. Zwei Neustarts (Netz-Behandler nachgebildet, im Einzelspiel)
        NetzStartstandMerken();
        string anfang = SpielstandFingerabdruck();
        var urteile = new List<string>();
        for (int n = 1; n <= 2; n++)
        {
            for (int t = 0; t < 300; t++) SimTickFuerProbe();
            var json = new Json();
            if (_netzStartJson == null || json.Parse(_netzStartJson) != Error.Ok) { urteile.Add($"#{n} kein Startstand"); break; }
            _entities.RemoveAll(e => !e.IsProp);
            foreach (var v in _netzStartVorlagen!) _entities.Add(v.Klon());
            NetzStandEinspielen(json.Data.AsGodotDictionary<string, Variant>());
            string nach = SpielstandFingerabdruck();
            string[] a = anfang.Split(" | "), b = nach.Split(" | ");
            var anders = new List<string>();
            for (int i = 0; i < a.Length; i++) if (i >= b.Length || a[i] != b[i]) anders.Add(i < b.Length ? $"{a[i]} -> {b[i]}" : a[i]);
            urteile.Add(anders.Count == 0 ? $"#{n} GLEICH" : $"#{n} ANDERS in {anders.Count}: {string.Join("; ", anders)}");
        }
        sb.Append($"\n   3 Zwei Neustarts (je 300 Takte), Fingerabdruck gegen den Anfang: {string.Join(" | ", urteile)}");
        bool gut = geteilt == 0 && veraendert == 0 && geprueft > 0;
        sb.Append($"\n   -> {(gut ? "BESTANDEN" : "DURCHGEFALLEN")}");
        return sb.ToString();
    }

    private static IEnumerable<string> JeText(Dictionary<string, int> je)
    {
        var l = new List<string>();
        foreach (var kv in je) l.Add($"{kv.Key} {kv.Value}");
        l.Sort(System.StringComparer.Ordinal);
        return l;
    }

    /// <summary>Was Schritt 2 verändert — als Text, damit »gleich« ein Zeichenvergleich ist.</summary>
    private static string KlonZustand(Entity e)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(",", e.BuildQueue)).Append('/').Append(string.Join(",", e.Depot)).Append('/')
          .Append(string.Join(",", e.TorZustand)).Append('/').Append(e.DoorCells.Count).Append('/')
          .Append(e.Hangar?.Count ?? -1).Append('/').Append(e.Path?.Count ?? -1);
        if (e.Route is Transportroute r) sb.Append('/').Append(r.Warten).Append(',').Append(r.Quelle[0]);
        return sb.ToString();
    }
}
