using System;
using System.Collections.Generic;
using UnityEngine;
using Il2Cpp;
using Il2CppLast.Entity.Field;
using Il2CppLast.Map;
using Il2CppLast.Message;
using FFVI_ScreenReader.Core;
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
    /// </summary>
    public class SonarSystem
    {
        /// <summary>
        /// Maximum range for wall detection in world units (16 units = 1 tile)
        /// </summary>
        public float MaxRange { get; set; } = 48f; // 3 tiles

        /// <summary>
        /// Audio manager for playing sonar tones
        /// </summary>
        private SonarAudio sonarAudio;

        /// <summary>
        /// Whether continuous sonar mode is active
        /// </summary>
        public bool IsActive { get; private set; }

        /// <summary>
        /// Whether sonar scanning is temporarily paused (e.g., during map transitions)
        /// </summary>
        public bool IsPaused { get; private set; }

        /// <summary>
        /// Reference to entity cache (set when sonar is activated)
        /// </summary>
        private EntityCache activeEntityCache;

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
        public SonarSystem()
        {
            sonarAudio = new SonarAudio();
        }

        /// <summary>
        /// Activates continuous sonar mode.
        /// </summary>
        /// <param name="entityCache">Entity cache for blocking checks</param>
        public void Activate(EntityCache entityCache)
        {
            if (IsActive)
                return;

            activeEntityCache = entityCache;

            // Initialize audio if not already done
            if (!sonarAudio.IsInitialized)
            {
                sonarAudio.Initialize();
            }

            IsActive = true;
        }

        /// <summary>
        /// Deactivates continuous sonar mode and stops all tones.
        /// </summary>
        public void Deactivate()
        {
            if (!IsActive)
                return;

            IsActive = false;
            IsPaused = false;
            sonarAudio.StopAll();
            activeEntityCache = null;
        }

        /// <summary>
        /// Temporarily pauses sonar scanning (e.g., during map transitions).
        /// Stops all tones but keeps sonar in active state.
        /// </summary>
        public void Pause()
        {
            if (!IsActive || IsPaused)
                return;

            IsPaused = true;
            sonarAudio.StopAll();
        }

        /// <summary>
        /// Resumes sonar scanning after a pause.
        /// </summary>
        public void Resume()
        {
            if (!IsActive || !IsPaused)
                return;

            IsPaused = false;
        }

        /// <summary>
        /// Toggles continuous sonar mode on/off.
        /// </summary>
        /// <param name="entityCache">Entity cache for blocking checks</param>
        /// <returns>True if sonar is now active, false if deactivated</returns>
        public bool Toggle(EntityCache entityCache)
        {
            if (IsActive)
            {
                Deactivate();
                return false;
            }
            else
            {
                Activate(entityCache);
                return true;
            }
        }

        /// <summary>
        /// Updates the sonar system. Call this every frame when sonar is active.
        /// Performs a scan and updates audio feedback.
        /// </summary>
        public void Update()
        {
            if (!IsActive || IsPaused || activeEntityCache == null)
                return;

            // Check if a message window is open (dialog/cutscene text)
            try
            {
                var messageManager = MessageWindowManager.Instance;
                if (messageManager != null && messageManager.IsOpen())
                {
                    // Stop all tones when message window is showing
                    sonarAudio.StopAll();
                    return;
                }
            }
            catch
            {
                // Ignore errors accessing MessageWindowManager
            }

            // Check if field map is in a playable state (not during cutscenes/events)
            try
            {
                var fieldMap = GameObjectCache.Get<FieldMap>();
                if (fieldMap != null && !fieldMap.IsPlayable())
                {
                    // Stop all tones when not in playable state
                    sonarAudio.StopAll();
                    return;
                }
            }
            catch
            {
                // Ignore errors accessing FieldMap
            }

            // Perform scan
            var result = ScanCardinalDirections(activeEntityCache);

            // Update audio based on scan results
            sonarAudio.UpdateFromScanResult(result);
        }

        /// <summary>
        /// Cleans up sonar resources.
        /// </summary>
        public void Cleanup()
        {
            Deactivate();
            sonarAudio.Cleanup();
        }

        /// <summary>
        /// Performs a sonar scan in all four cardinal directions from the player's position.
        /// </summary>
        /// <param name="entityCache">Entity cache to check for blocking entities</param>
        /// <returns>Scan results for all directions, or null if player not available</returns>
        public SonarScanResult ScanCardinalDirections(EntityCache entityCache)
        {
            var playerController = GameObjectCache.Get<FieldPlayerController>();
            if (playerController?.fieldPlayer == null)
                return null;

            Vector3 playerPos = playerController.fieldPlayer.transform.position;
            int playerLayer = playerController.fieldPlayer.gameObject.layer;

            return new SonarScanResult
            {
                North = ScanDirection(playerPos, CardinalDirection.North, playerLayer, entityCache),
                South = ScanDirection(playerPos, CardinalDirection.South, playerLayer, entityCache),
                East = ScanDirection(playerPos, CardinalDirection.East, playerLayer, entityCache),
                West = ScanDirection(playerPos, CardinalDirection.West, playerLayer, entityCache)
            };
        }

        /// <summary>
        /// Scans in a single direction for blocking obstacles.
        /// </summary>
        private SonarHit ScanDirection(Vector3 origin, CardinalDirection direction, int playerLayer, EntityCache entityCache)
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
                if (rayHit.collider != null && IsBlockingCollider(rayHit.collider, entityCache))
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
        private bool IsBlockingCollider(Collider2D collider, EntityCache entityCache)
        {
            if (collider == null || collider.gameObject == null)
                return false;

            // Skip player
            if (collider.gameObject.name.Contains("Player"))
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
