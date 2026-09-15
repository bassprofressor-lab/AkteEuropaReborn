namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <c>--verbuendete-check</c> (14.09.2026, seine Meldung aus K14: verbuendete Einheiten
/// anvisierbar, nicht auf der Minikarte, nicht sichtbar). Lesung
/// <c>berichte/verbuendete-fable.md</c>. Braucht <c>--fog</c>.
///
/// <para>Gemessen fuer jeden verbuendeten Spieler: Aufdecker, die er beisteuert;
/// seine Einheiten sichtbar / gesamt; seine Punkte auf der Minikarte; der Zeiger ueber
/// seiner ersten Einheit und seinem ersten Gebaeude (mit gewaehlter eigener Einheit).
/// Soll: Aufdecker &gt; 0, alle lebenden Einheiten sichtbar, Minikarte &gt; 0, Zeiger
/// nicht Enemy/Einnahme. Nullmodelle <c>--verbuendete-unbeteiligt</c>,
/// <c>--zeiger-verbuendet-alt</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    public string VerbuendeteCheck()
    {
        var sb = new System.Text.StringBuilder("verbuendete-check\n");
        if (!FogActive) return sb.Append("  ⚠ ohne --fog sagt der Lauf NICHTS\n  DURCHGEFALLEN").ToString();
        UpdateFog();
        bool ok = true, einer = false;
        int eigene = -1;
        for (int k = 0; k < _entities.Count; k++)
        {
            var x = _entities[k];
            if (!x.IsBuilding && !x.IsProp && !x.Dead && x.Owner == ViewPlayer && x.Mobile) { eigene = k; break; }
        }
        var punkte = MinimapDots();
        for (int p = 0; p < 8; p++)
        {
            if (p == ViewPlayer || IsNeutralPlayer(p) || !Allied(ViewPlayer, p)) continue;
            int einheiten = 0, sichtbar = 0, aufdecker = 0, mini = 0;
            Entity? ersteE = null, ersteG = null;
            foreach (var e in _entities)
            {
                if (e.Dead || e.IsProp || e.Owner != p) continue;
                if (e.IsBuilding) { if (!e.NoStructure) ersteG ??= e; continue; }
                if (Untergestellt(e)) continue;
                einheiten++;
                ersteE ??= e;
                if (!ImNebelVerborgen(e)) sichtbar++;
            }
            if (einheiten == 0 && ersteG == null) continue;
            einer = true;
            foreach (var w in Watchers()) { _ = w; }
            foreach (var e in _entities)
                if (!e.Dead && !e.IsProp && e.Owner == p && DecktAuf(e.Owner)) aufdecker++;
            foreach (var d in punkte) if (d.Owner == p) mini++;

            string zE = "-", zG = "-";
            if (eigene >= 0)
            {
                _sel.Clear(); _sel.Add(eigene); _selected = eigene;
                if (ersteE != null) zE = CursorHintAt(ersteE.Pos).ToString();
                if (ersteG != null)
                {
                    var foot = BuildingFootprint(ersteG.BType);
                    var mitte = (CellCenter(ersteG.Col, ersteG.Row) + CellCenter(ersteG.Col + foot.X - 1, ersteG.Row + foot.Y - 1)) * 0.5f;
                    zG = CursorHintAt(mitte).ToString();
                }
                _sel.Clear(); _selected = -1;
            }
            bool sichtOk = aufdecker > 0 && sichtbar == einheiten && mini > 0;
            bool zeigerOk = zE != "Enemy" && zG is not ("Enemy" or "Einnahme");
            // ⚠ Nullmodelle werden NICHT umgedreht: mit Gegenschalter MUSS der Lauf durchfallen.
            bool soll = sichtOk && zeigerOk;
            ok &= soll;
            sb.Append($"  Spieler {p} verbuendet: Aufdecker {aufdecker}, Einheiten sichtbar {sichtbar}/{einheiten}, "
                    + $"Minikarte {mini} Punkte; Zeiger ueber Einheit {zE}, ueber Gebaeude {zG}  {(soll ? "ja" : "NEIN")}\n");
        }
        if (!einer) return sb.Append("  ⚠ kein verbuendeter Spieler mit Einheiten — sagt NICHTS\n  DURCHGEFALLEN").ToString();
        sb.Append($"  --verbuendete-unbeteiligt {VerbuendeteUnbeteiligt}, --zeiger-verbuendet-alt {ZeigerVerbuendetAlt}\n");
        return sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }
}
