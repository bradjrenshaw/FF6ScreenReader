using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Il2Cpp;
using Il2CppLast.Entity.Field;
using Il2CppLast.Map;
using Il2CppLast.Message;
using MelonLoader;
using FFVI_ScreenReader.Core;
using FFVI_ScreenReader.Core.Systems;
using FFVI_ScreenReader.Field;
using FFVI_ScreenReader.Utils;

namespace FFVI_ScreenReader.Audio
{
    /// <summary>
    /// Represents a cardinal direction for sonar scanning.
    /// </summary>
    public enum CardinalDirection
    {
        North,
        South,
        East,
        West
    }

    /// <summary>
    /// Result of a sonar scan in a single direction.
    /// </summary>
    public class SonarHit
    {
        /// <summary>
        /// Direction of the scan
        /// </summary>
        public CardinalDirection Direction { get; set; }

        /// <summary>
        /// Whether an obstacle was detected
        /// </summary>
        public bool IsBlocked { get; set; }

        /// <summary>
        /// Distance to the obstacle in world units (16 units = 1 tile)
        /// </summary>
        public float Distance { get; set; }

        /// <summary>
        /// Distance in tiles
        /// </summary>
        public float DistanceInTiles => Distance / 16f;

        /// <summary>
        /// The collider that was hit (if any)
        /// </summary>
        public Collider2D Collider { get; set; }
    }

    /// <summary>
    /// Result of a full cardinal sonar scan (all 4 directions).
    /// </summary>
    public class SonarScanResult
    {
        public SonarHit North { get; set; }
        public SonarHit South { get; set; }
        public SonarHit East { get; set; }
        public SonarHit West { get; set; }

        /// <summary>
        /// Gets all hits as an enumerable
        /// </summary>
        public IEnumerable<SonarHit> AllHits
        {
            get
            {
                yield return North;
                yield return South;
                yield return East;
                yield return West;
            }
        }
    }

    /// <summary>
    /// Provides sonar-like detection of obstacles and entities around the player.
    /// Uses raycasting to detect blocking tiles in cardinal directions.
    /// Supports continuous scanning with audio feedback.
    /// Implements ISystem for lifecycle management.
    /// </summary>
    public class SonarSystem : ISystem
    {
        // ISystem implementation
        public string Name => "Sonar";
        public int Priority => 100; // Run after entity systems

        /// <summary>
        /// Whether the user has enabled sonar mode (hotkey toggle).
        /// </summary>
        public bool UserEnabled { get; private set; }

        /// <summary>
        /// System is active when user has enabled it AND we're on a field map.
        /// </summary>
        public bool IsActive => UserEnabled && IsOnFieldMap();

        /// <summary>
        /// Maximum range for wall detection in world units (16 units = 1 tile)
        /// </summary>
        public float MaxRange { get; set; } = 48f; // 3 tiles

        /// <summary>
        /// Audio manager for playing sonar tones
        /// </summary>
        private SonarAudio sonarAudio;

        /// <summary>
        /// Reference to entity cache for blocking checks
        /// </summary>
        private EntityCache entityCache;

        /// <summary>
        /// Debug: tracks last logged entity count to avoid spam
        /// </summary>
        private int lastLoggedEntityCount = -1;

        /// <summary>
        /// Current scene name
        /// </summary>
        private string currentScene = "";

        /// <summary>
        /// Direction vectors for cardinal directions
        /// </summary>
        private static readonly Dictionary<CardinalDirection, Vector2> DirectionVectors = new()
        {
            { CardinalDirection.North, new Vector2(0, 1) },
            { CardinalDirection.South, new Vector2(0, -1) },
            { CardinalDirection.East, new Vector2(1, 0) },
            { CardinalDirection.West, new Vector2(-1, 0) }
        };

        /// <summary>
        /// Creates a new SonarSystem instance.
        /// </summary>
        public SonarSystem(EntityCache entityCache)
        {
            this.entityCache = entityCache;
            sonarAudio = new SonarAudio();
        }

        /// <summary>
        /// Checks if we're currently on a field map.
        /// </summary>
        private bool IsOnFieldMap()
        {
            // Check if scene looks like a field map
            if (string.IsNullOrEmpty(currentScene))
                return false;

            // Field scenes typically start with "field" or similar
            // Also check for player controller availability
            var playerController = GameObjectCache.Get<FieldPlayerController>();
            return playerController?.fieldPlayer != null;
        }

        /// <summary>
        /// Checks if audio should be temporarily muted (dialogs, cutscenes).
        /// </summary>
        private bool ShouldMuteAudio()
        {
            // Check if a message window is open (dialog/cutscene text)
            try
            {
                var messageManager = MessageWindowManager.Instance;
                if (messageManager != null && messageManager.IsOpen())
                    return true;
            }
            catch { }

            // Check if field map is in a playable state (not during cutscenes/events)
            try
            {
                var fieldMap = GameObjectCache.Get<FieldMap>();
                if (fieldMap != null && !fieldMap.IsPlayable())
                    return true;
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Toggles sonar mode on/off (user preference).
        /// </summary>
        /// <returns>True if sonar is now enabled, false if disabled</returns>
        public bool Toggle()
        {
            UserEnabled = !UserEnabled;
            return UserEnabled;
        }

        /// <summary>
        /// Called when the system becomes active.
        /// </summary>
        public void OnActivate()
        {
            MelonLogger.Msg("[SonarSystem] Activating");

            // Initialize audio if not already done
            if (!sonarAudio.IsInitialized)
            {
                sonarAudio.Initialize();
            }
        }

        /// <summary>
        /// Called when the system becomes inactive.
        /// </summary>
        public void OnDeactivate()
        {
            MelonLogger.Msg("[SonarSystem] Deactivating");
            sonarAudio.StopAll();
        }

        /// <summary>
        /// Called when the scene changes.
        /// </summary>
        public void OnSceneChanged(string sceneName)
        {
            currentScene = sceneName;
            // Audio will be stopped by OnDeactivate if we leave a field map
        }

        /// <summary>
        /// Handles input for sonar system (toggle hotkey).
        /// Note: This is called even when system is inactive to allow toggling on.
        /// </summary>
        /// <returns>True if input was consumed</returns>
        public bool HandleInput()
        {
            // Sonar doesn't handle input - toggle is handled at a higher level
            // since it needs to work even when the system is inactive
            return false;
        }

        /// <summary>
        /// Updates the sonar system each frame.
        /// </summary>
        public void Update()
        {
            // Check for temporary muting (dialogs, cutscenes)
            if (ShouldMuteAudio())
            {
                sonarAudio.StopAll();
                return;
            }

            // Perform wall scan
            var result = ScanCardinalDirections();

            // Update wall audio based on scan results
            sonarAudio.UpdateFromScanResult(result);

            // Update entity sounds
            UpdateEntitySounds();
        }

        /// <summary>
        /// Updates entity sounds based on position relative to player.
        /// </summary>
        private void UpdateEntitySounds()
        {
            var playerController = GameObjectCache.Get<FieldPlayerController>();
            if (playerController?.fieldPlayer == null)
                return;

            Vector3 playerPos = playerController.fieldPlayer.transform.position;

            sonarAudio.BeginEntityUpdate();

            // Get unique entities (avoid duplicates from grouped entities)
            var uniqueEntities = entityCache.Entities.Values.Distinct().ToList();

            // Debug: log entity types periodically when count changes
            if (uniqueEntities.Count != lastLoggedEntityCount)
            {
                lastLoggedEntityCount = uniqueEntities.Count;
                var typeCounts = uniqueEntities
                    .GroupBy(e => e.GetType().Name)
                    .Select(g => $"{g.Key}:{g.Count()}")
                    .ToList();
                var sonarCounts = uniqueEntities
                    .GroupBy(e => e.SonarInfo?.Mode.ToString() ?? "null")
                    .Select(g => $"{g.Key}:{g.Count()}")
                    .ToList();
                MelonLogger.Msg($"[SonarSystem] Entities: {uniqueEntities.Count} - Types: {string.Join(", ", typeCounts)} - Sonar: {string.Join(", ", sonarCounts)}");
            }

            foreach (var entity in uniqueEntities)
            {
                var sonarInfo = entity.SonarInfo;
                if (sonarInfo == null || sonarInfo.Mode == SonarMode.Silent)
                    continue;

                // BlockingTerrain entities are handled by wall raycasting
                if (sonarInfo.Mode == SonarMode.BlockingTerrain)
                    continue;

                // Continuous mode - play the entity's sound file
                if (sonarInfo.Mode == SonarMode.Continuous && !string.IsNullOrEmpty(sonarInfo.Sound))
                {
                    // Get entity position (use world position, same as player)
                    Vector3 entityPos = entity.Position;

                    // Calculate relative position (from player to entity)
                    float relativeX = entityPos.x - playerPos.x;  // Positive = East
                    float relativeY = entityPos.y - playerPos.y;  // Positive = North

                    // Use entity's unique ID (hashcode of game entity)
                    string entityId = entity.GetHashCode().ToString();

                    sonarAudio.UpdateEntitySound(entityId, sonarInfo.Sound, relativeX, relativeY, sonarInfo.MaxRange);
                }
            }

            sonarAudio.EndEntityUpdate();
        }

        /// <summary>
        /// Cleans up sonar resources.
        /// </summary>
        public void Cleanup()
        {
            UserEnabled = false;
            sonarAudio.Cleanup();
        }

        /// <summary>
        /// Plays a test sound file for debugging.
        /// </summary>
        public void PlayTestSound(string soundFile)
        {
            // Initialize audio if not already done
            if (!sonarAudio.IsInitialized)
            {
                sonarAudio.Initialize();
            }
            sonarAudio.PlayTestSound(soundFile);
        }

        /// <summary>
        /// Performs a sonar scan in all four cardinal directions from the player's position.
        /// </summary>
        /// <returns>Scan results for all directions, or null if player not available</returns>
        public SonarScanResult ScanCardinalDirections()
        {
            var playerController = GameObjectCache.Get<FieldPlayerController>();
            if (playerController?.fieldPlayer == null)
                return null;

            Vector3 playerPos = playerController.fieldPlayer.transform.position;
            int playerLayer = playerController.fieldPlayer.gameObject.layer;

            return new SonarScanResult
            {
                North = ScanDirection(playerPos, CardinalDirection.North, playerLayer),
                South = ScanDirection(playerPos, CardinalDirection.South, playerLayer),
                East = ScanDirection(playerPos, CardinalDirection.East, playerLayer),
                West = ScanDirection(playerPos, CardinalDirection.West, playerLayer)
            };
        }

        /// <summary>
        /// Scans in a single direction for blocking obstacles.
        /// </summary>
        private SonarHit ScanDirection(Vector3 origin, CardinalDirection direction, int playerLayer)
        {
            var hit = new SonarHit
            {
                Direction = direction,
                IsBlocked = false,
                Distance = MaxRange
            };

            Vector2 origin2D = new Vector2(origin.x, origin.y);
            Vector2 dirVector = DirectionVectors[direction];

            // Create layer mask for player's layer only
            int layerMask = 1 << playerLayer;

            // Perform raycast to get all hits along the ray
            RaycastHit2D[] rayHits = Physics2D.RaycastAll(origin2D, dirVector, MaxRange, layerMask);

            // Find the closest blocking collider
            float closestDistance = MaxRange;
            Collider2D closestCollider = null;

            foreach (var rayHit in rayHits)
            {
                if (rayHit.collider != null && IsBlockingCollider(rayHit.collider))
                {
                    if (rayHit.distance < closestDistance)
                    {
                        closestDistance = rayHit.distance;
                        closestCollider = rayHit.collider;
                    }
                }
            }

            if (closestCollider != null)
            {
                hit.IsBlocked = true;
                hit.Distance = closestDistance;
                hit.Collider = closestCollider;
            }

            return hit;
        }

        /// <summary>
        /// Checks if a collider is a blocking obstacle.
        /// </summary>
        private bool IsBlockingCollider(Collider2D collider)
        {
            if (collider == null || collider.gameObject == null)
                return false;

            // Skip disabled Unity colliders
            if (!collider.enabled)
                return false;

            // Skip player
            if (collider.gameObject.name.Contains("Player"))
                return false;

            // Check the game's internal enable flag on FieldColliderEntity
            // The game may have disabled the collision logic even if the Unity collider is still enabled
            if (!IsFieldColliderEnabled(collider.gameObject))
                return false;

            // Check for matching entity in cache
            var matchedEntity = entityCache.FindEntityByGameObject(collider.gameObject);

            if (matchedEntity != null)
            {
                // Entity found - use its BlocksPathing property
                return matchedEntity.BlocksPathing;
            }

            // No matched entity - assume blocking
            return true;
        }

        /// <summary>
        /// Checks if the game's internal FieldColliderEntity enable flag is true.
        /// Returns true if no FieldColliderEntity is found (default to enabled).
        /// </summary>
        private bool IsFieldColliderEnabled(GameObject gameObject)
        {
            try
            {
                // Try to get FieldColliderEntity from the GameObject or its parent
                var colliderEntity = gameObject.GetComponent<FieldColliderEntity>();
                if (colliderEntity == null)
                    colliderEntity = gameObject.GetComponentInParent<FieldColliderEntity>();

                if (colliderEntity == null)
                    return true; // No FieldColliderEntity, assume enabled

                // Access the private 'enable' field via reflection
                var il2cppType = colliderEntity.GetIl2CppType();
                var bindingFlags = Il2CppSystem.Reflection.BindingFlags.NonPublic |
                                   Il2CppSystem.Reflection.BindingFlags.Instance;
                var enableField = il2cppType.GetField("enable", bindingFlags);

                if (enableField == null)
                    return true; // Field not found, assume enabled

                return enableField.GetValue(colliderEntity).Unbox<bool>();
            }
            catch
            {
                // On any error, assume enabled
                return true;
            }
        }

        /// <summary>
        /// Formats scan results for speech output.
        /// </summary>
        public string FormatScanResultsForSpeech(SonarScanResult results)
        {
            if (results == null)
                return "Sonar unavailable";

            var parts = new List<string>();

            foreach (var hit in results.AllHits)
            {
                string dirName = hit.Direction.ToString();

                if (hit.IsBlocked)
                {
                    // Use ceiling so partial tiles count as full tiles
                    // (raycast measures to collider edge, not tile center)
                    int tiles = Mathf.CeilToInt(hit.DistanceInTiles);
                    string tileWord = tiles == 1 ? "tile" : "tiles";
                    parts.Add($"{dirName}: {tiles} {tileWord}");
                }
                else
                {
                    parts.Add($"{dirName}: clear");
                }
            }

            return string.Join(", ", parts);
        }
    }
}
