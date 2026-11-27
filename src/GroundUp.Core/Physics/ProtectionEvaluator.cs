using GroundUp.Core.Gear;
using GroundUp.Core.Geology;

namespace GroundUp.Core.Physics;

public enum PlacementQuality
{
    Unusable,
    SketchyBodyWeightOnly,
    Marginal,
    Solid,
    Bomber
}

public sealed record PlacementEvaluation(
    GearSpecBase Gear,
    string FeatureId,
    float PlacementHeightMeters,
    bool FitsGeometry,
    float EffectiveHoldingForceKilonewtons,
    PlacementQuality Quality,
    string DiagnosticSummary)
{
    /// <summary>
    /// Checks whether the placement can support static aid/drilling body weight with a 1.35x shifting-body dynamic margin.
    /// </summary>
    public bool HoldsBodyWeight(float climberMassKg = 78f)
    {
        if (!FitsGeometry || EffectiveHoldingForceKilonewtons <= 0f)
        {
            return false;
        }

        float bodyLoadKn = (climberMassKg * 9.81f / 1000f) * 1.35f;
        return EffectiveHoldingForceKilonewtons >= bodyLoadKn;
    }
}

public sealed record PlacedProtection(
    string PlacementId,
    GearSpecBase Gear,
    string FeatureId,
    float HeightMeters,
    float EffectiveHoldingForceKilonewtons,
    PlacementQuality Quality);

/// <summary>
/// Evaluates holding strength of trad, aid, and fixed protection against semantic rock geometry.
/// </summary>
public static class ProtectionEvaluator
{
    public static PlacementEvaluation EvaluatePlacement(
        GearSpecBase gear,
        RockFeatureBase feature,
        float normalizedCrackPosition,
        GeologyProfile geology)
    {
        ArgumentNullException.ThrowIfNull(gear);
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(geology);

        return (gear, feature) switch
        {
            (CamSpec cam, CrackFeature crack) => EvaluateCam(cam, crack, normalizedCrackPosition, geology),
            (PitonSpec piton, CrackFeature seam) => EvaluatePiton(piton, seam, normalizedCrackPosition, geology),
            (SkyhookSpec hook, EdgeFeature edge) => EvaluateSkyhook(hook, edge, geology),
            (SlingSpec sling, HornFeature horn) => EvaluateSling(sling, horn, geology),
            _ => Unusable(
                gear,
                feature.Id,
                feature.GetLocalAttachmentPointMeters(normalizedCrackPosition).Y,
                $"{gear.DisplayName} ({gear.Kind}) cannot be placed on '{feature.Name}'.")
        };
    }

    public static PlacementEvaluation EvaluateCam(
        CamSpec cam,
        CrackFeature crack,
        float normalizedCrackPosition,
        GeologyProfile geology)
    {
        ArgumentNullException.ThrowIfNull(cam);
        ArgumentNullException.ThrowIfNull(crack);
        ArgumentNullException.ThrowIfNull(geology);

        CrackSample sample = crack.SampleAtNormalized(normalizedCrackPosition);
        if (crack.IsTrundled)
        {
            return Unusable(cam, crack.Id, sample.WorldHeightMeters, "Feature has been trundled away.");
        }

        // Usable expansion window leaves 6% margin above solid-closed and 5% below fully umbrella'd.
        float range = cam.MaxWidthMillimeters - cam.MinWidthMillimeters;
        float minUsable = cam.MinWidthMillimeters + (range * 0.06f);
        float maxUsable = cam.MaxWidthMillimeters - (range * 0.05f);

        if (sample.WidthMillimeters < minUsable)
        {
            return Unusable(cam, crack.Id, sample.WorldHeightMeters,
                $"Crack ({sample.WidthMillimeters:F1}mm) is too narrow; cam is over-cammed/jammed.");
        }

        if (sample.WidthMillimeters > maxUsable)
        {
            return Unusable(cam, crack.Id, sample.WorldHeightMeters,
                $"Crack ({sample.WidthMillimeters:F1}mm) exceeds usable range; cam umbrellas open.");
        }

        if (sample.DepthMillimeters < cam.HeadDepthMillimeters * 0.55f)
        {
            return Unusable(cam, crack.Id, sample.WorldHeightMeters,
                $"Crack depth ({sample.DepthMillimeters:F1}mm) is too shallow for all four lobes.");
        }

        float expansion = (sample.WidthMillimeters - cam.MinWidthMillimeters) / range;

        // Sweet spot is 0.20..0.65 expansion; tipped-out placements (> 0.65) lose holding power steeply.
        float expansionFactor = expansion switch
        {
            <= 0.20f => 0.78f + (expansion / 0.20f * 0.22f),
            <= 0.65f => 1.00f,
            _ => Math.Max(0.28f, 1.00f - ((expansion - 0.65f) / 0.30f * 0.68f))
        };

        // Cam lobes rely on friction * tan(camming angle); flaring cracks + slick/mossy rock cause blowouts.
        float effectiveFriction = geology.BaseFrictionCoefficient * crack.Surface.CleanlinessMultiplier;
        float flarePenalty = sample.FlareDegrees <= 4f
            ? 1.0f
            : Math.Max(0.12f, 1.0f - ((sample.FlareDegrees - 4f) / 28f));

        float frictionFactor = Math.Clamp(effectiveFriction / 0.75f, 0.35f, 1.08f);
        float depthFactor = Math.Clamp(sample.DepthMillimeters / cam.HeadDepthMillimeters, 0.65f, 1.0f);
        float rockStrengthCapKn = ComputeRockSubstrateCapKn(crack.StructuralIntegrity, geology, contactAreaScale: range / 25f);

        float rawHoldKn = cam.RatedStrengthKilonewtons * expansionFactor * flarePenalty * frictionFactor * depthFactor;
        float finalHoldKn = MathF.Round(Math.Min(rawHoldKn, rockStrengthCapKn), 2);

        PlacementQuality quality = ClassifyLeadGearQuality(finalHoldKn, crack.IsHollowChoss);
        string note = $"Expansion {expansion:P0}, flare {sample.FlareDegrees:F1} deg, hold {finalHoldKn:F1} kN.";

        return new PlacementEvaluation(cam, crack.Id, sample.WorldHeightMeters, FitsGeometry: true, finalHoldKn, quality, note);
    }

    public static PlacementEvaluation EvaluatePiton(
        PitonSpec piton,
        CrackFeature seam,
        float normalizedCrackPosition,
        GeologyProfile geology)
    {
        ArgumentNullException.ThrowIfNull(piton);
        ArgumentNullException.ThrowIfNull(seam);
        ArgumentNullException.ThrowIfNull(geology);

        CrackSample sample = seam.SampleAtNormalized(normalizedCrackPosition);
        if (seam.IsTrundled)
        {
            return Unusable(piton, seam.Id, sample.WorldHeightMeters, "Seam rock was trundled.");
        }

        if (sample.WidthMillimeters < piton.MinSeamWidthMillimeters || sample.WidthMillimeters > piton.MaxSeamWidthMillimeters)
        {
            return Unusable(piton, seam.Id, sample.WorldHeightMeters,
                $"Seam width ({sample.WidthMillimeters:F1}mm) outside piton blade range [{piton.MinSeamWidthMillimeters}-{piton.MaxSeamWidthMillimeters}mm].");
        }

        if (sample.DepthMillimeters < piton.BladeLengthMillimeters * 0.40f)
        {
            return Unusable(piton, seam.Id, sample.WorldHeightMeters, "Bottoming shallow seam; piton eyes stick out dangerously.");
        }

        // Pitons tolerate moderate flare much better than cams, but pulverize soft friable sandstone!
        float depthRatio = Math.Clamp(sample.DepthMillimeters / (piton.BladeLengthMillimeters * 0.85f), 0.45f, 1.0f);
        float flareFactor = sample.FlareDegrees <= 12f
            ? 1.0f
            : Math.Max(0.40f, 1.0f - ((sample.FlareDegrees - 12f) / 35f));

        float friabilityFactor = 1.0f - (geology.Friability * 0.75f);
        float rockCapKn = ComputeRockSubstrateCapKn(seam.StructuralIntegrity, geology, contactAreaScale: 0.9f);

        float rawHoldKn = piton.RatedStrengthKilonewtons * depthRatio * flareFactor * friabilityFactor * seam.Surface.CleanlinessMultiplier;
        float finalHoldKn = MathF.Round(Math.Min(rawHoldKn, rockCapKn), 2);

        PlacementQuality quality = ClassifyLeadGearQuality(finalHoldKn, seam.IsHollowChoss);
        return new PlacementEvaluation(piton, seam.Id, sample.WorldHeightMeters, true, finalHoldKn, quality,
            $"Driven {depthRatio:P0} blade depth in {geology.Type}, hold {finalHoldKn:F1} kN.");
    }

    public static PlacementEvaluation EvaluateSkyhook(
        SkyhookSpec hook,
        EdgeFeature edge,
        GeologyProfile geology)
    {
        ArgumentNullException.ThrowIfNull(hook);
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentNullException.ThrowIfNull(geology);

        if (edge.IsTrundled)
        {
            return Unusable(hook, edge.Id, edge.PositionMeters.Y, "Edge has been trundled.");
        }

        if (edge.DepthMillimeters < hook.MinEdgeDepthMillimeters)
        {
            return Unusable(hook, edge.Id, edge.PositionMeters.Y,
                $"Micro-edge ({edge.DepthMillimeters:F1}mm) shallower than hook point minimum ({hook.MinEdgeDepthMillimeters:F1}mm).");
        }

        // Effective incut accounts for moss/slime making sloping edges skate immediately.
        float effectiveIncut = edge.IncutDegrees - ((1.0f - edge.Surface.CleanlinessMultiplier) * 14f);
        if (effectiveIncut < hook.MaxLipSlopeToleranceDegrees)
        {
            return Unusable(hook, edge.Id, edge.PositionMeters.Y,
                $"Edge slopes ({edge.IncutDegrees:F1} deg) beyond hook bite threshold; skates off under load.");
        }

        float incutFactor = Math.Clamp(0.55f + ((effectiveIncut - hook.MaxLipSlopeToleranceDegrees) / 28f), 0.45f, 1.05f);
        // Point load of a steel hook crushes friable sandstone crystals under body weight and slips on mossy slime!
        float crystalStrengthFactor = (1.0f - (geology.Friability * 0.85f)) * edge.StructuralIntegrity * edge.Surface.CleanlinessMultiplier;
        float finalHoldKn = MathF.Round(hook.RatedStrengthKilonewtons * incutFactor * crystalStrengthFactor, 2);

        if (finalHoldKn < 1.05f)
        {
            return new PlacementEvaluation(hook, edge.Id, edge.PositionMeters.Y, FitsGeometry: true, finalHoldKn,
                PlacementQuality.Unusable, $"Rock lip crumbles or skates at {finalHoldKn:F2} kN (below body weight).");
        }

        return new PlacementEvaluation(hook, edge.Id, edge.PositionMeters.Y, FitsGeometry: true, finalHoldKn,
            PlacementQuality.SketchyBodyWeightOnly,
            $"Aid/stance skyhook holds ~{finalHoldKn:F1} kN (body weight only; will pop on dynamic lead fall).");
    }

    public static PlacementEvaluation EvaluateSling(
        SlingSpec sling,
        HornFeature horn,
        GeologyProfile geology)
    {
        ArgumentNullException.ThrowIfNull(sling);
        ArgumentNullException.ThrowIfNull(horn);
        ArgumentNullException.ThrowIfNull(geology);

        if (horn.IsTrundled)
        {
            return Unusable(sling, horn.Id, horn.PositionMeters.Y, "Horn was trundled.");
        }

        float effectiveCollarDegrees = horn.CollarAngleDegrees - ((1.0f - horn.Surface.CleanlinessMultiplier) * 10f);
        if (effectiveCollarDegrees < -8f)
        {
            return Unusable(sling, horn.Id, horn.PositionMeters.Y,
                "Tapering or slime-coated horn lacks a neck constriction; runner slips straight off.");
        }

        float collarSecurity = Math.Clamp(0.45f + ((effectiveCollarDegrees + 8f) / 28f), 0.40f, 1.0f);
        float rockCapKn = ComputeRockSubstrateCapKn(horn.StructuralIntegrity, geology, contactAreaScale: horn.NeckRadiusMillimeters / 22f);

        float finalHoldKn = MathF.Round(Math.Min(sling.RatedStrengthKilonewtons * collarSecurity, rockCapKn), 2);
        PlacementQuality quality = ClassifyLeadGearQuality(finalHoldKn, horn.IsHollowChoss);

        return new PlacementEvaluation(sling, horn.Id, horn.PositionMeters.Y, true, finalHoldKn, quality,
            $"Slung horn ({horn.CollarAngleDegrees:F1} deg collar), hold {finalHoldKn:F1} kN.");
    }

    public static PlacementEvaluation EvaluateDrilledBolt(
        BoltSpec bolt,
        float heightMeters,
        float rockIntegrityAtHole,
        GeologyProfile geology)
    {
        ArgumentNullException.ThrowIfNull(bolt);
        ArgumentNullException.ThrowIfNull(geology);

        rockIntegrityAtHole = Math.Clamp(rockIntegrityAtHole, 0.05f, 1.0f);

        // Mechanical wedge bolts lose massive pull-out strength in soft friable sandstone vs glue-ins!
        float geologyAnchorFactor = bolt.IsGlueIn
            ? 1.0f - (geology.Friability * 0.18f)
            : 1.0f - (geology.Friability * 0.65f);

        // Drilling into a hollow choss block caps bolt strength at the block's fracture strength!
        float integrityFactor = rockIntegrityAtHole < 0.50f
            ? rockIntegrityAtHole * 0.45f
            : MathF.Pow(rockIntegrityAtHole, 1.25f);

        float finalHoldKn = MathF.Round(bolt.RatedStrengthKilonewtons * geologyAnchorFactor * integrityFactor, 2);
        PlacementQuality quality = ClassifyLeadGearQuality(finalHoldKn, rockIntegrityAtHole < 0.45f);

        return new PlacementEvaluation(bolt, $"bolt@{heightMeters:F1}m", heightMeters, true, finalHoldKn, quality,
            $"{bolt.DisplayName} in {geology.Type} (integrity {rockIntegrityAtHole:P0}): {finalHoldKn:F1} kN.");
    }

    private static float ComputeRockSubstrateCapKn(float structuralIntegrity, GeologyProfile geology, float contactAreaScale)
    {
        float areaMultiplier = Math.Clamp(contactAreaScale, 0.45f, 1.45f);
        float intactRockKn = 24.0f * (1.0f - (geology.Friability * 0.52f)) * areaMultiplier;
        float integrityCurve = structuralIntegrity < 0.45f
            ? structuralIntegrity * 0.42f
            : MathF.Pow(structuralIntegrity, 1.35f);

        return Math.Max(0.5f, intactRockKn * integrityCurve);
    }

    private static PlacementQuality ClassifyLeadGearQuality(float holdKn, bool isHollowChoss)
    {
        if (holdKn < 1.1f)
        {
            return PlacementQuality.Unusable;
        }

        if (holdKn < 4.0f || isHollowChoss)
        {
            return PlacementQuality.SketchyBodyWeightOnly;
        }

        return holdKn switch
        {
            < 7.5f => PlacementQuality.Marginal,
            < 11.5f => PlacementQuality.Solid,
            _ => PlacementQuality.Bomber
        };
    }

    private static PlacementEvaluation Unusable(GearSpecBase gear, string featureId, float heightMeters, string reason) =>
        new(gear, featureId, heightMeters, FitsGeometry: false, EffectiveHoldingForceKilonewtons: 0f, PlacementQuality.Unusable, reason);
}
