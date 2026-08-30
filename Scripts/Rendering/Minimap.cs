namespace AkteEuropaReborn.Rendering;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// The overview map: the whole battlefield at a glance, above the side panel.
///
/// The terrain costs nothing to draw — it is the baked map picture the viewer
/// already has loaded, shown small. On top of it go the units and buildings as
/// dots in their faction's colour, the camera's own view as a frame, and the
/// most recent incidents as alarm marks. Clicking jumps the camera there.
///
/// <para><b>Ours, and worth saying so.</b> The original has no permanent
/// minimap: its command bar offers "Karte des Einsatzgebietes zeigen"
/// (@0x4ef50c), a map screen you call up. The side panel's recessed box is the
/// info display and stays that. This sits above the panel instead of taking it
/// over, so nothing the original does was displaced to make room.</para>
/// </summary>
public partial class Minimap : Control
{
    /// <summary>An incident worth a mark: where, and how long it still glows.</summary>
    public readonly struct Alarm
    {
        public Alarm(Vector2 world, float age, bool lost) { World = world; Age = age; Lost = lost; }
        public Vector2 World { get; }
        public float Age { get; }
        /// <summary>A loss, as opposed to something merely under fire.</summary>
        public bool Lost { get; }
    }

    private Texture2D? _terrain;
    private Vector2 _mapPixels;                 // size of the baked picture
    private Func<List<(Vector2 Pos, int Owner, bool Building)>>? _dots;
    private Func<Rect2>? _view;
    private Func<List<Alarm>>? _alarms;
    private Action<Vector2>? _jump;

    /// <summary>The fog, one pixel per cell, or null when it is switched off —
    /// see <see cref="MapEntityLayer.FogTexture"/>.</summary>
    private Func<Texture2D?>? _fog;

    /// <summary>
    /// ⭐⭐⭐ 28.08.2026 — <b>DIE OBJEKTEBENE GEHOERT MIT IN DIE UEBERSICHT.</b>
    ///
    /// <para>Gemeldet: »einerseits war die stadt korrekt, aber da wo neutrale
    /// gebaeude stehen, waren die schattierungen in der minimap«. Die Ursache
    /// lag nicht hier und nicht im Backofen, sondern dazwischen: eine
    /// KULISSENZELLE traegt im Gitter den Gebaeudekachelcode selbst, damit ist
    /// sie eine Objektzelle, faellt in <c>MapBaker</c> weder in den flachen
    /// noch in den eigenen Durchgang — und ins Kartenbild wird dort NICHTS
    /// gemalt. Stehen bleibt die <c>BuildBase</c>-Flutfuellung, unsere
    /// Erfindung aus den acht Nachbarn.</para>
    ///
    /// <para>Auf dem Schlachtfeld sieht man das nie: die Kulissenkachel liegt
    /// als zweite Ebene darueber und deckt. Die Uebersicht bekam bisher nur
    /// <c>map_XX.png</c> — dort lag die Flutfuellung blank. Gemessen als
    /// Fleckkontrast gegen den Umgebungsring: 11,7 unter Spielergebaeuden,
    /// 13,1 unter gebauten neutralen (beides Nullmodellniveau 10,1), aber
    /// <b>30,2 unter Kulissenbauten</b>, 45 % davon ueber dem Nullmodell-P95.
    /// Genau »da wo neutrale gebaeude stehen«.</para>
    ///
    /// <para>⚠⚠ <b>Warum die Behebung NICHT im Backofen sitzt.</b> Der
    /// naheliegende Griff waere, die Kulissenkachel flach ins Kartenbild
    /// mitzubacken. Das ist genau die Aenderung, die am 24.08. gemeldet und
    /// zurueckgenommen wurde: »ich sehe schon Gebaeude im Fog of War
    /// trotzdem«. Das Kartenbild sieht man immer; was im Nebel verschwinden
    /// koennen muss, darf nicht darin liegen. Der Dreck wird darum dort
    /// zugedeckt, wo er sichtbar ist.</para>
    ///
    /// <para>Im Original stellt sich die Frage gar nicht: es kennt kein
    /// zusammengesetztes Bodenbild, sondern malt jedes Bild neu aus der
    /// bekannten Karte (<c>0x401410</c> je Zelle). An einer Kulissenzelle malt
    /// es genau EINE Kachel — die Kulissenkachel selbst, ueber den
    /// Gebaeudezweig des verzahnten Durchgangs (<c>0x4B44C6</c>: ausserhalb
    /// [60000,60300) normaler Draw; <c>IsBuilt == 0</c> springt auf denselben
    /// normalen Draw <c>0x4B4587</c>). Und sie deckt: ueber alle 41 Karten
    /// decken <b>6.550 von 6.611</b> Kulissenzellen (99,1 %) ihre 40x20-Box zu
    /// 100 %; nach Vereinigung mit den Nachbarkacheln bleiben 29 Zellen mit
    /// zusammen 88 Streupunkten, an denen auch das Original zeigt, was gerade
    /// dasteht.</para>
    ///
    /// <para>Die Ebene liegt UNTER dem Nebel, wie auf dem Schlachtfeld: was
    /// nicht erkundet ist, deckt der Nebel danach wieder zu. Rueckfall
    /// <c>--uebersicht-ohne-objekte</c>.</para></summary>
    private Func<Texture2D?>? _objekte;

    /// <summary><c>--uebersicht-ohne-objekte</c> — der Stand von vor dem
    /// 28.08.2026: die Uebersicht zeigt nur das Kartenbild.</summary>
    public static bool OhneObjektebene;

    /// <summary><c>--minikarte-nebel-einmal</c> — der Stand von vor dem
    /// 30.08.2026: die Uebersicht dunkelt so stark ab wie das Schlachtfeld.</summary>
    public static bool NebelEinmal;

    /// <summary><c>--minikarte-nebel-zweimal</c> — die Schicht ein zweites
    /// Mal, der Versuch vom 30.08.2026 mittags. Er hat nicht gereicht.</summary>
    public static bool NebelZweimal;

    /// <summary>Wie oft die Objektebene wirklich in die Uebersicht gezeichnet
    /// wurde. ⚠ Eigene Zahl neben <see cref="Repaints"/>: eine Ebene, die nie
    /// ankommt, sieht sonst genauso aus wie eine, die nichts aendert.</summary>
    public int ObjektebeneGezeichnet;

    /// <summary>
    /// DER EIGENE STARTPLATZ, in Kartenbildpunkten — oder null.
    ///
    /// <para>Gewuenscht als B7: »Bei Gefecht Startplatz wuerde ich gerne sehen
    /// wo dieser ist auf der Minimap.« Der Punkt steht FEST, er wandert nicht
    /// mit der Basis mit: er wird beim Start einmal genommen und bleibt, auch
    /// wenn die Basis faellt. Genau das ist der Zweck — man will wissen, wo man
    /// hergekommen ist.</para>
    ///
    /// <para>⚠ UNSERE ZUTAT, wie die Minimap selbst. Das Original hat keine
    /// stehende Uebersichtskarte (siehe Kopf dieser Datei), also auch keinen
    /// Startplatzmerker darauf.</para>
    /// </summary>
    private Func<Vector2?>? _home;

    /// <summary>Wie oft der Startplatzmerker gezeichnet wurde — fuer den
    /// Pruefstand, damit »gebaut« und »sichtbar« zwei Zahlen sind.</summary>
    public int HomeDrawn;

    /// <summary>How long a mark keeps glowing. Ours — the data says nothing
    /// about how long an alarm should last.</summary>
    public const float AlarmSeconds = 12f;

    public void Setup(Texture2D terrain, Vector2 mapPixels,
                      Func<List<(Vector2 Pos, int Owner, bool Building)>> dots,
                      Func<Rect2> view, Func<List<Alarm>> alarms, Action<Vector2> jump,
                      Func<Texture2D?>? fog = null, Func<Vector2?>? home = null,
                      Func<Texture2D?>? objekte = null)
    {
        _objekte = objekte;
        _home = home;
        _terrain = terrain;
        _mapPixels = mapPixels;
        _dots = dots;
        _view = view;
        _alarms = alarms;
        _jump = jump;
        _fog = fog;
        QueueRedraw();
    }

    /// <summary>The height this map wants for a given width, so the caller can
    /// lay it out without knowing the map's shape.</summary>
    public float HeightFor(float width)
        => _mapPixels.X <= 0 ? 0 : Mathf.Round(width * _mapPixels.Y / _mapPixels.X);

    private Vector2 ToLocal(Vector2 world)
        => _mapPixels.X <= 0 || _mapPixels.Y <= 0
            ? Vector2.Zero
            : new Vector2(world.X / _mapPixels.X * Size.X, world.Y / _mapPixels.Y * Size.Y);

    private Vector2 ToWorld(Vector2 local)
        => Size.X <= 0 || Size.Y <= 0
            ? Vector2.Zero
            : new Vector2(local.X / Size.X * _mapPixels.X, local.Y / Size.Y * _mapPixels.Y);

    /// <summary>What was drawn last time, so the map only repaints when there is
    /// something new to show.
    ///
    /// ⚠ It used to repaint <b>never</b>: <see cref="Setup"/> queued one redraw
    /// and nothing ever queued another, so the view frame stayed wherever the
    /// camera had been when the map loaded and the dots stayed with it. Reported
    /// as "Mini Map bleibt immer auf dem Ursprungspunkt stehen", and that is
    /// exactly what it was.</summary>
    private Rect2 _lastView;
    private float _dotTimer;

    /// <summary>How often the dots are refreshed. Ours: the frame follows the
    /// camera immediately because that is what the eye tracks, while units move
    /// slowly enough that five times a second is plenty and costs nothing.</summary>
    private const float DotRefresh = 0.2f;

    /// <summary>Harness counters: how often the camera moved and how often the
    /// map actually repainted. Before the fix the second one stayed at 1 no
    /// matter what the first did, which is the whole of the bug.</summary>
    public int ViewMoves { get; private set; }
    public int Repaints { get; private set; }

    /// <summary>How often the fog was actually drawn over the overview. Zero
    /// while the fog is switched off, and zero would also be the symptom if the
    /// texture never arrived — hence the counter.</summary>
    public int FogDrawn { get; private set; }

    public override void _Process(double delta)
    {
        if (_terrain == null) return;
        bool due = false;
        if (_view != null)
        {
            var r = _view();
            if (r != _lastView) { _lastView = r; due = true; ViewMoves++; }
        }
        _dotTimer -= (float)delta;
        if (_dotTimer <= 0f) { _dotTimer = DotRefresh; due = true; }
        if (due) QueueRedraw();
    }

    public override void _Draw()
    {
        if (_terrain == null) return;
        Repaints++;
        var full = new Rect2(Vector2.Zero, Size);

        DrawTextureRect(_terrain, full, false, new Color(0.75f, 0.78f, 0.8f));

        // Die zweite Ebene darueber, in derselben Graustufung: sie traegt die
        // Kulissenbauten, die im Kartenbild nur ihre Flutfuellung
        // zuruecklassen. Herleitung und Messung bei _objekte.
        //
        // ⚠ Auf die Kartengroesse zuschneiden — unten an der Ebene haengt der
        // Streifen mit den verkohlten Baeumen (MapBaker.BurntAtlas), der sonst
        // in die Uebersicht gestaucht wird.
        var obj = OhneObjektebene ? null : _objekte?.Invoke();
        if (obj != null && _mapPixels.X > 0 && _mapPixels.Y > 0)
        {
            DrawTextureRectRegion(obj, full, new Rect2(Vector2.Zero, _mapPixels),
                                  new Color(0.75f, 0.78f, 0.8f));
            ObjektebeneGezeichnet++;
        }

        // The fog, over the terrain but UNDER the dots and the frame: what one
        // remembers of the ground is dimmed, what one is watching right now
        // stays bright, and what was never seen is black. The dots are drawn on
        // top because the entity layer already decides which of them may be
        // shown at all — dimming them a second time here would hide friendly
        // units in their own remembered territory.
        //
        // Fehlerliste Punkt 23. Nothing new is computed: this is the map's own
        // fog texture, one pixel per cell, stretched over the overview.
        var fog = _fog?.Invoke();
        if (fog != null)
        {
            DrawTextureRect(fog, full, false);
            FogDrawn++;
            // ⭐⭐⭐ 30.08.2026 — DIE UEBERSICHT DUNKELT ZWEIMAL AB, DAS
            // SCHLACHTFELD EINMAL. Gemeldet: »die Minikarte zeigt noch die
            // neutralen Gebaeude an im Fog of War (wenn noch nicht
            // erkundet)«.
            //
            // Das schien im Widerspruch zu seiner Ansage vom 18.08. zu stehen
            // (»die ganze Karte ist sichtbar, jedoch mit einem leichten Nebel
            // bedeckt«), aus der MapEntityLayer.FogDim = 0,50 kommt. Beide
            // Aussagen stimmen — das Original behandelt die zwei Orte
            // verschieden, und das steht im Uebersichtsmaler:
            //
            //   0x4B8296  ecx = Nebelbyte dieser Zelle
            //   0x4B829A  cmp byte[ecx], 0 ; jne  -> erkundet, KEINE Abdunklung
            //   0x4B829F  al = Merkbit [esp+0x2c] ; jne -> KEINE Abdunklung
            //   0x4B82A7  cl = tab[dl] ; dl = tab[cl]     <- ZWEIMAL durch die
            //             CWS-Schattentafel 0xB135B0
            //
            // ⭐ Und das Merkbit setzen NUR die drei Gebaeudezweige mit
            // `Built != 0` (@0x4B81C1, @0x4B81D7, @0x4B81E8). Der Zweig fuer
            // eine KULISSE — `byte[+0x18] == 0`, also genau die neutralen
            // Zivilbauten — setzt es NICHT (@0x4B81F9 schreibt nur die
            // Festfarbe 42). Ein echtes Gebaeude leuchtet also auch im Nebel
            // durch, eine Kulisse wird abgedunkelt wie der Boden. Genau das
            // beschreibt er.
            //
            // ⭐ Umgesetzt wird es woertlich: dieselbe Schicht ein zweites Mal.
            // Zwei Durchgaenge mit derselben Deckkraft ergeben 1-(1-a)^2, bei
            // FogDim 0,50 also 75 % statt 50 % — KEINE neue Zahl, und wer an
            // FogDim dreht, dreht die Uebersicht mit. Unsere Punkte
            // (MinimapDots) liegen darueber und leuchten weiter durch; das ist
            // die Entsprechung des Merkbits.
            // ⚠⚠ 30.08.2026 ZURUECKGENOMMEN: hier stand ein zweites
            // DrawTextureRect derselben Schicht ("zweimal durch die
            // Schattentafel", @0x4B82A7). Die Lesung stimmt, die Behebung
            // nicht — er meldete danach dieselben Kulissen wieder
            // ("in Kampagne1 sind auch noch die neutralen gebaeude im fog
            // of war geprinted"). Der Unterschied ist nicht die
            // HELLIGKEIT, sondern WAS gezeigt wird: das Original hat im
            // Unerkundeten kein Gedaechtnis und malt dort gar kein Objekt.
            // Das leistet jetzt die eigene Nebelschicht der Uebersicht,
            // siehe MapEntityLayer.FogTextureUebersicht. Der Schalter
            // --minikarte-nebel-einmal bleibt als Gegenprobe stehen.
            if (NebelZweimal) { DrawTextureRect(fog, full, false); FogDrawn++; }
        }

        DrawRect(full, new Color(0.55f, 0.58f, 0.6f), false, 1);

        if (_dots != null)
            foreach (var (pos, owner, building) in _dots())
            {
                var c = MapEntityLayer.FactionColor(owner);
                var p = ToLocal(pos);
                float s = building ? 5 : 3;
                DrawRect(new Rect2(p - new Vector2(s, s) * 0.5f, new Vector2(s, s)), c);
            }

        if (_alarms != null)
            foreach (var a in _alarms())
            {
                float t = 1f - Mathf.Clamp(a.Age / AlarmSeconds, 0f, 1f);
                if (t <= 0f) continue;
                // a ring that widens as it fades, so the eye finds it moving
                float r = 3f + (1f - t) * 7f;
                var c = a.Lost ? new Color(1f, 0.25f, 0.2f, t) : new Color(1f, 0.85f, 0.3f, t);
                DrawArc(ToLocal(a.World), r, 0, Mathf.Tau, 16, c, 1.5f);
            }

        // Der eigene Startplatz (B7). Er liegt UNTER dem Sichtfenster und UEBER
        // den Punkten: ein Merker, den ein Einheitenpunkt verdecken kann, ist
        // genau dann weg, wenn man ihn sucht — naemlich wenn dort etwas steht.
        // Form: eine Raute mit dunklem Rand, damit sie auf hellem wie auf
        // dunklem Gelaende steht, plus ein kurzer Stiel nach unten.
        if (_home?.Invoke() is { } h)
        {
            var p = ToLocal(h);
            const float R = 5f;
            var pts = new[]
            {
                p + new Vector2(0, -R), p + new Vector2(R, 0),
                p + new Vector2(0, R), p + new Vector2(-R, 0),
            };
            var outline = new[] { pts[0], pts[1], pts[2], pts[3], pts[0] };
            DrawPolygon(pts, new[] { new Color(1f, 1f, 1f, 0.9f) });
            DrawPolyline(outline, new Color(0.1f, 0.1f, 0.12f, 0.95f), 1.5f);
            HomeDrawn++;
        }

        if (_view != null)
        {
            var r = _view();
            var tl = ToLocal(r.Position);
            var br = ToLocal(r.End);
            DrawRect(new Rect2(tl, br - tl), new Color(1, 1, 1, 0.85f), false, 1);
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            _jump?.Invoke(ToWorld(mb.Position));
            AcceptEvent();
        }
        else if (@event is InputEventMouseMotion { ButtonMask: MouseButtonMask.Left } mm)
        {
            _jump?.Invoke(ToWorld(mm.Position));     // dragging scrubs the view
            AcceptEvent();
        }
    }
}
