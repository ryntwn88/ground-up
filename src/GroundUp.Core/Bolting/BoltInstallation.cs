using System.Numerics;
using GroundUp.Core.Gear;
using GroundUp.Core.Geology;
using GroundUp.Core.Physics;
using GroundUp.Core.Stamina;

namespace GroundUp.Core.Bolting;

public enum BoltInstallStage
{
    DrillingHole,
    HoleDepthReached,
    BoltInsertedUntorqued,
    NutTightened,
    QuickdrawHung
}

/// <summary>
/// Tracks the multi-stage physical installation of a fixed cliff bolt:
/// progressive hole drilling -> dust blow-out -> bolt & hanger insertion -> wrench nut torquing -> quickdraw clipping.
/// </summary>
public sealed class BoltInstallation
{
    public string Id { get; }
    public BoltSpec Spec { get; }
    public Vector3 WallLocalPositionMeters { get; }
    public Vector3 WorldPositionMeters { get; }
    public float SubstrateIntegrity { get; }

    public BoltInstallStage Stage { get; private set; } = BoltInstallStage.DrillingHole;
    public float CurrentDepthMillimeters { get; private set; }
    public float DustFillRatio { get; private set; }
    public bool WasHoleCleanWhenBoltInserted { get; private set; } = true;
    public float ResidualDustAtInsert { get; private set; }
    public float NutTorqueRatio { get; private set; }
    public PlacementEvaluation? InstalledEvaluation { get; private set; }

    public BoltInstallation(
        string id,
        BoltSpec spec,
        Vector3 wallLocalPositionMeters,
        Vector3 worldPositionMeters,
        float substrateIntegrity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(spec);

        Id = id;
        Spec = spec;
        WallLocalPositionMeters = wallLocalPositionMeters;
        WorldPositionMeters = worldPositionMeters;
        SubstrateIntegrity = Math.Clamp(substrateIntegrity, 0.05f, 1.0f);
    }

    public float TargetDepthMillimeters => Spec.HoleDepthMillimeters;

    public float DepthProgressRatio =>
        TargetDepthMillimeters > 0f ? Math.Clamp(CurrentDepthMillimeters / TargetDepthMillimeters, 0f, 1f) : 1f;

    public bool IsCompletedBolt =>
        Stage is BoltInstallStage.NutTightened or BoltInstallStage.QuickdrawHung;

    public bool HasQuickdraw => Stage == BoltInstallStage.QuickdrawHung;

    /// <summary>
    /// Advances hole penetration for <paramref name="deltaSeconds"/> of hammer/drill operation
    /// (with automatic bit rotation), accumulating pulverized rock dust that slows drilling until blown out.
    /// </summary>
    public bool AdvanceDrilling(float deltaSeconds, IDrillTool drillTool, GeologyProfile geology)
    {
        ArgumentNullException.ThrowIfNull(drillTool);
        ArgumentNullException.ThrowIfNull(geology);

        if (deltaSeconds <= 0f || Stage != BoltInstallStage.DrillingHole)
        {
            return Stage >= BoltInstallStage.HoleDepthReached;
        }

        float rockSpeedMultiplier = 1.0f / Math.Max(0.28f, geology.DrillHardness);
        float baseMmPerSec = Math.Max(0.05f, drillTool.BasePenetrationMillimetersPerSecond) * rockSpeedMultiplier;

        // Integrate in <= 0.25s substeps so dust accumulation slows drilling identically at 60 FPS or large test steps.
        float remaining = deltaSeconds;
        while (remaining > 0.0001f && Stage == BoltInstallStage.DrillingHole)
        {
            float step = MathF.Min(0.25f, remaining);
            remaining -= step;

            // Hand-drill tube bits pack rock flour rapidly (-72% speed at full choke);
            // SDS rotary hammer spiral flutes self-evacuate most dust while drilling (-32% max slowdown).
            float maxDustSlowdown = drillTool.IsPowerTool ? 0.32f : 0.72f;
            float dustSpeedFactor = 1.0f - (DustFillRatio * maxDustSlowdown);
            float gainedMm = baseMmPerSec * dustSpeedFactor * step;
            CurrentDepthMillimeters = Math.Min(TargetDepthMillimeters, CurrentDepthMillimeters + gainedMm);

            // ~28mm of pulverized stone chokes the bore to 100% dust capacity, requiring 2-3 blow-outs on an 89mm bolt hole.
            DustFillRatio = Math.Clamp(DustFillRatio + (gainedMm / 28.0f), 0f, 1.0f);

            if (CurrentDepthMillimeters >= TargetDepthMillimeters - 0.01f)
            {
                CurrentDepthMillimeters = TargetDepthMillimeters;
                Stage = BoltInstallStage.HoleDepthReached;
                return true;
            }
        }

        return Stage >= BoltInstallStage.HoleDepthReached;
    }

    /// <summary>
    /// Blows pulverized rock dust out of an open hole before bolt insertion.
    /// </summary>
    public bool TryBlowOutDust(out string message)
    {
        if (Stage is not BoltInstallStage.DrillingHole and not BoltInstallStage.HoleDepthReached)
        {
            message = "Bolt is already in the hole—cannot blow out dust after insertion.";
            return false;
        }

        DustFillRatio = 0f;
        message = Stage == BoltInstallStage.HoleDepthReached
            ? "Blew rock dust clear! Hole is clean and ready for bolt & hanger insertion [T]."
            : $"Blew rock dust clear ({CurrentDepthMillimeters:F0}/{TargetDepthMillimeters:F0} mm)—drilling speed restored!";
        return true;
    }

    public bool TryInsertBoltAndHanger(out string message)
    {
        if (Stage == BoltInstallStage.DrillingHole)
        {
            message = $"Hole is too shallow ({CurrentDepthMillimeters:F0}/{TargetDepthMillimeters:F0} mm) to accept {Spec.DisplayName}.";
            return false;
        }

        if (Stage != BoltInstallStage.HoleDepthReached)
        {
            message = "Bolt and hanger are already inserted.";
            return false;
        }

        ResidualDustAtInsert = DustFillRatio;
        WasHoleCleanWhenBoltInserted = DustFillRatio <= 0.15f;
        NutTorqueRatio = 0f;
        Stage = BoltInstallStage.BoltInsertedUntorqued;

        message = WasHoleCleanWhenBoltInserted
            ? "Tapped bolt & hanger into clean hole. Tighten the hex nut with wrench [T] before clipping!"
            : "WARNING: Drove bolt into a dust-choked hole! Tighten hex nut [T] (holding strength reduced by rock dust).";
        return true;
    }

    /// <summary>
    /// Cranks the wrench to torque the nut and expand the mechanical wedge clip inside the hole.
    /// </summary>
    public bool TryTightenNut(float torqueEffort, GeologyProfile geology, out string message)
    {
        ArgumentNullException.ThrowIfNull(geology);

        if (Stage < BoltInstallStage.BoltInsertedUntorqued)
        {
            message = "Insert the bolt and hanger into a full-depth hole first!";
            return false;
        }

        if (Stage >= BoltInstallStage.NutTightened)
        {
            message = "Hex nut is already torqued to spec—ready to clip quickdraw [V]!";
            return true;
        }

        NutTorqueRatio = Math.Clamp(NutTorqueRatio + Math.Max(0.05f, torqueEffort), 0f, 1.0f);
        if (NutTorqueRatio < 1.0f)
        {
            message = $"Tightening hex nut with wrench ({NutTorqueRatio:P0} torque)...";
            return true;
        }

        Stage = BoltInstallStage.NutTightened;

        PlacementEvaluation rawEval = ProtectionEvaluator.EvaluateDrilledBolt(
            Spec,
            WorldPositionMeters.Y,
            SubstrateIntegrity,
            geology);

        // Un-blown rock flour lubricates the wedge sleeve inside the hole, cutting pull-out strength up to 45%!
        float dustHoldMultiplier = WasHoleCleanWhenBoltInserted
            ? 1.0f
            : Math.Clamp(1.0f - (ResidualDustAtInsert * 0.45f), 0.50f, 0.95f);

        float effectiveKn = MathF.Round(rawEval.EffectiveHoldingForceKilonewtons * dustHoldMultiplier, 2);
        PlacementQuality finalQuality = effectiveKn switch
        {
            < 4.0f => PlacementQuality.SketchyBodyWeightOnly,
            < 7.5f => PlacementQuality.Marginal,
            < 11.5f => PlacementQuality.Solid,
            _ => PlacementQuality.Bomber
        };
        if (SubstrateIntegrity < 0.45f)
        {
            finalQuality = PlacementQuality.SketchyBodyWeightOnly;
        }

        string note = WasHoleCleanWhenBoltInserted
            ? rawEval.DiagnosticSummary
            : $"{rawEval.DiagnosticSummary} (Dust-choked hole penalty -> {effectiveKn:F1} kN)";

        InstalledEvaluation = new PlacementEvaluation(
            Spec,
            Id,
            WorldPositionMeters.Y,
            FitsGeometry: true,
            EffectiveHoldingForceKilonewtons: effectiveKn,
            Quality: finalQuality,
            DiagnosticSummary: note);

        message = "Hex nut torqued snug against hanger! Ready to hang & clip quickdraw [V].";
        return true;
    }

    public bool TryHangQuickdraw(out string message)
    {
        if (Stage < BoltInstallStage.NutTightened)
        {
            message = Stage == BoltInstallStage.BoltInsertedUntorqued
                ? $"Cannot clip quickdraw yet—tighten the hex nut first ({NutTorqueRatio:P0} torqued) [T]!"
                : "Finish drilling and tightening the bolt before hanging a quickdraw!";
            return false;
        }

        Stage = BoltInstallStage.QuickdrawHung;
        message = $"Hung quickdraw on {Id} and clipped the rope!";
        return true;
    }
}
