using System;
using System.Collections.Generic;
using System.Linq;
using MelonLoader;

namespace FFVI_ScreenReader.Core.Systems
{
    /// <summary>
    /// Manages the lifecycle of mod systems.
    /// Handles registration, activation/deactivation, and update calls.
    /// </summary>
    public class SystemManager
    {
        private readonly List<ISystem> systems = new();
        private readonly HashSet<ISystem> activeSystems = new();
        private bool systemsSorted = false;
        private string currentScene = "";

        /// <summary>
        /// Registers a system to be managed.
        /// Systems are sorted by priority before the first update.
        /// </summary>
        public void Register(ISystem system)
        {
            if (system == null)
                throw new ArgumentNullException(nameof(system));

            if (systems.Contains(system))
            {
                MelonLogger.Warning($"[SystemManager] System '{system.Name}' is already registered");
                return;
            }

            systems.Add(system);
            systemsSorted = false;
            MelonLogger.Msg($"[SystemManager] Registered system: {system.Name} (priority {system.Priority})");
        }

        /// <summary>
        /// Unregisters a system and deactivates it if active.
        /// </summary>
        public void Unregister(ISystem system)
        {
            if (system == null)
                return;

            if (activeSystems.Contains(system))
            {
                try
                {
                    system.OnDeactivate();
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[SystemManager] Error deactivating '{system.Name}': {ex.Message}");
                }
                activeSystems.Remove(system);
            }

            systems.Remove(system);
            MelonLogger.Msg($"[SystemManager] Unregistered system: {system.Name}");
        }

        /// <summary>
        /// Updates all systems. Called each frame from the main mod.
        /// Checks IsActive for each system and handles activation/deactivation transitions.
        /// </summary>
        public void Update()
        {
            // Sort systems by priority on first update or after registration
            if (!systemsSorted)
            {
                systems.Sort((a, b) => a.Priority.CompareTo(b.Priority));
                systemsSorted = true;
                MelonLogger.Msg($"[SystemManager] Systems sorted by priority: {string.Join(", ", systems.Select(s => $"{s.Name}({s.Priority})"))}");
            }

            foreach (var system in systems)
            {
                try
                {
                    bool shouldBeActive = system.IsActive;
                    bool wasActive = activeSystems.Contains(system);

                    // Handle activation transition
                    if (shouldBeActive && !wasActive)
                    {
                        MelonLogger.Msg($"[SystemManager] Activating: {system.Name}");
                        system.OnActivate();
                        activeSystems.Add(system);
                    }
                    // Handle deactivation transition
                    else if (!shouldBeActive && wasActive)
                    {
                        MelonLogger.Msg($"[SystemManager] Deactivating: {system.Name}");
                        system.OnDeactivate();
                        activeSystems.Remove(system);
                    }

                    // Update if active
                    if (shouldBeActive)
                    {
                        system.Update();
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[SystemManager] Error in system '{system.Name}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Notifies all systems of a scene change.
        /// </summary>
        public void OnSceneChanged(string sceneName)
        {
            if (sceneName == currentScene)
                return;

            currentScene = sceneName;
            MelonLogger.Msg($"[SystemManager] Scene changed to: {sceneName}");

            foreach (var system in systems)
            {
                try
                {
                    system.OnSceneChanged(sceneName);
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[SystemManager] Error in '{system.Name}'.OnSceneChanged: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Deactivates and cleans up all systems.
        /// Call on mod unload.
        /// </summary>
        public void Shutdown()
        {
            MelonLogger.Msg("[SystemManager] Shutting down all systems...");

            // Deactivate in reverse priority order
            var reversedSystems = systems.AsEnumerable().Reverse().ToList();

            foreach (var system in reversedSystems)
            {
                if (activeSystems.Contains(system))
                {
                    try
                    {
                        MelonLogger.Msg($"[SystemManager] Deactivating: {system.Name}");
                        system.OnDeactivate();
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Error($"[SystemManager] Error deactivating '{system.Name}': {ex.Message}");
                    }
                }
            }

            activeSystems.Clear();
            systems.Clear();
            MelonLogger.Msg("[SystemManager] Shutdown complete");
        }

        /// <summary>
        /// Gets whether a specific system is currently active.
        /// </summary>
        public bool IsSystemActive(ISystem system)
        {
            return activeSystems.Contains(system);
        }

        /// <summary>
        /// Gets all registered systems.
        /// </summary>
        public IReadOnlyList<ISystem> RegisteredSystems => systems;

        /// <summary>
        /// Gets all currently active systems.
        /// </summary>
        public IReadOnlyCollection<ISystem> ActiveSystems => activeSystems;
    }
}
