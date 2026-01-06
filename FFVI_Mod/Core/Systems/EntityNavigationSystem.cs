using System.Linq;
using UnityEngine;
using MelonLoader;
using Il2CppLast.Map;
using FFVI_ScreenReader.Field;
using FFVI_ScreenReader.Utils;

namespace FFVI_ScreenReader.Core.Systems
{
    /// <summary>
    /// Manages entity navigation on the field map.
    /// Handles cycling through entities, category filtering, and pathfinding.
    /// Implements ISystem for lifecycle management.
    /// </summary>
    public class EntityNavigationSystem : ISystem
    {
        // ISystem implementation
        public string Name => "EntityNavigation";
        public int Priority => 50; // Run before sonar (100)

        /// <summary>
        /// System is active when we're on a field map.
        /// </summary>
        public bool IsActive => IsOnFieldMap();

        // Core components
        private readonly EntityCache entityCache;
        private readonly EntityNavigator entityNavigator;

        // Filter state
        private bool filterByPathfinding = false;
        private bool filterMapExits = false;

        // Preferences
        private MelonPreferences_Category prefsCategory;
        private MelonPreferences_Entry<bool> prefPathfindingFilter;
        private MelonPreferences_Entry<bool> prefMapExitFilter;

        // Scene tracking
        private string currentScene = "";

        /// <summary>
        /// Gets the entity navigator for external access (e.g., teleportation).
        /// </summary>
        public EntityNavigator Navigator => entityNavigator;

        /// <summary>
        /// Creates a new EntityNavigationSystem.
        /// </summary>
        /// <param name="entityCache">Entity cache for entity data</param>
        public EntityNavigationSystem(EntityCache entityCache)
        {
            this.entityCache = entityCache;

            // Initialize preferences
            prefsCategory = MelonPreferences.CreateCategory("FFVI_ScreenReader");
            prefPathfindingFilter = prefsCategory.CreateEntry<bool>("PathfindingFilter", false, "Pathfinding Filter", "Only show entities with valid paths when cycling");
            prefMapExitFilter = prefsCategory.CreateEntry<bool>("MapExitFilter", false, "Map Exit Filter", "Filter multiple map exits to the same destination, showing only the closest one");

            // Load saved preferences
            filterByPathfinding = prefPathfindingFilter.Value;
            filterMapExits = prefMapExitFilter.Value;

            // Initialize navigator
            entityNavigator = new EntityNavigator(entityCache);
            entityNavigator.FilterByPathfinding = filterByPathfinding;
            entityNavigator.FilterMapExits = filterMapExits;
        }

        /// <summary>
        /// Checks if we're currently on a field map.
        /// </summary>
        private bool IsOnFieldMap()
        {
            var playerController = GameObjectCache.Get<FieldPlayerController>();
            return playerController?.fieldPlayer != null;
        }

        // ISystem lifecycle methods

        public void OnActivate()
        {
            MelonLogger.Msg("[EntityNavigationSystem] Activating");
        }

        public void OnDeactivate()
        {
            MelonLogger.Msg("[EntityNavigationSystem] Deactivating");
        }

        public void OnSceneChanged(string sceneName)
        {
            currentScene = sceneName;
        }

        public void Update()
        {
            // EntityNavigator handles its own updates via EntityCache events
            // No per-frame update needed
        }

        /// <summary>
        /// Handles input for entity navigation.
        /// </summary>
        /// <returns>True if input was consumed</returns>
        public bool HandleInput()
        {
            // Early exit if no keys pressed
            if (!Input.anyKeyDown)
                return false;

            bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            // Ctrl+Arrow: Teleport relative to selected entity
            if (ctrlHeld)
            {
                if (Input.GetKeyDown(KeyCode.UpArrow))
                {
                    TeleportInDirection(new Vector2(0, 16)); // North
                    return true;
                }
                if (Input.GetKeyDown(KeyCode.DownArrow))
                {
                    TeleportInDirection(new Vector2(0, -16)); // South
                    return true;
                }
                if (Input.GetKeyDown(KeyCode.LeftArrow))
                {
                    TeleportInDirection(new Vector2(-16, 0)); // West
                    return true;
                }
                if (Input.GetKeyDown(KeyCode.RightArrow))
                {
                    TeleportInDirection(new Vector2(16, 0)); // East
                    return true;
                }
            }

            // J or [ - cycle backward (Shift = category, normal = entity)
            if (!ctrlHeld && (Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.LeftBracket)))
            {
                if (shiftHeld)
                    CyclePreviousCategory();
                else
                    CyclePrevious();
                return true;
            }

            // K - repeat current entity (not Ctrl+K)
            if (!ctrlHeld && !shiftHeld && Input.GetKeyDown(KeyCode.K))
            {
                AnnounceEntityOnly();
                return true;
            }

            // Shift+K or 0 - reset to All category
            if ((Input.GetKeyDown(KeyCode.K) && shiftHeld) || Input.GetKeyDown(KeyCode.Alpha0))
            {
                ResetToAllCategory();
                return true;
            }

            // L or ] - cycle forward (Shift = category, normal = entity)
            if (!ctrlHeld && (Input.GetKeyDown(KeyCode.L) || Input.GetKeyDown(KeyCode.RightBracket)))
            {
                if (shiftHeld)
                    CycleNextCategory();
                else
                    CycleNext();
                return true;
            }

            // P or \ - pathfind (Shift = toggle filter, normal = announce path)
            if (Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.Backslash))
            {
                if (shiftHeld)
                    TogglePathfindingFilter();
                else
                    AnnounceCurrentEntity();
                return true;
            }

            // = (Equals) - cycle to next category
            if (Input.GetKeyDown(KeyCode.Equals))
            {
                CycleNextCategory();
                return true;
            }

            // - (Minus) - cycle to previous category
            if (Input.GetKeyDown(KeyCode.Minus))
            {
                CyclePreviousCategory();
                return true;
            }

            // Shift+M - toggle map exit filter
            if (Input.GetKeyDown(KeyCode.M) && shiftHeld)
            {
                ToggleMapExitFilter();
                return true;
            }

            return false;
        }

        // Entity cycling methods

        /// <summary>
        /// Cycles to the next entity in the navigation list.
        /// </summary>
        public void CycleNext()
        {
            if (entityNavigator.CycleNext())
            {
                AnnounceEntityOnly();
            }
            else
            {
                if (entityNavigator.EntityCount == 0)
                {
                    FFVI_ScreenReaderMod.SpeakText("No entities nearby");
                }
                else
                {
                    FFVI_ScreenReaderMod.SpeakText("No pathable entities found");
                }
            }
        }

        /// <summary>
        /// Cycles to the previous entity in the navigation list.
        /// </summary>
        public void CyclePrevious()
        {
            if (entityNavigator.CyclePrevious())
            {
                AnnounceEntityOnly();
            }
            else
            {
                if (entityNavigator.EntityCount == 0)
                {
                    FFVI_ScreenReaderMod.SpeakText("No entities nearby");
                }
                else
                {
                    FFVI_ScreenReaderMod.SpeakText("No pathable entities found");
                }
            }
        }

        /// <summary>
        /// Announces the currently selected entity without path info.
        /// </summary>
        public void AnnounceEntityOnly()
        {
            var entity = entityNavigator.CurrentEntity;
            if (entity == null)
            {
                FFVI_ScreenReaderMod.SpeakText("No entities nearby");
                return;
            }

            var playerController = GameObjectCache.Get<FieldPlayerController>();
            if (playerController?.fieldPlayer == null)
            {
                FFVI_ScreenReaderMod.SpeakText("Not in field");
                return;
            }

            // CRITICAL: Touch controller uses localPosition, NOT position!
            Vector3 playerPos = playerController.fieldPlayer.transform.localPosition;
            Vector3 targetPos = entity.GameEntity.transform.localPosition;

            string formatted = entity.FormatDescription(playerController.fieldPlayer.transform.position);

            // Check if path exists
            var pathInfo = FieldNavigationHelper.FindPathTo(
                playerPos,
                targetPos,
                playerController.mapHandle,
                playerController.fieldPlayer
            );

            // Announce entity info + path status + count at the end
            string countSuffix = $", {entityNavigator.CurrentIndex + 1} of {entityNavigator.EntityCount}";
            string announcement = pathInfo.Success ? $"{formatted}{countSuffix}" : $"{formatted}, no path{countSuffix}";
            FFVI_ScreenReaderMod.SpeakText(announcement);
        }

        /// <summary>
        /// Announces the current entity with full path information.
        /// </summary>
        public void AnnounceCurrentEntity()
        {
            var entity = entityNavigator.CurrentEntity;
            if (entity == null)
            {
                FFVI_ScreenReaderMod.SpeakText("No entities nearby");
                return;
            }

            var playerController = GameObjectCache.Get<FieldPlayerController>();
            if (playerController?.fieldPlayer == null)
            {
                FFVI_ScreenReaderMod.SpeakText("Not in field");
                return;
            }

            // CRITICAL: Touch controller uses localPosition, NOT position!
            Vector3 playerPos = playerController.fieldPlayer.transform.localPosition;
            Vector3 targetPos = entity.GameEntity.transform.localPosition;

            var pathInfo = FieldNavigationHelper.FindPathTo(
                playerPos,
                targetPos,
                playerController.mapHandle,
                playerController.fieldPlayer
            );

            string announcement;
            if (pathInfo.Success)
            {
                // Just announce the path - user knows what entity they're navigating to from cycling
                announcement = $"{pathInfo.Description}";
            }
            else
            {
                announcement = "no path";
            }

            FFVI_ScreenReaderMod.SpeakText(announcement);
        }

        // Category cycling methods

        /// <summary>
        /// Cycles to the next category.
        /// </summary>
        public void CycleNextCategory()
        {
            var cycleableCategories = CategoryRegistry.GetCycleableCategories().ToList();
            int currentIndex = cycleableCategories.IndexOf(entityNavigator.CurrentCategory);
            if (currentIndex < 0) currentIndex = 0;

            int nextIndex = (currentIndex + 1) % cycleableCategories.Count;
            EntityCategory newCategory = cycleableCategories[nextIndex];

            entityNavigator.SetCategory(newCategory);
            AnnounceCategoryChange();
        }

        /// <summary>
        /// Cycles to the previous category.
        /// </summary>
        public void CyclePreviousCategory()
        {
            var cycleableCategories = CategoryRegistry.GetCycleableCategories().ToList();
            int currentIndex = cycleableCategories.IndexOf(entityNavigator.CurrentCategory);
            if (currentIndex < 0) currentIndex = 0;

            int prevIndex = currentIndex - 1;
            if (prevIndex < 0) prevIndex = cycleableCategories.Count - 1;
            EntityCategory newCategory = cycleableCategories[prevIndex];

            entityNavigator.SetCategory(newCategory);
            AnnounceCategoryChange();
        }

        /// <summary>
        /// Resets to the "All" category.
        /// </summary>
        public void ResetToAllCategory()
        {
            if (entityNavigator.CurrentCategory == EntityCategory.All)
            {
                FFVI_ScreenReaderMod.SpeakText("Already in All category");
                return;
            }

            entityNavigator.SetCategory(EntityCategory.All);
            AnnounceCategoryChange();
        }

        /// <summary>
        /// Announces the current category and entity count.
        /// </summary>
        private void AnnounceCategoryChange()
        {
            string categoryName = CategoryRegistry.GetDisplayName(entityNavigator.CurrentCategory);
            int entityCount = entityNavigator.EntityCount;

            string announcement = $"Category: {categoryName}, {entityCount} {(entityCount == 1 ? "entity" : "entities")}";
            FFVI_ScreenReaderMod.SpeakText(announcement);
        }

        // Filter toggle methods

        /// <summary>
        /// Toggles the pathfinding filter on/off.
        /// </summary>
        public void TogglePathfindingFilter()
        {
            filterByPathfinding = !filterByPathfinding;

            entityNavigator.FilterByPathfinding = filterByPathfinding;

            prefPathfindingFilter.Value = filterByPathfinding;
            prefsCategory.SaveToFile(false);

            string status = filterByPathfinding ? "on" : "off";
            FFVI_ScreenReaderMod.SpeakText($"Pathfinding filter {status}");
        }

        /// <summary>
        /// Toggles the map exit deduplication filter on/off.
        /// </summary>
        public void ToggleMapExitFilter()
        {
            filterMapExits = !filterMapExits;

            entityNavigator.FilterMapExits = filterMapExits;
            entityNavigator.RebuildNavigationList();

            prefMapExitFilter.Value = filterMapExits;
            prefsCategory.SaveToFile(false);

            string status = filterMapExits ? "on" : "off";
            FFVI_ScreenReaderMod.SpeakText($"Map exit filter {status}");
        }

        // Teleportation (debug feature)

        /// <summary>
        /// Teleports the player in a direction relative to the selected entity.
        /// </summary>
        /// <param name="offset">Direction offset in world units (16 = 1 tile)</param>
        public void TeleportInDirection(Vector2 offset)
        {
            var entity = entityNavigator.CurrentEntity;
            if (entity == null)
            {
                FFVI_ScreenReaderMod.SpeakText("No entity selected");
                return;
            }

            var playerController = GameObjectCache.Get<FieldPlayerController>();
            if (playerController?.fieldPlayer == null)
            {
                FFVI_ScreenReaderMod.SpeakText("Player not available");
                return;
            }

            var player = playerController.fieldPlayer;

            // Calculate offset position relative to the target entity
            // One cell = 16 units
            Vector3 targetPos = entity.Position;
            Vector3 newPos = new Vector3(targetPos.x + offset.x, targetPos.y + offset.y, targetPos.z);

            // Instantly teleport by setting localPosition directly
            player.transform.localPosition = newPos;

            // Announce the direction
            string direction = GetDirectionName(offset);
            FFVI_ScreenReaderMod.SpeakText($"{direction} of {entity.Name}");
        }

        /// <summary>
        /// Gets a friendly name for a direction offset.
        /// </summary>
        private string GetDirectionName(Vector2 offset)
        {
            if (offset.y > 0) return "North";
            if (offset.y < 0) return "South";
            if (offset.x > 0) return "East";
            if (offset.x < 0) return "West";
            return "At";
        }
    }
}
