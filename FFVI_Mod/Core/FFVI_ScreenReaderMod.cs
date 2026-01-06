using System.Linq;
using MelonLoader;
using FFVI_ScreenReader.Utils;
using FFVI_ScreenReader.Field;
using FFVI_ScreenReader.Audio;
using FFVI_ScreenReader.Core.Systems;
using UnityEngine;
using Il2Cpp;
using Il2CppLast.Map;

[assembly: MelonInfo(typeof(FFVI_ScreenReader.Core.FFVI_ScreenReaderMod), "FFVI Screen Reader", "1.0.0", "Zachary Kline")]
[assembly: MelonGame("SQUARE ENIX, Inc.", "FINAL FANTASY VI")]

namespace FFVI_ScreenReader.Core
{
    /// <summary>
    /// Main mod class for FFVI Screen Reader.
    /// Provides screen reader accessibility support for Final Fantasy VI Pixel Remaster.
    /// </summary>
    public class FFVI_ScreenReaderMod : MelonMod
    {
        private static TolkWrapper tolk;
        private InputManager inputManager;
        private EntityCache entityCache;
        private SonarSystem sonarSystem;
        private EntityNavigationSystem entityNavigationSystem;
        private MapViewerSystem mapViewerSystem;
        private SystemManager systemManager;

        // Entity scanning
        private const float ENTITY_SCAN_INTERVAL = 5f;

        // Map transition tracking
        private int lastAnnouncedMapId = -1;

        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("FFVI Screen Reader Mod loaded!");

            // Subscribe to scene load events for automatic component caching
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (UnityEngine.Events.UnityAction<UnityEngine.SceneManagement.Scene, UnityEngine.SceneManagement.LoadSceneMode>)OnSceneLoaded;

            // Initialize Tolk for screen reader support
            tolk = new TolkWrapper();
            tolk.Load();

            // Initialize entity cache
            entityCache = new EntityCache(ENTITY_SCAN_INTERVAL);

            // Initialize system manager
            systemManager = new SystemManager();

            // Initialize map viewer system (depends on entityCache)
            mapViewerSystem = new MapViewerSystem(entityCache);
            systemManager.Register(mapViewerSystem);

            // Initialize entity navigation system (depends on entityCache)
            entityNavigationSystem = new EntityNavigationSystem(entityCache);
            systemManager.Register(entityNavigationSystem);

            // Initialize sonar system (depends on entityCache)
            sonarSystem = new SonarSystem(entityCache);
            systemManager.Register(sonarSystem);

            // Initialize input manager (depends on systemManager)
            inputManager = new InputManager(this, systemManager);
        }

        public override void OnDeinitializeMelon()
        {
            // Unsubscribe from scene load events
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= (UnityEngine.Events.UnityAction<UnityEngine.SceneManagement.Scene, UnityEngine.SceneManagement.LoadSceneMode>)OnSceneLoaded;

            CoroutineManager.CleanupAll();
            systemManager?.Shutdown();
            sonarSystem?.Cleanup();
            tolk?.Unload();
        }

        /// <summary>
        /// Called when a new scene is loaded.
        /// Automatically caches commonly-used Unity components to avoid expensive FindObjectOfType calls.
        /// </summary>
        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            try
            {
                LoggerInstance.Msg($"[ComponentCache] Scene loaded: {scene.name}");

                // Notify all systems of scene change
                systemManager?.OnSceneChanged(scene.name);

                // Try to find and cache FieldPlayerController
                var playerController = UnityEngine.Object.FindObjectOfType<Il2CppLast.Map.FieldPlayerController>();
                if (playerController != null)
                {
                    Utils.GameObjectCache.Register(playerController);
                    LoggerInstance.Msg($"[ComponentCache] Cached FieldPlayerController: {playerController.gameObject?.name}");
                }
                else
                {
                    LoggerInstance.Msg("[ComponentCache] No FieldPlayerController found in scene");
                }

                // Try to find and cache FieldMap
                var fieldMap = UnityEngine.Object.FindObjectOfType<Il2Cpp.FieldMap>();
                if (fieldMap != null)
                {
                    Utils.GameObjectCache.Register(fieldMap);
                    LoggerInstance.Msg($"[ComponentCache] Cached FieldMap: {fieldMap.gameObject?.name}");

                    // Delay entity scan to allow scene to fully initialize
                    CoroutineManager.StartManaged(DelayedInitialScan());
                }
                else
                {
                    LoggerInstance.Msg("[ComponentCache] No FieldMap found in scene");
                }
            }
            catch (System.Exception ex)
            {
                LoggerInstance.Error($"[ComponentCache] Error in OnSceneLoaded: {ex.Message}");
            }
        }

        /// <summary>
        /// Coroutine that delays entity scanning to allow scene to fully initialize.
        /// </summary>
        private System.Collections.IEnumerator DelayedInitialScan()
        {
            // Wait 0.5 seconds for scene to fully initialize and entities to spawn
            yield return new UnityEngine.WaitForSeconds(0.5f);

            // Scan for entities - EntityNavigator will be updated via OnEntityAdded events
            // No need to call RebuildNavigationList() as the event handlers already filter and add entities
            entityCache.ForceScan();

            LoggerInstance.Msg("[ComponentCache] Delayed initial entity scan completed");
        }

        /// <summary>
        /// Coroutine that delays entity scanning after map transition to allow entities to spawn.
        /// </summary>
        private System.Collections.IEnumerator DelayedMapTransitionScan()
        {
            // Wait 0.5 seconds for new map entities to spawn
            yield return new UnityEngine.WaitForSeconds(0.5f);

            // Scan for entities - EntityNavigator will be updated via OnEntityAdded/OnEntityRemoved events
            // No need to call RebuildNavigationList() as the event handlers already filter and add entities
            entityCache.ForceScan();

            LoggerInstance.Msg("[ComponentCache] Delayed map transition entity scan completed");
        }

        public override void OnUpdate()
        {
            // Update entity cache (handles periodic rescanning)
            entityCache.Update();

            // Update all managed systems (handles activation/deactivation and updates)
            systemManager.Update();

            // Check for map transitions
            CheckMapTransition();

            // Handle all input
            inputManager.Update();
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
                        string mapName = Field.MapNameResolver.GetCurrentMapName();
                        SpeakText($"Entering {mapName}", interrupt: false);
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
                LoggerInstance.Warning($"Error detecting map transition: {ex.Message}");
            }
        }

        private void AnnounceCurrentCharacterStatus()
        {
            try
            {
                // Get the currently active character from the battle patch
                var activeCharacter = FFVI_ScreenReader.Patches.BattleMenuController_SetCommandSelectTarget_Patch.CurrentActiveCharacter;

                if (activeCharacter == null)
                {
                    SpeakText("Not in battle or no active character");
                    return;
                }

                if (activeCharacter.ownedCharacterData == null)
                {
                    SpeakText("No character data available");
                    return;
                }

                var charData = activeCharacter.ownedCharacterData;
                string characterName = charData.Name;

                // Read HP/MP directly from character parameter
                if (charData.parameter == null)
                {
                    SpeakText($"{characterName}, status information not available");
                    return;
                }

                var param = charData.parameter;
                var statusParts = new System.Collections.Generic.List<string>();
                statusParts.Add(characterName);

                // Add HP
                int currentHP = param.CurrentHP;
                int maxHP = param.ConfirmedMaxHp();
                statusParts.Add($"HP {currentHP} of {maxHP}");

                // Add MP
                int currentMP = param.CurrentMP;
                int maxMP = param.ConfirmedMaxMp();
                statusParts.Add($"MP {currentMP} of {maxMP}");

                // Add status conditions
                if (param.CurrentConditionList != null && param.CurrentConditionList.Count > 0)
                {
                    var conditionNames = new System.Collections.Generic.List<string>();
                    foreach (var condition in param.CurrentConditionList)
                    {
                        if (condition != null)
                        {
                            // Get the condition name from the message ID
                            string conditionMesId = condition.MesIdName;
                            if (!string.IsNullOrEmpty(conditionMesId))
                            {
                                var messageManager = Il2CppLast.Management.MessageManager.Instance;
                                if (messageManager != null)
                                {
                                    string conditionName = messageManager.GetMessage(conditionMesId);
                                    if (!string.IsNullOrEmpty(conditionName))
                                    {
                                        conditionNames.Add(conditionName);
                                    }
                                }
                            }
                        }
                    }

                    if (conditionNames.Count > 0)
                    {
                        statusParts.Add("Status: " + string.Join(", ", conditionNames));
                    }
                }

                string statusMessage = string.Join(", ", statusParts);
                SpeakText(statusMessage);
            }
            catch (System.Exception ex)
            {
                LoggerInstance.Warning($"Error announcing character status: {ex.Message}");
                SpeakText("Error reading character status");
            }
        }

        internal void AnnounceGilAmount()
        {
            try
            {
                var userDataManager = Il2CppLast.Management.UserDataManager.Instance();

                if (userDataManager == null)
                {
                    SpeakText("User data not available");
                    return;
                }

                int gil = userDataManager.OwendGil;
                string gilMessage = $"{gil:N0} gil";

                SpeakText(gilMessage);
            }
            catch (System.Exception ex)
            {
                LoggerInstance.Warning($"Error announcing gil amount: {ex.Message}");
                SpeakText("Error reading gil amount");
            }
        }

        internal void AnnounceCurrentMap()
        {
            try
            {
                string mapName = Field.MapNameResolver.GetCurrentMapName();
                SpeakText(mapName);
            }
            catch (System.Exception ex)
            {
                LoggerInstance.Warning($"Error announcing current map: {ex.Message}");
                SpeakText("Error reading map name");
            }
        }

        /// <summary>
        /// Announces airship status if on airship, otherwise announces character status in battle.
        /// </summary>
        internal void AnnounceAirshipOrCharacterStatus()
        {
            // Check if we're on the airship by finding an active airship controller with input enabled
            var allControllers = UnityEngine.Object.FindObjectsOfType<FieldPlayerController>();
            Il2CppLast.Map.FieldPlayerKeyAirshipController activeAirshipController = null;

            foreach (var controller in allControllers)
            {
                if (controller != null && controller.gameObject != null && controller.gameObject.activeInHierarchy)
                {
                    var airshipController = controller.TryCast<Il2CppLast.Map.FieldPlayerKeyAirshipController>();
                    if (airshipController != null && airshipController.InputEnable)
                    {
                        activeAirshipController = airshipController;
                        break;
                    }
                }
            }

            if (activeAirshipController != null)
            {
                AnnounceAirshipStatus();
            }
            else
            {
                // Fall back to battle character status
                AnnounceCurrentCharacterStatus();
            }
        }

        private void AnnounceAirshipStatus()
        {
            try
            {
                var fieldMap = Utils.GameObjectCache.Get<FieldMap>();
                if (fieldMap == null || fieldMap.fieldController == null)
                {
                    SpeakText("Airship status not available");
                    return;
                }

                // Find the active airship controller with input enabled
                var allControllers = UnityEngine.Object.FindObjectsOfType<FieldPlayerController>();
                Il2CppLast.Map.FieldPlayerKeyAirshipController airshipController = null;

                foreach (var controller in allControllers)
                {
                    if (controller != null && controller.gameObject != null && controller.gameObject.activeInHierarchy)
                    {
                        var asAirship = controller.TryCast<Il2CppLast.Map.FieldPlayerKeyAirshipController>();
                        if (asAirship != null && asAirship.InputEnable)
                        {
                            airshipController = asAirship;
                            break;
                        }
                    }
                }

                if (airshipController == null || airshipController.fieldPlayer == null)
                {
                    SpeakText("Not on airship");
                    return;
                }

                var statusParts = new System.Collections.Generic.List<string>();

                // Get current direction in degrees
                float rotationZ = fieldMap.fieldController.GetZAxisRotateBirdCamera();
                // Normalize to 0-360 range
                float normalizedRotation = ((rotationZ % 360) + 360) % 360;
                // Mirror the rotation to match our E/W swapped compass directions
                float mirroredRotation = (360 - normalizedRotation) % 360;
                statusParts.Add($"Heading {mirroredRotation:F0} degrees");

                // Get current altitude
                float altitudeRatio = fieldMap.fieldController.GetFlightAltitudeFieldOfViewRatio(true);
                string altitude = Utils.AirshipNavigationReader.GetAltitudeDescription(altitudeRatio);
                statusParts.Add(altitude);

                // Get landing zone status
                Vector3 airshipPos = airshipController.fieldPlayer.transform.localPosition;
                string terrainName;
                bool canLand;
                bool success = Utils.AirshipNavigationReader.GetTerrainAtPosition(
                    airshipPos,
                    fieldMap.fieldController,
                    out terrainName,
                    out canLand
                );

                if (success)
                {
                    string landingStatus = Utils.AirshipNavigationReader.BuildLandingZoneAnnouncement(terrainName, canLand);
                    statusParts.Add(landingStatus);
                }

                string statusMessage = string.Join(". ", statusParts);
                SpeakText(statusMessage);
            }
            catch (System.Exception ex)
            {
                LoggerInstance.Warning($"Error announcing airship status: {ex.Message}");
                SpeakText("Error reading airship status");
            }
        }

        /// <summary>
        /// Debug: Announces detailed info about all colliders at the map viewer cursor position.
        /// </summary>
        internal void DebugCollidersAtCursor()
        {
            var playerController = Utils.GameObjectCache.Get<Il2CppLast.Map.FieldPlayerController>();
            if (playerController?.fieldPlayer == null)
            {
                SpeakText("Not in field");
                return;
            }

            Vector3 cursorPos = mapViewerSystem?.GetEffectiveCursorPosition() ?? playerController.fieldPlayer.transform.localPosition;

            int playerLayer = playerController.fieldPlayer.gameObject.layer;
            Vector2 point = new Vector2(cursorPos.x, cursorPos.y);
            Collider2D[] colliders = UnityEngine.Physics2D.OverlapPointAll(point);

            if (colliders == null || colliders.Length == 0)
            {
                SpeakText("No colliders at cursor");
                return;
            }

            var parts = new System.Collections.Generic.List<string>();
            parts.Add($"{colliders.Length} colliders");

            foreach (var collider in colliders)
            {
                if (collider == null || collider.gameObject == null)
                    continue;

                string objName = collider.gameObject.name;
                int layer = collider.gameObject.layer;
                string layerMatch = layer == playerLayer ? "player layer" : $"layer {layer}";

                // Try to get FieldColliderEntity and its enable state
                var colliderEntity = collider.gameObject.GetComponent<Il2CppLast.Entity.Field.FieldColliderEntity>();
                if (colliderEntity == null)
                    colliderEntity = collider.gameObject.GetComponentInParent<Il2CppLast.Entity.Field.FieldColliderEntity>();

                string enableState = "";
                if (colliderEntity != null)
                {
                    try
                    {
                        var il2cppType = colliderEntity.GetIl2CppType();
                        var bindingFlags = Il2CppSystem.Reflection.BindingFlags.NonPublic |
                                           Il2CppSystem.Reflection.BindingFlags.Instance;
                        var enableField = il2cppType.GetField("enable", bindingFlags);
                        bool enable = enableField != null && enableField.GetValue(colliderEntity).Unbox<bool>();
                        enableState = enable ? "enabled" : "disabled";
                    }
                    catch
                    {
                        enableState = "unknown";
                    }
                }

                // Try to get FieldEntity directly from the GameObject hierarchy
                var fieldEntity = collider.gameObject.GetComponent<Il2CppLast.Entity.Field.FieldEntity>();
                if (fieldEntity == null)
                    fieldEntity = collider.gameObject.GetComponentInParent<Il2CppLast.Entity.Field.FieldEntity>();

                string gameTypeInfo = "no FieldEntity";
                if (fieldEntity?.Property != null)
                {
                    var objType = (Il2Cpp.MapConstants.ObjectType)fieldEntity.Property.ObjectType;
                    int objTypeInt = fieldEntity.Property.ObjectType;
                    string entityName = fieldEntity.Property.Name ?? fieldEntity.gameObject?.name ?? "unknown";
                    gameTypeInfo = $"ObjectType: {objType} ({objTypeInt}), name: {entityName}";
                }
                else if (fieldEntity != null)
                {
                    gameTypeInfo = $"FieldEntity but no Property";
                }

                // Also check if it's in our cache
                var matchedEntity = entityCache.FindEntityByGameObject(collider.gameObject);
                string cacheInfo;
                bool wouldBlock;
                if (matchedEntity != null)
                {
                    wouldBlock = matchedEntity.BlocksPathing;
                    cacheInfo = $"cached as {matchedEntity.GetType().Name}, BlocksPathing={matchedEntity.BlocksPathing}";
                }
                else
                {
                    wouldBlock = true; // not cached = assumes blocking
                    cacheInfo = "NOT CACHED - WILL BLOCK";
                }

                string entityInfo = $"{gameTypeInfo}, {cacheInfo}";

                string colliderInfo = colliderEntity != null
                    ? $"{objName}, {layerMatch}, {enableState}, {entityInfo}"
                    : $"{objName}, {layerMatch}, not FieldColliderEntity, {entityInfo}";

                parts.Add(colliderInfo);
            }

            SpeakText(string.Join(". ", parts));
        }

        /// <summary>
        /// Toggles continuous sonar mode on/off.
        /// When active, plays continuous tones for blocked directions.
        /// </summary>
        internal void ToggleSonarMode()
        {
            bool isNowActive = sonarSystem.Toggle();
            string status = isNowActive ? "Sonar on" : "Sonar off";
            SpeakText(status);
        }

        /// <summary>
        /// Plays a test exit.wav sound for debugging entity sonar.
        /// </summary>
        internal void PlayTestExitSound()
        {
            SpeakText("Playing exit sound");
            sonarSystem.PlayTestSound("exit.wav");
        }

        /// <summary>
        /// Speak text through the screen reader.
        /// Thread-safe: TolkWrapper uses locking to prevent concurrent native calls.
        /// </summary>
        /// <param name="text">Text to speak</param>
        /// <param name="interrupt">Whether to interrupt current speech (true for user actions, false for game events)</param>
        public static void SpeakText(string text, bool interrupt = true)
        {
            tolk?.Speak(text, interrupt);
        }
    }
}
