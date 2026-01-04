using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Il2Cpp;
using Il2CppLast.Map;
using Il2CppLast.Entity.Field;
using FFVI_ScreenReader.Field;
using FFVI_ScreenReader.Utils;
using static FFVI_ScreenReader.Utils.TileCoordinateConverter;
using static FFVI_ScreenReader.Utils.DirectionHelper;

namespace FFVI_ScreenReader.Core
{
    /// <summary>
    /// Manages a virtual cursor for exploring the map without moving the player.
    /// The cursor can be moved tile-by-tile and announces what's at each position.
    /// </summary>
    public class MapViewer
    {
        private Vector3 cursorPosition;
        private bool isActive;

        /// <summary>
        /// Current cursor position in world coordinates
        /// </summary>
        public Vector3 CursorPosition => cursorPosition;

        /// <summary>
        /// Whether the cursor has been moved away from the player
        /// </summary>
        public bool IsActive => isActive;

        /// <summary>
        /// Snaps the cursor to the player's current position
        /// </summary>
        public void SnapToPlayer(Vector3 playerPos)
        {
            cursorPosition = playerPos;
            isActive = false;
        }

        /// <summary>
        /// Moves the cursor by the specified offset (in world units, 16 = 1 tile)
        /// </summary>
        public void MoveCursor(Vector2 offset)
        {
            cursorPosition += new Vector3(offset.x, offset.y, 0);
            isActive = true;
        }

        /// <summary>
        /// Describes what's at the current cursor position
        /// </summary>
        /// <param name="entityCache">The entity cache to query for entities</param>
        /// <returns>A description of the tile contents</returns>
        public string DescribeTileAtCursor(EntityCache entityCache)
        {
            var parts = new List<string>();

            // 1. Add tile coordinates
            string coords = GetTileCoordinates(cursorPosition);
            parts.Add(coords);

            // 2. Check if player is on this tile
            if (IsPlayerOnTile(cursorPosition))
            {
                parts.Add("Player");
            }

            // 3. Find all entities at cursor position
            var playerController = GameObjectCache.Get<FieldPlayerController>();
            Vector3 playerPos = playerController?.fieldPlayer?.transform.position ?? Vector3.zero;
            var entities = entityCache.FindEntitiesAtPosition(cursorPosition, playerController?.mapHandle);

            // 4. Check walkability
            var walkability = CheckWalkability(cursorPosition, entityCache);
            if (walkability != null)
            {
                parts.Add(walkability);
            }

            // 5. Add entity descriptions
            foreach (var entity in entities)
            {
                parts.Add(entity.FormatDescription(playerPos));
            }

            return string.Join(", ", parts);
        }

        /// <summary>
        /// Checks walkability by looking for enabled FieldColliderEntity on the player's layer.
        /// If enable is false, not blocking. If enable is true, check matched entity's BlocksPathing.
        /// </summary>
        private string CheckWalkability(Vector3 cursorPos, EntityCache entityCache)
        {
            try
            {
                var playerController = GameObjectCache.Get<FieldPlayerController>();
                int playerLayer = playerController?.fieldPlayer?.gameObject?.layer ?? -1;

                if (playerLayer < 0)
                    return null;

                Vector2 point = new Vector2(cursorPos.x, cursorPos.y);
                Collider2D[] colliders = Physics2D.OverlapPointAll(point);

                if (colliders == null || colliders.Length == 0)
                    return "Walkable";

                foreach (var collider in colliders)
                {
                    if (collider == null || collider.gameObject == null)
                        continue;

                    // Skip player
                    if (collider.gameObject.name.Contains("Player"))
                        continue;

                    // Only check colliders on the player's layer
                    if (collider.gameObject.layer != playerLayer)
                        continue;

                    // Check for matching entity in cache
                    NavigableEntity matchedEntity = entityCache.FindEntityByGameObject(collider.gameObject);

                    if (matchedEntity != null)
                    {
                        // Entity found - use its BlocksPathing property
                        if (matchedEntity.BlocksPathing)
                            return "Blocked";
                        // BlocksPathing is false, so this doesn't block
                        continue;
                    }
                    else
                    {
                        // No matched entity - assume blocking
                        return "Blocked";
                    }
                }

                return "Walkable";
            }
            catch
            {
                return null; // Don't report errors, just skip walkability info
            }
        }

        /// <summary>
        /// Checks if the player is on the specified tile position
        /// </summary>
        private bool IsPlayerOnTile(Vector3 cursorPos)
        {
            var playerController = GameObjectCache.Get<FieldPlayerController>();
            if (playerController?.fieldPlayer?.transform == null || playerController.mapHandle == null)
                return false;

            Vector3 playerPos = playerController.fieldPlayer.transform.localPosition;
            return AreOnSameTile(cursorPos, playerPos, playerController.mapHandle);
        }

        /// <summary>
        /// Converts world position to tile coordinates and formats as string
        /// </summary>
        private string GetTileCoordinates(Vector3 worldPos)
        {
            try
            {
                var playerController = GameObjectCache.Get<FieldPlayerController>();
                if (playerController?.mapHandle == null)
                    return "unknown";

                var tile = WorldToTile(worldPos, playerController.mapHandle);
                return tile.ToString();
            }
            catch (Exception)
            {
                return "unknown";
            }
        }

    }
}
