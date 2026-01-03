using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Il2Cpp;
using FFVI_ScreenReader.Field;
using FFVI_ScreenReader.Core.Filters;
using FFVI_ScreenReader.Utils;
using Il2CppLast.Entity.Field;
using Il2CppLast.Map;
using static FFVI_ScreenReader.Utils.TileCoordinateConverter;

namespace FFVI_ScreenReader.Core
{
    /// <summary>
    /// Maintains a registry of all navigable entities in the world.
    /// Tracks entities as they are added/removed and fires events for subscribers.
    /// Supports grouping related entities together using grouping strategies.
    /// </summary>
    public class EntityCache
    {
        private readonly float scanInterval;
        private float lastScanTime = 0f;
        private Dictionary<FieldEntity, NavigableEntity> entityMap = new Dictionary<FieldEntity, NavigableEntity>();
        private List<IGroupingStrategy> enabledStrategies = new List<IGroupingStrategy>();
        // O(1) lookup for groups by key (avoids scanning all entities)
        private Dictionary<string, GroupEntity> groupsByKey = new Dictionary<string, GroupEntity>();

        /// <summary>
        /// Fired when a new entity is added to the cache.
        /// </summary>
        public event Action<NavigableEntity> OnEntityAdded;

        /// <summary>
        /// Fired when an entity is removed from the cache.
        /// </summary>
        public event Action<NavigableEntity> OnEntityRemoved;

        /// <summary>
        /// Read-only access to the entity registry.
        /// </summary>
        public IReadOnlyDictionary<FieldEntity, NavigableEntity> Entities => entityMap;

        /// <summary>
        /// Creates a new entity cache with the specified scan interval.
        /// </summary>
        /// <param name="scanInterval">Time in seconds between automatic scans</param>
        public EntityCache(float scanInterval = 0.1f)
        {
            this.scanInterval = scanInterval;
        }

        /// <summary>
        /// Enables a grouping strategy.
        /// Existing entities are immediately regrouped.
        /// </summary>
        public void EnableGroupingStrategy(IGroupingStrategy strategy)
        {
            if (enabledStrategies.Contains(strategy))
                return; // Already enabled

            enabledStrategies.Add(strategy);

            // Immediately regroup existing entities
            RegroupEntitiesForStrategy(strategy);
        }

        /// <summary>
        /// Regroups existing entities for a newly enabled strategy.
        /// Finds individual entities that should be grouped and groups them.
        /// </summary>
        private void RegroupEntitiesForStrategy(IGroupingStrategy strategy)
        {
            // Collect all individual entities that can be grouped by this strategy
            var individualsToGroup = entityMap
                .Where(kvp => !(kvp.Value is GroupEntity)) // Only individual entities
                .Select(kvp => new { FieldEntity = kvp.Key, NavEntity = kvp.Value })
                .Where(item => strategy.GetGroupKey(item.NavEntity) != null) // Can be grouped
                .ToList();

            // Group them by their group key
            var grouped = individualsToGroup
                .GroupBy(item => strategy.GetGroupKey(item.NavEntity))
                .Where(g => g.Key != null)
                .ToList();

            foreach (var group in grouped)
            {
                // Get category from first member (all members in a group should have the same category)
                var firstMember = group.First();
                EntityCategory groupCategory = firstMember.NavEntity.Category;

                // Remove all individual entities from this group
                foreach (var item in group)
                {
                    OnEntityRemoved?.Invoke(item.NavEntity);
                }

                // Create a new GroupEntity with explicit category
                var groupEntity = new GroupEntity(group.Key, strategy, groupCategory);

                // Register in group lookup dictionary
                groupsByKey[group.Key] = groupEntity;

                // Add all members to the group
                foreach (var item in group)
                {
                    groupEntity.AddMember(item.NavEntity);
                    entityMap[item.FieldEntity] = groupEntity; // Point to group
                }

                // Fire OnEntityAdded for the new group
                OnEntityAdded?.Invoke(groupEntity);
            }
        }

        /// <summary>
        /// Disables a grouping strategy.
        /// Existing groups created by this strategy will be dissolved and members promoted to individual entities.
        /// </summary>
        public void DisableGroupingStrategy(IGroupingStrategy strategy)
        {
            if (!enabledStrategies.Contains(strategy))
                return;

            enabledStrategies.Remove(strategy);

            // Dissolve all groups created by this strategy
            DissolveGroupsForStrategy(strategy);
        }

        /// <summary>
        /// Dissolves all groups created by a specific strategy.
        /// Promotes individual members back to standalone entities.
        /// </summary>
        private void DissolveGroupsForStrategy(IGroupingStrategy strategy)
        {
            // Find all GroupEntity instances in the map
            var groups = entityMap.Values
                .OfType<GroupEntity>()
                .Where(g => IsGroupFromStrategy(g, strategy))
                .Distinct()
                .ToList();

            foreach (var group in groups)
            {
                // Remove from group lookup dictionary
                groupsByKey.Remove(group.GroupKey);

                // Remove the group
                OnEntityRemoved?.Invoke(group);

                // Promote each member to an individual entity
                foreach (var member in group.Members.ToList())
                {
                    var fieldEntity = member.GameEntity;
                    if (fieldEntity != null && entityMap.ContainsKey(fieldEntity))
                    {
                        entityMap[fieldEntity] = member;
                        OnEntityAdded?.Invoke(member);
                    }
                }
            }
        }

        /// <summary>
        /// Checks if a group was created by a specific strategy.
        /// </summary>
        private bool IsGroupFromStrategy(GroupEntity group, IGroupingStrategy strategy)
        {
            if (group.Members.Count == 0)
                return false;

            // Check if any member would produce a group key for this strategy
            var firstMember = group.Members[0];
            string groupKey = strategy.GetGroupKey(firstMember);

            return groupKey != null && groupKey == group.GroupKey;
        }

        /// <summary>
        /// Called every frame to handle periodic scanning.
        /// </summary>
        public void Update()
        {
            if (Time.time - lastScanTime >= scanInterval)
            {
                lastScanTime = Time.time;
                Scan();
            }
        }

        /// <summary>
        /// Scans for changes in the world and updates the entity registry.
        /// Fires OnEntityAdded/OnEntityRemoved events for changes.
        /// Groups related entities together using enabled grouping strategies.
        /// </summary>
        public void Scan()
        {
            // Get all current FieldEntity objects from the world
            var currentFieldEntities = FieldNavigationHelper.GetAllFieldEntities();

            // Convert to HashSet for O(1) lookups
            var currentSet = new HashSet<FieldEntity>(currentFieldEntities);

            // REMOVE phase: Find entities that are no longer in the world
            var toRemove = new List<FieldEntity>();
            foreach (var kvp in entityMap)
            {
                if (!currentSet.Contains(kvp.Key))
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var fieldEntity in toRemove)
            {
                HandleEntityRemoval(fieldEntity);
            }

            // ADD phase: Find new entities and wrap them
            Vector3 playerPos = GetPlayerPosition();

            foreach (var fieldEntity in currentFieldEntities)
            {
                if (!entityMap.ContainsKey(fieldEntity))  // O(1) hash lookup
                {
                    // Create NavigableEntity wrapper
                    var navEntity = EntityFactory.CreateFromFieldEntity(fieldEntity, playerPos);

                    // Only add if factory returned a valid entity (filters non-interactive types)
                    if (navEntity != null)
                    {
                        HandleEntityAddition(fieldEntity, navEntity);
                    }
                }
            }
        }

        /// <summary>
        /// Handles adding a new entity to the cache.
        /// Checks if entity should be grouped and manages group membership.
        /// </summary>
        private void HandleEntityAddition(FieldEntity fieldEntity, NavigableEntity navEntity)
        {
            // Check if this entity should be grouped
            GroupEntity group = FindOrCreateGroup(navEntity);

            if (group != null)
            {
                // Add to group
                bool isNewGroup = group.Members.Count == 0;
                group.AddMember(navEntity);
                entityMap[fieldEntity] = group; // Point to group

                // Only fire OnEntityAdded if this is a NEW group
                if (isNewGroup)
                {
                    OnEntityAdded?.Invoke(group);
                }
            }
            else
            {
                // Not grouped - add as normal individual entity
                entityMap[fieldEntity] = navEntity;
                OnEntityAdded?.Invoke(navEntity);
            }
        }

        /// <summary>
        /// Handles removing an entity from the cache.
        /// Manages group membership and dissolves groups when they become empty.
        /// </summary>
        private void HandleEntityRemoval(FieldEntity fieldEntity)
        {
            if (!entityMap.TryGetValue(fieldEntity, out var entity))
                return;

            if (entity is GroupEntity group)
            {
                // Remove from group
                group.RemoveMember(fieldEntity);

                if (group.Members.Count == 0)
                {
                    // Group is now empty - remove it from lookup dictionary and fire event
                    groupsByKey.Remove(group.GroupKey);
                    OnEntityRemoved?.Invoke(group);
                }
                // Note: We keep groups with 1+ members (Option 1)
            }
            else
            {
                // Regular individual entity
                OnEntityRemoved?.Invoke(entity);
            }

            entityMap.Remove(fieldEntity);
        }

        /// <summary>
        /// Finds an existing group for an entity or creates a new one if needed.
        /// Returns null if entity should not be grouped.
        /// </summary>
        private GroupEntity FindOrCreateGroup(NavigableEntity navEntity)
        {
            foreach (var strategy in enabledStrategies)
            {
                string groupKey = strategy.GetGroupKey(navEntity);
                if (groupKey != null)
                {
                    // O(1) lookup for existing group
                    if (groupsByKey.TryGetValue(groupKey, out var existingGroup))
                        return existingGroup;

                    // Create new group with explicit category
                    var newGroup = new GroupEntity(groupKey, strategy, navEntity.Category);
                    groupsByKey[groupKey] = newGroup;
                    return newGroup;
                }
            }

            return null; // Not groupable
        }

        /// <summary>
        /// Forces an immediate scan, bypassing the scan interval timer.
        /// </summary>
        public void ForceScan()
        {
            lastScanTime = Time.time;
            Scan();
        }

        /// <summary>
        /// Finds a NavigableEntity that matches the given GameObject.
        /// Walks up the hierarchy from the object checking each level against known entities.
        /// Useful for matching colliders/triggers to their owning entity.
        /// </summary>
        /// <param name="gameObject">The GameObject to match (e.g., a collider's gameObject)</param>
        /// <returns>The matching NavigableEntity, or null if not found</returns>
        public NavigableEntity FindEntityByGameObject(GameObject gameObject)
        {
            if (gameObject == null)
                return null;

            // Walk up the hierarchy from the object, checking each level against entities
            Transform current = gameObject.transform;

            while (current != null)
            {
                GameObject currentObj = current.gameObject;

                // Check this level against all entities
                var uniqueEntities = entityMap.Values.Distinct();

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
        /// Finds all entities at the specified world position.
        /// Uses Physics2D for collider-based entities (handles multi-tile),
        /// then checks entity cache for collider-less entities by tile position.
        /// </summary>
        /// <param name="worldPos">World position to check</param>
        /// <param name="mapHandle">Map accessor for tile conversion</param>
        /// <returns>List of entities at this position (no duplicates)</returns>
        public List<NavigableEntity> FindEntitiesAtPosition(Vector3 worldPos, IMapAccessor mapHandle)
        {
            var result = new List<NavigableEntity>();
            var processedEntities = new HashSet<NavigableEntity>();

            // === Phase 1: Physics2D check for collider-based entities ===
            // This correctly handles multi-tile entities since it checks collider overlap
            Vector2 point = new Vector2(worldPos.x, worldPos.y);
            Collider2D[] colliders = Physics2D.OverlapPointAll(point);

            if (colliders != null)
            {
                foreach (var collider in colliders)
                {
                    if (collider == null || collider.gameObject == null)
                        continue;

                    NavigableEntity matchedEntity = FindEntityByGameObject(collider.gameObject);

                    if (matchedEntity != null && !processedEntities.Contains(matchedEntity))
                    {
                        processedEntities.Add(matchedEntity);
                        result.Add(matchedEntity);
                    }
                }
            }

            // === Phase 2: Entity cache check for collider-less entities ===
            // For entities without colliders, check if their origin is on the position's tile
            if (mapHandle != null)
            {
                var positionTile = WorldToTile(worldPos, mapHandle);

                // Check each entity in the cache
                foreach (var entity in entityMap.Values.Distinct())
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

                            if (IsEntityOnTile(member, positionTile, mapHandle))
                            {
                                processedEntities.Add(member);
                                result.Add(member);
                            }
                        }
                    }
                    else
                    {
                        if (IsEntityOnTile(entity, positionTile, mapHandle))
                        {
                            processedEntities.Add(entity);
                            result.Add(entity);
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Checks if an entity's position is on the specified tile.
        /// </summary>
        /// <param name="entity">Entity to check</param>
        /// <param name="tile">Tile coordinates to check against</param>
        /// <param name="mapHandle">Map accessor for coordinate conversion</param>
        /// <returns>True if entity is on the tile</returns>
        public bool IsEntityOnTile(NavigableEntity entity, TileCoordinates tile, IMapAccessor mapHandle)
        {
            var gameEntity = entity.GameEntity;
            if (gameEntity?.transform == null)
                return false;

            var entityTile = WorldToTile(gameEntity.transform.localPosition, mapHandle);
            return entityTile == tile;
        }

        /// <summary>
        /// Gets the player's current world position.
        /// </summary>
        private Vector3 GetPlayerPosition()
        {
            var playerController = GameObjectCache.Get<FieldPlayerController>();
            if (playerController?.fieldPlayer == null)
                return Vector3.zero;

            return playerController.fieldPlayer.transform.position;
        }
    }
}
