using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>Die Spielseite des <c>--generator-check</c> (13.09.2026), siehe
/// Rendering/GeneratorLauf.cs.</summary>
public partial class MapEntityLayer
{
    private int _genBauer = -1;
    private readonly System.Collections.Generic.List<Vector2I> _genSperre = new();

    /// <summary>EINGRIFF: einen Generatorenbauer (Bauteil 74) neben eine eigene Einheit
    /// setzen und ihm den Auftrag auf seine Standzelle geben — wie Knopf 19 +
    /// Kartenklick nach der Ankunft.</summary>
    public string GeneratorProbeSetzen()
    {
        LoadDesigns();
        if (_nav == null || _designs == null) return "keine Karte";
        Design? d = null;
        foreach (var x in _designs) if (x.Weapon == PartGeneratorTech && x.Slot >= 0) { d = x; break; }
        if (d == null) return "kein Entwurf mit Bauteil 74";
        Entity? eigene = null;
        foreach (var e in _entities) if (!e.IsBuilding && !e.Dead && e.Owner == ViewPlayer && e.Mobile) { eigene = e; break; }
        if (eigene == null) return "keine eigene Einheit";
        // eine Stelle suchen, an der der Generator stehen kann
        for (int r = 6; r < 60; r++)
        {
            var z = _nav.NearestFree(new Vector2I(eigene.Col + r, eigene.Row + r / 2));
            if (z == null) continue;
            if (Patterns == null || !CanBuild(Patterns, TypeGenerator, z.Value.X - 1, z.Value.Y - 1, -1, null)) continue;
            int slot = SpawnAusEntwurf(d.Value, d.Value.Slot % 200, z.Value.X, z.Value.Y, ViewPlayer);
            if (slot < 0) continue;
            _genBauer = _entities.Count - 1;
            var u = _entities[_genBauer];
            u.BuildOrder = OrderGenerator;
            u.BuildTarget = (u.Row << 8) | u.Col;
            return $"\"{d.Value.Name}\" auf ({u.Col},{u.Row}), Auftrag Generator auf die Standzelle";
        }
        return "keine baubare Stelle gefunden";
    }

    /// <summary>EINGRIFF: den Fussabdruck mit fremder Belegung sperren bzw. freigeben.</summary>
    public void GeneratorProbeSperren(bool an)
    {
        if (_nav == null || _genBauer < 0) return;
        var u = _entities[_genBauer];
        int sperrer = -1;
        for (int i = 0; i < _entities.Count; i++) if (_entities[i].IsBuilding && !_entities[i].Dead) { sperrer = i; break; }
        if (an)
        {
            _genSperre.Clear();
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    int c = u.Col + dx, r = u.Row + dy;
                    if ((dx == 0 && dy == 0) || _nav.OccupantAt(c, r) >= 0) continue;
                    _nav.SetOccupant(c, r, sperrer, immobile: true);
                    _genSperre.Add(new Vector2I(c, r));
                }
        }
        else
        {
            foreach (var z in _genSperre) _nav.ClearOccupant(z.X, z.Y, sperrer);
            _genSperre.Clear();
        }
    }

    public (bool AuftragDa, Entity? Generator) GeneratorProbeStand()
    {
        bool auftrag = _genBauer >= 0 && !_entities[_genBauer].Dead && _entities[_genBauer].BuildOrder == OrderGenerator;
        Entity? g = null;
        foreach (var e in _entities) if (e.IsBuilding && !e.Dead && e.BType == TypeGenerator && e.Owner == ViewPlayer) g = e;
        return (auftrag, g);
    }

    /// <summary>Ein Schuss auf den Generator — über ApplyHit, mit einem Schützen.</summary>
    public int GeneratorProbeBeschiessen(Entity g)
    {
        int gi = _entities.IndexOf(g), si = -1;
        for (int i = 0; i < _entities.Count; i++)
            if (!_entities[i].IsBuilding && !_entities[i].Dead && _entities[i].Attack > 0) { si = i; break; }
        int vor = g.Hp;
        ApplyHit(si, gi, g, 200, "generator-check");
        return vor - g.Hp;
    }

    public Vector2 GeneratorProbeKamera(Entity g) => new(_ox + (g.Col + 2) * TileW, _oy + (g.Row + 2) * TileH);

    public string GeneratorProbeMuster(Entity g)
    {
        if (Patterns == null) return "keine Muster";
        var bt = Patterns.GetBuildingType(g.BildArt);
        string Kacheln(int p)
        {
            int n = 0;
            for (int dx = 0; dx < Import.CwpFile.PatternWidth; dx++)
                for (int dy = 0; dy < Import.CwpFile.PatternHeight; dy++)
                    if (Patterns.PatternTile(p, dx, dy) != 0) n++;
            return $"{p}:{n}";
        }
        var sb = new System.Text.StringBuilder($"BType {g.BType} BildArt {g.BildArt} First {bt.FirstPattern} Count {bt.PatternCount} Tile {bt.TilePattern}; Kacheln ");
        for (int p = bt.FirstPattern; p < bt.FirstPattern + bt.PatternCount; p++) sb.Append(Kacheln(p)).Append(' ');
        sb.Append("| Geruest ");
        for (int k = 0; k < 3; k++) sb.Append(Kacheln(bt.TilePattern - 2 + k)).Append(' ');
        sb.Append($"| Zelle ({g.Col},{g.Row}) Pos {g.Pos}");
        return sb.ToString();
    }

    public void GeneratorProbeAnklicken(Entity g) => PostenAnwaehlenWieKlick(_entities.IndexOf(g));
}
