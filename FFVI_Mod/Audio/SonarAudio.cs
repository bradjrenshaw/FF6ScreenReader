using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using MelonLoader;
using UnityEngine;

namespace FFVI_ScreenReader.Audio
{
    /// <summary>
    /// Manages audio feedback for the sonar system using NAudio.
    /// Plays continuous tones for blocked directions and entity sounds based on position.
    /// </summary>
    public class SonarAudio
    {
        /// <summary>
        /// Tracks an active entity sound player
        /// </summary>
        private class EntitySoundPlayer
        {
            public string EntityId { get; set; }
            public string SoundFile { get; set; }
            public LoopingWaveProvider LoopProvider { get; set; }
            public VolumeSampleProvider VolumeProvider { get; set; }
            public PanningSampleProvider PanProvider { get; set; }
            public bool IsActive { get; set; }
        }

        /// <summary>
        /// A wave provider that loops an audio file
        /// </summary>
        private class LoopingWaveProvider : ISampleProvider
        {
            private readonly AudioFileReader reader;
            private readonly float[] sourceBuffer;
            private bool enabled;

            public WaveFormat WaveFormat => reader.WaveFormat;
            public bool Enabled
            {
                get => enabled;
                set => enabled = value;
            }

            public LoopingWaveProvider(AudioFileReader audioReader)
            {
                reader = audioReader;
                sourceBuffer = new float[reader.WaveFormat.SampleRate * reader.WaveFormat.Channels]; // 1 second buffer
                enabled = true;
            }

            public int Read(float[] buffer, int offset, int count)
            {
                if (!enabled)
                {
                    // Return silence
                    Array.Clear(buffer, offset, count);
                    return count;
                }

                int totalRead = 0;
                while (totalRead < count)
                {
                    int toRead = Math.Min(count - totalRead, sourceBuffer.Length);
                    int read = reader.Read(sourceBuffer, 0, toRead);

                    if (read == 0)
                    {
                        // End of file - loop back
                        reader.Position = 0;
                        read = reader.Read(sourceBuffer, 0, toRead);
                        if (read == 0)
                            break; // Empty file
                    }

                    Array.Copy(sourceBuffer, 0, buffer, offset + totalRead, read);
                    totalRead += read;
                }

                return totalRead;
            }
        }

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

        // Entity sound management
        private Dictionary<string, EntitySoundPlayer> entityPlayers = new();
        private HashSet<string> activeEntityIds = new();
        private string soundsDirectory;

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

                // Set up sounds directory path
                // Use the game's directory with UserData subfolder
                string gameDir = AppDomain.CurrentDomain.BaseDirectory;
                soundsDirectory = Path.Combine(gameDir, "UserData", "FFVI_ScreenReader", "sounds");
                MelonLogger.Msg($"[SonarAudio] Sounds directory: {soundsDirectory}");

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
        /// Begins an entity sound update cycle. Call before UpdateEntitySound calls.
        /// </summary>
        public void BeginEntityUpdate()
        {
            activeEntityIds.Clear();
        }

        /// <summary>
        /// Ends an entity sound update cycle. Stops sounds for entities no longer in range.
        /// </summary>
        public void EndEntityUpdate()
        {
            // Find entities that are no longer active
            var toRemove = new List<string>();
            foreach (var kvp in entityPlayers)
            {
                if (!activeEntityIds.Contains(kvp.Key))
                {
                    // Entity went out of range - silence it
                    kvp.Value.LoopProvider.Enabled = false;
                    kvp.Value.VolumeProvider.Volume = 0f;
                    kvp.Value.IsActive = false;
                    toRemove.Add(kvp.Key);
                }
            }

            // Note: We don't remove players from mixer, just disable them
            // This avoids audio glitches from constantly adding/removing mixer inputs
        }

        /// <summary>
        /// Updates or creates an entity sound at the given position relative to the player.
        /// </summary>
        /// <param name="entityId">Unique identifier for the entity</param>
        /// <param name="soundFile">Sound file name (e.g., "npc.wav")</param>
        /// <param name="relativeX">X distance from player (positive = East)</param>
        /// <param name="relativeY">Y distance from player (positive = North)</param>
        /// <param name="maxRange">Maximum range in tiles</param>
        public void UpdateEntitySound(string entityId, string soundFile, float relativeX, float relativeY, float maxRange)
        {
            if (!IsInitialized || string.IsNullOrEmpty(soundFile))
                return;

            // Calculate distance in tiles (16 world units = 1 tile)
            float distanceInTiles = Mathf.Sqrt(relativeX * relativeX + relativeY * relativeY) / 16f;

            // Skip if out of range (don't mark as active, so it gets silenced)
            if (distanceInTiles > maxRange)
                return;

            // Only mark as active if in range
            activeEntityIds.Add(entityId);

            // Calculate volume based on distance (closer = louder)
            // Use exponential falloff for more natural perception (sound drops quickly with distance)
            float normalizedDistance = Mathf.Clamp01(distanceInTiles / maxRange);
            float falloffFactor = Mathf.Pow(1f - normalizedDistance, 2.5f); // Exponential curve
            float volume = MinVolume + (MaxVolume - MinVolume) * falloffFactor;

            // Calculate pan based on X position (-1 = left, 1 = right)
            // Use relative X position scaled by max range
            float pan = Mathf.Clamp(relativeX / (maxRange * 16f), -1f, 1f);

            // Get or create player for this entity
            if (!entityPlayers.TryGetValue(entityId, out var player))
            {
                player = CreateEntityPlayer(entityId, soundFile);
                if (player == null)
                    return;
            }
            else if (player.SoundFile != soundFile)
            {
                // Sound file changed - need to recreate
                player.LoopProvider.Enabled = false;
                player.VolumeProvider.Volume = 0f;
                player = CreateEntityPlayer(entityId, soundFile);
                if (player == null)
                    return;
            }

            // Update volume and pan
            player.VolumeProvider.Volume = volume;
            player.PanProvider.Pan = pan;
            player.LoopProvider.Enabled = true;
            player.IsActive = true;
        }

        /// <summary>
        /// Creates a new entity sound player.
        /// </summary>
        private EntitySoundPlayer CreateEntityPlayer(string entityId, string soundFile)
        {
            string soundPath = Path.Combine(soundsDirectory, soundFile);

            if (!File.Exists(soundPath))
            {
                MelonLogger.Warning($"[SonarAudio] Sound file not found: {soundPath}");
                return null;
            }

            try
            {
                var reader = new AudioFileReader(soundPath);
                MelonLogger.Msg($"[SonarAudio] Loading {soundFile}: {reader.WaveFormat.SampleRate}Hz, {reader.WaveFormat.Channels}ch");

                // Create looping provider
                var loopProvider = new LoopingWaveProvider(new AudioFileReader(soundPath));

                // Start with the loop provider
                ISampleProvider source = loopProvider;

                // Resample if needed to match mixer (44100Hz)
                if (loopProvider.WaveFormat.SampleRate != 44100)
                {
                    MelonLogger.Msg($"[SonarAudio] Resampling {soundFile} from {loopProvider.WaveFormat.SampleRate}Hz to 44100Hz");
                    source = new WdlResamplingSampleProvider(loopProvider, 44100);
                }

                // Convert to mono for panning (panning provider needs mono input)
                if (source.WaveFormat.Channels == 2)
                {
                    source = new StereoToMonoSampleProvider(source);
                }

                // Create volume provider
                var volumeProvider = new VolumeSampleProvider(source)
                {
                    Volume = 0f // Start silent
                };

                // Create panning provider
                var panProvider = new PanningSampleProvider(volumeProvider)
                {
                    Pan = 0f
                };

                // Add to mixer
                mixer.AddMixerInput(panProvider);

                var player = new EntitySoundPlayer
                {
                    EntityId = entityId,
                    SoundFile = soundFile,
                    LoopProvider = loopProvider,
                    VolumeProvider = volumeProvider,
                    PanProvider = panProvider,
                    IsActive = true
                };

                entityPlayers[entityId] = player;
                MelonLogger.Msg($"[SonarAudio] Created entity sound player for {entityId} with {soundFile}");

                return player;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[SonarAudio] Failed to create entity player for {soundFile}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Plays a test sound file directly (for debugging).
        /// </summary>
        public void PlayTestSound(string soundFile)
        {
            if (!IsInitialized)
            {
                MelonLogger.Warning("[SonarAudio] Cannot play test sound - not initialized");
                return;
            }

            string soundPath = Path.Combine(soundsDirectory, soundFile);
            MelonLogger.Msg($"[SonarAudio] Attempting to play test sound: {soundPath}");

            if (!File.Exists(soundPath))
            {
                MelonLogger.Warning($"[SonarAudio] Test sound file not found: {soundPath}");
                return;
            }

            try
            {
                var reader = new AudioFileReader(soundPath);
                MelonLogger.Msg($"[SonarAudio] Loaded sound: {reader.WaveFormat.SampleRate}Hz, {reader.WaveFormat.Channels}ch, {reader.TotalTime.TotalSeconds:F2}s");

                // Create a one-shot player
                ISampleProvider source = reader;

                // Resample if needed to match mixer (44100Hz)
                if (reader.WaveFormat.SampleRate != 44100)
                {
                    MelonLogger.Msg($"[SonarAudio] Resampling from {reader.WaveFormat.SampleRate}Hz to 44100Hz");
                    source = new WdlResamplingSampleProvider(source, 44100);
                }

                // Convert to stereo if mono
                if (source.WaveFormat.Channels == 1)
                {
                    source = new MonoToStereoSampleProvider(source);
                }

                var volumeProvider = new VolumeSampleProvider(source)
                {
                    Volume = 1.0f
                };

                mixer.AddMixerInput(volumeProvider);
                MelonLogger.Msg("[SonarAudio] Test sound added to mixer and playing");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[SonarAudio] Failed to play test sound: {ex.Message}");
            }
        }

        /// <summary>
        /// Stops all tones immediately.
        /// </summary>
        public void StopAll()
        {
            if (!IsInitialized)
                return;

            // Stop direction tones
            foreach (var direction in volumeProviders.Keys)
            {
                volumeProviders[direction].Volume = 0f;
                isPlaying[direction] = false;
            }

            // Stop entity sounds
            foreach (var player in entityPlayers.Values)
            {
                player.LoopProvider.Enabled = false;
                player.VolumeProvider.Volume = 0f;
                player.IsActive = false;
            }
            activeEntityIds.Clear();
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
            entityPlayers.Clear();
            activeEntityIds.Clear();
        }
    }
}
