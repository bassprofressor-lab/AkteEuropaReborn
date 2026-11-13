namespace AkteEuropaReborn.Editor;

using System;
using Godot;

/// <summary>
/// ⭐ 13.11.2026 — D9 (Fable-Pruefung): DAS LAGER EINES GEBAEUDES.
///
/// <para><b>Was das Spiel zeigt und verbraucht</b> (Gebaeudeliste @0x47BBF2, bei uns
/// <c>MapEntityLayer</c> Gebaeudezeilen): Waffenteile bei Art 1, 2, 6, 9, 12;
/// Fahrwerkteile bei 1, 3, 6, 9, 12; Spezialteile bei 1, 4, 6, 9, 12; Terranium bei
/// 2, 3, 4, 6, 10, 12, 15. Datei sec3 +0x2C/+0x2E/+0x30 (Teile), +0x32 (Terranium).</para>
///
/// <para><b>GEMESSEN an den 55 gelieferten Karten</b> (scratchpad bld.py, nur gebaute
/// Saetze): Basis (1) und Depot (9) tragen Bauteile — am haeufigsten 1500/2000/1000,
/// 500/500/500, 1000/1500/800 —, die drei Fabriken (2/3/4) NUR Terranium (Teile dort
/// 0 in allen bis auf Einzelfaelle): 1000, 2000, 150, 500. Die Mine (10) und Art 15
/// tragen kleine Terraniummengen (50, 100, 120, 200). Bahnhoefe (6/12) sind
/// Umschlagplaetze und auf den Karten fast immer leer — dort wird nichts angeboten.
/// Die Stufen unten sind diese gemessenen Werte; welche als Vorgabe gilt, ist UNSERE
/// SETZUNG: »keine/0«, also genau das, was der Editor bis heute schrieb.</para>
///
/// <para><b>Der Gebaeudename wird NICHT angeboten:</b> das Original ueberschreibt das
/// Namensfeld (+0x1B) beim Missionsaufbau fuer alle 256 Plaetze mit der Platznummer
/// (0x440930, bug-443) und zeigt den Ortsnamen der Karte nirgends. Ein Namensfeld im
/// Editor haette im Spiel keine Wirkung.</para>
///
/// <para>⭐ Seit 13.11.2026 (Spielerentscheidung) wirkt das Lager im Gefecht mit der neuen
/// Rohstoffstufe »wie auf der Karte« (UI.SkirmishSetup.ResourcesKarte); die vier Stufen des
/// Originals fuellen alle Lager weiter neu (0x41A002).</para>
///
/// <para>Gegenschalter <c>--editor-lager-aus</c>: keine Auswahl, Lager immer leer.</para>
/// </summary>
public partial class MapEditOverlay
{
    public static bool LagerAus => Array.IndexOf(Core.CommandLine.Args, "--editor-lager-aus") >= 0;

    /// <summary>Bauteilstufen (Waffen, Fahrwerk, Spezial) — gemessen an Basis/Depot.</summary>
    public static readonly (string Name, int W, int F, int S)[] TeileStufen =
    {
        ("keine", 0, 0, 0),
        ("wenig (500/500/500)", 500, 500, 500),
        ("mittel (1000/1500/800)", 1000, 1500, 800),
        ("viel (1500/2000/1000)", 1500, 2000, 1000),
    };

    /// <summary>Terraniumstufen der Fabriken (2/3/4) — gemessen.</summary>
    public static readonly int[] TerraFabrik = { 0, 150, 500, 1000, 2000 };
    /// <summary>Terraniumstufen von Mine (10) und Art 15 — gemessen.</summary>
    public static readonly int[] TerraKlein = { 0, 50, 100, 200 };

    public static bool HatTeile(int typ) => !LagerAus && typ is 1 or 9;
    public static bool HatTerra(int typ) => !LagerAus && typ is 2 or 3 or 4 or 10 or 15;
    public static int[] TerraStufen(int typ) => typ is 10 or 15 ? TerraKlein : TerraFabrik;

    private int _teileStufe;            // Index in TeileStufen
    private int _terraStufe;            // Index in TerraStufen(_typ)

    /// <summary>Das Lager, das ein neues (oder angeklicktes) Gebaeude dieser Art bekommt.</summary>
    private (int W, int F, int S, int T) LagerFuer(int typ)
    {
        var t = HatTeile(typ) ? TeileStufen[Math.Clamp(_teileStufe, 0, TeileStufen.Length - 1)] : ("", 0, 0, 0);
        var st = TerraStufen(typ);
        int terra = HatTerra(typ) ? st[Math.Clamp(_terraStufe, 0, st.Length - 1)] : 0;
        return (t.Item2, t.Item3, t.Item4, terra);
    }

    /// <summary>Kurztext des Lagers fuer Statuszeile und Liste.</summary>
    private static string LagerText(int typ, (int W, int F, int S, int T) l)
        => HatTeile(typ) ? $"Lager Waffen {l.W} / Fahrwerk {l.F} / Spezial {l.S}"
         : HatTerra(typ) ? $"Lager {l.T} Terranium" : "";

    /// <summary>Ein Klick mit dem Gebaeudepinsel auf ein STEHENDES Gebaeude: dessen
    /// Lager auf die gewaehlte Stufe setzen. true, wenn dort ein Gebaeude stand.</summary>
    private bool LagerUebernehmen(Vector2I cell)
    {
        int idx = _layer.EditorBuildingAt(cell);
        if (idx < 0) return false;
        int slot = _layer.EditorSlotOf(idx);
        var s = _map.Sec(3);
        if (slot < 0 || s == null) return false;
        int o = slot * Import.CwmData.BuildingStride;
        if (o + Import.CwmData.BuildingStride > s.Length) return false;
        int typ = s[o + 0x04];
        if (!HatTeile(typ) && !HatTerra(typ))
        { _say = $"»{KachelsatzInfo.GebaeudeName(typ)}« hat kein Lager"; return true; }
        var l = LagerFuer(typ);
        void U16(int at, int v) { s[o + at] = (byte)(v & 0xFF); s[o + at + 1] = (byte)(v >> 8); }
        if (HatTeile(typ)) { U16(0x2c, l.W); U16(0x2e, l.F); U16(0x30, l.S); }
        if (HatTerra(typ)) U16(0x32, l.T);
        MapEditSession.Dirty = true;
        _mine.Add((cell.X, cell.Y, Brush.Gebaeude));
        _say = $"»{KachelsatzInfo.GebaeudeName(typ)}« (Platz {slot}): {LagerText(typ, l)}";
        return true;
    }

    // ---- Bedienfeld -----------------------------------------------------------

    private HBoxContainer _pLager = null!;
    private OptionButton _pTeile = null!, _pTerra = null!;
    private int _pTerraFuer = -1;

    private void LagerUiBauen(VBoxContainer v)
    {
        _pLager = new HBoxContainer();
        _pLager.AddThemeConstantOverride("separation", 6);
        var kopf = new Label { Text = "Lager:" };
        _pLager.AddChild(kopf);
        _pTeile = new OptionButton
        {
            FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Bauteile im Lager (Waffen / Fahrwerk / Spezial) — Werte wie auf den Originalkarten.\n" +
                          "Gilt für neue Gebäude; ein Klick auf ein stehendes Gebäude übernimmt es.\n" +
                          "Im Gefecht wirkt es mit der Rohstoffstufe »wie auf der Karte« — die anderen Stufen füllen alle Lager neu (wie im Original).",
        };
        foreach (var t in TeileStufen) _pTeile.AddItem($"Bauteile: {t.Name}");
        _pTeile.ItemSelected += i => { _teileStufe = (int)i; _say = ""; Refresh(); };
        _pLager.AddChild(_pTeile);
        _pTerra = new OptionButton
        {
            FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Terranium im Lager — Werte wie auf den Originalkarten.\n" +
                          "Gilt für neue Gebäude; ein Klick auf ein stehendes Gebäude übernimmt es.\n" +
                          "Im Gefecht wirkt es mit der Rohstoffstufe »wie auf der Karte« — die anderen Stufen füllen alle Lager neu (wie im Original).",
        };
        _pTerra.ItemSelected += i => { _terraStufe = (int)i; _say = ""; Refresh(); };
        _pLager.AddChild(_pTerra);
        v.AddChild(_pLager);
    }

    private void LagerUiAuffrischen()
    {
        if (_pLager == null) return;
        bool geb = _brush == Brush.Gebaeude;
        _pTeile.Visible = geb && HatTeile(_typ);
        _pTerra.Visible = geb && HatTerra(_typ);
        _pLager.Visible = _pTeile.Visible || _pTerra.Visible;
        _pTeile.Select(Math.Clamp(_teileStufe, 0, TeileStufen.Length - 1));
        if (_pTerra.Visible)
        {
            var st = TerraStufen(_typ);
            if (_pTerraFuer != (st == TerraKlein ? 1 : 0))
            {
                _pTerraFuer = st == TerraKlein ? 1 : 0;
                _pTerra.Clear();
                foreach (int t in st) _pTerra.AddItem($"Terranium: {t}");
            }
            _terraStufe = Math.Clamp(_terraStufe, 0, st.Length - 1);
            _pTerra.Select(_terraStufe);
        }
    }
}
