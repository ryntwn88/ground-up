using Godot;
using GroundUp.Core.Climbing;
using GroundUp.Core.Gear;
using GroundUp.Core.Geology;
using GroundUp.Core.Physics;
using GroundUp.Core.Samples;

namespace GroundUp.GodotClient;

/// <summary>
/// Phase 1 playable scene controller ("The 10-Meter Graybox Wall"):
/// connects player input, 3D feature raycasting, IK climber visuals, rope physics, close-up inspection camera,
/// procedural rock-sounding audio, and dynamic whippers to <see cref="ClimbingSession"/>.
/// </summary>
public partial class GrayboxSandboxController : Node3D
{
    private CliffFace _cliff = null!;
    private ClimbingSession _session = null!;

    private CliffGrayboxBuilder _cliffBuilder = null!;
    private ClimberVisualRig _climberRig = null!;
    private DynamicRopeRenderer _ropeRenderer = null!;
    private HybridClimbingCamera _cameraRig = null!;
    private WhipperAnimator _whipperAnimator = null!;
    private RockSoundSynth _soundSynth = null!;
    private ClimbingHudController _hud = null!;

    private LimbSlot? _manualLimbOverride;
    private GearKind _selectedGearCategory = GearKind.Cam;
    private int _selectedGearIndexInCategory = 5; // Default to #0.75 Green Cam
    private bool _isRightMouseOrbiting;

    private string? _hoveredFeatureId;
    private float _hoveredNormalizedPos = 0.5f;
    private Vector3? _hoveredWorldPoint;
    private string? _lockedInspectFeatureId;

    public override void _Ready()
    {
        InitializeSimulation();
        SetupEnvironmentAndLighting();

        _cliffBuilder = new CliffGrayboxBuilder { Name = "CliffGrayboxBuilder" };
        AddChild(_cliffBuilder);
        _cliffBuilder.BuildCliff(_cliff);

        _climberRig = new ClimberVisualRig { Name = "ClimberVisualRig" };
        AddChild(_climberRig);

        _ropeRenderer = new DynamicRopeRenderer { Name = "DynamicRopeRenderer" };
        AddChild(_ropeRenderer);

        _cameraRig = new HybridClimbingCamera { Name = "HybridClimbingCamera" };
        AddChild(_cameraRig);

        _whipperAnimator = new WhipperAnimator { Name = "WhipperAnimator" };
        AddChild(_whipperAnimator);

        _soundSynth = new RockSoundSynth { Name = "RockSoundSynth" };
        AddChild(_soundSynth);

        _hud = new ClimbingHudController { Name = "ClimbingHUD" };
        _hud.ToggleStanceRequested += HandleToggleStance;
        _hud.WeightGearRequested += HandleWeightGear;
        _hud.ResetClimbRequested += HandleFullReset;
        _hud.HotkeySlotPressed += HandleToolSlotSelected;
        AddChild(_hud);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Right)
            {
                _isRightMouseOrbiting = mb.Pressed;
                return;
            }

            if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp)
            {
                if (_session.SelectedTool == ActiveToolKind.PlaceGear)
                {
                    CycleGearInCategory(+1);
                }
                else
                {
                    _cameraRig.AdjustZoom(+1f);
                }

                return;
            }

            if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown)
            {
                if (_session.SelectedTool == ActiveToolKind.PlaceGear)
                {
                    CycleGearInCategory(-1);
                }
                else
                {
                    _cameraRig.AdjustZoom(-1f);
                }

                return;
            }

            if (mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                HandlePrimaryClick();
                return;
            }
        }

        if (@event is InputEventMouseMotion motion && _isRightMouseOrbiting)
        {
            _cameraRig.HandleOrbitDrag(motion.Relative);
            return;
        }

        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            HandleKeyPress(key.Keycode);
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        // Vertical scouting look-ahead with W / S keys.
        float verticalInput = 0f;
        if (Input.IsPhysicalKeyPressed(Key.W))
        {
            verticalInput += 3.2f * dt;
        }

        if (Input.IsPhysicalKeyPressed(Key.S))
        {
            verticalInput -= 3.2f * dt;
        }

        if (Mathf.Abs(verticalInput) > 0.0001f)
        {
            _cameraRig.AdjustVerticalLookOffset(verticalInput);
        }

        UpdateMouseHoverRaycast();

        SessionMode prevMode = _session.Mode;
        _session.AdvanceTime(dt);
        if (prevMode != SessionMode.WhipperFall && _session.Mode == SessionMode.WhipperFall)
        {
            _whipperAnimator.BeginWhipper(_session, _climberRig.FocusPelvisWorldPosition, _soundSynth);
        }

        if (_session.Mode == SessionMode.WhipperFall)
        {
            if (!_whipperAnimator.IsPlayingWhipper)
            {
                _whipperAnimator.BeginWhipper(_session, _climberRig.FocusPelvisWorldPosition, _soundSynth);
            }

            _whipperAnimator.TickWhipper(_session, dt);
            _climberRig.SetOverridePelvisPosition(_cliff, _session, _whipperAnimator.CurrentFallingPelvisWorld, dt);
        }
        else
        {
            _climberRig.SyncFromSession(_session, _hoveredWorldPoint, dt);
        }

        bool ropeTaut = _session.Mode == SessionMode.HangingOnGear || _whipperAnimator.IsRopeCurrentlyTaut;
        _ropeRenderer.SyncRopeAndGear(_session, _climberRig.HarnessWorldPosition, ropeTaut);

        Vector3? inspectFocus = ResolveCloseUpCameraFocus();
        _cameraRig.UpdateCamera(_session, _climberRig.FocusPelvisWorldPosition, inspectFocus, dt);

        _cliffBuilder.TickDebris(dt);
        _cliffBuilder.RefreshVisuals(_cliff, _session, _hoveredFeatureId, _hoveredNormalizedPos);

        RockFeatureBase? hoveredFeature = _hoveredFeatureId is not null ? _cliff.FindFeature(_hoveredFeatureId) : null;
        PlacementEvaluation? hoveredEval = (_session.SelectedTool == ActiveToolKind.PlaceGear && _session.SelectedGear is not null && hoveredFeature is not null)
            ? _session.PreviewPlacement(_session.SelectedGear, hoveredFeature.Id, _hoveredNormalizedPos)
            : null;

        _hud.UpdateHud(_session, _manualLimbOverride, hoveredFeature, _hoveredNormalizedPos, hoveredEval);
    }

    private void InitializeSimulation()
    {
        _cliff = TestCliffFactory.CreateTenMeterGraniteWall();
        _session = new ClimbingSession(_cliff);
        _manualLimbOverride = null;
        _lockedInspectFeatureId = null;
    }

    private void HandlePrimaryClick()
    {
        if (_session.Mode is SessionMode.WhipperFall or SessionMode.ToppedOut)
        {
            return;
        }

        if (_hoveredFeatureId is null)
        {
            return;
        }

        if (_session.Mode == SessionMode.Grounded)
        {
            _session.TryStepOntoWallFromGround();
            _cameraRig.ResetScoutOffset();
            return;
        }

        if (_session.Mode is SessionMode.FreeClimbing or SessionMode.HangingOnGear &&
            _session.SelectedTool == ActiveToolKind.None)
        {
            RockFeatureBase? clickedFeature = _cliff.FindFeature(_hoveredFeatureId);
            bool wasChoss = clickedFeature is not null && clickedFeature.IsHollowChoss && !clickedFeature.IsTrundled;
            Vector3? chossWorld = clickedFeature is not null ? _cliff.GetFeatureWorldPositionMeters(clickedFeature, _hoveredNormalizedPos).ToGodot() : null;

            _session.TryMoveLimbToFeature(_hoveredFeatureId, _hoveredNormalizedPos, _manualLimbOverride);
            _cameraRig.ResetScoutOffset();

            if (wasChoss && clickedFeature!.IsTrundled && chossWorld.HasValue)
            {
                _cliffBuilder.SpawnTrundleDebris(chossWorld.Value);
                _soundSynth.PlayTrundleCrash();
            }

            if (_session.Mode == SessionMode.WhipperFall)
            {
                _whipperAnimator.BeginWhipper(_session, _climberRig.FocusPelvisWorldPosition, _soundSynth);
            }

            return;
        }

        // In StanceInspect / CleaningAndGear (or HangingOnGear with a tool active):
        _lockedInspectFeatureId = _hoveredFeatureId;
        switch (_session.SelectedTool)
        {
            case ActiveToolKind.HammerSound:
                if (_session.TrySoundFeature(_hoveredFeatureId, out RockSoundingResult sound))
                {
                    _soundSynth.PlayRockSounding(sound);
                }
                break;

            case ActiveToolKind.WireBrush:
                if (_session.TryBrushFeature(_hoveredFeatureId, brushEffort: 0.70f))
                {
                    _soundSynth.PlayBrushScrape();
                }
                break;

            case ActiveToolKind.Prybar:
                RockFeatureBase? target = _cliff.FindFeature(_hoveredFeatureId);
                Vector3 debrisPos = target is not null
                    ? _cliff.GetFeatureWorldPositionMeters(target, 0.5f).ToGodot()
                    : _climberRig.FocusPelvisWorldPosition;
                if (_session.TryTrundleFeature(_hoveredFeatureId))
                {
                    _cliffBuilder.SpawnTrundleDebris(debrisPos);
                    _soundSynth.PlayTrundleCrash();
                }
                break;

            case ActiveToolKind.PlaceGear when _session.SelectedGear is not null:
                if (_session.TryPlaceProtection(_session.SelectedGear, _hoveredFeatureId, _hoveredNormalizedPos, out _, out _))
                {
                    _soundSynth.PlayGearPlaced(_session.SelectedGear.Kind);
                }
                break;

            default:
                if (_session.TrySoundFeature(_hoveredFeatureId, out RockSoundingResult knuckleTap))
                {
                    _soundSynth.PlayRockSounding(knuckleTap);
                }
                break;
        }
    }

    private void HandleKeyPress(Key keycode)
    {
        switch (keycode)
        {
            case Key.F:
                HandleToggleStance();
                break;
            case Key.G:
                HandleWeightGear();
                break;
            case Key.R:
                HandleFullReset();
                break;
            case Key.F3:
                _hud.ShowTelemetry = !_hud.ShowTelemetry;
                break;
            case Key.H:
                _hud.ShowControlsHelp = !_hud.ShowControlsHelp;
                break;
            case Key.Escape:
                _manualLimbOverride = null;
                break;
            case Key.Q:
                ToggleLimbOverride(LimbSlot.LeftHand);
                break;
            case Key.E:
                ToggleLimbOverride(LimbSlot.RightHand);
                break;
            case Key.Z:
                ToggleLimbOverride(LimbSlot.LeftFoot);
                break;
            case Key.C:
                ToggleLimbOverride(LimbSlot.RightFoot);
                break;
            case Key.Key1:
                HandleToolSlotSelected(1);
                break;
            case Key.Key2:
                HandleToolSlotSelected(2);
                break;
            case Key.Key3:
                HandleToolSlotSelected(3);
                break;
            case Key.Key4:
                HandleToolSlotSelected(4);
                break;
            case Key.Key5:
                HandleToolSlotSelected(5);
                break;
            case Key.Key6:
                HandleToolSlotSelected(6);
                break;
            case Key.Key7:
                HandleToolSlotSelected(7);
                break;
            case Key.Bracketleft:
                CycleGearInCategory(-1);
                break;
            case Key.Bracketright:
                CycleGearInCategory(+1);
                break;
        }
    }

    private void ToggleLimbOverride(LimbSlot slot)
    {
        _manualLimbOverride = _manualLimbOverride == slot ? null : slot;
    }

    private void HandleToggleStance()
    {
        if (_session.Mode == SessionMode.Grounded)
        {
            _session.TryStepOntoWallFromGround();
            return;
        }

        if (_session.Mode == SessionMode.FreeClimbing)
        {
            _lockedInspectFeatureId = _hoveredFeatureId;
            _session.TryLockThreePointStance();
        }
        else if (_session.Mode is SessionMode.StanceInspect or SessionMode.CleaningAndGear or SessionMode.HangingOnGear)
        {
            _lockedInspectFeatureId = null;
            _session.ReturnToFreeClimbing();
        }
    }

    private void HandleWeightGear()
    {
        bool held = _session.TryWeightPlacedGear();
        if (!held && _session.Mode == SessionMode.WhipperFall)
        {
            _soundSynth.PlayGearBlowout();
            _whipperAnimator.BeginWhipper(_session, _climberRig.FocusPelvisWorldPosition, _soundSynth);
        }
    }

    private void HandleFullReset()
    {
        InitializeSimulation();
        _cliffBuilder.BuildCliff(_cliff);
        _cameraRig.ResetScoutOffset();
    }

    private void HandleToolSlotSelected(int slot)
    {
        if (_session.Mode == SessionMode.FreeClimbing)
        {
            _session.TryLockThreePointStance();
        }

        switch (slot)
        {
            case 1:
                _session.SelectWorkTool(ActiveToolKind.HammerSound);
                break;
            case 2:
                _session.SelectWorkTool(ActiveToolKind.WireBrush);
                break;
            case 3:
                _session.SelectWorkTool(ActiveToolKind.Prybar);
                break;
            case 4:
                SelectGearCategory(GearKind.Cam);
                break;
            case 5:
                SelectGearCategory(GearKind.Piton);
                break;
            case 6:
                SelectGearCategory(GearKind.Skyhook);
                break;
            case 7:
                SelectGearCategory(GearKind.Sling);
                break;
        }
    }

    private void SelectGearCategory(GearKind kind)
    {
        _selectedGearCategory = kind;
        IReadOnlyList<RackEntry> entries = _session.Rack.GetEntriesByKind(kind);
        if (entries.Count == 0)
        {
            return;
        }

        _selectedGearIndexInCategory = Mathf.Clamp(_selectedGearIndexInCategory, 0, entries.Count - 1);
        _session.SelectWorkTool(ActiveToolKind.PlaceGear, entries[_selectedGearIndexInCategory].Gear);
    }

    private void CycleGearInCategory(int delta)
    {
        IReadOnlyList<RackEntry> entries = _session.Rack.GetEntriesByKind(_selectedGearCategory);
        if (entries.Count == 0)
        {
            return;
        }

        _selectedGearIndexInCategory = (_selectedGearIndexInCategory + delta + entries.Count) % entries.Count;
        _session.SelectWorkTool(ActiveToolKind.PlaceGear, entries[_selectedGearIndexInCategory].Gear);
    }

    private void UpdateMouseHoverRaycast()
    {
        if (DisplayServer.GetName() == "headless")
        {
            _hoveredFeatureId = null;
            _hoveredWorldPoint = null;
            return;
        }

        Viewport viewport = GetViewport();
        Vector2 mousePos = viewport.GetMousePosition();
        Vector3 from = _cameraRig.Camera.ProjectRayOrigin(mousePos);
        Vector3 to = from + (_cameraRig.Camera.ProjectRayNormal(mousePos) * 28f);

        var query = PhysicsRayQueryParameters3D.Create(from, to, CliffGrayboxBuilder.FeatureCollisionLayer);
        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(query);

        if (hit.Count > 0 && hit["collider"].AsGodotObject() is Node collider && collider.HasMeta("feature_id"))
        {
            string id = collider.GetMeta("feature_id").AsString();
            Vector3 worldHit = hit["position"].AsVector3();
            RockFeatureBase? feat = _cliff.FindFeature(id);
            if (feat is not null)
            {
                _hoveredFeatureId = id;
                _hoveredNormalizedPos = _cliffBuilder.GetNormalizedPositionFromWorldHit(feat, worldHit);
                _hoveredWorldPoint = _cliff.GetFeatureWorldPositionMeters(feat, _hoveredNormalizedPos).ToGodot();
                return;
            }
        }

        _hoveredFeatureId = null;
        _hoveredWorldPoint = null;
    }

    private Vector3? ResolveCloseUpCameraFocus()
    {
        if (_hoveredWorldPoint.HasValue)
        {
            return _hoveredWorldPoint.Value;
        }

        if (_lockedInspectFeatureId is not null && _cliff.FindFeature(_lockedInspectFeatureId) is RockFeatureBase feat)
        {
            return _cliff.GetFeatureWorldPositionMeters(feat, 0.5f).ToGodot();
        }

        return null;
    }

    private void SetupEnvironmentAndLighting()
    {
        var sun = new DirectionalLight3D
        {
            Name = "SunLight",
            RotationDegrees = new Vector3(-42f, 32f, 0f),
            ShadowEnabled = true,
            LightEnergy = 1.25f
        };
        AddChild(sun);

        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.11f, 0.15f, 0.20f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.40f, 0.44f, 0.50f),
            AmbientLightEnergy = 0.68f
        };
        AddChild(new WorldEnvironment { Environment = env });
    }
}
