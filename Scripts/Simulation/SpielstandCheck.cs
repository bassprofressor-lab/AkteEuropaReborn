namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Text;
using Godot;

/// <summary>
/// <c>--spielstand-check</c> (bug-506..508, KayelGee R5#16/#18): Speichern und
/// Laden auf dem ECHTEN Weg — nicht wie <c>--save-check</c> im selben Bild,
/// sondern ueber den Knopf (<see cref="MapViewer.SpielstandSchreiben"/>), einen
/// Szenenwechsel und den ganzen Startzweig in <c>_Ready</c> (Mitnahme,
/// Startgeld, Nachziehen), so wie ein Spieler laedt.
///
/// <para>⚠ Warum ein neuer Pruefstand: <c>--save-check</c> meldete DECKUNGSGLEICH,
/// waehrend alle vier gemeldeten Fehler drinsteckten — er ging den Ladeweg nicht
/// und zaehlte weder Sprit, Munition, Bildart, Fussabdruck, Flugzeuge noch
/// Garage. Die Lehre aus Runde 4: am gemeldeten Bildelement messen.</para>
///
/// <para>Ablauf: nach 8 s werden Dinge hingelegt, die verloren gehen KOENNEN
/// (zwei gekaufte Helis, halber Sprit und halbe Munition bei drei Einheiten, eine
/// Einheit in einer Garage), dann gespeichert, Fingerabdruck gemerkt, geladen.
/// Nach dem Laden wird Teil fuer Teil verglichen; der Skriptzustand nach dem
/// ersten Takt Zeichen fuer Zeichen. Nullmodell:
/// <c>--laden-vorlage-alt --laden-mitnahme-alt</c>. Gedacht fuer
/// <c>--campaign=2 --no-briefing</c>.</para>
/// </summary>
public partial class MapViewer
{
    public static bool SpielstandCheckAn;

    /// <summary>0 = laeuft noch nicht, 1 = gespeichert, die neue Szene laedt,
    /// 2 = verglichen, wartet auf den Skriptzustand, 3 = fertig.</summary>
    private static int _ssPhase;
    private static string _ssVorher = "", _ssSkriptVorher = "", _ssGelegt = "";
    private float _ssUhr;

    private void SpielstandCheckTakt(double delta)
    {
        if (!SpielstandCheckAn || _entities == null) return;
        _ssUhr += (float)delta;
        if (_ssPhase == 0 && _ssUhr >= 8f)
        {
            _ssGelegt = _entities.SpielstandProbeVorbereiten();
            _ssVorher = _entities.SpielstandFingerabdruck();
            _ssSkriptVorher = _entities.SkriptZustandJetzt();
            string name = SpielstandSchreiben();
            if (name.Length == 0) { GD.Print("spielstand-check: Speichern fehlgeschlagen"); _ssPhase = 3; return; }
            _ssPhase = 1;
            UI.SkirmishSetup.PendingSave = name;
            Core.LeaveToMenu.Tidy(this);
            GetTree().ChangeSceneToFile(UI.SkirmishSetup.GameScene);
            return;
        }
        if (_ssPhase == 2 && MapEntityLayer.SkriptZustandGeladen.Length > 0)
        {
            _ssPhase = 3;
            bool gleich = MapEntityLayer.SkriptZustandGeladen == _ssSkriptVorher;
            GD.Print($"spielstand-check skript: {(gleich ? "GLEICH" : "WEICHT AB")} " +
                     $"({_ssSkriptVorher.Length} Zeichen vorher, {MapEntityLayer.SkriptZustandGeladen.Length} nachher)");
            if (!gleich)
            {
                GD.Print($"   vorher : {Kurz(_ssSkriptVorher)}");
                GD.Print($"   nachher: {Kurz(MapEntityLayer.SkriptZustandGeladen)}");
            }
            GD.Print("spielstand-check: fertig");
            GetTree().Quit();
        }
        if (_ssPhase == 2 && _ssUhr > 5f && MapEntityLayer.SkriptZustandGeladen.Length == 0)
        {
            _ssPhase = 3;
            GD.Print("spielstand-check skript: KEIN Skriptzustand nach dem Laden aufgesetzt" +
                     (_ssSkriptVorher.Length == 0 ? " (vorher auch keiner — Karte ohne Skript)" : "  ⚠ VERLOREN"));
            GetTree().Quit();
        }
    }

    private static string Kurz(string s) => s.Length > 400 ? s[..400] + "…" : s;

    /// <summary>Aus <c>_Ready</c>, nach dem ganzen Startzweig.</summary>
    private void SpielstandCheckNachLaden()
    {
        if (!SpielstandCheckAn || _ssPhase != 1 || _entities == null) return;
        _ssPhase = 2;
        _ssUhr = 0f;
        string nachher = _entities.SpielstandFingerabdruck();
        var sb = new StringBuilder($"spielstand-check: {_ssGelegt}\n   Baum angehalten nach dem Laden: {GetTree().Paused}, " +
                                   $"Vorlagen {MapEntityLayer.LadenVorlagen}" +
                                   (MapEntityLayer.LadenVorlageAlt ? "  ⚠ --laden-vorlage-alt" : "") +
                                   (LadenMitnahmeAlt ? "  ⚠ --laden-mitnahme-alt" : ""));
        string[] a = _ssVorher.Split(" | "), b = nachher.Split(" | ");
        int ab = 0;
        for (int i = 0; i < a.Length; i++)
        {
            string n = i < b.Length ? b[i] : "—";
            bool g = a[i] == n;
            if (!g) ab++;
            sb.Append($"\n   {(g ? "  gleich " : "⚠ ANDERS ")} {a[i]}{(g ? "" : $"   ->   {n}")}");
        }
        sb.Append($"\n   {(ab == 0 ? "DECKUNGSGLEICH" : $"WEICHT AB in {ab} von {a.Length} Teilen")}");
        GD.Print(sb.ToString());
    }
}

public partial class MapEntityLayer
{
    public string SkriptZustandJetzt() => _mscript?.ZustandJson() ?? "";

    /// <summary>Legt hin, was beim Laden verloren gehen KANN.</summary>
    public string SpielstandProbeVorbereiten()
    {
        var sb = new StringBuilder();
        sb.Append(NachschubSichtProbe().Replace("nachschub-sicht-check: ", "Helis: "));
        int me = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;
        int n = 0;
        Entity? garagengast = null; int gastIdx = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsProp || e.IsBuilding || e.Dead || e.Owner != me) continue;
            if (n < 3 && e.FuelMax > 0)
            {
                e.Fuel = e.FuelMax / 2; e.Ammo = e.AmmoMax / 2; n++;
            }
            else if (garagengast == null && e.Infantry < 0) { garagengast = e; gastIdx = i; }
        }
        sb.Append($"; {n} Einheiten auf halben Sprit/Munition");
        // ⚠ das Geld VERAENDERN: stand der Stand zufaellig auf dem Startgeld der
        // Mission, saehe ein ueberschriebener Kontostand wie ein richtiger aus
        _money[me] += 77;
        sb.Append($"; Kontostand +77 = {_money[me]}");
        // eine Garage: das naechste Gebaeude mit Tuer bekommt den Spieler als
        // Besitzer und nimmt eine Einheit auf — Einfahren, der Weg des Spiels
        if (garagengast != null)
            foreach (var b in _entities)
            {
                if (!b.IsBuilding || b.Dead || b.DoorCells.Count == 0 || b.BType is not (1 or 5 or 6 or 7)) continue;
                b.Owner = b.Team = b.ShownOwner = me;
                Einfahren(b, gastIdx, garagengast);
                sb.Append($"; Einheit {garagengast.Slot} in Gebaeude {b.Slot} (Typ {b.BType}) eingefahren, Garage {b.Garage.Count}");
                break;
            }
        return sb.ToString();
    }

    /// <summary>Teile, durch » | « getrennt — der Vergleich nennt jeden einzeln.</summary>
    public string SpielstandFingerabdruck()
    {
        var je = new SortedDictionary<int, int>();
        long fuel = 0, ammo = 0, hp = 0, lager = 0;
        long bild = 0, flaeche = 0, tueren = 0, marke = 0, ziele = 0, kein = 0, ruinen = 0;
        int garage = 0, ukol = 0, depot = 0, gebaeude = 0;
        foreach (var e in _entities)
        {
            if (e.IsProp) continue;
            if (e.IsBuilding)
            {
                gebaeude++;
                bild += e.BildArt; flaeche += e.FootW * e.FootH; tueren += e.DoorCells.Count;
                marke += e.Mark + 1; if (e.IsTarget) ziele++; if (e.NoStructure) kein++;
                if (e.BType == 0 && e.BildArt > 0) ruinen++;
                garage += e.Garage.Count; depot += e.Depot.Count;
                lager += e.StockW + e.StockF + e.StockS + e.StockT;
                hp += e.Hp;
                continue;
            }
            if (e.Dead) continue;
            je[e.Owner] = je.GetValueOrDefault(e.Owner) + 1;
            fuel += e.Fuel; ammo += e.Ammo; hp += e.Hp; ukol += e.Ukol;
        }
        int luft = 0; long lfuel = 0, lammo = 0, lart = 0, lalt = 0;
        foreach (var a in _special)
        {
            if (a.Dead) continue;
            luft++; lfuel += a.Fuel; lammo += a.Ammo; lart += a.Kind; lalt += a.Alt;
        }
        long geld = 0;
        foreach (int m in _money) geld += m;
        var besitz = new StringBuilder();
        foreach (var kv in je) besitz.Append($"{kv.Key}:{kv.Value} ");
        return $"Einheiten je Besitzer {besitz.ToString().Trim()}" +
               $" | Sprit {fuel}" +
               $" | Munition {ammo}" +
               $" | HP {hp}" +
               $" | Geld {geld}" +
               $" | {gebaeude} Gebaeude, Bildart {bild}" +
               $" | Flaeche {flaeche}" +
               $" | Tueren {tueren}" +
               $" | Marken {marke}, Siegziele {ziele}, ohne Bauwerk {kein}, Ruinen {ruinen}" +
               $" | Lager {lager}" +
               $" | Garage {garage}, Ukol {ukol}, Depot {depot}" +
               $" | Flugzeuge {luft}, Sprit {lfuel}, Munition {lammo}, Arten {lart}, Hoehe {lalt}";
    }
}
