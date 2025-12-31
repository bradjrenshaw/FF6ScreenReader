using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Il2Cpp;
using Il2CppLast.Map;
using FFVI_ScreenReader.Field;
using FFVI_ScreenReader.Utils;

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

            // 3. Check for entities at this exact tile position
            var entitiesAtCursor = GetEntitiesAtPosition(cursorPosition, entityCache);
            if (entitiesAtCursor.Count > 0)
            {
                // Get player position for FormatDescription
                var playerController = GameObjectCache.Get<FieldPlayerController>();
                Vector3 playerPos = playerController?.fieldPlayer?.transform.position ?? Vector3.zero;

                // Add full entity descriptions
                foreach (var entity in entitiesAtCursor)
                {
                    parts.Add(entity.FormatDescription(playerPos));
                }
            }

            return string.Join(", ", parts);
        }

        /// <summary>
        /// Checks if the player is on the specified tile position
        /// </summary>
        private bool IsPlayerOnTile(Vector3 cursorPos)
        {
            var playerController = GameObjectCache.Get<FieldPlayerController>();
            if (playerController?.fieldPlayer?.transform == null || playerController.mapHandle == null)
                return false;

            int mapWidth = playerController.mapHandle.GetCollisionLayerWidth();
            int mapHeight = playerController.mapHandle.GetCollisionLayerHeight();

            // Convert cursor position to tile coordinates
            int cursorTileX = Mathf.FloorToInt(mapWidth * 0.5f + cursorPos.x * 0.0625f);
            int cursorTileY = Mathf.FloorToInt(mapHeight * 0.5f - cursorPos.y * 0.0625f);

            // Convert player position to tile coordinates
            Vector3 playerPos = playerController.fieldPlayer.transform.localPosition;
            int playerTileX = Mathf.FloorToInt(mapWidth * 0.5f + playerPos.x * 0.0625f);
            int playerTileY = Mathf.FloorToInt(mapHeight * 0.5f - playerPos.y * 0.0625f);

            return cursorTileX == playerTileX && cursorTileY == playerTileY;
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

                int mapWidth = playerController.mapHandle.GetCollisionLayerWidth();
                int mapHeight = playerController.mapHandle.GetCollisionLayerHeight();

                // Use the same formula as pathfinding (from FieldNavigationHelper)
                int cellX = Mathf.FloorToInt(mapWidth * 0.5f + worldPos.x * 0.0625f);
                int cellY = Mathf.FloorToInt(mapHeight * 0.5f - worldPos.y * 0.0625f);

                return $"{cellX}, {cellY}";
            }
            catch (Exception)
            {
                return "unknown";
            }
        }

        /// <summary>
        /// Gets entities at the specified tile position
        /// </summary>
        private List<NavigableEntity> GetEntitiesAtPosition(Vector3 cursorPos, EntityCache entityCache)
        {
            var result = new List<NavigableEntity>();

            var playerController = GameObjectCache.Get<FieldPlayerController>();
            if (playerController?.mapHandle == null)
                return result;

            int mapWidth = playerController.mapHandle.GetCollisionLayerWidth();
            int mapHeight = playerController.mapHandle.GetCollisionLayerHeight();

            // Convert cursor position to tile coordinates
            int cursorTileX = Mathf.FloorToInt(mapWidth * 0.5f + cursorPos.x * 0.0625f);
            int cursorTileY = Mathf.FloorToInt(mapHeight * 0.5f - cursorPos.y * 0.0625f);

            // Get unique entities from cache
            var uniqueEntities = entityCache.Entities.Values.Distinct();

            foreach (var entity in uniqueEntities)
            {
                // Handle GroupEntity specially - check each member's position
                if (entity is GroupEntity group)
                {
                    foreach (var member in group.Members)
                    {
                        if (member?.GameEntity?.transform == null)
                            continue;

                        if (IsEntityOnTile(member, cursorTileX, cursorTileY, mapWidth, mapHeight))
                        {
                            result.Add(member);
                        }
                    }
                }
                else
                {
                    // Regular entity - check its position directly
                    if (entity?.GameEntity?.transform == null)
                        continue;

                    if (IsEntityOnTile(entity, cursorTileX, cursorTileY, mapWidth, mapHeight))
                    {
                        result.Add(entity);
                    }
                }
            }

            // Sort by priority (lower = more important)
            return result.OrderBy(e => e.Priority).ToList();
        }

        /// <summary>
        /// Checks if an entity is on the specified tile
        /// </summary>
        private bool IsEntityOnTile(NavigableEntity entity, int tileX, int tileY, int mapWidth, int mapHeight)
        {
            Vector3 entityPos = entity.GameEntity.transform.localPosition;

            // Convert entity position to tile coordinates
            int entityTileX = Mathf.FloorToInt(mapWidth * 0.5f + entityPos.x * 0.0625f);
            int entityTileY = Mathf.FloorToInt(mapHeight * 0.5f - entityPos.y * 0.0625f);

            return tileX == entityTileX && tileY == entityTileY;
        }

        /// <summary>
        /// Gets cardinal/intercardinal direction from one position to another
        /// </summary>
        public static string GetDirection(Vector3 from, Vector3 to)
        {
            Vector3 diff = to - from;
            float angle = Mathf.Atan2(diff.x, diff.y) * Mathf.Rad2Deg;

            // Normalize to 0-360
            if (angle < 0) angle += 360;

            // Convert to cardinal/intercardinal directions
            if (angle >= 337.5 || angle < 22.5) return "North";
            else if (angle >= 22.5 && angle < 67.5) return "Northeast";
            else if (angle >= 67.5 && angle < 112.5) return "East";
            else if (angle >= 112.5 && angle < 157.5) return "Southeast";
            else if (angle >= 157.5 && angle < 202.5) return "South";
            else if (angle >= 202.5 && angle < 247.5) return "Southwest";
            else if (angle >= 247.5 && angle < 292.5) return "West";
            else if (angle >= 292.5 && angle < 337.5) return "Northwest";
            else return "Unknown";
        }
    }
}
