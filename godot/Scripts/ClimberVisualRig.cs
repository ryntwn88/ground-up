using Godot;
using GroundUp.Core.Climbing;
using GroundUp.Core.Geology;

namespace GroundUp.GodotClient;

/// <summary>
/// Articulated 3D graybox climber with analytic 2-bone IK arms and legs driven by <see cref="ClimberRigState"/>.
/// </summary>
public partial class ClimberVisualRig : Node3D
{
    private sealed class LimbChainVisual
    {
        public required MeshInstance3D UpperSegment { get; init; }
        public required MeshInstance3D LowerSegment { get; init; }
        public required MeshInstance3D JointSphere { get; init; }
        public required MeshInstance3D EndEffector { get; init; }
        public required float UpperLength { get; init; }
        public required float LowerLength { get; init; }
        public Vector3 SmoothedTargetWorld { get; set; }
        public bool Initialized { get; set; }
    }

    private readonly Dictionary<LimbSlot, LimbChainVisual> _limbs = new();
    private Node3D _pelvisNode = null!;
    private MeshInstance3D _torsoMesh = null!;
    private MeshInstance3D _headMesh = null!;
    private MeshInstance3D _heldToolMesh = null!;
    private StandardMaterial3D _jacketMaterial = null!;
    private StandardMaterial3D _toolMaterial = null!;
    private float _swayClock;

    public Vector3 HarnessWorldPosition { get; private set; } = new(-0.5f, 0.95f, 0.45f);
    public Vector3 FocusPelvisWorldPosition { get; private set; } = new(-0.5f, 0.95f, 0.45f);

    public override void _Ready()
    {
        BuildClimberMeshes();
    }

    public void SyncFromSession(ClimbingSession session, Vector3? hoveredTargetWorld, float deltaSeconds)
    {
        _swayClock += deltaSeconds;

        Vector3 targetPelvis = session.Rig.CenterOfMassMeters.ToGodot();

        // Subtle forearm/body tremor when approaching 100% pump!
        float pumpRatio = session.Pump.CurrentPump / 100f;
        if (pumpRatio > 0.72f && session.Mode is SessionMode.FreeClimbing or SessionMode.StanceInspect or SessionMode.CleaningAndGear)
        {
            float intensity = (pumpRatio - 0.72f) * 0.035f;
            targetPelvis += new Vector3(
                Mathf.Sin(_swayClock * 34f) * intensity,
                Mathf.Cos(_swayClock * 29f) * intensity * 0.5f,
                0f);
        }

        FocusPelvisWorldPosition = FocusPelvisWorldPosition.Lerp(targetPelvis, Mathf.Clamp(deltaSeconds * 11f, 0.05f, 1f));
        ApplyBodyAndLimbIk(session.Cliff, session, FocusPelvisWorldPosition, hoveredTargetWorld, deltaSeconds);
    }

    public void SetOverridePelvisPosition(CliffFace cliff, ClimbingSession session, Vector3 pelvisWorldPos, float deltaSeconds)
    {
        _swayClock += deltaSeconds;
        FocusPelvisWorldPosition = pelvisWorldPos;
        ApplyBodyAndLimbIk(cliff, session, pelvisWorldPos, hoveredTargetWorld: null, deltaSeconds);
    }

    private void ApplyBodyAndLimbIk(
        CliffFace cliff,
        ClimbingSession session,
        Vector3 pelvisWorld,
        Vector3? hoveredTargetWorld,
        float deltaSeconds)
    {
        _pelvisNode.GlobalPosition = pelvisWorld;
        _pelvisNode.RotationDegrees = new Vector3(cliff.OverhangDegrees * 0.65f, 0f, 0f);

        Vector3 outward = cliff.WallOutwardNormal.ToGodot();
        HarnessWorldPosition = pelvisWorld + new Vector3(0f, 0.04f, 0f) + (outward * 0.11f);

        // Tint jacket subtly warmer when heavily pumped.
        float pumpT = Mathf.Clamp((session.Pump.CurrentPump - 55f) / 45f, 0f, 1f);
        _jacketMaterial.AlbedoColor = new Color(0.18f, 0.52f, 0.78f).Lerp(new Color(0.86f, 0.28f, 0.22f), pumpT * 0.65f);

        Vector3 chestBase = pelvisWorld + _pelvisNode.GlobalTransform.Basis * new Vector3(0f, 0.44f, 0f);
        Vector3 hipBase = pelvisWorld;

        UpdateSingleLimb(
            LimbSlot.LeftHand,
            rootWorld: chestBase + new Vector3(-0.20f, 0f, 0f),
            bendHint: new Vector3(-0.55f, -0.45f, 0.65f).Normalized(),
            session,
            pelvisWorld,
            outward,
            hoveredTargetWorld,
            deltaSeconds);

        UpdateSingleLimb(
            LimbSlot.RightHand,
            rootWorld: chestBase + new Vector3(0.20f, 0f, 0f),
            bendHint: new Vector3(0.55f, -0.45f, 0.65f).Normalized(),
            session,
            pelvisWorld,
            outward,
            hoveredTargetWorld,
            deltaSeconds);

        UpdateSingleLimb(
            LimbSlot.LeftFoot,
            rootWorld: hipBase + new Vector3(-0.13f, -0.05f, 0f),
            bendHint: new Vector3(-0.40f, 0.25f, 0.85f).Normalized(),
            session,
            pelvisWorld,
            outward,
            hoveredTargetWorld,
            deltaSeconds);

        UpdateSingleLimb(
            LimbSlot.RightFoot,
            rootWorld: hipBase + new Vector3(0.13f, -0.05f, 0f),
            bendHint: new Vector3(0.40f, 0.25f, 0.85f).Normalized(),
            session,
            pelvisWorld,
            outward,
            hoveredTargetWorld,
            deltaSeconds);

        UpdateHeldToolVisual(session);
    }

    private void UpdateSingleLimb(
        LimbSlot slot,
        Vector3 rootWorld,
        Vector3 bendHint,
        ClimbingSession session,
        Vector3 pelvisWorld,
        Vector3 outward,
        Vector3? hoveredTargetWorld,
        float deltaSeconds)
    {
        LimbChainVisual chain = _limbs[slot];
        Vector3 desiredTarget;

        LimbAttachment? attached = session.Rig.GetAttachment(slot);
        if (attached.HasValue && session.Mode is not SessionMode.Grounded and not SessionMode.HangingOnGear and not SessionMode.WhipperFall)
        {
            desiredTarget = attached.Value.WorldPositionMeters.ToGodot() + (outward * 0.04f);
        }
        else if (session.Mode == SessionMode.Grounded)
        {
            desiredTarget = slot switch
            {
                LimbSlot.LeftFoot => new Vector3(pelvisWorld.X - 0.16f, 0.16f, pelvisWorld.Z - 0.04f),
                LimbSlot.RightFoot => new Vector3(pelvisWorld.X + 0.16f, 0.16f, pelvisWorld.Z - 0.04f),
                LimbSlot.LeftHand => pelvisWorld + new Vector3(-0.28f, 0.10f, -0.05f),
                _ => pelvisWorld + new Vector3(0.28f, 0.10f, -0.05f)
            };
        }
        else if (session.Mode == SessionMode.DrillingBolt && session.ActiveBoltTarget is not null &&
                 (session.FreeWorkHand == slot || (session.FreeWorkHand is null && slot == LimbSlot.RightHand)))
        {
            Vector3 drillTarget = session.ActiveBoltTarget.WorldPositionMeters.ToGodot() + (outward * 0.10f);
            float strikeBob = Mathf.Sin(_swayClock * 22f) * 0.025f;
            desiredTarget = drillTarget + new Vector3(0f, strikeBob, strikeBob * 0.6f);
        }
        else if (session.FreeWorkHand == slot && hoveredTargetWorld.HasValue &&
                 session.Mode is SessionMode.StanceInspect or SessionMode.CleaningAndGear)
        {
            Vector3 toHover = hoveredTargetWorld.Value - rootWorld;
            float maxReach = chain.UpperLength + chain.LowerLength - 0.04f;
            desiredTarget = toHover.Length() > maxReach
                ? rootWorld + (toHover.Normalized() * maxReach)
                : hoveredTargetWorld.Value + (outward * 0.06f);
        }
        else
        {
            // Flagging leg or hanging/falling relaxed limb pose.
            float sway = Mathf.Sin(_swayClock * 2.4f + ((int)slot * 1.3f)) * 0.04f;
            desiredTarget = slot switch
            {
                LimbSlot.LeftHand => pelvisWorld + new Vector3(-0.26f + sway, 0.34f, -0.10f),
                LimbSlot.RightHand => pelvisWorld + new Vector3(0.26f - sway, 0.34f, -0.10f),
                LimbSlot.LeftFoot => pelvisWorld + new Vector3(-0.18f + sway, -0.74f, -0.06f),
                _ => pelvisWorld + new Vector3(0.18f - sway, -0.74f, -0.06f)
            };
        }

        if (!chain.Initialized)
        {
            chain.SmoothedTargetWorld = desiredTarget;
            chain.Initialized = true;
        }
        else
        {
            chain.SmoothedTargetWorld = chain.SmoothedTargetWorld.Lerp(desiredTarget, Mathf.Clamp(deltaSeconds * 14f, 0.08f, 1f));
        }

        SolveAndPlaceTwoBoneIk(chain, rootWorld, chain.SmoothedTargetWorld, bendHint);
    }

    private static void SolveAndPlaceTwoBoneIk(
        LimbChainVisual chain,
        Vector3 root,
        Vector3 target,
        Vector3 bendHint)
    {
        float a = chain.UpperLength;
        float b = chain.LowerLength;
        Vector3 toTarget = target - root;
        float dist = toTarget.Length();
        float maxReach = a + b - 0.005f;
        float minReach = Mathf.Abs(a - b) + 0.02f;

        Vector3 dir = dist > 0.0001f ? toTarget / dist : Vector3.Down;
        float clampedDist = Mathf.Clamp(dist, minReach, maxReach);
        Vector3 effectiveEnd = dist > maxReach ? root + (dir * maxReach) : target;

        // Law of cosines for elbow/knee angle.
        float cosAngle = Mathf.Clamp(((a * a) + (clampedDist * clampedDist) - (b * b)) / (2f * a * clampedDist), -1f, 1f);
        float angle = Mathf.Acos(cosAngle);

        Vector3 bendPerp = (bendHint - (dir * bendHint.Dot(dir)));
        if (bendPerp.LengthSquared() < 0.0001f)
        {
            bendPerp = dir.Cross(Vector3.Right);
            if (bendPerp.LengthSquared() < 0.0001f)
            {
                bendPerp = dir.Cross(Vector3.Forward);
            }
        }

        bendPerp = bendPerp.Normalized();
        Vector3 jointPos = root + ((dir * Mathf.Cos(angle)) + (bendPerp * Mathf.Sin(angle))) * a;

        chain.JointSphere.GlobalPosition = jointPos;
        chain.EndEffector.GlobalPosition = effectiveEnd;

        chain.UpperSegment.GlobalTransform = GodotMathAdapter.CreateCylinderTransform(root, jointPos, out float upperLen);
        ((CylinderMesh)chain.UpperSegment.Mesh).Height = Mathf.Max(0.05f, upperLen);

        chain.LowerSegment.GlobalTransform = GodotMathAdapter.CreateCylinderTransform(jointPos, effectiveEnd, out float lowerLen);
        ((CylinderMesh)chain.LowerSegment.Mesh).Height = Mathf.Max(0.05f, lowerLen);
    }

    private void UpdateHeldToolVisual(ClimbingSession session)
    {
        LimbSlot? activeToolHand = session.FreeWorkHand
            ?? (session.Mode == SessionMode.DrillingBolt ? LimbSlot.RightHand : null);

        if (activeToolHand is not LimbSlot freeHand ||
            session.SelectedTool == ActiveToolKind.None ||
            session.Mode is not SessionMode.CleaningAndGear and not SessionMode.StanceInspect and not SessionMode.DrillingBolt)
        {
            _heldToolMesh.Visible = false;
            return;
        }

        LimbChainVisual handChain = _limbs[freeHand];
        _heldToolMesh.Visible = true;
        _heldToolMesh.GlobalPosition = handChain.EndEffector.GlobalPosition + new Vector3(0f, 0.07f, -0.04f);

        _toolMaterial.AlbedoColor = session.SelectedTool switch
        {
            ActiveToolKind.HammerSound => new Color(0.75f, 0.78f, 0.84f),
            ActiveToolKind.WireBrush => new Color(0.88f, 0.68f, 0.24f),
            ActiveToolKind.Prybar => new Color(0.88f, 0.28f, 0.22f),
            ActiveToolKind.BoltDrill => session.EquippedDrill.IsPowerTool
                ? new Color(0.95f, 0.72f, 0.12f)
                : new Color(0.70f, 0.76f, 0.86f),
            ActiveToolKind.PlaceGear when session.SelectedGear is not null => GodotMathAdapter.GetGearHardwareColor(session.SelectedGear),
            _ => new Color(0.8f, 0.8f, 0.8f)
        };
    }

    private void BuildClimberMeshes()
    {
        _jacketMaterial = new StandardMaterial3D { AlbedoColor = new Color(0.18f, 0.52f, 0.78f), Roughness = 0.75f };
        var pantsMaterial = new StandardMaterial3D { AlbedoColor = new Color(0.22f, 0.24f, 0.28f), Roughness = 0.85f };
        var helmetMaterial = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.76f, 0.14f), Roughness = 0.45f };
        var skinGloveMaterial = new StandardMaterial3D { AlbedoColor = new Color(0.88f, 0.74f, 0.62f), Roughness = 0.80f };
        var shoeMaterial = new StandardMaterial3D { AlbedoColor = new Color(0.14f, 0.82f, 0.58f), Roughness = 0.70f };
        _toolMaterial = new StandardMaterial3D { AlbedoColor = new Color(0.8f, 0.8f, 0.8f), Roughness = 0.4f };

        _pelvisNode = new Node3D { Name = "Pelvis" };
        AddChild(_pelvisNode);

        var hipsMesh = new MeshInstance3D
        {
            Position = Vector3.Zero,
            Mesh = new BoxMesh { Size = new Vector3(0.28f, 0.16f, 0.18f) },
            MaterialOverride = pantsMaterial
        };
        _pelvisNode.AddChild(hipsMesh);

        _torsoMesh = new MeshInstance3D
        {
            Position = new Vector3(0f, 0.26f, 0f),
            Mesh = new BoxMesh { Size = new Vector3(0.34f, 0.40f, 0.20f) },
            MaterialOverride = _jacketMaterial
        };
        _pelvisNode.AddChild(_torsoMesh);

        _headMesh = new MeshInstance3D
        {
            Position = new Vector3(0f, 0.58f, 0.01f),
            Mesh = new SphereMesh { Radius = 0.115f, Height = 0.23f },
            MaterialOverride = helmetMaterial
        };
        _pelvisNode.AddChild(_headMesh);

        _limbs[LimbSlot.LeftHand] = CreateLimbChain("LeftArm", 0.36f, 0.34f, 0.042f, _jacketMaterial, skinGloveMaterial, isHand: true);
        _limbs[LimbSlot.RightHand] = CreateLimbChain("RightArm", 0.36f, 0.34f, 0.042f, _jacketMaterial, skinGloveMaterial, isHand: true);
        _limbs[LimbSlot.LeftFoot] = CreateLimbChain("LeftLeg", 0.46f, 0.44f, 0.052f, pantsMaterial, shoeMaterial, isHand: false);
        _limbs[LimbSlot.RightFoot] = CreateLimbChain("RightLeg", 0.46f, 0.44f, 0.052f, pantsMaterial, shoeMaterial, isHand: false);

        _heldToolMesh = new MeshInstance3D
        {
            Name = "HeldTool",
            Visible = false,
            Mesh = new BoxMesh { Size = new Vector3(0.05f, 0.18f, 0.05f) },
            MaterialOverride = _toolMaterial
        };
        AddChild(_heldToolMesh);
    }

    private LimbChainVisual CreateLimbChain(
        string prefix,
        float upperLen,
        float lowerLen,
        float radius,
        Material limbMat,
        Material endMat,
        bool isHand)
    {
        var upper = new MeshInstance3D
        {
            Name = $"{prefix}_Upper",
            Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius * 0.9f, Height = upperLen },
            MaterialOverride = limbMat
        };
        var lower = new MeshInstance3D
        {
            Name = $"{prefix}_Lower",
            Mesh = new CylinderMesh { TopRadius = radius * 0.88f, BottomRadius = radius * 0.75f, Height = lowerLen },
            MaterialOverride = limbMat
        };
        var joint = new MeshInstance3D
        {
            Name = $"{prefix}_Joint",
            Mesh = new SphereMesh { Radius = radius * 1.05f, Height = radius * 2.1f },
            MaterialOverride = limbMat
        };
        var end = new MeshInstance3D
        {
            Name = $"{prefix}_End",
            Mesh = isHand
                ? new SphereMesh { Radius = 0.048f, Height = 0.096f }
                : new BoxMesh { Size = new Vector3(0.09f, 0.065f, 0.16f) },
            MaterialOverride = endMat
        };

        AddChild(upper);
        AddChild(lower);
        AddChild(joint);
        AddChild(end);

        return new LimbChainVisual
        {
            UpperSegment = upper,
            LowerSegment = lower,
            JointSphere = joint,
            EndEffector = end,
            UpperLength = upperLen,
            LowerLength = lowerLen
        };
    }
}
