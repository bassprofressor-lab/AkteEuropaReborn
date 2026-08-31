using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>--bodenangriff-probe — schiesst eine Einheit auf einen BAUM?</b>
///
/// <para>Gebaut am 31.08.2026 zusammen mit dem Bodenangriff. Der Fall ist im
/// normalen Lauf gar nicht zu bekommen: er hängt an einem Strg-Klick des
/// Spielers, und ein kopfloser Lauf klickt nicht. Also wird er gestellt —
/// dieselbe Bauweise wie <c>--ausweich-probe</c> und
/// <c>--aufgeben-probe</c>.</para>
///
/// <para><b>Der Aufbau:</b> eine eigene kampffähige Einheit wählen, in ihrer
/// Nähe eine Zelle mit unverbranntem Wald suchen, und den Bodenangriff darauf
/// absetzen — über <see cref="PostAttackGround"/>, also über den Befehlsbus
/// und nicht an ihm vorbei.</para>
///
/// <para><b>Die Messlatte steht vorher fest:</b></para>
/// <list type="bullet">
/// <item><c>bodenangriff: … befohlen</c> steigt — der Befehl kommt durch den
/// Bus (Busbefehl 11 mit <c>UTOK_NA = 30000 + Spalte</c>).</item>
/// <item><c>Schuesse</c> steigt — die Einheit feuert wirklich, statt den
/// Auftrag stillschweigend fallen zu lassen.</item>
/// <item>Am Boden passiert etwas: Wald angezündet oder Wald weg.</item>
/// </list>
///
/// <para>⚠ Ohne die dritte Zahl wäre »sie schiesst« nicht von »sie schiesst
/// ins Leere« zu unterscheiden — und genau das ist der Punkt, um den es geht:
/// der Baum ist kein Ziel, sondern Bewohner der Zelle.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    private int _bapProbe = -1, _bapSchuetze = -1;
    private float _bapUhr, _bapTicker;
    private Vector2I _bapZelle;
    private int _bapBefVor, _bapSchussVor, _bapFeuerVor, _bapWegVor;

    /// <summary>Wie lange gemessen wird. Eine Nachladezeit liegt bei rund einer
    /// Sekunde, sechs lassen also mehrere Schüsse zu.</summary>
    private const float BapSekunden = 22f;

    /// <summary><c>--bodenangriff-probe</c> starten.</summary>
    public void BodenangriffProbeStart() { _bapProbe = 0; }

    /// <summary>Eine eigene Einheit, die schiessen kann und fährt.</summary>
    private int BapSchuetzeSuchen()
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead) continue;
            if (e.Owner != ViewPlayer || !CanFight(e) || e.DugIn) continue;
            return i;
        }
        return -1;
    }

    /// <summary>Eine Waldzelle in Reichweite des Schützen — die nächste
    /// gewinnt, damit er nicht erst hinfahren muss.</summary>
    private Vector2I? BapWaldSuchen(Entity e)
    {
        // ⚠ NICHT auf die Reichweite einschraenken. Auf map_03 steht zu
        // Beginn kein Wald in Schussweite, und die Probe meldete »auf dieser
        // Karte nicht stellbar« — ein Befund ueber die Karte, der in Wahrheit
        // einer ueber meine Suche war. Der NAECHSTE Wald genuegt: liegt er zu
        // weit, faehrt die Einheit hin, und genau das gehoert mitgeprueft.
        Vector2I? beste = null; int besteEnt = int.MaxValue;
        foreach (var o in _objDraw)
        {
            if (!o.IstWald || o.Abgebrannt) continue;
            int d = Mathf.Max(Mathf.Abs(o.Col - e.Col), Mathf.Abs(o.Row - e.Row));
            if (d < besteEnt) { besteEnt = d; beste = new Vector2I(o.Col, o.Row); }
        }
        return beste;
    }

    private void PollBodenangriffProbe(float dt)
    {
        if (_bapProbe < 0 || _nav == null) return;
        switch (_bapProbe)
        {
            case 0:
                // Kurz warten, bis die Karte steht und die Objektebene liegt.
                _bapUhr -= dt;
                if (_bapUhr > 0f) return;
                _bapUhr = 5f; _bapProbe = 1;
                return;

            case 1:
            {
                _bapUhr -= dt;
                // ⚠ NICHT die erste Einheit nehmen, sondern das erste PAAR aus
                // Einheit und Wald in ihrer Reichweite. Der erste Anlauf nahm
                // Platz 1 und meldete »kein Wald in Reichweite« — das sah wie
                // ein Befund ueber die Karte aus und war einer ueber meine
                // Auswahl.
                _bapSchuetze = -1; Vector2I? wald = null;
                for (int k = 0; k < _entities.Count && _bapSchuetze < 0; k++)
                {
                    var k0 = _entities[k];
                    if (k0.IsBuilding || k0.IsProp || k0.Dead) continue;
                    if (k0.Owner != ViewPlayer || !CanFight(k0) || k0.DugIn) continue;
                    var t = BapWaldSuchen(k0);
                    if (t == null) continue;
                    _bapSchuetze = k; wald = t;
                }
                if (_bapSchuetze < 0 || wald == null)
                {
                    if (_bapUhr > 0f) return;
                    GD.Print("bodenangriff-probe: keine eigene kampffaehige Einheit mit Wald "
                           + "in Reichweite — auf dieser Karte nicht stellbar");
                    _bapProbe = -1; return;
                }
                var s = _entities[_bapSchuetze];
                _bapZelle = wald.Value;

                // Anwaehlen und den Befehl ueber den BUS absetzen, nicht daneben.
                _sel.Clear(); _sel.Add(_bapSchuetze);
                _bapBefVor = BodenBefohlen; _bapSchussVor = BodenSchuesse;
                _bapFeuerVor = BodenWaldFeuer; _bapWegVor = BodenWaldWeg;

                bool ab = PostAttackGround(ZellMitte(_bapZelle.X, _bapZelle.Y));
                GD.Print($"bodenangriff-probe AUFBAU: Platz {s.Slot} ({LabelOf(s.UnitType)}) "
                       + $"auf ({s.Col},{s.Row}), Waldzelle ({_bapZelle.X},{_bapZelle.Y}), "
                       + $"Abstand {Mathf.Max(Mathf.Abs(s.Col - _bapZelle.X), Mathf.Abs(s.Row - _bapZelle.Y))}, "
                       + $"Befehl abgesetzt: {(ab ? "ja" : "NEIN")}");
                GD.Print("   ERWARTET: befohlen steigt, Schuesse steigen, und am Boden "
                       + "brennt oder verschwindet Wald.");
                if (!ab) { _bapProbe = -1; return; }
                _bapUhr = BapSekunden; _bapTicker = 0f; _bapProbe = 2;
                return;
            }

            case 2:
            {
                _bapUhr -= dt; _bapTicker -= dt;
                if (_bapTicker <= 0f)
                {
                    // ⚠ Ohne die laufende Zeile ist »0 Schuesse« nicht von
                    // »die Einheit stand woanders« zu unterscheiden.
                    _bapTicker = 1f;
                    var d = _entities[_bapSchuetze];
                    GD.Print($"   bodenangriff-probe [t] Schuetze ({d.Col},{d.Row}) "
                           + $"Zielzelle {(d.AngriffsZelle is { } q ? $"({q.X},{q.Y})" : "-")} "
                           + $"Nachladen {d.Cooldown:0.00} Munition {d.Ammo}/{d.AmmoMax} | "
                           + $"Schuesse {BodenSchuesse} Feuer {BodenWaldFeuer} weg {BodenWaldWeg}");
                }
                if (_bapUhr > 0f) return;

                int dBef = BodenBefohlen - _bapBefVor, dSch = BodenSchuesse - _bapSchussVor;
                int dFeu = BodenWaldFeuer - _bapFeuerVor, dWeg = BodenWaldWeg - _bapWegVor;
                bool wirkung = dFeu > 0 || dWeg > 0;
                GD.Print($"bodenangriff-probe ERGEBNIS nach {BapSekunden:0} s: "
                       + $"befohlen +{dBef}, Schuesse +{dSch}, Wald angezuendet +{dFeu}, "
                       + $"Wald weg +{dWeg}  -> "
                       + (dBef == 0 ? "NICHTS BEFOHLEN (der Bus hat den Satz nicht angenommen)"
                        : dSch == 0 ? "NICHT GESCHOSSEN (Auftrag kam an, Einheit feuerte nicht)"
                        : wirkung ? "WIE ERWARTET (sie schiesst auf die Zelle, und der Wald merkt es)"
                                  : "GESCHOSSEN, ABER OHNE WIRKUNG am Boden"));
                _bapProbe = -1;
                return;
            }
        }
    }
}
