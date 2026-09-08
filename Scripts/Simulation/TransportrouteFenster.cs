using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>WAS DAS ROUTENFENSTER BRAUCHT</b> — die Bedienseite der Materialroute
/// (Stufe 2, 08.09.2026). Der Umlauf selbst steht in
/// <c>Simulation/Transportroute.cs</c>.
///
/// <para>Im Original sind das <b>zwei</b> Fenster: das Sechs-Symbole-Menü
/// (Fensterart 1, Doppelklick oder Leertaste auf die gewählte Einheit,
/// <c>0x4141B4</c>/<c>0x413069</c>), dessen Eintrag »Transportzyklus
/// einstellen« (Code <c>0x10</c>, nur bei Turmaufsatz <c>+0x0C == 0x2E</c>)
/// dann <b>Fensterart 16</b> öffnet (<c>0x44897D</c>): 280×120, vier Knöpfe
/// <b>Von · Nach · Leeren · Start</b> bei <c>y+14</c>, je 60×20, x =
/// 20/80/140/200 (<c>0x45EF48</c>), dazu die Planungskarte (Art 3,
/// Betriebsart 4), auf der die Gebäude angeklickt werden
/// (<c>0x4495BD</c>).</para>
///
/// <para><b>Der Ablauf des Originals</b>, und den bauen wir nach: Fenster auf →
/// <b>Nach</b> ist gedrückt → der erste Gebäudeklick setzt das <b>Ziel</b> und
/// schaltet auf <b>Von</b> → weitere Klicks füllen die vier Quellfächer (ein
/// zweiter Klick auf dieselbe Quelle löscht sie) → <b>Start</b>. Und: das
/// Öffnen des Fensters <b>hält die Route an</b> (Netzbefehl 514,
/// <c>0x4489C3</c>).</para>
///
/// <para><b>⚠ UNSERE Setzungen, ausdrücklich:</b></para>
/// <list type="bullet">
///   <item>Die Gebäude werden auf der <b>Hauptkarte</b> angeklickt, nicht auf
///   einer eigenen Planungskarte in Betriebsart 4 — die haben wir nicht. Der
///   Modus ist derselbe, die Fläche ist eine andere.</item>
///   <item>Die <b>Verträglichkeitstafel</b> <c>0x4FDC00</c> (welche Gebäudeart
///   für welches Ziel überhaupt Quelle sein darf) ist NICHT gelesen — sie steht
///   auch im Fable-Bericht unter »was nicht gelesen wurde«. Wir prüfen darum nur
///   das, was gelesen ist: <b>eine Quelle darf nicht das Ziel sein</b>
///   (<c>0x4362E0</c>).</item>
///   <item>Das <b>Sechs-Symbole-Menü</b> ist nicht gebaut. Der Weg dorthin
///   führt bei uns über einen Knopf in der Befehlsleiste der Einheit — dieselbe
///   Entscheidung wie bei »Verkaufen« und »Radar setzen«, und aus demselben
///   Grund (siehe <c>UI/UnitOrderBar.cs</c>: eine Mechanik, die nur auf einer
///   Taste liegt, ist für den Spieler nicht vorhanden). Die <b>Leertaste</b>
///   des Originals öffnet es zusätzlich.</item>
/// </list>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary>0 = aus · 1 = »Von« (Quellen wählen) · 2 = »Nach« (Ziel
    /// wählen). Das Original führt zwei Druckflaggen im Fenstersatz
    /// (<c>+0xACA8</c> Von, <c>+0xACAC</c> Nach); eine Zahl sagt dasselbe und
    /// kann nicht in einen Zustand geraten, den es dort nicht gibt.</summary>
    public int RouteWahlModus;

    /// <summary>Der Wagen, dessen Route gerade bearbeitet wird — im Original
    /// <c>word[Fenster+0xACA0]</c>. −1 = kein Fenster.</summary>
    public int RouteFensterWagen = -1;

    /// <summary>Die Rueckmeldung des Fensters — »Kann nicht starten.« und
    /// dergleichen.</summary>
    public string RouteNote = "";

    /// <summary>Der erste gewaehlte eigene Transporter, oder −1.</summary>
    public int RouteAuswahlWagen()
    {
        foreach (int i in _sel)
        {
            if (i < 0 || i >= _entities.Count) continue;
            var e = _entities[i];
            if (e.Dead || e.IsBuilding || e.IsProp) continue;
            if (e.Owner != ViewPlayer || !IstTransporter(e)) continue;
            return i;
        }
        return -1;
    }

    /// <summary>Traegt die AUSWAHL einen Transporter? Fuer die Befehlsleiste.</summary>
    public bool RouteKnopfSichtbar() => RouteAuswahlWagen() >= 0;

    /// <summary>
    /// Das Fenster oeffnen — <c>0x44897D</c>. ⚠ Der erste Schritt dort ist
    /// Netzbefehl <b>514</b>: <b>die Route haelt an, sobald das Fenster
    /// aufgeht</b>. Danach ist »Nach« gedrueckt (<c>+0xACAC := 1</c>).
    /// </summary>
    public bool RouteFensterOeffnen()
    {
        int i = RouteAuswahlWagen();
        if (i < 0) return false;
        var u = _entities[i];
        var r = RouteVon(u) ?? RouteAnlegen(u);
        if (r == null) { RouteNote = "dieser Wagen fuehrt keinen Umschlagsatz"; return false; }
        RouteAnhalten(u);                       // Befehl 514
        RouteFensterWagen = i;
        RouteWahlModus = 2;                     // »Nach« ist zuerst gedrueckt
        RouteNote = "Ziel anklicken";
        return true;
    }

    public void RouteFensterSchliessen()
    {
        RouteFensterWagen = -1;
        RouteWahlModus = 0;
        RouteNote = "";
    }

    /// <summary>Der Satz des Fensters, oder <c>null</c>.</summary>
    public Transportroute? RouteFensterSatz()
        => RouteFensterWagen >= 0 && RouteFensterWagen < _entities.Count
         ? RouteVon(_entities[RouteFensterWagen]) : null;

    /// <summary>Was im Fenster steht: die vier Quellnamen, der Zielname und ob
    /// die Route laeuft. Die Namen kommen aus <see cref="BuildingTypeName"/> —
    /// im Original aus <c>0x459110</c>, nach Gebaeudeart.</summary>
    public (string[] Quellen, string Ziel, bool Gestartet) RouteAnzeige()
    {
        var r = RouteFensterSatz();
        var q = new string[4];
        for (int k = 0; k < 4; k++)
        {
            var b = r == null ? null : GebaeudePlatz(r.Quelle[k]);
            q[k] = b == null ? "leer" : $"{BuildingTypeName(b.BType)} ({b.Col},{b.Row})";
        }
        var z = r == null ? null : GebaeudePlatz(r.Ziel);
        return (q, z == null ? "leer" : $"{BuildingTypeName(z.BType)} ({z.Col},{z.Row})",
                r?.Gestartet ?? false);
    }

    /// <summary>
    /// Ein Fach setzen — <c>0x4362E0(satz, fach, wert)</c>. Fach 4 ist das
    /// ZIEL, 0..3 sind die Quellen.
    ///
    /// <para>⚠ Von den beiden Regeln des Originals ist nur EINE gelesen und
    /// darum nur eine gebaut: <b>eine Quelle, die das Ziel ist, faellt heraus</b>.
    /// Die zweite haengt an der Vertraeglichkeitstafel <c>0x4FDC00</c>, die
    /// nicht gelesen ist.</para>
    /// </summary>
    public void RouteFachSetzen(int fach, int gebaeude)
    {
        var r = RouteFensterSatz();
        if (r == null) return;
        if (fach == 4)
        {
            r.Ziel = gebaeude;
            for (int k = 0; k < 4; k++)
                if (r.Quelle[k] == gebaeude) r.Quelle[k] = -1;
        }
        else if (fach is >= 0 and <= 3)
        {
            r.Quelle[fach] = gebaeude;
            if (r.Ziel == gebaeude) r.Ziel = -1;
        }
    }

    /// <summary>
    /// Der Klick auf ein Gebaeude — <c>0x4495BD</c>, Schritt fuer Schritt:
    /// im »Nach«-Modus setzt er das Ziel und schaltet auf »Von«; im
    /// »Von«-Modus fuellt er das erste freie Quellfach, und ein zweiter Klick
    /// auf dieselbe Quelle loescht sie wieder.
    /// </summary>
    public bool RouteKlickAufGebaeude(Vector2 mapPos)
    {
        if (RouteWahlModus == 0) return false;
        var r = RouteFensterSatz();
        if (r == null) return false;

        int hit = Pick(mapPos);
        if (hit < 0 || !_entities[hit].IsBuilding)
        {
            RouteNote = "das ist kein Gebaeude";
            return false;
        }
        var b = _entities[hit];
        if (b.Owner != ViewPlayer)
        {
            RouteNote = "nur eigene Gebaeude";
            return false;
        }

        if (RouteWahlModus == 2)                       // »Nach«
        {
            RouteFachSetzen(4, b.Slot);
            RouteWahlModus = 1;                        // -> »Von«
            RouteNote = $"Ziel: {BuildingTypeName(b.BType)} — jetzt die Quellen anklicken";
            return true;
        }

        for (int k = 0; k < 4; k++)                    // schon Quelle? -> abwaehlen
            if (r.Quelle[k] == b.Slot)
            {
                r.Quelle[k] = -1;
                RouteNote = $"{BuildingTypeName(b.BType)} wieder abgewaehlt";
                return true;
            }
        for (int k = 0; k < 4; k++)
            if (r.Quelle[k] < 0)
            {
                RouteFachSetzen(k, b.Slot);
                RouteNote = $"Quelle {k + 1}: {BuildingTypeName(b.BType)}";
                return true;
            }
        RouteNote = "alle vier Quellfaecher sind belegt";
        return false;
    }

    /// <summary>Knopf »Von« bzw. »Nach«.</summary>
    public void RouteModusSetzen(int modus)
    {
        RouteWahlModus = modus;
        RouteNote = modus == 1 ? "Quellen anklicken" : "Ziel anklicken";
    }

    /// <summary>Knopf »Leeren« — im Original fuenfmal Befehl 512 mit
    /// <c>0xFF</c>, danach steht der Modus wieder auf »Nach«.</summary>
    public void RouteLeeren()
    {
        var r = RouteFensterSatz();
        if (r == null) return;
        for (int k = 0; k < 4; k++) r.Quelle[k] = -1;
        r.Ziel = -1;
        RouteWahlModus = 2;
        RouteNote = "geleert — Ziel anklicken";
    }

    /// <summary>
    /// Knopf »Start« — <c>0x44B7E5</c>. Sind alle Quellen oder ist das Ziel
    /// leer, kommt die Meldung des Originals (»Kann nicht starten.« /
    /// »Waehlen Sie bitte Start- und Zielgebaeude.«); sonst Befehl 513 und das
    /// Fenster geht zu.
    /// </summary>
    public bool RouteStartKnopf()
    {
        var r = RouteFensterSatz();
        if (r == null) return false;
        bool keineQuelle = true;
        foreach (int q in r.Quelle) if (q >= 0) keineQuelle = false;
        if (keineQuelle || r.Ziel < 0)
        {
            RouteNote = "Kann nicht starten. Waehlen Sie bitte Start- und Zielgebaeude.";
            return false;
        }
        RouteStarten(_entities[RouteFensterWagen], r);
        RouteNote = "Transport gestartet";
        RouteFensterSchliessen();
        return true;
    }
}
