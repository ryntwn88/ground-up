using Godot;
using GroundUp.Core.Climbing;

namespace GroundUp.GodotClient;

/// <summary>
/// Renders the ground-anchor lead-solo device, placed 3D protection hardware, and dynamic climbing rope.
/// </summary>
public partial class DynamicRopeRenderer : Node3D
{
    private static readonly Vector3 GroundAnchorWorld = new(-0.35f, 0.16f, 0.72f);

    private readonly List<MeshInstance3D> _ropeSegments = [];
    private readonly List<Node3D> _gearMarkers = [];
    private StandardMaterial3D _ropeMaterial = null!;
    private StandardMaterial3D _carabinerMaterial = null!;

    public override void _Ready()
    {
        _ropeMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.96f, 0.42f, 0.14f),
            Roughness = 0.65f,
            EmissionEnabled = true,
            Emission = new Color(0.96f, 0.36f, 0.10f),
            EmissionEnergyMultiplier = 0.18f
        };

        _carabinerMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.82f, 0.85f, 0.90f),
            Metallic = 0.75f,
            Roughness = 0.30f
        };

        // Ground-anchor belay bollard at base of cliff.
        var anchorBase = new MeshInstance3D
        {
            Name = "GroundSoloAnchor",
            Position = GroundAnchorWorld,
            Mesh = new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.11f, Height = 0.24f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.75f, 0.68f, 0.22f), Metallic = 0.5f, Roughness = 0.4f }
        };
        AddChild(anchorBase);
    }

    public void SyncRopeAndGear(ClimbingSession session, Vector3 harnessWorldPos, bool ropeIsTaut)
    {
        Vector3 outward = session.Cliff.WallOutwardNormal.ToGodot();

        // Sync placed protection 3D hardware markers.
        EnsureGearMarkerCount(session.PlacedGear.Count);
        var orderedByHeight = session.PlacedGear.OrderBy(g => g.Protection.HeightMeters).ToList();

        var ropeWaypoints = new List<Vector3> { GroundAnchorWorld };

        for (int i = 0; i < orderedByHeight.Count; i++)
        {
            PlacedProtectionDetail item = orderedByHeight[i];
            Node3D marker = _gearMarkers[i];
            marker.Visible = true;
            Vector3 rockPos = item.WorldPositionMeters.ToGodot();
            marker.GlobalPosition = rockPos + (outward * 0.05f);

            var headMesh = marker.GetNode<MeshInstance3D>("HardwareHead");
            ((StandardMaterial3D)headMesh.MaterialOverride).AlbedoColor = GodotMathAdapter.GetGearHardwareColor(item.Protection.Gear);

            Vector3 binerPoint = rockPos + (outward * 0.11f) + new Vector3(0f, -0.05f, 0f);
            ropeWaypoints.Add(binerPoint);
        }

        for (int i = orderedByHeight.Count; i < _gearMarkers.Count; i++)
        {
            _gearMarkers[i].Visible = false;
        }

        // Add subtle slack sag midpoint on final span when not hanging taut on gear.
        Vector3 lastAnchor = ropeWaypoints[^1];
        if (!ropeIsTaut && session.Mode is SessionMode.FreeClimbing or SessionMode.StanceInspect or SessionMode.CleaningAndGear)
        {
            Vector3 mid = (lastAnchor + harnessWorldPos) * 0.5f + new Vector3(0.03f, -0.18f, 0.08f);
            mid.Y = Mathf.Max(0.08f, mid.Y);
            ropeWaypoints.Add(mid);
        }

        ropeWaypoints.Add(harnessWorldPos);

        int neededSegments = ropeWaypoints.Count - 1;
        EnsureRopeSegmentCount(neededSegments);

        for (int i = 0; i < neededSegments; i++)
        {
            MeshInstance3D seg = _ropeSegments[i];
            Transform3D xform = GodotMathAdapter.CreateCylinderTransform(ropeWaypoints[i], ropeWaypoints[i + 1], out float len);
            if (len < 0.005f)
            {
                seg.Visible = false;
                continue;
            }

            seg.Visible = true;
            seg.GlobalTransform = xform;
            ((CylinderMesh)seg.Mesh).Height = len;
        }

        for (int i = neededSegments; i < _ropeSegments.Count; i++)
        {
            _ropeSegments[i].Visible = false;
        }
    }

    private void EnsureGearMarkerCount(int count)
    {
        while (_gearMarkers.Count < count)
        {
            var root = new Node3D { Name = $"PlacedGear_{_gearMarkers.Count}" };
            var head = new MeshInstance3D
            {
                Name = "HardwareHead",
                Mesh = new BoxMesh { Size = new Vector3(0.065f, 0.065f, 0.065f) },
                MaterialOverride = new StandardMaterial3D { Roughness = 0.35f, Metallic = 0.55f }
            };
            var biner = new MeshInstance3D
            {
                Name = "CarabinerDraw",
                Position = new Vector3(0f, -0.05f, 0.05f),
                Mesh = new CylinderMesh { TopRadius = 0.012f, BottomRadius = 0.012f, Height = 0.09f },
                MaterialOverride = _carabinerMaterial
            };
            root.AddChild(head);
            root.AddChild(biner);
            AddChild(root);
            _gearMarkers.Add(root);
        }
    }

    private void EnsureRopeSegmentCount(int count)
    {
        while (_ropeSegments.Count < count)
        {
            var seg = new MeshInstance3D
            {
                Name = $"RopeSpan_{_ropeSegments.Count}",
                Mesh = new CylinderMesh { TopRadius = 0.013f, BottomRadius = 0.013f, Height = 1.0f },
                MaterialOverride = _ropeMaterial
            };
            AddChild(seg);
            _ropeSegments.Add(seg);
        }
    }
}
