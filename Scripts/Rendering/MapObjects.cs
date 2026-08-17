namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;
using GDict = Godot.Collections.Dictionary<string, Godot.Variant>;

/// <summary>
/// <b>AUFRAGENDE KARTENOBJEKTE — Bäume, Masten, Felsen.</b>
///
/// <para>⚠⚠ 18.08.2026, gemeldet als »im Original verdecken z. B. auch Bäume
/// Einheiten, bei uns nicht«. Der Bericht stimmt, und die Ursache lag im
/// Backofen: <c>MapBaker.Bake</c> Durchgang C backte <b>alle</b> Objekte ins
/// Kartenbild. Nur GEBÄUDE waren ausgenommen und wurden lebend gezeichnet —
/// genau deshalb konnten die verdecken und Bäume nicht. Ein eingebackener Baum
/// liegt unter allem, was danach kommt.</para>
///
/// <para><b>Die Kur ist die der Gebäudekacheln</b>: flach bleibt im Boden,
/// Aufragendes kommt ins Zeilenfach. Der Backofen schreibt die aufragenden
/// Objekte seither in eine zweite Ebene <c>&lt;karte&gt;.objects.png</c> (nur
/// sie, alles andere durchsichtig) und ihre Rechtecke in die Meta unter
/// <c>objects</c>. Dieser Durchgang schneidet sie dort aus und setzt sie
/// zwischen die Einheiten.</para>
///
/// <para><b>Die Schwelle ist gemessen</b>, siehe <c>MapBaker.RagtAbPx</c>: über
/// 36 Karten und 13.491 verschiedene Objektbilder sitzt bei 20 px — genau der
/// Zellhöhe — der grösste Haufen (7228 Bilder, 40.582 Zellen); darüber läuft
/// die Verteilung bis 70 px durch. 79.925 von 125.116 Objektzellen ragen
/// auf.</para>
///
/// <para>⚠ <b>Eine Karte aus einem älteren Import hat die zweite Ebene
/// nicht.</b> Dann bleibt alles wie vorher: die Bäume stehen im Kartenbild und
/// verdecken nichts. Das ist Absicht — ein fehlendes Bild darf kein Loch in die
/// Karte reissen. Wer die Verdeckung will, spielt die Karten neu ein
/// (<c>--reexport-maps=&lt;ordner&gt;</c>).</para>
/// </summary>
public partial class MapEntityLayer
{
    private Texture2D? _objTex;

    /// <summary>Je aufragendem Objekt: seine Zelle (fürs Zeilenfach) und sein
    /// Rechteck in der zweiten Ebene. Nach Zeile sortiert, damit der Zeichner
    /// nur durchlaufen muss.</summary>
    private readonly List<(int Row, Rect2 Src)> _objDraw = new();

    /// <summary>Wie viele aufragende Objekte der letzte Durchgang gezeichnet
    /// hat — ⚠ Regel 33: ohne diese Zahl ist »kein Unterschied im Bild« nicht
    /// von »der Durchgang lief gar nicht« zu unterscheiden.</summary>
    public int ObjectsDrawn { get; private set; }

    /// <summary>Wie viele in der Karte stehen (unabhängig davon, wie viele
    /// gerade im Bild sind).</summary>
    public int ObjectsLoaded => _objDraw.Count;

    /// <summary><c>--keine-objekt-verdeckung</c> — die Gegenprobe: der Stand von
    /// vor dem 18.08.2026, alles im Boden.</summary>
    public static bool NoObjectOcclusion;

    /// <summary>Die zweite Ebene und ihre Rechtecke aus der Meta holen.</summary>
    private void LoadObjectLayer(GDict meta, string mapName)
    {
        _objTex = null;
        _objDraw.Clear();
        if (NoObjectOcclusion) return;

        string p = Core.Content.Path($"Maps/{mapName}.objects.png");
        if (ResourceLoader.Exists(p)) _objTex = ResourceLoader.Load<Texture2D>(p);
        if (_objTex == null && FileAccess.FileExists(p))
        {
            // Eingespielte Inhalte gehen nicht durch Godots Importschritt —
            // dieselbe Doppelung wie beim Kartenbild selbst.
            var im = Image.LoadFromFile(ProjectSettings.GlobalizePath(p));
            if (im != null) _objTex = ImageTexture.CreateFromImage(im);
        }
        if (_objTex == null) return;

        if (!meta.TryGetValue("objects", out var ov) || ov.VariantType != Variant.Type.Array) return;
        foreach (var item in ov.AsGodotArray())
        {
            if (item.VariantType != Variant.Type.Dictionary) continue;
            var o = item.AsGodotDictionary<string, Variant>();
            _objDraw.Add((GetI(o, "row"),
                          new Rect2(GetI(o, "x"), GetI(o, "y"), GetI(o, "w"), GetI(o, "h"))));
        }
        // ⚠ Nach ZEILE sortieren, nicht nach Lage im Bild: das Zeilenfach
        // entscheidet, was vor wem liegt, und der Backofen liefert schon in
        // Zeilenfolge — die Sortierung ist die Zusicherung, nicht die Arbeit.
        _objDraw.Sort((a, b) => a.Row - b.Row);
        GD.Print($"objekte: {_objDraw.Count} aufragende Kartenobjekte aus " +
                 $"{mapName}.objects.png — sie verdecken jetzt Einheiten");
    }

    /// <summary>Alles bis einschliesslich dieser Zeile zeichnen. Genauso
    /// gebaut wie <c>DrawRailUpTo</c> und <c>DrawUnitsUpTo</c>: der Zeiger läuft
    /// mit, jeder Eintrag kommt genau einmal dran.
    ///
    /// <para>⚠ Die Zeile eines Objekts ist seine ZELLE, ohne Zuschlag. Ein
    /// Gebäude bekommt <c>+3</c> bzw. <c>+tür0.row</c> und ein Gleis <c>+2</c>
    /// (beides gelesen); für ein Objekt ist im Original nichts dergleichen
    /// belegt, also steht hier keine Zahl. ⚠ UNSERE SETZUNG insofern, als das
    /// Original diese Objekte gar nicht in ein Fach einreiht — es zeichnet sie
    /// in einem Durchgang von hinten nach vorn, und Einheiten laufen im selben
    /// Fachwerk mit.</para></summary>
    private void DrawObjectsUpTo(int throughRow, ref int at)
    {
        if (_objTex == null) return;
        for (; at < _objDraw.Count && _objDraw[at].Row <= throughRow; at++)
        {
            var s = _objDraw[at].Src;
            DrawTextureRectRegion(_objTex, new Rect2(s.Position, s.Size), s);
            ObjectsDrawn++;
        }
    }
}
