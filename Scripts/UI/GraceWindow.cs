namespace AkteEuropaReborn.UI;

using Godot;

/// <summary>
/// <b>»MISSION BEENDET — ZEIT FÜR UNBEENDETE UNTERMISSIONEN«</b>
///
/// <para>Das kleine Fenster, das der Spieler photographiert hat. Es erscheint,
/// wenn das Missionsziel erreicht ist, aber noch eine <b>Untermission</b> offen
/// steht: das Original beendet die Mission dann NICHT, sondern gibt eine
/// Nachfrist von zehn Spielminuten und zeigt sie hier herunterlaufen.</para>
///
/// <para><b>Der Wortlaut ist der des Originals</b>, aus dem Zeichner
/// <c>0x4872A0</c>, jede Zeile mit ihrer y-Stelle aus dem <c>push</c> davor:
/// <list type="bullet">
/// <item>Titel <c>0x502974</c> »Mission beendet«</item>
/// <item>y=17 <c>0x502968</c> »Zeit für«</item>
/// <item>y=29 <c>0x502958</c> »unbeendete«</item>
/// <item>y=41 <c>0x502944</c> »Untermissionen:«</item>
/// <item>darunter <c>0x502940</c> »00:« und der Zähler <c>byte[0x4F6FA4]</c>,
/// als Rohwert mit führender Null unter 10 (@0x487485) — daher das »00:09«
/// im Bildschirmfoto: neun Schritte übrig</item>
/// <item>der Knopf <c>0x5024F0</c> »Beenden«</item>
/// </list></para>
///
/// <para>Wo der Zähler herkommt und warum ein Schritt eine Spielminute ist,
/// steht bei <see cref="Campaign.MissionScript.TickGrace"/>.</para>
///
/// <para>⚠ <b>UNSER ist der AUFBAU</b> — Rahmen, Farben, Schriftgrössen. Das
/// Original zeichnet mit eigener Grafik; hier stehen Godot-Stilkästen. Die
/// Wörter, ihre Reihenfolge und die Zahl sind die des Originals.</para>
///
/// <para>⚠ Und unser ist, <b>was »Beenden« tut</b>: bei uns bricht es die
/// Nachfrist ab und schliesst die Mission sofort als Sieg. Im Original ist der
/// Knopf gelesen, seine Wirkung nicht — dass er dasselbe tut wie ein
/// abgelaufener Zähler, ist die naheliegende, aber ungeprüfte Deutung.</para>
/// </summary>
public sealed partial class GraceWindow : PanelContainer
{
    private Label? _rest;
    private Button? _knopf;

    /// <summary>Was »Beenden« auslöst.</summary>
    public System.Action? OnFinish;

    public override void _Ready()
    {
        var rahmen = new StyleBoxFlat
        {
            BgColor = Color.Color8(24, 28, 36),
            BorderColor = Color.Color8(150, 160, 175),
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 10,
            ContentMarginBottom = 10,
        };
        rahmen.SetBorderWidthAll(2);
        AddThemeStyleboxOverride("panel", rahmen);

        var spalte = new VBoxContainer();
        spalte.AddThemeConstantOverride("separation", 2);
        AddChild(spalte);

        var titel = new Label
        {
            Text = "MISSION BEENDET",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        titel.AddThemeColorOverride("font_color", Color.Color8(232, 96, 72));
        spalte.AddChild(titel);

        // Die drei Zeilen des Originals stehen dort untereinander (y=17/29/41);
        // wir setzen sie genauso, damit die Umbrueche dieselben sind.
        foreach (string z in new[] { "ZEIT FUER", "UNBEENDETE", "UNTERMISSIONEN:" })
        {
            var l = new Label { Text = z, HorizontalAlignment = HorizontalAlignment.Center };
            l.AddThemeColorOverride("font_color", Color.Color8(206, 212, 222));
            spalte.AddChild(l);
        }

        _rest = new Label { Text = "00:00", HorizontalAlignment = HorizontalAlignment.Center };
        _rest.AddThemeColorOverride("font_color", Color.Color8(244, 196, 96));
        spalte.AddChild(_rest);

        _knopf = new Button { Text = "BEENDEN" };
        _knopf.Pressed += () => OnFinish?.Invoke();
        spalte.AddChild(_knopf);
    }

    /// <summary>Den Zähler nachführen. <paramref name="rest"/> ist der Rohwert
    /// des Originals (0..10); die führende Null unter 10 macht das »00:09«.
    /// </summary>
    public void SetRest(int rest)
    {
        if (_rest != null) _rest.Text = $"00:{rest:00}";
    }

    public void SetFont(Font f, int size)
    {
        foreach (var kind in GetChildren())
            if (kind is VBoxContainer box)
                foreach (var n in box.GetChildren())
                {
                    if (n is Label l)
                    {
                        l.AddThemeFontOverride("font", f);
                        l.AddThemeFontSizeOverride("font_size", size);
                    }
                    else if (n is Button b)
                    {
                        b.AddThemeFontOverride("font", f);
                        b.AddThemeFontSizeOverride("font_size", size);
                    }
                }
    }
}
