using System.Numerics;
using GroundUp.Core.Gear;
using GroundUp.Core.Geology;
using GroundUp.Core.Physics;
using Xunit;

namespace GroundUp.Core.Tests;

public sealed class ProtectionPhysicsTests
{
    [Fact]
    public void EvaluateCam_SweetSpotParallelGraniteSplitter_IsBomber()
    {
        var splitter = new CrackFeature(
            "splitter-075",
            "Parallel Hand Crack",
            new Vector3(0, 5f, 0),
            [new CrackPoint(0f, WidthMillimeters: 31.0f, FlareDegrees: 1.5f, DepthMillimeters: 55f)]);

        PlacementEvaluation eval = ProtectionEvaluator.EvaluateCam(
            StandardRack.Cam075,
            splitter,
            normalizedCrackPosition: 0f,
            GeologyProfile.Granite);

        Assert.True(eval.FitsGeometry);
        Assert.Equal(PlacementQuality.Bomber, eval.Quality);
        Assert.True(eval.EffectiveHoldingForceKilonewtons >= 12.5f);
        Assert.True(eval.HoldsBodyWeight());
    }

    [Fact]
    public void EvaluateCam_OvercammedOrUmbrellaWidth_IsRejectedAsUnusable()
    {
        var narrowSeam = new CrackFeature(
            "seam-narrow",
            "Thin Seam",
            new Vector3(0, 3f, 0),
            [new CrackPoint(0f, WidthMillimeters: 18.0f, FlareDegrees: 0f, DepthMillimeters: 45f)]);

        var wideOffwidth = new CrackFeature(
            "crack-wide",
            "Wide Pod",
            new Vector3(0, 3f, 0),
            [new CrackPoint(0f, WidthMillimeters: 50.0f, FlareDegrees: 0f, DepthMillimeters: 60f)]);

        PlacementEvaluation overcammed = ProtectionEvaluator.EvaluateCam(StandardRack.Cam075, narrowSeam, 0f, GeologyProfile.Granite);
        PlacementEvaluation umbrella = ProtectionEvaluator.EvaluateCam(StandardRack.Cam075, wideOffwidth, 0f, GeologyProfile.Granite);

        Assert.False(overcammed.FitsGeometry);
        Assert.Equal(PlacementQuality.Unusable, overcammed.Quality);
        Assert.False(umbrella.FitsGeometry);
        Assert.Equal(PlacementQuality.Unusable, umbrella.Quality);
    }

    [Fact]
    public void EvaluateCam_TippedOutInFlaringMossySandstone_LosesMostHoldingStrength()
    {
        var sketchyPod = new CrackFeature(
            "pod-flared",
            "Flaring Mossy Pod",
            new Vector3(0, 6f, 0),
            [new CrackPoint(0f, WidthMillimeters: 24.8f, FlareDegrees: 20f, DepthMillimeters: 24f)],
            structuralIntegrity: 0.75f,
            surface: new SurfaceCondition(mossCoverage: 0.70f));

        PlacementEvaluation eval = ProtectionEvaluator.EvaluateCam(
            StandardRack.Cam04,
            sketchyPod,
            0f,
            GeologyProfile.Sandstone);

        Assert.True(eval.FitsGeometry);
        Assert.True(eval.EffectiveHoldingForceKilonewtons < 3.5f);
        Assert.Equal(PlacementQuality.SketchyBodyWeightOnly, eval.Quality);
    }

    [Fact]
    public void EvaluatePiton_DrivenIntoFlaringGraniteSeam_OutperformsRejectedMicroCam()
    {
        var incipientSeam = new CrackFeature(
            "seam-pin",
            "Incipient Granite Seam",
            new Vector3(0, 7f, 0),
            [new CrackPoint(0f, WidthMillimeters: 4.0f, FlareDegrees: 10f, DepthMillimeters: 50f)]);

        PlacementEvaluation camAttempt = ProtectionEvaluator.EvaluateCam(StandardRack.Cam01, incipientSeam, 0f, GeologyProfile.Granite);
        PlacementEvaluation pitonEval = ProtectionEvaluator.EvaluatePiton(StandardRack.Knifeblade, incipientSeam, 0f, GeologyProfile.Granite);

        Assert.False(camAttempt.FitsGeometry);
        Assert.True(pitonEval.FitsGeometry);
        Assert.True(pitonEval.EffectiveHoldingForceKilonewtons >= 7.5f);
        Assert.Equal(PlacementQuality.Solid, pitonEval.Quality);
    }

    [Fact]
    public void EvaluateSkyhook_OnCleanIncutGraniteEdge_HoldsBodyWeightOnly()
    {
        var crispEdge = new EdgeFeature("edge-hook", "Crisp Quartz Edge", new Vector3(0, 8f, 0), 30f, 8f, incutDegrees: 12f);

        PlacementEvaluation eval = ProtectionEvaluator.EvaluateSkyhook(
            StandardRack.CliffhangerHook,
            crispEdge,
            GeologyProfile.Granite);

        Assert.True(eval.FitsGeometry);
        Assert.True(eval.HoldsBodyWeight(climberMassKg: 80f));
        Assert.Equal(PlacementQuality.SketchyBodyWeightOnly, eval.Quality);
        Assert.InRange(eval.EffectiveHoldingForceKilonewtons, 1.8f, 3.5f);
    }

    [Fact]
    public void EvaluateSkyhook_OnMossySlopingEdge_SkatesOffImmediately()
    {
        var mossySloper = new EdgeFeature(
            "edge-sloper",
            "Mossy Sloping Lip",
            new Vector3(0, 5f, 0),
            widthMillimeters: 35f,
            depthMillimeters: 8f,
            incutDegrees: -2f,
            surface: new SurfaceCondition(mossCoverage: 0.80f));

        PlacementEvaluation dirtyEval = ProtectionEvaluator.EvaluateSkyhook(StandardRack.CliffhangerHook, mossySloper, GeologyProfile.Granite);
        Assert.False(dirtyEval.HoldsBodyWeight());

        mossySloper.Surface.BrushClean(2.0f);
        PlacementEvaluation cleanedEval = ProtectionEvaluator.EvaluateSkyhook(StandardRack.CliffhangerHook, mossySloper, GeologyProfile.Granite);
        Assert.True(cleanedEval.HoldsBodyWeight());
    }

    [Fact]
    public void EvaluateSling_ConstrictedChickenheadIsBomber_TaperingConeIsUnusable()
    {
        var constrictedHorn = new HornFeature("horn-good", "Granite Chickenhead", new Vector3(0, 9f, 0), neckRadiusMillimeters: 35f, collarAngleDegrees: 18f);
        var taperingCone = new HornFeature("horn-bad", "Tapering Nubbin", new Vector3(0, 9f, 0), neckRadiusMillimeters: 25f, collarAngleDegrees: -15f);

        PlacementEvaluation goodEval = ProtectionEvaluator.EvaluateSling(StandardRack.Dyneema60, constrictedHorn, GeologyProfile.Granite);
        PlacementEvaluation badEval = ProtectionEvaluator.EvaluateSling(StandardRack.Dyneema60, taperingCone, GeologyProfile.Granite);

        Assert.Equal(PlacementQuality.Bomber, goodEval.Quality);
        Assert.True(goodEval.EffectiveHoldingForceKilonewtons >= 15f);
        Assert.False(badEval.FitsGeometry);
        Assert.Equal(PlacementQuality.Unusable, badEval.Quality);
    }

    [Fact]
    public void EvaluateDrilledBolt_GlueInOutperformsWedgeBoltInFriableSandstone()
    {
        PlacementEvaluation wedgeInSandstone = ProtectionEvaluator.EvaluateDrilledBolt(
            StandardRack.Expansion38, heightMeters: 10f, rockIntegrityAtHole: 0.90f, GeologyProfile.Sandstone);

        PlacementEvaluation glueInSandstone = ProtectionEvaluator.EvaluateDrilledBolt(
            StandardRack.GlueInWave, heightMeters: 10f, rockIntegrityAtHole: 0.90f, GeologyProfile.Sandstone);

        Assert.True(glueInSandstone.EffectiveHoldingForceKilonewtons > wedgeInSandstone.EffectiveHoldingForceKilonewtons + 6.0f);
        Assert.Equal(PlacementQuality.Bomber, glueInSandstone.Quality);
    }

    [Fact]
    public void EvaluateDrilledBolt_PlacedIntoHollowChossBlock_IsSeverelyCompromised()
    {
        PlacementEvaluation chossBolt = ProtectionEvaluator.EvaluateDrilledBolt(
            StandardRack.Expansion38, heightMeters: 8f, rockIntegrityAtHole: 0.25f, GeologyProfile.Granite);

        Assert.Equal(PlacementQuality.SketchyBodyWeightOnly, chossBolt.Quality);
        Assert.True(chossBolt.EffectiveHoldingForceKilonewtons < 4.0f);
    }

    [Fact]
    public void StandardRack_AllCams_ProvideContinuousOverlappingUsableWidthCoverage()
    {
        IReadOnlyList<CamSpec> cams = StandardRack.AllCams;
        for (int i = 0; i < cams.Count - 1; i++)
        {
            CamSpec smaller = cams[i];
            CamSpec larger = cams[i + 1];

            float smallerMaxUsable = smaller.MaxWidthMillimeters - ((smaller.MaxWidthMillimeters - smaller.MinWidthMillimeters) * 0.05f);
            float largerMinUsable = larger.MinWidthMillimeters + ((larger.MaxWidthMillimeters - larger.MinWidthMillimeters) * 0.06f);

            Assert.True(
                smallerMaxUsable > largerMinUsable,
                $"Gap detected between {smaller.Code} (max usable {smallerMaxUsable:F2}mm) and {larger.Code} (min usable {largerMinUsable:F2}mm).");
        }

        var fingerCrack = new CrackFeature(
            "crack-13mm",
            "13.2mm Finger Tip Crack",
            new Vector3(0, 4f, 0),
            [new CrackPoint(0f, WidthMillimeters: 13.2f, FlareDegrees: 1f, DepthMillimeters: 35f)]);

        PlacementEvaluation cam02Eval = ProtectionEvaluator.EvaluateCam(StandardRack.Cam02, fingerCrack, 0f, GeologyProfile.Granite);
        Assert.True(cam02Eval.FitsGeometry);
        Assert.True(cam02Eval.HoldsBodyWeight());
    }

    [Fact]
    public void EvaluateSkyhook_MarginalSandstoneSloper_QualityMatchesHoldsBodyWeight()
    {
        var marginalSandstoneLip = new EdgeFeature(
            "sandstone-sloper",
            "Friable Sloping Sandstone Lip",
            new Vector3(0, 5f, 0),
            widthMillimeters: 30f,
            depthMillimeters: 5f,
            incutDegrees: -6f);

        PlacementEvaluation eval = ProtectionEvaluator.EvaluateSkyhook(
            StandardRack.TalonHook,
            marginalSandstoneLip,
            GeologyProfile.Sandstone);

        Assert.False(eval.HoldsBodyWeight(climberMassKg: 78f));
        Assert.Equal(PlacementQuality.Unusable, eval.Quality);
    }

    [Fact]
    public void EvaluateSling_MossyMarginalHornSlips_UntilWireBrushed()
    {
        var mossyKnob = new HornFeature(
            "knob-mossy",
            "Slime-Coated Shallow Knob",
            new Vector3(0, 6f, 0),
            neckRadiusMillimeters: 28f,
            collarAngleDegrees: -5f,
            surface: new SurfaceCondition(mossCoverage: 0.85f, dirtCoverage: 0.40f));

        PlacementEvaluation dirtyEval = ProtectionEvaluator.EvaluateSling(StandardRack.Dyneema60, mossyKnob, GeologyProfile.Granite);
        Assert.False(dirtyEval.FitsGeometry);
        Assert.Equal(PlacementQuality.Unusable, dirtyEval.Quality);

        mossyKnob.Surface.BrushClean(2.0f);
        PlacementEvaluation cleanEval = ProtectionEvaluator.EvaluateSling(StandardRack.Dyneema60, mossyKnob, GeologyProfile.Granite);
        Assert.True(cleanEval.FitsGeometry);
        Assert.True(cleanEval.HoldsBodyWeight());
    }
}
