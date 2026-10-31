namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Text;
using AkteEuropaReborn.Simulation.Commands;
using Godot;

/// <summary>
/// ⭐⭐ <b>ILLUSION (bug-720) UND SPIEGELUNG (bug-721)</b> (30.10.2026). Lesung
/// <c>berichte/fehlende-funktionen-opus.md</c> §6, Bau <c>berichte/bau-teleporter.md</c>.
/// Selbst nachgelesen in C UND F (cmpfn.py: Befehle 23..25 0x4C342A/0x4C2FA4 100 Befehle
/// 0 Abweichungen, Knoepfe 0x448A98/0x447A8C 75/0, Tarnwurf 0x450790/0x44F440 50/0, Zeichner
/// 0x4299AE/0x428B9E 75/0, Menue c4 0x4419C9/0x4409C9 14/0):
///
/// <code>
/// Menue c4  C 0x4419C9 / F 0x4409C9 (nur Fahrzeug, c4 leer): +0x10 == 0x53 -> 0x16 + word[+0x40],
///           +0x10 == 0x54 -> 0x18 + word[+0x40]
/// Knoepfe   0x16 @0x448A98: Befehl 24 (E, 1, rand()%8)        0x17 @0x448AE7: Befehl 24 (E, 0)
///           0x18 @0x448B1F: Befehl 25 (E, 1, 0x450790(Ich), 0x4507D0(Ich))
///           0x19 @0x448B8B: Befehl 25 (E, 0)                    — gewuerfelt wird IM MENUE
/// Tarnwurf  0x450790: Bauteil[161 + rand%10]+0x0D        (Rumpfbild, Fahrwerke 161..170)
///           0x4507D0: r = rand%34; r &lt; 15 ? Bauteil[65 + r]+0x0D : Bauteil[r − 14]+0x0D
///           (Aufsatzbild: Ausruestung 65..79 oder Waffe 1..19), je Spielerblock 0x5045A0
/// Bef. 24   C 0x4C344F / F 0x4C2FC9: word[+0x40] := P2; == 1: +0x4A/+0x4B := 0x539268[2·P3]
///           = (40,40)(0,40)(0,20)(0,0)(40,0)(80,0)(80,20)(80,40), Bild 0x61 an
///           (Spalte + 4A/40 − 1, Zeile + 4B/20 − 1), Klang 52 Modus 1; sonst Klang 53
/// Bef. 25   C 0x4C3503 / F 0x4C307D: word[+0x40] := P2; == 1: +0x4A := P3, +0x4B := P4,
///           +0x11 := 0xFF / 0 nach den Gruppen des Tarnrumpfs (0x77C871), Klang 54; sonst
///           +0x11 nach dem eigenen Rumpf, Klang 55; in BEIDEN Faellen Bild 0x62 an der Einheit
/// Spiegel   0x4299C3: +0x10 == 0x53 und word[+0x40] != 0 -> derselbe Koerper ein zweites Mal bei
///           (+0x4A − 40, +0x4B − 20) Bildpunkten, Zeichenart 1; rand()%100 == 0 -> Zeichenart 2
/// Maus      0x431D3D / F 0x430E7F: fuer einen FEINDLICHEN Betrachter (0x87B155 == 0), UKOL &lt; 45,
///           Spiegel an: der Kasten um das Spiegelbild trifft die Einheit
/// Illusion  0x429A25 / F 0x428C15 (Zeichner) und 0x4076E8 / F 0x407612 (Gangzaehler):
///           +0x10 == 0x54 und word[+0x40] != 0 -> +0x0B/+0x0C := +0x4A/+0x4B, danach zurueck.
///           Fuer JEDEN Betrachter, auch den Besitzer. Keine Uhr: es endet nur mit Befehl 25 (E, 0)
/// </code>
/// <para>Kampf, Zielwahl und Schaden fragen +0x10 == 0x53/0x54 nicht (fld.py, Bericht §6.1):
/// <b>reine Optik</b>, gemessen im Pruefstand an der Pruefsumme.</para>
///
/// <para>⚠ <b>UNSERE SETZUNGEN</b>:</para>
/// <list type="bullet">
/// <item>Eigene Felder <see cref="Entity.SpiegelAn"/>/<see cref="Entity.IllusionAn"/> statt des
///   geteilten Worts +0x40.</item>
/// <item>Die Wuerfe im Menue und im Zeichner (im Original der C-Zufall 0x4D6C70) nehmen einen
///   EIGENEN Wuerfel dieser Datei — weder den Simulationswuerfel (er laeuft nur auf der
///   Maschine mit dem Klick) noch den Klangwuerfel. Das Ergebnis reist im Befehl.</item>
/// <item>Der Tarnrumpf ist bei uns ein Fahrwerk (<see cref="Entity.UnitType"/>), nicht ein Bild:
///   Rumpfbild b -> das Fahrwerk 160..199 mit Bauteil+0x0D == b (161..170 -> 2..11).</item>
/// <item>+0x11 wird in beiden Faellen 0xFF oder 0 — bei uns ist beides <c>Pose = 0</c>.</item>
/// <item>Zeichenart 2 (das Flackern) ist seit bug-726 gelesen und gebaut: ein schwarzer
///   Schattenriss (siehe <see cref="_tarnFarbe"/>); <c>--spiegel-flackern-alt</c> = Aussetzen.
///   Der Sonderfall Tarnrumpf 9 (+0x02 := 0 beim Zeichnen) fehlt.</item>
/// <item>Der Mauskasten ist <see cref="BodyRect"/> um den Spiegelversatz verschoben (Original: ein
///   Kasten 40×60 Bildpunkte), und er trifft nur, wenn sonst nichts getroffen wird.</item>
/// <item>Bild 0x61/0x62 = ANIM-Folge 97/98 — seit bug-726 GELESEN (Effektzeichner @0x42B11F:
///   word[0x7A404A + 4·Art]) und ausgegeben (»spiegelblitz« 10 Bilder, »tarnblitz« 9 Bilder,
///   InterfaceExporter). Bis zum Neueinlesen (Stufe oberflaeche 3) steht »lichtblitz« dafuer.</item>
/// </list>
/// <para>Gegenschalter <c>--illusion-aus</c>, <c>--spiegelung-aus</c>. Pruefstaende
/// <c>--illusion-check</c> (DM_4: P0 hat 5 Traeger), <c>--spiegelung-check</c> (mit Eingriff).</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--illusion-aus</c> — der Stand bis 30.10.2026.</summary>
    public static bool IllusionAus;
    /// <summary><c>--spiegelung-aus</c> — der Stand bis 30.10.2026.</summary>
    public static bool SpiegelungAus;

    /// <summary>Ausruestung 83 »Spiegelbild« / 84 »Illusionator« (+0x10).</summary>
    public const int SpiegelTeil = 0x53, IllusionTeil = 0x54;
    /// <summary>Die vier Zeilen der Befehlsliste 0x4FD660.</summary>
    public const int CodeSpiegelAn = 0x16, CodeSpiegelAus = 0x17, CodeIllusionAn = 0x18, CodeIllusionAus = 0x19;
    /// <summary>Klang 52/53 Spiegel an/aus, 54/55 Illusion an/aus.</summary>
    public const int SpiegelKlangAn = 52, SpiegelKlangAus = 53, IllusionKlangAn = 54, IllusionKlangAus = 55;

    /// <summary>Die Tafel 0x539268 (C), acht Paare.</summary>
    private static readonly (int X, int Y)[] SpiegelVersatz =
        { (40, 40), (0, 40), (0, 20), (0, 0), (40, 0), (80, 0), (80, 20), (80, 40) };

    /// <summary>UNSERE SETZUNG: der eigene Wuerfel fuer Menue und Zeichner (Kopf).</summary>
    private static readonly System.Random TarnWuerfel = new(0x7A2D);

    public int SpiegelBefehle, IllusionBefehle, SpiegelBilder, SpiegelFlackern, IllusionBilder, SpiegelMausTreffer;

    // ---- Menue ---------------------------------------------------------------------

    /// <summary>c4 @0x4419C9 — −1 = leer.</summary>
    private static int TarnMenueCode(Entity e)
    {
        if (e.Equipment == SpiegelTeil && !SpiegelungAus) return CodeSpiegelAn + (e.SpiegelAn != 0 ? 1 : 0);
        if (e.Equipment == IllusionTeil && !IllusionAus) return CodeIllusionAn + (e.IllusionAn != 0 ? 1 : 0);
        return -1;
    }

    /// <summary>0x16/0x17 — Befehl 24, an: P3 = rand()%8 im Menue.</summary>
    public string PostSpiegelung(bool an)
    {
        int idx = MenueEinheit();
        if (idx < 0) return "keine eigene Einheit gewaehlt";
        Emit(an ? CommandRecord.Make(CommandOp.Spiegelung, (byte)ViewPlayer, (short)idx, 1, (short)TarnWuerfel.Next(8))
                : CommandRecord.Make(CommandOp.Spiegelung, (byte)ViewPlayer, (short)idx, 0));
        return "";
    }

    /// <summary>0x450790: Rumpfbild eines Fahrwerks 161..170 des Spielerblocks.</summary>
    private int TarnRumpfWurf(int spieler) => TarnBild(spieler, 161 + TarnWuerfel.Next(10));

    /// <summary>0x4507D0: Aufsatzbild einer Ausruestung 65..79 oder einer Waffe 1..19.</summary>
    private int TarnAufsatzWurf(int spieler)
    {
        int r = TarnWuerfel.Next(34);
        return TarnBild(spieler, r < 15 ? 65 + r : r - 14);
    }

    private int TarnBild(int spieler, int bauteil)
    {
        var z = BauteilFuer(spieler, bauteil) ?? Simulation.DesignMath.GrundZeile(bauteil);
        return z != null && z.Length > 0x0D ? z[0x0D] : 0;
    }

    /// <summary>0x18/0x19 — Befehl 25, an: P3/P4 im Menue gewuerfelt (Spieler [0x4FA284]).</summary>
    public string PostIllusion(bool an)
    {
        int idx = MenueEinheit();
        if (idx < 0) return "keine eigene Einheit gewaehlt";
        int ich = ViewPlayer;
        Emit(an ? CommandRecord.Make(CommandOp.Illusion, (byte)ich, (short)idx, 1, (short)TarnRumpfWurf(ich), (short)TarnAufsatzWurf(ich))
                : CommandRecord.Make(CommandOp.Illusion, (byte)ich, (short)idx, 0));
        return "";
    }

    // ---- die Befehle ---------------------------------------------------------------

    private string TarnBlitz(string eigen)
        => EffectFrames(eigen).Count > 0 ? eigen : "lichtblitz";               // UNSERE SETZUNG (Kopf)

    /// <summary>Befehl 24, C 0x4C344F.</summary>
    private bool ApplySpiegelung(in CommandRecord c)
    {
        if (SpiegelungAus) return false;
        var e = _entities[c.P1];
        if (e.Dead || e.IsBuilding || e.IsProp) return false;
        SpiegelBefehle++;
        e.SpiegelAn = c.P2;                                                      // word[+0x40] := P2
        if (c.P2 == 1)
        {
            var (vx, vy) = SpiegelVersatz[c.P3 & 7];                             // 0x539268[2·P3] (& 7: UNSERE Klemme)
            e.Bild4A = vx; e.Bild4B = vy;
            _effects.Add(new Effect { Pos = CellCenter(e.Col + vx / 40 - 1, e.Row + vy / 20 - 1), Kind = TarnBlitz("spiegelblitz"), FrameTime = 1f / SimHz });
            Audio.GameSounds.PlayAt(SpiegelKlangAn, e.Col, e.Row);              // 52, Modus 1
        }
        else Audio.GameSounds.PlayAt(SpiegelKlangAus, e.Col, e.Row);            // 53, Modus 1
        return true;
    }

    /// <summary>Befehl 25, C 0x4C3503.</summary>
    private bool ApplyIllusion(in CommandRecord c)
    {
        if (IllusionAus) return false;
        var e = _entities[c.P1];
        if (e.Dead || e.IsBuilding || e.IsProp) return false;
        IllusionBefehle++;
        e.IllusionAn = c.P2;                                                     // word[+0x40] := P2
        if (c.P2 == 1)
        {
            e.Bild4A = c.P3 & 0xFF; e.Bild4B = c.P4 & 0xFF;                      // +0x4A/+0x4B (Bytes)
            e.Pose = 0;                                                          // +0x11 := 0xFF/0 (Kopf)
            Audio.GameSounds.PlayAt(IllusionKlangAn, e.Col, e.Row);              // 54, Modus 1
        }
        else
        {
            e.Pose = 0;
            Audio.GameSounds.PlayAt(IllusionKlangAus, e.Col, e.Row);             // 55, Modus 1
        }
        _effects.Add(new Effect { Pos = e.Pos, Kind = TarnBlitz("tarnblitz"), FrameTime = 1f / SimHz });   // 0x62
        return true;
    }

    // ---- der Zeichner ----------------------------------------------------------------

    private bool _tarnInnen;
    private static readonly Dictionary<int, int> _tarnFahrwerk = new();

    /// <summary>Rumpfbild -> Fahrwerk (UNSERE SETZUNG, Kopf). 0 = keins.</summary>
    private static int TarnFahrwerk(int bild)
    {
        if (_tarnFahrwerk.TryGetValue(bild, out int p)) return p;
        p = 0;
        for (int k = 160; k < 200 && p == 0; k++)
        {
            var z = Simulation.DesignMath.GrundZeile(k);
            if (z != null && z.Length > 0x0D && z[0x0D] == bild && bild > 0) p = k;
        }
        _tarnFahrwerk[bild] = p;
        return p;
    }

    private bool IllusionWirkt(Entity e) => !IllusionAus && e.Equipment == IllusionTeil && e.IllusionAn != 0 && e.Infantry < 0;
    private bool SpiegelWirkt(Entity e) => !SpiegelungAus && e.Equipment == SpiegelTeil && e.SpiegelAn != 0 && e.Infantry < 0;

    /// <summary>Wie die Einheit gezeichnet wird: (Fahrwerk, Aufsatzbild) — fuer Zeichner und Probe.</summary>
    public (int Rumpf, int Aufsatz) TarnAnsicht(Entity e)
    {
        if (!IllusionWirkt(e)) return (e.UnitType, e.Weapon);
        int f = TarnFahrwerk(e.Bild4A);
        return (f > 0 ? f : e.UnitType, e.Bild4B);
    }

    /// <summary>Aus <c>DrawUnitBody</c>: true = schon gezeichnet (mit Tarnung und/oder Spiegel).</summary>
    private bool TarnZeichnen(Entity e)
    {
        if (_tarnInnen || e.Dead || e.IsBuilding || e.IsProp) return false;
        bool ill = IllusionWirkt(e), spi = SpiegelWirkt(e);
        if (!ill && !spi) return false;
        int ut = e.UnitType, w = e.Weapon;
        _tarnInnen = true;
        try
        {
            if (ill) { (e.UnitType, e.Weapon) = TarnAnsicht(e); IllusionBilder++; }   // 0x429A53/0x429A5F
            DrawUnitBody(e);
            if (spi)
            {
                bool art2 = TarnWuerfel.Next(100) == 0;                                // 0x4299DA rand%100 == 0
                if (art2) SpiegelFlackern++;
                if (!(art2 && SpiegelFlackernAlt))
                {
                    if (art2) _tarnFarbe = new Color(0, 0, 0);                         // bug-726: Zeichenart 2 = Schattenriss Farbe 0
                    DrawSetTransform(new Vector2(e.Bild4A - 40, e.Bild4B - 20));       // 0x429A04/0x429A19
                    DrawUnitBody(e);
                    DrawSetTransform(Vector2.Zero);
                    _tarnFarbe = null;
                    SpiegelBilder++;
                }
            }
        }
        finally { e.UnitType = ut; e.Weapon = w; _tarnInnen = false; _tarnFarbe = null; }
        return true;
    }

    /// <summary><c>--spiegel-flackern-alt</c> — bug-726 aus: in Zeichenart 2 setzt das
    /// Spiegelbild aus (Stand vom 30.10. mittags) statt als Schattenriss zu erscheinen.</summary>
    public static bool SpiegelFlackernAlt;

    /// <summary>⭐ bug-726 — <b>Zeichenart 2 des Spiegels</b>: der Blitter C 0x4012FD → 0x4ACB90
    /// (Art 2 @0x4AC147 mit Farbe 0, Art 3 = Auswahlblinken mit 0x28) schreibt in JEDES
    /// deckende Bildpunkt des Bildes dieselbe Palettenfarbe (<c>mov al,[ebp+0x14]; mov [esi],al</c>
    /// @0x4ACC51/0x4ACC66, Durchsicht 0xFF bleibt) — ein SCHATTENRISS. Farbe 0 in 01.PAL ist
    /// (0,0,0), also schwarz. Waehrend <see cref="_tarnFarbe"/> gesetzt ist, zeichnet jedes
    /// <c>DrawTexture(Bild, Ort)</c> dieser Ebene mit dieser Farbe als Modulation — bei
    /// Schwarz genau der Schattenriss. Sonst unveraendert der Godot-Aufruf.</summary>
    private Color? _tarnFarbe;

    private new void DrawTexture(Texture2D texture, Vector2 position)
    {
        if (_tarnFarbe is { } f) base.DrawTexture(texture, position, f);
        else base.DrawTexture(texture, position);
    }

    /// <summary>0x431D3D — der Spiegel faengt die Maus eines feindlichen Betrachters.</summary>
    private int SpiegelPick(Vector2 p, int getroffen)
    {
        if (getroffen >= 0 || SpiegelungAus) return getroffen;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!SpiegelWirkt(e) || e.IsBuilding || e.IsProp || e.Dead || Untergestellt(e)) continue;
            if (ViewPlayer < 0 || !MineFeindlich(ViewPlayer, e.Owner)) continue;        // 0x87B155 == 0
            if (!PickOhneNebel && ImNebelVerborgen(e)) continue;
            var r = BodyRect(e);
            r.Position += new Vector2(e.Bild4A - 40, e.Bild4B - 20);
            if (!r.HasPoint(p)) continue;
            SpiegelMausTreffer++;
            return i;
        }
        return getroffen;
    }

    // ---- --illusion-check -----------------------------------------------------------

    /// <summary>
    /// <c>--illusion-check</c> (DM_4: P0 hat 5 Illusionatoren). Spielerweg: Auswahl, Menue c4 0x18,
    /// Befehl 25 ueber den Ring, echte Takte; die Zeichneransicht wechselt, die Pruefsumme NICHT;
    /// Menue c4 0x19 schaltet ab. Dazu 1000 Tarnwuerfe: alle 10 Rumpfbilder kommen vor.
    /// Nullmodell <c>--illusion-aus</c>: c4 leer, kein Befehl — MUSS durchfallen.
    /// </summary>
    public string IllusionCheck()
    {
        var sb = new StringBuilder("illusion-check (bug-720)\n");
        bool ok = true;
        void Soll(bool b, string was) { sb.Append($"  {(b ? "ok  " : "⚠ FALSCH")} {was}\n"); ok &= b; }
        if (IllusionAus) sb.AppendLine("  ⚠ NULLMODELL --illusion-aus: hier MUSS der Lauf durchfallen");
        var gehoert = new List<int>();
        var vorherHorcher = Audio.SoundBankPlayer.Horcher;
        Audio.SoundBankPlayer.Horcher = (s, c, r) => { gehoert.Add(s); vorherHorcher?.Invoke(s, c, r); };
        try
        {
            var traeger = new List<int>();
            var je = new Dictionary<int, int>();
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (e.Dead || e.IsProp || e.IsBuilding || e.Equipment != IllusionTeil) continue;
                je[e.Owner] = je.GetValueOrDefault(e.Owner) + 1;
                if (e.Owner == ViewPlayer && !Untergestellt(e)) traeger.Add(i);
            }
            sb.AppendLine($"  Illusionatoren je Spieler: {string.Join(" ", je)}; beim Betrachter P{ViewPlayer}: {traeger.Count}");
            if (traeger.Count == 0) return sb.Append("  keiner beim Betrachter\n  DURCHGEFALLEN").ToString();
            int idx = traeger[0];
            var t = _entities[idx];
            var echt = (t.UnitType, t.Weapon);
            int c4 = MenueCodes(idx)[3];
            _sel.Clear(); _sel.Add(idx); _selected = idx;
            string menue = MenueAktion(CodeIllusionAn);
            int h0 = gehoert.Count, b0 = IllusionBefehle;
            for (int k = 0; k < 30 && IllusionBefehle == b0; k++) SimTickFuerProbe();   // der Ring braucht ein paar Takte
            var ansicht = TarnAnsicht(t);
            sb.AppendLine($"  Einheit {idx} »{LabelOf(t)}« Rumpf {echt.UnitType} Aufsatz {echt.Weapon}; Menue c4 {c4} (0x18 = 24), »{(menue.Length == 0 ? "ok" : menue)}«");
            Soll(c4 == CodeIllusionAn, "Menue c4 = 0x18 (Illusion an)");
            Soll(t.IllusionAn == 1 && t.Bild4A is >= 2 and <= 11 && t.Bild4B is >= 21 and <= 54 && IllusionBefehle > 0,
                 $"Befehl 25 an: +0x40 {t.IllusionAn}, Rumpfbild {t.Bild4A} (2..11), Aufsatzbild {t.Bild4B} (21..54)");
            Soll(ansicht.Rumpf == TarnFahrwerk(t.Bild4A) && ansicht.Aufsatz == t.Bild4B && (t.UnitType, t.Weapon) == echt,
                 $"gezeichnet als Rumpf {ansicht.Rumpf} Aufsatz {ansicht.Aufsatz}, die Einheit bleibt {t.UnitType}/{t.Weapon}");
            Soll(gehoert.GetRange(h0, gehoert.Count - h0).Contains(IllusionKlangAn), $"Klang 54: {string.Join(",", gehoert.GetRange(h0, gehoert.Count - h0))}");
            // reine Optik: derselbe Befehl unmittelbar angewandt aendert die Pruefsumme nicht
            ulong s0 = DeterminismChecksum();
            ApplyCommand(CommandRecord.Make(CommandOp.Illusion, (byte)ViewPlayer, (short)idx, 1, 5, 30));
            ulong s1 = DeterminismChecksum();
            ApplyCommand(CommandRecord.Make(CommandOp.Move, (byte)ViewPlayer, (short)idx, (short)(t.Col + 3), (short)t.Row));
            SimTickFuerProbe();
            ulong s2 = DeterminismChecksum();
            Soll(s0 == s1 && s1 != s2, $"Pruefsumme: vor {s0:X16} / nach Befehl 25 {s1:X16} (gleich), Gegenprobe Fahrbefehl + Takt {s2:X16} (anders)");
            // abschalten
            int c4b = MenueCodes(idx)[3];
            h0 = gehoert.Count; b0 = IllusionBefehle;
            MenueAktion(CodeIllusionAus);
            for (int k = 0; k < 30 && IllusionBefehle == b0; k++) SimTickFuerProbe();
            var neuK = gehoert.GetRange(h0, gehoert.Count - h0);
            Soll(c4b == CodeIllusionAus && t.IllusionAn == 0 && TarnAnsicht(t) == echt && neuK.Contains(IllusionKlangAus),
                 $"aus: Menue c4 {c4b} (0x19 = 25), +0x40 {t.IllusionAn}, Ansicht {TarnAnsicht(t)}, Klang 55 {(neuK.Contains(IllusionKlangAus) ? "ja" : "NEIN")}");
            // die Wuerfe
            var rumpf = new HashSet<int>(); var aufsatz = new HashSet<int>();
            for (int k = 0; k < 1000; k++) { rumpf.Add(TarnRumpfWurf(ViewPlayer)); aufsatz.Add(TarnAufsatzWurf(ViewPlayer)); }
            Soll(rumpf.Count == 10 && aufsatz.Count == 34, $"1000 Tarnwuerfe: {rumpf.Count} Rumpfbilder (10), {aufsatz.Count} Aufsatzbilder (34)");
        }
        finally { Audio.SoundBankPlayer.Horcher = vorherHorcher; _sel.Clear(); _selected = -1; }
        sb.AppendLine($"  Zaehler: Befehle {IllusionBefehle}; --illusion-aus {IllusionAus}");
        return sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }

    // ---- --spiegelung-check ---------------------------------------------------------

    /// <summary>
    /// <c>--spiegelung-check</c>: es gibt keinen Traeger (0 Einheiten, kein Entwurf mit Aufbau 83),
    /// darum ⚠ EINGRIFF: ein eigenes Fahrzeug bekommt Ausruestung 0x53. Dann der Spielerweg:
    /// Menue c4 0x16, Befehl 24 ueber den Ring; Versatz aus 0x539268, Klang 52; die Maus eines
    /// FEINDLICHEN Betrachters faengt sich am Spiegelbild, die des Besitzers nicht; Menue 0x17
    /// schaltet ab (Klang 53). Nullmodell <c>--spiegelung-aus</c>: MUSS durchfallen.
    /// </summary>
    public string SpiegelungCheck()
    {
        var sb = new StringBuilder("spiegelung-check (bug-721)\n");
        bool ok = true;
        void Soll(bool b, string was) { sb.Append($"  {(b ? "ok  " : "⚠ FALSCH")} {was}\n"); ok &= b; }
        if (SpiegelungAus) sb.AppendLine("  ⚠ NULLMODELL --spiegelung-aus: hier MUSS der Lauf durchfallen");
        var gehoert = new List<int>();
        var vorherHorcher = Audio.SoundBankPlayer.Horcher;
        Audio.SoundBankPlayer.Horcher = (s, c, r) => { gehoert.Add(s); vorherHorcher?.Invoke(s, c, r); };
        int ich = ViewPlayer;
        bool nebelVorher = PickOhneNebel;
        try
        {
            int idx = -1;
            for (int i = 0; i < _entities.Count && idx < 0; i++)
            {
                var e = _entities[i];
                if (!e.Dead && !e.IsProp && !e.IsBuilding && e.Infantry < 0 && e.Owner == ich && e.GameUnitType == 0 && !Untergestellt(e)) idx = i;
            }
            if (idx < 0) return sb.Append("  kein eigenes Fahrzeug\n  DURCHGEFALLEN").ToString();
            var t = _entities[idx];
            int ausr = t.Equipment;
            t.Equipment = SpiegelTeil;
            sb.AppendLine($"  ⚠ EINGRIFF: Einheit {idx} »{LabelOf(t)}« ({t.Col},{t.Row}) Ausruestung {ausr} -> 0x53");
            int c4 = MenueCodes(idx)[3];
            _sel.Clear(); _sel.Add(idx); _selected = idx;
            int h0 = gehoert.Count, b0 = SpiegelBefehle;
            MenueAktion(CodeSpiegelAn);
            for (int k = 0; k < 30 && SpiegelBefehle == b0; k++) SimTickFuerProbe();   // der Ring braucht ein paar Takte
            var neuK = gehoert.GetRange(h0, gehoert.Count - h0);
            bool imTafel = System.Array.IndexOf(SpiegelVersatz, (t.Bild4A, t.Bild4B)) >= 0;
            Soll(c4 == CodeSpiegelAn && t.SpiegelAn == 1 && imTafel && neuK.Contains(SpiegelKlangAn),
                 $"an: Menue c4 {c4} (0x16 = 22), +0x40 {t.SpiegelAn}, Versatz ({t.Bild4A},{t.Bild4B}) aus 0x539268 {(imTafel ? "ja" : "NEIN")}, Klang 52 {(neuK.Contains(SpiegelKlangAn) ? "ja" : "NEIN")}");
            // die Maus: der Spiegel ist (4A−40, 4B−20) neben dem Koerper
            var mitte = BodyRect(t).GetCenter() + new Vector2(t.Bild4A - 40, t.Bild4B - 20);
            int feind = -1;
            for (int p = 0; p < 8 && feind < 0; p++) if (p != ich && MineFeindlich(p, t.Owner)) feind = p;
            PickOhneNebel = true;
            sb.AppendLine($"  ⚠ EINGRIFF: Treffer ohne Nebel; Mauspunkt {mitte}");
            int eigenTreffer = Pick(mitte);
            int feindTreffer = -2;
            if (feind >= 0) { ViewPlayer = feind; feindTreffer = Pick(mitte); ViewPlayer = ich; }
            bool versetzt = t.Bild4A != 40 || t.Bild4B != 20;
            Soll(versetzt && feindTreffer == idx && eigenTreffer != idx,
                 $"Maus am Spiegelbild: feindlicher Betrachter P{feind} trifft {feindTreffer} (Soll {idx}), der Besitzer trifft {eigenTreffer} (nicht {idx})");
            // abschalten
            int c4b = MenueCodes(idx)[3];
            h0 = gehoert.Count; b0 = SpiegelBefehle;
            MenueAktion(CodeSpiegelAus);
            for (int k = 0; k < 30 && SpiegelBefehle == b0; k++) SimTickFuerProbe();
            neuK = gehoert.GetRange(h0, gehoert.Count - h0);
            int nachher = -2;
            if (feind >= 0) { ViewPlayer = feind; nachher = Pick(mitte); ViewPlayer = ich; }
            Soll(c4b == CodeSpiegelAus && t.SpiegelAn == 0 && neuK.Contains(SpiegelKlangAus) && nachher != idx,
                 $"aus: Menue c4 {c4b} (0x17 = 23), +0x40 {t.SpiegelAn}, Klang 53 {(neuK.Contains(SpiegelKlangAus) ? "ja" : "NEIN")}, Feindmaus trifft {nachher}");
            t.Equipment = ausr;
        }
        finally { Audio.SoundBankPlayer.Horcher = vorherHorcher; PickOhneNebel = nebelVorher; ViewPlayer = ich; _sel.Clear(); _selected = -1; }
        sb.AppendLine($"  Zaehler: Befehle {SpiegelBefehle}, Maustreffer {SpiegelMausTreffer}; --spiegelung-aus {SpiegelungAus}");
        return sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }
}
