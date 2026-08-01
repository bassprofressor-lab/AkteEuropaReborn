namespace AkteEuropaReborn.Core;

/// <summary>
/// Technology types for research tree.
/// </summary>
public enum TechType : ushort
{
    None = 0,
    
    // Power
    AdvancedPower = 1,
    NuclearPower = 2,
    
    // Armor
    HeavyArmor = 10,
    CompositeArmor = 11,
    
    // Weapons
    AdvancedWeapons = 20,
    Railguns = 21,
    PlasmaWeapons = 22,
    
    // Detection
    StealthDetection = 30,
    AdvancedSensors = 31,
    
    // Superweapons
    NuclearMissile = 40,
    IonCannon = 41,
    Chronosphere = 42,
    IronCurtain = 43,
}