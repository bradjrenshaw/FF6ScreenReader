using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Il2Cpp;
using Il2CppLast.Map;
using Il2CppLast.Entity.Field;
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

            // 3. Use Physics2D to find all colliders at cursor position
            var descriptions = GetCollidersAtPosition(cursorPosition, entityCache);

            // 4. Check walkability
            var walkability = CheckWalkability(cursorPosition, entityCache);
            if (walkability != null)
            {
                parts.Add(walkability);
            }

            // 5. Add entity/collider descriptions
            if (descriptions.Count > 0)
            {
                foreach (var description in descriptions)
                {
                    parts.Add(description);
                }
            }

            return string.Join(", ", parts);
        }

        /// <summary>
        /// Gets descriptions of all entities at the specified position.
        /// Uses Physics2D for collider-based entities (handles multi-tile),
        /// then checks entity cache for collider-less entities by tile position.
        /// </summary>
        private List<string> GetCollidersAtPosition(Vector3 cursorPos, EntityCache entityCache)
        {
            var result = new List<string>();
            var processedEntities = new HashSet<NavigableEntity>();

            // Get player info for FormatDescription
            var playerController = GameObjectCache.Get<FieldPlayerController>();
            Vector3 playerPos = playerController?.fieldPlayer?.transform.position ?? Vector3.zero;

            // === Phase 1: Physics2D check for collider-based entities ===
            // This correctly handles multi-tile entities since it checks collider overlap
            Vector2 point = new Vector2(cursorPos.x, cursorPos.y);
            Collider2D[] colliders = Physics2D.OverlapPointAll(point);

            if (colliders != null)
            {
                foreach (var collider in colliders)
                {
                    if (collider == null || collider.gameObject == null)
                        continue;

                    NavigableEntity matchedEntity = FindMatchingEntity(collider.gameObject, entityCache);

                    if (matchedEntity != null && !processedEntities.Contains(matchedEntity))
                    {
                        processedEntities.Add(matchedEntity);
                        result.Add(matchedEntity.FormatDescription(playerPos));
                    }
                }
            }

            // === Phase 2: Entity cache check for collider-less entities ===
            // For entities without colliders, check if their origin is on the cursor tile
            if (playerController?.mapHandle != null)
            {
                int mapWidth = playerController.mapHandle.GetCollisionLayerWidth();
                int mapHeight = playerController.mapHandle.GetCollisionLayerHeight();

                // Convert cursor position to tile coordinates
                int cursorTileX = Mathf.FloorToInt(mapWidth * 0.5f + cursorPos.x * 0.0625f);
                int cursorTileY = Mathf.FloorToInt(mapHeight * 0.5f - cursorPos.y * 0.0625f);

                // Check each entity in the cache
                foreach (var entity in entityCache.Entities.Values.Distinct())
                {
                    // Skip if already found via Physics2D
                    if (processedEntities.Contains(entity))
                        continue;

                    // Handle GroupEntity - check each member
                    if (entity is GroupEntity group)
                    {
                        foreach (var member in group.Members)
                        {
                            if (member == null || processedEntities.Contains(member))
                                continue;

                            if (IsEntityOnTile(member, mapWidth, mapHeight, cursorTileX, cursorTileY))
                            {
                                processedEntities.Add(member);
                                result.Add(member.FormatDescription(playerPos));
                            }
                        }
                    }
                    else
                    {
                        if (IsEntityOnTile(entity, mapWidth, mapHeight, cursorTileX, cursorTileY))
                        {
                            processedEntities.Add(entity);
                            result.Add(entity.FormatDescription(playerPos));
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Checks if an entity's position is on the specified tile
        /// </summary>
        private bool IsEntityOnTile(NavigableEntity entity, int mapWidth, int mapHeight, int tileX, int tileY)
        {
            var gameEntity = entity.GameEntity;
            if (gameEntity?.transform == null)
                return false;

            Vector3 entityPos = gameEntity.transform.localPosition;
            int entityTileX = Mathf.FloorToInt(mapWidth * 0.5f + entityPos.x * 0.0625f);
            int entityTileY = Mathf.FloorToInt(mapHeight * 0.5f - entityPos.y * 0.0625f);

            return entityTileX == tileX && entityTileY == tileY;
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

                    // Try to get FieldColliderEntity
                    var colliderEntity = collider.gameObject.GetComponent<FieldColliderEntity>();
                    if (colliderEntity == null)
                        colliderEntity = collider.gameObject.GetComponentInParent<FieldColliderEntity>();

                    if (colliderEntity != null)
                    {
                        // Check the enable field
                        bool enable = false;
                        try
                        {
                            var il2cppType = colliderEntity.GetIl2CppType();
                            var bindingFlags = Il2CppSystem.Reflection.BindingFlags.NonPublic |
                                               Il2CppSystem.Reflection.BindingFlags.Instance;

                            var enableField = il2cppType.GetField("enable", bindingFlags);
                            enable = enableField != null &&
                                enableField.GetValue(colliderEntity).Unbox<bool>();
                        }
                        catch
                        {
                            // If we can't read the field, assume enabled to be safe
                            enable = true;
                        }

                        // If not enabled, this collider doesn't block
                        if (!enable)
                            continue;

                        // Enabled - check if it's a known entity with BlocksPathing
                        NavigableEntity matchedEntity = FindMatchingEntity(collider.gameObject, entityCache);

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
                            // No matched entity - this is pure terrain collision, so it blocks
                            return "Blocked";
                        }
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
        /// Dumps all fields, properties, and methods of a component
        /// </summary>
        private string DumpComponentMembers(object component, string name)
        {
            var lines = new List<string>();
            lines.Add($"=== {name} ===");

            try
            {
                var type = component.GetType();
                var il2cppType = (component as Il2CppSystem.Object)?.GetIl2CppType();

                if (il2cppType != null)
                {
                    var bindingFlags = Il2CppSystem.Reflection.BindingFlags.Public |
                        Il2CppSystem.Reflection.BindingFlags.NonPublic |
                        Il2CppSystem.Reflection.BindingFlags.Instance;

                    // Fields
                    lines.Add("--- Fields ---");
                    var fields = il2cppType.GetFields(bindingFlags);
                    for (int i = 0; i < fields.Length; i++)
                    {
                        var field = fields[i];
                        try
                        {
                            string info = $"{field.Name} ({field.FieldType.Name})";
                            if (field.FieldType.IsPrimitive || field.FieldType.FullName == "System.String")
                            {
                                var value = field.GetValue(component as Il2CppSystem.Object);
                                info += $" = {value}";
                            }
                            lines.Add(info);
                        }
                        catch { lines.Add($"{field.Name} (error)"); }
                    }

                    // Properties
                    lines.Add("--- Properties ---");
                    var props = il2cppType.GetProperties(bindingFlags);
                    for (int i = 0; i < props.Length; i++)
                    {
                        var prop = props[i];
                        try
                        {
                            string info = $"{prop.Name} ({prop.PropertyType.Name})";
                            if (prop.CanRead && (prop.PropertyType.IsPrimitive || prop.PropertyType.FullName == "System.String"))
                            {
                                try
                                {
                                    var value = prop.GetValue(component as Il2CppSystem.Object);
                                    info += $" = {value}";
                                }
                                catch { info += " = (error)"; }
                            }
                            lines.Add(info);
                        }
                        catch { lines.Add($"{prop.Name} (error)"); }
                    }

                    // Methods
                    lines.Add("--- Methods ---");
                    var methods = il2cppType.GetMethods(bindingFlags);
                    for (int i = 0; i < methods.Length; i++)
                    {
                        var method = methods[i];
                        try
                        {
                            if (method.Name.StartsWith("get_") || method.Name.StartsWith("set_"))
                                continue;

                            var parms = method.GetParameters();
                            var parmList = new List<string>();
                            for (int j = 0; j < parms.Length; j++)
                                parmList.Add(parms[j].ParameterType.Name);

                            lines.Add($"{method.Name}({string.Join(", ", parmList)}) -> {method.ReturnType.Name}");
                        }
                        catch { lines.Add($"{method.Name} (error)"); }
                    }
                }
            }
            catch (Exception ex)
            {
                lines.Add($"Error: {ex.Message}");
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Dumps all properties, methods, and fields of a FieldColliderEntity component
        /// </summary>
        private string DumpFieldColliderEntity(Component component, string objName)
        {
            var parts = new List<string>();
            parts.Add($"FieldColliderEntity on {objName}");

            try
            {
                var type = component.GetIl2CppType();

                // Use Il2Cpp binding flags
                var bindingFlags = Il2CppSystem.Reflection.BindingFlags.Public |
                    Il2CppSystem.Reflection.BindingFlags.NonPublic |
                    Il2CppSystem.Reflection.BindingFlags.Instance |
                    Il2CppSystem.Reflection.BindingFlags.Static;

                // Get all fields
                var fields = type.GetFields(bindingFlags);

                for (int i = 0; i < fields.Length; i++)
                {
                    var field = fields[i];
                    try
                    {
                        string fieldInfo = $"Field: {field.Name} ({field.FieldType.Name})";

                        // Try to get value for primitive types
                        if (field.FieldType.IsPrimitive || field.FieldType.FullName == "System.String")
                        {
                            var value = field.GetValue(component);
                            fieldInfo += $" = {value}";
                        }

                        parts.Add(fieldInfo);
                    }
                    catch
                    {
                        parts.Add($"Field: {field.Name} (error reading)");
                    }
                }

                // Get all properties
                var properties = type.GetProperties(bindingFlags);

                for (int i = 0; i < properties.Length; i++)
                {
                    var prop = properties[i];
                    try
                    {
                        string propInfo = $"Property: {prop.Name} ({prop.PropertyType.Name})";

                        // Try to get value for primitive types
                        if (prop.CanRead && (prop.PropertyType.IsPrimitive || prop.PropertyType.FullName == "System.String"))
                        {
                            try
                            {
                                var value = prop.GetValue(component);
                                propInfo += $" = {value}";
                            }
                            catch
                            {
                                propInfo += " = (error)";
                            }
                        }

                        parts.Add(propInfo);
                    }
                    catch
                    {
                        parts.Add($"Property: {prop.Name} (error reading)");
                    }
                }

                // Get all methods (just names)
                var methods = type.GetMethods(bindingFlags);

                for (int i = 0; i < methods.Length; i++)
                {
                    var method = methods[i];
                    try
                    {
                        // Skip property getters/setters and common object methods
                        if (method.Name.StartsWith("get_") || method.Name.StartsWith("set_") ||
                            method.Name == "ToString" || method.Name == "GetHashCode" ||
                            method.Name == "Equals" || method.Name == "GetType")
                            continue;

                        var parameters = method.GetParameters();
                        var paramList = new List<string>();
                        for (int j = 0; j < parameters.Length; j++)
                        {
                            paramList.Add(parameters[j].ParameterType.Name);
                        }
                        parts.Add($"Method: {method.Name}({string.Join(", ", paramList)}) -> {method.ReturnType.Name}");
                    }
                    catch
                    {
                        parts.Add($"Method: {method.Name} (error reading)");
                    }
                }
            }
            catch (Exception ex)
            {
                parts.Add($"Error dumping: {ex.Message}");
            }

            return string.Join("; ", parts);
        }

        /// <summary>
        /// Walks up the hierarchy to find the entity root object
        /// </summary>
        private GameObject GetEntityRoot(GameObject obj)
        {
            // Walk up to find the root entity (typically has FieldEntity component or is a few levels up)
            Transform current = obj.transform;

            // Walk up a few levels to find a meaningful parent
            // Most entities have their colliders on child objects
            for (int i = 0; i < 3 && current.parent != null; i++)
            {
                // Stop if we hit a known container name
                string parentName = current.parent.name;
                if (parentName.Contains("Map") || parentName.Contains("Field") || parentName.Contains("Root"))
                    break;
                current = current.parent;
            }

            return current.gameObject;
        }

        /// <summary>
        /// Tries to find a matching NavigableEntity for the given collider GameObject.
        /// Walks up the hierarchy from the collider checking each level against known entities.
        /// </summary>
        private NavigableEntity FindMatchingEntity(GameObject colliderObj, EntityCache entityCache)
        {
            // Walk up the hierarchy from the collider, checking each level against entities
            Transform current = colliderObj.transform;

            while (current != null)
            {
                GameObject currentObj = current.gameObject;

                // Check this level against all entities
                var uniqueEntities = entityCache.Entities.Values.Distinct();

                foreach (var entity in uniqueEntities)
                {
                    // Handle GroupEntity - check each member
                    if (entity is GroupEntity group)
                    {
                        foreach (var member in group.Members)
                        {
                            if (member?.GameEntity?.gameObject == currentObj)
                                return member;
                        }
                    }
                    else
                    {
                        if (entity?.GameEntity?.gameObject == currentObj)
                            return entity;
                    }
                }

                current = current.parent;
            }

            return null;
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
