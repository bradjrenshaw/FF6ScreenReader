using System;
using System.Collections.Generic;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using MelonLoader;

namespace FFVI_ScreenReader.Audio
{
    /// <summary>
    /// Manages audio feedback for the sonar system using NAudio.
    /// Plays continuous tones for blocked directions, with volume based on distance.
    /// </summary>
    public class SonarAudio
    {
        // Frequencies for each direction (in Hz) - chosen to be distinct but harmonious
        private static readonly Dictionary<CardinalDirection, float> DirectionFrequencies = new()
        {
            { CardinalDirection.North, 800f },
            { CardinalDirection.South, 500f },
            { CardinalDirection.East, 700f },
            { CardinalDirection.West, 600f }
        };

        // NAudio output device
        private WaveOutEvent waveOut;

        // Mixing provider to combine multiple tones
        private MixingSampleProvider mixer;

        // Signal generators for each direction
        private Dictionary<CardinalDirection, SignalGenerator> signalGenerators = new();

        // Volume providers for each direction
        private Dictionary<CardinalDirection, VolumeSampleProvider> volumeProviders = new();

        // Panning providers for left/right positioning
        private Dictionary<CardinalDirection, PanningSampleProvider> panningProviders = new();

        // Track which directions are currently playing
        private Dictionary<CardinalDirection, bool> isPlaying = new();

        // Panning values: -1 = full left, 0 = center, 1 = full right
        private static readonly Dictionary<CardinalDirection, float> DirectionPanning = new()
        {
            { CardinalDirection.North, 0f },    // Center
            { CardinalDirection.South, 0f },    // Center
            { CardinalDirection.East, 0.8f },   // Right
            { CardinalDirection.West, -0.8f }   // Left
        };

        // Maximum range for volume calculation (matches SonarSystem.MaxRange)
        private const float MaxRange = 48f;

        // Minimum volume (at max range)
        private const float MinVolume = 0.1f;

        // Maximum volume (at closest range)
        private const float MaxVolume = 1.0f;

        /// <summary>
        /// Whether the sonar audio system has been initialized.
        /// </summary>
        public bool IsInitialized => waveOut != null;

        /// <summary>
        /// Initializes the sonar audio system.
        /// Creates signal generators for each direction.
        /// </summary>
        public void Initialize()
        {
            if (IsInitialized)
                return;

            try
            {
                MelonLogger.Msg("[SonarAudio] Initializing with NAudio...");

                // Create wave output with low latency
                waveOut = new WaveOutEvent
                {
                    DesiredLatency = 50 // 50ms latency - lowest stable value without crackling
                };

                // Create mixer (44100 Hz, stereo)
                mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));
                mixer.ReadFully = true;

                // Create signal generators for each direction
                foreach (var kvp in DirectionFrequencies)
                {
                    var direction = kvp.Key;
                    var frequency = kvp.Value;

                    // Create sine wave generator (mono, will be converted to stereo by panning)
                    var signal = new SignalGenerator(44100, 1)
                    {
                        Frequency = frequency,
                        Type = SignalGeneratorType.Sin,
                        Gain = 0.08 // Reduced gain - was too loud at 0.3
                    };

                    // Wrap in volume provider for per-direction volume control
                    var volumeProvider = new VolumeSampleProvider(signal)
                    {
                        Volume = 0f // Start silent
                    };

                    // Add panning for stereo positioning
                    var panningProvider = new PanningSampleProvider(volumeProvider)
                    {
                        Pan = DirectionPanning[direction]
                    };

                    signalGenerators[direction] = signal;
                    volumeProviders[direction] = volumeProvider;
                    panningProviders[direction] = panningProvider;
                    isPlaying[direction] = false;

                    // Add panned output to mixer
                    mixer.AddMixerInput(panningProvider);

                    MelonLogger.Msg($"[SonarAudio] Created generator for {direction} at {frequency}Hz, pan={DirectionPanning[direction]}");
                }

                // Start playback
                waveOut.Init(mixer);
                waveOut.Play();

                MelonLogger.Msg("[SonarAudio] Initialization complete, playback started");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[SonarAudio] Failed to initialize: {ex.Message}");
                Cleanup();
            }
        }

        /// <summary>
        /// Updates the audio for a single direction based on scan results.
        /// </summary>
        /// <param name="direction">The direction to update</param>
        /// <param name="isBlocked">Whether an obstacle was detected</param>
        /// <param name="distance">Distance to the obstacle (if blocked)</param>
        public void UpdateDirection(CardinalDirection direction, bool isBlocked, float distance)
        {
            if (!IsInitialized || !volumeProviders.ContainsKey(direction))
                return;

            var volumeProvider = volumeProviders[direction];

            if (isBlocked)
            {
                // Calculate volume based on distance (closer = louder)
                float normalizedDistance = Math.Clamp(distance / MaxRange, 0f, 1f);
                float volume = Lerp(MaxVolume, MinVolume, normalizedDistance);

                volumeProvider.Volume = volume;

                // Update panning for East/West based on distance
                // 1 tile (16 units) = 0.4, 2 tiles (32 units) = 0.7, 3 tiles (48 units) = 1.0
                if (direction == CardinalDirection.East || direction == CardinalDirection.West)
                {
                    float tiles = Math.Clamp(distance / 16f, 1f, 3f);
                    float panMagnitude = 0.4f + (tiles - 1f) * 0.3f; // 0.4 at 1 tile, 1.0 at 3 tiles
                    float pan = direction == CardinalDirection.East ? panMagnitude : -panMagnitude;
                    panningProviders[direction].Pan = pan;
                }

                if (!isPlaying[direction])
                {
                    MelonLogger.Msg($"[SonarAudio] {direction} now blocked at distance {distance:F1}, volume {volume:F2}");
                    isPlaying[direction] = true;
                }
            }
            else
            {
                // Direction is clear - silence the tone
                if (isPlaying[direction])
                {
                    MelonLogger.Msg($"[SonarAudio] {direction} now clear");
                    isPlaying[direction] = false;
                }
                volumeProvider.Volume = 0f;
            }
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        /// <summary>
        /// Updates all directions from a scan result.
        /// </summary>
        /// <param name="result">The sonar scan result</param>
        public void UpdateFromScanResult(SonarScanResult result)
        {
            if (result == null)
            {
                StopAll();
                return;
            }

            UpdateDirection(CardinalDirection.North, result.North.IsBlocked, result.North.Distance);
            UpdateDirection(CardinalDirection.South, result.South.IsBlocked, result.South.Distance);
            UpdateDirection(CardinalDirection.East, result.East.IsBlocked, result.East.Distance);
            UpdateDirection(CardinalDirection.West, result.West.IsBlocked, result.West.Distance);
        }

        /// <summary>
        /// Stops all tones immediately.
        /// </summary>
        public void StopAll()
        {
            if (!IsInitialized)
                return;

            foreach (var direction in volumeProviders.Keys)
            {
                volumeProviders[direction].Volume = 0f;
                isPlaying[direction] = false;
            }
        }

        /// <summary>
        /// Cleans up audio resources.
        /// </summary>
        public void Cleanup()
        {
            StopAll();

            if (waveOut != null)
            {
                try
                {
                    waveOut.Stop();
                    waveOut.Dispose();
                }
                catch { }
                waveOut = null;
            }

            mixer = null;
            signalGenerators.Clear();
            volumeProviders.Clear();
            panningProviders.Clear();
            isPlaying.Clear();
        }
    }
}
