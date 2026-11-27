using System.Numerics;
using GroundUp.Core.Geology;

namespace GroundUp.Core.Samples;

/// <summary>
/// Builds the canonical 10m x 6m synthetic test cliff used by Phase 0 diagnostics
/// and the Phase 1 playable 3D graybox wall in Godot.
/// </summary>
public static class TestCliffFactory
{
    public static CliffFace CreateTenMeterGraniteWall()
    {
        RockFeatureBase[] features =
        [
            new StanceFeature(
                id: "stance-start-left",
                name: "Left Base Foothold",
                positionMeters: new Vector3(-0.85f, 0.45f, 0f),
                footAreaSqCentimeters: 180f,
                slopeDegrees: 8f),

            new StanceFeature(
                id: "stance-start-right",
                name: "Right Base Foothold",
                positionMeters: new Vector3(-0.15f, 0.55f, 0f),
                footAreaSqCentimeters: 160f,
                slopeDegrees: 10f),

            new EdgeFeature(
                id: "edge-start-jug",
                name: "Starting Granite Jug",
                positionMeters: new Vector3(-0.95f, 1.45f, 0f),
                widthMillimeters: 75f,
                depthMillimeters: 24f,
                incutDegrees: 12f),

            new EdgeFeature(
                id: "edge-low-right",
                name: "Low Right Rail",
                positionMeters: new Vector3(0.35f, 1.75f, 0f),
                widthMillimeters: 55f,
                depthMillimeters: 18f,
                incutDegrees: 8f),

            new StanceFeature(
                id: "stance-low-ledge",
                name: "Lower No-Hands Ledge",
                positionMeters: new Vector3(-1.2f, 2.2f, 0f),
                footAreaSqCentimeters: 480f,
                slopeDegrees: 6f),

            new CrackFeature(
                id: "crack-main-splitter",
                name: "Central #0.75-#1 Parallel Splitter",
                basePositionMeters: new Vector3(-0.6f, 1.5f, 0f),
                profile:
                [
                    new CrackPoint(0.0f, WidthMillimeters: 38.0f, FlareDegrees: 1.5f, DepthMillimeters: 65f),
                    new CrackPoint(2.5f, WidthMillimeters: 31.0f, FlareDegrees: 2.0f, DepthMillimeters: 58f),
                    new CrackPoint(4.5f, WidthMillimeters: 26.5f, FlareDegrees: 4.0f, DepthMillimeters: 50f)
                ]),

            new StanceFeature(
                id: "stance-mid-smear",
                name: "Center Crystal Dish",
                positionMeters: new Vector3(0.35f, 3.1f, 0f),
                footAreaSqCentimeters: 110f,
                slopeDegrees: 14f),

            new EdgeFeature(
                id: "edge-mid-rail",
                name: "Mid-Wall Traverse Rail",
                positionMeters: new Vector3(0.22f, 3.40f, 0f),
                widthMillimeters: 65f,
                depthMillimeters: 16f,
                incutDegrees: 6f),

            new StanceFeature(
                id: "stance-flake-foot",
                name: "Flake Inspection Foothold",
                positionMeters: new Vector3(0.95f, 3.7f, 0f),
                footAreaSqCentimeters: 125f,
                slopeDegrees: 12f),

            new EdgeFeature(
                id: "flake-hollow-choss",
                name: "Hollow Dinnerplate Flake",
                positionMeters: new Vector3(1.3f, 4.4f, 0f),
                widthMillimeters: 140f,
                depthMillimeters: 28f,
                incutDegrees: 14f,
                structuralIntegrity: 0.24f),

            new StanceFeature(
                id: "stance-mid-left",
                name: "Mid Splitter Foot Edge",
                positionMeters: new Vector3(-0.35f, 4.3f, 0f),
                footAreaSqCentimeters: 130f,
                slopeDegrees: 11f),

            new EdgeFeature(
                id: "edge-mossy-sloper",
                name: "Moss-Choked Sloping Lip",
                positionMeters: new Vector3(-0.2f, 5.1f, 0f),
                widthMillimeters: 55f,
                depthMillimeters: 11f,
                incutDegrees: -1f,
                surface: new SurfaceCondition(mossCoverage: 0.80f, dirtCoverage: 0.35f)),

            new EdgeFeature(
                id: "edge-crux-crimp",
                name: "Crux Transition Crimp",
                positionMeters: new Vector3(0.15f, 5.8f, 0f),
                widthMillimeters: 48f,
                depthMillimeters: 12f,
                incutDegrees: 5f),

            new CrackFeature(
                id: "seam-upper-pin",
                name: "Upper Flaring Knifeblade Seam",
                basePositionMeters: new Vector3(0.4f, 6.2f, 0f),
                profile:
                [
                    new CrackPoint(0.0f, WidthMillimeters: 4.8f, FlareDegrees: 9.0f, DepthMillimeters: 52f),
                    new CrackPoint(1.8f, WidthMillimeters: 3.2f, FlareDegrees: 11.0f, DepthMillimeters: 46f)
                ]),

            new StanceFeature(
                id: "stance-upper-dime",
                name: "Upper Dime-Edge Drilling Stance",
                positionMeters: new Vector3(0.8f, 6.3f, 0f),
                footAreaSqCentimeters: 95f,
                slopeDegrees: 16f),

            new EdgeFeature(
                id: "edge-seam-sidepull",
                name: "Seam Sidepull Edge",
                positionMeters: new Vector3(0.10f, 6.9f, 0f),
                widthMillimeters: 45f,
                depthMillimeters: 13f,
                incutDegrees: 7f),

            new EdgeFeature(
                id: "edge-crisp-hook",
                name: "Crisp Quartz Skyhook Edge",
                positionMeters: new Vector3(0.9f, 7.6f, 0f),
                widthMillimeters: 42f,
                depthMillimeters: 9f,
                incutDegrees: 11f),

            new StanceFeature(
                id: "stance-high-step",
                name: "Upper Quartz Foot Chip",
                positionMeters: new Vector3(0.55f, 7.5f, 0f),
                footAreaSqCentimeters: 105f,
                slopeDegrees: 15f),

            new EdgeFeature(
                id: "edge-high-latch",
                name: "Upper Headwall Latch",
                positionMeters: new Vector3(-0.25f, 8.3f, 0f),
                widthMillimeters: 58f,
                depthMillimeters: 17f,
                incutDegrees: 9f),

            new StanceFeature(
                id: "stance-summit-foot",
                name: "Sub-Summit Foot Ledge",
                positionMeters: new Vector3(-0.15f, 8.6f, 0f),
                footAreaSqCentimeters: 150f,
                slopeDegrees: 9f),

            new HornFeature(
                id: "horn-summit-knob",
                name: "Constricted Summit Chickenhead",
                positionMeters: new Vector3(0.2f, 8.9f, 0f),
                neckRadiusMillimeters: 38f,
                collarAngleDegrees: 16f),

            new StanceFeature(
                id: "stance-summit-topout",
                name: "Summit Top-Out Ledge",
                positionMeters: new Vector3(0.0f, 9.9f, 0f),
                footAreaSqCentimeters: 650f,
                slopeDegrees: 3f)
        ];

        return new CliffFace(
            name: "Phase 1 Graybox Granite Test Wall",
            widthMeters: 6.0f,
            heightMeters: 10.0f,
            overhangDegrees: 4.0f,
            geology: GeologyProfile.Granite,
            features: features);
    }
}
