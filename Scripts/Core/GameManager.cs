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
        // There used to be a HookButtons(GetTree()) here. It gave every button
        // sound 600, which is a spoken sub-mission line, not a click — see the
        // note at the top of GameSounds. The original's menu is silent.
    }
}