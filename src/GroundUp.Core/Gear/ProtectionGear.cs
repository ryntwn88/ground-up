namespace GroundUp.Core.Gear;

public enum GearKind
{
    Cam,
    Piton,
    Skyhook,
    Sling,
    Bolt
}

public abstract record GearSpecBase(
    string Code,
    string DisplayName,
    GearKind Kind,
    float RatedStrengthKilonewtons,
    float MassGrams);

/// <summary>
/// Spring-loaded camming device (SLCD). Usable range lies inside [MinWidthMm, MaxWidthMm],
/// with peak holding power in the 20%-65% retraction sweet spot.
/// </summary>
public sealed record CamSpec(
    string Code,
    string DisplayName,
    float MinWidthMillimeters,
    float MaxWidthMillimeters,
    float HeadDepthMillimeters,
    float RatedStrengthKilonewtons,
    float MassGrams)
    : GearSpecBase(Code, DisplayName, GearKind.Cam, RatedStrengthKilonewtons, MassGrams);

/// <summary>
/// Aid/route-development skyhook or talon hook. Designed to hold body weight while drilling,
/// not severe dynamic lead whippers.
/// </summary>
public sealed record SkyhookSpec(
    string Code,
    string DisplayName,
    float MinEdgeDepthMillimeters,
    float MaxLipSlopeToleranceDegrees,
    float RatedStrengthKilonewtons,
    float MassGrams)
    : GearSpecBase(Code, DisplayName, GearKind.Skyhook, RatedStrengthKilonewtons, MassGrams);

/// <summary>
/// Hammered steel/chromoly piton for incipient seams and flaring cracks.
/// </summary>
public sealed record PitonSpec(
    string Code,
    string DisplayName,
    float MinSeamWidthMillimeters,
    float MaxSeamWidthMillimeters,
    float BladeLengthMillimeters,
    float RatedStrengthKilonewtons,
    float MassGrams)
    : GearSpecBase(Code, DisplayName, GearKind.Piton, RatedStrengthKilonewtons, MassGrams);

/// <summary>
/// Sewn webbing runner for slinging rock horns, knobs, and chickenheads.
/// </summary>
public sealed record SlingSpec(
    string Code,
    string DisplayName,
    float LengthCentimeters,
    float RatedStrengthKilonewtons,
    float MassGrams)
    : GearSpecBase(Code, DisplayName, GearKind.Sling, RatedStrengthKilonewtons, MassGrams);

/// <summary>
/// Permanent fixed anchor placed via hand-drilling or rotary hammer drill.
/// </summary>
public sealed record BoltSpec(
    string Code,
    string DisplayName,
    float HoleDiameterMillimeters,
    float HoleDepthMillimeters,
    bool IsGlueIn,
    float RatedStrengthKilonewtons,
    float MassGrams)
    : GearSpecBase(Code, DisplayName, GearKind.Bolt, RatedStrengthKilonewtons, MassGrams);

/// <summary>
/// Realistic baseline catalog of trad, aid, and bolting hardware for ground-up route development.
/// </summary>
public static class StandardRack
{
    public static readonly CamSpec Cam01 = new("#0.1", "Micro Cam #0.1 (Red)", 6.5f, 11.4f, 18f, 5.0f, 52f);
    public static readonly CamSpec Cam02 = new("#0.2", "Micro Cam #0.2 (Yellow)", 7.8f, 12.7f, 20f, 6.0f, 56f);
    public static readonly CamSpec Cam03 = new("#0.3", "Cam #0.3 (Blue)", 13.8f, 23.4f, 26f, 8.0f, 70f);
    public static readonly CamSpec Cam04 = new("#0.4", "Cam #0.4 (Gray)", 15.5f, 26.7f, 28f, 9.0f, 78f);
    public static readonly CamSpec Cam05 = new("#0.5", "Cam #0.5 (Purple)", 19.6f, 33.5f, 32f, 12.0f, 93f);
    public static readonly CamSpec Cam075 = new("#0.75", "Cam #0.75 (Green)", 23.9f, 41.2f, 35f, 14.0f, 108f);
    public static readonly CamSpec Cam1 = new("#1", "Cam #1 (Red)", 30.2f, 52.1f, 40f, 14.0f, 124f);
    public static readonly CamSpec Cam2 = new("#2", "Cam #2 (Yellow)", 37.2f, 64.9f, 45f, 14.0f, 140f);
    public static readonly CamSpec Cam3 = new("#3", "Cam #3 (Blue)", 50.7f, 87.9f, 52f, 14.0f, 181f);
    public static readonly CamSpec Cam4 = new("#4", "Cam #4 (Gray)", 66.0f, 114.7f, 60f, 14.0f, 258f);

    public static readonly SkyhookSpec TalonHook = new("HOOK-TALON", "Micro Talon Hook", 3.0f, -8.0f, 2.2f, 38f);
    public static readonly SkyhookSpec CliffhangerHook = new("HOOK-STD", "Standard Skyhook", 6.0f, -4.0f, 3.2f, 54f);
    public static readonly SkyhookSpec WideRingHook = new("HOOK-WIDE", "Wide Ledge Hook", 14.0f, 2.0f, 3.8f, 75f);

    public static readonly PitonSpec Knifeblade = new("PIN-KB", "Chromoly Knifeblade", 1.8f, 5.5f, 55f, 10.0f, 62f);
    public static readonly PitonSpec LostArrow = new("PIN-LA", "Lost Arrow Piton", 4.5f, 11.0f, 75f, 14.0f, 98f);
    public static readonly PitonSpec AnglePiton = new("PIN-ANG", "5/8\" Angle Piton", 10.0f, 22.0f, 90f, 16.0f, 130f);

    public static readonly SlingSpec Dyneema60 = new("SLING-60", "60cm Dyneema Runner", 60f, 22.0f, 36f);
    public static readonly SlingSpec Nylon120 = new("SLING-120", "120cm Nylon Runner", 120f, 22.0f, 88f);

    public static readonly BoltSpec Expansion38 = new("BOLT-EXP-38", "3/8\" x 3.5\" Stainless Wedge Bolt", 9.5f, 89f, IsGlueIn: false, 25.0f, 115f);
    public static readonly BoltSpec GlueInWave = new("BOLT-GLUE", "Stainless Wave Glue-In Bolt", 12.0f, 100f, IsGlueIn: true, 30.0f, 145f);

    public static IReadOnlyList<CamSpec> AllCams =>
    [
        Cam01, Cam02, Cam03, Cam04, Cam05, Cam075, Cam1, Cam2, Cam3, Cam4
    ];
}
