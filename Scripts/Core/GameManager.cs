namespace AkteEuropaReborn.Core;

using Godot;
using AkteEuropaReborn.Simulation;

[GlobalClass]
public partial class GameManager : Node
{
    public static GameManager Instance { get; private set; }

    public override void _Notification(int what)
    {
        if (what == NotificationSceneInstantiated)
            Instance = this;
    }

    public override void _Ready()
    {
        GD.Print("GameManager ready");
        // ⚠ Schadensbehebung, kein Nachbau: bis zum 21.08.2026 hat der
        // Musikregler die Lautstaerke des MIDI-Geraets SYSTEMWEIT verstellt,
        // und dieser Wert ueberlebt das Beenden des Spiels. Wer damals am
        // Regler war, hat sein Geraet womoeglich immer noch leise. Einmal beim
        // Start auf voll zuruecksetzen. Siehe MidiMusic.Volume.
        Audio.MidiMusic.GeraeteLautstaerkeZuruecksetzen();
        // There used to be a HookButtons(GetTree()) here. It gave every button
        // sound 600, which is a spoken sub-mission line, not a click — see the
        // note at the top of GameSounds. The original's menu is silent.
    }
}