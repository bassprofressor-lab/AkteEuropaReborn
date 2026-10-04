namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Text;
using Godot;

/// <summary>
/// ⭐⭐ <b>PAKET 2 INFOFENSTER — der Bedienblock unten links</b> (04.10.2026,
/// KayelGee-Meldungen 2 und 3, <c>berichte/infofenster-fable.md</c> §2/§3).
/// <list type="bullet">
/// <item><b>bug-421 — der Name</b> (<see cref="BlockName"/>): Rangzeichen + ENTWURFSname
/// (<c>+0x3E</c> → sec47), nicht der Waffenname. Gegenschalter <c>--panelname-alt</c>.</item>
/// <item><b>bug-422 — der Leerzweig</b> (<see cref="MissionsZeilen"/>): sechs Zeilen an festen
/// Punkten, auch bei gewähltem GEBÄUDE. Gegenschalter <c>--leerzweig-alt</c> (alter
/// Streifen mit Fließtext) und <c>--gebaeudetext-im-block</c> (unser Gebäudetext).</item>
/// </list>
/// Das Bild (bug-420) steht bei <c>MapViewer.PanelPortrait</c>, die Schrift (bug-423) bei
/// <c>UI.WindowChrome.SchriftEinrichten</c>, der Lokator (bug-424) in
/// <c>UI/LokatorChrome.cs</c>; der Prüfstand <c>--infofenster-check</c> in
/// <c>Rendering/InfofensterLauf.cs</c>.
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--panelname-alt</c> — der Stand vor dem 04.10.2026: der Block nennt die
    /// Einheit nach <see cref="LabelOf(Entity)"/> (Waffe/Fahrwerk), in Großbuchstaben, ohne
    /// Rangzeichen.</summary>
    public static bool PanelnameAlt;

    /// <summary><c>--leerzweig-alt</c> — der Stand vor dem 04.10.2026: ohne Auswahl ein
    /// Fließtext im 34-Punkte-Streifen (zwei Zeilen sichtbar), in Großbuchstaben.</summary>
    public static bool LeerzweigAlt;

    /// <summary><c>--gebaeudetext-im-block</c> — der Stand vor dem 04.10.2026: ein gewähltes
    /// Gebäude zeigt unseren eigenen Gebäudetext statt der Missionszeilen.</summary>
    public static bool GebaeudetextImBlock;

    /// <summary><c>--aufbauteil-alt</c> — der Stand vor dem 04.10.2026: die Spalte
    /// »Aufbauteil« der Einheitenliste nennt <see cref="MountName"/> (unseren langen
    /// Tafelnamen) statt des kurzen EXE-Namens der Bauteilzeile. Siehe
    /// <see cref="AufbauteilName"/>.</summary>
    public static bool AufbauteilAlt;

    /// <summary><c>--missionsname-alt</c> — der Stand vor bug-428: die erste
    /// Blockzeile nennt den Kartenkopf (»Airborne Ambush«) statt »Mission N« aus
    /// der Tafel 0x4F81C0.</summary>
    public static bool MissionsnameAlt = System.Array.IndexOf(Core.CommandLine.Args, "--missionsname-alt") >= 0;

    /// <summary>Der Kartenkopf <c>+0x1C</c> so, wie er dasteht (<c>_mission</c> ist
    /// großgeschrieben).</summary>
    private string _missionRoh = "";

    /// <summary>
    /// ⭐⭐ bug-421 — <b>der Name im Block</b>, Einheitenzweig Zeile 1 (<c>0x4701CF…0x4702A8</c>):
    /// <code>
    ///   si := word[+0x3E]                                    ; ENTWURFSNUMMER
    ///   Puffer := Rangzeichen(byte[+0x28]) (0x4649F0)        ; OHNE Unterdrückung von 0xB4
    ///   si &gt;= 200 → 0x82F728 + 21·si                         ; (Laufzeittafel, ungelesen)
    ///   Klasse +0x0A &lt; 4 → sec47 0x51CE22 + 46·(si + 200·Betrachter)   ; +0x02 = NAME
    ///   sonst (Schiff)   → Schiffstafel 0x52EDA1 + 42·(si + 10·Spieler)
    ///   Text(11, 45)
    /// </code>
    /// K1 Platz 0: <c>+0x3E = 92</c> → »Panzer«; das alte <see cref="LabelOf(Entity)"/> gab den
    /// Waffennamen »Schwere Bordkanone«. ⚠ Geschrieben wird wie in sec47, nicht großgeschrieben.
    /// <para>⚠ UNSERE Setzungen: (1) ein eigener <see cref="Entity.Name"/> gewinnt — eine
    /// GEBAUTE Einheit trägt dort ihren Entwurfsnamen (für eigene Einheiten dasselbe), eine
    /// mitgenommene den ihren; (2) SCHIFFE bleiben bei der Schiffstafel nach dem RUMPF
    /// (<see cref="LabelOf(Entity)"/>) — auf den Karten ist dort <c>+0x3E = Rumpf − 150</c>, und
    /// ob das Original daraus einen anderen Namen macht, ist nicht nachgeprüft
    /// (infofenster-fable.md §7.6); (3) <c>+0x3E ≥ 200</c> oder unbekannt → alter Weg.</para>
    /// </summary>
    private string BlockName(Entity e)
    {
        if (PanelnameAlt) return LabelOf(e).ToUpper();
        string rang = UI.UnitListView.RangText(_uiFont, e.Rating28);
        return rang + EntwurfsName(e);
    }

    /// <summary>Der Entwurfsname ohne Rangzeichen — siehe <see cref="BlockName"/>.</summary>
    private string EntwurfsName(Entity e)
    {
        if (e.Name.Length > 0) return e.Name;
        if (e.GameUnitType is 4 or 5) return LabelOf(e);          // Schiff: Setzung (2)
        int t = e.Entwurf3E;
        if (t is < 0 or >= 200) return LabelOf(e);               // Setzung (3)
        int v = ViewPlayer is >= 0 and < 8 ? ViewPlayer : 0;
        LoadDesigns();
        if (_designBySlot.TryGetValue(t + 200 * v, out var d) && d.Name.Length > 0) return d.Name;
        if (_designBySlot.TryGetValue(t, out var d0) && d0.Name.Length > 0) return d0.Name;
        return LabelOf(e);
    }

    /// <summary>
    /// bug-421, Beifang — <b>Zeile 2 des Einheitenzweigs</b> (<c>0x4702C9…0x4703E3</c>):
    /// <c>byte[+0x10]</c> (Ausrüstung) ≠ 0 → Bauteilname + »(« + <c>+0x01</c> der Zeile + »)«
    /// an <b>(11, 122)</b>. Bei K1 Platz 0 ist <c>+0x10 = 0</c>, die Zeile fehlt dort.
    /// <para>⚠ Ohne Ausrüstung steht hier UNSERE Warnung »Kein Sprit«/»Keine Munition«
    /// (seit dem 18.08. im Block; das Original hat sie nicht). Sie stand bisher als zweite
    /// Zeile im Namensstreifen; mit der richtig großen Schrift (bug-423) passt dort nur
    /// noch eine Zeile.</para>
    /// </summary>
    private string BlockZeile2(Entity e, string warnung)
    {
        if (e.Equipment != 0)
        {
            string n = UI.UnitStatBook.ComponentPlain(e.Equipment);
            int stufe = UI.UnitStatBook.ComponentStufe(e.Equipment);
            if (n.Length > 0) return $"{n}({(stufe < 0 ? 0 : stufe)})";
        }
        return warnung;
    }

    /// <summary>
    /// ⭐⭐ bug-422 — <b>DER LEERZWEIG</b> <c>0x470E76…0x471194</c>: ohne Auswahl (Griff 0xFFFF)
    /// UND bei gewähltem GEBÄUDE (8000..9999 fällt durch dieselbe Weiche) sechs Zeilen bei
    /// x = 11, y = 45/61/74/87/100/113 — Kartenkopf, »Kontostand «, »Sprit gesamt «…%,
    /// »Munition gesamt «…%, »Ausgeschaltet «, »Verluste «. Werte wie
    /// <see cref="MissionSummaryText"/> (0x436740/0x436800: Mittel in Prozent über die
    /// eigenen Nicht-Fußvolk-Einheiten, sec53 +0x20/+0x24).
    /// </summary>
    public static readonly int[] LeerzweigY = { 45, 61, 74, 87, 100, 113 };

    public List<string> MissionsZeilen()
    {
        var w = MissionsWerte();
        return new List<string>
        {
            _missionRoh,
            $"Kontostand {w.Konto}",
            $"Sprit gesamt {w.Sprit}%",
            $"Munition gesamt {w.Munition}%",
            $"Ausgeschaltet {w.Aus}",
            $"Verluste {w.Verluste}",
        };
    }

    /// <summary>Die vier Zahlen des Leerzweigs, nach der Regel von <c>0x436740</c>/<c>0x436800</c>.</summary>
    public (int Konto, int Sprit, int Munition, int Aus, int Verluste) MissionsWerte()
    {
        int me = ViewPlayer;
        int sS = 0, sZ = 0, mS = 0, mZ = 0;
        foreach (var u in _entities)
        {
            if (u.IsBuilding || u.IsProp || u.Dead || u.Owner != me) continue;
            if (u.GameUnitType == 1) continue;                    // Klasse 1 = Fußvolk zählt nicht
            if (u.FuelMax > 0) { sS += 100 * u.Fuel / u.FuelMax; sZ++; }
            if (u.AmmoMax > 0) { mS += 100 * u.Ammo / u.AmmoMax; mZ++; }
        }
        return (Money(me), sZ > 0 ? (sS / sZ) & 0xFF : 0, mZ > 0 ? (mS / mZ) & 0xFF : 0,
                me is >= 0 and < 8 ? _killCount[me] : 0,
                me is >= 0 and < 8 ? _lossCount[me] : 0);
    }

    // ---- die Zeilen an festen Blockpunkten ------------------------------------

    /// <summary>Zeichnet Text an BLOCKpunkte (204×170-Raster des Originals) — der Leerzweig
    /// und Zeile 2 des Einheitenzweigs. Ein Godot-Label kann nur gleichmäßige Zeilen; der
    /// Leerzweig hat 16 Punkte zur ersten Lücke und danach 13.</summary>
    private sealed partial class BlockZeilenFeld : Control
    {
        public Font? Schrift;
        public int Groesse = 26;
        public float Massstab = 2f;
        public Color Farbe = Colors.White;
        private readonly List<(int X, int Y, string T)> _zeilen = new();

        public int Anzahl => _zeilen.Count;
        public IReadOnlyList<(int X, int Y, string T)> Zeilen => _zeilen;

        public BlockZeilenFeld()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            TextureFilter = TextureFilterEnum.Nearest;
        }

        public void Setze(List<(int X, int Y, string T)>? neu)
        {
            neu ??= new List<(int, int, string)>();
            bool gleich = neu.Count == _zeilen.Count;
            for (int i = 0; gleich && i < neu.Count; i++) gleich = neu[i] == _zeilen[i];
            if (gleich) return;
            _zeilen.Clear();
            _zeilen.AddRange(neu);
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (Schrift == null) return;
            float auf = Schrift.GetAscent(Groesse);
            foreach (var z in _zeilen)
                DrawString(Schrift, new Vector2(z.X * Massstab, z.Y * Massstab + auf), z.T,
                           HorizontalAlignment.Left, -1, Groesse, Farbe);
        }

        /// <summary>Unterkante der untersten Zeile in Blockpunkten — für den Prüfstand.</summary>
        public float UnterkanteBlock()
        {
            if (Schrift == null || _zeilen.Count == 0) return 0;
            float h = Schrift.GetHeight(Groesse) / Massstab;
            float u = 0;
            foreach (var z in _zeilen) u = Mathf.Max(u, z.Y + h);
            return u;
        }
    }

    private BlockZeilenFeld? _blockZeilen;

    private void BlockZeilenAnlegen(CanvasLayer layer)
    {
        _blockZeilen = new BlockZeilenFeld { Visible = false };
        layer.AddChild(_blockZeilen);
    }

    /// <summary>Lage, Schrift und Farbe nachziehen — aus <c>SetPanelBox</c> und
    /// <c>SetUiFont</c>.</summary>
    private void BlockZeilenLage()
    {
        if (_blockZeilen == null) return;
        _blockZeilen.Position = _panelOrigin;
        _blockZeilen.Massstab = _panelScale > 0 ? _panelScale : 2f;
        _blockZeilen.Size = new Vector2(204, 170) * _blockZeilen.Massstab;
        if (_uiFont != null) { _blockZeilen.Schrift = _uiFont; _blockZeilen.Groesse = _uiFontSize; }
        _blockZeilen.Farbe = _panel.GetThemeColor("font_color");
        _blockZeilen.QueueRedraw();
    }

    /// <summary>Die festen Zeilen geht es nur mit der echten Blocklage
    /// (<c>SetPanelBox(box, ecke, vergroesserung)</c>); sonst bleibt der alte Fließtext.</summary>
    private bool BlockZeilenMoeglich => _blockZeilen != null && _panelScale > 0f && _uiFont != null;

    /// <summary>Den Leerzweig zeigen: sechs Zeilen an festen Punkten, kein Bild, keine Balken.</summary>
    private void ZeigeLeerzweig()
    {
        _panel.Visible = _panelTextOn;
        if (LeerzweigAlt || !BlockZeilenMoeglich)
        {
            PanelHoehe(false);
            _panel.Text = MissionSummaryText();
            _blockZeilen?.Setze(null);
            ShowPanelBars(null);
            return;
        }
        PanelHoehe(false);
        _panel.Text = "";
        var z = MissionsZeilen();
        var l = new List<(int, int, string)>();
        for (int i = 0; i < z.Count; i++) l.Add((11, LeerzweigY[i], z[i]));
        _blockZeilen!.Visible = _panelTextOn;
        _blockZeilen.Setze(l);
        ShowPanelBars(null);
    }

    /// <summary>Zeile 2 an (11,122) oder nichts.</summary>
    private void BlockZeile2Setzen(string text)
    {
        if (_blockZeilen == null) return;
        _blockZeilen.Visible = _panelTextOn;
        _blockZeilen.Setze(text.Length > 0
            ? new List<(int, int, string)> { (11, 122, text) } : null);
    }

    /// <summary>
    /// <c>--aufbauteil-alt</c>-Weiche für die Spalte »Aufbauteil« der Einheitenliste
    /// (bug-423, Maße nachgezogen). Mit der richtig großen Schrift lief unser langer
    /// Tafelname »Schwere Bordkanone« in die Spalte »Munition« (Bild 04.10.). Das Original
    /// schreibt dort den KURZEN Bauteilnamen der Rohbytes <c>+0x0D</c>/<c>+0x0E</c>
    /// (Kopf von <see cref="UI.UnitListView"/>), K1 Platz 0: Zeile 2 »S.Kanone« — und der
    /// passt in seine Spalte, wie »S.Ketten« in »Fahrwerk«.
    /// </summary>
    private static string AufbauteilName(Entity e)
    {
        if (AufbauteilAlt) return MountName(e);
        int row = e.Comp0D != 0 ? e.Comp0D : e.Part;
        string n = row > 0 ? UI.UnitStatBook.ComponentPlain(row) : "";
        return n.Length > 0 && !n.StartsWith('?') ? n : MountName(e);
    }

    // ---- für den Prüfstand ----------------------------------------------------

    /// <summary>Was der Block gerade zeigt: Namensstreifen, feste Zeilen, Streifenhöhe.</summary>
    public (string Streifen, List<string> Zeilen, float Hoehe, float Unterkante, int SichtbareZeilen)
        BlockStand()
    {
        UpdatePanel();
        var z = new List<string>();
        if (_blockZeilen != null && _blockZeilen.Visible)
            foreach (var x in _blockZeilen.Zeilen) z.Add($"({x.X},{x.Y}) {x.T}");
        return (_panel.Text, z, _panel.Size.Y, _blockZeilen?.UnterkanteBlock() ?? 0,
                _panel.GetVisibleLineCount());
    }

    /// <summary>Den Block auf eine Einheit/ein Gebäude/nichts stellen. ⚠ EINGRIFF für den
    /// Prüfstand: ohne <c>SetPrimary</c>, damit kein Gebäudefenster aufgeht.</summary>
    public void BlockAuswahl(int idx)
    {
        _sel.Clear();
        _selAir = -1;
        _selected = -1;
        if (idx >= 0 && idx < _entities.Count) { _sel.Add(idx); _selected = idx; }
        UpdatePanel();
    }

    public int ErstesEigenesGebaeude()
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding && !e.Dead && e.Owner == ViewPlayer) return i;
        }
        return -1;
    }

    /// <summary>Irgendein stehendes Gebäude (für Karten ohne eigenes).</summary>
    public int ErstesGebaeude()
    {
        for (int i = 0; i < _entities.Count; i++)
            if (_entities[i].IsBuilding && !_entities[i].Dead && !_entities[i].NoStructure) return i;
        return -1;
    }

    /// <summary>Der alte Fließtext — die zweite, unabhängige Rechnung derselben Zahlen.</summary>
    public string MissionSummaryAlt() => MissionSummaryText();

    public string GebaeudeKurz(int idx)
        => idx >= 0 && idx < _entities.Count
            ? $"Platz {_entities[idx].Slot} »{_entities[idx].Name}« Besitzer {_entities[idx].Owner}" : "keins";

    public int IndexOfSlot(int slot)
    {
        for (int i = 0; i < _entities.Count; i++)
            if (!_entities[i].IsBuilding && !_entities[i].IsProp && _entities[i].Slot == slot) return i;
        return -1;
    }

    /// <summary>Der Entwurfsname über <c>+0x3E</c>, ohne den Rückfall auf den alten Weg
    /// (leer, wenn die Tafel ihn nicht hat) — die Sollzeile des Prüfstands.</summary>
    public string EntwurfsNameVon(int idx)
    {
        if (idx < 0 || idx >= _entities.Count) return "";
        var e = _entities[idx];
        if (e.Name.Length > 0) return e.Name;
        if (e.GameUnitType is 4 or 5) return LabelOf(e);
        LoadDesigns();
        int v = ViewPlayer is >= 0 and < 8 ? ViewPlayer : 0;
        return e.Entwurf3E is >= 0 and < 200
               && _designBySlot.TryGetValue(e.Entwurf3E + 200 * v, out var d) ? d.Name : "";
    }

    /// <summary>Bild- und Namensangaben einer Einheit für die Prüfzeile.</summary>
    public string EinheitKurz(int idx)
    {
        if (idx < 0 || idx >= _entities.Count) return "keine";
        var e = _entities[idx];
        return $"Platz {e.Slot} +0x3E={e.Entwurf3E} +0x43={e.Mark} +0x28={e.Rating28} "
             + $"Waffe {e.Weapon} »{MountName(e)}« Entwurf »{EntwurfsName(e)}«";
    }
}
