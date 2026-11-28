using System.Numerics;
using GroundUp.Core.Bolting;
using GroundUp.Core.Geology;
using GroundUp.Core.Physics;

namespace GroundUp.Core.Scoring;

public sealed record RouteEvaluationReport(
    float StarRating,
    string SuggestedYdsGrade,
    float SafetyScore,
    float ErgonomicsScore,
    float EthicsScore,
    float RockQualityScore,
    IReadOnlyList<string> FeedbackNotes);

public sealed record DevelopedRoute(
    string RouteName,
    string AuthorProposedGrade,
    CliffFace Cliff,
    RouteEvaluationReport Evaluation);

/// <summary>
/// Algorithmic 1.0-4.0 Star Rating and YDS Grade engine evaluating safety, clipping ergonomics,
/// natural-protection bolting ethics, and rock cleaning/choss preparation.
/// </summary>
public static class RouteStarRatingEvaluator
{
    public static RouteEvaluationReport EvaluateRoute(
        CliffFace cliff,
        IReadOnlyList<PlacedProtection> ffaProtectionChain,
        IReadOnlyList<string>? climbedFeatureIds = null)
    {
        ArgumentNullException.ThrowIfNull(cliff);
        ArgumentNullException.ThrowIfNull(ffaProtectionChain);

        List<string> notes = [];
        List<BoltInstallation> completedBolts = cliff.BoltInstallations
            .Where(b => b.IsCompletedBolt)
            .OrderBy(b => b.WorldPositionMeters.Y)
            .ToList();

        bool hasHollowDeathBolt = false;

        // 1. SAFETY SCORE (35% weight)
        float safety = 100f;
        foreach (BoltInstallation bolt in completedBolts)
        {
            float holdKn = bolt.InstalledEvaluation?.EffectiveHoldingForceKilonewtons ?? 0f;
            if (holdKn < 8.0f || bolt.SubstrateIntegrity < 0.45f)
            {
                hasHollowDeathBolt = true;
                safety -= 45f;
                notes.Add($"SAFETY HAZARD: {bolt.Id} ({bolt.WorldPositionMeters.Y:F1}m) is drilled into hollow choss ({holdKn:F1} kN)!");
            }
            else if (!bolt.WasHoleCleanWhenBoltInserted)
            {
                safety -= 14f;
                notes.Add($"Sketchy bolt prep: {bolt.Id} was driven into an un-blown dust-filled hole.");
            }
        }

        // Check ground-fall runout exposure at representative crux heights along the 10m climb.
        float[] testFallHeights = [3.8f, 5.6f, 7.6f, 9.2f];
        int deckZones = 0;
        foreach (float h in testFallHeights.Where(y => y < cliff.HeightMeters - 0.3f))
        {
            List<PlacedProtection> chainBelow = ffaProtectionChain
                .Where(p => p.HeightMeters <= h + 0.15f)
                .ToList();

            FallSimulationResult sim = RopeAndFallSimulator.SimulateLeadFall(h, chainBelow);
            if (sim.Outcome == FallOutcome.GroundFall)
            {
                deckZones++;
            }
        }

        if (deckZones > 0)
        {
            safety -= deckZones * 22f;
            notes.Add($"Runout warning: {deckZones} fall checkpoint(s) above 3.5m risk a ground fall with the clipped protection.");
        }
        else
        {
            notes.Add("Solid protection spacing—no mid/upper-wall ground-fall deck zones.");
        }

        safety = Math.Clamp(safety, 0f, 100f);

        // 2. CLIPPING ERGONOMICS SCORE (20% weight)
        float ergonomics = 100f;
        for (int i = 0; i < completedBolts.Count; i++)
        {
            BoltInstallation bolt = completedBolts[i];
            float distToHold = FindDistanceToNearestClimbableFeature(cliff, bolt.WorldPositionMeters);
            if (distToHold > 1.50f)
            {
                ergonomics -= 24f;
                notes.Add($"Awkward clip: {bolt.Id} ({bolt.WorldPositionMeters.Y:F1}m) sits far from comfortable free holds ({distToHold:F2}m).");
            }
            else if (distToHold > 1.25f)
            {
                ergonomics -= 10f;
            }

            if (i > 0)
            {
                float lateralZigZag = MathF.Abs(bolt.WorldPositionMeters.X - completedBolts[i - 1].WorldPositionMeters.X);
                if (lateralZigZag > 1.85f)
                {
                    ergonomics -= 14f;
                    notes.Add($"Rope drag: wide {lateralZigZag:F1}m horizontal zig-zag between {completedBolts[i - 1].Id} and {bolt.Id}.");
                }
            }
        }

        ergonomics = Math.Clamp(ergonomics, 0f, 100f);

        // 3. NATURAL-PRO & BOLTING ETHICS SCORE (25% weight)
        float ethics = 100f;
        for (int i = 0; i < completedBolts.Count; i++)
        {
            BoltInstallation bolt = completedBolts[i];
            bool isTopAnchorStationBolt = bolt.WallLocalPositionMeters.Y >= cliff.HeightMeters - 1.0f;

            // Penalize bolting lead protection right next to a cam-protectable crack or constricted horn
            // (exempting dedicated top-anchor lower-off bolts near the cliff rim).
            RockFeatureBase? naturalAlternative = isTopAnchorStationBolt
                ? null
                : FindAdjacentNaturalProtectionFeature(cliff, bolt.WallLocalPositionMeters);

            if (naturalAlternative is not null)
            {
                ethics -= 32f;
                notes.Add($"ETHICS DEDUCTION: {bolt.Id} ({bolt.WorldPositionMeters.Y:F1}m) was drilled beside '{naturalAlternative.Name}' where clean trad gear goes in!");
            }

            if (i > 0 && bolt.WorldPositionMeters.Y < cliff.HeightMeters - 1.1f)
            {
                float verticalSpacing = bolt.WorldPositionMeters.Y - completedBolts[i - 1].WorldPositionMeters.Y;
                if (verticalSpacing < 1.30f)
                {
                    ethics -= 18f;
                    notes.Add($"Over-bolted spacing ({verticalSpacing:F2}m) between {completedBolts[i - 1].Id} and {bolt.Id}.");
                }
            }
        }

        if (ethics >= 95f)
        {
            notes.Add("Clean ground-up ethics: natural gear features respected and bolts reserved for blank/thin sections.");
        }

        ethics = Math.Clamp(ethics, 0f, 100f);

        // 4. ROCK QUALITY & PREPARATION SCORE (20% weight)
        float rockQuality = 100f;
        HashSet<string> climbedSet = climbedFeatureIds is not null
            ? climbedFeatureIds.ToHashSet(StringComparer.Ordinal)
            : [];

        foreach (RockFeatureBase feature in cliff.Features)
        {
            if (feature.IsHollowChoss && !feature.IsTrundled)
            {
                bool nearBolts = completedBolts.Any(b => Vector3.Distance(b.WorldPositionMeters, cliff.GetFeatureWorldPositionMeters(feature)) <= 1.8f);
                bool onLine = climbedSet.Count == 0 || nearBolts || feature.PositionMeters.X <= 1.5f;
                if (onLine)
                {
                    rockQuality -= 35f;
                    notes.Add($"Loose rock hazard: '{feature.Name}' was left un-trundled on the cliff!");
                }
            }

            if (!feature.IsTrundled && !feature.Surface.IsClean && (climbedSet.Contains(feature.Id) || climbedSet.Count == 0))
            {
                rockQuality -= 22f;
                notes.Add($"Dirty hold: '{feature.Name}' still has unbrushed moss/dirt.");
            }
        }

        if (rockQuality >= 95f)
        {
            notes.Add("Five-star rock prep: moss brushed clean and hollow choss safely trundled.");
        }

        rockQuality = Math.Clamp(rockQuality, 0f, 100f);

        // Weighted 1.0 - 4.0 Star Rating
        float composite = (safety * 0.35f) + (ergonomics * 0.20f) + (ethics * 0.25f) + (rockQuality * 0.20f);
        float rawStars = 1.0f + ((composite / 100f) * 3.0f);
        if (hasHollowDeathBolt)
        {
            rawStars = MathF.Min(rawStars, 2.0f);
        }

        float finalStars = MathF.Round(Math.Clamp(rawStars, 1.0f, 4.0f), 1);
        string grade = EstimateYdsGrade(cliff, climbedSet);

        return new RouteEvaluationReport(
            StarRating: finalStars,
            SuggestedYdsGrade: grade,
            SafetyScore: MathF.Round(safety, 0),
            ErgonomicsScore: MathF.Round(ergonomics, 0),
            EthicsScore: MathF.Round(ethics, 0),
            RockQualityScore: MathF.Round(rockQuality, 0),
            FeedbackNotes: notes);
    }

    private static float FindDistanceToNearestClimbableFeature(CliffFace cliff, Vector3 boltWorldPos)
    {
        float best = float.MaxValue;
        foreach (RockFeatureBase feat in cliff.Features)
        {
            if (feat.IsTrundled || feat.IsHollowChoss)
            {
                continue;
            }

            float norm = feat is CrackFeature c ? c.GetNormalizedPositionForWorldHeight(boltWorldPos.Y) : 0.5f;
            if (feat is CrackFeature thin && thin.SampleAtNormalized(norm).WidthMillimeters < 11f)
            {
                continue;
            }

            float d = Vector3.Distance(boltWorldPos, cliff.GetFeatureWorldPositionMeters(feat, norm));
            best = MathF.Min(best, d);
        }

        return best;
    }

    private static RockFeatureBase? FindAdjacentNaturalProtectionFeature(CliffFace cliff, Vector3 boltLocalPos)
    {
        foreach (RockFeatureBase feat in cliff.Features)
        {
            if (feat.IsTrundled || feat.IsHollowChoss)
            {
                continue;
            }

            if (feat is CrackFeature crack)
            {
                if (boltLocalPos.Y < crack.MinWorldHeightMeters - 0.30f || boltLocalPos.Y > crack.MaxWorldHeightMeters + 0.30f)
                {
                    continue;
                }

                CrackSample sample = crack.SampleAtWorldHeight(boltLocalPos.Y);
                // Thin incipient seams (< 6.8mm, piton-only) do NOT accept free-climbing cams, so bolting beside them is ethical!
                bool acceptsFreeCam = sample.WidthMillimeters is >= 6.8f and <= 112.0f && sample.FlareDegrees <= 18f;
                if (acceptsFreeCam && MathF.Abs(boltLocalPos.X - crack.PositionMeters.X) <= 0.85f)
                {
                    return crack;
                }
            }
            else if (feat is HornFeature horn && horn.CollarAngleDegrees >= 5f)
            {
                float dx = boltLocalPos.X - horn.PositionMeters.X;
                float dy = boltLocalPos.Y - horn.PositionMeters.Y;
                if (MathF.Sqrt((dx * dx) + (dy * dy)) <= 0.75f)
                {
                    return horn;
                }
            }
        }

        return null;
    }

    private static string EstimateYdsGrade(CliffFace cliff, HashSet<string> climbedIds)
    {
        float smallestEdgeMm = 20f;
        foreach (EdgeFeature edge in cliff.Features.OfType<EdgeFeature>())
        {
            if (edge.IsTrundled || (climbedIds.Count > 0 && !climbedIds.Contains(edge.Id)))
            {
                continue;
            }

            smallestEdgeMm = MathF.Min(smallestEdgeMm, edge.DepthMillimeters);
        }

        float difficultyIndex = (cliff.OverhangDegrees * 0.28f) + ((22f - smallestEdgeMm) * 0.55f);
        return difficultyIndex switch
        {
            < 3.5f => "5.8",
            < 5.5f => "5.9",
            < 7.2f => "5.10a",
            < 8.8f => "5.10c",
            < 10.5f => "5.11a",
            < 12.5f => "5.11c",
            _ => "5.12a"
        };
    }
}
