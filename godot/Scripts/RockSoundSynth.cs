using Godot;
using GroundUp.Core.Gear;
using GroundUp.Core.Geology;

namespace GroundUp.GodotClient;

/// <summary>
/// Procedural acoustic synthesizer for hammer rock-sounding (pitch/resonance driven by <see cref="RockSoundingResult"/>),
/// wire-brushing, prybar trundling, piton/cam seating, and blowout snaps. Safe in headless mode.
/// </summary>
public partial class RockSoundSynth : Node
{
    private const int MixRateHz = 22050;

    private AudioStreamPlayer? _player;
    private AudioStreamGeneratorPlayback? _playback;
    private bool _audioAvailable;

    public override void _Ready()
    {
        if (DisplayServer.GetName() == "headless")
        {
            _audioAvailable = false;
            return;
        }

        var generator = new AudioStreamGenerator
        {
            MixRate = MixRateHz,
            BufferLength = 0.35f
        };

        _player = new AudioStreamPlayer
        {
            Name = "ProceduralRockAudio",
            Stream = generator,
            VolumeDb = -6f
        };
        AddChild(_player);
        _player.Play();
        _playback = _player.GetStreamPlayback() as AudioStreamGeneratorPlayback;
        _audioAvailable = _playback is not null;
    }

    public void PlayRockSounding(RockSoundingResult sounding)
    {
        if (!_audioAvailable || _playback is null)
        {
            return;
        }

        float baseFreq = sounding.AcousticPitchHz;
        float decayRate = sounding.Resonance switch
        {
            ResonanceType.SolidRing => 16f,
            ResonanceType.SuspectDull => 26f,
            _ => 11f // Hollow boom lingers in low cavity resonance
        };

        int sampleCount = (int)(MixRateHz * 0.22f);
        int available = Mathf.Min(sampleCount, _playback.GetFramesAvailable());
        for (int i = 0; i < available; i++)
        {
            float t = (float)i / MixRateHz;
            float env = Mathf.Exp(-t * decayRate);
            float overtone = sounding.Resonance == ResonanceType.HollowBoom
                ? Mathf.Sin(Mathf.Tau * (baseFreq * 0.52f) * t) * 0.65f
                : Mathf.Sin(Mathf.Tau * (baseFreq * 2.41f) * t) * 0.35f;

            float sample = (Mathf.Sin(Mathf.Tau * baseFreq * t) + overtone) * env * 0.42f;
            _playback.PushFrame(new Vector2(sample, sample));
        }
    }

    public void PlayBrushScrape()
    {
        PushSimpleBurst(freqHz: 920f, durationSec: 0.12f, decay: 22f, noiseMix: 0.75f);
    }

    public void PlayTrundleCrash()
    {
        PushSimpleBurst(freqHz: 115f, durationSec: 0.30f, decay: 7f, noiseMix: 0.55f);
    }

    public void PlayGearPlaced(GearKind kind)
    {
        float freq = kind switch
        {
            GearKind.Piton => 1420f,
            GearKind.Cam => 780f,
            GearKind.Skyhook => 1120f,
            _ => 520f
        };
        PushSimpleBurst(freqHz: freq, durationSec: 0.11f, decay: 24f, noiseMix: 0.12f);
    }

    public void PlayGearBlowout()
    {
        PushSimpleBurst(freqHz: 310f, durationSec: 0.18f, decay: 15f, noiseMix: 0.48f);
    }

    public void PlayDrillStrike(bool isPowerDrill, float depthProgressRatio)
    {
        if (isPowerDrill)
        {
            PushSimpleBurst(freqHz: 185f + (depthProgressRatio * 60f), durationSec: 0.07f, decay: 18f, noiseMix: 0.35f);
        }
        else
        {
            // Crisp steel hammer-on-chromoly ring that shifts pitch slightly as hole deepens.
            PushSimpleBurst(freqHz: 1580f + (depthProgressRatio * 280f), durationSec: 0.085f, decay: 28f, noiseMix: 0.10f);
        }
    }

    public void PlayBlowTubePuff()
    {
        PushSimpleBurst(freqHz: 640f, durationSec: 0.18f, decay: 14f, noiseMix: 0.90f);
    }

    public void PlayWrenchRatchet(bool finishedTorque)
    {
        float freq = finishedTorque ? 1320f : 890f;
        PushSimpleBurst(freqHz: freq, durationSec: 0.09f, decay: 26f, noiseMix: 0.18f);
    }

    public void PlayQuickdrawClip()
    {
        PushSimpleBurst(freqHz: 1760f, durationSec: 0.07f, decay: 32f, noiseMix: 0.08f);
    }

    private void PushSimpleBurst(float freqHz, float durationSec, float decay, float noiseMix)
    {
        if (!_audioAvailable || _playback is null)
        {
            return;
        }

        uint rng = 0x9E3779B9u;
        int frames = Mathf.Min((int)(MixRateHz * durationSec), _playback.GetFramesAvailable());
        for (int i = 0; i < frames; i++)
        {
            rng ^= rng << 13;
            rng ^= rng >> 17;
            rng ^= rng << 5;
            float white = ((rng & 0xFFFF) / 32768f) - 1f;

            float t = (float)i / MixRateHz;
            float env = Mathf.Exp(-t * decay);
            float tone = Mathf.Sin(Mathf.Tau * freqHz * t);
            float val = Mathf.Lerp(tone, white, noiseMix) * env * 0.38f;
            _playback.PushFrame(new Vector2(val, val));
        }
    }
}
