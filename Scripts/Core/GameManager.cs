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
    }
}