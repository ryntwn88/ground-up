using System.Numerics;
using GroundUp.Core.Bolting;
using GroundUp.Core.Climbing;
using GroundUp.Core.Gear;
using GroundUp.Core.Geology;
using GroundUp.Core.Physics;
using GroundUp.Core.Samples;
using GroundUp.Core.Scoring;
using GroundUp.Core.Stamina;
using Xunit;

namespace GroundUp.Core.Tests;

public sealed class BoltingAndStarRatingTests
{
    [Fact]
    public void BoltInstallation_DustSlowsDrilling_AndBlowTubeRestoresPenetrationAndStrength()
    {
        var dustyHole = new BoltInstallation("bolt-dusty", StandardRack.Expansion38, new Vector3(0.5f, 7f, 0f), new Vector3(0.5f, 7f, 0.4f), 0.96f);
        var cleanHole = new BoltInstallation("bolt-clean", StandardRack.Expansion38, new Vector3(0.5f, 7f, 0f), new Vector3(0.5f, 7f, 0.4f), 0.96f);
        var handDrill = new HandDrillTool();

        // Drill 45 seconds continuously without blowing dust vs. blowing dust every 15 seconds.
        dustyHole.AdvanceDrilling(45f, handDrill, GeologyProfile.Granite);

        for (int i = 0; i < 3; i++)
        {
            cleanHole.AdvanceDrilling(15f, handDrill, GeologyProfile.Granite);
            cleanHole.TryBlowOutDust(out _);
        }

        Assert.True(cleanHole.CurrentDepthMillimeters > dustyHole.CurrentDepthMillimeters + 6.0f);

        // Finish both holes, leaving dustyHole choked with dust while cleanHole is blown clean before bolt insertion.
        dustyHole.AdvanceDrilling(300f, handDrill, GeologyProfile.Granite);
        cleanHole.AdvanceDrilling(300f, handDrill, GeologyProfile.Granite);
        cleanHole.TryBlowOutDust(out _);

        Assert.True(dustyHole.TryInsertBoltAndHanger(out _));
        Assert.True(cleanHole.TryInsertBoltAndHanger(out _));

        dustyHole.TryTightenNut(1.0f, GeologyProfile.Granite, out _);
        cleanHole.TryTightenNut(1.0f, GeologyProfile.Granite, out _);

        Assert.True(cleanHole.InstalledEvaluation!.EffectiveHoldingForceKilonewtons >
                    dustyHole.InstalledEvaluation!.EffectiveHoldingForceKilonewtons + 5.0f);
    }

    [Fact]
    public void BoltInstallation_RequiresNutTighteningBeforeQuickdrawCanBeClipped()
    {
        var bolt = new BoltInstallation("bolt-1", StandardRack.Expansion38, new Vector3(0.4f, 6.8f, 0f), new Vector3(0.4f, 6.8f, 0.4f), 0.96f);
        bolt.AdvanceDrilling(30f, new CordlessPowerDrillTool(), GeologyProfile.Granite);
        bolt.TryBlowOutDust(out _);

        Assert.Equal(BoltInstallStage.HoleDepthReached, bolt.Stage);
        Assert.False(bolt.TryHangQuickdraw(out _)); // Bolt not even inserted yet!

        Assert.True(bolt.TryInsertBoltAndHanger(out _));
        Assert.Equal(BoltInstallStage.BoltInsertedUntorqued, bolt.Stage);

        // Partial wrench torque (50%) must still block quickdraw clipping!
        Assert.True(bolt.TryTightenNut(0.50f, GeologyProfile.Granite, out _));
        Assert.Equal(BoltInstallStage.BoltInsertedUntorqued, bolt.Stage);
        Assert.False(bolt.TryHangQuickdraw(out _));

        // Completing 100% nut torque unlocks quickdraw attachment!
        Assert.True(bolt.TryTightenNut(0.60f, GeologyProfile.Granite, out _));
        Assert.Equal(BoltInstallStage.NutTightened, bolt.Stage);
        Assert.True(bolt.TryHangQuickdraw(out _));
        Assert.Equal(BoltInstallStage.QuickdrawHung, bolt.Stage);
    }

    [Fact]
    public void DrillingIntoUntrundledHollowChoss_ProducesCompromisedDeathBolt()
    {
        CliffFace wall = TestCliffFactory.CreateTenMeterGraniteWall();

        // Hollow dinnerplate flake sits at (1.3, 4.4) with 0.24 integrity.
        BoltInstallation chossBolt = wall.StartOrFindBoltHole(StandardRack.Expansion38, new Vector3(1.25f, 4.45f, 0f));
        BoltInstallation solidBolt = wall.StartOrFindBoltHole(StandardRack.Expansion38, new Vector3(0.45f, 7.10f, 0f));

        Assert.True(chossBolt.SubstrateIntegrity < 0.30f);
        Assert.True(solidBolt.SubstrateIntegrity > 0.90f);

        chossBolt.AdvanceDrilling(30f, new CordlessPowerDrillTool(), wall.Geology);
        chossBolt.TryBlowOutDust(out _);
        chossBolt.TryInsertBoltAndHanger(out _);
        chossBolt.TryTightenNut(1.0f, wall.Geology, out _);

        solidBolt.AdvanceDrilling(30f, new CordlessPowerDrillTool(), wall.Geology);
        solidBolt.TryBlowOutDust(out _);
        solidBolt.TryInsertBoltAndHanger(out _);
        solidBolt.TryTightenNut(1.0f, wall.Geology, out _);

        Assert.Equal(PlacementQuality.SketchyBodyWeightOnly, chossBolt.InstalledEvaluation!.Quality);
        Assert.True(chossBolt.InstalledEvaluation.EffectiveHoldingForceKilonewtons < 4.0f);
        Assert.Equal(PlacementQuality.Bomber, solidBolt.InstalledEvaluation!.Quality);
        Assert.True(solidBolt.InstalledEvaluation.EffectiveHoldingForceKilonewtons > 22.0f);
    }

    [Fact]
    public void PartialBoltHole_PreservesDepthAcrossLowerAndResume()
    {
        CliffFace wall = TestCliffFactory.CreateTenMeterGraniteWall();
        var session = new ClimbingSession(wall);
        session.SetPumpPreset(PumpDifficultyPreset.Off);

        Assert.True(session.TryStepOntoWallFromGround());
        Assert.True(session.TryLockThreePointStance());

        Vector3 targetSpot = new(-0.2f, 1.8f, 0f);
        Assert.True(session.TryPositionDrillAtWallPoint(targetSpot, out BoltInstallation? hole1));
        Assert.True(session.TryOperateDrillHammer(20f));
        float savedDepthMm = hole1!.CurrentDepthMillimeters;
        Assert.InRange(savedDepthMm, 10f, 60f);

        // Lower to ground to rest, then step back up and re-position at the same hole -> depth is preserved!
        session.ResetToGround();
        Assert.True(session.TryStepOntoWallFromGround());
        Assert.True(session.TryLockThreePointStance());
        Assert.True(session.TryPositionDrillAtWallPoint(targetSpot, out BoltInstallation? resumedHole));

        Assert.Same(hole1, resumedHole);
        Assert.Equal(savedDepthMm, resumedHole!.CurrentDepthMillimeters, precision: 2);
    }

    [Fact]
    public void FullDevelopThenFirstFreeAscentWorkflow_ClaimsCleanFfaAndEarnsHighStarRating()
    {
        CliffFace wall = TestCliffFactory.CreateTenMeterGraniteWall();
        var session = new ClimbingSession(wall);
        session.SetPumpPreset(PumpDifficultyPreset.Off);
        session.SetDevDrillTool(new CordlessPowerDrillTool());

        // --- STAGE A: GROUND-UP DEVELOPMENT ---
        Assert.True(session.TryStepOntoWallFromGround());
        Assert.True(session.TryMoveLimbToFeature("crack-main-splitter", 0.22f));
        Assert.True(session.TryMoveLimbToFeature("stance-low-ledge", 0.5f));
        Assert.True(session.TryMoveLimbToFeature("edge-mid-rail", 0.5f));
        Assert.True(session.TryMoveLimbToFeature("stance-mid-smear", 0.5f));
        Assert.True(session.TryMoveLimbToFeature("crack-main-splitter", 0.52f));
        Assert.True(session.TryMoveLimbToFeature("stance-flake-foot", 0.5f));

        // Trundle the hollow death flake & scrub the mossy sloper!
        Assert.True(session.TryLockThreePointStance());
        Assert.True(session.TryTrundleFeature("flake-hollow-choss"));
        Assert.True(session.ReturnToFreeClimbing());

        Assert.True(session.TryMoveLimbToFeature("crack-main-splitter", 0.72f));
        Assert.True(session.TryMoveLimbToFeature("stance-mid-left", 0.5f));
        Assert.True(session.TryLockThreePointStance());
        Assert.True(session.TryBrushFeature("edge-mossy-sloper", 2.0f));
        Assert.True(session.ReturnToFreeClimbing());

        // Climb to upper thin pin-seam headwall (where cams don't fit!) and drill 2 ethical face bolts!
        Assert.True(session.TryMoveLimbToFeature("edge-mossy-sloper", 0.5f));
        Assert.True(session.TryMoveLimbToFeature("edge-crux-crimp", 0.5f));
        Assert.True(session.TryMoveLimbToFeature("crack-main-splitter", 0.88f));
        Assert.True(session.TryMoveLimbToFeature("edge-seam-sidepull", 0.5f));
        Assert.True(session.TryMoveLimbToFeature("stance-upper-dime", 0.5f));
        Assert.True(session.TryMoveLimbToFeature("edge-crisp-hook", 0.5f));

        Assert.True(session.TryLockThreePointStance());
        DrillCleanAndCompleteBolt(session, new Vector3(0.55f, 7.15f, 0f));
        Assert.True(session.ReturnToFreeClimbing());

        Assert.True(session.TryMoveLimbToFeature("stance-high-step", 0.5f));
        Assert.True(session.TryMoveLimbToFeature("edge-high-latch", 0.5f));
        Assert.True(session.TryMoveLimbToFeature("horn-summit-knob", 0.5f));
        Assert.True(session.TryMoveLimbToFeature("stance-summit-foot", 0.5f));

        Assert.True(session.TryLockThreePointStance());
        DrillCleanAndCompleteBolt(session, new Vector3(-0.35f, 9.25f, 0f)); // Top anchor bolt away from horn

        // --- STAGE B: FIRST FREE ASCENT (FFA) REDPOINT ---
        session.StartFirstFreeAscentAttempt();
        Assert.Equal(AscentMode.FirstFreeAscent, session.CurrentAscentMode);
        Assert.True(session.IsCleanFfaAscent);
        Assert.Empty(session.PlacedGear);
        Assert.Equal(2, wall.BoltInstallations.Count(b => b.HasQuickdraw));

        // Aid hooks & drills are blocked in FFA mode!
        Assert.True(session.TryStepOntoWallFromGround());
        Assert.True(session.TryLockThreePointStance());
        Assert.False(session.SelectWorkTool(ActiveToolKind.BoltDrill));
        Assert.False(session.SelectWorkTool(ActiveToolKind.PlaceGear, StandardRack.CliffhangerHook));
        Assert.True(session.ReturnToFreeClimbing());

        // Free climb placing supplemental natural cams low, then clipping the 2 pre-hung quickdraws high!
        Assert.True(session.TryMoveLimbToFeature("crack-main-splitter", 0.22f));
        Assert.True(session.TryMoveLimbToFeature("stance-low-ledge", 0.5f));
        Assert.True(session.TryMoveLimbToFeature("crack-main-splitter", 0.48f));
        Assert.True(session.TryMoveLimbToFeature("crack-main-splitter", 0.35f));

        Assert.True(session.TryLockThreePointStance());
        Assert.True(session.TryPlaceProtection(StandardRack.Cam1, "crack-main-splitter", 0.38f, out _, out _));
        Assert.True(session.ReturnToFreeClimbing());

        Assert.True(session.TryMoveLimbToFeature("crack-main-splitter", 0.72f));
        Assert.True(session.TryMoveLimbToFeature("stance-mid-left", 0.5f));
        Assert.True(session.TryLockThreePointStance());
        Assert.True(session.TryPlaceProtection(StandardRack.Cam075, "crack-main-splitter", 0.78f, out _, out _));
        Assert.True(session.ReturnToFreeClimbing());

        Assert.True(session.TryMoveLimbToFeature("edge-mossy-sloper", 0.5f));
        Assert.True(session.TryMoveLimbToFeature("edge-crux-crimp", 0.5f));
        Assert.True(session.TryMoveLimbToFeature("crack-main-splitter", 0.88f));
        Assert.True(session.TryMoveLimbToFeature("edge-seam-sidepull", 0.5f));
        Assert.True(session.TryMoveLimbToFeature("stance-upper-dime", 0.5f));
        Assert.True(session.TryMoveLimbToFeature("edge-crisp-hook", 0.5f));

        // Clip Bolt 1 quickdraw straight from FreeClimbing!
        Assert.True(session.TryClipBoltQuickdraw("bolt-1"));

        Assert.True(session.TryMoveLimbToFeature("stance-high-step", 0.5f));
        Assert.True(session.TryMoveLimbToFeature("edge-high-latch", 0.5f));
        Assert.True(session.TryMoveLimbToFeature("horn-summit-knob", 0.5f));
        Assert.True(session.TryMoveLimbToFeature("stance-summit-foot", 0.5f));

        // Clip Bolt 2 quickdraw and top out!
        Assert.True(session.TryClipBoltQuickdraw("bolt-2"));
        Assert.True(session.TryMoveLimbToFeature("stance-summit-topout", 0.5f));

        Assert.Equal(SessionMode.ToppedOut, session.Mode);
        Assert.True(session.IsCleanFfaAscent);
        Assert.True(session.HasCompletedCleanFfa);

        // --- STAGE C: STAR RATING EVALUATION ---
        RouteEvaluationReport report = RouteStarRatingEvaluator.EvaluateRoute(
            wall,
            session.ActiveProtectionChain,
            session.ClimbedFeatureIds);

        Assert.True(report.StarRating >= 3.6f, string.Join(" | ", report.FeedbackNotes));
        Assert.True(report.EthicsScore >= 95f);
        Assert.True(report.RockQualityScore >= 95f);
    }

    [Fact]
    public void WeightingGearDuringFfa_InvalidatesCleanRedpointUntilLowered()
    {
        CliffFace wall = TestCliffFactory.CreateTenMeterGraniteWall();
        var session = new ClimbingSession(wall);
        session.StartFirstFreeAscentAttempt();

        Assert.True(session.TryStepOntoWallFromGround());
        Assert.True(session.TryLockThreePointStance());
        Assert.True(session.TryPlaceProtection(StandardRack.Cam1, "crack-main-splitter", 0.05f, out var cam, out _));

        // Hangdogging on the cam invalidates the clean FFA!
        Assert.True(session.TryWeightPlacedGear(cam!.Value.Protection.PlacementId));
        Assert.False(session.IsCleanFfaAscent);

        // Lowering to the ground restores a fresh clean FFA burn.
        session.ResetToGround();
        Assert.True(session.IsCleanFfaAscent);
    }

    [Fact]
    public void StarRatingEngine_PenalizesCrackBoltingChossBoltsAndDirtyRock()
    {
        CliffFace badWall = TestCliffFactory.CreateTenMeterGraniteWall();
        var powerDrill = new CordlessPowerDrillTool();

        // Sin 1: Drill two bolts right alongside the bomber #0.75-#1 central splitter crack!
        BoltInstallation crackBolt1 = badWall.StartOrFindBoltHole(StandardRack.Expansion38, new Vector3(-0.45f, 2.8f, 0f));
        CompleteBoltFast(crackBolt1, powerDrill, badWall.Geology);

        BoltInstallation crackBolt2 = badWall.StartOrFindBoltHole(StandardRack.Expansion38, new Vector3(-0.50f, 3.8f, 0f));
        CompleteBoltFast(crackBolt2, powerDrill, badWall.Geology);

        // Sin 2: Drill a bolt right into the un-trundled hollow dinnerplate choss flake (4.4m) AND leave moss unbrushed!
        BoltInstallation chossBolt = badWall.StartOrFindBoltHole(StandardRack.Expansion38, new Vector3(1.30f, 4.40f, 0f));
        CompleteBoltFast(chossBolt, powerDrill, badWall.Geology);

        PlacedProtection[] chain = badWall.BoltInstallations
            .Select(b => new PlacedProtection(b.Id, b.Spec, b.Id, b.WorldPositionMeters.Y, b.InstalledEvaluation!.EffectiveHoldingForceKilonewtons, b.InstalledEvaluation.Quality))
            .ToArray();

        RouteEvaluationReport badReport = RouteStarRatingEvaluator.EvaluateRoute(badWall, chain);

        Assert.True(badReport.EthicsScore <= 40f);
        Assert.True(badReport.SafetyScore <= 60f);
        Assert.True(badReport.RockQualityScore <= 50f);
        Assert.True(badReport.StarRating <= 2.0f);
    }

    private static void DrillCleanAndCompleteBolt(ClimbingSession session, Vector3 localWallSpot)
    {
        Assert.True(session.TryPositionDrillAtWallPoint(localWallSpot, out BoltInstallation? bolt), session.StatusMessage);
        while (bolt!.Stage == BoltInstallStage.DrillingHole)
        {
            Assert.True(session.TryOperateDrillHammer(8f), session.StatusMessage);
            if (bolt.DustFillRatio > 0.45f && bolt.Stage == BoltInstallStage.DrillingHole)
            {
                Assert.True(session.TryBlowOutActiveHole());
            }
        }

        Assert.True(session.TryBlowOutActiveHole());
        Assert.True(session.TryInsertOrTightenActiveBolt()); // Insert bolt & hanger
        Assert.True(session.TryInsertOrTightenActiveBolt(1.0f)); // Torque hex nut to 100%
        Assert.True(session.TryClipBoltQuickdraw(bolt.Id));
    }

    private static void CompleteBoltFast(BoltInstallation bolt, IDrillTool drill, GeologyProfile geology)
    {
        bolt.AdvanceDrilling(30f, drill, geology);
        bolt.TryBlowOutDust(out _);
        bolt.TryInsertBoltAndHanger(out _);
        bolt.TryTightenNut(1.0f, geology, out _);
        bolt.TryHangQuickdraw(out _);
    }
}
