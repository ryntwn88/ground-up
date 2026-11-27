using Godot;
using GroundUp.Core.Climbing;
using GroundUp.Core.Physics;

namespace GroundUp.GodotClient;

/// <summary>
/// Plays back <see cref="FallSimulationResult"/> trajectories in real time: gravitational free-fall,
/// sequential blown-piece pop FX, and dynamic rope stretch bounce before settling.
/// </summary>
public partial class WhipperAnimator : Node3D
{
    private Vector3 _startPelvisWorld;
    private float _elapsedSeconds;
    private float _freeFallDurationSeconds;
    private float _totalDurationSeconds;
    private int _blownPoppedCount;
    private RockSoundSynth? _soundSynth;

    public bool IsPlayingWhipper { get; private set; }
    public Vector3 CurrentFallingPelvisWorld { get; private set; }
    public bool IsRopeCurrentlyTaut { get; private set; }

    public void BeginWhipper(ClimbingSession session, Vector3 startPelvisWorld, RockSoundSynth? soundSynth)
    {
        if (session.LastFallResult is null)
        {
            return;
        }

        FallSimulationResult fall = session.LastFallResult;
        _startPelvisWorld = startPelvisWorld;
        CurrentFallingPelvisWorld = startPelvisWorld;
        _elapsedSeconds = 0f;
        _blownPoppedCount = 0;
        _soundSynth = soundSynth;

        float dropMeters = Mathf.Max(0.35f, fall.TotalFallDistanceMeters);
        _freeFallDurationSeconds = Mathf.Sqrt(2f * dropMeters / 9.81f);
        _totalDurationSeconds = fall.Outcome == FallOutcome.CaughtByGear
            ? _freeFallDurationSeconds + 0.75f
            : _freeFallDurationSeconds + 0.45f;

        IsPlayingWhipper = true;
        IsRopeCurrentlyTaut = false;
    }

    public void TickWhipper(ClimbingSession session, float deltaSeconds)
    {
        if (!IsPlayingWhipper || session.LastFallResult is null)
        {
            return;
        }

        FallSimulationResult fall = session.LastFallResult;
        _elapsedSeconds += deltaSeconds;

        float targetEndY = fall.Outcome == FallOutcome.CaughtByGear
            ? fall.FinalRestingHeightMeters
            : 0.55f;

        if (_elapsedSeconds <= _freeFallDurationSeconds)
        {
            float tNorm = _elapsedSeconds / _freeFallDurationSeconds;
            float currentY = Mathf.Lerp(_startPelvisWorld.Y, targetEndY, tNorm * tNorm);
            CurrentFallingPelvisWorld = new Vector3(_startPelvisWorld.X, currentY, _startPelvisWorld.Z + 0.18f);
            IsRopeCurrentlyTaut = tNorm > 0.78f;

            // Fire blowout sound cues as the falling climber zippers through marginal pieces.
            if (_blownPoppedCount < fall.BlownPieces.Count && tNorm >= (0.35f + (_blownPoppedCount * 0.22f)))
            {
                _blownPoppedCount++;
                _soundSynth?.PlayGearBlowout();
            }
        }
        else
        {
            IsRopeCurrentlyTaut = fall.Outcome == FallOutcome.CaughtByGear;
            float bounceTime = _elapsedSeconds - _freeFallDurationSeconds;
            float bounceOffset = fall.Outcome == FallOutcome.CaughtByGear
                ? -Mathf.Sin(bounceTime * 11f) * Mathf.Exp(-bounceTime * 4.5f) * 0.32f
                : 0f;

            CurrentFallingPelvisWorld = new Vector3(
                _startPelvisWorld.X,
                Mathf.Max(0.45f, targetEndY + bounceOffset),
                _startPelvisWorld.Z + 0.14f);
        }

        if (_elapsedSeconds >= _totalDurationSeconds)
        {
            IsPlayingWhipper = false;
            IsRopeCurrentlyTaut = false;
            session.CompleteWhipperAftermath();
        }
    }
}
