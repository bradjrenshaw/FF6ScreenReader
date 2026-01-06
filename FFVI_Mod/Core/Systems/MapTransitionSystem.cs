using MelonLoader;
using FFVI_ScreenReader.Utils;
using FFVI_ScreenReader.Field;

namespace FFVI_ScreenReader.Core.Systems
{
    /// <summary>
    /// Tracks map transitions and announces new map names.
    /// Also triggers entity rescans after map changes.
    /// Implements ISystem for lifecycle management.
    /// </summary>
    public class MapTransitionSystem : ISystem
    {
        // ISystem implementation
        public string Name => "MapTransition";
        public int Priority => 10; // Run early, before other systems

        /// <summary>
        /// System is always active when we can access game data.
        /// </summary>
        public bool IsActive => Il2CppLast.Management.UserDataManager.Instance() != null;

        // Dependencies
        private readonly EntityCache entityCache;
        private readonly MapViewerSystem mapViewerSystem;

        // Map transition tracking
        private int lastAnnouncedMapId = -1;

        /// <summary>
        /// Creates a new MapTransitionSystem.
        /// </summary>
        /// <param name="entityCache">Entity cache for triggering rescans</param>
        /// <param name="mapViewerSystem">Map viewer to reset cursor on transitions</param>
        public MapTransitionSystem(EntityCache entityCache, MapViewerSystem mapViewerSystem)
        {
            this.entityCache = entityCache;
            this.mapViewerSystem = mapViewerSystem;
        }

        // ISystem lifecycle methods

        public void OnActivate()
        {
            MelonLogger.Msg("[MapTransitionSystem] Activating");
        }

        public void OnDeactivate()
        {
            MelonLogger.Msg("[MapTransitionSystem] Deactivating");
            // Reset tracking on deactivation so we re-detect on reactivation
            lastAnnouncedMapId = -1;
        }

        public void OnSceneChanged(string sceneName)
        {
            // Reset tracking on scene change
            lastAnnouncedMapId = -1;
        }

        public void Update()
        {
            CheckMapTransition();
        }

        public bool HandleInput()
        {
            // No input handling for this system
            return false;
        }

        /// <summary>
        /// Checks for map transitions and announces the new map name.
        /// </summary>
        private void CheckMapTransition()
        {
            try
            {
                var userDataManager = Il2CppLast.Management.UserDataManager.Instance();
                if (userDataManager != null)
                {
                    int currentMapId = userDataManager.CurrentMapId;
                    if (currentMapId != lastAnnouncedMapId && lastAnnouncedMapId != -1)
                    {
                        // Map has changed, announce the new map
                        string mapName = MapNameResolver.GetCurrentMapName();
                        FFVI_ScreenReaderMod.SpeakText($"Entering {mapName}", interrupt: false);
                        lastAnnouncedMapId = currentMapId;

                        // Reset map viewer cursor on map transition (silent)
                        mapViewerSystem?.SnapToPlayer(announce: false);

                        // Delay entity scan to allow new map to fully initialize
                        CoroutineManager.StartManaged(DelayedMapTransitionScan());
                    }
                    else if (lastAnnouncedMapId == -1)
                    {
                        // First run, just store the current map without announcing
                        lastAnnouncedMapId = currentMapId;
                    }
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[MapTransitionSystem] Error detecting map transition: {ex.Message}");
            }
        }

        /// <summary>
        /// Coroutine that delays entity scanning after map transition to allow entities to spawn.
        /// </summary>
        private System.Collections.IEnumerator DelayedMapTransitionScan()
        {
            // Wait 0.5 seconds for new map entities to spawn
            yield return new UnityEngine.WaitForSeconds(0.5f);

            // Scan for entities - EntityNavigator will be updated via OnEntityAdded/OnEntityRemoved events
            entityCache.ForceScan();

            MelonLogger.Msg("[MapTransitionSystem] Delayed map transition entity scan completed");
        }
    }
}
