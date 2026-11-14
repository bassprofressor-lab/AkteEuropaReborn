#nullable enable
namespace AkteEuropaReborn.UI;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// ⭐ 14.11.2026 — Fable C2 (Spielerwunsch »feste klassische Auswahlen«): EINE FESTE AUSWAHL
/// STATT HOCHZAEHLEN. Gegner, Konto, Techstandard und Menschen waren SpinBoxen, die man mit
/// Pfeilen hochzaehlte. Diese Klappliste traegt dieselben Eigenschaften wie die SpinBox
/// (<see cref="Value"/>, <see cref="MinValue"/>, <see cref="MaxValue"/>, <see cref="ValueChanged"/>),
/// damit der Code drumherum bleiben kann, zeigt aber eine Liste fester Werte mit Beschriftung.
///
/// <para>Die Werte kommen entweder aus <see cref="Werte"/> (feste Liste, z. B. Konto) oder als
/// Bereich <see cref="MinValue"/>..<see cref="MaxValue"/> (z. B. Gegner bis zur Platzzahl der
/// Karte). Ein gesetzter Wert, den die Liste nicht hat, nimmt den naechstkleineren — wie die
/// SpinBox auf ihren Bereich klemmt.</para>
/// </summary>
public partial class ZahlWahl : OptionButton
{
    private readonly List<int> _werte = new();
    private int[]? _fest;
    private double _min, _max = 1;
    private Func<int, string> _text = v => v.ToString();
    private bool _bau;

    /// <summary>Wie die SpinBox: gerufen, wenn der Spieler (oder der Code) den Wert aendert.</summary>
    public event Action<double>? ValueChanged;

    public ZahlWahl() => ItemSelected += _ => { if (!_bau) ValueChanged?.Invoke(Value); };

    /// <summary>Beschriftung je Wert (Vorgabe: die Zahl).</summary>
    public Func<int, string> Beschriftung { get => _text; set { _text = value; Neu(); } }

    /// <summary>Feste Werteliste statt Bereich (null = Bereich Min..Max).</summary>
    public int[]? Werte { get => _fest; set { _fest = value; Neu(); } }

    public double MinValue { get => _min; set { _min = value; Neu(); } }
    public double MaxValue { get => _max; set { _max = value; Neu(); } }

    public double Value
    {
        get => Selected >= 0 && Selected < _werte.Count ? _werte[Selected] : _min;
        set
        {
            if (_werte.Count == 0) return;
            int v = (int)Math.Round(value), best = 0;
            for (int i = 0; i < _werte.Count; i++) if (_werte[i] <= v) best = i;
            if (best == Selected) return;
            Select(best);
            if (!_bau) ValueChanged?.Invoke(Value);
        }
    }

    private void Neu()
    {
        double war = _werte.Count > 0 ? Value : double.NaN;
        _bau = true;
        Clear();
        _werte.Clear();
        if (_fest != null) _werte.AddRange(_fest);
        else for (int v = (int)_min; v <= (int)_max; v++) _werte.Add(v);
        foreach (int v in _werte) AddItem(_text(v));
        _bau = false;
        if (_werte.Count == 0) return;
        if (double.IsNaN(war)) { Select(0); return; }
        int keep = 0;
        for (int i = 0; i < _werte.Count; i++) if (_werte[i] <= war) keep = i;
        Select(keep);
        if (Math.Abs(Value - war) > 0.5) ValueChanged?.Invoke(Value);
    }
}
