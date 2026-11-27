using System.Numerics;
using GroundUp.Core.Gear;
using GroundUp.Core.Geology;
using GroundUp.Core.Physics;
using GroundUp.Core.Stamina;
using Xunit;

namespace GroundUp.Core.Tests;

public sealed class FallAndStaminaSimulationTests
{
    [Fact]
    public void SimulateLeadFall_AboveBomberBoltHighOnWall_CatchesCleanlyWithoutGroundFall()
    {
        PlacedProtection[] chain =
        [
            new("bolt-1", StandardRack.Expansion38, "b1", HeightMeters: 4.0f, EffectiveHoldingForceKilonewtons: 24.0f, PlacementQuality.Bomber),
            new("bolt-2", StandardRack.Expansion38, "b2", HeightMeters: 9.0f, EffectiveHoldingForceKilonewtons: 24.0f, PlacementQuality.Bomber)
        ];

        FallSimulationResult result = RopeAndFallSimulator.SimulateLeadFall(
            climberHeightMeters: 10.5f,
            activeProtectionChain: chain,
            climberMassKg: 78f,
            slackMeters: 0.20f);

        Assert.Equal(FallOutcome.CaughtByGear, result.Outcome);
        Assert.Equal("bolt-2", result.CatchingPlacementId);
        Assert.Empty(result.BlownPieces);
        Assert.True(result.FinalRestingHeightMeters > 5.0f);
        Assert.InRange(result.PeakClimberImpactKilonewtons, 4.0f, 8.0f);
    }

    [Fact]
    public void SimulateLeadFall_OntoSkyhookAboveSolidCam_BlowsSkyhookAndCatchesOnCam()
    {
        PlacedProtection[] chain =
        [
            new("cam-solid", StandardRack.Cam1, "crack-1", HeightMeters: 7.0f, EffectiveHoldingForceKilonewtons: 13.5f, PlacementQuality.Bomber),
            new("hook-aid", StandardRack.CliffhangerHook, "edge-1", HeightMeters: 9.2f, EffectiveHoldingForceKilonewtons: 2.6f, PlacementQuality.SketchyBodyWeightOnly)
        ];

        FallSimulationResult result = RopeAndFallSimulator.SimulateLeadFall(
            climberHeightMeters: 9.8f,
            activeProtectionChain: chain,
            climberMassKg: 80f);

        Assert.Equal(FallOutcome.CaughtByGear, result.Outcome);
        Assert.Single(result.BlownPieces);
        Assert.Equal("hook-aid", result.BlownPieces[0].PlacementId);
        Assert.Equal("cam-solid", result.CatchingPlacementId);
        Assert.True(result.FinalRestingHeightMeters > 2.0f);
    }

    [Fact]
    public void SimulateLeadFall_MarginalGearChain_ZippersCompletelyIntoGroundFall()
    {
        // All three pieces sit high enough (> half active rope length) to be sequentially loaded before ground impact,
        // and each blows under dynamic pulley force until the climber decks.
        PlacedProtection[] marginalChain =
        [
            new("micro-1", StandardRack.Cam01, "f1", HeightMeters: 6.0f, EffectiveHoldingForceKilonewtons: 3.2f, PlacementQuality.SketchyBodyWeightOnly),
            new("micro-2", StandardRack.Cam02, "f2", HeightMeters: 7.6f, EffectiveHoldingForceKilonewtons: 3.8f, PlacementQuality.SketchyBodyWeightOnly),
            new("hook-top", StandardRack.TalonHook, "f3", HeightMeters: 9.0f, EffectiveHoldingForceKilonewtons: 1.9f, PlacementQuality.SketchyBodyWeightOnly)
        ];

        FallSimulationResult result = RopeAndFallSimulator.SimulateLeadFall(
            climberHeightMeters: 10.2f,
            activeProtectionChain: marginalChain,
            climberMassKg: 82f);

        Assert.Equal(FallOutcome.GroundFall, result.Outcome);
        Assert.Equal(3, result.BlownPieces.Count);
        Assert.Null(result.CatchingPlacementId);
    }

    [Fact]
    public void SimulateLeadFall_HighRunoutAboveLowFirstPiece_HitsGroundBeforeCatch()
    {
        PlacedProtection[] lowPieceOnly =
        [
            new("cam-low", StandardRack.Cam2, "crack-low", HeightMeters: 2.2f, EffectiveHoldingForceKilonewtons: 14.0f, PlacementQuality.Bomber)
        ];

        // At 5.0m above a 2.2m piece, climber falls > 5.6m and hits the ground!
        FallSimulationResult result = RopeAndFallSimulator.SimulateLeadFall(
            climberHeightMeters: 5.0f,
            activeProtectionChain: lowPieceOnly);

        Assert.Equal(FallOutcome.GroundFall, result.Outcome);
        Assert.Equal(0f, result.FinalRestingHeightMeters);
    }

    [Fact]
    public void SimulateLeadFall_WithZeroPlacedGear_ResultsInDirectGroundFall()
    {
        FallSimulationResult result = RopeAndFallSimulator.SimulateLeadFall(
            climberHeightMeters: 4.2f,
            activeProtectionChain: []);

        Assert.Equal(FallOutcome.GroundFall, result.Outcome);
        Assert.Equal(4.2f, result.TotalFallDistanceMeters, precision: 2);
    }

    [Fact]
    public void ForearmPump_LockingOneHandStanceDrainsFasterThanTwoHandFreeClimbing()
    {
        var crimp = new EdgeFeature("crimp", "Small Crimp", new Vector3(0, 6f, 0), 30f, depthMillimeters: 10f, incutDegrees: 2f);

        float climbRate = ForearmPumpModel.ComputePumpRatePerSecond(ClimberActionState.FreeClimbing, cliffOverhangDegrees: 10f, activeHandHold: crimp);
        float inspectRate = ForearmPumpModel.ComputePumpRatePerSecond(ClimberActionState.StanceInspect, cliffOverhangDegrees: 10f, activeHandHold: crimp);

        Assert.True(climbRate > 0f);
        Assert.True(inspectRate > climbRate * 1.45f);
    }

    [Fact]
    public void ForearmPump_HangingOnGearRecoversPump_AndAllowsSustainableHandDrilling()
    {
        var pump = new ForearmPumpModel(initialPump: 65f);

        pump.AdvanceSeconds(10f, ClimberActionState.HangingOnGear, cliffOverhangDegrees: 20f);
        Assert.True(pump.CurrentPump < 40f);

        // Drilling while hanging on a skyhook/cam in granite keeps net forearm load near-neutral or slightly negative.
        float hangDrillRate = ForearmPumpModel.ComputePumpRatePerSecond(
            ClimberActionState.DrillingBolt,
            cliffOverhangDegrees: 15f,
            isDrillingWhileHangingOnGear: true,
            drillTool: new HandDrillTool(),
            geology: GeologyProfile.Granite);

        Assert.True(hangDrillRate < 0.5f);
    }

    [Fact]
    public void HandDrillingFromFreeStance_PumpsOutInHardGranite_ButSucceedsInSoftSandstone()
    {
        var handDrill = new HandDrillTool();
        var smallEdge = new EdgeFeature("stance-crimp", "14mm Edge", new Vector3(0, 7f, 0), 45f, depthMillimeters: 14f, incutDegrees: 5f);
        var smallStance = new StanceFeature("micro-stance", "Dime Edge Foot Stance", new Vector3(0, 5.5f, 0), footAreaSqCentimeters: 85f, slopeDegrees: 18f);

        float graniteHoleTime = ForearmPumpModel.EstimateBoltDrillTimeSeconds(89f, GeologyProfile.Granite, handDrill);
        float sandstoneHoleTime = ForearmPumpModel.EstimateBoltDrillTimeSeconds(89f, GeologyProfile.Sandstone, handDrill);

        Assert.True(graniteHoleTime > sandstoneHoleTime * 2.5f);

        var graniteClimber = new ForearmPumpModel(initialPump: 20f);
        bool pumpedOutInGranite = graniteClimber.AdvanceSeconds(
            graniteHoleTime,
            ClimberActionState.DrillingBolt,
            cliffOverhangDegrees: 5f,
            activeHandHold: smallEdge,
            activeStance: smallStance,
            isDrillingWhileHangingOnGear: false,
            drillTool: handDrill,
            geology: GeologyProfile.Granite);

        var sandstoneClimber = new ForearmPumpModel(initialPump: 10f);
        bool pumpedOutInSandstone = sandstoneClimber.AdvanceSeconds(
            sandstoneHoleTime,
            ClimberActionState.DrillingBolt,
            cliffOverhangDegrees: -8f,
            activeHandHold: smallEdge,
            activeStance: smallStance,
            isDrillingWhileHangingOnGear: false,
            drillTool: handDrill,
            geology: GeologyProfile.Sandstone);

        Assert.True(pumpedOutInGranite);
        Assert.False(pumpedOutInSandstone);
    }

    [Fact]
    public void SimulateLeadFall_MarginalChainGroundFall_CapsTransmittedRopeForceAtPieceStrength()
    {
        PlacedProtection[] skyhooksOnly =
        [
            new("hook-low", StandardRack.TalonHook, "e1", HeightMeters: 6.5f, EffectiveHoldingForceKilonewtons: 1.9f, PlacementQuality.SketchyBodyWeightOnly),
            new("hook-high", StandardRack.CliffhangerHook, "e2", HeightMeters: 8.5f, EffectiveHoldingForceKilonewtons: 2.4f, PlacementQuality.SketchyBodyWeightOnly)
        ];

        FallSimulationResult result = RopeAndFallSimulator.SimulateLeadFall(
            climberHeightMeters: 9.5f,
            activeProtectionChain: skyhooksOnly,
            climberMassKg: 80f);

        Assert.Equal(FallOutcome.GroundFall, result.Outcome);
        Assert.Equal(2, result.BlownPieces.Count);
        Assert.Equal(2.4f, result.PeakTopPieceLoadKilonewtons, precision: 2);
        Assert.Equal(1.5f, result.PeakClimberImpactKilonewtons, precision: 2);
    }

    [Fact]
    public void SimulateLeadFall_RopeStretchGroundFall_ClearsCatchingPlacementId()
    {
        PlacedProtection[] lowCam =
        [
            new("cam-stretch-deck", StandardRack.Cam2, "crack-low", HeightMeters: 2.2f, EffectiveHoldingForceKilonewtons: 14.0f, PlacementQuality.Bomber)
        ];

        // Rope comes taut 0.15m above the ground, then stretches past 0m into a ground fall.
        FallSimulationResult result = RopeAndFallSimulator.SimulateLeadFall(
            climberHeightMeters: 4.0f,
            activeProtectionChain: lowCam,
            climberMassKg: 80f,
            slackMeters: 0.25f);

        Assert.Equal(FallOutcome.GroundFall, result.Outcome);
        Assert.Equal(0f, result.FinalRestingHeightMeters);
        Assert.Null(result.CatchingPlacementId);
    }

    [Fact]
    public void ForearmPump_NoHandsLedgeRestWorksOnVerticalWall_ButNotOnSteepOverhang()
    {
        var bigLedge = new StanceFeature("ledge", "Flat Ledge", new Vector3(0, 3f, 0), footAreaSqCentimeters: 450f, slopeDegrees: 5f);
        var hold = new EdgeFeature("edge", "Moderate Edge", new Vector3(0, 4.5f, 0), 45f, depthMillimeters: 16f, incutDegrees: 4f);

        float verticalRate = ForearmPumpModel.ComputePumpRatePerSecond(
            ClimberActionState.FreeClimbing,
            cliffOverhangDegrees: 4f,
            activeHandHold: hold,
            activeStance: bigLedge);

        float steepOverhangRate = ForearmPumpModel.ComputePumpRatePerSecond(
            ClimberActionState.FreeClimbing,
            cliffOverhangDegrees: 30f,
            activeHandHold: hold,
            activeStance: bigLedge);

        Assert.True(verticalRate < 0f);
        Assert.True(steepOverhangRate > 0f);
    }
}
