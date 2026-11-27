using System.Numerics;

namespace GroundUp.Core.Geology;

public enum ResonanceType
{
    SolidRing,
    SuspectDull,
    HollowBoom
}

public readonly record struct RockSoundingResult(
    ResonanceType Resonance,
    float AcousticPitchHz,
    bool AdvisesTrundle);

/// <summary>
/// Surface moss, lichen, and dirt layer on a rock feature.
/// Penalizes climbing friction and gear/hook security until wire-brushed.
/// </summary>
public sealed class SurfaceCondition
{
    public float MossCoverage { get; private set; }
    public float DirtCoverage { get; private set; }

    public SurfaceCondition(float mossCoverage = 0f, float dirtCoverage = 0f)
    {
        MossCoverage = Math.Clamp(mossCoverage, 0f, 1f);
        DirtCoverage = Math.Clamp(dirtCoverage, 0f, 1f);
    }

    /// <summary>Multiplier in [0.35, 1.0] applied to rock friction and cam/hook bite.</summary>
    public float CleanlinessMultiplier
    {
        get
        {
            float penalty = (MossCoverage * 0.45f) + (DirtCoverage * 0.20f);
            return Math.Clamp(1.0f - penalty, 0.35f, 1.0f);
        }
    }

    public bool IsClean => MossCoverage <= 0.05f && DirtCoverage <= 0.05f;

    public void BrushClean(float brushEffort)
    {
        if (brushEffort <= 0f)
        {
            return;
        }

        MossCoverage = Math.Max(0f, MossCoverage - (brushEffort * 0.65f));
        DirtCoverage = Math.Max(0f, DirtCoverage - (brushEffort * 0.85f));
    }
}

/// <summary>
/// Base class for all semantic geological features embedded on a cliff face.
/// Acts as the single source of truth for climbing IK, protection holding math, and visuals.
/// </summary>
public abstract class RockFeatureBase
{
    public string Id { get; }
    public string Name { get; }
    public Vector3 PositionMeters { get; }
    public float StructuralIntegrity { get; }
    public SurfaceCondition Surface { get; }
    public bool IsTrundled { get; private set; }

    protected RockFeatureBase(
        string id,
        string name,
        Vector3 positionMeters,
        float structuralIntegrity = 1.0f,
        SurfaceCondition? surface = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id;
        Name = name;
        PositionMeters = positionMeters;
        StructuralIntegrity = Math.Clamp(structuralIntegrity, 0.05f, 1.0f);
        Surface = surface ?? new SurfaceCondition();
    }

    public bool IsHollowChoss => StructuralIntegrity < 0.45f;

    /// <summary>
    /// Simulates tapping the feature with a hammer or knuckles to assess rock solidity.
    /// </summary>
    public RockSoundingResult SoundRock()
    {
        float pitchHz = 220f + (StructuralIntegrity * 980f);
        ResonanceType resonance = StructuralIntegrity switch
        {
            >= 0.75f => ResonanceType.SolidRing,
            >= 0.45f => ResonanceType.SuspectDull,
            _ => ResonanceType.HollowBoom
        };

        return new RockSoundingResult(resonance, pitchHz, AdvisesTrundle: IsHollowChoss && !IsTrundled);
    }

    /// <summary>
    /// Attempts to pry loose hollow/fractured choss off the cliff using a prybar or hammer claw.
    /// Solid rock (> 0.55 integrity) cannot be pried off.
    /// </summary>
    public bool TryTrundleWithPrybar()
    {
        if (IsTrundled || StructuralIntegrity > 0.55f)
        {
            return false;
        }

        IsTrundled = true;
        return true;
    }

    /// <summary>
    /// Returns the attachment coordinate in cliff-face local meters (before cliff overhang rotation).
    /// Point features ignore <paramref name="normalizedPosition"/>; linear cracks interpolate along their profile.
    /// </summary>
    public virtual Vector3 GetLocalAttachmentPointMeters(float normalizedPosition = 0.5f) => PositionMeters;
}

public readonly record struct CrackPoint(
    float HeightOffsetMeters,
    float WidthMillimeters,
    float FlareDegrees,
    float DepthMillimeters);

public readonly record struct CrackSample(
    float WorldHeightMeters,
    float WidthMillimeters,
    float FlareDegrees,
    float DepthMillimeters);

/// <summary>
/// Continuous crack or incipient seam along the wall with interpolated width, flare, and depth.
/// </summary>
public sealed class CrackFeature : RockFeatureBase
{
    private readonly IReadOnlyList<CrackPoint> _profile;

    public CrackFeature(
        string id,
        string name,
        Vector3 basePositionMeters,
        IReadOnlyList<CrackPoint> profile,
        float structuralIntegrity = 1.0f,
        SurfaceCondition? surface = null)
        : base(id, name, basePositionMeters, structuralIntegrity, surface)
    {
        if (profile is null || profile.Count == 0)
        {
            throw new ArgumentException("Crack profile must contain at least one control point.", nameof(profile));
        }

        _profile = profile.OrderBy(p => p.HeightOffsetMeters).ToArray();
    }

    public IReadOnlyList<CrackPoint> Profile => _profile;

    public float MinWorldHeightMeters => PositionMeters.Y + _profile[0].HeightOffsetMeters;

    public float MaxWorldHeightMeters => PositionMeters.Y + _profile[^1].HeightOffsetMeters;

    public float TotalLengthMeters => _profile[^1].HeightOffsetMeters - _profile[0].HeightOffsetMeters;

    public float GetNormalizedPositionForWorldHeight(float worldHeightMeters)
    {
        float length = TotalLengthMeters;
        if (length <= 0.0001f)
        {
            return 0f;
        }

        return Math.Clamp((worldHeightMeters - MinWorldHeightMeters) / length, 0f, 1f);
    }

    public CrackSample SampleAtWorldHeight(float worldHeightMeters) =>
        SampleAtNormalized(GetNormalizedPositionForWorldHeight(worldHeightMeters));

    public override Vector3 GetLocalAttachmentPointMeters(float normalizedPosition = 0.5f)
    {
        CrackSample sample = SampleAtNormalized(normalizedPosition);
        return new Vector3(PositionMeters.X, sample.WorldHeightMeters, PositionMeters.Z);
    }

    public CrackSample SampleAtNormalized(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        if (_profile.Count == 1)
        {
            CrackPoint single = _profile[0];
            return new CrackSample(
                PositionMeters.Y + single.HeightOffsetMeters,
                single.WidthMillimeters,
                single.FlareDegrees,
                single.DepthMillimeters);
        }

        float minOffset = _profile[0].HeightOffsetMeters;
        float targetOffset = minOffset + (t * TotalLengthMeters);

        for (int i = 0; i < _profile.Count - 1; i++)
        {
            CrackPoint a = _profile[i];
            CrackPoint b = _profile[i + 1];
            if (targetOffset <= b.HeightOffsetMeters || i == _profile.Count - 2)
            {
                float span = Math.Max(0.0001f, b.HeightOffsetMeters - a.HeightOffsetMeters);
                float localT = Math.Clamp((targetOffset - a.HeightOffsetMeters) / span, 0f, 1f);
                return new CrackSample(
                    WorldHeightMeters: PositionMeters.Y + Lerp(a.HeightOffsetMeters, b.HeightOffsetMeters, localT),
                    WidthMillimeters: Lerp(a.WidthMillimeters, b.WidthMillimeters, localT),
                    FlareDegrees: Lerp(a.FlareDegrees, b.FlareDegrees, localT),
                    DepthMillimeters: Lerp(a.DepthMillimeters, b.DepthMillimeters, localT));
            }
        }

        CrackPoint last = _profile[^1];
        return new CrackSample(
            PositionMeters.Y + last.HeightOffsetMeters,
            last.WidthMillimeters,
            last.FlareDegrees,
            last.DepthMillimeters);
    }

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);
}

/// <summary>
/// Crimp, pocket lip, flake edge, or sloper used for free climbing and skyhooking.
/// Positive IncutDegrees = incut edge/flake; 0 = flat horizontal edge; negative = sloping lip.
/// </summary>
public sealed class EdgeFeature : RockFeatureBase
{
    public float WidthMillimeters { get; }
    public float DepthMillimeters { get; }
    public float IncutDegrees { get; }
    public float LipRadiusMillimeters { get; }

    public EdgeFeature(
        string id,
        string name,
        Vector3 positionMeters,
        float widthMillimeters,
        float depthMillimeters,
        float incutDegrees,
        float lipRadiusMillimeters = 3.0f,
        float structuralIntegrity = 1.0f,
        SurfaceCondition? surface = null)
        : base(id, name, positionMeters, structuralIntegrity, surface)
    {
        WidthMillimeters = Math.Max(2f, widthMillimeters);
        DepthMillimeters = Math.Max(2f, depthMillimeters);
        IncutDegrees = Math.Clamp(incutDegrees, -45f, 45f);
        LipRadiusMillimeters = Math.Max(0.5f, lipRadiusMillimeters);
    }
}

/// <summary>
/// Protruding rock horn, knob, or chickenhead that can be slung with a runner.
/// CollarAngleDegrees > 0 means a constricted neck; negative means a tapering cone prone to sling pop-off.
/// </summary>
public sealed class HornFeature : RockFeatureBase
{
    public float NeckRadiusMillimeters { get; }
    public float CollarAngleDegrees { get; }

    public HornFeature(
        string id,
        string name,
        Vector3 positionMeters,
        float neckRadiusMillimeters,
        float collarAngleDegrees,
        float structuralIntegrity = 1.0f,
        SurfaceCondition? surface = null)
        : base(id, name, positionMeters, structuralIntegrity, surface)
    {
        NeckRadiusMillimeters = Math.Max(10f, neckRadiusMillimeters);
        CollarAngleDegrees = Math.Clamp(collarAngleDegrees, -35f, 45f);
    }
}

/// <summary>
/// Foot ledge or rest stance on the wall. Governs pump recovery and free-stance hand-drilling viability.
/// </summary>
public sealed class StanceFeature : RockFeatureBase
{
    public float FootAreaSqCentimeters { get; }
    public float SlopeDegrees { get; }

    public StanceFeature(
        string id,
        string name,
        Vector3 positionMeters,
        float footAreaSqCentimeters,
        float slopeDegrees,
        float structuralIntegrity = 1.0f,
        SurfaceCondition? surface = null)
        : base(id, name, positionMeters, structuralIntegrity, surface)
    {
        FootAreaSqCentimeters = Math.Max(15f, footAreaSqCentimeters);
        SlopeDegrees = Math.Clamp(slopeDegrees, -15f, 60f);
    }

    public bool AllowsNoHandsRest => FootAreaSqCentimeters >= 350f && SlopeDegrees <= 12f && !IsTrundled;
}

/// <summary>
/// Semantic representation of a climbable cliff sector.
/// OverhangDegrees: 0 = dead vertical (90 deg), positive = overhanging, negative = slab.
/// </summary>
public sealed class CliffFace
{
    private readonly List<RockFeatureBase> _features = [];

    public string Name { get; }
    public float WidthMeters { get; }
    public float HeightMeters { get; }
    public float OverhangDegrees { get; }
    public GeologyProfile Geology { get; }

    public CliffFace(
        string name,
        float widthMeters,
        float heightMeters,
        float overhangDegrees,
        GeologyProfile geology,
        IEnumerable<RockFeatureBase>? features = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(geology);

        Name = name;
        WidthMeters = Math.Max(2f, widthMeters);
        HeightMeters = Math.Max(3f, heightMeters);
        OverhangDegrees = Math.Clamp(overhangDegrees, -40f, 65f);
        Geology = geology;

        if (features is not null)
        {
            foreach (RockFeatureBase feature in features)
            {
                AddFeature(feature);
            }
        }
    }

    public IReadOnlyList<RockFeatureBase> Features => _features;

    /// <summary>
    /// Outward surface normal pointing from the rock face toward the climber (+Z when vertical).
    /// </summary>
    public Vector3 WallOutwardNormal
    {
        get
        {
            float radians = OverhangDegrees * (MathF.PI / 180f);
            return new Vector3(0f, -MathF.Sin(radians), MathF.Cos(radians));
        }
    }

    /// <summary>
    /// Transforms a cliff-local coordinate (X = lateral, Y = distance up face, Z = outward depth offset)
    /// into 3D world space accounting for <see cref="OverhangDegrees"/>.
    /// </summary>
    public Vector3 WallToWorldPositionMeters(Vector3 wallLocalPositionMeters)
    {
        float radians = OverhangDegrees * (MathF.PI / 180f);
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);

        return new Vector3(
            wallLocalPositionMeters.X,
            (wallLocalPositionMeters.Y * cos) - (wallLocalPositionMeters.Z * sin),
            (wallLocalPositionMeters.Y * sin) + (wallLocalPositionMeters.Z * cos));
    }

    public Vector3 GetFeatureWorldPositionMeters(RockFeatureBase feature, float normalizedPosition = 0.5f)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return WallToWorldPositionMeters(feature.GetLocalAttachmentPointMeters(normalizedPosition));
    }

    public void AddFeature(RockFeatureBase feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        if (_features.Any(existing => string.Equals(existing.Id, feature.Id, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"A feature with ID '{feature.Id}' already exists on cliff '{Name}'.", nameof(feature));
        }

        _features.Add(feature);
    }

    public RockFeatureBase? FindFeature(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _features.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.Ordinal));
    }

    public T GetFeature<T>(string id) where T : RockFeatureBase
    {
        RockFeatureBase feature = FindFeature(id)
            ?? throw new KeyNotFoundException($"Feature '{id}' was not found on cliff '{Name}'.");

        return feature as T
            ?? throw new InvalidOperationException($"Feature '{id}' is of type '{feature.GetType().Name}', expected '{typeof(T).Name}'.");
    }
}
