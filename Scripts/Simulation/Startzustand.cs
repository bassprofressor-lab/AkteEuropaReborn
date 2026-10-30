namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// ⭐⭐ 29.10.2026, bug-664 (Reaper R5: »Flak-Geschuetz feuert nicht«, Bild
/// <c>Bug Bilder/29102026flak.png</c>, <c>berichte/flak-einnahme-r8-opus.md</c> §1.2).
///
/// <para>Jeder von der KI gestartete Flieger bekam <c>Order = 4</c> (Start,
/// <c>SkirmishAi.AiAirTakeoff</c>) und behielt es <b>fuer immer</b> — kein Code
/// schrieb es zurueck. Der Flak-Kegel ueberspringt uk 2/4 (<c>Flak.cs</c>, C
/// <c>0x42840B..0x428419</c>, richtig gelesen): KI-Luftangriffe waren fuer unsere Flak
/// unsichtbar (gemessen K17: 4 Kampfhubschrauber in der Luft, Kegelproben 0).</para>
/// <para>Im Original ist uk 4 ein <b>Vorgang mit Uhr</b> (AIR_RE »uk 4 @0x423411«),
/// selbst nachgelesen (C):</para>
/// <code>
///   0x423489  cmp cl, 0x19 ; jbe …        ; Flugzeug: Uhr &gt; 25 -> fertig
///   0x42348E  mov al, [esi+0x6DDF81]      ; m_uk
///   0x423494  cmp al, 6 ; jne 0x4234EE
///   0x423498  mov byte [esi+0x6DDF80], 3  ; m_uk 6 -> uk 3
///   0x4234EE  mov byte [esi+0x6DDF80], al ; sonst uk := m_uk
///   0x4235C4..0x4235D8  Heli: Uhr &gt; 42 -> uk := m_uk      (F Heli-Zweig 0x422798)
/// </code>
/// <para>⚠ Die 6→3-Wandlung steht nur im FLUGZEUGzweig; der Heli-Zweig schreibt
/// m_uk unveraendert (so gelesen, hier so gebaut). ⚠ UNSERE SETZUNG: die Uhr
/// (<c>byte[0x6DDF82]</c>, Satz +0x12) zaehlt einen je UpdateAircraft-Lauf (= ein
/// Originaltakt, vgl. die Hoehe +2 je Takt) und beginnt bei 0 — auch fuer
/// Kartenflieger mit uk 4 (DM_1, Slots 12..14): ob das Original ihre Uhr aus dem
/// Kartensatz mitlaedt, ist ungelesen. Wo genau im Takt die Uhr erhoeht wird
/// (vor/nach dem Vergleich), ist nicht Zeile fuer Zeile gelesen: hier wird erst
/// erhoeht, dann verglichen (Wechsel bei Uhr 26 bzw. 43).
/// Gegenschalter <c>--startzustand-alt</c> (Order bleibt 4, Stand bis 0.6.11).</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--startzustand-alt</c> — bug-664: der Start endet nie (Stand 0.6.11).</summary>
    public static bool StartzustandAlt;

    /// <summary>Uhr &gt; 25 (Flugzeug, <c>cmp cl,0x19</c>) bzw. &gt; 42 (Heli).</summary>
    private const int StartUhrFlugzeug = 25, StartUhrHeli = 42;

    /// <summary>Pruefstand: wie viele Starts geendet haben.</summary>
    public int StartsBeendet;

    private void StartuhrTakt(Special a)
    {
        if (StartzustandAlt || a.Order != 4) return;
        bool heli = a.Kind >= 10;
        // ⭐ 30.10.2026, bug-680 (Paket R10): die Startklaenge, Modus 1 am Flieger.
        // 303 — FLUGZEUG (Typ < 10) bekommt die Bahn: Uhr +0x12 == 0 (C 0x423411..
        // 0x423467). 304 — HELI bei Uhr == 39 (C 0x42361F..0x42363C). ⚠ UNSERE
        // SETZUNG: die Bahnfreigabe (Tafel 0x879441) ist nicht gebaut, 303 kommt im
        // ersten Starttakt; und nur ein Start mit uk 4 klingt — der Spielerstart
        // (LaunchAircraft, Hangar) geht bei uns nicht durch uk 4 und bleibt stumm.
        // Gegenschalter --flugklang-aus.
        if (!heli && a.StartUhr == 0 && !Audio.GameSounds.FlugklangAus)
            Audio.GameSounds.PlayAt(Audio.GameSounds.FlugzeugStartbahn, a.Col, a.Row);
        a.StartUhr++;
        if (heli && a.StartUhr == 39 && !Audio.GameSounds.FlugklangAus)
            Audio.GameSounds.PlayAt(Audio.GameSounds.HeliStartSteigen, a.Col, a.Row);
        if (a.StartUhr <= (heli ? StartUhrHeli : StartUhrFlugzeug)) return;
        a.Order = !heli && a.Order2 == 6 ? 3 : a.Order2;           // 0x423498 / 0x4234EE / 0x4235D8
        StartsBeendet++;
        if (_kiStartCheck.TryGetValue(a, out var m)) _kiStartCheck[a] = (m.Takt0, a.StartUhr, a.Order);
    }

    // ---- --flak-ki-start-check ----------------------------------------------

    public static bool FlakKiStartCheckAn;

    /// <summary>Je KI-Start: (Startuhr bei Abflug, Uhr beim Wechsel, Order danach).</summary>
    private readonly Dictionary<Special, (int Takt0, int UhrWechsel, int OrderDanach)> _kiStartCheck = new();

    private void KiStartGemerkt(Special a)
    {
        if (FlakKiStartCheckAn) _kiStartCheck[a] = (a.StartUhr, -1, 4);
    }

    /// <summary>
    /// Die Zeile zu <c>--flak-ki-start-check</c> (kopflos, z. B. K17 mit
    /// <c>--quit-after=400</c>): je KI-gestartetem Flieger Order jetzt und die Uhr beim
    /// Wechsel (Soll 7 nach 26/43 Takten), dazu »vom uk-Filter verworfen«,
    /// Kegelproben, Auftraege und Abschuesse der Flak. Nullmodell
    /// <c>--startzustand-alt</c>: Order bleibt 4, Kegelproben 0, Auftraege 0.
    /// </summary>
    public string FlakKiStartLine()
    {
        var sb = new System.Text.StringBuilder("flak-ki-start-check"
            + (StartzustandAlt ? " — NULLMODELL --startzustand-alt" : "") + ": ");
        int flak = 0;
        foreach (var e in _entities) if (!e.IsProp && !e.Dead && e.Weapon == FlakAufsatz) flak++;
        int n = _kiStartCheck.Count, gewechselt = 0, falsch = 0;
        var teile = new List<string>();
        foreach (var (a, m) in _kiStartCheck)
        {
            bool heli = a.Kind >= 10;
            int soll = (heli ? StartUhrHeli : StartUhrFlugzeug) + 1;
            if (m.UhrWechsel >= 0) { gewechselt++; if (m.UhrWechsel != soll || m.OrderDanach != a.Order2 && !(a.Order2 == 6 && m.OrderDanach == 3)) falsch++; }
            if (teile.Count < 6)
                teile.Add($"{(heli ? "Heli" : "Flugzeug")} Platz {a.Slot} Sp{a.Owner}: Order {a.Order} (m_uk {a.Order2}), "
                        + (m.UhrWechsel >= 0 ? $"Wechsel bei Uhr {m.UhrWechsel} (Soll {soll})" : $"kein Wechsel, Uhr {a.StartUhr}")
                        + (a.Dead ? ", abgeschossen" : ""));
        }
        sb.Append($"{flak} Flak, {n} KI-Starts, davon {gewechselt} mit Wechsel ({falsch} mit falscher Uhr/Order); ");
        sb.Append($"vom uk-Filter verworfen {FlakUkVerworfen}, Kegelproben {FlakKegelProben}, Auftraege {FlakAuftraege}, "
                + $"Schuesse {FlakSchuesse}, Abschuesse {FlakAbschuesse}");
        foreach (var t in teile) sb.Append("\n   ").Append(t);
        bool urteil = flak > 0 && n > 0;
        bool ok = urteil && gewechselt == n - Unbeendet() && falsch == 0 && FlakKegelProben > 0 && FlakAuftraege >= 1;
        sb.Append(!urteil ? "\n   KEIN URTEIL (keine Flak oder kein KI-Start)"
                          : ok ? "\n   BESTANDEN" : "\n   DURCHGEFALLEN");
        return sb.ToString();

        int Unbeendet()
        {
            int u = 0;
            foreach (var (a, m) in _kiStartCheck)
                if (m.UhrWechsel < 0 && (a.Dead || a.Stored || a.StartUhr <= ((a.Kind >= 10) ? StartUhrHeli : StartUhrFlugzeug)) && !StartzustandAlt) u++;
            return u;
        }
    }
}
