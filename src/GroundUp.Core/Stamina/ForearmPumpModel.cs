using GroundUp.Core.Geology;

namespace GroundUp.Core.Stamina;

public enum ClimberActionState
{
    FreeClimbing,
    StanceInspect,
    HangingOnGear,
    DrillingBolt
}

/// <summary>
/// Modular drill abstraction supporting ground-up hand drills in Phase 0-2
/// and unlockable cordless rotary hammer drills in Phase 2/4.
/// </summary>
public interface IDrillTool
{
    string Name { get; }
    bool IsPowerTool { get; }
    float BasePenetrationMillimetersPerSecond { get; }
    float ForearmEffortPerSecond { get; }
}

public sealed record HandDrillTool(
    string Name = "Chromoly Hand Drill + 18oz Hammer",
    float BasePenetrationMillimetersPerSecond = 0.85f,
    float ForearmEffortPerSecond = 2.65f) : IDrillTool
{
    public bool IsPowerTool => false;
}

public sealed record CordlessPowerDrillTool(
    string Name = "24V SDS-Plus Rotary Hammer",
    float BasePenetrationMillimetersPerSecond = 5.50f,
    float ForearmEffortPerSecond = 0.70f) : IDrillTool
{
    public bool IsPowerTool => true;
}

public sealed class ForearmPumpModel
{
    public const float MaxPump = 100.0f;

    public float CurrentPump { get; private set; }

    public bool IsPumpedOut => CurrentPump >= MaxPump;

    public ForearmPumpModel(float initialPump = 0f)
    {
        CurrentPump = Math.Clamp(initialPump, 0f, MaxPump);
    }

    /// <summary>
    /// Computes net forearm pump change per second (+ = pumping out, - = recovering).
    /// Captures the core route-development dilemma: racing the pump clock in a free stance
    /// versus trusting body weight to a marginal skyhook or cam to rest and drill.
    /// </summary>
    public static float ComputePumpRatePerSecond(
        ClimberActionState actionState,
        float cliffOverhangDegrees,
        EdgeFeature? activeHandHold = null,
        StanceFeature? activeStance = null,
        bool isDrillingWhileHangingOnGear = false,
        IDrillTool? drillTool = null,
        GeologyProfile? geology = null)
    {
        // Hanging on gear/hooks lets climbing grip recover, even if swinging a hand drill mildly taxes one arm.
        if (actionState == ClimberActionState.HangingOnGear)
        {
            return -3.20f;
        }

        if (actionState == ClimberActionState.DrillingBolt && isDrillingWhileHangingOnGear)
        {
            IDrillTool tool = drillTool ?? new HandDrillTool();
            float hardnessScale = geology is null ? 1.0f : (0.55f + (geology.DrillHardness * 0.65f));
            return (tool.ForearmEffortPerSecond * 0.45f * hardnessScale) - 1.40f;
        }

        // Free stance or climbing: wall angle + hold size + stance support govern grip load.
        float angleLoad = cliffOverhangDegrees switch
        {
            < -10f => 0.55f,
            <= 0f => 1.00f + ((cliffOverhangDegrees + 10f) * 0.045f),
            _ => 1.45f + (cliffOverhangDegrees * 0.085f)
        };

        float holdDifficulty = 1.0f;
        if (activeHandHold is not null)
        {
            float depthFactor = Math.Clamp(22f / activeHandHold.DepthMillimeters, 0.55f, 2.35f);
            float incutModifier = 1.0f - (activeHandHold.IncutDegrees / 65f);
            float cleanlinessModifier = 1.0f / activeHandHold.Surface.CleanlinessMultiplier;
            holdDifficulty = depthFactor * incutModifier * cleanlinessModifier;
        }

        float stanceRelief = 1.0f;
        if (activeStance is not null && !activeStance.IsTrundled)
        {
            if (activeStance.AllowsNoHandsRest && actionState != ClimberActionState.DrillingBolt)
            {
                return -2.40f;
            }

            float areaRelief = Math.Clamp(activeStance.FootAreaSqCentimeters / 400f, 0.10f, 0.72f);
            float slopePenalty = Math.Clamp(activeStance.SlopeDegrees / 50f, 0f, 0.55f);
            stanceRelief = Math.Clamp(1.0f - (areaRelief * (1.0f - slopePenalty)), 0.28f, 1.0f);
        }

        float baseClimbDrain = 1.65f * angleLoad * holdDifficulty * stanceRelief;

        return actionState switch
        {
            ClimberActionState.FreeClimbing => MathF.Round(baseClimbDrain, 2),
            // Locking off on one hand to inspect rock / place gear drains ~1.55x faster than two-handed climbing!
            ClimberActionState.StanceInspect => MathF.Round(baseClimbDrain * 1.55f, 2),
            ClimberActionState.DrillingBolt => MathF.Round(
                (baseClimbDrain * 1.65f) + ComputeDrillArmLoad(drillTool, geology), 2),
            _ => 0f
        };
    }

    public bool AdvanceSeconds(
        float deltaSeconds,
        ClimberActionState actionState,
        float cliffOverhangDegrees,
        EdgeFeature? activeHandHold = null,
        StanceFeature? activeStance = null,
        bool isDrillingWhileHangingOnGear = false,
        IDrillTool? drillTool = null,
        GeologyProfile? geology = null)
    {
        if (deltaSeconds <= 0f)
        {
            return IsPumpedOut;
        }

        float rate = ComputePumpRatePerSecond(
            actionState,
            cliffOverhangDegrees,
            activeHandHold,
            activeStance,
            isDrillingWhileHangingOnGear,
            drillTool,
            geology);

        CurrentPump = Math.Clamp(CurrentPump + (rate * deltaSeconds), 0f, MaxPump);
        return IsPumpedOut;
    }

    /// <summary>
    /// Computes expected drilling time in seconds for a bolt hole given rock hardness and drill tool.
    /// </summary>
    public static float EstimateBoltDrillTimeSeconds(
        float holeDepthMillimeters,
        GeologyProfile geology,
        IDrillTool? drillTool = null)
    {
        ArgumentNullException.ThrowIfNull(geology);
        IDrillTool tool = drillTool ?? new HandDrillTool();

        // Soft sandstone (0.35 hardness) drills ~2.6x faster than crystalline granite (1.00 hardness).
        float rockSpeedMultiplier = 1.0f / Math.Max(0.28f, geology.DrillHardness);
        float effectiveMmPerSec = tool.BasePenetrationMillimetersPerSecond * rockSpeedMultiplier;
        return MathF.Round(holeDepthMillimeters / effectiveMmPerSec, 1);
    }

    private static float ComputeDrillArmLoad(IDrillTool? drillTool, GeologyProfile? geology)
    {
        IDrillTool tool = drillTool ?? new HandDrillTool();
        float hardnessMultiplier = geology is null ? 1.0f : (0.65f + (geology.DrillHardness * 0.55f));
        return tool.ForearmEffortPerSecond * hardnessMultiplier;
    }
}
