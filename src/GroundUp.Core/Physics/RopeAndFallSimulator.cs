namespace GroundUp.Core.Physics;

public enum FallOutcome
{
    CaughtByGear,
    GroundFall
}

public readonly record struct BlownPieceRecord(
    string PlacementId,
    string GearCode,
    float HeightMeters,
    float AppliedLoadKilonewtons,
    float RatedHoldKilonewtons);

public sealed record FallSimulationResult(
    FallOutcome Outcome,
    float StartHeightMeters,
    float FinalRestingHeightMeters,
    float TotalFallDistanceMeters,
    float InitialFallFactor,
    float PeakClimberImpactKilonewtons,
    float PeakTopPieceLoadKilonewtons,
    string? CatchingPlacementId,
    IReadOnlyList<BlownPieceRecord> BlownPieces);

/// <summary>
/// Simulates dynamic lead falls on an automated ground-anchor lead-solo tether,
/// including Fall Factor physics, dynamic rope stretch, pulley multiplication, and sequential gear zippering.
/// </summary>
public static class RopeAndFallSimulator
{
    private const float Gravity = 9.81f;

    /// <summary>
    /// Calibrated UIAA single dynamic rope modulus (~29 kN) so an 80kg Factor 1.77 fall
    /// stays safely below the 12 kN UIAA peak climber force limit while testing top anchors near ~14-15 kN.
    /// </summary>
    private const float DefaultDynamicRopeModulusKilonewtons = 29.0f;

    /// <summary>
    /// Pulley multiplier at the highest protection carabiner (~1.60x accounting for carabiner/tether friction).
    /// </summary>
    private const float CarabinerPulleyFactor = 1.60f;

    public static FallSimulationResult SimulateLeadFall(
        float climberHeightMeters,
        IReadOnlyList<PlacedProtection> activeProtectionChain,
        float climberMassKg = 78f,
        float slackMeters = 0.25f,
        float ropeModulusKilonewtons = DefaultDynamicRopeModulusKilonewtons)
    {
        ArgumentNullException.ThrowIfNull(activeProtectionChain);
        climberHeightMeters = Math.Max(0f, climberHeightMeters);
        climberMassKg = Math.Max(35f, climberMassKg);
        slackMeters = Math.Max(0f, slackMeters);
        ropeModulusKilonewtons = Math.Max(5f, ropeModulusKilonewtons);

        // Sort protection highest-first so falls test the top piece first and zipper downward on blowouts.
        List<PlacedProtection> orderedChain = activeProtectionChain
            .Where(p => p.HeightMeters > 0f && p.EffectiveHoldingForceKilonewtons > 0f)
            .OrderByDescending(p => p.HeightMeters)
            .ToList();

        List<BlownPieceRecord> blownPieces = [];
        float climberWeightKn = climberMassKg * Gravity / 1000f;

        if (orderedChain.Count == 0 || climberHeightMeters <= 0.1f)
        {
            return new FallSimulationResult(
                FallOutcome.GroundFall,
                StartHeightMeters: climberHeightMeters,
                FinalRestingHeightMeters: 0f,
                TotalFallDistanceMeters: climberHeightMeters,
                InitialFallFactor: climberHeightMeters > 0f ? 1.0f : 0f,
                PeakClimberImpactKilonewtons: 0f,
                PeakTopPieceLoadKilonewtons: 0f,
                CatchingPlacementId: null,
                BlownPieces: blownPieces);
        }

        // Active rope paid out from the automated ground-anchor lead-solo device (at 0m) through the chain to the climber.
        float highestPieceOrClimber = Math.Max(climberHeightMeters, orderedChain[0].HeightMeters);
        float activeRopeLengthMeters = Math.Max(0.5f, highestPieceOrClimber + (highestPieceOrClimber - climberHeightMeters) + slackMeters);

        // Track energy dissipated by ripping marginal gear so blew-out pieces absorb a small fraction of fall energy.
        float dissipatedEnergyKilojoules = 0f;
        float initialFallFactor = 0f;
        float peakClimberKn = 0f;
        float peakPieceKn = 0f;

        for (int i = 0; i < orderedChain.Count; i++)
        {
            PlacedProtection candidate = orderedChain[i];

            // Free-fall distance until the rope comes taut on this piece:
            // climber drops from start height past the piece by (ropeLength - pieceHeight) below the piece.
            float ropeTautClimberHeight = candidate.HeightMeters - (activeRopeLengthMeters - candidate.HeightMeters);
            float freeFallDistance = climberHeightMeters - ropeTautClimberHeight;

            // If the climber hits the dirt before the rope even comes tight on this piece -> immediate ground fall!
            if (ropeTautClimberHeight <= 0f)
            {
                return new FallSimulationResult(
                    FallOutcome.GroundFall,
                    climberHeightMeters,
                    FinalRestingHeightMeters: 0f,
                    TotalFallDistanceMeters: climberHeightMeters,
                    InitialFallFactor: initialFallFactor > 0f ? initialFallFactor : MathF.Round(climberHeightMeters / activeRopeLengthMeters, 2),
                    PeakClimberImpactKilonewtons: peakClimberKn,
                    PeakTopPieceLoadKilonewtons: peakPieceKn,
                    CatchingPlacementId: null,
                    BlownPieces: blownPieces);
            }

            float rawFallFactor = Math.Max(0.02f, freeFallDistance / activeRopeLengthMeters);
            if (i == 0)
            {
                initialFallFactor = MathF.Round(rawFallFactor, 2);
            }

            // Kinetic energy at rope-taut point minus energy bled off by prior blown pieces (~0.10m tear-out deformation per blown piece).
            float potentialEnergyKj = climberWeightKn * Math.Max(0f, freeFallDistance);
            float remainingEnergyKj = Math.Max(0.02f, potentialEnergyKj - dissipatedEnergyKilojoules);
            float effectiveFallFactor = remainingEnergyKj / (climberWeightKn * activeRopeLengthMeters);

            float climberImpactKn = climberWeightKn * (1.0f + MathF.Sqrt(1.0f + (2.0f * ropeModulusKilonewtons * effectiveFallFactor / climberWeightKn)));
            float pieceLoadKn = climberImpactKn * CarabinerPulleyFactor;

            if (pieceLoadKn > candidate.EffectiveHoldingForceKilonewtons)
            {
                // Piece blows at its failure limit! Cap transmitted rope forces to what the piece actually resisted before popping.
                float transmittedPieceKn = candidate.EffectiveHoldingForceKilonewtons;
                float transmittedClimberKn = transmittedPieceKn / CarabinerPulleyFactor;
                peakClimberKn = Math.Max(peakClimberKn, MathF.Round(transmittedClimberKn, 2));
                peakPieceKn = Math.Max(peakPieceKn, MathF.Round(transmittedPieceKn, 2));

                blownPieces.Add(new BlownPieceRecord(
                    candidate.PlacementId,
                    candidate.Gear.Code,
                    candidate.HeightMeters,
                    MathF.Round(pieceLoadKn, 2),
                    candidate.EffectiveHoldingForceKilonewtons));

                dissipatedEnergyKilojoules += candidate.EffectiveHoldingForceKilonewtons * 0.10f;
                continue;
            }

            peakClimberKn = Math.Max(peakClimberKn, MathF.Round(climberImpactKn, 2));
            peakPieceKn = Math.Max(peakPieceKn, MathF.Round(pieceLoadKn, 2));

            // Piece holds! Compute dynamic rope elongation (accounting for carabiner friction along the lower strand)
            // to verify the climber does not stretch into the ground.
            float stretchFraction = Math.Clamp(0.04f + ((climberImpactKn - climberWeightKn) / 40.0f), 0.04f, 0.32f);
            float dynamicStretchMeters = (activeRopeLengthMeters * 0.85f) * stretchFraction;
            float finalRestingHeight = ropeTautClimberHeight - dynamicStretchMeters;

            if (finalRestingHeight <= 0f)
            {
                return new FallSimulationResult(
                    FallOutcome.GroundFall,
                    climberHeightMeters,
                    FinalRestingHeightMeters: 0f,
                    TotalFallDistanceMeters: climberHeightMeters,
                    InitialFallFactor: initialFallFactor,
                    PeakClimberImpactKilonewtons: peakClimberKn,
                    PeakTopPieceLoadKilonewtons: peakPieceKn,
                    CatchingPlacementId: null,
                    BlownPieces: blownPieces);
            }

            float totalDrop = MathF.Round(climberHeightMeters - finalRestingHeight, 2);
            return new FallSimulationResult(
                FallOutcome.CaughtByGear,
                climberHeightMeters,
                FinalRestingHeightMeters: MathF.Round(finalRestingHeight, 2),
                TotalFallDistanceMeters: totalDrop,
                InitialFallFactor: initialFallFactor,
                PeakClimberImpactKilonewtons: peakClimberKn,
                PeakTopPieceLoadKilonewtons: peakPieceKn,
                CatchingPlacementId: candidate.PlacementId,
                BlownPieces: blownPieces);
        }

        // Every piece in the chain blew out -> Ground fall!
        return new FallSimulationResult(
            FallOutcome.GroundFall,
            climberHeightMeters,
            FinalRestingHeightMeters: 0f,
            TotalFallDistanceMeters: climberHeightMeters,
            InitialFallFactor: initialFallFactor,
            PeakClimberImpactKilonewtons: peakClimberKn,
            PeakTopPieceLoadKilonewtons: peakPieceKn,
            CatchingPlacementId: null,
            BlownPieces: blownPieces);
    }
}
