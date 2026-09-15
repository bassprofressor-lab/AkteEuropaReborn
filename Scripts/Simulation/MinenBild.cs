namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// ⭐⭐ <b>WAS MAN VON EINER MINE SIEHT</b> (15.09.2026, Kampagne 15). Seine Ansage
/// »man sieht die Minen so gut wie nicht« — und das IST das Original
/// (berichte/minen-opus.md §2, beide GAME.EXE).
///
/// <para><b>Zeichenliste</b> <c>0x42F2B0</c> (F <c>0x42E470</c>): jede belegte Mine,
/// deren Zelle gerade SICHTBAR ist (sec50, ohne Besitzerfrage), kommt in den Korb
/// <c>Zeile + 2</c>, an
/// <c>sx = 40·x − 20 + byte[+2]</c>, <c>sy = 20·y − 20 + byte[+3] − Höhe(x, y, +2, +3)</c>.</para>
///
/// <para><b>Zeichner Art 15</b> <c>0x42CEAD</c> (F <c>0x42C08C</c>):
/// <list type="bullet">
/// <item><c>T[lokal, Leger] != 0</c> (eigene/verbündete) → Kopierer <c>0x4ACCD0</c>,
/// Bild = Folge 93 + Geländeklasse (<c>0x41D110</c>) — eine graue Scheibe;</item>
/// <item>sonst <c>rand() % 1000 == 333</c> → Schattenblitter <c>0x4AC6D0</c> mit dem
/// Umriss aus Folge 93, andernfalls Schattenblitter mit Folge 73 — <b>ein
/// 2×2-Punkt</b>.</item>
/// </list></para>
///
/// <para>Die Bildlage ist die des Frachters (Frachter.cs): der Kartenpunkt
/// <c>(_ox + 40·x, _oy + 20·y)</c> entspricht dem Bildschirmpunkt des Originals, die
/// PNG trägt den y-Versatz des Rahmens (InterfaceExporter.Canvas).</para>
///
/// <para>⚠ <b>UNSERE Setzungen:</b> (1) der Würfel für das 1/1000-Aufflackern ist ein
/// EIGENER Zufall, nicht der Simulationszufall — das Original nimmt <c>rand()</c>
/// <c>0x43B750</c> im Zeichner; ob das denselben Strom verstellt, ist ungelesen (V),
/// und bei uns darf das Zeichnen die Simulation nicht verändern. (2) Gewürfelt wird
/// je gezeichnetem Bild; wie oft der Zeichner des Originals je Sekunde läuft, ist
/// ungelesen (V). (3) Der Schattenblitter dunkelt über eine Tafel ab
/// (<c>0xB135B0</c>); wir zeichnen mit <see cref="ShadowTint"/> wie beim Frachter.</para>
///
/// <para>Gegenschalter <c>--minen-unsichtbar</c> (der Stand bis 15.09.2026: nichts
/// wird gezeichnet), <c>--minen-nebel-alt</c> (eigene Minen decken ihre Zelle nicht
/// auf).</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--minen-unsichtbar</c> — keine Mine wird gezeichnet.</summary>
    public static bool MinenUnsichtbar;

    /// <summary><c>--minen-nebel-alt</c> — eigene/verbündete Minen decken ihre Zelle
    /// nicht auf.</summary>
    public static bool MinenNebelAlt;

    /// <summary>Gezeichnet: volle Scheiben (eigen/verbündet), Punkte (feindlich),
    /// Umrisse (feindlich, 1/1000).</summary>
    public int MinenScheiben, MinenPunkte, MinenUmrisse;

    private readonly System.Random _minenBildWurf = new(0x333);

    private enum MinenBildArt { Scheibe, Punkt, Umriss }

    /// <summary>Die Zeichenliste eines Korbes — getrennt vom Zeichnen, damit der
    /// kopflose Prüfstand dieselbe Entscheidung zählen kann.</summary>
    private IEnumerable<(Vector2 Pos, MinenBildArt Art, int Klasse, int Leger)> MinenKorb(int zeile)
    {
        for (int i = 0; i < _minen.Length; i++)
        {
            var m = _minen[i];
            if (!m.Aktiv || m.Row + 2 != zeile) continue;
            // sec50 — nur die Sichtbarkeit, kein Besitzer (0x42F2B0)
            if (FogActive && !Watched(m.Col, m.Row)) continue;
            int h = Simulation.Hang.Hub(ElevOf(m.Col, m.Row), HangArt(m.Col, m.Row), m.LageX, m.LageY);
            var pos = new Vector2(_ox + m.Col * TileW - 20 + m.LageX,
                                  _oy + m.Row * TileH - 20 + m.LageY - h);
            int klasse = SlopeClassOf(m.Col, m.Row);                   // 0x41D110
            MinenBildArt art = Allied(ViewPlayer, m.Player) ? MinenBildArt.Scheibe
                             : _minenBildWurf.Next(1000) == 333 ? MinenBildArt.Umriss
                             : MinenBildArt.Punkt;
            yield return (pos, art, klasse, m.Player);
        }
    }

    /// <summary>Kartenpunkt der ersten aktiven Mine — fuer den Bildlauf.</summary>
    public Vector2? MinenBildProbe()
    {
        foreach (var m in _minen)
            if (m.Aktiv) return CellCenter(m.Col, m.Row);
        return null;
    }

    private void MinenZeichnen(int zeile)
    {
        if (MinenUnsichtbar) return;
        var scheibe = EffectFrames("mine");
        var punkt = EffectFrames("minenpunkt");
        if (scheibe.Count == 0 || punkt.Count == 0) return;
        foreach (var (pos, art, klasse, _) in MinenKorb(zeile))
        {
            var bild = art == MinenBildArt.Punkt ? punkt[0] : scheibe[Mathf.Clamp(klasse, 0, scheibe.Count - 1)];
            if (art == MinenBildArt.Scheibe) { DrawTexture(bild, pos); MinenScheiben++; }       // 0x4ACCD0
            else { DrawTexture(bild, pos, ShadowTint); if (art == MinenBildArt.Punkt) MinenPunkte++; else MinenUmrisse++; }   // 0x4AC6D0
        }
    }

    /// <summary>
    /// <c>--minenbild-check</c>, Kampagne 15 mit <c>--fog</c>:
    /// <list type="number">
    /// <item>Bilder: <c>mine</c> 5 Rahmen, <c>minenpunkt</c> 1 Rahmen mit genau 4 deckenden
    /// Bildpunkten (sonst <c>--reexport-effects</c> laufen lassen).</item>
    /// <item>Eine EIGENE Mine weit im Nebel deckt ihre Zelle auf und steht als Scheibe
    /// in der Liste (Nullmodell <c>--minen-nebel-alt</c>: nicht sichtbar, nicht in der Liste).</item>
    /// <item>Die feindlichen Minen: nur die in sichtbaren Zellen stehen in der Liste, alle
    /// als Punkt oder Umriss, keine als Scheibe.</item>
    /// <item>Das Aufflackern: über 10 000 Durchgänge je feindlicher sichtbarer Mine
    /// ≈ 10 Umrisse (1/1000).</item>
    /// </list>
    /// ⚠ <c>--minen-unsichtbar</c> wirkt nur im Zeichner und ist kopflos nicht messbar.
    /// </summary>
    public string MinenbildCheck()
    {
        var sb = new System.Text.StringBuilder("minenbild-check\n");
        bool ok = true;
        void Soll(bool b, string was) { sb.Append($"  {(b ? "ok  " : "⚠ FALSCH")} {was}\n"); ok &= b; }
        EnsureMissionScript();
        if (!FogActive || _fog == null) return sb.Append("  ⚠ ohne --fog sagt der Lauf NICHTS\n  DURCHGEFALLEN").ToString();
        if (MinenNebelAlt) sb.AppendLine("  ⚠ NULLMODELL --minen-nebel-alt: Aussage 2 MUSS scheitern");

        // 1. Bilder
        var scheibe = EffectFrames("mine");
        var punkt = EffectFrames("minenpunkt");
        int deckend = 0;
        if (punkt.Count > 0)
        {
            var img = punkt[0].GetImage();
            for (int y = 0; y < img.GetHeight(); y++)
                for (int x = 0; x < img.GetWidth(); x++)
                    if (img.GetPixel(x, y).A > 0.5f) deckend++;
        }
        Soll(scheibe.Count == 5, $"Effects/mine (Folge 93): {scheibe.Count} Rahmen (Soll 5)");
        Soll(punkt.Count == 1 && deckend == 4, $"Effects/minenpunkt (Folge 73): {punkt.Count} Rahmen, {deckend} deckende Bildpunkte (Soll 1 / 4)");

        // 2. eine eigene Mine im Nebel
        UpdateFog();
        Vector2I? nebel = null;
        for (int r = 2; r < _nav!.Height - 2 && nebel == null; r++)
            for (int c = 2; c < _nav.Width - 2 && nebel == null; c++)
                if (!_fog.IsWatched(c, r) && !_fog.IsWatched(c, r - 2) && !_fog.IsWatched(c + 2, r) && !_fog.IsWatched(c - 2, r)
                    && _nav.GroundAt(c, r) == Simulation.NavGrid.Ground.Free && _nav.BesetztVon(c, r) < 0
                    && MinenAufZelle(c, r) == 0)
                    nebel = new Vector2I(c, r);
        if (nebel == null) Soll(false, "keine freie Nebelzelle fuer die eigene Mine");
        else
        {
            MineLegen(nebel.Value.X, nebel.Value.Y, ViewPlayer);
            UpdateFog();
            bool sicht = _fog.IsWatched(nebel.Value.X, nebel.Value.Y);
            // ⚠ Die Nachbarzelle wird durch den SAUM sichtbar (MarkCorners/SaumSetzen, 0x41FF50) —
            // gemessen wird darum zwei Zellen weiter: Radius 0 heisst, dort bleibt Nebel.
            bool nachbar = _fog.IsWatched(nebel.Value.X + 2, nebel.Value.Y);
            int inListe = 0;
            foreach (var k in MinenKorb(nebel.Value.Y + 2))
                if (k.Art == MinenBildArt.Scheibe) inListe++;
            Soll(sicht && !nachbar && inListe >= 1,
                 $"eigene Mine auf ({nebel.Value.X},{nebel.Value.Y}) im Nebel: Zelle sichtbar {sicht}, zwei Zellen weiter sichtbar {nachbar} (Soll false, Radius 0 + Saum), als Scheibe in der Liste {inListe}");
        }

        // 3. feindliche Minen — eine eigene Einheit an ein Minenfeld stellen, damit
        // welche in Sicht sind (⚠ EINGRIFF)
        for (int i = 0; i < _minen.Length; i++)
        {
            var mm = _minen[i];
            if (!mm.Aktiv || Allied(ViewPlayer, mm.Player)) continue;
            int ei = -1;
            for (int k = 0; k < _entities.Count; k++)
            {
                var e = _entities[k];
                if (!e.Dead && !e.IsProp && !e.IsBuilding && e.Mobile && e.Owner == ViewPlayer && e.Infantry < 0
                    && e.Move != Simulation.NavGrid.MoveClass.Ship && !Untergestellt(e)) { ei = k; break; }
            }
            if (ei < 0) break;
            Vector2I? platz = null;
            for (int d = 1; d <= 4 && platz == null; d++)
                foreach (var (dx, dy) in new[] { (0, -d), (-d, 0), (d, 0), (0, d) })
                    if (platz == null && _nav.InBounds(mm.Col + dx, mm.Row + dy)
                        && _nav.IsFree(mm.Col + dx, mm.Row + dy, _entities[ei].Move, ei)
                        && MinenAufZelle(mm.Col + dx, mm.Row + dy) == 0)
                        platz = new Vector2I(mm.Col + dx, mm.Row + dy);
            if (platz == null) continue;
            ProbeVersetzen(ei, platz.Value.X, platz.Value.Y);
            UpdateFog();
            sb.AppendLine($"  ⚠ EINGRIFF: Platz {_entities[ei].Slot} an das Minenfeld bei ({mm.Col},{mm.Row}) versetzt");
            break;
        }
        int feind = 0, feindSichtbar = 0, liste = 0, scheibeFeind = 0;
        foreach (var m in _minen)
        {
            if (!m.Aktiv || Allied(ViewPlayer, m.Player)) continue;
            feind++;
            if (_fog.IsWatched(m.Col, m.Row)) feindSichtbar++;
        }
        var zeilen = new HashSet<int>();
        foreach (var m in _minen) if (m.Aktiv) zeilen.Add(m.Row + 2);
        foreach (int z in zeilen)
            foreach (var k in MinenKorb(z))
            {
                if (Allied(ViewPlayer, k.Leger)) continue;
                if (k.Art == MinenBildArt.Scheibe) scheibeFeind++; else liste++;
            }
        Soll(feind > 0 && liste == feindSichtbar && scheibeFeind == 0,
             $"feindliche Minen {feind}, davon in sichtbaren Zellen {feindSichtbar}; in der Liste als Punkt/Umriss {liste} (Soll {feindSichtbar}), als Scheibe {scheibeFeind} (Soll 0)");

        // 4. das Aufflackern
        if (feindSichtbar > 0)
        {
            int umriss = 0, durch = 0;
            for (int n = 0; n < 10000; n++)
                foreach (int z in zeilen)
                    foreach (var k in MinenKorb(z))
                    {
                        if (Allied(ViewPlayer, k.Leger)) continue;
                        durch++;
                        if (k.Art == MinenBildArt.Umriss) umriss++;
                    }
            double je = 10000.0 * umriss / System.Math.Max(1, durch);
            Soll(je is > 4 and < 16, $"Aufflackern: {umriss} Umrisse in {durch} Punktbildern = {je:0.0} je 10 000 (Soll 10 ± 6)");
        }
        else sb.AppendLine("  (Aufflackern ungeprueft: keine feindliche Mine in Sicht — mit --no-fog oder naeher heran pruefen)");

        // ⚠ Der Gegenschalter --minen-unsichtbar wirkt im ZEICHNER, und der laeuft
        // kopflos nicht — er ist hier nicht messbar, nur am Bild.
        sb.AppendLine($"  --minen-nebel-alt {MinenNebelAlt}, --minen-unsichtbar {MinenUnsichtbar}");
        return sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }
}
