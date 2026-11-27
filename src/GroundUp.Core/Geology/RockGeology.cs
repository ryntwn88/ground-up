namespace GroundUp.Core.Geology;

public enum RockType
{
    Granite,
    Sandstone,
    Limestone,
    Basalt
}

/// <summary>
/// Macro physical properties of a cliff's parent rock type.
/// Drives friction, hand-drilling speed, and gear/hook blowout risk.
/// </summary>
public sealed record GeologyProfile(
    RockType Type,
    string DisplayName,
    float BaseFrictionCoefficient,
    float DrillHardness,
    float Friability)
{
    /// <summary>Bulletproof crystalline granite: high friction, bomber gear, grueling hand-drilling.</summary>
    public static readonly GeologyProfile Granite = new(
        RockType.Granite,
        "Crystalline Granite",
        BaseFrictionCoefficient: 0.85f,
        DrillHardness: 1.00f,
        Friability: 0.08f);

    /// <summary>Desert/canyon sandstone: high grip, fast drilling, high blowout risk on small cams and hooks.</summary>
    public static readonly GeologyProfile Sandstone = new(
        RockType.Sandstone,
        "Navajo / Wingate Sandstone",
        BaseFrictionCoefficient: 0.90f,
        DrillHardness: 0.35f,
        Friability: 0.42f);

    /// <summary>Sport/pocketed limestone: slicker polish risk, moderate drilling speed, moderate friability.</summary>
    public static readonly GeologyProfile Limestone = new(
        RockType.Limestone,
        "Karst Limestone",
        BaseFrictionCoefficient: 0.72f,
        DrillHardness: 0.62f,
        Friability: 0.18f);

    /// <summary>Columnar basalt: glassy edges, hard drilling, crisp fractures.</summary>
    public static readonly GeologyProfile Basalt = new(
        RockType.Basalt,
        "Columnar Basalt",
        BaseFrictionCoefficient: 0.78f,
        DrillHardness: 0.90f,
        Friability: 0.15f);

    public static GeologyProfile FromRockType(RockType rockType) => rockType switch
    {
        RockType.Granite => Granite,
        RockType.Sandstone => Sandstone,
        RockType.Limestone => Limestone,
        RockType.Basalt => Basalt,
        _ => throw new ArgumentOutOfRangeException(nameof(rockType), rockType, "Unsupported rock type.")
    };
}
