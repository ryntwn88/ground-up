using Godot;
using GroundUp.Core.Climbing;

namespace GroundUp.GodotClient;

/// <summary>
/// Smoothly blends between third-person wall framing during climbing/falls and a tactile close-up rig
/// during 3-point stance inspection, rock cleaning, and protection placement.
/// </summary>
public partial class HybridClimbingCamera : Node3D
{
    private float _yawRadians;
    private float _pitchRadians = -0.06f;
    private float _wallDistanceMeters = 5.4f;
    private float _verticalScoutOffsetMeters;
    private Vector3 _smoothedLookTarget = new(0f, 2.0f, 0f);

    public Camera3D Camera { get; private set; } = null!;

    public override void _Ready()
    {
        Camera = new Camera3D
        {
            Name = "HybridClimbingCamera3D",
            Fov = 56f,
            Current = true
        };
        AddChild(Camera);
    }

    public void HandleOrbitDrag(Vector2 relativePixels)
    {
        _yawRadians = Mathf.Clamp(_yawRadians - (relativePixels.X * 0.0045f), -0.95f, 0.95f);
        _pitchRadians = Mathf.Clamp(_pitchRadians - (relativePixels.Y * 0.0038f), -0.60f, 0.55f);
    }

    public void AdjustZoom(float wheelSteps)
    {
        _wallDistanceMeters = Mathf.Clamp(_wallDistanceMeters - (wheelSteps * 0.45f), 2.6f, 10.5f);
    }

    public void AdjustVerticalLookOffset(float deltaMeters)
    {
        _verticalScoutOffsetMeters = Mathf.Clamp(_verticalScoutOffsetMeters + deltaMeters, -2.5f, 3.5f);
    }

    public void ResetScoutOffset()
    {
        _verticalScoutOffsetMeters = 0f;
    }

    public void UpdateCamera(
        ClimbingSession session,
        Vector3 climberPelvisWorld,
        Vector3? inspectTargetWorld,
        float deltaSeconds)
    {
        bool closeUpInspect = session.Mode is SessionMode.StanceInspect or SessionMode.CleaningAndGear or SessionMode.DrillingBolt;

        Vector3 desiredLookTarget;
        float desiredDistance;
        float desiredFov;

        if (closeUpInspect)
        {
            Vector3 featureFocus = inspectTargetWorld ?? (climberPelvisWorld + new Vector3(0f, 0.45f, -0.25f));
            desiredLookTarget = climberPelvisWorld.Lerp(featureFocus, 0.68f) + new Vector3(0f, 0.12f, 0f);
            desiredDistance = 2.05f;
            desiredFov = 44f;
        }
        else
        {
            desiredLookTarget = climberPelvisWorld + new Vector3(0f, 0.50f + _verticalScoutOffsetMeters, -0.15f);
            desiredDistance = session.Mode == SessionMode.Grounded ? Mathf.Max(5.8f, _wallDistanceMeters) : _wallDistanceMeters;
            desiredFov = 56f;
        }

        float blend = Mathf.Clamp(deltaSeconds * 7.5f, 0.04f, 1f);
        _smoothedLookTarget = _smoothedLookTarget.Lerp(desiredLookTarget, blend);
        Camera.Fov = Mathf.Lerp(Camera.Fov, desiredFov, blend);

        float cosPitch = Mathf.Cos(_pitchRadians);
        Vector3 offset = new(
            Mathf.Sin(_yawRadians) * cosPitch * desiredDistance,
            -Mathf.Sin(_pitchRadians) * desiredDistance,
            Mathf.Cos(_yawRadians) * cosPitch * desiredDistance);

        Vector3 desiredCamPos = _smoothedLookTarget + offset;
        desiredCamPos.Y = Mathf.Max(0.45f, desiredCamPos.Y);

        Camera.GlobalPosition = Camera.GlobalPosition == Vector3.Zero
            ? desiredCamPos
            : Camera.GlobalPosition.Lerp(desiredCamPos, blend);

        Camera.LookAt(_smoothedLookTarget, Vector3.Up);
    }
}
