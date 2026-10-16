namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;
using AkteEuropaReborn.Simulation.Commands;

/// <summary>
/// ⭐⭐ <b>DIE ÜBRIGEN EINGABEN, DIE AM RING VORBEI GINGEN</b> (17.10.2026,
/// zweites Paket nach bug-530, <c>berichte/desync2-opus.md</c>).
///
/// <para>Der Rahmen, den alle Befehle dieses Pakets teilen — die Absender
/// und Behandler selbst stehen in <c>Direktbefehle2*.cs</c> je
/// Sachgebiet.</para>
///
/// <para><b>Der SATZKONTEXT.</b> Die alten Direktwege lesen fast alle die
/// AUSWAHL (<c>_sel</c>, <c>_selected</c>, <c>_selAir</c>, das Fenstergebäude)
/// und den SICHTSPIELER. Im Behandler gibt es weder das eine noch das andere —
/// er läuft auf jeder Maschine, und dort ist eine andere Auswahl und ein
/// anderer Sichtspieler. <see cref="ImSatz{T}"/> stellt darum für die Dauer des
/// Behandlers genau das her, was der Satz sagt (Spieler = Absender, Auswahl =
/// die Einheit/das Gebäude aus P1), ruft den alten Weg, und stellt danach die
/// örtliche Auswahl wieder her. So wirkt im Einzelspieler DERSELBE Code wie
/// vorher, nur einen Takt später — und im Netz auf beiden Maschinen gleich.
/// Kommt der Satz von einem ANDEREN Spieler, bleiben Klang, Meldungsfenster
/// und Auftragszeile stumm: sie gehören dem, der geklickt hat.</para>
///
/// <para>Gegenschalter <c>--direktbefehle2-alt</c> (alle Pakete wieder
/// unmittelbar) oder <c>--direktbefehle2-alt=paket,paket</c> mit den Namen
/// aus <see cref="Pakete"/>. <c>--direktbefehle-alt</c> (bug-530) schliesst
/// dieses Paket mit ein.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Die Unterwahl von <c>--direktbefehle2-alt</c>: leer = keines,
    /// »*« = alle, sonst durch Komma getrennt.</summary>
    public static string Direktbefehle2Alt = "";

    /// <summary>Die Paketnamen der Unterwahl.</summary>
    public static readonly string[] Pakete =
    {
        "produktion", "reparatur", "depot", "flughafen", "hand", "route", "entwurf",
        "bahn", "pionier", "verkauf", "sonst", "pause", "spielstand", "cheat",
    };

    /// <summary>Geht dieses Paket noch den alten, unmittelbaren Weg?</summary>
    public static bool Alt2(string paket)
        => DirektbefehleAlt || Direktbefehle2Alt == "*"
           || ("," + Direktbefehle2Alt + ",").Contains("," + paket + ",");

    /// <summary>Läuft gerade ein Netzspiel (der Taktgeber hält den Ring)?</summary>
    public static bool ImNetz => NetGameRunner.Active;

    /// <summary>
    /// Den alten Weg unter dem SATZKONTEXT laufen lassen — siehe Kopf.
    /// </summary>
    /// <param name="c">der Satz (Spieler = Absender).</param>
    /// <param name="sel">die Einheit/das Gebäude, das als gewählt gilt (−1 = keine).</param>
    /// <param name="air">das Flugzeug, das als gewählt gilt (−1 = keines).</param>
    /// <param name="f">der alte Weg.</param>
    private T ImSatz<T>(in CommandRecord c, int sel, int air, System.Func<T> f)
    {
        bool fremd = c.Player != ViewPlayer;
        int merkView = _viewPlayer, merkSelected = _selected, merkAir = _selAir;
        var merkSel = new List<int>(_sel);
        string merkOrder = _order;
        var merkMeldung = OnMeldung;
        bool merkStumm = Audio.SoundBankPlayer.Suppressed;

        _viewPlayer = c.Player;
        _sel.Clear();
        if (sel >= 0) _sel.Add(sel);
        _selected = sel;
        _selAir = air;
        if (fremd)
        {
            OnMeldung = null;
            Audio.SoundBankPlayer.Suppressed = true;
        }
        T r;
        try { r = f(); }
        finally
        {
            bool unberuehrt = _selected == sel && _selAir == air
                              && _sel.Count == (sel >= 0 ? 1 : 0) && (sel < 0 || _sel.Contains(sel));
            _viewPlayer = merkView;
            // Die örtliche Auswahl zurück — es sei denn, der eigene Befehl hat
            // sie selbst gesetzt (z. B. die Handsteuerung wählt ihr Flugzeug an;
            // das tat der Direktweg auch).
            if (fremd || unberuehrt)
            {
                _sel.Clear();
                foreach (int i in merkSel) _sel.Add(i);
                _selected = merkSelected;
                _selAir = merkAir;
            }
            if (fremd)
            {
                _order = merkOrder;
                OnMeldung = merkMeldung;
                Audio.SoundBankPlayer.Suppressed = merkStumm;
            }
            UpdatePanel();
            QueueRedraw();
        }
        return r;
    }

    private void ImSatz(in CommandRecord c, int sel, int air, System.Action f)
        => ImSatz(c, sel, air, () => { f(); return true; });

    /// <summary>Ein Satz für einen Index in der Einheitenliste — gültig und vom
    /// Absender?</summary>
    private bool EigenesObjekt(in CommandRecord c, int i)
        => i >= 0 && i < _entities.Count && !_entities[i].Dead && _entities[i].Owner == c.Player;

    /// <summary>Wie <see cref="EigenesObjekt"/>, aber auch Gebäude ohne
    /// Besitzer (Markt, Nachschubposten — Besitzer 255), die jeder bedient.</summary>
    private bool BedienbaresGebaeude(in CommandRecord c, int i)
        => i >= 0 && i < _entities.Count && _entities[i].IsBuilding && !_entities[i].Dead
           && (_entities[i].Owner == c.Player || _entities[i].Owner is < 0 or > 7);

    private static short S(int v) => (short)Mathf.Clamp(v, short.MinValue, short.MaxValue);

    // ================= DER VERTEILER DIESES PAKETS ============================

    /// <summary>Gehört die Nummer diesem Paket? Steht im Verteiler VOR der
    /// Eigentümerschranke — viele P1 sind keine Einheit (Flugzeug 20000+,
    /// Fach, Linie, Gebäudeplatz); die Behandler prüfen selbst.</summary>
    internal static bool IstDirekt2(short op) => op switch
    {
        CommandOp.HandSchritt or CommandOp.HandRohr or CommandOp.HandUebernehmen
            or CommandOp.HandAbgeben or CommandOp.HandLuft or CommandOp.FireAt
            or CommandOp.Selbstzerstoerung or CommandOp.Pionierauftrag or CommandOp.Abwahl
            or CommandOp.Bahnschalter or CommandOp.FlugzeugKauf or CommandOp.Flugzeugstart
            or CommandOp.EinheitBauen or CommandOp.Aussenden or CommandOp.EntwurfAnlegen
            or CommandOp.Verwerten or CommandOp.BuildShip or CommandOp.RouteFach
            or CommandOp.RouteStarten or CommandOp.RouteAnhalten or CommandOp.Bahnverlegung
            or CommandOp.HeliKauf or CommandOp.BombeWechseln or CommandOp.FlugzeugVerwerten
            or CommandOp.Patrouille or CommandOp.Laden or CommandOp.Speichern or CommandOp.Pause
            or OpPanelzeile or OpStaffelmarke or OpSchonfrist or OpProbeHerrichten => true,
        _ => false,
    };

    /// <summary>Der Verteiler. Ein Satz eines ANDEREN Spielers bleibt stumm
    /// (kein Klang, kein Meldungsfenster, keine Auftragszeile).</summary>
    private bool ApplyDirekt2(in CommandRecord c)
    {
        bool fremd = c.Player != ViewPlayer;
        string merkOrder = _order;
        var merkMeldung = OnMeldung;
        bool merkStumm = Audio.SoundBankPlayer.Suppressed;
        if (fremd) { OnMeldung = null; Audio.SoundBankPlayer.Suppressed = true; }
        try
        {
            return c.Op switch
            {
                CommandOp.HandSchritt => ApplyHandSchritt(c),
                CommandOp.HandRohr => ApplyHandRohr(c),
                CommandOp.HandUebernehmen => ApplyHandUebernehmen(c),
                CommandOp.HandAbgeben or CommandOp.Abwahl => ApplyHandAbgeben(c),
                CommandOp.HandLuft => ApplyHandLuft(c),
                CommandOp.FireAt => ApplyFireAtBefehl(c),
                CommandOp.Selbstzerstoerung => ApplySelbstzerstoerung(c),
                CommandOp.Pionierauftrag => ApplyPionier(c),
                CommandOp.Bahnschalter => ApplyBahnschalter(c),
                CommandOp.FlugzeugKauf or CommandOp.EinheitBauen or CommandOp.BuildShip
                    or CommandOp.HeliKauf => ApplyProduktion(c),
                CommandOp.Flugzeugstart => ApplyFlugzeugstart(c),
                CommandOp.Aussenden => ApplyAussenden(c),
                CommandOp.Verwerten => ApplyVerwerten(c),
                OpPanelzeile => ApplyPanelzeile(c),
                CommandOp.EntwurfAnlegen => ApplyEntwurf(c),
                CommandOp.RouteFach => ApplyRouteFach(c),
                CommandOp.RouteStarten => ApplyRouteStarten(c),
                CommandOp.RouteAnhalten => ApplyRouteAnhalten(c),
                CommandOp.Bahnverlegung => ApplyBahnverlegung(c),
                CommandOp.BombeWechseln => ApplyBombeWechseln(c),
                CommandOp.FlugzeugVerwerten => ApplyFlugzeugVerwerten(c),
                CommandOp.Patrouille => ApplyPatrouille(c),
                OpStaffelmarke => ApplyStaffelmarke(c),
                OpSchonfrist => ApplySchonfrist(c),
                CommandOp.Pause => ApplyPause(c),
                CommandOp.Speichern => ApplySpeichern(c),
                CommandOp.Laden => ApplyLaden(c),
                OpProbeHerrichten => ApplyProbeHerrichten(c),
                _ => false,
            };
        }
        finally
        {
            if (fremd)
            {
                _order = merkOrder;
                OnMeldung = merkMeldung;
                Audio.SoundBankPlayer.Suppressed = merkStumm;
            }
        }
    }

    // ================= CHEATS UND ENTWÜRFE BEIM NETZSTART =====================

    /// <summary>Sind die Cheats gesperrt? Im Netz ja — wie im Original:
    /// <c>0x43AE54</c> fragt den Netzschalter <c>dword[0x539234]</c>, bevor eine
    /// Cheattaste wirkt. Bei uns kommt dazu: die drei Schalter sind STATISCH und
    /// hängen am Sichtspieler (<see cref="Cheated(Entity)"/>) — auf der anderen
    /// Maschine wirkten sie für den anderen Spieler oder gar nicht.</summary>
    public static bool CheatImNetzGesperrt() => ImNetz && !Alt2("cheat");

    /// <summary>
    /// Beim Netzstart (aus <see cref="NetzTaktUebernehmen"/>):
    /// <list type="number">
    /// <item>die Cheats AUS — sie überleben als statische Felder einen
    /// Einzelspielerlauf derselben Sitzung (Paket »cheat«);</item>
    /// <item>die EIGENEN ENTWÜRFE heraus (Slot −1, aus <c>user://designs.cfg</c>).
    /// Jede Maschine liest dort ihre eigene Datei; die Entwurfsliste hätte damit
    /// je Maschine eine andere Länge, und eine Entwurfsnummer im Bausatz hiesse
    /// auf der anderen Seite etwas anderes. Im Netz entsteht ein eigener Entwurf
    /// nur noch über den Ring (Befehl 505, Paket »entwurf«), also auf allen
    /// Maschinen im selben Takt an derselben Stelle. ⚠ SETZUNG: das Original
    /// hält Entwürfe je Spielerblock in sec47 (200 je Spieler) — wie es die
    /// Entwürfe eines Netzpartners kennt, ist über 505 gelesen, die Herkunft
    /// der Startliste nicht.</item>
    /// </list></summary>
    private void NetzStartDirektbefehle2()
    {
        if (!Alt2("cheat")) CheatGodMode = CheatAmmo = CheatFuel = false;
        if (!Alt2("entwurf") && _designs != null)
        {
            int weg = _designs.RemoveAll(d => d.Slot < 0);
            if (weg > 0) GD.Print($"Netzstart: {weg} eigene Entwürfe aus designs.cfg nicht im Netzspiel");
        }
    }
}
