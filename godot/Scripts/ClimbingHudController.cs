using System.Text;
using Godot;
using GroundUp.Core.Climbing;
using GroundUp.Core.Gear;
using GroundUp.Core.Geology;
using GroundUp.Core.Physics;

namespace GroundUp.GodotClient;

/// <summary>
/// Phase 1 HUD presenting the forearm pump gauge, diegetic status/controls, harness tool & rack bar,
/// and the optional F3 physics telemetry overlay.
/// </summary>
public partial class ClimbingHudController : CanvasLayer
{
    private Label _modeBadgeLabel = null!;
    private ProgressBar _pumpBar = null!;
    private StyleBoxFlat _pumpFillStyle = null!;
    private Label _pumpTrendLabel = null!;
    private Label _limbModeLabel = null!;
    private Label _statusBannerLabel = null!;
    private Label _rackStatusLabel = null!;
    private PanelContainer _telemetryPanel = null!;
    private Label _telemetryLabel = null!;
    private PanelContainer _helpPanel = null!;

    public bool ShowTelemetry { get; set; }
    public bool ShowControlsHelp { get; set; } = true;

    public event Action? ToggleStanceRequested;
    public event Action? WeightGearRequested;
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
        PlacementEvaluation? hoveredGearEval)
    {
        _modeBadgeLabel.Text = session.Mode switch
        {
            SessionMode.Grounded => "STATE: GROUNDED AT BASE",
            SessionMode.FreeClimbing => $"STATE: FREE CLIMBING ({session.Rig.CenterOfMassMeters.Y:F1}m)",
            SessionMode.StanceInspect => $"STATE: 3-POINT STANCE LOCKED ({session.FreeWorkHand} FREE)",
            SessionMode.CleaningAndGear => $"STATE: ROCK CLEANING & GEAR ({session.SelectedTool})",
            SessionMode.HangingOnGear => $"STATE: HANGING ON GEAR ({session.ActiveWeightedPiece?.Protection.Gear.DisplayName})",
            SessionMode.WhipperFall => "STATE: DYNAMIC WHIPPER!",
            SessionMode.ToppedOut => "STATE: SUMMIT TOP-OUT! 🏆",
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

        string trend = session.CurrentPumpRatePerSecond switch
        {
            > 2.5f => "▲▲ HEAVY DRAIN",
            > 0.05f => "▲ PUMPING",
            < -0.05f => "▼ RECOVERING",
            _ => "• STEADY"
        };
        _pumpTrendLabel.Text = $"Forearm Pump: {pump:F0}% ({trend})";

        _limbModeLabel.Text = limbOverride.HasValue
            ? $"Limb Input: MANUAL OVERRIDE -> {limbOverride.Value}  (Press same key or Esc for Smart Auto)"
            : "Limb Input: SMART CURSOR AUTO  (Optional override: [Q] LH  [E] RH  [Z] LF  [C] RF)";

        string hoverHint = hoveredFeature is not null
            ? $"Aiming at: {hoveredFeature.Name}"
            : "Aim cursor at a rock feature on the cliff";
        _statusBannerLabel.Text = $"{hoverHint}\n» {session.StatusMessage}";

        string selectedReadout = session.SelectedTool switch
        {
            ActiveToolKind.HammerSound => "[1] HAMMER (Click feature to sound solidity)",
            ActiveToolKind.WireBrush => "[2] WIRE BRUSH (Click mossy feature to scrub clean)",
            ActiveToolKind.Prybar => "[3] PRYBAR (Click hollow choss flake to trundle)",
            ActiveToolKind.PlaceGear when session.SelectedGear is not null =>
                $"ACTIVE GEAR: {session.SelectedGear.DisplayName}  (x{session.Rack.GetQuantity(session.SelectedGear.Code)} on rack) — Scroll/[ ] to resize, Click to place",
            _ => "No tool readied — Lock 3-pt stance [F] then press [1]-[7]"
        };
        _rackStatusLabel.Text = $"{selectedReadout}   |   Placed on Wall: {session.PlacedGear.Count}";

        _helpPanel.Visible = ShowControlsHelp;
        _telemetryPanel.Visible = ShowTelemetry;
        if (ShowTelemetry)
        {
            _telemetryLabel.Text = BuildTelemetryText(session, hoveredFeature, hoveredNorm, hoveredGearEval);
        }
    }

    private static string BuildTelemetryText(
        ClimbingSession session,
        RockFeatureBase? hoveredFeature,
        float hoveredNorm,
        PlacementEvaluation? hoveredGearEval)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[F3] DEVELOPER PHYSICS TELEMETRY");
        sb.AppendLine($"CoM: ({session.Rig.CenterOfMassMeters.X:F2}, {session.Rig.CenterOfMassMeters.Y:F2}, {session.Rig.CenterOfMassMeters.Z:F2}) m");
        sb.AppendLine($"Balance Multiplier: {session.Rig.ComputeBalancePumpMultiplier():F2}x  |  Pump Rate: {session.CurrentPumpRatePerSecond:+0.00;-0.00;0.00} /s");

        if (hoveredFeature is not null)
        {
            sb.AppendLine($"--- Hovered: {hoveredFeature.Id} ---");
            sb.AppendLine($"Integrity: {hoveredFeature.StructuralIntegrity:P0} (Choss: {hoveredFeature.IsHollowChoss}) | Cleanliness: {hoveredFeature.Surface.CleanlinessMultiplier:P0}");
            switch (hoveredFeature)
            {
                case CrackFeature crack:
                    CrackSample s = crack.SampleAtNormalized(hoveredNorm);
                    sb.AppendLine($"Crack @ t={hoveredNorm:F2} ({s.WorldHeightMeters:F2}m): Width {s.WidthMillimeters:F1}mm, Flare {s.FlareDegrees:F1}°, Depth {s.DepthMillimeters:F0}mm");
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
            sb.AppendLine($"Placement Eval: {hoveredGearEval.Quality} | Fits: {hoveredGearEval.FitsGeometry} | Hold: {hoveredGearEval.EffectiveHoldingForceKilonewtons:F2} kN");
            sb.AppendLine($"  {hoveredGearEval.DiagnosticSummary}");
        }

        if (session.LastFallResult is not null)
        {
            FallSimulationResult f = session.LastFallResult;
            sb.AppendLine($"Last Whipper: {f.Outcome}, FF={f.InitialFallFactor:F2}, Drop={f.TotalFallDistanceMeters:F2}m, Peak={f.PeakClimberImpactKilonewtons:F1}kN, Blown={f.BlownPieces.Count}");
        }

        return sb.ToString();
    }

    private void BuildUiTree()
    {
        var panelBg = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.10f, 0.13f, 0.86f),
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
            Position = new Vector2(18f, 16f),
            CustomMinimumSize = new Vector2(540f, 0f)
        };
        topPanel.AddThemeStyleboxOverride("panel", panelBg);

        var topVBox = new VBoxContainer();
        topVBox.AddThemeConstantOverride("separation", 6);

        _modeBadgeLabel = new Label { Text = "STATE: GROUNDED" };
        _modeBadgeLabel.AddThemeColorOverride("font_color", new Color(0.96f, 0.84f, 0.28f));

        _pumpTrendLabel = new Label { Text = "Forearm Pump: 0%" };

        _pumpFillStyle = new StyleBoxFlat { BgColor = new Color(0.22f, 0.78f, 0.42f), CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4 };
        _pumpBar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 100,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(500f, 18f)
        };
        _pumpBar.AddThemeStyleboxOverride("fill", _pumpFillStyle);

        _limbModeLabel = new Label();
        _limbModeLabel.AddThemeColorOverride("font_color", new Color(0.72f, 0.84f, 0.96f));

        _statusBannerLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };

        topVBox.AddChild(_modeBadgeLabel);
        topVBox.AddChild(_pumpTrendLabel);
        topVBox.AddChild(_pumpBar);
        topVBox.AddChild(_limbModeLabel);
        topVBox.AddChild(_statusBannerLabel);
        topPanel.AddChild(topVBox);
        AddChild(topPanel);

        // Bottom harness & tool bar
        var bottomPanel = new PanelContainer
        {
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 1.0f,
            AnchorBottom = 1.0f,
            OffsetLeft = -530f,
            OffsetRight = 530f,
            OffsetTop = -98f,
            OffsetBottom = -14f
        };
        bottomPanel.AddThemeStyleboxOverride("panel", panelBg);

        var bottomVBox = new VBoxContainer();
        bottomVBox.AddThemeConstantOverride("separation", 6);

        var buttonRow = new HBoxContainer();
        buttonRow.AddThemeConstantOverride("separation", 6);

        AddBarButton(buttonRow, "[F] 3-Pt Stance / Climb", () => ToggleStanceRequested?.Invoke());
        AddBarButton(buttonRow, "[1] Hammer", () => HotkeySlotPressed?.Invoke(1));
        AddBarButton(buttonRow, "[2] Brush", () => HotkeySlotPressed?.Invoke(2));
        AddBarButton(buttonRow, "[3] Prybar", () => HotkeySlotPressed?.Invoke(3));
        AddBarButton(buttonRow, "[4] Cams", () => HotkeySlotPressed?.Invoke(4));
        AddBarButton(buttonRow, "[5] Pitons", () => HotkeySlotPressed?.Invoke(5));
        AddBarButton(buttonRow, "[6] Skyhooks", () => HotkeySlotPressed?.Invoke(6));
        AddBarButton(buttonRow, "[7] Slings", () => HotkeySlotPressed?.Invoke(7));
        AddBarButton(buttonRow, "[G] Weight Gear", () => WeightGearRequested?.Invoke());
        AddBarButton(buttonRow, "[R] Reset", () => ResetClimbRequested?.Invoke());

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
            OffsetLeft = -370f,
            OffsetRight = -18f,
            OffsetTop = 16f
        };
        _helpPanel.AddThemeStyleboxOverride("panel", panelBg);
        var helpLabel = new Label
        {
            Text = "CONTROLS ([H] Hide)\n" +
                   "• Left Click: Move Limb / Use Active Tool\n" +
                   "• Right Drag: Orbit / Peek Camera\n" +
                   "• W / S: Scout Camera Up / Down\n" +
                   "• Q / E / Z / C: Force LH / RH / LF / RF\n" +
                   "• F: Lock 3-Pt Stance <-> Resume Climb\n" +
                   "• 1-3: Hammer, Brush, Prybar\n" +
                   "• 4-7 + Wheel / [ ]: Rack Gear & Size\n" +
                   "• G: Weight Gear (Hang & Recover)\n" +
                   "• F3: Toggle Physics Telemetry"
        };
        _helpPanel.AddChild(helpLabel);
        AddChild(_helpPanel);

        // F3 Developer Telemetry panel (hidden by default per diegetic design choice)
        _telemetryPanel = new PanelContainer
        {
            AnchorLeft = 1.0f,
            AnchorRight = 1.0f,
            OffsetLeft = -460f,
            OffsetRight = -18f,
            OffsetTop = 250f,
            Visible = false
        };
        _telemetryPanel.AddThemeStyleboxOverride("panel", panelBg);
        _telemetryLabel = new Label();
        _telemetryLabel.AddThemeColorOverride("font_color", new Color(0.62f, 0.96f, 0.78f));
        _telemetryPanel.AddChild(_telemetryLabel);
        AddChild(_telemetryPanel);
    }

    private static void AddBarButton(HBoxContainer row, string label, Action onClick)
    {
        var btn = new Button { Text = label, FocusMode = Control.FocusModeEnum.None };
        btn.Pressed += onClick;
        row.AddChild(btn);
    }
}
