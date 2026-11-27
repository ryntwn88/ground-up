using System.Numerics;
using GroundUp.Core.Geology;

namespace GroundUp.Core.Climbing;

public enum LimbSlot
{
    LeftHand,
    RightHand,
    LeftFoot,
    RightFoot
}

public readonly record struct LimbAttachment(
    LimbSlot Limb,
    string FeatureId,
    float NormalizedFeaturePosition,
    Vector3 WorldPositionMeters);

/// <summary>
/// Pure-C# 4-limb kinematic state, reach-envelope validator, smart limb selector, and center-of-mass balance calculator.
/// </summary>
public sealed class ClimberRigState
{
    public const float MaxArmReachFromPelvisMeters = 1.58f;
    public const float MaxLegReachFromPelvisMeters = 1.35f;
    public const float MaxTotalBodySpanMeters = 2.35f;
    public const float PelvisWallStandOffMeters = 0.32f;

    private readonly Dictionary<LimbSlot, LimbAttachment> _attachments = new();
    private LimbSlot? _lastMovedHand;
    private LimbSlot? _lastMovedFoot;

    public IReadOnlyDictionary<LimbSlot, LimbAttachment> Attachments => _attachments;

    public Vector3 CenterOfMassMeters { get; private set; } = new(0f, 0.95f, PelvisWallStandOffMeters);

    public int AttachedLimbCount => _attachments.Count;

    public int AttachedHandCount =>
        (_attachments.ContainsKey(LimbSlot.LeftHand) ? 1 : 0) +
        (_attachments.ContainsKey(LimbSlot.RightHand) ? 1 : 0);

    public int AttachedFootCount =>
        (_attachments.ContainsKey(LimbSlot.LeftFoot) ? 1 : 0) +
        (_attachments.ContainsKey(LimbSlot.RightFoot) ? 1 : 0);

    public static bool IsHand(LimbSlot limb) => limb is LimbSlot.LeftHand or LimbSlot.RightHand;

    public static bool IsLeft(LimbSlot limb) => limb is LimbSlot.LeftHand or LimbSlot.LeftFoot;

    public LimbAttachment? GetAttachment(LimbSlot limb) =>
        _attachments.TryGetValue(limb, out LimbAttachment attachment) ? attachment : null;

    public bool IsAttachedToFeature(string featureId) =>
        _attachments.Values.Any(a => string.Equals(a.FeatureId, featureId, StringComparison.Ordinal));

    public void SetGroundedAtBase(float lateralX = -0.5f)
    {
        _attachments.Clear();
        _lastMovedHand = null;
        _lastMovedFoot = null;
        CenterOfMassMeters = new Vector3(lateralX, 0.95f, PelvisWallStandOffMeters + 0.15f);
    }

    public void SetHangingAtPosition(CliffFace cliff, Vector3 anchorWorldMeters)
    {
        ArgumentNullException.ThrowIfNull(cliff);
        _attachments.Clear();
        CenterOfMassMeters = anchorWorldMeters
            + new Vector3(0f, -0.35f, 0f)
            + (cliff.WallOutwardNormal * (PelvisWallStandOffMeters + 0.08f));
    }

    public void DetachLimb(CliffFace cliff, LimbSlot limb)
    {
        ArgumentNullException.ThrowIfNull(cliff);
        if (_attachments.Remove(limb) && _attachments.Count > 0)
        {
            RecomputeCenterOfMass(cliff);
        }
    }

    public void SetLimbAttachment(CliffFace cliff, LimbAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(cliff);
        _attachments[attachment.Limb] = attachment;
        if (IsHand(attachment.Limb))
        {
            _lastMovedHand = attachment.Limb;
        }
        else
        {
            _lastMovedFoot = attachment.Limb;
        }

        PruneOverstretchedTrailingFoot(cliff, attachment.Limb);
        RecomputeCenterOfMass(cliff);
    }

    /// <summary>
    /// Chooses the most natural hand or foot for a clicked 3D feature target unless overridden manually.
    /// </summary>
    public LimbSlot SelectBestLimbForTarget(
        RockFeatureBase feature,
        Vector3 targetWorldMeters,
        LimbSlot? manualOverride = null)
    {
        ArgumentNullException.ThrowIfNull(feature);
        if (manualOverride.HasValue)
        {
            return manualOverride.Value;
        }

        bool chooseFoot;
        if (feature is StanceFeature && targetWorldMeters.Y <= CenterOfMassMeters.Y + 0.45f)
        {
            chooseFoot = true;
        }
        else if (feature is HornFeature)
        {
            chooseFoot = false;
        }
        else
        {
            chooseFoot = targetWorldMeters.Y < CenterOfMassMeters.Y + 0.05f;
        }

        if (chooseFoot)
        {
            return ChooseSide(LimbSlot.LeftFoot, LimbSlot.RightFoot, _lastMovedFoot, targetWorldMeters);
        }

        return ChooseSide(LimbSlot.LeftHand, LimbSlot.RightHand, _lastMovedHand, targetWorldMeters);
    }

    /// <summary>
    /// Checks whether <paramref name="feature"/> can physically support a hand or foot and lies within human reach.
    /// </summary>
    public bool CanReachAndUseFeature(
        CliffFace cliff,
        LimbSlot limb,
        RockFeatureBase feature,
        float normalizedPosition,
        out string failureReason)
    {
        ArgumentNullException.ThrowIfNull(cliff);
        ArgumentNullException.ThrowIfNull(feature);

        if (feature.IsTrundled)
        {
            failureReason = $"'{feature.Name}' has already been trundled off the cliff.";
            return false;
        }

        bool isHand = IsHand(limb);
        if (feature is CrackFeature crack)
        {
            CrackSample sample = crack.SampleAtNormalized(normalizedPosition);
            float minJamWidthMm = isHand ? 11.0f : 6.0f;
            if (sample.WidthMillimeters < minJamWidthMm)
            {
                failureReason = $"'{crack.Name}' ({sample.WidthMillimeters:F1}mm) is an incipient seam—too thin to {(isHand ? "hand-jam" : "foot-jam")}.";
                return false;
            }
        }

        Vector3 targetWorld = cliff.GetFeatureWorldPositionMeters(feature, normalizedPosition);

        // When stepping off the ground or pulling back onto rock from gear, check proximity to current pelvis.
        float maxStepReach = isHand ? 1.78f : 1.55f;
        float distFromCurrentPelvis = Vector3.Distance(CenterOfMassMeters, targetWorld);
        if (distFromCurrentPelvis > maxStepReach)
        {
            failureReason = $"'{feature.Name}' is out of reach ({distFromCurrentPelvis:F2}m > {maxStepReach:F2}m).";
            return false;
        }

        if (_attachments.Count == 0)
        {
            failureReason = string.Empty;
            return true;
        }

        // Simulate resulting limb constellation (allowing a low trailing foot to naturally lift/flag on high-steps).
        var simulated = new Dictionary<LimbSlot, LimbAttachment>(_attachments)
        {
            [limb] = new LimbAttachment(limb, feature.Id, normalizedPosition, targetWorld)
        };

        PruneTrailingFootInSimulation(simulated, limb);

        float maxHandY = float.MinValue;
        float minFootY = float.MaxValue;
        float maxFootY = float.MinValue;

        foreach (LimbAttachment item in simulated.Values)
        {
            if (IsHand(item.Limb))
            {
                maxHandY = MathF.Max(maxHandY, item.WorldPositionMeters.Y);
            }
            else
            {
                minFootY = MathF.Min(minFootY, item.WorldPositionMeters.Y);
                maxFootY = MathF.Max(maxFootY, item.WorldPositionMeters.Y);
            }
        }

        bool hasHands = maxHandY > float.MinValue;
        bool hasFeet = minFootY < float.MaxValue;
        bool isSummitTopout = string.Equals(feature.Id, "stance-summit-topout", StringComparison.Ordinal);

        if (hasHands && hasFeet && !isSummitTopout && maxFootY > maxHandY + 0.15f)
        {
            failureReason = "Cannot step higher than your highest handhold.";
            return false;
        }

        if (hasHands && hasFeet && maxHandY < minFootY - 0.05f)
        {
            failureReason = "At least one hand must stay above your lowest foothold.";
            return false;
        }

        Vector3 projectedCom = ComputeCenterOfMassForConstellation(cliff, simulated.Values);

        // Verify the newly placed limb and at least one supporting foot/hand stay within anatomical reach of the new pelvis.
        float movedLimbMax = isHand ? MaxArmReachFromPelvisMeters : MaxLegReachFromPelvisMeters;
        float movedDist = Vector3.Distance(projectedCom, targetWorld);
        if (movedDist > movedLimbMax)
        {
            failureReason = $"{limb} cannot reach '{feature.Name}' from your current stance ({movedDist:F2}m > {movedLimbMax:F2}m).";
            return false;
        }

        if (hasFeet)
        {
            float closestFootDist = simulated.Values
                .Where(a => !IsHand(a.Limb))
                .Min(a => Vector3.Distance(projectedCom, a.WorldPositionMeters));

            if (closestFootDist > MaxLegReachFromPelvisMeters)
            {
                failureReason = "Step your feet up before reaching that high!";
                return false;
            }
        }

        failureReason = string.Empty;
        return true;
    }

    /// <summary>
    /// Computes a stamina drain multiplier in [1.0, 1.85] reflecting lateral barn-door torque and over-extension.
    /// </summary>
    public float ComputeBalancePumpMultiplier()
    {
        if (_attachments.Count == 0)
        {
            return 1.0f;
        }

        List<LimbAttachment> feet = _attachments.Values.Where(a => !IsHand(a.Limb)).ToList();
        List<LimbAttachment> hands = _attachments.Values.Where(a => IsHand(a.Limb)).ToList();

        float penalty = 0f;

        // No feet on rock (campusing!) severely taxes forearms.
        if (feet.Count == 0)
        {
            penalty += 0.65f;
        }
        else
        {
            float minSupportX = feet.Min(f => f.WorldPositionMeters.X) - 0.22f;
            float maxSupportX = feet.Max(f => f.WorldPositionMeters.X) + 0.22f;
            float handCenterX = hands.Count > 0 ? hands.Average(h => h.WorldPositionMeters.X) : CenterOfMassMeters.X;

            // Lateral barn-door offset when upper body pulls far outside foot support base.
            float lateralOffsetMeters = 0f;
            if (handCenterX < minSupportX)
            {
                lateralOffsetMeters = minSupportX - handCenterX;
            }
            else if (handCenterX > maxSupportX)
            {
                lateralOffsetMeters = handCenterX - maxSupportX;
            }

            penalty += Math.Clamp(lateralOffsetMeters * 0.55f, 0f, 0.55f);
        }

        // Crossed-over hands or severely bunched/over-extended vertical span adds extra isometric tension.
        if (_attachments.TryGetValue(LimbSlot.LeftHand, out LimbAttachment lh) &&
            _attachments.TryGetValue(LimbSlot.RightHand, out LimbAttachment rh) &&
            lh.WorldPositionMeters.X > rh.WorldPositionMeters.X + 0.25f)
        {
            penalty += 0.20f;
        }

        float verticalSpan = _attachments.Values.Max(a => a.WorldPositionMeters.Y) - _attachments.Values.Min(a => a.WorldPositionMeters.Y);
        if (verticalSpan > 1.85f)
        {
            penalty += (verticalSpan - 1.85f) * 0.45f;
        }

        return MathF.Round(Math.Clamp(1.0f + penalty, 1.0f, 1.85f), 2);
    }

    private LimbSlot ChooseSide(
        LimbSlot leftSlot,
        LimbSlot rightSlot,
        LimbSlot? lastMovedSlot,
        Vector3 targetWorldMeters)
    {
        bool leftAttached = _attachments.TryGetValue(leftSlot, out LimbAttachment left);
        bool rightAttached = _attachments.TryGetValue(rightSlot, out LimbAttachment right);

        if (!leftAttached && rightAttached)
        {
            return leftSlot;
        }

        if (!rightAttached && leftAttached)
        {
            return rightSlot;
        }

        if (leftAttached && rightAttached)
        {
            // Advance trailing lower limb when one limb lags noticeably behind.
            float heightDiff = left.WorldPositionMeters.Y - right.WorldPositionMeters.Y;
            if (heightDiff < -0.35f)
            {
                return leftSlot;
            }

            if (heightDiff > 0.35f)
            {
                return rightSlot;
            }

            // Otherwise prefer lateral side matching target X unless that exact limb just moved.
            if (MathF.Abs(targetWorldMeters.X - CenterOfMassMeters.X) > 0.22f)
            {
                return targetWorldMeters.X <= CenterOfMassMeters.X ? leftSlot : rightSlot;
            }

            if (lastMovedSlot == leftSlot)
            {
                return rightSlot;
            }

            if (lastMovedSlot == rightSlot)
            {
                return leftSlot;
            }
        }

        return targetWorldMeters.X <= CenterOfMassMeters.X ? leftSlot : rightSlot;
    }

    private void PruneOverstretchedTrailingFoot(CliffFace cliff, LimbSlot justMovedLimb)
    {
        PruneTrailingFootInSimulation(_attachments, justMovedLimb);
        if (_attachments.Count > 0)
        {
            Vector3 com = ComputeCenterOfMassForConstellation(cliff, _attachments.Values);
            List<LimbAttachment> feet = _attachments.Values.Where(a => !IsHand(a.Limb)).ToList();
            if (feet.Count > 1)
            {
                LimbAttachment lowestFoot = feet.OrderBy(f => f.WorldPositionMeters.Y).First();
                if (lowestFoot.Limb != justMovedLimb && Vector3.Distance(com, lowestFoot.WorldPositionMeters) > MaxLegReachFromPelvisMeters)
                {
                    _attachments.Remove(lowestFoot.Limb);
                }
            }
        }
    }

    private static void PruneTrailingFootInSimulation(Dictionary<LimbSlot, LimbAttachment> constellation, LimbSlot justMovedLimb)
    {
        if (constellation.TryGetValue(LimbSlot.LeftFoot, out LimbAttachment lf) &&
            constellation.TryGetValue(LimbSlot.RightFoot, out LimbAttachment rf))
        {
            float verticalFootSpread = MathF.Abs(lf.WorldPositionMeters.Y - rf.WorldPositionMeters.Y);
            if (verticalFootSpread > 1.25f)
            {
                LimbSlot lowerFoot = lf.WorldPositionMeters.Y < rf.WorldPositionMeters.Y ? LimbSlot.LeftFoot : LimbSlot.RightFoot;
                if (lowerFoot != justMovedLimb)
                {
                    constellation.Remove(lowerFoot);
                }
            }
        }
    }

    private void RecomputeCenterOfMass(CliffFace cliff)
    {
        CenterOfMassMeters = ComputeCenterOfMassForConstellation(cliff, _attachments.Values);
    }

    private static Vector3 ComputeCenterOfMassForConstellation(
        CliffFace cliff,
        IEnumerable<LimbAttachment> constellation)
    {
        List<LimbAttachment> all = constellation.ToList();
        if (all.Count == 0)
        {
            return new Vector3(0f, 0.95f, PelvisWallStandOffMeters);
        }

        List<LimbAttachment> hands = all.Where(a => IsHand(a.Limb)).ToList();
        List<LimbAttachment> feet = all.Where(a => !IsHand(a.Limb)).ToList();

        Vector3 rockAnchor;
        if (hands.Count > 0 && feet.Count > 0)
        {
            // Climbers shift weight onto their higher standing foot and higher pulling hand when extending upward.
            Vector3 handAnchor = WeightedUpperPosition(hands, upperWeight: 0.72f);
            Vector3 footAnchor = WeightedUpperPosition(feet, upperWeight: 0.78f);
            rockAnchor = Vector3.Lerp(footAnchor, handAnchor, 0.48f);
        }
        else if (hands.Count > 0)
        {
            rockAnchor = WeightedUpperPosition(hands, 0.72f) + new Vector3(0f, -0.55f, 0f);
        }
        else
        {
            rockAnchor = WeightedUpperPosition(feet, 0.78f) + new Vector3(0f, 0.55f, 0f);
        }

        return rockAnchor + (cliff.WallOutwardNormal * PelvisWallStandOffMeters);
    }

    private static Vector3 WeightedUpperPosition(IReadOnlyList<LimbAttachment> items, float upperWeight)
    {
        if (items.Count == 1)
        {
            return items[0].WorldPositionMeters;
        }

        List<LimbAttachment> sorted = items.OrderByDescending(i => i.WorldPositionMeters.Y).ToList();
        return Vector3.Lerp(sorted[1].WorldPositionMeters, sorted[0].WorldPositionMeters, upperWeight);
    }
}
