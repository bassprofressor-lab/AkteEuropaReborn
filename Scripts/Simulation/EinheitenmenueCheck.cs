using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <c>--einheitenmenue-check</c> — <b>steht im Menü, was vorher in der Leiste
/// stand?</b>
///
/// <para>Die Messlatte zu seiner Ansage »bau alles nach original mit dem
/// Doppelklick, anstatt unsere Anzeige die unten mittig ist«. Die Leiste konnte
/// sechs Dinge; wenn eines davon im Menü fehlt, ist es für den Spieler weg —
/// und genau das wäre der Rückschritt, den niemand bemerkt, bis er ihn
/// braucht.</para>
///
/// <para>Geprüft wird je Einheit der Karte: <b>bietet die Leiste etwas an, das
/// das Menü NICHT hat?</b> Dazu die Gegenrichtung — wie viele Einheiten
/// überhaupt ein Menü bekommen. ⚠ Ohne die zweite Zahl wäre »0 Lücken« auch
/// dann grün, wenn gar kein Menü entstünde.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    public string EinheitenmenueCheck()
    {
        var sb = new System.Text.StringBuilder("einheitenmenue-check\n");
        int mitMenue = 0, luecken = 0, gezeigt = 0, geprueft = 0;
        var alteAuswahl = new System.Collections.Generic.List<int>(_sel);

        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Dead || e.IsBuilding || e.IsProp || e.Owner != ViewPlayer) continue;
            geprueft++;
            // Die Leiste fragt IMMER die Auswahl — also eine Einheit waehlen.
            _sel.Clear(); _sel.Add(i);

            var codes = MenueCodes(i);
            bool hat(int c) { foreach (int k in codes) if (k == c) return true; return false; }
            bool leer = true;
            foreach (int k in codes) if (k >= 0) leer = false;
            if (!leer) mitMenue++;

            var fehlt = new System.Collections.Generic.List<string>();
            // ⚠⚠ 08.09.2026 — DIE ERWARTUNG IST NACHGEZOGEN, und das gehoert
            // gesagt: sie war nach UNSERER Leiste gebaut, nicht nach dem
            // Original. Die Tafel 0x441810 gibt »Verkaufen« nur einem FAHRZEUG
            // (+0x0A == 0) und »Anhalten« nur einer UNBEWAFFNETEN Einheit
            // (+0x0D == 0) — ein bewaffneter Panzer hat im Menue des Originals
            // kein »Anhalten«. Wer die alte Erwartung stehen laesst, misst
            // seine eigene Leiste und nennt das Original einen Fehler.
            if (e.GameUnitType == 0 && SellChoiceOfSelection() != null
                && !hat(CodeVerkaufen)) fehlt.Add("Verkaufen");
            if (RadarChoiceOfSelection() != null && !hat(CodeRadar)) fehlt.Add("Radar setzen");
            foreach (var b in BuildChoicesOfSelection())
            {
                int c = b.Order == OrderDepot ? CodeDepot
                      : b.Order == OrderFieldMine ? CodeFeldmine
                      : b.Order == OrderGenerator ? CodeGenerator : -1;
                if (c >= 0 && !hat(c)) fehlt.Add(b.Word);
            }
            if (IstTransporter(e) && !hat(CodeTransportzyklus)) fehlt.Add("Transportzyklus");
            if (e.Mobile && e.Comp0D == 0 && e.Comp0F != 0xAB && !hat(CodeAnhalten))
                fehlt.Add("Anhalten");

            if (fehlt.Count == 0) continue;
            luecken++;
            if (gezeigt++ < 6)
                sb.Append($"  ⚠ Platz {e.Slot} \"{LabelOf(e)}\" (Aufsatz {e.Weapon}): "
                        + $"im Menue FEHLT {string.Join(", ", fehlt)}\n");
        }

        _sel.Clear();
        foreach (int i in alteAuswahl) _sel.Add(i);

        sb.Append($"  {geprueft} eigene Einheiten geprueft, {mitMenue} bekommen ein Menue, "
                + $"{luecken} mit Luecke -> "
                + (mitMenue == 0 ? "KEIN MENUE — die Probe misst nichts"
                 : luecken == 0 ? "WIE ERWARTET (das Menue kann alles, was die Leiste konnte)"
                                : "NICHT wie erwartet"));
        return sb.ToString();
    }
}
