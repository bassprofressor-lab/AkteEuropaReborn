using Godot;
using System.Collections.Generic;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <c>--absetz-check</c> — <b>bleibt eine ausgeladene Einheit auch draussen?</b>
/// (10.09.2026, bug-155.)
///
/// <para>⚠⚠ Seine Meldung: »wenn ich eine einheit auslade, laeuft sie kurz und
/// beamt sich wieder zurueck in das Transport Schiff.« Der bestehende
/// <c>--einsteigen-check</c> konnte das NICHT zeigen: er zaehlt beim
/// Missionsstart, und da steht noch niemand neben einem Traeger — er meldete
/// eine glatte 0 und haette »behoben« gesagt, ohne den Fall je gesehen zu
/// haben. Derselbe Merkposten wie bei bug-105: <b>eine Probe, die den Fall
/// nicht herstellt, misst ihn auch nicht.</b>
/// </para>
///
/// <para>Dieser Prueflauf stellt ihn her, ueber den echten Weg und in der
/// echten Uhr: er sucht einen Traeger, nimmt einen Fusssoldaten an Bord, setzt
/// ihn wieder ab und sieht <see cref="WarteSekunden"/> Sekunden spaeter nach,
/// wo er steht. Bestanden ist der Lauf, wenn der Abgesetzte DRAUSSEN ist.</para>
///
/// <para>Nullmodell: mit <c>--einsteigen-im-stand</c> (dem Stand vor dem
/// 10.09.) muss er wieder an Bord sein. Zeigt der Gegenschalter dasselbe
/// Ergebnis, misst der Stand nicht, was er zu messen vorgibt.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary>Wie lange nach dem Absetzen nachgesehen wird. ⚠ UNSERE Zahl,
    /// und sie ist laenger als die Absetzsperre von 3 s — sonst misst der Lauf
    /// nur die Sperre und nicht die Regel dahinter.</summary>
    private const float WarteSekunden = 6f;

    private int _absetzTraeger = -1, _absetzSlot = -1;
    private float _absetzUm2 = -1f;
    private Vector2I _absetzOrt;
    private string _absetzZurueck = "";
    private float _absetzUm = -1f;
    private string _absetzStart = "", _absetzErgebnis = "";

    /// <summary>Sucht Traeger und Fahrgast und setzt ab. Einmal zu rufen.</summary>
    public string AbsetzCheckStart()
    {
        var sb = new System.Text.StringBuilder("absetz-check\n");

        // 1. ein Traeger, der etwas tragen kann
        int traeger = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var q = _entities[i];
            if (q.Dead || q.IsBuilding || q.IsProp || !IstTraeger(q)) continue;
            traeger = i; break;
        }
        if (traeger < 0)
        {
            _absetzStart = "  kein Traeger auf dieser Karte — der Lauf sagt NICHTS";
            return sb.Append(_absetzStart).ToString();
        }
        var t = _entities[traeger];

        // 2. ein Fusssoldat desselben Spielers, moeglichst nah
        int gast = -1, nah = int.MaxValue;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Dead || e.IsBuilding || e.IsProp || !e.Mobile) continue;
            if (e.Owner != t.Owner || e.Infantry < 0) continue;
            int d = Mathf.Max(Mathf.Abs(e.Col - t.Col), Mathf.Abs(e.Row - t.Row));
            if (d >= nah) continue;
            nah = d; gast = i;
        }
        if (gast < 0)
        {
            _absetzStart = $"  Traeger Platz {t.Slot}, aber kein eigenes Fussvolk — "
                         + "der Lauf sagt NICHTS";
            return sb.Append(_absetzStart).ToString();
        }

        // 3. an Bord — DIREKT ueber BeladeVersuch, nicht ueber den Takt: der
        //    Takt verlangt seit dem 10.09. den Ankommenden, und wir wollen hier
        //    das ABSETZEN messen, nicht das Einsteigen.
        var g = _entities[gast];
        int gSlot = g.Slot, gCol = g.Col, gRow = g.Row;
        g.Col = t.Col; g.Row = t.Row;                 // neben den Traeger stellen
        int rc = BeladeVersuch(gast, melden: false);
        if (rc < 0)
        {
            g.Col = gCol; g.Row = gRow;
            _absetzStart = $"  Platz {gSlot} kam nicht an Bord ({_order}) — "
                         + "der Lauf sagt NICHTS";
            return sb.Append(_absetzStart).ToString();
        }

        // 4. und gleich wieder herunter
        int raus = FrachtAbsetzen(t.Slot, new Vector2I(t.Col, t.Row), 1);
        _absetzTraeger = t.Slot; _absetzSlot = gSlot;
        _absetzOrt = new Vector2I(t.Col, t.Row);
        _absetzUm = _clock + WarteSekunden;
        _absetzStart = $"  Traeger Platz {t.Slot} (Spieler {t.Owner}) bei ({t.Col},{t.Row}), "
                     + $"Fahrgast Platz {gSlot}\n"
                     + $"  an Bord: ja | abgesetzt: {raus} | "
                     + $"nachgesehen wird in {WarteSekunden:0} s";
        return sb.Append(_absetzStart).ToString();
    }

    /// <summary>Jeden Takt zu rufen. Sieht nach, sobald die Zeit um ist.</summary>
    public void AbsetzCheckTick()
    {
        AbsetzCheckTeil2();
        if (_absetzUm < 0 || _clock < _absetzUm || _absetzErgebnis.Length > 0) return;
        bool draussen = false;
        int col = -1, row = -1;
        foreach (var e in _entities)
            if (e.Slot == _absetzSlot && !e.IsBuilding)
            { draussen = true; col = e.Col; row = e.Row; break; }
        bool anBord = false;
        foreach (var q in FrachtAnBord(_absetzTraeger))
            if (q.Slot == _absetzSlot) { anBord = true; break; }

        var sb = new System.Text.StringBuilder();
        sb.Append($"  nach {WarteSekunden:0} s: Platz {_absetzSlot} ");
        sb.Append(draussen ? $"steht DRAUSSEN bei ({col},{row})"
                : anBord  ? "ist WIEDER AN BORD"
                          : "ist verschwunden (weder draussen noch an Bord)");
        _absetzErgebnis = sb.ToString();
        GD.Print(_absetzErgebnis);

        // ⚠⚠ DIE ZWEITE HAELFTE, und ohne sie taugt der Lauf nichts: wer das
        // Zurueckbeamen abstellt, kann dabei das EINLADEN erschlagen — und
        // genau das Einladen der drei Forscher war seine gute Nachricht von
        // heute. Der Lauf schickt den Abgesetzten darum jetzt WIEDER zum
        // Traeger und sieht nach, ob er an Bord geht.
        if (!draussen) return;
        var zelle = FreieZelleUm(new Vector2I(_absetzOrt.X, _absetzOrt.Y));
        if (zelle == null) { _absetzZurueck = "  kein Platz am Traeger — ungeprueft"; return; }
        MissionOrderAt(_absetzSlot, zelle.Value.X, zelle.Value.Y, -1);
        _absetzUm2 = _clock + WarteSekunden * 2f;
    }

    /// <summary>Die zweite Haelfte: geht der Abgesetzte auf Befehl wieder an
    /// Bord? Wenn nicht, haette die Behebung das Einladen erschlagen.</summary>
    private void AbsetzCheckTeil2()
    {
        if (_absetzUm2 < 0 || _clock < _absetzUm2 || _absetzZurueck.Length > 0) return;
        bool anBord = false;
        foreach (var q in FrachtAnBord(_absetzTraeger))
            if (q.Slot == _absetzSlot) { anBord = true; break; }
        _absetzZurueck = "  auf Befehl zurueck zum Traeger: "
                       + (anBord ? "WIEDER AN BORD — das Einladen geht noch"
                                 : "NICHT an Bord ⚠ — das Einladen ist erschlagen")
                       + (anBord ? "\n  BESTANDEN" : "\n  DURCHGEFALLEN");
        GD.Print(_absetzZurueck);
    }

    /// <summary>Der Rueckgabewert des Laufs: 0 bestanden, 1 durchgefallen.
    /// ⚠ Ein Lauf ohne Traeger oder ohne Fussvolk gibt 0 zurueck und sagt es
    /// in seiner Zeile — er hat nichts gemessen, also darf er nichts
    /// behaupten.</summary>
    public int AbsetzCheckRc()
        => _absetzErgebnis.Length == 0 ? 0
         : !_absetzErgebnis.Contains("BESTANDEN") ? 1
         : _absetzZurueck.Length > 0 && !_absetzZurueck.Contains("BESTANDEN") ? 1
         : 0;
}
