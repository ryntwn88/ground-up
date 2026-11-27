using System.Text;
using Godot;
using GroundUp.Core.Gear;
using GroundUp.Core.Geology;
using GroundUp.Core.Physics;
using GroundUp.Core.Samples;
using GroundUp.Core.Stamina;

namespace GroundUp.GodotClient;

/// <summary>
/// Phase 0/1 bridge scene: renders the semantic 10m x 6m granite test cliff as color-coded 3D graybox geometry
/// and verifies live GroundUp.Core physics evaluations on startup.
/// </summary>
public partial class GrayboxSandboxController : Node3D
{
    private CliffFace _cliff = null!;
    private Camera3D _camera = null!;
    private float _orbitAngleRadians;

    public override void _Ready()
    {
        _cliff = TestCliffFactory.CreateTenMeterGraniteWall();

        SetupEnvironmentAndLighting();
        BuildGrayboxCliffGeometry();
        SetupCamera();
        BuildDiagnosticsOverlay();
    }

    public override void _Process(double delta)
    {
        // Gentle camera sway around the 10m test wall; Phase 1 replaces this with the hybrid climber/close-up camera.
        _orbitAngleRadians += (float)delta * 0.18f;
        float swayX = Mathf.Sin(_orbitAngleRadians) * 2.2f;
        _camera.Position = new Vector3(swayX, 5.2f, 9.5f);
        _camera.LookAt(new Vector3(0f, 5.0f, 0f), Vector3.Up);
    }

    private void SetupEnvironmentAndLighting()
    {
        var sun = new DirectionalLight3D
        {
            Name = "SunLight",
            RotationDegrees = new Vector3(-42f, 32f, 0f),
            ShadowEnabled = true,
            LightEnergy = 1.25f
        };
        AddChild(sun);

        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.11f, 0.14f, 0.18f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.38f, 0.42f, 0.48f),
            AmbientLightEnergy = 0.65f
        };
        AddChild(new WorldEnvironment { Environment = env });
    }

    private void BuildGrayboxCliffGeometry()
    {
        // Ground pad
        AddBoxMesh("GroundBase", new Vector3(12f, 0.4f, 8f), new Vector3(0f, -0.2f, 2.5f), new Color(0.24f, 0.22f, 0.19f));

        // Main 10m x 6m granite slab/overhang wall
        var wallRoot = new Node3D
        {
            Name = "CliffWallRoot",
            RotationDegrees = new Vector3(_cliff.OverhangDegrees, 0f, 0f)
        };
        AddChild(wallRoot);

        AddBoxMeshTo(
            wallRoot,
            "GraniteWallBody",
            new Vector3(_cliff.WidthMeters, _cliff.HeightMeters, 0.8f),
            new Vector3(0f, _cliff.HeightMeters * 0.5f, -0.4f),
            new Color(0.46f, 0.47f, 0.50f));

        // Semantic feature debug markers
        foreach (RockFeatureBase feature in _cliff.Features)
        {
            Vector3 pos = new(feature.PositionMeters.X, feature.PositionMeters.Y, feature.PositionMeters.Z + 0.06f);
            switch (feature)
            {
                case CrackFeature crack:
                    float length = Mathf.Max(0.4f, crack.TotalLengthMeters);
                    Color crackColor = crack.SampleAtNormalized(0f).WidthMillimeters < 10f
                        ? new Color(0.95f, 0.62f, 0.18f) // Orange = thin piton seam
                        : new Color(0.18f, 0.72f, 0.95f); // Cyan = splitter crack
                    AddBoxMeshTo(wallRoot, crack.Id, new Vector3(0.08f, length, 0.12f), pos + new Vector3(0f, length * 0.5f, 0f), crackColor);
                    break;

                case EdgeFeature edge:
                    Color edgeColor = edge.IsHollowChoss
                        ? new Color(0.90f, 0.22f, 0.22f) // Red = hollow choss block!
                        : (!edge.Surface.IsClean ? new Color(0.28f, 0.68f, 0.26f) : new Color(0.92f, 0.88f, 0.30f));
                    AddBoxMeshTo(wallRoot, edge.Id, new Vector3(edge.WidthMillimeters / 220f + 0.18f, 0.12f, 0.14f), pos, edgeColor);
                    break;

                case HornFeature horn:
                    AddBoxMeshTo(wallRoot, horn.Id, new Vector3(0.24f, 0.24f, 0.28f), pos, new Color(0.75f, 0.45f, 0.95f));
                    break;

                case StanceFeature stance:
                    float stanceWidth = Mathf.Clamp(stance.FootAreaSqCentimeters / 450f, 0.3f, 1.1f);
                    AddBoxMeshTo(wallRoot, stance.Id, new Vector3(stanceWidth, 0.14f, 0.32f), pos, new Color(0.32f, 0.85f, 0.55f));
                    break;
            }
        }
    }

    private void SetupCamera()
    {
        _camera = new Camera3D
        {
            Name = "PreviewCamera",
            Fov = 55f
        };
        AddChild(_camera);
    }

    private void BuildDiagnosticsOverlay()
    {
        var crack = (CrackFeature)_cliff.Features[1];
        var hookEdge = (EdgeFeature)_cliff.Features[3];
        var chossFlake = (EdgeFeature)_cliff.Features[5];

        PlacementEvaluation camEval = ProtectionEvaluator.EvaluateCam(StandardRack.Cam075, crack, 0.5f, _cliff.Geology);
        PlacementEvaluation hookEval = ProtectionEvaluator.EvaluateSkyhook(StandardRack.CliffhangerHook, hookEdge, _cliff.Geology);
        RockSoundingResult chossSound = chossFlake.SoundRock();

        FallSimulationResult testWhipper = RopeAndFallSimulator.SimulateLeadFall(
            climberHeightMeters: 8.2f,
            activeProtectionChain:
            [
                new PlacedProtection("cam-075", StandardRack.Cam075, crack.Id, camEval.PlacementHeightMeters, camEval.EffectiveHoldingForceKilonewtons, camEval.Quality),
                new PlacedProtection("skyhook-top", StandardRack.CliffhangerHook, hookEdge.Id, hookEval.PlacementHeightMeters, hookEval.EffectiveHoldingForceKilonewtons, hookEval.Quality)
            ]);

        float graniteDrillSec = ForearmPumpModel.EstimateBoltDrillTimeSeconds(89f, _cliff.Geology, new HandDrillTool());

        var sb = new StringBuilder();
        sb.AppendLine($"GROUND UP! — Phase 0/1 Graybox Simulation Bridge");
        sb.AppendLine($"Cliff: {_cliff.Name} ({_cliff.WidthMeters:F0}m x {_cliff.HeightMeters:F0}m, {_cliff.OverhangDegrees:F0}° overhang, {_cliff.Geology.DisplayName})");
        sb.AppendLine();
        sb.AppendLine($"[Live GroundUp.Core Telemetry]");
        sb.AppendLine($"• {StandardRack.Cam075.DisplayName} @ {crack.Name}: {camEval.Quality} ({camEval.EffectiveHoldingForceKilonewtons:F1} kN)");
        sb.AppendLine($"• {StandardRack.CliffhangerHook.DisplayName} @ {hookEdge.Name}: {hookEval.Quality} ({hookEval.EffectiveHoldingForceKilonewtons:F1} kN, holds body weight: {hookEval.HoldsBodyWeight()})");
        sb.AppendLine($"• Sounding '{chossFlake.Name}': {chossSound.Resonance} ({chossSound.AcousticPitchHz:F0} Hz, advise trundle: {chossSound.AdvisesTrundle})");
        sb.AppendLine($"• 8.2m Whipper above Skyhook + #0.75 Cam: {testWhipper.Outcome} (blown: {testWhipper.BlownPieces.Count}, caught by: {testWhipper.CatchingPlacementId} at {testWhipper.FinalRestingHeightMeters:F1}m)");
        sb.AppendLine($"• Hand-Drill 3/8\" x 3.5\" Wedge Bolt Time in Granite: ~{graniteDrillSec:F0}s");

        var canvas = new CanvasLayer { Name = "DiagnosticsHUD" };
        var panel = new PanelContainer
        {
            Position = new Vector2(24f, 24f)
        };
        var label = new Label
        {
            Text = sb.ToString()
        };
        panel.AddChild(label);
        canvas.AddChild(panel);
        AddChild(canvas);
    }

    private void AddBoxMesh(string name, Vector3 size, Vector3 position, Color color) =>
        AddBoxMeshTo(this, name, size, position, color);

    private static void AddBoxMeshTo(Node3D parent, string name, Vector3 size, Vector3 position, Color color)
    {
        var meshInstance = new MeshInstance3D
        {
            Name = name,
            Position = position,
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.85f }
        };
        parent.AddChild(meshInstance);
    }
}
