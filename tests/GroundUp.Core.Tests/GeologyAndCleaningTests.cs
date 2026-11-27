using System.Numerics;
using GroundUp.Core.Geology;
using Xunit;

namespace GroundUp.Core.Tests;

public sealed class GeologyAndCleaningTests
{
    [Fact]
    public void SoundRock_OnBulletproofGraniteEdge_ReturnsHighPitchSolidRing()
    {
        var edge = new EdgeFeature("edge-1", "Solid Granite Crimp", new Vector3(0, 4f, 0), 40f, 12f, incutDegrees: 8f, structuralIntegrity: 0.95f);

        RockSoundingResult sound = edge.SoundRock();

        Assert.Equal(ResonanceType.SolidRing, sound.Resonance);
        Assert.False(sound.AdvisesTrundle);
        Assert.True(sound.AcousticPitchHz > 1000f);
    }

    [Fact]
    public void SoundRock_OnSuspectFracturedBlock_ReturnsSuspectDullWithoutMandatoryTrundle()
    {
        var edge = new EdgeFeature("edge-2", "Fractured Flake", new Vector3(0, 5f, 0), 60f, 18f, incutDegrees: 10f, structuralIntegrity: 0.55f);

        RockSoundingResult sound = edge.SoundRock();

        Assert.Equal(ResonanceType.SuspectDull, sound.Resonance);
        Assert.False(sound.AdvisesTrundle);
    }

    [Fact]
    public void SoundRock_OnHollowDeathFlake_ReturnsHollowBoomAndAdvisesTrundle()
    {
        var chossFlake = new EdgeFeature("flake-death", "Hollow Dinnerplate Flake", new Vector3(1f, 6f, 0), 120f, 25f, incutDegrees: 15f, structuralIntegrity: 0.22f);

        RockSoundingResult sound = chossFlake.SoundRock();

        Assert.Equal(ResonanceType.HollowBoom, sound.Resonance);
        Assert.True(sound.AdvisesTrundle);
        Assert.True(chossFlake.IsHollowChoss);
    }

    [Fact]
    public void TryTrundleWithPrybar_RemovesHollowChoss_ButLeavesSolidStoneIntact()
    {
        var chossFlake = new EdgeFeature("flake-loose", "Loose Block", new Vector3(0, 3f, 0), 80f, 20f, 12f, structuralIntegrity: 0.30f);
        var solidEdge = new EdgeFeature("edge-solid", "Solid Edge", new Vector3(0, 4f, 0), 50f, 14f, 5f, structuralIntegrity: 0.92f);

        Assert.True(chossFlake.TryTrundleWithPrybar());
        Assert.True(chossFlake.IsTrundled);
        Assert.False(chossFlake.TryTrundleWithPrybar()); // Cannot trundle twice

        Assert.False(solidEdge.TryTrundleWithPrybar());
        Assert.False(solidEdge.IsTrundled);
    }

    [Fact]
    public void SurfaceCondition_HeavyMossPenalizesCleanliness_AndWireBrushingRestoresIt()
    {
        var surface = new SurfaceCondition(mossCoverage: 0.85f, dirtCoverage: 0.50f);
        float dirtyMultiplier = surface.CleanlinessMultiplier;

        Assert.False(surface.IsClean);
        Assert.True(dirtyMultiplier < 0.60f);

        surface.BrushClean(brushEffort: 1.5f);

        Assert.True(surface.IsClean);
        Assert.Equal(1.0f, surface.CleanlinessMultiplier, precision: 2);
    }

    [Fact]
    public void CrackFeature_SampleAtNormalized_InterpolatesWidthFlareAndHeight()
    {
        var crack = new CrackFeature(
            "crack-main",
            "Tapering Splitter",
            new Vector3(0, 2.0f, 0),
            [
                new CrackPoint(HeightOffsetMeters: 0.0f, WidthMillimeters: 20.0f, FlareDegrees: 2.0f, DepthMillimeters: 50.0f),
                new CrackPoint(HeightOffsetMeters: 4.0f, WidthMillimeters: 40.0f, FlareDegrees: 14.0f, DepthMillimeters: 30.0f)
            ]);

        CrackSample midpoint = crack.SampleAtNormalized(0.5f);

        Assert.Equal(4.0f, midpoint.WorldHeightMeters, precision: 2);
        Assert.Equal(30.0f, midpoint.WidthMillimeters, precision: 2);
        Assert.Equal(8.0f, midpoint.FlareDegrees, precision: 2);
        Assert.Equal(40.0f, midpoint.DepthMillimeters, precision: 2);
    }

    [Fact]
    public void GeologyProfile_PreservesExpectedHardnessAndFriabilityOrdering()
    {
        Assert.True(GeologyProfile.Granite.DrillHardness > GeologyProfile.Limestone.DrillHardness);
        Assert.True(GeologyProfile.Limestone.DrillHardness > GeologyProfile.Sandstone.DrillHardness);
        Assert.True(GeologyProfile.Sandstone.Friability > GeologyProfile.Granite.Friability);
    }
}
