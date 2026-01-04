using System;
using System.Collections.Generic;
using UnityEngine;
using Il2Cpp;
using Il2CppLast.Entity.Field;
using Il2CppLast.Map;
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
    /// </summary>
    public class SonarSystem
    {
        /// <summary>
        /// Maximum range for wall detection in world units (16 units = 1 tile)
        /// </summary>
        public float MaxRange { get; set; } = 96f; // 6 tiles

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

            // Perform raycast to get all hits along the ray
            RaycastHit2D[] rayHits = Physics2D.RaycastAll(origin2D, dirVector, MaxRange);

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

            // Skip player
            if (collider.gameObject.name.Contains("Player"))
                return false;

            // Try to get FieldColliderEntity
            var colliderEntity = collider.gameObject.GetComponent<FieldColliderEntity>();
            if (colliderEntity == null)
                colliderEntity = collider.gameObject.GetComponentInParent<FieldColliderEntity>();

            if (colliderEntity != null)
            {
                // Check the enable field using IL2CPP reflection
                try
                {
                    var il2cppType = colliderEntity.GetIl2CppType();
                    var bindingFlags = Il2CppSystem.Reflection.BindingFlags.NonPublic |
                                       Il2CppSystem.Reflection.BindingFlags.Instance;

                    var enableField = il2cppType.GetField("enable", bindingFlags);
                    bool enable = enableField != null &&
                        enableField.GetValue(colliderEntity).Unbox<bool>();

                    return enable;
                }
                catch
                {
                    // If we can't read the field, assume it's blocking to be safe
                    return true;
                }
            }

            return false;
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
