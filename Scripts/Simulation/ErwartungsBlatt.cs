namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// ⭐⭐⭐ <c>--erwartung</c> — <b>WAS ICH ERWARTE, BEVOR ER HINSIEHT</b>
/// (24.08.2026).
///
/// <para>Auf seinen Vorschlag: »Kampagnenmission pö à pö durchgehen, du
/// schreibst mir was du erwartest, ich schaue ob es auslöst, und vergleiche mit
/// dem Let's Play, ob es identisch ist oder abweicht.«</para>
///
/// <para><b>Warum das mehr ist als eine nette Idee.</b> Jeder unserer 104
/// Prüfstände misst, was UNSER Programm tut — und kann darum nie zeigen, dass
/// unser Programm etwas anderes tut als das Original. Genau daran ist am
/// 24.08. eine erfundene Mechanik durchgerutscht, die alle sechs Messungen
/// bestanden hat. <b>Ein Prüfstand kann eine Erfindung nicht widerlegen; nur
/// das Original kann das.</b> Dieses Blatt ist die Brücke: es schreibt unsere
/// Lesung als <b>vorher festgelegte, nachprüfbare Behauptungen</b> auf, gegen
/// die sein Let's Play entscheiden kann.</para>
///
/// <para>⚠ <b>Zwei Regeln, ohne die es wertlos wäre:</b></para>
/// <list type="number">
/// <item><b>Vorher.</b> Das Blatt wird gedruckt, BEVOR gespielt wird. Eine
/// Erwartung, die man nach dem Hinsehen formuliert, ist keine.</item>
/// <item><b>Jede Zeile muss falsch sein KÖNNEN.</b> »Die Mission startet« ist
/// keine Erwartung. »Spieler 0 beginnt mit 4 Gaswerfern bei (35,27), (33,28),
/// (49,51)« ist eine — sie steht in den Daten und ein Video kann sie
/// umstossen.</item>
/// </list>
///
/// <para>⚠ Alles hier kommt aus <b>eingelesenen Daten</b> (entities.json,
/// campaign.json, dem Missionsskript) — nichts ist getippt. Wo unsere Lesung
/// eine Lücke hat, steht das als Zeile drin, statt weggelassen zu werden: eine
/// Erwartungsliste, die ihre Löcher verschweigt, prüft nur, was ohnehin
/// stimmt.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Die Waffennamen aus der Bauteiltafel — damit im Blatt nicht
    /// »ZBRAN 15« steht, sondern »Minenleger«.</summary>
    private string WaffenName(int zbran)
        => zbran == 0 ? "unbewaffnet" : WeaponOf(zbran).Name is { Length: > 0 } n ? n : $"ZBRAN {zbran}";

    /// <summary>Ist alles geladen, was das Blatt braucht? Das Missionsskript
    /// kommt erst nach dem Aufbau — ein Blatt ohne Skript behauptet weniger,
    /// als es koennte.</summary>
    public bool ErwartungBereit() => _mscript != null;

    /// <summary><c>--hilfe-check</c>: je Fensterregel des Missionsskripts,
    /// WARUM sie nicht feuern kann.</summary>
    public string HilfeCheckLine()
        => _mscript?.TutorialCheck() ?? "hilfe-check: kein Missionsskript";

    public string ErwartungsBlatt()
    {
        var sb = new System.Text.StringBuilder();
        int m = _mscript?.Mission ?? -1;
        sb.Append("═══════════════════════════════════════════════════════════════\n");
        sb.Append($"  ERWARTUNGSBLATT — Kampagnenmission {m}\n");
        sb.Append("  Jede Zeile ist eine Behauptung, die dein Let's Play umstossen kann.\n");
        sb.Append("  Bitte je Zeile: STIMMT / WEICHT AB (und wie) / NICHT ZU SEHEN\n");
        sb.Append("═══════════════════════════════════════════════════════════════\n\n");

        int nr = 0;
        string Z(string s) => $"  E{++nr,-3} {s}\n";

        // ---- 1. Die Startaufstellung -------------------------------------
        // ⚠ Aus den EINGELESENEN Kartendaten, nicht aus dem Gedaechtnis.
        sb.Append("── Startaufstellung ───────────────────────────────────────────\n");
        var jeSpieler = new SortedDictionary<int, List<Entity>>();
        foreach (var e in _entities)
        {
            if (e.IsProp || e.Dead) continue;
            if (e.Owner is < 0 or > 7) continue;
            if (!jeSpieler.TryGetValue(e.Owner, out var l)) jeSpieler[e.Owner] = l = new List<Entity>();
            l.Add(e);
        }
        foreach (var kv in jeSpieler)
        {
            var geb = kv.Value.Count(x => x.IsBuilding);
            var einh = kv.Value.Count(x => !x.IsBuilding);
            sb.Append(Z($"Spieler {kv.Key}{(kv.Key == ViewPlayer ? " (DU)" : "")}: "
                      + $"{einh} Einheiten, {geb} Gebaeude"));
        }

        // ⭐ Die BESONDEREN Einheiten einzeln — das sind die, an denen sich eine
        // Abweichung zeigt. Eine Zahl »37 Einheiten« kann niemand am Video
        // pruefen; »4 Gaswerfer bei (35,27)« schon.
        var meine = jeSpieler.TryGetValue(ViewPlayer, out var mm) ? mm : new List<Entity>();
        var nachWaffe = meine.Where(x => !x.IsBuilding && x.Weapon > 0)
                             .GroupBy(x => x.Weapon)
                             .OrderBy(g => g.Key);
        foreach (var g in nachWaffe)
        {
            var orte = string.Join(", ", g.Take(4).Select(x => $"({x.Col},{x.Row})"));
            sb.Append(Z($"du hast {g.Count()}x {WaffenName(g.Key)} bei {orte}"
                      + (g.Count() > 4 ? " …" : "")));
        }

        // ⭐ Die HOEHE unter der eigenen Einheit. Sie steht hier, weil Mission 1
        // ihr einziges Gelaendefenster daran haengt (#20, »auf einem Huegel«,
        // Bedingung > 4) — und weil eine Zahl, die man am Bildschirm nachfahren
        // kann, die beste Gegenprobe fuer den Haken ist.
        var ersteEigene = meine.FirstOrDefault(x => !x.IsBuilding && !x.Dead);
        if (ersteEigene != null)
            sb.Append(Z($"Hoehe unter deiner Einheit auf ({ersteEigene.Col},{ersteEigene.Row}): "
                      + $"{ElevOf(ersteEigene.Col, ersteEigene.Row)} "
                      + "— das Bergfenster #20 kommt ab 5"));

        // ---- 2. Was das Skript von selbst tut ----------------------------
        sb.Append("\n── Missionsskript ─────────────────────────────────────────────\n");
        if (_mscript == null) sb.Append(Z("KEIN Missionsskript geladen — das waere selbst ein Befund"));
        else
        {
            sb.Append(Z($"das Skript hat {_mscript.RuleCountOrZero()} Regeln"));
            // ⚠ Diese Zeile sagte »in den ersten 60 s feuert das Skript N
            // Regeln« und druckte dabei den Stand nach VIER Takten. Eine
            // Erwartung darf nur behaupten, was sie gemessen hat.
            sb.Append(Z($"bis hierher ({_mscript.Ticks} Takte) hat das Skript "
                      + $"{_mscript.RulesFired} Regel(n) von selbst gefeuert — "
                      + "was in der ersten Minute kommt, sagt --tick-check=60"));
        }

        // ---- 3. Die Ziele ------------------------------------------------
        sb.Append("\n── Ziele (so lesen wir sie) ───────────────────────────────────\n");
        if (_mscript != null)
            foreach (var zeile in _mscript.GoalCheck().Split('\n'))
                if (zeile.Trim().Length > 0) sb.Append(Z(zeile.Trim()));

        // ---- 4. Die bekannten LÜCKEN -------------------------------------
        //
        // ⚠⚠ Diese Liste ist der wichtigste Teil des Blattes. Sie sagt ihm, wo
        // er BESONDERS hinsehen soll — dort, wo unsere Lesung schwach ist.
        // Eine Erwartungsliste, die nur das Sichere aufzaehlt, bestaetigt sich
        // selbst.
        sb.Append("\n── ⚠ Wo unsere Lesung schwach ist — hier BESONDERS hinsehen ──\n");
        // ⚠⚠ 24.08.2026 — DIE LUECKENLISTE HAENGT AM NAMEN, NICHT AN EINER
        // NUMMER. Erst stand hier die Tafelzeile (15/16/9), dann die
        // Bauteilnummer (35/36/29) — und beide Male stimmte die Liste NICHT mit
        // den Zeilen darueber ueberein, die denselben Wert ueber
        // `WaffenName` aufloesen. Zwei Zahlenkreise fuer dasselbe Ding sind eine
        // Dauerfalle; der NAME ist die eine Groesse, die beide aufloesen.
        // ⭐ Aufgefallen ist es nur, weil dasselbe Blatt sich selbst widersprach.
        var luecken = new List<string>();
        var offene = new (string Name, string Text)[]
        {
            ("Minenleger",  "MINENLEGER — wir wissen NICHT, wie man ihn ausloest"),
            ("Fallenleger", "FALLENLEGER — derselbe Code, dieselben Felder wie die Mine: "
                          + "wie du IHN ausloest, ist die Antwort fuer beide"),
            ("Gaswerfer",   "GASWERFER — Gaswolken sind bei uns gar nicht gebaut"),
        };
        foreach (var (name, text) in offene)
        {
            var wo = meine.Where(x => !x.IsBuilding && !x.Dead && x.Weapon > 0
                                   && WaffenName(x.Weapon) == name).ToList();
            if (wo.Count == 0) continue;
            luecken.Add($"{wo.Count}x {text}  —  "
                      + string.Join(", ", wo.Select(x => $"({x.Col},{x.Row})")));
        }

        if (luecken.Count == 0) sb.Append(Z("in dieser Mission keine der offenen Einheiten"));
        else foreach (var l in luecken.Distinct()) sb.Append(Z(l));

        sb.Append("\n═══════════════════════════════════════════════════════════════\n");
        return sb.ToString();
    }
}
