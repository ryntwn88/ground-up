using System.Numerics;
using GroundUp.Core.Bolting;
using GroundUp.Core.Gear;
using GroundUp.Core.Geology;
using GroundUp.Core.Physics;
using GroundUp.Core.Stamina;

namespace GroundUp.Core.Climbing;

public enum AscentMode
{
    RouteDevelopment,
    FirstFreeAscent
}

public enum SessionMode
{
    Grounded,
    FreeClimbing,
    StanceInspect,
    CleaningAndGear,
    HangingOnGear,
    DrillingBolt,
    WhipperFall,
    ToppedOut
}

public enum ActiveToolKind
{
    None,
    HammerSound,
    WireBrush,
    Prybar,
    PlaceGear,
    BoltDrill
}

public enum PumpDifficultyPreset
{
    Standard,
    Relaxed,
    Low,
    Off,
    Hardcore
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
    private readonly List<string> _climbedFeatureHistory = [];
    private int _nextPlacementSequence = 1;
    private LimbAttachment? _stashedHandBeforeStance;
    private bool _drillingHammerActiveThisStep;

    public CliffFace Cliff { get; }
    public ClimberRigState Rig { get; } = new();
    public ForearmPumpModel Pump { get; }
    public RackInventory Rack { get; }
    public float ClimberMassKg { get; }

    public AscentMode CurrentAscentMode { get; private set; } = AscentMode.RouteDevelopment;
    public bool IsCleanFfaAscent { get; private set; } = true;
    public bool HasCompletedCleanFfa { get; private set; }
    public IDrillTool EquippedDrill { get; private set; } = new HandDrillTool();
    public BoltSpec EquippedBoltSpec { get; private set; } = StandardRack.Expansion38;
    public int BoltsRemainingInBag { get; private set; } = 12;
    public BoltInstallation? ActiveBoltTarget { get; private set; }

    public SessionMode Mode { get; private set; } = SessionMode.Grounded;
    public LimbSlot? FreeWorkHand { get; private set; }
    public ActiveToolKind SelectedTool { get; private set; } = ActiveToolKind.None;
    public GearSpecBase? SelectedGear { get; private set; }
    public PlacedProtectionDetail? ActiveWeightedPiece { get; private set; }
    public FallSimulationResult? LastFallResult { get; private set; }
    public PumpDifficultyPreset PumpPreset { get; private set; } = PumpDifficultyPreset.Standard;
    public float PumpDrainScale { get; private set; } = 1.0f;
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

    public IReadOnlyList<string> ClimbedFeatureIds => _climbedFeatureHistory;

    /// <summary>
    /// Dev-only testing hook to swap between the default <see cref="HandDrillTool"/> and <see cref="CordlessPowerDrillTool"/>.
    /// </summary>
    public void SetDevDrillTool(IDrillTool drillTool)
    {
        ArgumentNullException.ThrowIfNull(drillTool);
        EquippedDrill = drillTool;
        RefreshPumpRate();
        StatusMessage = $"Equipped drill: {drillTool.Name}.";
    }

    public static float GetScaleForPreset(PumpDifficultyPreset preset) => preset switch
    {
        PumpDifficultyPreset.Standard => 1.00f,
        PumpDifficultyPreset.Relaxed => 0.50f,
        PumpDifficultyPreset.Low => 0.25f,
        PumpDifficultyPreset.Off => 0.00f,
        PumpDifficultyPreset.Hardcore => 1.50f,
        _ => 1.00f
    };

    public static string GetDisplayLabelForPreset(PumpDifficultyPreset preset) => preset switch
    {
        PumpDifficultyPreset.Standard => "1.0x Standard",
        PumpDifficultyPreset.Relaxed => "0.5x Relaxed",
        PumpDifficultyPreset.Low => "0.25x Low",
        PumpDifficultyPreset.Off => "OFF (No Pump)",
        PumpDifficultyPreset.Hardcore => "1.5x Hardcore",
        _ => "1.0x Standard"
    };

    public void SetPumpPreset(PumpDifficultyPreset preset)
    {
        PumpPreset = preset;
        PumpDrainScale = GetScaleForPreset(preset);
        if (preset == PumpDifficultyPreset.Off)
        {
            Pump.AdvanceSeconds(100f, ClimberActionState.HangingOnGear, 0f);
        }

        if (Mode is SessionMode.FreeClimbing or SessionMode.StanceInspect or SessionMode.CleaningAndGear or SessionMode.HangingOnGear)
        {
            RefreshPumpRate();
        }

        StatusMessage = $"Pump intensity set to {GetDisplayLabelForPreset(preset)}.";
    }

    public PumpDifficultyPreset CyclePumpPreset()
    {
        PumpDifficultyPreset next = PumpPreset switch
        {
            PumpDifficultyPreset.Standard => PumpDifficultyPreset.Relaxed,
            PumpDifficultyPreset.Relaxed => PumpDifficultyPreset.Low,
            PumpDifficultyPreset.Low => PumpDifficultyPreset.Off,
            PumpDifficultyPreset.Off => PumpDifficultyPreset.Hardcore,
            _ => PumpDifficultyPreset.Standard
        };

        SetPumpPreset(next);
        return next;
    }

    /// <summary>
    /// Non-mutating preview of which limb would move (and whether the move is valid) if the player clicks <paramref name="featureId"/>.
    /// </summary>
    public bool PreviewLimbMove(
        string featureId,
        float normalizedPosition,
        LimbSlot? manualLimbOverride,
        out LimbSlot? predictedLimb,
        out string diagnostic)
    {
        predictedLimb = null;
        RockFeatureBase? feature = Cliff.FindFeature(featureId);
        if (feature is null || feature.IsTrundled)
        {
            diagnostic = "Feature unavailable.";
            return false;
        }

        normalizedPosition = Math.Clamp(normalizedPosition, 0f, 1f);
        if (Mode == SessionMode.Grounded)
        {
            predictedLimb = LimbSlot.LeftHand;
            diagnostic = "Step onto starting holds";
            return true;
        }

        if (Mode == SessionMode.HangingOnGear)
        {
            Vector3 targetWorld = Cliff.GetFeatureWorldPositionMeters(feature, normalizedPosition);
            float dist = Vector3.Distance(Rig.CenterOfMassMeters, targetWorld);
            if (dist > 1.78f)
            {
                diagnostic = $"Out of reach from gear hang ({dist:F2}m).";
                return false;
            }

            if (feature is CrackFeature thin && thin.SampleAtNormalized(normalizedPosition).WidthMillimeters < 6f)
            {
                diagnostic = "Incipient seam is too thin to pull onto.";
                return false;
            }

            predictedLimb = manualLimbOverride ?? (feature is StanceFeature ? LimbSlot.RightFoot : LimbSlot.LeftHand);
            diagnostic = $"Pull off gear -> {feature.Name}";
            return true;
        }

        if (Rig.TrySelectBestReachableLimb(Cliff, feature, normalizedPosition, manualLimbOverride, out LimbSlot chosen, out string reason))
        {
            predictedLimb = chosen;
            diagnostic = $"Move {chosen} -> {feature.Name}";
            return true;
        }

        diagnostic = reason;
        return false;
    }

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

        if (Mode is SessionMode.StanceInspect or SessionMode.CleaningAndGear or SessionMode.DrillingBolt)
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

        // Pulling off HangingOnGear via a direct hold click must re-establish BOTH hands and at least one foothold
        // so the climber never enters a zero-foot floating state!
        if (Mode == SessionMode.HangingOnGear)
        {
            return TryPullOffGearOntoFeature(feature, normalizedPosition, targetWorld, manualLimbOverride);
        }

        if (!Rig.TrySelectBestReachableLimb(Cliff, feature, normalizedPosition, manualLimbOverride, out LimbSlot limb, out string reason))
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
        RecordClimbedFeature(feature.Id);
        ActiveWeightedPiece = null;
        ActiveBoltTarget = null;
        FreeWorkHand = null;
        SelectedTool = ActiveToolKind.None;

        // Check summit top-out condition!
        if (string.Equals(feature.Id, "stance-summit-topout", StringComparison.Ordinal) ||
            (targetWorld.Y >= Cliff.HeightMeters - 0.25f && ClimberRigState.IsHand(limb)))
        {
            Mode = SessionMode.ToppedOut;
            CurrentPumpRatePerSecond = -6.0f;
            if (CurrentAscentMode == AscentMode.FirstFreeAscent && IsCleanFfaAscent)
            {
                HasCompletedCleanFfa = true;
                StatusMessage = $"FIRST FREE ASCENT COMPLETE! Topped out '{Cliff.Name}' with zero falls or hangs!";
            }
            else
            {
                StatusMessage = $"TOPPED OUT '{Cliff.Name}'! (Switch to FFA Mode [M] to claim the First Free Ascent)";
            }

            return true;
        }

        Mode = SessionMode.FreeClimbing;
        RefreshPumpRate();
        StatusMessage = $"Moved {limb} to '{feature.Name}'.";
        return true;
    }

    public bool TryLockThreePointStance(LimbSlot? handToFree = null)
    {
        if (Mode is SessionMode.CleaningAndGear or SessionMode.DrillingBolt)
        {
            Mode = ActiveWeightedPiece is not null ? SessionMode.HangingOnGear : SessionMode.StanceInspect;
            SelectedTool = ActiveToolKind.None;
            ActiveBoltTarget = null;
            RefreshPumpRate();
            StatusMessage = Mode == SessionMode.HangingOnGear
                ? "Stopped bolting work; still hanging on gear."
                : "Returned to 3-point stance inspection.";
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
        if (Mode == SessionMode.DrillingBolt && ActiveWeightedPiece is not null)
        {
            ActiveBoltTarget = null;
            SelectedTool = ActiveToolKind.None;
            Mode = SessionMode.HangingOnGear;
        }

        if (Mode is SessionMode.StanceInspect or SessionMode.CleaningAndGear or SessionMode.DrillingBolt)
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
            ActiveBoltTarget = null;
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
        if (Mode is not SessionMode.StanceInspect and not SessionMode.CleaningAndGear and not SessionMode.HangingOnGear and not SessionMode.DrillingBolt)
        {
            StatusMessage = "Lock a 3-point stance (F) or hang on gear first to access your harness tools.";
            return false;
        }

        if (CurrentAscentMode == AscentMode.FirstFreeAscent)
        {
            bool forbiddenInFfa = tool is ActiveToolKind.BoltDrill or ActiveToolKind.Prybar or ActiveToolKind.HammerSound
                || (tool == ActiveToolKind.PlaceGear && gear is not null && gear.Kind is GearKind.Skyhook or GearKind.Piton);
            if (forbiddenInFfa)
            {
                StatusMessage = "Bolting, prybars, and aid hooks/pitons are not permitted during a pure Free Ascent (FFA)!";
                return false;
            }
        }

        if (tool == ActiveToolKind.PlaceGear && gear is null)
        {
            StatusMessage = "Select a piece of protection from your rack.";
            return false;
        }

        SelectedTool = tool;
        SelectedGear = gear;
        if (tool != ActiveToolKind.BoltDrill)
        {
            ActiveBoltTarget = null;
            if (Mode == SessionMode.DrillingBolt)
            {
                Mode = ActiveWeightedPiece is not null ? SessionMode.HangingOnGear : SessionMode.CleaningAndGear;
            }
        }

        if (Mode == SessionMode.StanceInspect && tool != ActiveToolKind.None)
        {
            Mode = SessionMode.CleaningAndGear;
        }

        RefreshPumpRate();
        StatusMessage = tool switch
        {
            ActiveToolKind.PlaceGear when gear is not null => $"Readied {gear.DisplayName} ({Rack.GetQuantity(gear.Code)} remaining on rack).",
            ActiveToolKind.BoltDrill => $"Readied {EquippedDrill.Name} ({BoltsRemainingInBag} bolts in bag). Click rock face to position drill.",
            _ => $"Selected tool: {tool}."
        };
        return true;
    }

    /// <summary>
    /// Control 1 of Bolting: Positions the drill bit on a reachable spot on the rock face (or resumes an existing hole/bolt).
    /// </summary>
    public bool TryPositionDrillAtWallPoint(Vector3 wallLocalPositionMeters, out BoltInstallation? boltTarget)
    {
        boltTarget = null;
        if (CurrentAscentMode != AscentMode.RouteDevelopment)
        {
            StatusMessage = "Cannot drill new bolts during an FFA redpoint attempt!";
            return false;
        }

        if (Mode is not SessionMode.StanceInspect and not SessionMode.CleaningAndGear and not SessionMode.HangingOnGear and not SessionMode.DrillingBolt)
        {
            StatusMessage = "Lock a 3-point stance [F] or hang on gear [G] before positioning the drill!";
            return false;
        }

        Vector3 candidateWorld = Cliff.WallToWorldPositionMeters(new Vector3(wallLocalPositionMeters.X, wallLocalPositionMeters.Y, 0f));
        float dist = Vector3.Distance(Rig.CenterOfMassMeters, candidateWorld);
        if (dist > MaxWorkReachMeters)
        {
            StatusMessage = $"Target bolt spot is out of arm's reach ({dist:F2}m > {MaxWorkReachMeters:F2}m).";
            return false;
        }

        BoltInstallation target = Cliff.StartOrFindBoltHole(EquippedBoltSpec, wallLocalPositionMeters);
        ActiveBoltTarget = target;
        SelectedTool = ActiveToolKind.BoltDrill;
        Mode = SessionMode.DrillingBolt;
        boltTarget = target;
        RefreshPumpRate();

        StatusMessage = target.Stage switch
        {
            BoltInstallStage.DrillingHole when target.CurrentDepthMillimeters <= 0.1f =>
                $"Positioned {EquippedDrill.Name} at {target.WorldPositionMeters.Y:F1}m. Hold [Space] to strike hammer (auto-rotates bit).",
            BoltInstallStage.DrillingHole =>
                $"Resumed hole {target.Id} ({target.CurrentDepthMillimeters:F0}/{target.TargetDepthMillimeters:F0} mm, dust {target.DustFillRatio:P0}). Hold [Space] to hammer, [B] to blow dust.",
            BoltInstallStage.HoleDepthReached =>
                $"Selected full-depth hole {target.Id}. Press [B] to blow dust clear, then [T] to insert bolt & hanger.",
            BoltInstallStage.BoltInsertedUntorqued =>
                $"Selected untorqued {target.Id} ({target.NutTorqueRatio:P0} torque). Press/hold [T] to tighten nut with wrench!",
            BoltInstallStage.NutTightened =>
                $"Selected torqued {target.Id}. Press [V] to hang & clip quickdraw!",
            _ => $"Selected completed {target.Id}."
        };
        return true;
    }

    /// <summary>
    /// Control 2 of Bolting: Operates the hammer (or power trigger) on the positioned drill bit, advancing hole depth,
    /// building rock dust, draining forearm pump, and testing marginal gear-hang stability under hammer vibration.
    /// </summary>
    public bool TryOperateDrillHammer(float deltaSeconds)
    {
        if (deltaSeconds <= 0f)
        {
            return false;
        }

        if (ActiveBoltTarget is null || Mode != SessionMode.DrillingBolt)
        {
            StatusMessage = "Position the drill on the rock face first (Left-Click with Drill [8])!";
            return false;
        }

        float dist = Vector3.Distance(Rig.CenterOfMassMeters, ActiveBoltTarget.WorldPositionMeters);
        if (dist > MaxWorkReachMeters + 0.08f)
        {
            StatusMessage = "Drifted out of reach of the positioned drill hole.";
            return false;
        }

        if (ActiveBoltTarget.Stage != BoltInstallStage.DrillingHole)
        {
            StatusMessage = ActiveBoltTarget.Stage == BoltInstallStage.HoleDepthReached
                ? $"Hole reached {ActiveBoltTarget.TargetDepthMillimeters:F0}mm! Blow out dust [B], then insert & tighten bolt [T]."
                : "Hole is already drilled—use [T] to tighten nut or [V] to clip quickdraw.";
            return false;
        }

        // Hammering while hanging on ultra-marginal gear transmits ~1.48x body-weight impact impulses!
        if (ActiveWeightedPiece is PlacedProtectionDetail hangPiece && !EquippedDrill.IsPowerTool)
        {
            float hammerImpulseLoadKn = (ClimberMassKg * 9.81f / 1000f) * 1.48f;
            if (hangPiece.Protection.EffectiveHoldingForceKilonewtons < hammerImpulseLoadKn)
            {
                float fallHeight = MathF.Max(Rig.CenterOfMassMeters.Y, hangPiece.WorldPositionMeters.Y - 0.20f);
                _placedGear.RemoveAll(p => string.Equals(p.Protection.PlacementId, hangPiece.Protection.PlacementId, StringComparison.Ordinal));
                TriggerWhipper(fallHeight, $"HAMMER VIBRATION BLOWOUT! Marginal {hangPiece.Protection.Gear.DisplayName} skated off mid-drill!");
                return false;
            }
        }

        _drillingHammerActiveThisStep = true;
        bool reachedFullDepth = ActiveBoltTarget.AdvanceDrilling(deltaSeconds, EquippedDrill, Cliff.Geology);

        // Apply active drilling forearm pump for this hammer step unless Pump is set to Off.
        if (PumpPreset != PumpDifficultyPreset.Off && PumpDrainScale > 0f)
        {
            bool hanging = ActiveWeightedPiece is not null;
            float rawDrillRate = ForearmPumpModel.ComputePumpRatePerSecond(
                ClimberActionState.DrillingBolt,
                Cliff.OverhangDegrees,
                ResolvePrimaryHandHold(),
                ResolvePrimaryStance(),
                isDrillingWhileHangingOnGear: hanging,
                drillTool: EquippedDrill,
                geology: Cliff.Geology);

            float scaledDelta = rawDrillRate > 0f
                ? deltaSeconds * (hanging ? 1.0f : Rig.ComputeBalancePumpMultiplier()) * PumpDrainScale
                : deltaSeconds;

            CurrentPumpRatePerSecond = rawDrillRate > 0f
                ? MathF.Round(rawDrillRate * (hanging ? 1.0f : Rig.ComputeBalancePumpMultiplier()) * PumpDrainScale, 2)
                : rawDrillRate;

            bool pumpedOut = Pump.AdvanceSeconds(
                scaledDelta,
                ClimberActionState.DrillingBolt,
                Cliff.OverhangDegrees,
                ResolvePrimaryHandHold(),
                ResolvePrimaryStance(),
                isDrillingWhileHangingOnGear: hanging,
                drillTool: EquippedDrill,
                geology: Cliff.Geology);

            if (pumpedOut)
            {
                TriggerWhipper(Rig.CenterOfMassMeters.Y, "PUMPED OUT MID-DRILL! Grip failed while hand-drilling—WHIPPER!");
                return false;
            }
        }

        StatusMessage = reachedFullDepth
            ? $"Hole {ActiveBoltTarget.Id} reached full depth ({ActiveBoltTarget.TargetDepthMillimeters:F0} mm)! Press [B] to blow dust, then [T] to insert bolt."
            : $"Drilling {ActiveBoltTarget.Id}: {ActiveBoltTarget.CurrentDepthMillimeters:F1}/{ActiveBoltTarget.TargetDepthMillimeters:F0} mm  (Dust: {ActiveBoltTarget.DustFillRatio:P0})";
        return true;
    }

    public bool TryBlowOutActiveHole()
    {
        if (ActiveBoltTarget is null)
        {
            StatusMessage = "Select or position a drill hole first!";
            return false;
        }

        bool ok = ActiveBoltTarget.TryBlowOutDust(out string msg);
        StatusMessage = msg;
        return ok;
    }

    /// <summary>
    /// Inserts the wedge bolt & hanger once hole depth is reached, then cranks the hex nut to 100% torque.
    /// </summary>
    public bool TryInsertOrTightenActiveBolt(float wrenchTorqueStep = 0.36f)
    {
        if (ActiveBoltTarget is null)
        {
            StatusMessage = "Select a drilled hole or bolt first!";
            return false;
        }

        if (ActiveBoltTarget.Stage == BoltInstallStage.HoleDepthReached)
        {
            if (BoltsRemainingInBag <= 0)
            {
                StatusMessage = "No bolts left in your bolting bag!";
                return false;
            }

            if (ActiveBoltTarget.TryInsertBoltAndHanger(out string insertMsg))
            {
                BoltsRemainingInBag--;
                StatusMessage = insertMsg;
                return true;
            }

            StatusMessage = insertMsg;
            return false;
        }

        bool tightened = ActiveBoltTarget.TryTightenNut(wrenchTorqueStep, Cliff.Geology, out string torqueMsg);
        StatusMessage = torqueMsg;
        return tightened;
    }

    /// <summary>
    /// Hangs a quickdraw on a torqued bolt (in Development mode) or clips the lead rope into an existing
    /// pre-hung bolt quickdraw (in either Development or First Free Ascent mode).
    /// </summary>
    public bool TryClipBoltQuickdraw(string? boltId = null)
    {
        BoltInstallation? bolt = boltId is not null
            ? Cliff.FindBoltInstallation(boltId)
            : ActiveBoltTarget ?? Cliff.BoltInstallations
                .Where(b => Vector3.Distance(Rig.CenterOfMassMeters, b.WorldPositionMeters) <= MaxWorkReachMeters)
                .OrderBy(b => Vector3.Distance(Rig.CenterOfMassMeters, b.WorldPositionMeters))
                .FirstOrDefault();

        if (bolt is null)
        {
            StatusMessage = "No bolt within arm's reach to clip!";
            return false;
        }

        float dist = Vector3.Distance(Rig.CenterOfMassMeters, bolt.WorldPositionMeters);
        if (dist > MaxWorkReachMeters)
        {
            StatusMessage = $"Bolt {bolt.Id} is out of clipping reach ({dist:F2}m > {MaxWorkReachMeters:F2}m).";
            return false;
        }

        if (_placedGear.Any(p => string.Equals(p.Protection.FeatureId, bolt.Id, StringComparison.Ordinal)))
        {
            StatusMessage = $"Rope is already clipped into {bolt.Id}!";
            return false;
        }

        if (!bolt.TryHangQuickdraw(out string hangMsg))
        {
            StatusMessage = hangMsg;
            return false;
        }

        PlacementEvaluation eval = bolt.InstalledEvaluation!;
        var protection = new PlacedProtection(
            PlacementId: $"clip-{bolt.Id}",
            Gear: bolt.Spec,
            FeatureId: bolt.Id,
            HeightMeters: bolt.WorldPositionMeters.Y,
            EffectiveHoldingForceKilonewtons: eval.EffectiveHoldingForceKilonewtons,
            Quality: eval.Quality);

        _placedGear.Add(new PlacedProtectionDetail(protection, 0.5f, bolt.WorldPositionMeters));

        // In FFA FreeClimbing mode, clipping a quickdraw takes a quick ~0.45s one-handed lock-off effort.
        if (CurrentAscentMode == AscentMode.FirstFreeAscent && Mode == SessionMode.FreeClimbing && PumpPreset != PumpDifficultyPreset.Off)
        {
            AdvanceTime(0.45f);
        }

        if (Mode == SessionMode.DrillingBolt)
        {
            ActiveBoltTarget = null;
            SelectedTool = ActiveToolKind.None;
            Mode = ActiveWeightedPiece is not null ? SessionMode.HangingOnGear : SessionMode.StanceInspect;
            RefreshPumpRate();
        }

        StatusMessage = hangMsg;
        return true;
    }

    /// <summary>
    /// Module 3: Strips temporary aid/development gear, equips quickdraws on all completed permanent bolts,
    /// and starts a clean Ground-Up First Free Ascent (FFA) redpoint attempt from 0m!
    /// </summary>
    public void StartFirstFreeAscentAttempt()
    {
        foreach (PlacedProtectionDetail detail in _placedGear)
        {
            if (detail.Protection.Gear.Kind != GearKind.Bolt)
            {
                Rack.ReturnGear(detail.Protection.Gear);
            }
        }

        _placedGear.Clear();
        _climbedFeatureHistory.Clear();

        // Pre-hang quickdraws on any fully torqued bolts on the cliff so they are ready to clip on the FFA burn.
        foreach (BoltInstallation bolt in Cliff.BoltInstallations.Where(b => b.Stage == BoltInstallStage.NutTightened))
        {
            bolt.TryHangQuickdraw(out _);
        }

        _stashedHandBeforeStance = null;
        ActiveBoltTarget = null;
        FreeWorkHand = null;
        SelectedTool = ActiveToolKind.None;
        SelectedGear = null;
        ActiveWeightedPiece = null;
        LastFallResult = null;
        CurrentAscentMode = AscentMode.FirstFreeAscent;
        IsCleanFfaAscent = true;
        HasCompletedCleanFfa = false;

        Rig.SetGroundedAtBase();
        Pump.AdvanceSeconds(100f, ClimberActionState.HangingOnGear, 0f);
        Mode = SessionMode.Grounded;
        CurrentPumpRatePerSecond = 0f;

        int readyBolts = Cliff.BoltInstallations.Count(b => b.HasQuickdraw);
        StatusMessage = $"FFA REDPOINT MODE! {readyBolts} pre-hung bolt quickdraw(s) on wall. Climb bottom-to-top with zero falls or hangs!";
    }

    public void ReturnToRouteDevelopmentMode()
    {
        CurrentAscentMode = AscentMode.RouteDevelopment;
        ResetToGround();
        StatusMessage = "Returned to Route Development Mode. Permanent bolts and cleaned rock remain on the cliff.";
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

        if (CurrentAscentMode == AscentMode.FirstFreeAscent && gear.Kind is GearKind.Skyhook or GearKind.Piton)
        {
            StatusMessage = "Aid hooks and hammered pitons cannot be placed during a pure Free Ascent!";
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
        if (Mode is not SessionMode.StanceInspect and not SessionMode.CleaningAndGear and not SessionMode.FreeClimbing and not SessionMode.DrillingBolt)
        {
            StatusMessage = "Can only weight gear while on the wall.";
            return false;
        }

        if (CurrentAscentMode == AscentMode.FirstFreeAscent)
        {
            IsCleanFfaAscent = false;
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

        // If actively striking the drill hammer this frame, TryOperateDrillHammer already applied drilling pump.
        if (_drillingHammerActiveThisStep)
        {
            _drillingHammerActiveThisStep = false;
            return;
        }

        RefreshPumpRate();
        if (PumpPreset == PumpDifficultyPreset.Off || PumpDrainScale <= 0f)
        {
            if (CurrentPumpRatePerSecond < 0f)
            {
                Pump.AdvanceSeconds(deltaSeconds, ClimberActionState.HangingOnGear, 0f);
            }

            return;
        }

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
            if (detail.Protection.Gear.Kind != GearKind.Bolt)
            {
                Rack.ReturnGear(detail.Protection.Gear);
            }
        }

        _placedGear.Clear();
        _climbedFeatureHistory.Clear();
        _stashedHandBeforeStance = null;
        ActiveBoltTarget = null;
        FreeWorkHand = null;
        SelectedTool = ActiveToolKind.None;
        SelectedGear = null;
        ActiveWeightedPiece = null;
        LastFallResult = null;
        if (CurrentAscentMode == AscentMode.FirstFreeAscent)
        {
            IsCleanFfaAscent = true;
            HasCompletedCleanFfa = false;
        }

        Rig.SetGroundedAtBase();
        Pump.AdvanceSeconds(100f, ClimberActionState.HangingOnGear, 0f);
        Mode = SessionMode.Grounded;
        CurrentPumpRatePerSecond = 0f;
        StatusMessage = CurrentAscentMode == AscentMode.FirstFreeAscent
            ? "Lowered to base for a fresh, clean FFA Redpoint attempt!"
            : "Lowered to ground and cleaned removable gear (installed bolts remain on wall).";
    }

    private void TriggerWhipper(float fallStartHeightMeters, string reasonMessage)
    {
        if (CurrentAscentMode == AscentMode.FirstFreeAscent)
        {
            IsCleanFfaAscent = false;
        }

        _stashedHandBeforeStance = null;
        ActiveBoltTarget = null;
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

        if (Mode is not SessionMode.StanceInspect and not SessionMode.CleaningAndGear and not SessionMode.HangingOnGear and not SessionMode.DrillingBolt)
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

    private bool TryPullOffGearOntoFeature(
        RockFeatureBase clickedFeature,
        float normalizedPosition,
        Vector3 targetWorld,
        LimbSlot? manualLimbOverride)
    {
        if (clickedFeature.IsTrundled)
        {
            StatusMessage = $"'{clickedFeature.Name}' has already been trundled.";
            return false;
        }

        float reachFromHang = Vector3.Distance(Rig.CenterOfMassMeters, targetWorld);
        if (reachFromHang > 1.78f)
        {
            StatusMessage = $"'{clickedFeature.Name}' is out of reach from your gear hang ({reachFromHang:F2}m).";
            return false;
        }

        bool clickedAsFoot = manualLimbOverride is LimbSlot.LeftFoot or LimbSlot.RightFoot
            || (manualLimbOverride is null && clickedFeature is StanceFeature && !string.Equals(clickedFeature.Id, "stance-summit-topout", StringComparison.Ordinal) && targetWorld.Y <= Rig.CenterOfMassMeters.Y + 0.35f);

        if (clickedFeature is CrackFeature crackCheck)
        {
            float minW = clickedAsFoot ? 6.0f : 11.0f;
            if (crackCheck.SampleAtNormalized(normalizedPosition).WidthMillimeters < minW)
            {
                StatusMessage = $"'{crackCheck.Name}' is too thin to pull onto.";
                return false;
            }
        }

        if (clickedFeature.IsHollowChoss && !clickedFeature.IsTrundled)
        {
            float fallHeight = MathF.Max(Rig.CenterOfMassMeters.Y, targetWorld.Y - 0.35f);
            clickedFeature.TryTrundleWithPrybar();
            TriggerWhipper(fallHeight, $"CHOSS BREAKOFF! '{clickedFeature.Name}' ripped off as you pulled off your gear!");
            return false;
        }

        // Gather nearby solid climbable features so we plant both hands + at least one solid foothold!
        List<(RockFeatureBase Feature, float Norm, Vector3 Pos)> nearby = [];
        foreach (RockFeatureBase f in Cliff.Features.Where(f => !f.IsTrundled && !f.IsHollowChoss))
        {
            float norm = f is CrackFeature c ? c.GetNormalizedPositionForWorldHeight(targetWorld.Y - 0.65f) : 0.5f;
            Vector3 pos = Cliff.GetFeatureWorldPositionMeters(f, norm);
            if (Vector3.Distance(targetWorld, pos) <= 2.05f)
            {
                nearby.Add((f, norm, pos));
            }
        }

        if (clickedAsFoot)
        {
            var handSupport = nearby
                .Where(c => c.Feature is not StanceFeature
                    && (c.Feature is not CrackFeature cf || cf.SampleAtNormalized(c.Norm).WidthMillimeters >= 11f)
                    && c.Pos.Y >= targetWorld.Y + 0.20f
                    && c.Pos.Y <= targetWorld.Y + 1.85f)
                .OrderBy(c => Vector3.Distance(targetWorld + new Vector3(0f, 0.85f, 0f), c.Pos))
                .FirstOrDefault();

            if (handSupport.Feature is null)
            {
                StatusMessage = "No reachable handhold above that foothold to pull onto!";
                return false;
            }

            LimbSlot footSlot = manualLimbOverride ?? (targetWorld.X <= Rig.CenterOfMassMeters.X ? LimbSlot.LeftFoot : LimbSlot.RightFoot);
            Rig.SetLimbAttachment(Cliff, new LimbAttachment(footSlot, clickedFeature.Id, normalizedPosition, targetWorld));
            Rig.SetLimbAttachment(Cliff, new LimbAttachment(LimbSlot.LeftHand, handSupport.Feature.Id, handSupport.Norm, handSupport.Pos));
            float rhNorm = handSupport.Feature is CrackFeature ? Math.Clamp(handSupport.Norm + 0.06f, 0f, 1f) : handSupport.Norm;
            Rig.SetLimbAttachment(Cliff, new LimbAttachment(LimbSlot.RightHand, handSupport.Feature.Id, rhNorm, Cliff.GetFeatureWorldPositionMeters(handSupport.Feature, rhNorm)));
        }
        else
        {
            var footSupport = nearby
                .Where(c => (c.Feature is not CrackFeature cf || cf.SampleAtNormalized(c.Norm).WidthMillimeters >= 6f)
                    && c.Pos.Y <= targetWorld.Y - 0.25f
                    && c.Pos.Y >= targetWorld.Y - 1.95f)
                .OrderByDescending(c => c.Feature is StanceFeature ? 1 : 0)
                .ThenBy(c => Vector3.Distance(targetWorld + new Vector3(0f, -0.95f, 0f), c.Pos))
                .FirstOrDefault();

            if (footSupport.Feature is null && !string.Equals(clickedFeature.Id, "stance-summit-topout", StringComparison.Ordinal))
            {
                StatusMessage = "No reachable foothold below that hold to establish stance!";
                return false;
            }

            LimbSlot primaryHand = manualLimbOverride is LimbSlot.LeftHand or LimbSlot.RightHand
                ? manualLimbOverride.Value
                : (targetWorld.X <= Rig.CenterOfMassMeters.X ? LimbSlot.LeftHand : LimbSlot.RightHand);
            LimbSlot secondaryHand = primaryHand == LimbSlot.LeftHand ? LimbSlot.RightHand : LimbSlot.LeftHand;

            Rig.SetLimbAttachment(Cliff, new LimbAttachment(primaryHand, clickedFeature.Id, normalizedPosition, targetWorld));

            var partnerHandHold = nearby
                .Where(c => c.Feature is not StanceFeature
                    && (c.Feature is not CrackFeature cf || cf.SampleAtNormalized(c.Norm).WidthMillimeters >= 11f)
                    && MathF.Abs(c.Pos.Y - targetWorld.Y) <= 0.75f)
                .OrderBy(c => Vector3.Distance(targetWorld, c.Pos))
                .FirstOrDefault();

            if (partnerHandHold.Feature is not null)
            {
                float secNorm = ReferenceEquals(partnerHandHold.Feature, clickedFeature) && clickedFeature is CrackFeature
                    ? Math.Clamp(normalizedPosition - 0.06f, 0f, 1f)
                    : partnerHandHold.Norm;
                Rig.SetLimbAttachment(Cliff, new LimbAttachment(secondaryHand, partnerHandHold.Feature.Id, secNorm, Cliff.GetFeatureWorldPositionMeters(partnerHandHold.Feature, secNorm)));
            }
            else
            {
                Rig.SetLimbAttachment(Cliff, new LimbAttachment(secondaryHand, clickedFeature.Id, normalizedPosition, targetWorld));
            }

            if (footSupport.Feature is not null)
            {
                LimbSlot footSlot = footSupport.Pos.X <= targetWorld.X ? LimbSlot.LeftFoot : LimbSlot.RightFoot;
                Rig.SetLimbAttachment(Cliff, new LimbAttachment(footSlot, footSupport.Feature.Id, footSupport.Norm, footSupport.Pos));
            }
        }

        ActiveWeightedPiece = null;
        FreeWorkHand = null;
        SelectedTool = ActiveToolKind.None;

        if (string.Equals(clickedFeature.Id, "stance-summit-topout", StringComparison.Ordinal) ||
            targetWorld.Y >= Cliff.HeightMeters - 0.25f)
        {
            Mode = SessionMode.ToppedOut;
            CurrentPumpRatePerSecond = -6.0f;
            StatusMessage = $"TOPPED OUT '{Cliff.Name}' cleanly!";
            return true;
        }

        Mode = SessionMode.FreeClimbing;
        RefreshPumpRate();
        StatusMessage = $"Pulled off gear onto '{clickedFeature.Name}' with feet established.";
        return true;
    }

    private void RefreshPumpRate()
    {
        float raw = ComputeBaseModelPumpRate();
        if (raw > 0f)
        {
            CurrentPumpRatePerSecond = MathF.Round(raw * Rig.ComputeBalancePumpMultiplier() * PumpDrainScale, 2);
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

    private ClimberActionState MapToActionState(SessionMode mode) => mode switch
    {
        SessionMode.HangingOnGear => ClimberActionState.HangingOnGear,
        SessionMode.DrillingBolt when ActiveWeightedPiece is not null => ClimberActionState.HangingOnGear,
        SessionMode.StanceInspect or SessionMode.CleaningAndGear or SessionMode.DrillingBolt => ClimberActionState.StanceInspect,
        _ => ClimberActionState.FreeClimbing
    };

    private void RecordClimbedFeature(string featureId)
    {
        if (!_climbedFeatureHistory.Contains(featureId, StringComparer.Ordinal))
        {
            _climbedFeatureHistory.Add(featureId);
        }
    }

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
