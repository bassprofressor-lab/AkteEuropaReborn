namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;
using AkteEuropaReborn.Import;

/// <summary>
/// DIE SCHNITTSTELLE DES BEARBEITUNGSMODUS ZUM ZEICHNER — ein eigener Teil von
/// <see cref="MapEntityLayer"/>, der in <c>Scripts/Editor/</c> liegt.
///
/// <para><b>Warum diese Datei hier liegt und nicht bei den anderen Teilen der
/// Klasse.</b> An <c>Scripts/Rendering/MapEntityLayer.cs</c> arbeitet gerade ein
/// anderer Agent (Regel 19: Agenten nach DATEIHOHEIT trennen). Die Klasse ist
/// <c>partial</c> und hat ihre Teile ohnehin schon ueber drei Verzeichnisse
/// verstreut — <c>Simulation/Capture.cs</c>, <c>Simulation/RailFreight.cs</c>,
/// <c>Simulation/Commands/CommandBridge.cs</c>. Ein weiterer Teil kostet nichts
/// und vermeidet den Zusammenstoss.</para>
///
/// <para><b>Was hier steht und was nicht.</b> Nur die Handvoll Zugriffe, die der
/// <see cref="Editor.MapEditOverlay"/> braucht: Zelle unter dem Zeiger,
/// Bildpunkt einer Zelle, und ein LEBENDES Gebaeude nachtragen. Die
/// Bearbeitungslogik selbst steht drueben im Editor — diese Datei ist die
/// Durchreiche, nicht der Editor.</para>
///
/// <para>⚠ <b>Warum ein Gebaeude live nachgetragen wird, ein Gegenstand aber
/// nicht.</b> Ein Gebaeude ist im Kartenbild GAR NICHT enthalten: der Backofen
/// laesst seine Zellen aus, damit eine zerstoerte Ruine zeigen kann, und der
/// Zeichner malt es aus dem Muster (<c>MapBaker.BuildingCells</c>,
/// <c>DrawBuildingBody</c>). Ein neu gesetztes Gebaeude steht deshalb sofort da,
/// sobald sein Satz in der Liste liegt. Ein Gegenstand und ein Gleis sind
/// dagegen BILD — sie sind in die Karte gebacken. Fuer sie zeigt der Editor eine
/// Vorschau und backt beim Speichern neu; alles andere hiesse, eine zweite
/// Zeichenroutine neben dem Backofen zu fuehren, die mit ihm auseinanderlaufen
/// kann.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Der Bildpunkt der linken oberen Ecke einer Zelle, mit Hoehe —
    /// dieselbe Rechnung, die <c>MapBaker.Blit</c> benutzt.</summary>
    public Vector2 EditorCellTopLeft(int col, int row)
        => new(_ox + col * MapBaker.TileW,
               _oy + row * MapBaker.TileH - ElevOf(col, row) * MapBaker.ElevStep);

    /// <summary>Die Hoehe einer Zelle, wie der Zeichner sie kennt.</summary>
    public int EditorElev(int col, int row) => ElevOf(col, row);

    /// <summary>Wie viele Gebaeudesaetze die geladene Karte fuehrt — der
    /// Editor zaehlt seine neuen Plaetze dahinter weiter.</summary>
    public int EditorBuildingCount
    {
        get
        {
            int n = 0;
            foreach (var e in _entities) if (e.IsBuilding) n++;
            return n;
        }
    }

    /// <summary>
    /// Ein eben gesetztes Gebaeude in die lebende Liste nachtragen, damit es
    /// ohne Neuladen dasteht.
    ///
    /// <para>Die Felder sind die, die <see cref="DrawBuildingBody"/> und die
    /// Auswahl brauchen. <c>HpMax</c> wird mitgesetzt, weil die Schadensstufe
    /// <c>(hp_max − hp) / (hp_max / musterzahl)</c> sonst durch null teilt und
    /// das Gebaeude als Ruine zeichnen wuerde.</para>
    /// </summary>
    public void EditorAddBuilding(int slot, int typ, int owner, int col, int row,
                                  int footW, int footH, int hp)
    {
        int el = ElevOf(col, row);
        _entities.Add(new Entity
        {
            Slot = slot, BType = typ, IsBuilding = true, IsProp = false,
            Col = col, Row = row, Owner = owner, Team = owner,
            Hp = hp, HpMax = hp, Elev = el, UnitType = -1, Category = -1,
            Footprint = new Rect2(_ox + col * MapBaker.TileW,
                                  _oy + row * MapBaker.TileH - el * MapBaker.ElevStep,
                                  footW * MapBaker.TileW, footH * MapBaker.TileH),
        });
        QueueRedraw();
    }

    /// <summary>Das zuletzt nachgetragene Gebaeude wieder wegnehmen — der
    /// Ruecknahmeknopf des Editors. Sucht von hinten, damit ein aus der Karte
    /// geladenes nie getroffen wird, solange ein selbst gesetztes da ist.</summary>
    public bool EditorRemoveBuilding(int slot)
    {
        for (int i = _entities.Count - 1; i >= 0; i--)
            if (_entities[i].IsBuilding && _entities[i].Slot == slot)
            { _entities.RemoveAt(i); QueueRedraw(); return true; }
        return false;
    }

    /// <summary>Das Gebaeude, dessen Grundflaeche diese Zelle enthaelt — fuer
    /// »wegnehmen, worauf ich zeige«.</summary>
    public int EditorBuildingAt(Vector2I cell)
    {
        var p = Patterns;
        for (int i = _entities.Count - 1; i >= 0; i--)
        {
            var e = _entities[i];
            if (!e.IsBuilding) continue;
            int dx = cell.X - e.Col, dy = cell.Y - e.Row;
            if (dx < 0 || dy < 0 || dx >= CwpFile.PatternWidth || dy >= CwpFile.PatternHeight) continue;
            if (p == null) return i;
            var bt = p.GetBuildingType(e.BType);
            if (bt.IsEmpty) continue;
            if (p.PatternTile(bt.FirstPattern, dx, dy) != 0) return i;
        }
        return -1;
    }

    /// <summary>Die Namen der Gebaeudearten, die dieser Kachelsatz traegt —
    /// damit die Auswahlliste des Editors nicht raet, was es gibt.</summary>
    public List<int> EditorBuildingTypes()
    {
        var list = new List<int>();
        var p = Patterns;
        if (p == null) return list;
        for (int t = 0; t < CwpFile.BuildingTypeCount; t++)
            if (!p.GetBuildingType(t).IsEmpty) list.Add(t);
        return list;
    }

    /// <summary>Die Grundflaeche einer Art, aus ihrem Grundmuster gelesen.</summary>
    public (int W, int H) EditorFootprint(int typ)
    {
        var p = Patterns;
        if (p == null) return (1, 1);
        var bt = p.GetBuildingType(typ);
        if (bt.IsEmpty) return (1, 1);
        int w = 0, h = 0;
        for (int dx = 0; dx < CwpFile.PatternWidth; dx++)
            for (int dy = 0; dy < CwpFile.PatternHeight; dy++)
                if (p.PatternTile(bt.FirstPattern, dx, dy) != 0)
                { if (dx + 1 > w) w = dx + 1; if (dy + 1 > h) h = dy + 1; }
        return (w == 0 ? 1 : w, h == 0 ? 1 : h);
    }

    /// <summary>Die Zellen des Grundmusters einer Art — der Editor prueft jede
    /// einzeln, so wie <c>can_build_here</c> @0x4203C0 es tut.</summary>
    public List<(int Dx, int Dy)> EditorFootCells(int typ)
    {
        var list = new List<(int, int)>();
        var p = Patterns;
        if (p == null) return list;
        var bt = p.GetBuildingType(typ);
        if (bt.IsEmpty) return list;
        for (int dx = 0; dx < CwpFile.PatternWidth; dx++)
            for (int dy = 0; dy < CwpFile.PatternHeight; dy++)
                if (p.PatternTile(bt.FirstPattern, dx, dy) != 0) list.Add((dx, dy));
        return list;
    }
}
