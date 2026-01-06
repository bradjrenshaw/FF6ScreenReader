using UnityEngine;
using UnityEngine.EventSystems;
using Il2Cpp;
using FFVI_ScreenReader.Core.Systems;

namespace FFVI_ScreenReader.Core
{
    /// <summary>
    /// Manages keyboard input handling for the screen reader mod.
    /// Delegates system-specific input to SystemManager, handles global hotkeys directly.
    /// </summary>
    public class InputManager
    {
        private readonly FFVI_ScreenReaderMod mod;
        private readonly SystemManager systemManager;
        private Il2CppSerial.FF6.UI.KeyInput.StatusDetailsController cachedStatusController;

        public InputManager(FFVI_ScreenReaderMod mod, SystemManager systemManager)
        {
            this.mod = mod;
            this.systemManager = systemManager;
        }

        /// <summary>
        /// Called every frame to check for input and route hotkeys.
        /// Should be called after SystemManager.Update().
        /// </summary>
        public void Update()
        {
            // Early exit if no keys pressed this frame
            if (!Input.anyKeyDown)
            {
                return;
            }

            // Check if ANY Unity InputField is focused - if so, let all keys pass through
            if (IsInputFieldFocused())
            {
                return;
            }

            // Check if status details screen is active - handle separately
            if (IsStatusScreenActive())
            {
                HandleStatusScreenInput();
                // Don't return - global hotkeys should still work on status screen
            }
            else
            {
                // Let active systems handle their input first
                if (systemManager.HandleInput())
                {
                    return; // Input was consumed by a system
                }
            }

            // Handle global hotkeys (work everywhere, not system-specific)
            HandleGlobalInput();
        }

        /// <summary>
        /// Checks if a Unity InputField is currently focused (player is typing).
        /// </summary>
        private bool IsInputFieldFocused()
        {
            try
            {
                if (EventSystem.current == null)
                    return false;

                var currentObj = EventSystem.current.currentSelectedGameObject;
                if (currentObj == null)
                    return false;

                return currentObj.TryGetComponent(out UnityEngine.UI.InputField inputField);
            }
            catch (System.Exception ex)
            {
                MelonLoader.MelonLogger.Warning($"Error checking input field state: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks if the status details screen is currently active.
        /// </summary>
        private bool IsStatusScreenActive()
        {
            if (cachedStatusController == null || cachedStatusController.gameObject == null)
            {
                cachedStatusController = Utils.GameObjectCache.Get<Il2CppSerial.FF6.UI.KeyInput.StatusDetailsController>();
            }

            return cachedStatusController != null &&
                   cachedStatusController.gameObject != null &&
                   cachedStatusController.gameObject.activeInHierarchy;
        }

        /// <summary>
        /// Handles input when on the status details screen.
        /// </summary>
        private void HandleStatusScreenInput()
        {
            if (IsCtrlHeld())
                return;

            // J/[ announces physical stats, L/] announces magical stats
            if (Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.LeftBracket))
            {
                string physicalStats = FFVI_ScreenReader.Menus.StatusDetailsReader.ReadPhysicalStats();
                FFVI_ScreenReaderMod.SpeakText(physicalStats);
            }

            if (Input.GetKeyDown(KeyCode.L) || Input.GetKeyDown(KeyCode.RightBracket))
            {
                string magicalStats = FFVI_ScreenReader.Menus.StatusDetailsReader.ReadMagicalStats();
                FFVI_ScreenReaderMod.SpeakText(magicalStats);
            }
        }

        /// <summary>
        /// Handles global input (works everywhere, not system-specific).
        /// </summary>
        private void HandleGlobalInput()
        {
            bool shiftHeld = IsShiftHeld();

            // H: Announce airship heading or character health
            if (Input.GetKeyDown(KeyCode.H))
            {
                mod.AnnounceAirshipOrCharacterStatus();
            }

            // G: Announce gil
            if (Input.GetKeyDown(KeyCode.G))
            {
                mod.AnnounceGilAmount();
            }

            // M: Announce current map (Shift+M handled by EntityNavigationSystem)
            if (Input.GetKeyDown(KeyCode.M) && !shiftHeld)
            {
                mod.AnnounceCurrentMap();
            }

            // T: Announce timers, Shift+T: Toggle freeze
            if (Input.GetKeyDown(KeyCode.T))
            {
                if (shiftHeld)
                    Patches.TimerHelper.ToggleTimerFreeze();
                else
                    Patches.TimerHelper.AnnounceActiveTimers();
            }

            // 4: Toggle sonar mode
            if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                mod.ToggleSonarMode();
            }

            // 5: Debug colliders at cursor
            if (Input.GetKeyDown(KeyCode.Alpha5))
            {
                mod.DebugCollidersAtCursor();
            }

            // 6: Play test exit sound
            if (Input.GetKeyDown(KeyCode.Alpha6))
            {
                mod.PlayTestExitSound();
            }
        }

        private bool IsShiftHeld()
        {
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }

        private bool IsCtrlHeld()
        {
            return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        }
    }
}
