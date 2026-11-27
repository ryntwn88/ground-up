using System.Numerics;
using GroundUp.Core.Gear;
using GroundUp.Core.Geology;
using GroundUp.Core.Physics;
using GroundUp.Core.Stamina;

namespace GroundUp.Core.Climbing;

public enum SessionMode
{
    Grounded,
    FreeClimbing,
    StanceInspect,
    CleaningAndGear,
    HangingOnGear,
    WhipperFall,
    ToppedOut
}

public enum ActiveToolKind
{
    None,
    HammerSound,
    WireBrush,
    Prybar,
    PlaceGear
}

public readonly record struct PlacedProtectionDetail(
    PlacedProtection Protection,
    float NormalizedFeaturePosition,
    Vector3 WorldPositionMeters);

/// <summary>
/// Authoritative Phase 1 gameplay state machine uniting 4-limb free climbing, 3-point stance locks,
/// rock sounding/brushing/trundling, trad/aid gear placement, body-weight hangs, forearm pump, and dynamic whippers.
/// </summary>
public sealed class ClimbingSession
{
    public const float MaxWorkReachMeters = 1.48f;

    private readonly List<PlacedProtectionDetail> _placedGear = [];
    private int _nextPlacementSequence = 1;
    private LimbAttachment? _stashedHandBeforeStance;

    public CliffFace Cliff { get; }
    public ClimberRigState Rig { get; } = new();
    public ForearmPumpModel Pump { get; }
    public RackInventory Rack { get; }
    public float ClimberMassKg { get; }

    public SessionMode Mode { get; private set; } = SessionMode.Grounded;
    public LimbSlot? FreeWorkHand { get; private set; }
    public ActiveToolKind SelectedTool { get; private set; } = ActiveToolKind.None;
    public GearSpecBase? SelectedGear { get; private set; }
    public PlacedProtectionDetail? ActiveWeightedPiece { get; private set; }
    public FallSimulationResult? LastFallResult { get; private set; }
    public float CurrentPumpRatePerSecond { get; private set; }
    public string StatusMessage { get; private set; } = "Standing on the ground pad. Click starting holds to begin climbing.";

    public ClimbingSession(
        CliffFace cliff,
        RackInventory? rack = null,
        float climberMassKg = 78f,
        float initialPump = 0f)
    {
        ArgumentNullException.ThrowIfNull(cliff);
        Cliff = cliff;
        Rack = rack ?? RackInventory.CreateDefaultGroundUpRack();
        ClimberMassKg = Math.Max(40f, climberMassKg);
        Pump = new ForearmPumpModel(initialPump);
        Rig.SetGroundedAtBase();
    }

    public IReadOnlyList<PlacedProtectionDetail> PlacedGear => _placedGear;

    public IReadOnlyList<PlacedProtection> ActiveProtectionChain =>
        _placedGear.Select(g => g.Protection).ToArray();

    /// <summary>
    /// Attaches the climber onto the nearest reachable low starting holds/stances and enters <see cref="SessionMode.FreeClimbing"/>.
    /// </summary>
    public bool TryStepOntoWallFromGround(float preferredCenterX = -0.5f)
    {
        if (Mode is not SessionMode.Grounded and not SessionMode.ToppedOut)
        {
            return false;
        }

        Rig.SetGroundedAtBase(preferredCenterX);

        List<RockFeatureBase> lowFeet = Cliff.Features
            .Where(f => !f.IsTrundled && f is StanceFeature && f.PositionMeters.Y <= 1.2f)
            .OrderBy(f => MathF.Abs(f.PositionMeters.X - preferredCenterX))
            .ToList();

        List<RockFeatureBase> lowHands = Cliff.Features
            .Where(f => !f.IsTrundled && f is not StanceFeature && f.GetLocalAttachmentPointMeters(0f).Y is >= 1.1f and <= 2.1f)
            .OrderBy(f => MathF.Abs(f.PositionMeters.X - preferredCenterX))
            .ToList();

        if (lowFeet.Count == 0 || lowHands.Count == 0)
        {
            StatusMessage = "No reachable starting holds near the base.";
            return false;
        }

        RockFeatureBase lf = lowFeet[0];
        RockFeatureBase rf = lowFeet.Count > 1 ? lowFeet[1] : lowFeet[0];
        if (lf.PositionMeters.X > rf.PositionMeters.X)
        {
            (lf, rf) = (rf, lf);
        }

        RockFeatureBase lh = lowHands[0];
        RockFeatureBase rh = lowHands.Count > 1 ? lowHands[1] : lowHands[0];
        if (lh.PositionMeters.X > rh.PositionMeters.X)
        {
            (lh, rh) = (rh, lh);
        }

        Rig.SetLimbAttachment(Cliff, CreateAttachment(LimbSlot.LeftFoot, lf, 0f));
        if (!ReferenceEquals(rf, lf))
        {
            Rig.SetLimbAttachment(Cliff, CreateAttachment(LimbSlot.RightFoot, rf, 0f));
        }

        Rig.SetLimbAttachment(Cliff, CreateAttachment(LimbSlot.LeftHand, lh, 0f));
        if (!ReferenceEquals(rh, lh) || rh is CrackFeature)
        {
            float rhNorm = ReferenceEquals(rh, lh) && rh is CrackFeature ? 0.08f : 0f;
            Rig.SetLimbAttachment(Cliff, CreateAttachment(LimbSlot.RightHand, rh, rhNorm));
        }

        Mode = SessionMode.FreeClimbing;
        FreeWorkHand = null;
        SelectedTool = ActiveToolKind.None;
        ActiveWeightedPiece = null;
        LastFallResult = null;
        RefreshPumpRate();
        StatusMessage = "Stepped onto the rock in Free Climbing mode.";
        return true;
    }

    public bool TryMoveLimbToFeature(
        string featureId,
        float normalizedPosition = 0.5f,
        LimbSlot? manualLimbOverride = null)
    {
        if (Mode is SessionMode.WhipperFall)
        {
            StatusMessage = "Cannot grab holds mid-whipper!";
            return false;
        }

        if (Mode is SessionMode.StanceInspect or SessionMode.CleaningAndGear)
        {
            StatusMessage = "Unlock your 3-point stance (F) before making climbing moves.";
            return false;
        }

        RockFeatureBase? feature = Cliff.FindFeature(featureId);
        if (feature is null)
        {
            StatusMessage = $"Unknown feature '{featureId}'.";
            return false;
        }

        normalizedPosition = Math.Clamp(normalizedPosition, 0f, 1f);
        Vector3 targetWorld = Cliff.GetFeatureWorldPositionMeters(feature, normalizedPosition);
        LimbSlot limb = Rig.SelectBestLimbForTarget(feature, targetWorld, manualLimbOverride);

        if (!Rig.CanReachAndUseFeature(Cliff, limb, feature, normalizedPosition, out string reason))
        {
            StatusMessage = reason;
            return false;
        }

        // Pulling onto or weighting an un-trundled hollow choss block snaps it off the cliff and triggers a whipper!
        if (feature.IsHollowChoss && !feature.IsTrundled)
        {
            float fallHeight = MathF.Max(Rig.CenterOfMassMeters.Y, targetWorld.Y - 0.35f);
            feature.TryTrundleWithPrybar();
            TriggerWhipper(fallHeight, $"CHOSS BREAKOFF! '{feature.Name}' ripped off the wall under your weight!");
            return false;
        }

        Rig.SetLimbAttachment(Cliff, new LimbAttachment(limb, feature.Id, normalizedPosition, targetWorld));
        ActiveWeightedPiece = null;
        FreeWorkHand = null;
        SelectedTool = ActiveToolKind.None;

        // Check summit top-out condition!
        if (string.Equals(feature.Id, "stance-summit-topout", StringComparison.Ordinal) ||
            (targetWorld.Y >= Cliff.HeightMeters - 0.25f && ClimberRigState.IsHand(limb)))
        {
            Mode = SessionMode.ToppedOut;
            CurrentPumpRatePerSecond = -6.0f;
            StatusMessage = $"TOPPED OUT '{Cliff.Name}' cleanly!";
            return true;
        }

        Mode = SessionMode.FreeClimbing;
        RefreshPumpRate();
        StatusMessage = $"Moved {limb} to '{feature.Name}'.";
        return true;
    }

    public bool TryLockThreePointStance(LimbSlot? handToFree = null)
    {
        if (Mode == SessionMode.CleaningAndGear)
        {
            Mode = SessionMode.StanceInspect;
            SelectedTool = ActiveToolKind.None;
            RefreshPumpRate();
            StatusMessage = "Returned to 3-point stance inspection.";
            return true;
        }

        if (Mode != SessionMode.FreeClimbing)
        {
            StatusMessage = "Must be free climbing to lock a 3-point stance.";
            return false;
        }

        if (Rig.AttachedHandCount < 2 || Rig.AttachedFootCount < 1)
        {
            StatusMessage = "Need both hands and at least one solid foot on the rock to lock a 3-point stance.";
            return false;
        }

        LimbSlot releaseHand = handToFree is LimbSlot.LeftHand or LimbSlot.RightHand
            ? handToFree.Value
            : LimbSlot.RightHand;

        _stashedHandBeforeStance = Rig.GetAttachment(releaseHand);
        Rig.DetachLimb(Cliff, releaseHand);
        FreeWorkHand = releaseHand;
        Mode = SessionMode.StanceInspect;
        SelectedTool = ActiveToolKind.None;
        RefreshPumpRate();
        StatusMessage = $"Locked 3-point stance ({releaseHand} free to clean rock or place gear).";
        return true;
    }

    public bool ReturnToFreeClimbing()
    {
        if (Mode is SessionMode.StanceInspect or SessionMode.CleaningAndGear)
        {
            if (_stashedHandBeforeStance is LimbAttachment stashed)
            {
                RockFeatureBase? prevFeature = Cliff.FindFeature(stashed.FeatureId);
                if (prevFeature is not null && !prevFeature.IsTrundled)
                {
                    Rig.SetLimbAttachment(Cliff, stashed);
                }
            }

            _stashedHandBeforeStance = null;
            FreeWorkHand = null;
            SelectedTool = ActiveToolKind.None;
            Mode = SessionMode.FreeClimbing;
            RefreshPumpRate();
            StatusMessage = "Resumed two-handed Free Climbing.";
            return true;
        }

        if (Mode == SessionMode.HangingOnGear)
        {
            PlacedProtectionDetail? anchor = ActiveWeightedPiece;
            Vector3 refPos = anchor?.WorldPositionMeters ?? Rig.CenterOfMassMeters;

            List<(RockFeatureBase Feature, float Norm, Vector3 Pos)> candidates = [];
            foreach (RockFeatureBase f in Cliff.Features.Where(f => !f.IsTrundled && !f.IsHollowChoss))
            {
                float norm = f is CrackFeature c ? c.GetNormalizedPositionForWorldHeight(refPos.Y) : 0.5f;
                if (f is CrackFeature thin && thin.SampleAtNormalized(norm).WidthMillimeters < 11.0f)
                {
                    continue;
                }

                Vector3 world = Cliff.GetFeatureWorldPositionMeters(f, norm);
                if (Vector3.Distance(refPos, world) <= 1.55f)
                {
                    candidates.Add((f, norm, world));
                }
            }

            var handHold = candidates
                .Where(c => c.Feature is not StanceFeature && c.Pos.Y >= refPos.Y - 0.55f)
                .OrderBy(c => Vector3.Distance(refPos, c.Pos))
                .FirstOrDefault();

            var footHold = candidates
                .Where(c => c.Pos.Y <= (handHold.Feature is not null ? handHold.Pos.Y - 0.35f : refPos.Y - 0.25f))
                .OrderBy(c => Vector3.Distance(refPos + new Vector3(0f, -0.75f, 0f), c.Pos))
                .FirstOrDefault();

            if (handHold.Feature is null || footHold.Feature is null)
            {
                StatusMessage = "Click a reachable handhold directly to pull off the gear.";
                return false;
            }

            Rig.SetLimbAttachment(Cliff, new LimbAttachment(LimbSlot.LeftHand, handHold.Feature.Id, handHold.Norm, handHold.Pos));
            float rhNorm = handHold.Feature is CrackFeature ? Math.Clamp(handHold.Norm + 0.06f, 0f, 1f) : handHold.Norm;
            Rig.SetLimbAttachment(Cliff, new LimbAttachment(LimbSlot.RightHand, handHold.Feature.Id, rhNorm, Cliff.GetFeatureWorldPositionMeters(handHold.Feature, rhNorm)));
            Rig.SetLimbAttachment(Cliff, new LimbAttachment(LimbSlot.LeftFoot, footHold.Feature.Id, footHold.Norm, footHold.Pos));

            ActiveWeightedPiece = null;
            Mode = SessionMode.FreeClimbing;
            RefreshPumpRate();
            StatusMessage = $"Pulled off gear back onto '{handHold.Feature.Name}'.";
            return true;
        }

        return false;
    }

    public bool SelectWorkTool(ActiveToolKind tool, GearSpecBase? gear = null)
    {
        if (Mode is not SessionMode.StanceInspect and not SessionMode.CleaningAndGear and not SessionMode.HangingOnGear)
        {
            StatusMessage = "Lock a 3-point stance (F) or hang on gear first to access your harness tools.";
            return false;
        }

        if (tool == ActiveToolKind.PlaceGear && gear is null)
        {
            StatusMessage = "Select a piece of protection from your rack.";
            return false;
        }

        SelectedTool = tool;
        SelectedGear = gear;
        if (Mode == SessionMode.StanceInspect && tool != ActiveToolKind.None)
        {
            Mode = SessionMode.CleaningAndGear;
        }

        RefreshPumpRate();
        StatusMessage = tool == ActiveToolKind.PlaceGear && gear is not null
            ? $"Readied {gear.DisplayName} ({Rack.GetQuantity(gear.Code)} remaining on rack)."
            : $"Selected tool: {tool}.";
        return true;
    }

    public bool TrySoundFeature(string featureId, out RockSoundingResult sounding)
    {
        sounding = default;
        if (!EnsureWorkAccessToFeature(featureId, 0.5f, out RockFeatureBase? feature, out string error))
        {
            StatusMessage = error;
            return false;
        }

        sounding = feature!.SoundRock();
        StatusMessage = $"Sounded '{feature.Name}': {sounding.Resonance} ({sounding.AcousticPitchHz:F0} Hz)"
            + (sounding.AdvisesTrundle ? " — HOLLOW CHOSS! Prybar trundle strongly advised!" : " — solid stone.");
        return true;
    }

    public bool TryBrushFeature(string featureId, float brushEffort = 0.75f)
    {
        if (!EnsureWorkAccessToFeature(featureId, 0.5f, out RockFeatureBase? feature, out string error))
        {
            StatusMessage = error;
            return false;
        }

        feature!.Surface.BrushClean(brushEffort);
        RefreshPumpRate();
        StatusMessage = feature.Surface.IsClean
            ? $"Brushed '{feature.Name}' clean! Rock friction and hook bite fully restored."
            : $"Brushing '{feature.Name}' (cleanliness {feature.Surface.CleanlinessMultiplier:P0})...";
        return true;
    }

    public bool TryTrundleFeature(string featureId)
    {
        if (!EnsureWorkAccessToFeature(featureId, 0.5f, out RockFeatureBase? feature, out string error))
        {
            StatusMessage = error;
            return false;
        }

        if (Rig.IsAttachedToFeature(featureId))
        {
            StatusMessage = $"Move your limbs off '{feature!.Name}' before prying it off the cliff!";
            return false;
        }

        if (!feature!.TryTrundleWithPrybar())
        {
            StatusMessage = feature.IsTrundled
                ? $"'{feature.Name}' was already trundled."
                : $"'{feature.Name}' is solid bedrock and will not budge under the prybar.";
            return false;
        }

        if (_stashedHandBeforeStance?.FeatureId == featureId)
        {
            _stashedHandBeforeStance = null;
        }

        StatusMessage = $"TRUNDLED '{feature.Name}'! Hollow block crashed safely to the base.";
        return true;
    }

    public PlacementEvaluation PreviewPlacement(GearSpecBase gear, string featureId, float normalizedPosition = 0.5f)
    {
        ArgumentNullException.ThrowIfNull(gear);
        RockFeatureBase? feature = Cliff.FindFeature(featureId);
        if (feature is null)
        {
            return new PlacementEvaluation(gear, featureId, 0f, false, 0f, PlacementQuality.Unusable, "Unknown rock feature.");
        }

        return ProtectionEvaluator.EvaluatePlacement(gear, feature, normalizedPosition, Cliff.Geology);
    }

    public bool TryPlaceProtection(
        GearSpecBase gear,
        string featureId,
        float normalizedPosition,
        out PlacedProtectionDetail? placedDetail,
        out PlacementEvaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(gear);
        placedDetail = null;
        evaluation = PreviewPlacement(gear, featureId, normalizedPosition);

        if (!EnsureWorkAccessToFeature(featureId, normalizedPosition, out RockFeatureBase? feature, out string error))
        {
            StatusMessage = error;
            return false;
        }

        if (Rack.GetQuantity(gear.Code) <= 0)
        {
            StatusMessage = $"No {gear.DisplayName} left on your rack.";
            return false;
        }

        if (!evaluation.FitsGeometry)
        {
            StatusMessage = evaluation.DiagnosticSummary;
            return false;
        }

        Rack.TryConsume(gear);

        Vector3 worldPos = Cliff.GetFeatureWorldPositionMeters(feature!, normalizedPosition);
        string placementId = $"pro-{_nextPlacementSequence++}-{gear.Code}";
        var protection = new PlacedProtection(
            placementId,
            gear,
            feature!.Id,
            HeightMeters: worldPos.Y,
            EffectiveHoldingForceKilonewtons: evaluation.EffectiveHoldingForceKilonewtons,
            Quality: evaluation.Quality);

        var detail = new PlacedProtectionDetail(protection, normalizedPosition, worldPos);
        _placedGear.Add(detail);
        placedDetail = detail;

        StatusMessage = $"Placed {gear.DisplayName} in '{feature.Name}'.";
        return true;
    }

    /// <summary>
    /// Transfers climber body weight onto a placed piece of gear or skyhook (defaults to highest nearby piece).
    /// Transitions to <see cref="SessionMode.HangingOnGear"/> if it holds body weight, or blows into a whipper if it fails!
    /// </summary>
    public bool TryWeightPlacedGear(string? placementId = null)
    {
        if (Mode is not SessionMode.StanceInspect and not SessionMode.CleaningAndGear and not SessionMode.FreeClimbing)
        {
            StatusMessage = "Can only weight gear while on the wall.";
            return false;
        }

        PlacedProtectionDetail? target = placementId is not null
            ? _placedGear.FirstOrDefault(p => string.Equals(p.Protection.PlacementId, placementId, StringComparison.Ordinal))
            : _placedGear
                .Where(p => Vector3.Distance(Rig.CenterOfMassMeters, p.WorldPositionMeters) <= MaxWorkReachMeters + 0.25f)
                .OrderByDescending(p => p.Protection.HeightMeters)
                .FirstOrDefault();

        if (target is null || target.Value.Protection.PlacementId is null)
        {
            StatusMessage = "No placed gear within reach to weight!";
            return false;
        }

        RockFeatureBase? hostFeature = Cliff.FindFeature(target.Value.Protection.FeatureId);
        float bodyLoadKn = (ClimberMassKg * 9.81f / 1000f) * 1.35f;
        bool chossBroke = hostFeature is not null && hostFeature.IsHollowChoss && !hostFeature.IsTrundled;
        bool pieceHoldsBody = !chossBroke && target.Value.Protection.EffectiveHoldingForceKilonewtons >= bodyLoadKn;

        if (!pieceHoldsBody)
        {
            float fallStartHeight = MathF.Max(Rig.CenterOfMassMeters.Y, target.Value.WorldPositionMeters.Y - 0.20f);
            _placedGear.RemoveAll(p => string.Equals(p.Protection.PlacementId, target.Value.Protection.PlacementId, StringComparison.Ordinal));
            if (chossBroke)
            {
                hostFeature!.TryTrundleWithPrybar();
            }

            TriggerWhipper(
                fallStartHeight,
                $"GEAR BLOWOUT! {target.Value.Protection.Gear.DisplayName} popped under body weight!");
            return false;
        }

        _stashedHandBeforeStance = null;
        FreeWorkHand = null;
        SelectedTool = ActiveToolKind.None;
        ActiveWeightedPiece = target;
        Rig.SetHangingAtPosition(Cliff, target.Value.WorldPositionMeters);
        Mode = SessionMode.HangingOnGear;
        RefreshPumpRate();
        StatusMessage = $"Hanging on {target.Value.Protection.Gear.DisplayName} — forearms recovering.";
        return true;
    }

    public void AdvanceTime(float deltaSeconds)
    {
        if (deltaSeconds <= 0f || Mode == SessionMode.WhipperFall)
        {
            return;
        }

        if (Mode is SessionMode.Grounded or SessionMode.ToppedOut)
        {
            CurrentPumpRatePerSecond = -6.0f;
            Pump.AdvanceSeconds(deltaSeconds, ClimberActionState.HangingOnGear, 0f);
            return;
        }

        RefreshPumpRate();
        if (CurrentPumpRatePerSecond >= 0f)
        {
            float rawRate = ComputeBaseModelPumpRate();
            float scale = rawRate > 0.001f ? CurrentPumpRatePerSecond / rawRate : 1.0f;
            bool pumpedOut = Pump.AdvanceSeconds(
                deltaSeconds * scale,
                MapToActionState(Mode),
                Cliff.OverhangDegrees,
                ResolvePrimaryHandHold(),
                ResolvePrimaryStance(),
                geology: Cliff.Geology);

            if (pumpedOut)
            {
                TriggerWhipper(Rig.CenterOfMassMeters.Y, "PUMPED OUT! Forearms failed at 100% pump—WHIPPER!");
            }
        }
        else
        {
            Pump.AdvanceSeconds(
                deltaSeconds,
                MapToActionState(Mode),
                Cliff.OverhangDegrees,
                ResolvePrimaryHandHold(),
                ResolvePrimaryStance(),
                geology: Cliff.Geology);
        }
    }

    /// <summary>
    /// Resolves the resting state after a dynamic whipper finishes playing out.
    /// </summary>
    public void CompleteWhipperAftermath()
    {
        if (Mode != SessionMode.WhipperFall || LastFallResult is null)
        {
            return;
        }

        if (LastFallResult.Outcome == FallOutcome.CaughtByGear && LastFallResult.CatchingPlacementId is not null)
        {
            PlacedProtectionDetail catching = _placedGear.First(
                p => string.Equals(p.Protection.PlacementId, LastFallResult.CatchingPlacementId, StringComparison.Ordinal));

            ActiveWeightedPiece = catching;
            Vector3 hangAnchor = new(catching.WorldPositionMeters.X, LastFallResult.FinalRestingHeightMeters + 0.35f, catching.WorldPositionMeters.Z);
            Rig.SetHangingAtPosition(Cliff, hangAnchor);
            Mode = SessionMode.HangingOnGear;
            RefreshPumpRate();
            StatusMessage = $"Caught by {catching.Protection.Gear.DisplayName} at {LastFallResult.FinalRestingHeightMeters:F1}m! Hanging on rope.";
        }
        else
        {
            ActiveWeightedPiece = null;
            Rig.SetGroundedAtBase();
            Pump.AdvanceSeconds(100f, ClimberActionState.HangingOnGear, 0f);
            Mode = SessionMode.Grounded;
            CurrentPumpRatePerSecond = 0f;
            StatusMessage = "GROUND FALL! Reset on the crash pad at the base of the cliff.";
        }
    }

    public void ResetToGround()
    {
        foreach (PlacedProtectionDetail detail in _placedGear)
        {
            Rack.ReturnGear(detail.Protection.Gear);
        }

        _placedGear.Clear();
        _stashedHandBeforeStance = null;
        FreeWorkHand = null;
        SelectedTool = ActiveToolKind.None;
        SelectedGear = null;
        ActiveWeightedPiece = null;
        LastFallResult = null;
        Rig.SetGroundedAtBase();
        Pump.AdvanceSeconds(100f, ClimberActionState.HangingOnGear, 0f);
        Mode = SessionMode.Grounded;
        CurrentPumpRatePerSecond = 0f;
        StatusMessage = "Lowered to ground and cleaned all gear back onto rack.";
    }

    private void TriggerWhipper(float fallStartHeightMeters, string reasonMessage)
    {
        _stashedHandBeforeStance = null;
        FreeWorkHand = null;
        SelectedTool = ActiveToolKind.None;
        ActiveWeightedPiece = null;

        FallSimulationResult fall = RopeAndFallSimulator.SimulateLeadFall(
            climberHeightMeters: MathF.Max(0.2f, fallStartHeightMeters),
            activeProtectionChain: ActiveProtectionChain,
            climberMassKg: ClimberMassKg);

        if (fall.BlownPieces.Count > 0)
        {
            HashSet<string> blownIds = fall.BlownPieces.Select(b => b.PlacementId).ToHashSet(StringComparer.Ordinal);
            _placedGear.RemoveAll(p => blownIds.Contains(p.Protection.PlacementId));
        }

        LastFallResult = fall;
        Mode = SessionMode.WhipperFall;
        CurrentPumpRatePerSecond = 0f;
        StatusMessage = fall.Outcome == FallOutcome.CaughtByGear
            ? $"{reasonMessage} Caught at {fall.FinalRestingHeightMeters:F1}m ({fall.PeakClimberImpactKilonewtons:F1} kN impact, {fall.BlownPieces.Count} blown)."
            : $"{reasonMessage} GROUND FALL from {fall.StartHeightMeters:F1}m!";
    }

    private bool EnsureWorkAccessToFeature(
        string featureId,
        float normalizedPosition,
        out RockFeatureBase? feature,
        out string error)
    {
        feature = Cliff.FindFeature(featureId);
        if (feature is null)
        {
            error = $"Unknown feature '{featureId}'.";
            return false;
        }

        if (Mode is not SessionMode.StanceInspect and not SessionMode.CleaningAndGear and not SessionMode.HangingOnGear)
        {
            error = "Lock a 3-point stance (F) or hang on gear first!";
            return false;
        }

        Vector3 targetWorld = Cliff.GetFeatureWorldPositionMeters(feature, normalizedPosition);
        float dist = Vector3.Distance(Rig.CenterOfMassMeters, targetWorld);
        if (dist > MaxWorkReachMeters)
        {
            error = $"'{feature.Name}' is out of arm's reach ({dist:F2}m > {MaxWorkReachMeters:F2}m).";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private void RefreshPumpRate()
    {
        float raw = ComputeBaseModelPumpRate();
        if (raw > 0f)
        {
            CurrentPumpRatePerSecond = MathF.Round(raw * Rig.ComputeBalancePumpMultiplier(), 2);
        }
        else
        {
            CurrentPumpRatePerSecond = raw;
        }
    }

    private float ComputeBaseModelPumpRate() =>
        ForearmPumpModel.ComputePumpRatePerSecond(
            MapToActionState(Mode),
            Cliff.OverhangDegrees,
            ResolvePrimaryHandHold(),
            ResolvePrimaryStance(),
            geology: Cliff.Geology);

    private static ClimberActionState MapToActionState(SessionMode mode) => mode switch
    {
        SessionMode.HangingOnGear => ClimberActionState.HangingOnGear,
        SessionMode.StanceInspect or SessionMode.CleaningAndGear => ClimberActionState.StanceInspect,
        _ => ClimberActionState.FreeClimbing
    };

    private EdgeFeature? ResolvePrimaryHandHold()
    {
        foreach (LimbAttachment attachment in Rig.Attachments.Values.Where(a => ClimberRigState.IsHand(a.Limb)))
        {
            if (Cliff.FindFeature(attachment.FeatureId) is EdgeFeature edge && !edge.IsTrundled)
            {
                return edge;
            }
        }

        return null;
    }

    private StanceFeature? ResolvePrimaryStance()
    {
        StanceFeature? best = null;
        foreach (LimbAttachment attachment in Rig.Attachments.Values.Where(a => !ClimberRigState.IsHand(a.Limb)))
        {
            if (Cliff.FindFeature(attachment.FeatureId) is StanceFeature stance && !stance.IsTrundled)
            {
                if (best is null || stance.FootAreaSqCentimeters > best.FootAreaSqCentimeters)
                {
                    best = stance;
                }
            }
        }

        return best;
    }

    private LimbAttachment CreateAttachment(LimbSlot limb, RockFeatureBase feature, float normalizedPos) =>
        new(limb, feature.Id, normalizedPos, Cliff.GetFeatureWorldPositionMeters(feature, normalizedPos));
}
