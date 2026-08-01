namespace AkteEuropaReborn.Core;

/// <summary>
/// Settings for skirmish matches.
/// </summary>
public class SkirmishSettings
{
    public string MapName { get; set; } = "default";
    public int MaxPlayers { get; set; } = 8;
    public int StartingCredits { get; set; } = 5000;
    public int StartingEnergy { get; set; } = 100;
    public bool FogOfWar { get; set; } = true;
    public bool Crates { get; set; } = true;
    public GameSpeed GameSpeed { get; set; } = GameSpeed.Normal;
    public VictoryCondition VictoryCondition { get; set; } = VictoryCondition.DestroyAll;
}

public enum GameSpeed : byte
{
    Slow = 0,
    Normal = 1,
    Fast = 2,
    Faster = 3
}

public enum VictoryCondition : byte
{
    DestroyAll = 0,
    CaptureFlags = 1,
    Survive = 2,
    Assassinate = 3
}