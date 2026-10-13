namespace AkteEuropaReborn.Rendering;

using System.Text;
using Godot;

/// <summary>
/// <c>--garage-unsichtbar-check</c> (bug-511, KayelGee R5#10/#13, R4#20): »beim
/// Einfahren ins Depot bleibt eine unsichtbare Einheit zurueck, die auch
/// angegriffen werden kann«.
///
/// <para>⚠ Er misst am GEMELDETEN: Lebensbalken (Tab-Stellung 1, wie der
/// Zeichner ihn fragt), Minikartenpunkt, laufender Angriff und Geschoss im Flug.
/// Die Pruefstaende aus Runde 4 (--depotauslass-check, --tuerauslass-check)
/// massen das Herausfahren und waren deshalb gruen.</para>
///
/// <para>Aufbau: eine eigene Einheit A, ein fremder bewaffneter Angreifer B mit
/// <c>Target = A</c>, ein Geschoss auf A im Flug; dann faehrt A ueber
/// <see cref="Einfahren"/> in das naechste Gebaeude mit Tuer (fuer die Messung
/// dem Spieler zugeschrieben, gesagt). Nach 6 s wird gezaehlt. Soll: Balken 0,
/// Punkt 0, B.Target != A, A unbeschaedigt. Nullmodell
/// <c>--garage-balken-alt --garage-ziel-alt</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    private int _gA = -1, _gB = -1, _gHp, _gImFlug;
    private int _gGeloestVorher, _gVerhindertVorher;

    public string GarageProbeStart()
    {
        int me = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;
        for (int i = 0; i < _entities.Count && (_gA < 0 || _gB < 0); i++)
        {
            var e = _entities[i];
            if (e.IsProp || e.IsBuilding || e.Dead || !e.Mobile) continue;
            if (_gA < 0 && e.Owner == me && e.Infantry < 0) _gA = i;
            else if (_gB < 0 && e.Owner is >= 0 and <= 7 && e.Owner != me && !Allied(me, e.Owner) && e.Weapon > 0) _gB = i;
        }
        if (_gA < 0 || _gB < 0) return "garage-unsichtbar-check: keine eigene Einheit oder kein Gegner — sagt NICHTS";
        var a = _entities[_gA]; var b = _entities[_gB];
        Entity? geb = null;
        foreach (var x in _entities)
            if (x.IsBuilding && !x.Dead && x.DoorCells.Count > 0 && x.BType is 1 or 5 or 6 or 7) { geb = x; break; }
        if (geb == null) return "garage-unsichtbar-check: kein Gebaeude mit Tuer — sagt NICHTS";
        geb.Owner = geb.Team = geb.ShownOwner = me;
        b.Target = _gA; b.Ordered = true;
        // ein ECHTER Schuss ueber Fire — ist die Waffe ein Flugkoerper, ist er
        // waehrend des Einfahrens noch unterwegs
        Fire(_gB, b, _gA, a, ("Probe", 10, 99f));
        _gImFlug = 0;
        foreach (var s in _shots) if (s.Target == _gA) _gImFlug++;
        _gHp = a.Hp;
        _gGeloestVorher = GarageZieleGeloest; _gVerhindertVorher = GarageTrefferVerhindert;
        Einfahren(geb, _gA, a);
        return $"garage-unsichtbar-check: Einheit {a.Slot} (Hp {a.Hp}) faehrt in Gebaeude {geb.Slot} (Typ {geb.BType}, " +
               $"fuer die Messung dem Spieler zugeschrieben); Angreifer {b.Slot} (Spieler {b.Owner}) zielt auf sie, " +
               $"{_gImFlug} Geschoss(e) im Flug{(_gImFlug == 0 ? " (Sofortwaffe — der Geschossteil sagt NICHTS)" : "")}" +
               (GarageBalkenAlt ? "  ⚠ --garage-balken-alt" : "") + (GarageZielAlt ? "  ⚠ --garage-ziel-alt" : "");
    }

    public string GarageProbeAuswerten()
    {
        if (_gA < 0) return "garage-unsichtbar-check: kein Aufbau";
        var a = _entities[_gA]; var b = _entities[_gB];
        bool balken = TabBalkenFuer(a, 1) != null;
        int punkte = 0;
        foreach (var (pos, owner, bau) in MinimapDots())
            if (!bau && pos.DistanceTo(a.Pos) < 1f) punkte++;
        bool ziel = b.Target == _gA;
        int schaden = _gHp - a.Hp;
        bool ok = !balken && punkte == 0 && !ziel && schaden == 0 && Untergestellt(a);
        return "garage-unsichtbar-check:" +
               $"\n   untergestellt {Untergestellt(a)} (Ukol {a.Ukol}), Lebensbalken Stellung 1: {(balken ? "JA" : "nein")}, " +
               $"Minikartenpunkte an der Tuer: {punkte}" +
               $"\n   Angreifer zielt noch auf sie: {(ziel ? "JA" : "nein")}, Schaden seit dem Einfahren: {schaden} " +
               $"(Ziele geloest {GarageZieleGeloest - _gGeloestVorher}, Treffer verhindert {GarageTrefferVerhindert - _gVerhindertVorher})" +
               $"\n   {(ok ? "BESTANDEN" : "DURCHGEFALLEN")}";
    }
}
