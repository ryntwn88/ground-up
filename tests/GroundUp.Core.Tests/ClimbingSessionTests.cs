using GroundUp.Core.Climbing;
using GroundUp.Core.Gear;
using GroundUp.Core.Geology;
using GroundUp.Core.Physics;
using GroundUp.Core.Samples;
using Xunit;

namespace GroundUp.Core.Tests;

public sealed class ClimbingSessionTests
{
    [Fact]
    public void StepOntoWall_ReachesLowHolds_AndRejectsOutOfRangeSummitFeature()
    {
        CliffFace wall = TestCliffFactory.CreateTenMeterGraniteWall();
        var session = new ClimbingSession(wall);

        Assert.True(session.TryStepOntoWallFromGround());
        Assert.Equal(SessionMode.FreeClimbing, session.Mode);
        Assert.True(session.Rig.AttachedHandCount >= 2);
        Assert.True(session.Rig.AttachedFootCount >= 1);

        // Summit horn at 8.9m is far beyond human reach from the 1m starting stance.
        Assert.False(session.TryMoveLimbToFeature("horn-summit-knob"));
        Assert.Equal(SessionMode.FreeClimbing, session.Mode);
    }

    [Fact]
    public void OffBalanceBarnDoorStance_IncreasesForearmPumpMultiplier()
    {
        CliffFace wall = TestCliffFactory.CreateTenMeterGraniteWall();
        var centeredRig = new ClimberRigState();
        centeredRig.SetLimbAttachment(wall, new LimbAttachment(LimbSlot.LeftFoot, "stance-start-left", 0f, wall.GetFeatureWorldPositionMeters(wall.GetFeature<StanceFeature>("stance-start-left"))));
        centeredRig.SetLimbAttachment(wall, new LimbAttachment(LimbSlot.RightFoot, "stance-start-right", 0f, wall.GetFeatureWorldPositionMeters(wall.GetFeature<StanceFeature>("stance-start-right"))));
        centeredRig.SetLimbAttachment(wall, new LimbAttachment(LimbSlot.LeftHand, "edge-start-jug", 0f, wall.GetFeatureWorldPositionMeters(wall.GetFeature<EdgeFeature>("edge-start-jug"))));
        centeredRig.SetLimbAttachment(wall, new LimbAttachment(LimbSlot.RightHand, "crack-main-splitter", 0.05f, wall.GetFeatureWorldPositionMeters(wall.GetFeature<CrackFeature>("crack-main-splitter"), 0.05f)));

        var barnDoorRig = new ClimberRigState();
        barnDoorRig.SetLimbAttachment(wall, new LimbAttachment(LimbSlot.LeftFoot, "stance-start-left", 0f, wall.GetFeatureWorldPositionMeters(wall.GetFeature<StanceFeature>("stance-start-left"))));
        barnDoorRig.SetLimbAttachment(wall, new LimbAttachment(LimbSlot.LeftHand, "edge-low-right", 0f, wall.GetFeatureWorldPositionMeters(wall.GetFeature<EdgeFeature>("edge-low-right"))));
        barnDoorRig.SetLimbAttachment(wall, new LimbAttachment(LimbSlot.RightHand, "edge-low-right", 0f, wall.GetFeatureWorldPositionMeters(wall.GetFeature<EdgeFeature>("edge-low-right"))));

        Assert.Equal(1.0f, centeredRig.ComputeBalancePumpMultiplier(), precision: 2);
        Assert.True(barnDoorRig.ComputeBalancePumpMultiplier() > 1.30f);
    }

    [Fact]
    public void PullingOntoUntrundledHollowChossFlake_SnapsFlakeAndTriggersWhipperCaughtBySplitterCam()
    {
        CliffFace wall = TestCliffFactory.CreateTenMeterGraniteWall();
        var session = new ClimbingSession(wall);
        Assert.True(session.TryStepOntoWallFromGround());

        // Climb up the splitter and place a bomber #0.75 cam at ~3.75m.
        Assert.True(session.TryMoveLimbToFeature("crack-main-splitter", 0.20f, LimbSlot.LeftHand));
        Assert.True(session.TryMoveLimbToFeature("crack-main-splitter", 0.25f, LimbSlot.RightHand));
        Assert.True(session.TryMoveLimbToFeature("stance-low-ledge", 0.5f, LimbSlot.LeftFoot));
        Assert.True(session.TryMoveLimbToFeature("edge-mid-rail", 0.5f, LimbSlot.RightHand));
        Assert.True(session.TryMoveLimbToFeature("stance-mid-smear", 0.5f, LimbSlot.RightFoot));
        Assert.True(session.TryMoveLimbToFeature("crack-main-splitter", 0.52f, LimbSlot.LeftHand));
        Assert.True(session.TryMoveLimbToFeature("stance-flake-foot", 0.5f, LimbSlot.RightFoot));

        Assert.True(session.TryLockThreePointStance(LimbSlot.LeftHand));
        Assert.True(session.TryPlaceProtection(StandardRack.Cam075, "crack-main-splitter", 0.50f, out _, out PlacementEvaluation camEval));
        Assert.Equal(PlacementQuality.Bomber, camEval.Quality);
        Assert.True(session.ReturnToFreeClimbing());

        // Recklessly grab the un-sounded hollow dinnerplate flake at 4.4m -> SNAP + WHIPPER!
        var chossFlake = wall.GetFeature<EdgeFeature>("flake-hollow-choss");
        Assert.False(chossFlake.IsTrundled);

        bool moveSucceeded = session.TryMoveLimbToFeature("flake-hollow-choss", 0.5f, LimbSlot.RightHand);
        Assert.False(moveSucceeded);
        Assert.True(chossFlake.IsTrundled);
        Assert.Equal(SessionMode.WhipperFall, session.Mode);
        Assert.NotNull(session.LastFallResult);
        Assert.Equal(FallOutcome.CaughtByGear, session.LastFallResult!.Outcome);

        session.CompleteWhipperAftermath();
        Assert.Equal(SessionMode.HangingOnGear, session.Mode);
    }

    [Fact]
    public void SoundingAndPryingHollowFlake_SafelyTrundlesWithoutFall()
    {
        CliffFace wall = TestCliffFactory.CreateTenMeterGraniteWall();
        var session = new ClimbingSession(wall);
        Assert.True(session.TryStepOntoWallFromGround());

        Assert.True(session.TryMoveLimbToFeature("crack-main-splitter", 0.22f, LimbSlot.LeftHand));
        Assert.True(session.TryMoveLimbToFeature("stance-low-ledge", 0.5f, LimbSlot.LeftFoot));
        Assert.True(session.TryMoveLimbToFeature("edge-mid-rail", 0.5f, LimbSlot.RightHand));
        Assert.True(session.TryMoveLimbToFeature("stance-mid-smear", 0.5f, LimbSlot.RightFoot));
        Assert.True(session.TryMoveLimbToFeature("crack-main-splitter", 0.52f, LimbSlot.LeftHand));
        Assert.True(session.TryMoveLimbToFeature("stance-flake-foot", 0.5f, LimbSlot.RightFoot));

        Assert.True(session.TryLockThreePointStance(LimbSlot.LeftHand));
        Assert.True(session.SelectWorkTool(ActiveToolKind.HammerSound));
        Assert.True(session.TrySoundFeature("flake-hollow-choss", out RockSoundingResult sound));
        Assert.Equal(ResonanceType.HollowBoom, sound.Resonance);
        Assert.True(sound.AdvisesTrundle);

        Assert.True(session.SelectWorkTool(ActiveToolKind.Prybar));
        Assert.True(session.TryTrundleFeature("flake-hollow-choss"));
        Assert.NotEqual(SessionMode.WhipperFall, session.Mode);
        Assert.True(wall.GetFeature<EdgeFeature>("flake-hollow-choss").IsTrundled);
    }

    [Fact]
    public void MossySloper_SkyhookBlowsUnderBodyWeightWhenDirty_ButHoldsAfterWireBrushing()
    {
        CliffFace dirtyWall = TestCliffFactory.CreateTenMeterGraniteWall();
        var dirtySession = ClimbToMossySloperStance(dirtyWall);

        // Place solid backup cam below, then perch a TalonHook on the unbrushed mossy sloper.
        Assert.True(dirtySession.TryPlaceProtection(StandardRack.Cam075, "crack-main-splitter", 0.70f, out _, out _));
        Assert.True(dirtySession.TryPlaceProtection(StandardRack.TalonHook, "edge-mossy-sloper", 0.5f, out var dirtyHook, out _));

        // Weighting the hook on unbrushed moss blows the hook into a whipper!
        Assert.False(dirtySession.TryWeightPlacedGear(dirtyHook!.Value.Protection.PlacementId));
        Assert.Equal(SessionMode.WhipperFall, dirtySession.Mode);
        Assert.Equal(FallOutcome.CaughtByGear, dirtySession.LastFallResult!.Outcome);

        // Now repeat on a fresh wall, wire-brushing the moss first -> hook holds body weight cleanly!
        CliffFace cleanWall = TestCliffFactory.CreateTenMeterGraniteWall();
        var cleanSession = ClimbToMossySloperStance(cleanWall);
        Assert.True(cleanSession.TryBrushFeature("edge-mossy-sloper", brushEffort: 2.0f));
        Assert.True(cleanWall.GetFeature<EdgeFeature>("edge-mossy-sloper").Surface.IsClean);

        Assert.True(cleanSession.TryPlaceProtection(StandardRack.TalonHook, "edge-mossy-sloper", 0.5f, out var cleanHook, out _));
        Assert.True(cleanSession.TryWeightPlacedGear(cleanHook!.Value.Protection.PlacementId));
        Assert.Equal(SessionMode.HangingOnGear, cleanSession.Mode);

        // Hanging on the skyhook recovers forearm pump!
        float pumpBefore = cleanSession.Pump.CurrentPump;
        cleanSession.AdvanceTime(5.0f);
        Assert.True(cleanSession.Pump.CurrentPump <= pumpBefore);
    }

    [Fact]
    public void PumpingOutInOneHandedStance_AutomaticallyTriggersWhipper()
    {
        CliffFace wall = TestCliffFactory.CreateTenMeterGraniteWall();
        var session = new ClimbingSession(wall, initialPump: 92f);
        Assert.True(session.TryStepOntoWallFromGround());
        Assert.True(session.TryLockThreePointStance());

        session.AdvanceTime(15f);

        Assert.True(session.Pump.IsPumpedOut);
        Assert.Equal(SessionMode.WhipperFall, session.Mode);
        Assert.Equal(FallOutcome.GroundFall, session.LastFallResult!.Outcome);
    }

    [Fact]
    public void FullGroundToSummitAscent_PlacingCamPitonSkyhookAndHornSling_TopsOutCleanly()
    {
        CliffFace wall = TestCliffFactory.CreateTenMeterGraniteWall();
        var session = new ClimbingSession(wall);

        Assert.True(session.TryStepOntoWallFromGround());

        // Climb splitter to ~5m, placing #0.75 cam and brushing mossy sloper.
        Assert.True(session.TryMoveLimbToFeature("crack-main-splitter", 0.20f));
        Assert.True(session.TryMoveLimbToFeature("stance-low-ledge", 0.5f, LimbSlot.LeftFoot));
        Assert.True(session.TryMoveLimbToFeature("crack-main-splitter", 0.45f));
        Assert.True(session.TryMoveLimbToFeature("crack-main-splitter", 0.38f, LimbSlot.RightFoot));
        Assert.True(session.TryMoveLimbToFeature("crack-main-splitter", 0.72f));
        Assert.True(session.TryMoveLimbToFeature("stance-mid-left", 0.5f, LimbSlot.LeftFoot));

        Assert.True(session.TryLockThreePointStance());
        Assert.True(session.TryPlaceProtection(StandardRack.Cam075, "crack-main-splitter", 0.75f, out _, out _));
        Assert.True(session.TryBrushFeature("edge-mossy-sloper", 2.0f));
        Assert.True(session.ReturnToFreeClimbing());

        // Pull into upper crux (6.2m-8.0m), hammer Knifeblade into pin seam, slot Skyhook on quartz edge, weight to rest!
        Assert.True(session.TryMoveLimbToFeature("edge-mossy-sloper", 0.5f, LimbSlot.RightHand));
        Assert.True(session.TryMoveLimbToFeature("edge-crux-crimp", 0.5f, LimbSlot.LeftHand));
        Assert.True(session.TryMoveLimbToFeature("crack-main-splitter", 0.88f, LimbSlot.LeftFoot));
        Assert.True(session.TryMoveLimbToFeature("edge-seam-sidepull", 0.5f, LimbSlot.LeftHand));
        Assert.True(session.TryMoveLimbToFeature("stance-upper-dime", 0.5f, LimbSlot.RightFoot));
        Assert.True(session.TryMoveLimbToFeature("edge-crisp-hook", 0.5f, LimbSlot.RightHand));

        // Thin pin seam cannot be hand-jammed directly, but accepts a driven Knifeblade piton!
        Assert.False(session.TryMoveLimbToFeature("seam-upper-pin", 0.5f, LimbSlot.LeftHand));

        Assert.True(session.TryLockThreePointStance(LimbSlot.RightHand));
        Assert.True(session.TryPlaceProtection(StandardRack.Knifeblade, "seam-upper-pin", 0.40f, out _, out PlacementEvaluation pinEval));
        Assert.Equal(PlacementQuality.Solid, pinEval.Quality);
        Assert.True(session.TryPlaceProtection(StandardRack.CliffhangerHook, "edge-crisp-hook", 0.5f, out var skyhook, out _));
        Assert.True(session.TryWeightPlacedGear(skyhook!.Value.Protection.PlacementId));
        Assert.Equal(SessionMode.HangingOnGear, session.Mode);

        session.AdvanceTime(8.0f);
        Assert.True(session.ReturnToFreeClimbing());

        // Finish headwall: sling summit chickenhead at 8.9m and top out at 9.9m!
        Assert.True(session.TryMoveLimbToFeature("stance-high-step", 0.5f, LimbSlot.RightFoot));
        Assert.True(session.TryMoveLimbToFeature("edge-high-latch", 0.5f, LimbSlot.LeftHand));
        Assert.True(session.TryMoveLimbToFeature("horn-summit-knob", 0.5f, LimbSlot.RightHand));
        Assert.True(session.TryMoveLimbToFeature("stance-summit-foot", 0.5f, LimbSlot.LeftFoot));

        Assert.True(session.TryLockThreePointStance(LimbSlot.LeftHand));
        Assert.True(session.TryPlaceProtection(StandardRack.Dyneema60, "horn-summit-knob", 0.5f, out _, out PlacementEvaluation slingEval));
        Assert.Equal(PlacementQuality.Bomber, slingEval.Quality);
        Assert.True(session.ReturnToFreeClimbing());

        Assert.True(session.TryMoveLimbToFeature("stance-summit-topout", 0.5f, LimbSlot.LeftHand));
        Assert.Equal(SessionMode.ToppedOut, session.Mode);
        Assert.Equal(4, session.PlacedGear.Count);
    }

    private static ClimbingSession ClimbToMossySloperStance(CliffFace wall)
    {
        var session = new ClimbingSession(wall, initialPump: 35f);
        Assert.True(session.TryStepOntoWallFromGround());
        Assert.True(session.TryMoveLimbToFeature("crack-main-splitter", 0.22f, LimbSlot.LeftHand));
        Assert.True(session.TryMoveLimbToFeature("stance-low-ledge", 0.5f, LimbSlot.LeftFoot));
        Assert.True(session.TryMoveLimbToFeature("crack-main-splitter", 0.52f, LimbSlot.RightHand));
        Assert.True(session.TryMoveLimbToFeature("crack-main-splitter", 0.42f, LimbSlot.RightFoot));
        Assert.True(session.TryMoveLimbToFeature("crack-main-splitter", 0.72f, LimbSlot.LeftHand));
        Assert.True(session.TryMoveLimbToFeature("stance-mid-left", 0.5f, LimbSlot.LeftFoot));
        Assert.True(session.TryLockThreePointStance(LimbSlot.RightHand));
        return session;
    }
}
