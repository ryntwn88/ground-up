using System.Text;
using Godot;
using GroundUp.Core.Bolting;
using GroundUp.Core.Climbing;
using GroundUp.Core.Gear;
using GroundUp.Core.Geology;
using GroundUp.Core.Physics;

namespace GroundUp.GodotClient;

/// <summary>
/// Phase 1/2 HUD presenting the forearm pump gauge, Ascent Mode badge (Develop vs FFA),
/// interactive Bolting Workbench (depth, dust blow-out, wrench nut torque, quickdraw clip),
/// harness rack bar, and the F3 developer telemetry + power-drill toggle panel.
/// </summary>
public partial class ClimbingHudController : CanvasLayer
{
    private Label _ascentModeBannerLabel = null!;
    private Label _modeBadgeLabel = null!;
    private ProgressBar _pumpBar = null!;
    private StyleBoxFlat _pumpFillStyle = null!;
    private Label _pumpTrendLabel = null!;
    private Label _limbModeLabel = null!;
    private Label _statusBannerLabel = null!;
    private Label _rackStatusLabel = null!;
    private Button _pumpToggleButton = null!;
    private Button _ascentModeButton = null!;

    private PanelContainer _boltingWorkbenchPanel = null!;
    private Label _boltStageTitleLabel = null!;
    private ProgressBar _holeDepthBar = null!;
    private ProgressBar _holeDustBar = null!;
    private ProgressBar _nutTorqueBar = null!;
    private Label _boltInstructionLabel = null!;

    private PanelContainer _telemetryPanel = null!;
    private Label _telemetryLabel = null!;
    private Button _devPowerDrillButton = null!;
    private PanelContainer _helpPanel = null!;

    public bool ShowTelemetry { get; set; }
    public bool ShowControlsHelp { get; set; } = true;

    public event Action? ToggleStanceRequested;
    public event Action? WeightGearRequested;
    public event Action? ClipQuickdrawRequested;
    public event Action? BlowDustRequested;
    public event Action? InsertOrTorqueBoltRequested;
    public event Action? ToggleAscentModeRequested;
    public event Action? ToggleDevPowerDrillRequested;
    public event Action? CyclePumpPresetRequested;
    public event Action? ResetClimbRequested;
    public event Action<int>? HotkeySlotPressed;

    public override void _Ready()
    {
        BuildUiTree();
    }

    public void UpdateHud(
        ClimbingSession session,
        LimbSlot? limbOverride,
        RockFeatureBase? hoveredFeature,
        float hoveredNorm,
        PlacementEvaluation? hoveredGearEval,
        bool? hoveredMoveValid = null,
        LimbSlot? predictedLimb = null,
        string? movePreviewDiagnostic = null,
        BoltInstallation? hoveredBolt = null)
    {
        if (session.CurrentAscentMode == AscentMode.FirstFreeAscent)
        {
            string cleanTag = session.IsCleanFfaAscent ? "CLEAN REDPOINT BURN" : "INVALIDATED (FALL/HANG — PRESS [R] TO RESTART)";
            _ascentModeBannerLabel.Text = $"MODULE 3: FIRST FREE ASCENT (FFA) — {cleanTag}";
            _ascentModeBannerLabel.AddThemeColorOverride(
                "font_color",
                session.IsCleanFfaAscent ? new Color(0.32f, 0.96f, 0.65f) : new Color(0.96f, 0.36f, 0.30f));
            _ascentModeButton.Text = "[M] Back to Dev Mode";
        }
        else
        {
            int completedBolts = session.Cliff.BoltInstallations.Count(b => b.IsCompletedBolt);
            _ascentModeBannerLabel.Text = $"MODULE 2: GROUND-UP DEVELOPMENT  (Installed Bolts: {completedBolts} | Bag: {session.BoltsRemainingInBag})";
            _ascentModeBannerLabel.AddThemeColorOverride("font_color", new Color(0.68f, 0.88f, 1.0f));
            _ascentModeButton.Text = "[M] Start FFA Attempt";
        }

        _modeBadgeLabel.Text = session.Mode switch
        {
            SessionMode.Grounded => "STATE: GROUNDED AT BASE",
            SessionMode.FreeClimbing => $"STATE: FREE CLIMBING ({session.Rig.CenterOfMassMeters.Y:F1}m)",
            SessionMode.StanceInspect => $"STATE: 3-POINT STANCE LOCKED ({session.FreeWorkHand} FREE)",
            SessionMode.CleaningAndGear => $"STATE: ROCK CLEANING & GEAR ({session.SelectedTool})",
            SessionMode.DrillingBolt => $"STATE: BOLTING WORKBENCH ({(session.ActiveWeightedPiece is not null ? "ON GEAR" : "FREE STANCE")})",
            SessionMode.HangingOnGear => $"STATE: HANGING ON GEAR ({session.ActiveWeightedPiece?.Protection.Gear.DisplayName})",
            SessionMode.WhipperFall => "STATE: DYNAMIC WHIPPER!",
            SessionMode.ToppedOut => session.HasCompletedCleanFfa ? "STATE: CLEAN FFA SUMMIT! 🏆" : "STATE: SUMMIT TOP-OUT!",
            _ => session.Mode.ToString()
        };

        float pump = session.Pump.CurrentPump;
        _pumpBar.Value = pump;
        _pumpFillStyle.BgColor = pump switch
        {
            < 45f => new Color(0.22f, 0.78f, 0.42f),
            < 75f => new Color(0.94f, 0.72f, 0.18f),
            _ => new Color(0.92f, 0.22f, 0.22f)
        };

        string trend = session.PumpPreset == PumpDifficultyPreset.Off
            ? "⏸ PUMP DISABLED"
            : session.CurrentPumpRatePerSecond switch
            {
                > 2.5f => "▲▲ HEAVY DRAIN",
                > 0.05f => "▲ PUMPING",
                < -0.05f => "▼ RECOVERING",
                _ => "• STEADY"
            };
        string presetBadge = ClimbingSession.GetDisplayLabelForPreset(session.PumpPreset);
        _pumpTrendLabel.Text = $"Forearm Pump: {pump:F0}% ({trend})   |   Pump [P]: {presetBadge}";
        _pumpToggleButton.Text = session.PumpPreset == PumpDifficultyPreset.Off
            ? "[P] Pump: OFF"
            : $"[P] Pump: {session.PumpDrainScale:0.##}x";

        _limbModeLabel.Text = limbOverride.HasValue
            ? $"Limb Input: MANUAL OVERRIDE -> {limbOverride.Value}  (Press same key or Esc for Smart Auto)"
            : "Limb Input: SMART CURSOR AUTO  (Optional: [Q] LH  [E] RH  [Z] LF  [C] RF)";

        string hoverHint;
        if (hoveredBolt is not null)
        {
            hoverHint = hoveredBolt.HasQuickdraw
                ? $"Aiming at: {hoveredBolt.Id} Quickdraw   [Click or Press V -> Clip Rope]"
                : $"Aiming at: {hoveredBolt.Id} ({hoveredBolt.Stage})";
        }
        else if (hoveredFeature is null)
        {
            hoverHint = session.SelectedTool == ActiveToolKind.BoltDrill
                ? "Aim at reachable rock face and Left-Click to position the drill bit"
                : "Aim cursor at a rock feature on the cliff";
        }
        else if (hoveredMoveValid == true && predictedLimb.HasValue)
        {
            hoverHint = $"Aiming at: {hoveredFeature.Name}   [Click -> {predictedLimb.Value}]";
        }
        else if (hoveredMoveValid == false && !string.IsNullOrEmpty(movePreviewDiagnostic))
        {
            hoverHint = $"Aiming at: {hoveredFeature.Name}   [Blocked: {movePreviewDiagnostic}]";
        }
        else
        {
            hoverHint = $"Aiming at: {hoveredFeature.Name}";
        }

        _statusBannerLabel.Text = $"{hoverHint}\n» {session.StatusMessage}";

        string selectedReadout = session.SelectedTool switch
        {
            ActiveToolKind.HammerSound => "[1] HAMMER (Click feature to sound solidity)",
            ActiveToolKind.WireBrush => "[2] WIRE BRUSH (Click mossy feature to scrub clean)",
            ActiveToolKind.Prybar => "[3] PRYBAR (Click hollow choss flake to trundle)",
            ActiveToolKind.BoltDrill => $"[8] {session.EquippedDrill.Name} — Left-Click rock to position drill, Hold [Space] to hammer",
            ActiveToolKind.PlaceGear when session.SelectedGear is not null =>
                $"ACTIVE GEAR: {session.SelectedGear.DisplayName}  (x{session.Rack.GetQuantity(session.SelectedGear.Code)}) — Wheel/[ ] to resize, Click to place",
            _ => session.CurrentAscentMode == AscentMode.FirstFreeAscent
                ? "FFA Mode: Click holds to climb, Click/[V] pre-hung quickdraws to clip, or [F]+[4]/[7] for trad gear"
                : "No tool readied — Lock 3-pt stance [F] then press [1]-[8]"
        };
        _rackStatusLabel.Text = $"{selectedReadout}   |   Active Rope Chain: {session.PlacedGear.Count}";

        UpdateBoltingWorkbench(session);

        _helpPanel.Visible = ShowControlsHelp;
        _telemetryPanel.Visible = ShowTelemetry;
        if (ShowTelemetry)
        {
            _devPowerDrillButton.Text = session.EquippedDrill.IsPowerTool
                ? "[DEV] Drill: 24V Rotary Hammer (Click for Hand Drill)"
                : "[DEV] Drill: Hand Drill (Click for 24V Power Drill)";
            _telemetryLabel.Text = BuildTelemetryText(session, hoveredFeature, hoveredNorm, hoveredGearEval, hoveredBolt);
        }
    }

    private void UpdateBoltingWorkbench(ClimbingSession session)
    {
        BoltInstallation? bolt = session.ActiveBoltTarget;
        bool showWorkbench = session.CurrentAscentMode == AscentMode.RouteDevelopment
            && (session.Mode == SessionMode.DrillingBolt || session.SelectedTool == ActiveToolKind.BoltDrill);

        _boltingWorkbenchPanel.Visible = showWorkbench;
        if (!showWorkbench)
        {
            return;
        }

        if (bolt is null)
        {
            _boltStageTitleLabel.Text = $"BOLTING WORKBENCH — {session.EquippedDrill.Name}";
            _holeDepthBar.Value = 0;
            _holeDustBar.Value = 0;
            _nutTorqueBar.Value = 0;
            _boltInstructionLabel.Text = "Step 1: Left-Click any reachable spot on the rock face to position the drill bit.";
            return;
        }

        _boltStageTitleLabel.Text = $"BOLT {bolt.Id.ToUpperInvariant()} @ {bolt.WorldPositionMeters.Y:F1}m — Stage: {bolt.Stage}";
        _holeDepthBar.MaxValue = bolt.TargetDepthMillimeters;
        _holeDepthBar.Value = bolt.CurrentDepthMillimeters;
        _holeDustBar.Value = bolt.DustFillRatio * 100f;
        _nutTorqueBar.Value = bolt.NutTorqueRatio * 100f;

        _boltInstructionLabel.Text = bolt.Stage switch
        {
            BoltInstallStage.DrillingHole =>
                $"Depth: {bolt.CurrentDepthMillimeters:F1}/{bolt.TargetDepthMillimeters:F0} mm  |  Dust: {bolt.DustFillRatio:P0}\n" +
                "• Hold [SPACE] to operate hammer (drill bit rotates automatically)\n" +
                "• Press [B] when dust builds up to blow the hole clean!",
            BoltInstallStage.HoleDepthReached =>
                $"Full Depth Reached ({bolt.TargetDepthMillimeters:F0} mm)!  Dust in hole: {bolt.DustFillRatio:P0}\n" +
                "• Press [B] to blow rock dust clear, then press [T] to tap in bolt & hanger!",
            BoltInstallStage.BoltInsertedUntorqued =>
                $"Bolt & Hanger Inserted — Hex Nut Torque: {bolt.NutTorqueRatio:P0}\n" +
                "• Press [T] repeatedly to tighten hex nut to 100% before clipping quickdraw!",
            BoltInstallStage.NutTightened =>
                "Hex Nut Torqued to 100%! Wedge expanded.\n" +
                "• Press [V] to hang quickdraw & clip the lead rope!",
            _ => "Bolt & Quickdraw Complete! Press [G] to hang & rest on bolt, or [F] to resume climbing."
        };
    }

    private static string BuildTelemetryText(
        ClimbingSession session,
        RockFeatureBase? hoveredFeature,
        float hoveredNorm,
        PlacementEvaluation? hoveredGearEval,
        BoltInstallation? hoveredBolt)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[F3] DEVELOPER PHYSICS TELEMETRY");
        sb.AppendLine($"CoM: ({session.Rig.CenterOfMassMeters.X:F2}, {session.Rig.CenterOfMassMeters.Y:F2}, {session.Rig.CenterOfMassMeters.Z:F2}) m");
        sb.AppendLine($"Balance: {session.Rig.ComputeBalancePumpMultiplier():F2}x  |  Pump Rate: {session.CurrentPumpRatePerSecond:+0.00;-0.00;0.00} /s");

        if (hoveredBolt?.InstalledEvaluation is PlacementEvaluation bEval)
        {
            sb.AppendLine($"--- Bolt {hoveredBolt.Id} ---");
            sb.AppendLine($"Substrate Integrity: {hoveredBolt.SubstrateIntegrity:P0} | Clean Hole: {hoveredBolt.WasHoleCleanWhenBoltInserted}");
            sb.AppendLine($"Hold: {bEval.EffectiveHoldingForceKilonewtons:F1} kN ({bEval.Quality})");
        }
        else if (hoveredFeature is not null)
        {
            sb.AppendLine($"--- Hovered: {hoveredFeature.Id} ---");
            sb.AppendLine($"Integrity: {hoveredFeature.StructuralIntegrity:P0} (Choss: {hoveredFeature.IsHollowChoss}) | Clean: {hoveredFeature.Surface.CleanlinessMultiplier:P0}");
            switch (hoveredFeature)
            {
                case CrackFeature crack:
                    CrackSample s = crack.SampleAtNormalized(hoveredNorm);
                    sb.AppendLine($"Crack @ t={hoveredNorm:F2} ({s.WorldHeightMeters:F2}m): {s.WidthMillimeters:F1}mm, Flare {s.FlareDegrees:F1}°, Depth {s.DepthMillimeters:F0}mm");
                    break;
                case EdgeFeature edge:
                    sb.AppendLine($"Edge: Depth {edge.DepthMillimeters:F1}mm, Incut {edge.IncutDegrees:F1}°, Width {edge.WidthMillimeters:F0}mm");
                    break;
                case HornFeature horn:
                    sb.AppendLine($"Horn: Neck {horn.NeckRadiusMillimeters:F0}mm, Collar {horn.CollarAngleDegrees:F1}°");
                    break;
                case StanceFeature stance:
                    sb.AppendLine($"Stance: Area {stance.FootAreaSqCentimeters:F0} cm², Slope {stance.SlopeDegrees:F1}°");
                    break;
            }
        }

        if (hoveredGearEval is not null)
        {
            sb.AppendLine($"Eval: {hoveredGearEval.Quality} | Fits: {hoveredGearEval.FitsGeometry} | Hold: {hoveredGearEval.EffectiveHoldingForceKilonewtons:F2} kN");
        }

        return sb.ToString();
    }

    private void BuildUiTree()
    {
        var panelBg = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.10f, 0.13f, 0.88f),
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 10,
            ContentMarginBottom = 10
        };

        // Top-left status & stamina box
        var topPanel = new PanelContainer
        {
            Position = new Vector2(18f, 14f),
            CustomMinimumSize = new Vector2(560f, 0f)
        };
        topPanel.AddThemeStyleboxOverride("panel", panelBg);

        var topVBox = new VBoxContainer();
        topVBox.AddThemeConstantOverride("separation", 5);

        _ascentModeBannerLabel = new Label { Text = "MODULE 2: GROUND-UP DEVELOPMENT" };
        _modeBadgeLabel = new Label { Text = "STATE: GROUNDED" };
        _modeBadgeLabel.AddThemeColorOverride("font_color", new Color(0.96f, 0.84f, 0.28f));

        _pumpTrendLabel = new Label { Text = "Forearm Pump: 0%" };

        _pumpFillStyle = new StyleBoxFlat { BgColor = new Color(0.22f, 0.78f, 0.42f), CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4 };
        _pumpBar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 100,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(520f, 16f)
        };
        _pumpBar.AddThemeStyleboxOverride("fill", _pumpFillStyle);

        _limbModeLabel = new Label();
        _limbModeLabel.AddThemeColorOverride("font_color", new Color(0.72f, 0.84f, 0.96f));

        _statusBannerLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };

        topVBox.AddChild(_ascentModeBannerLabel);
        topVBox.AddChild(_modeBadgeLabel);
        topVBox.AddChild(_pumpTrendLabel);
        topVBox.AddChild(_pumpBar);
        topVBox.AddChild(_limbModeLabel);
        topVBox.AddChild(_statusBannerLabel);
        topPanel.AddChild(topVBox);
        AddChild(topPanel);

        // Bolting Workbench Sub-Panel (left side below status box when drilling/bolting)
        _boltingWorkbenchPanel = new PanelContainer
        {
            Position = new Vector2(18f, 224f),
            CustomMinimumSize = new Vector2(460f, 0f),
            Visible = false
        };
        _boltingWorkbenchPanel.AddThemeStyleboxOverride("panel", panelBg);

        var boltVBox = new VBoxContainer();
        boltVBox.AddThemeConstantOverride("separation", 6);

        _boltStageTitleLabel = new Label { Text = "BOLTING WORKBENCH" };
        _boltStageTitleLabel.AddThemeColorOverride("font_color", new Color(0.32f, 0.92f, 0.98f));

        boltVBox.AddChild(_boltStageTitleLabel);

        boltVBox.AddChild(new Label { Text = "Hole Penetration Depth (mm):" });
        _holeDepthBar = new ProgressBar { MinValue = 0, MaxValue = 89, ShowPercentage = true, CustomMinimumSize = new Vector2(420f, 16f) };
        boltVBox.AddChild(_holeDepthBar);

        boltVBox.AddChild(new Label { Text = "Pulverized Rock Dust Choke (%):" });
        _holeDustBar = new ProgressBar { MinValue = 0, MaxValue = 100, ShowPercentage = true, CustomMinimumSize = new Vector2(420f, 14f) };
        boltVBox.AddChild(_holeDustBar);

        boltVBox.AddChild(new Label { Text = "Hex Nut Wrench Torque (%):" });
        _nutTorqueBar = new ProgressBar { MinValue = 0, MaxValue = 100, ShowPercentage = true, CustomMinimumSize = new Vector2(420f, 14f) };
        boltVBox.AddChild(_nutTorqueBar);

        _boltInstructionLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        boltVBox.AddChild(_boltInstructionLabel);

        var boltActionRow = new HBoxContainer();
        boltActionRow.AddThemeConstantOverride("separation", 6);
        AddBarButton(boltActionRow, "[B] Blow Dust", () => BlowDustRequested?.Invoke());
        AddBarButton(boltActionRow, "[T] Insert / Wrench Nut", () => InsertOrTorqueBoltRequested?.Invoke());
        AddBarButton(boltActionRow, "[V] Clip Quickdraw", () => ClipQuickdrawRequested?.Invoke());
        boltVBox.AddChild(boltActionRow);

        _boltingWorkbenchPanel.AddChild(boltVBox);
        AddChild(_boltingWorkbenchPanel);

        // Bottom harness & tool bar
        var bottomPanel = new PanelContainer
        {
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 1.0f,
            AnchorBottom = 1.0f,
            OffsetLeft = -730f,
            OffsetRight = 730f,
            OffsetTop = -98f,
            OffsetBottom = -12f
        };
        bottomPanel.AddThemeStyleboxOverride("panel", panelBg);

        var bottomVBox = new VBoxContainer();
        bottomVBox.AddThemeConstantOverride("separation", 6);

        var buttonRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttonRow.AddThemeConstantOverride("separation", 5);

        AddBarButton(buttonRow, "[F] Stance/Climb", () => ToggleStanceRequested?.Invoke());
        AddBarButton(buttonRow, "[1] Hammer", () => HotkeySlotPressed?.Invoke(1));
        AddBarButton(buttonRow, "[2] Brush", () => HotkeySlotPressed?.Invoke(2));
        AddBarButton(buttonRow, "[3] Prybar", () => HotkeySlotPressed?.Invoke(3));
        AddBarButton(buttonRow, "[4] Cams", () => HotkeySlotPressed?.Invoke(4));
        AddBarButton(buttonRow, "[5] Pitons", () => HotkeySlotPressed?.Invoke(5));
        AddBarButton(buttonRow, "[6] Hooks", () => HotkeySlotPressed?.Invoke(6));
        AddBarButton(buttonRow, "[7] Slings", () => HotkeySlotPressed?.Invoke(7));
        AddBarButton(buttonRow, "[8] Drill", () => HotkeySlotPressed?.Invoke(8));
        AddBarButton(buttonRow, "[V] Clip Draw", () => ClipQuickdrawRequested?.Invoke());
        AddBarButton(buttonRow, "[G] Weight Gear", () => WeightGearRequested?.Invoke());
        _pumpToggleButton = AddBarButton(buttonRow, "[P] Pump: 1x", () => CyclePumpPresetRequested?.Invoke());
        _ascentModeButton = AddBarButton(buttonRow, "[M] Start FFA Attempt", () => ToggleAscentModeRequested?.Invoke());
        AddBarButton(buttonRow, "[R] Lower", () => ResetClimbRequested?.Invoke());

        _rackStatusLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _rackStatusLabel.AddThemeColorOverride("font_color", new Color(0.92f, 0.94f, 0.98f));

        bottomVBox.AddChild(buttonRow);
        bottomVBox.AddChild(_rackStatusLabel);
        bottomPanel.AddChild(bottomVBox);
        AddChild(bottomPanel);

        // Top-right compact controls card
        _helpPanel = new PanelContainer
        {
            AnchorLeft = 1.0f,
            AnchorRight = 1.0f,
            OffsetLeft = -395f,
            OffsetRight = -18f,
            OffsetTop = 14f
        };
        _helpPanel.AddThemeStyleboxOverride("panel", panelBg);
        var helpLabel = new Label
        {
            Text = "CONTROLS ([H] Hide)\n" +
                   "• Left Click: Smart Move / Position Drill / Clip\n" +
                   "• Right Drag & W/S: Orbit & Scout Camera\n" +
                   "• Q / E / Z / C: Force LH / RH / LF / RF\n" +
                   "• F: Lock 3-Pt Stance <-> Resume Climb\n" +
                   "• 1-3: Hammer, Brush, Prybar  |  4-7: Trad/Aid\n" +
                   "• 8: Ready Drill -> Click rock to position\n" +
                   "• Hold SPACE: Strike Hammer (auto-rotates bit)\n" +
                   "• B: Blow Dust  |  T: Insert & Wrench Nut\n" +
                   "• V: Clip Quickdraw  |  G: Weight Gear\n" +
                   "• M: Switch Dev <-> FFA Mode  |  P: Pump Scale\n" +
                   "• F3: Dev Telemetry & Power-Drill Toggle"
        };
        _helpPanel.AddChild(helpLabel);
        AddChild(_helpPanel);

        // F3 Developer Telemetry + Dev-Only Power-Drill Toggle panel
        _telemetryPanel = new PanelContainer
        {
            AnchorLeft = 1.0f,
            AnchorRight = 1.0f,
            OffsetLeft = -470f,
            OffsetRight = -18f,
            OffsetTop = 292f,
            Visible = false
        };
        _telemetryPanel.AddThemeStyleboxOverride("panel", panelBg);

        var teleVBox = new VBoxContainer();
        teleVBox.AddThemeConstantOverride("separation", 6);

        _devPowerDrillButton = new Button
        {
            Text = "[DEV] Drill: Hand Drill (Click for 24V Power Drill)",
            FocusMode = Control.FocusModeEnum.None
        };
        _devPowerDrillButton.Pressed += () => ToggleDevPowerDrillRequested?.Invoke();

        _telemetryLabel = new Label();
        _telemetryLabel.AddThemeColorOverride("font_color", new Color(0.62f, 0.96f, 0.78f));

        teleVBox.AddChild(_devPowerDrillButton);
        teleVBox.AddChild(_telemetryLabel);
        _telemetryPanel.AddChild(teleVBox);
        AddChild(_telemetryPanel);
    }

    private static Button AddBarButton(HBoxContainer row, string label, Action onClick)
    {
        var btn = new Button { Text = label, FocusMode = Control.FocusModeEnum.None };
        btn.Pressed += onClick;
        row.AddChild(btn);
        return btn;
    }
}
