using Godot;
using GroundUp.Core.Bolting;
using GroundUp.Core.Climbing;
using GroundUp.Core.Gear;
using GroundUp.Core.Geology;
using GroundUp.Core.Physics;

namespace GroundUp.GodotClient;

/// <summary>
/// Synthesizes 3D graybox cliff geometry, raycastable feature & wall-face pick colliders, moss/trundle visual state,
/// progressive bolt-drilling / hanger / nut / quickdraw visuals, and diegetic gear-fit ghosts.
/// </summary>
public partial class CliffGrayboxBuilder : Node3D
{
    public const uint FeatureCollisionLayer = 2;
    public const uint WallSurfaceCollisionLayer = 4;

    private sealed class FeatureVisualEntry
    {
        public required RockFeatureBase Feature { get; init; }
        public required MeshInstance3D PrimaryMesh { get; init; }
        public required StandardMaterial3D PrimaryMaterial { get; init; }
        public required Color BaseCleanColor { get; init; }
        public required CollisionShape3D PickShape { get; init; }
        public MeshInstance3D? MossOverlayMesh { get; init; }
        public StandardMaterial3D? MossMaterial { get; init; }
        public MeshInstance3D? TrundleScarMesh { get; init; }
    }

    private sealed class FallingDebrisItem
    {
        public required MeshInstance3D Mesh { get; init; }
        public Vector3 Velocity { get; set; }
        public Vector3 SpinRadiansPerSec { get; init; }
        public float RemainingSeconds { get; set; }
    }

    private readonly Dictionary<string, FeatureVisualEntry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Node3D> _boltVisualNodes = new(StringComparer.Ordinal);
    private readonly List<FallingDebrisItem> _debris = [];

    private Node3D _placementGhostRoot = null!;
    private MeshInstance3D _ghostLeftLobe = null!;
    private MeshInstance3D _ghostRightLobe = null!;
    private MeshInstance3D _ghostStem = null!;
    private StandardMaterial3D _ghostMaterial = null!;
    private MeshInstance3D _climbCursorPip = null!;
    private StandardMaterial3D _climbCursorPipMaterial = null!;
    private MeshInstance3D _drillAimReticle = null!;
    private StandardMaterial3D _drillAimMaterial = null!;
    private float _autoDrillSpinDegrees;

    public Node3D WallRoot { get; private set; } = null!;

    public void BuildCliff(CliffFace cliff)
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        _entries.Clear();
        _boltVisualNodes.Clear();
        _debris.Clear();

        // Ground terrain + bouldering/belay crash pad at cliff base.
        AddStaticBox(this, "GroundTerrain", new Vector3(16f, 0.5f, 12f), new Vector3(0f, -0.25f, 3.5f), new Color(0.20f, 0.19f, 0.17f));
        AddStaticBox(this, "BelayCrashPad", new Vector3(3.6f, 0.14f, 1.8f), new Vector3(-0.3f, 0.07f, 0.95f), new Color(0.16f, 0.26f, 0.36f));

        WallRoot = new Node3D
        {
            Name = "CliffWallRoot",
            RotationDegrees = new Vector3(cliff.OverhangDegrees, 0f, 0f)
        };
        AddChild(WallRoot);

        AddStaticBox(
            WallRoot,
            "GraniteWallBody",
            new Vector3(cliff.WidthMeters, cliff.HeightMeters, 0.9f),
            new Vector3(0f, cliff.HeightMeters * 0.5f, -0.45f),
            new Color(0.44f, 0.45f, 0.48f));

        // Raycastable wall surface body so the player can position the bolt drill anywhere on open rock face!
        var wallPickBody = new StaticBody3D
        {
            Name = "GraniteWallSurfacePick",
            Position = new Vector3(0f, cliff.HeightMeters * 0.5f, -0.02f),
            CollisionLayer = WallSurfaceCollisionLayer,
            CollisionMask = 0
        };
        wallPickBody.SetMeta("wall_surface", true);
        wallPickBody.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(cliff.WidthMeters, cliff.HeightMeters, 0.06f) }
        });
        WallRoot.AddChild(wallPickBody);

        // Subtle 2m elevation tick markers on left/right cliff edges to help judge runout height.
        for (int h = 2; h <= 10; h += 2)
        {
            AddStaticBox(
                WallRoot,
                $"ElevationTick_{h}m",
                new Vector3(0.18f, 0.03f, 0.04f),
                new Vector3(-cliff.WidthMeters * 0.5f + 0.10f, h, 0.02f),
                new Color(0.68f, 0.70f, 0.74f));
        }

        foreach (RockFeatureBase feature in cliff.Features)
        {
            BuildFeatureNode(feature);
        }

        BuildPlacementGhostNodes();
    }

    public float GetNormalizedPositionFromWorldHit(RockFeatureBase feature, Vector3 worldHit)
    {
        if (feature is not CrackFeature crack)
        {
            return 0.5f;
        }

        Vector3 localHit = WallRoot.ToLocal(worldHit);
        return crack.GetNormalizedPositionForWorldHeight(localHit.Y);
    }

    public void AdvanceDrillBitVisualSpin(float degreesDelta)
    {
        _autoDrillSpinDegrees = (_autoDrillSpinDegrees + degreesDelta) % 360f;
    }

    public void SpawnDustPuff(Vector3 worldPos)
    {
        for (int i = 0; i < 5; i++)
        {
            var puff = new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.035f + (i * 0.008f), Height = 0.07f + (i * 0.016f) },
                MaterialOverride = new StandardMaterial3D
                {
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    AlbedoColor = new Color(0.88f, 0.88f, 0.86f, 0.68f),
                    Roughness = 1.0f
                }
            };
            AddChild(puff);
            puff.GlobalPosition = worldPos + new Vector3((i - 2) * 0.03f, (i % 2) * 0.02f, 0.06f);

            _debris.Add(new FallingDebrisItem
            {
                Mesh = puff,
                Velocity = new Vector3((i - 2) * 0.35f, 0.35f + (i * 0.08f), 1.15f + (i * 0.15f)),
                SpinRadiansPerSec = Vector3.Zero,
                RemainingSeconds = 0.55f
            });
        }
    }

    public void RefreshVisuals(
        CliffFace cliff,
        ClimbingSession session,
        string? hoveredFeatureId,
        float hoveredNormalizedPos,
        bool? hoveredMoveIsValid = null,
        Vector3? hoveredWallLocalSpot = null,
        string? hoveredBoltId = null)
    {
        Color validHoverEmission = new(0.28f, 0.95f, 0.68f);
        Color blockedHoverEmission = new(0.95f, 0.36f, 0.22f);
        Color neutralHoverEmission = new(0.75f, 0.88f, 1.0f);

        foreach ((string id, FeatureVisualEntry entry) in _entries)
        {
            RockFeatureBase feature = entry.Feature;
            bool isTrundled = feature.IsTrundled;

            entry.PrimaryMesh.Visible = !isTrundled;
            entry.PickShape.Disabled = isTrundled;
            if (entry.TrundleScarMesh is not null)
            {
                entry.TrundleScarMesh.Visible = isTrundled;
            }

            if (isTrundled)
            {
                if (entry.MossOverlayMesh is not null)
                {
                    entry.MossOverlayMesh.Visible = false;
                }

                continue;
            }

            // Blend base stone color with olive-green moss tint based on actual SurfaceCondition.
            float mossBlend = Mathf.Clamp((1.0f - feature.Surface.CleanlinessMultiplier) * 1.45f, 0f, 0.88f);
            Color mossTint = new(0.22f, 0.48f, 0.16f);
            Color currentAlbedo = entry.BaseCleanColor.Lerp(mossTint, mossBlend);

            bool isHovered = string.Equals(id, hoveredFeatureId, StringComparison.Ordinal);
            bool isAttached = session.Rig.IsAttachedToFeature(id);

            entry.PrimaryMaterial.AlbedoColor = currentAlbedo;
            if (isHovered)
            {
                entry.PrimaryMaterial.EmissionEnabled = true;
                entry.PrimaryMaterial.Emission = hoveredMoveIsValid switch
                {
                    true => validHoverEmission,
                    false => blockedHoverEmission,
                    null => neutralHoverEmission
                };
                entry.PrimaryMaterial.EmissionEnergyMultiplier = 0.34f;
            }
            else if (isAttached)
            {
                entry.PrimaryMaterial.EmissionEnabled = true;
                entry.PrimaryMaterial.Emission = new Color(0.25f, 0.75f, 0.95f);
                entry.PrimaryMaterial.EmissionEnergyMultiplier = 0.16f;
            }
            else
            {
                entry.PrimaryMaterial.EmissionEnabled = false;
            }

            if (entry.MossOverlayMesh is not null && entry.MossMaterial is not null)
            {
                float coverage = Mathf.Max(feature.Surface.MossCoverage, feature.Surface.DirtCoverage * 0.6f);
                entry.MossOverlayMesh.Visible = coverage > 0.05f;
                entry.MossMaterial.AlbedoColor = new Color(0.20f, 0.50f, 0.15f, Mathf.Clamp(coverage * 0.85f, 0f, 0.85f));
            }
        }

        UpdateClimbCursorPip(cliff, session, hoveredFeatureId, hoveredNormalizedPos, hoveredMoveIsValid);
        UpdatePlacementGhost(cliff, session, hoveredFeatureId, hoveredNormalizedPos);
        UpdateDrillReticleAndBolts(cliff, session, hoveredWallLocalSpot, hoveredBoltId);
    }

    public void TickDebris(float deltaSeconds)
    {
        for (int i = _debris.Count - 1; i >= 0; i--)
        {
            FallingDebrisItem d = _debris[i];
            d.RemainingSeconds -= deltaSeconds;
            d.Velocity += new Vector3(0f, -9.81f * deltaSeconds, 0f);
            d.Mesh.GlobalPosition += d.Velocity * deltaSeconds;
            d.Mesh.Rotation += d.SpinRadiansPerSec * deltaSeconds;

            if (d.Mesh.GlobalPosition.Y <= 0.12f)
            {
                d.Mesh.GlobalPosition = new Vector3(d.Mesh.GlobalPosition.X, 0.12f, d.Mesh.GlobalPosition.Z);
                d.Velocity = new Vector3(d.Velocity.X * 0.4f, Mathf.Abs(d.Velocity.Y) * 0.22f, d.Velocity.Z * 0.4f);
            }

            if (d.RemainingSeconds <= 0f)
            {
                d.Mesh.QueueFree();
                _debris.RemoveAt(i);
            }
        }
    }

    public void SpawnTrundleDebris(Vector3 startWorldPos)
    {
        for (int i = 0; i < 3; i++)
        {
            var chunk = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.26f - (i * 0.05f), 0.18f, 0.09f) },
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.56f, 0.38f, 0.34f),
                    Roughness = 0.92f
                }
            };
            AddChild(chunk);
            chunk.GlobalPosition = startWorldPos + new Vector3((i - 1) * 0.12f, 0f, 0.14f);

            _debris.Add(new FallingDebrisItem
            {
                Mesh = chunk,
                Velocity = new Vector3((i - 1) * 0.45f, 0.6f, 0.9f + (i * 0.2f)),
                SpinRadiansPerSec = new Vector3(3.4f, 2.1f * (i + 1), 4.2f),
                RemainingSeconds = 2.8f
            });
        }
    }

    private void BuildFeatureNode(RockFeatureBase feature)
    {
        Vector3 localPos = new(feature.PositionMeters.X, feature.PositionMeters.Y, feature.PositionMeters.Z + 0.05f);
        Vector3 visualSize;
        Vector3 pickSize;
        Vector3 centerLocalPos = localPos;
        Color cleanColor;

        switch (feature)
        {
            case CrackFeature crack:
                float len = Mathf.Max(0.35f, crack.TotalLengthMeters);
                float centerY = (crack.MinWorldHeightMeters + crack.MaxWorldHeightMeters) * 0.5f;
                float avgWidthMeters = Mathf.Clamp(crack.SampleAtNormalized(0.5f).WidthMillimeters / 1000f, 0.012f, 0.11f);
                bool isThinSeam = crack.SampleAtNormalized(0.5f).WidthMillimeters < 10f;
                cleanColor = isThinSeam
                    ? new Color(0.78f, 0.52f, 0.24f) // Thin incipient piton seam
                    : new Color(0.18f, 0.22f, 0.28f); // Deep recessed splitter recess
                visualSize = new Vector3(avgWidthMeters + 0.025f, len, 0.10f);
                pickSize = new Vector3(0.28f, len + 0.08f, 0.24f);
                centerLocalPos = new Vector3(localPos.X, centerY, localPos.Z - 0.02f);

                // Raised crack lips on left and right sides so crack width & taper are visually unmistakable in close-up!
                float lipOffset = (avgWidthMeters * 0.5f) + 0.028f;
                Color lipColor = isThinSeam ? new Color(0.56f, 0.53f, 0.50f) : new Color(0.54f, 0.56f, 0.60f);
                AddStaticBox(WallRoot, $"{crack.Id}_LeftLip", new Vector3(0.035f, len, 0.09f), new Vector3(localPos.X - lipOffset, centerY, 0.03f), lipColor);
                AddStaticBox(WallRoot, $"{crack.Id}_RightLip", new Vector3(0.035f, len, 0.09f), new Vector3(localPos.X + lipOffset, centerY, 0.03f), lipColor);
                break;

            case EdgeFeature edge:
                float widthM = Mathf.Clamp(edge.WidthMillimeters / 180f + 0.16f, 0.20f, 0.85f);
                float depthM = Mathf.Clamp(edge.DepthMillimeters / 140f + 0.08f, 0.09f, 0.26f);
                cleanColor = edge.IsHollowChoss
                    ? new Color(0.60f, 0.47f, 0.42f) // Subtle suspect iron-stained dinnerplate flake!
                    : new Color(0.76f, 0.74f, 0.66f);
                visualSize = new Vector3(widthM, edge.IsHollowChoss ? 0.36f : 0.11f, depthM);
                pickSize = new Vector3(widthM + 0.14f, (edge.IsHollowChoss ? 0.42f : 0.24f), depthM + 0.16f);
                break;

            case HornFeature:
                cleanColor = new Color(0.72f, 0.66f, 0.78f);
                visualSize = new Vector3(0.24f, 0.22f, 0.28f);
                pickSize = new Vector3(0.38f, 0.36f, 0.36f);
                break;

            case StanceFeature stance:
                float stanceW = Mathf.Clamp(stance.FootAreaSqCentimeters / 420f, 0.28f, 1.35f);
                cleanColor = stance.AllowsNoHandsRest
                    ? new Color(0.46f, 0.68f, 0.56f)
                    : new Color(0.56f, 0.62f, 0.58f);
                visualSize = new Vector3(stanceW, 0.12f, 0.28f);
                pickSize = new Vector3(stanceW + 0.14f, 0.26f, 0.38f);
                break;

            default:
                cleanColor = new Color(0.7f, 0.7f, 0.7f);
                visualSize = new Vector3(0.2f, 0.2f, 0.2f);
                pickSize = new Vector3(0.3f, 0.3f, 0.3f);
                break;
        }

        var mat = new StandardMaterial3D
        {
            AlbedoColor = cleanColor,
            Roughness = 0.86f
        };

        var mesh = new MeshInstance3D
        {
            Name = $"{feature.Id}_Mesh",
            Position = centerLocalPos,
            Mesh = new BoxMesh { Size = visualSize },
            MaterialOverride = mat
        };

        if (feature is EdgeFeature tiltedEdge && !tiltedEdge.IsHollowChoss)
        {
            mesh.RotationDegrees = new Vector3(-tiltedEdge.IncutDegrees * 0.6f, 0f, 0f);
        }

        WallRoot.AddChild(mesh);

        // Raycastable pick body tagged strictly with semantic feature_id (never triangle-inferred physics!).
        var pickBody = new StaticBody3D
        {
            Name = $"{feature.Id}_PickBody",
            Position = centerLocalPos,
            CollisionLayer = FeatureCollisionLayer,
            CollisionMask = 0
        };
        pickBody.SetMeta("feature_id", feature.Id);

        var pickShape = new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = pickSize }
        };
        pickBody.AddChild(pickShape);
        WallRoot.AddChild(pickBody);

        // Optional visible moss cap layer for dirty holds/lips.
        MeshInstance3D? mossMesh = null;
        StandardMaterial3D? mossMat = null;
        if (!feature.Surface.IsClean)
        {
            mossMat = new StandardMaterial3D
            {
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(0.20f, 0.52f, 0.15f, 0.78f),
                Roughness = 0.95f
            };
            mossMesh = new MeshInstance3D
            {
                Name = $"{feature.Id}_MossCap",
                Position = centerLocalPos + new Vector3(0f, visualSize.Y * 0.45f, 0.02f),
                Mesh = new BoxMesh { Size = new Vector3(visualSize.X * 0.96f, 0.045f, visualSize.Z * 0.95f) },
                MaterialOverride = mossMat
            };
            WallRoot.AddChild(mossMesh);
        }

        // Pale scar left behind if hollow choss is pried off or breaks.
        MeshInstance3D? scarMesh = null;
        if (feature.StructuralIntegrity <= 0.55f)
        {
            scarMesh = new MeshInstance3D
            {
                Name = $"{feature.Id}_Scar",
                Position = new Vector3(centerLocalPos.X, centerLocalPos.Y, 0.012f),
                Visible = feature.IsTrundled,
                Mesh = new BoxMesh { Size = new Vector3(visualSize.X * 0.88f, visualSize.Y * 0.85f, 0.02f) },
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.68f, 0.65f, 0.60f),
                    Roughness = 0.95f
                }
            };
            WallRoot.AddChild(scarMesh);
        }

        _entries[feature.Id] = new FeatureVisualEntry
        {
            Feature = feature,
            PrimaryMesh = mesh,
            PrimaryMaterial = mat,
            BaseCleanColor = cleanColor,
            PickShape = pickShape,
            MossOverlayMesh = mossMesh,
            MossMaterial = mossMat,
            TrundleScarMesh = scarMesh
        };
    }

    private void BuildPlacementGhostNodes()
    {
        _ghostMaterial = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(0.3f, 0.95f, 0.45f, 0.72f),
            EmissionEnabled = true,
            Emission = new Color(0.2f, 0.85f, 0.35f),
            EmissionEnergyMultiplier = 0.55f
        };

        _placementGhostRoot = new Node3D { Name = "PlacementFitGhost", Visible = false };
        AddChild(_placementGhostRoot);

        _ghostLeftLobe = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.028f, 0.055f, 0.055f) },
            MaterialOverride = _ghostMaterial
        };
        _ghostRightLobe = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.028f, 0.055f, 0.055f) },
            MaterialOverride = _ghostMaterial
        };
        _ghostStem = new MeshInstance3D
        {
            Position = new Vector3(0f, -0.075f, 0.04f),
            Mesh = new CylinderMesh { TopRadius = 0.006f, BottomRadius = 0.008f, Height = 0.13f },
            MaterialOverride = _ghostMaterial
        };

        _placementGhostRoot.AddChild(_ghostLeftLobe);
        _placementGhostRoot.AddChild(_ghostRightLobe);
        _placementGhostRoot.AddChild(_ghostStem);

        _climbCursorPipMaterial = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(0.28f, 0.95f, 0.68f, 0.82f),
            EmissionEnabled = true,
            Emission = new Color(0.25f, 0.92f, 0.65f),
            EmissionEnergyMultiplier = 0.65f
        };

        _climbCursorPip = new MeshInstance3D
        {
            Name = "ClimbTargetPip",
            Visible = false,
            Mesh = new SphereMesh { Radius = 0.055f, Height = 0.11f },
            MaterialOverride = _climbCursorPipMaterial
        };
        AddChild(_climbCursorPip);

        _drillAimMaterial = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(0.25f, 0.88f, 0.98f, 0.80f),
            EmissionEnabled = true,
            Emission = new Color(0.20f, 0.82f, 0.95f),
            EmissionEnergyMultiplier = 0.65f
        };

        _drillAimReticle = new MeshInstance3D
        {
            Name = "DrillAimReticle",
            Visible = false,
            Mesh = new CylinderMesh { TopRadius = 0.055f, BottomRadius = 0.055f, Height = 0.015f },
            MaterialOverride = _drillAimMaterial
        };
        AddChild(_drillAimReticle);
    }

    private void UpdateDrillReticleAndBolts(
        CliffFace cliff,
        ClimbingSession session,
        Vector3? hoveredWallLocalSpot,
        string? hoveredBoltId)
    {
        // 1. Drill positioning reticle when BoltDrill tool is selected.
        bool showDrillAim = session.SelectedTool == ActiveToolKind.BoltDrill
            && session.CurrentAscentMode == AscentMode.RouteDevelopment
            && hoveredWallLocalSpot.HasValue;

        if (showDrillAim)
        {
            Vector3 localSpot = new(hoveredWallLocalSpot!.Value.X, hoveredWallLocalSpot.Value.Y, 0f);
            Vector3 worldSpot = cliff.WallToWorldPositionMeters(localSpot.ToCore()).ToGodot();
            float dist = worldSpot.DistanceTo(session.Rig.CenterOfMassMeters.ToGodot());
            bool inReach = dist <= ClimbingSession.MaxWorkReachMeters;

            _drillAimReticle.Visible = true;
            _drillAimReticle.GlobalPosition = worldSpot + (cliff.WallOutwardNormal.ToGodot() * 0.025f);
            _drillAimReticle.RotationDegrees = new Vector3(cliff.OverhangDegrees + 90f, 0f, 0f);

            Color aimCol = inReach
                ? new Color(0.22f, 0.92f, 0.98f, 0.82f)
                : new Color(0.95f, 0.30f, 0.22f, 0.82f);
            _drillAimMaterial.AlbedoColor = aimCol;
            _drillAimMaterial.Emission = new Color(aimCol.R, aimCol.G, aimCol.B);
        }
        else
        {
            _drillAimReticle.Visible = false;
        }

        // 2. Sync 3D visual nodes for all BoltInstallations on the cliff.
        HashSet<string> activeIds = [];
        foreach (BoltInstallation bolt in cliff.BoltInstallations)
        {
            activeIds.Add(bolt.Id);
            if (!_boltVisualNodes.TryGetValue(bolt.Id, out Node3D? boltRoot))
            {
                boltRoot = CreateBoltVisualNode(bolt);
                WallRoot.AddChild(boltRoot);
                _boltVisualNodes[bolt.Id] = boltRoot;
            }

            UpdateSingleBoltVisual(boltRoot, bolt, session, string.Equals(bolt.Id, hoveredBoltId, StringComparison.Ordinal));
        }

        List<string> staleIds = _boltVisualNodes.Keys.Where(id => !activeIds.Contains(id)).ToList();
        foreach (string stale in staleIds)
        {
            _boltVisualNodes[stale].QueueFree();
            _boltVisualNodes.Remove(stale);
        }
    }

    private static Node3D CreateBoltVisualNode(BoltInstallation bolt)
    {
        var root = new Node3D
        {
            Name = $"BoltVisual_{bolt.Id}",
            Position = new Vector3(bolt.WallLocalPositionMeters.X, bolt.WallLocalPositionMeters.Y, 0.015f)
        };

        var bore = new MeshInstance3D
        {
            Name = "HoleBore",
            RotationDegrees = new Vector3(90f, 0f, 0f),
            Mesh = new CylinderMesh { TopRadius = 0.014f, BottomRadius = 0.014f, Height = 0.02f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.10f, 0.10f, 0.12f), Roughness = 0.95f }
        };

        var dustRing = new MeshInstance3D
        {
            Name = "DustCollar",
            Position = new Vector3(0f, 0f, 0.008f),
            RotationDegrees = new Vector3(90f, 0f, 0f),
            Mesh = new CylinderMesh { TopRadius = 0.042f, BottomRadius = 0.042f, Height = 0.008f },
            MaterialOverride = new StandardMaterial3D
            {
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(0.88f, 0.86f, 0.82f, 0.0f),
                Roughness = 1.0f
            }
        };

        var drillBit = new Node3D { Name = "SeatedDrillAssembly", Visible = false };
        drillBit.AddChild(new MeshInstance3D
        {
            Position = new Vector3(0f, 0f, 0.09f),
            RotationDegrees = new Vector3(90f, 0f, 0f),
            Mesh = new CylinderMesh { TopRadius = 0.009f, BottomRadius = 0.016f, Height = 0.18f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.86f, 0.68f, 0.16f), Metallic = 0.6f, Roughness = 0.35f }
        });
        drillBit.AddChild(new MeshInstance3D
        {
            Position = new Vector3(0f, 0f, 0.15f),
            Mesh = new BoxMesh { Size = new Vector3(0.065f, 0.022f, 0.022f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.28f, 0.32f), Roughness = 0.6f }
        });

        var hanger = new MeshInstance3D
        {
            Name = "StainlessHanger",
            Position = new Vector3(0f, -0.015f, 0.014f),
            Visible = false,
            Mesh = new BoxMesh { Size = new Vector3(0.052f, 0.072f, 0.012f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.82f, 0.86f, 0.90f), Metallic = 0.80f, Roughness = 0.25f }
        };

        var studAndNut = new MeshInstance3D
        {
            Name = "HexNutStud",
            Position = new Vector3(0f, 0f, 0.045f),
            RotationDegrees = new Vector3(90f, 0f, 0f),
            Visible = false,
            Mesh = new CylinderMesh { TopRadius = 0.013f, BottomRadius = 0.013f, Height = 0.026f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.92f, 0.78f, 0.24f), Metallic = 0.75f, Roughness = 0.30f }
        };

        var quickdraw = new Node3D { Name = "Quickdraw", Visible = false };
        quickdraw.AddChild(new MeshInstance3D
        {
            Name = "Dogbone",
            Position = new Vector3(0f, -0.085f, 0.045f),
            Mesh = new BoxMesh { Size = new Vector3(0.022f, 0.13f, 0.012f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.18f, 0.56f, 0.92f), Roughness = 0.65f }
        });
        quickdraw.AddChild(new MeshInstance3D
        {
            Name = "LowerBiner",
            Position = new Vector3(0f, -0.16f, 0.055f),
            Mesh = new BoxMesh { Size = new Vector3(0.036f, 0.055f, 0.014f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.88f, 0.90f, 0.94f), Metallic = 0.8f, Roughness = 0.25f }
        });

        var pickBody = new StaticBody3D
        {
            Name = "BoltPickBody",
            CollisionLayer = FeatureCollisionLayer | WallSurfaceCollisionLayer,
            CollisionMask = 0
        };
        pickBody.SetMeta("bolt_id", bolt.Id);
        pickBody.AddChild(new CollisionShape3D
        {
            Position = new Vector3(0f, -0.05f, 0.04f),
            Shape = new BoxShape3D { Size = new Vector3(0.22f, 0.28f, 0.18f) }
        });

        root.AddChild(bore);
        root.AddChild(dustRing);
        root.AddChild(drillBit);
        root.AddChild(hanger);
        root.AddChild(studAndNut);
        root.AddChild(quickdraw);
        root.AddChild(pickBody);
        return root;
    }

    private void UpdateSingleBoltVisual(
        Node3D root,
        BoltInstallation bolt,
        ClimbingSession session,
        bool isHovered)
    {
        var dustRing = root.GetNode<MeshInstance3D>("DustCollar");
        var dustMat = (StandardMaterial3D)dustRing.MaterialOverride;
        bool showDust = bolt.Stage is BoltInstallStage.DrillingHole or BoltInstallStage.HoleDepthReached && bolt.DustFillRatio > 0.04f;
        dustRing.Visible = showDust;
        if (showDust)
        {
            dustMat.AlbedoColor = new Color(0.90f, 0.88f, 0.84f, Mathf.Clamp(bolt.DustFillRatio * 0.88f, 0.12f, 0.88f));
        }

        var drillAssembly = root.GetNode<Node3D>("SeatedDrillAssembly");
        bool isActiveTarget = ReferenceEquals(session.ActiveBoltTarget, bolt) && session.Mode == SessionMode.DrillingBolt;
        drillAssembly.Visible = isActiveTarget && bolt.Stage is BoltInstallStage.DrillingHole or BoltInstallStage.HoleDepthReached;
        if (drillAssembly.Visible)
        {
            drillAssembly.RotationDegrees = new Vector3(0f, 0f, _autoDrillSpinDegrees);
        }

        var hanger = root.GetNode<MeshInstance3D>("StainlessHanger");
        var nutStud = root.GetNode<MeshInstance3D>("HexNutStud");
        var quickdraw = root.GetNode<Node3D>("Quickdraw");

        hanger.Visible = bolt.Stage >= BoltInstallStage.BoltInsertedUntorqued;
        nutStud.Visible = bolt.Stage >= BoltInstallStage.BoltInsertedUntorqued;
        quickdraw.Visible = bolt.Stage == BoltInstallStage.QuickdrawHung;

        if (nutStud.Visible)
        {
            // Nut threads visibly inward along the stud as torque increases from 0% to 100%!
            float zOut = Mathf.Lerp(0.052f, 0.022f, bolt.NutTorqueRatio);
            nutStud.Position = new Vector3(0f, 0f, zOut);
            var nutMat = (StandardMaterial3D)nutStud.MaterialOverride;
            nutMat.AlbedoColor = bolt.Stage >= BoltInstallStage.NutTightened
                ? new Color(0.84f, 0.88f, 0.92f)
                : new Color(0.96f, 0.68f, 0.16f);
        }

        var hangerMat = (StandardMaterial3D)hanger.MaterialOverride;
        bool isClipped = session.PlacedGear.Any(p => string.Equals(p.Protection.FeatureId, bolt.Id, StringComparison.Ordinal));
        float dist = bolt.WorldPositionMeters.ToGodot().DistanceTo(session.Rig.CenterOfMassMeters.ToGodot());
        bool canClipHighlight = bolt.HasQuickdraw && !isClipped && dist <= ClimbingSession.MaxWorkReachMeters;

        if (isHovered || canClipHighlight)
        {
            hangerMat.EmissionEnabled = true;
            hangerMat.Emission = canClipHighlight ? new Color(0.25f, 0.95f, 0.55f) : new Color(0.65f, 0.88f, 1.0f);
            hangerMat.EmissionEnergyMultiplier = isHovered ? 0.55f : 0.30f;
        }
        else
        {
            hangerMat.EmissionEnabled = false;
        }
    }

    private void UpdateClimbCursorPip(
        CliffFace cliff,
        ClimbingSession session,
        string? hoveredFeatureId,
        float hoveredNormalizedPos,
        bool? hoveredMoveIsValid)
    {
        bool showPip = hoveredFeatureId is not null
            && session.SelectedTool == ActiveToolKind.None
            && session.Mode is SessionMode.Grounded or SessionMode.FreeClimbing or SessionMode.HangingOnGear;

        if (!showPip)
        {
            _climbCursorPip.Visible = false;
            return;
        }

        RockFeatureBase? feat = cliff.FindFeature(hoveredFeatureId!);
        if (feat is null || feat.IsTrundled)
        {
            _climbCursorPip.Visible = false;
            return;
        }

        Vector3 worldPos = cliff.GetFeatureWorldPositionMeters(feat, hoveredNormalizedPos).ToGodot();
        _climbCursorPip.Visible = true;
        _climbCursorPip.GlobalPosition = worldPos + (cliff.WallOutwardNormal.ToGodot() * 0.07f);

        Color pipColor = hoveredMoveIsValid == false
            ? new Color(0.95f, 0.32f, 0.22f, 0.82f)
            : new Color(0.28f, 0.95f, 0.68f, 0.82f);

        _climbCursorPipMaterial.AlbedoColor = pipColor;
        _climbCursorPipMaterial.Emission = new Color(pipColor.R, pipColor.G, pipColor.B);
    }

    private void UpdatePlacementGhost(
        CliffFace cliff,
        ClimbingSession session,
        string? hoveredFeatureId,
        float hoveredNormalizedPos)
    {
        bool showGhost = session.SelectedTool == ActiveToolKind.PlaceGear
            && session.SelectedGear is not null
            && hoveredFeatureId is not null
            && session.Mode is SessionMode.StanceInspect or SessionMode.CleaningAndGear or SessionMode.HangingOnGear;

        if (!showGhost)
        {
            _placementGhostRoot.Visible = false;
            return;
        }

        RockFeatureBase? feature = cliff.FindFeature(hoveredFeatureId!);
        if (feature is null || feature.IsTrundled)
        {
            _placementGhostRoot.Visible = false;
            return;
        }

        GearSpecBase gear = session.SelectedGear!;
        PlacementEvaluation eval = session.PreviewPlacement(gear, feature.Id, hoveredNormalizedPos);

        Vector3 targetWorld = cliff.GetFeatureWorldPositionMeters(feature, hoveredNormalizedPos).ToGodot();
        _placementGhostRoot.Visible = true;
        _placementGhostRoot.GlobalPosition = targetWorld + (cliff.WallOutwardNormal.ToGodot() * 0.05f);
        _placementGhostRoot.RotationDegrees = new Vector3(cliff.OverhangDegrees, 0f, 0f);

        // Diegetic color + lobe geometry communicates fit without needing numeric kN readouts!
        Color fitColor = eval.Quality switch
        {
            PlacementQuality.Bomber or PlacementQuality.Solid => new Color(0.22f, 0.95f, 0.45f, 0.78f),
            PlacementQuality.Marginal or PlacementQuality.SketchyBodyWeightOnly => new Color(0.96f, 0.76f, 0.18f, 0.78f),
            _ => new Color(0.95f, 0.22f, 0.22f, 0.78f)
        };

        _ghostMaterial.AlbedoColor = fitColor;
        _ghostMaterial.Emission = new Color(fitColor.R, fitColor.G, fitColor.B);

        if (gear is CamSpec cam && feature is CrackFeature crack)
        {
            CrackSample sample = crack.SampleAtNormalized(hoveredNormalizedPos);
            float expansion = Mathf.Clamp((sample.WidthMillimeters - cam.MinWidthMillimeters) / (cam.MaxWidthMillimeters - cam.MinWidthMillimeters), -0.2f, 1.3f);
            float spread = Mathf.Clamp(sample.WidthMillimeters / 2000f, 0.008f, 0.065f);
            float splayDeg = Mathf.Lerp(-8f, 38f, Mathf.Clamp(expansion, 0f, 1.2f));

            _ghostLeftLobe.Visible = true;
            _ghostRightLobe.Visible = true;
            _ghostLeftLobe.Position = new Vector3(-spread, 0f, 0f);
            _ghostRightLobe.Position = new Vector3(spread, 0f, 0f);
            _ghostLeftLobe.RotationDegrees = new Vector3(0f, 0f, splayDeg);
            _ghostRightLobe.RotationDegrees = new Vector3(0f, 0f, -splayDeg);
        }
        else
        {
            _ghostLeftLobe.Visible = true;
            _ghostRightLobe.Visible = false;
            _ghostLeftLobe.Position = Vector3.Zero;
            _ghostLeftLobe.RotationDegrees = Vector3.Zero;
        }
    }

    private static void AddStaticBox(Node3D parent, string name, Vector3 size, Vector3 pos, Color color)
    {
        var mi = new MeshInstance3D
        {
            Name = name,
            Position = pos,
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.88f }
        };
        parent.AddChild(mi);
    }
}
