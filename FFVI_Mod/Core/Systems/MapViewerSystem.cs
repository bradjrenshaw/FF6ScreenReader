using UnityEngine;
using MelonLoader;
using Il2CppLast.Map;
using FFVI_ScreenReader.Utils;

namespace FFVI_ScreenReader.Core.Systems
{
    /// <summary>
    /// Manages a virtual cursor for exploring the map without moving the player.
    /// Allows tile-by-tile navigation and announces tile contents.
    /// Implements ISystem for lifecycle management.
    /// </summary>
    public class MapViewerSystem : ISystem
    {
        // ISystem implementation
        public string Name => "MapViewer";
        public int Priority => 40; // Run before EntityNavigation (50)

        /// <summary>
        /// System is active when we're on a field map.
        /// </summary>
        public bool IsActive => IsOnFieldMap();

        // Core components
        private readonly MapViewer mapViewer;
        private readonly EntityCache entityCache;

        // Scene tracking
        private string currentScene = "";

        /// <summary>
        /// Gets the current cursor position (for debug methods).
        /// </summary>
        public Vector3 CursorPosition => mapViewer.CursorPosition;

        /// <summary>
        /// Whether the cursor has been moved away from player.
        /// </summary>
        public bool IsCursorActive => mapViewer.IsActive;

        /// <summary>
        /// Creates a new MapViewerSystem.
        /// </summary>
        /// <param name="entityCache">Entity cache for tile descriptions</param>
        public MapViewerSystem(EntityCache entityCache)
        {
            this.entityCache = entityCache;
            this.mapViewer = new MapViewer();
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
            MelonLogger.Msg("[MapViewerSystem] Activating");
        }

        public void OnDeactivate()
        {
            MelonLogger.Msg("[MapViewerSystem] Deactivating");
        }

        public void OnSceneChanged(string sceneName)
        {
            currentScene = sceneName;
            // Reset cursor on scene change
            var playerController = GameObjectCache.Get<FieldPlayerController>();
            if (playerController?.fieldPlayer != null)
            {
                mapViewer.SnapToPlayer(playerController.fieldPlayer.transform.localPosition);
            }
        }

        public void Update()
        {
            // No per-frame update needed
        }

        /// <summary>
        /// Handles input for map viewer navigation.
        /// </summary>
        /// <returns>True if input was consumed</returns>
        public bool HandleInput()
        {
            // Early exit if no keys pressed
            if (!Input.anyKeyDown)
                return false;

            bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            // Map viewer only responds to Ctrl+key combinations
            if (!ctrlHeld)
                return false;

            // Ctrl+I: Move North
            if (Input.GetKeyDown(KeyCode.I))
            {
                MoveCursor(new Vector2(0, 16));
                return true;
            }

            // Ctrl+K: Move South
            if (Input.GetKeyDown(KeyCode.K))
            {
                MoveCursor(new Vector2(0, -16));
                return true;
            }

            // Ctrl+J: Move West
            if (Input.GetKeyDown(KeyCode.J))
            {
                MoveCursor(new Vector2(-16, 0));
                return true;
            }

            // Ctrl+L: Move East
            if (Input.GetKeyDown(KeyCode.L))
            {
                MoveCursor(new Vector2(16, 0));
                return true;
            }

            // Ctrl+O: Snap to player
            if (Input.GetKeyDown(KeyCode.O))
            {
                SnapToPlayer();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Moves the cursor in the specified direction and announces tile contents.
        /// </summary>
        /// <param name="offset">Direction offset in world units (16 = 1 tile)</param>
        public void MoveCursor(Vector2 offset)
        {
            var playerController = GameObjectCache.Get<FieldPlayerController>();
            if (playerController?.fieldPlayer == null)
            {
                FFVI_ScreenReaderMod.SpeakText("Not in field");
                return;
            }

            Vector3 playerPos = playerController.fieldPlayer.transform.localPosition;

            // Initialize cursor to player position if first use
            if (!mapViewer.IsActive)
            {
                mapViewer.SnapToPlayer(playerPos);
            }

            mapViewer.MoveCursor(offset);
            string description = mapViewer.DescribeTileAtCursor(entityCache);
            FFVI_ScreenReaderMod.SpeakText(description);
        }

        /// <summary>
        /// Snaps the cursor back to the player's position.
        /// </summary>
        /// <param name="announce">Whether to announce the reset (false for automatic resets)</param>
        public void SnapToPlayer(bool announce = true)
        {
            var playerController = GameObjectCache.Get<FieldPlayerController>();
            if (playerController?.fieldPlayer == null)
            {
                return; // Silently fail if player not available
            }

            Vector3 playerPos = playerController.fieldPlayer.transform.localPosition;
            mapViewer.SnapToPlayer(playerPos);

            if (announce)
            {
                FFVI_ScreenReaderMod.SpeakText("Cursor reset to player");
            }
        }

        /// <summary>
        /// Gets the effective cursor position (cursor if active, player if not).
        /// Used by debug methods that need the current inspection point.
        /// </summary>
        public Vector3 GetEffectiveCursorPosition()
        {
            var playerController = GameObjectCache.Get<FieldPlayerController>();
            if (playerController?.fieldPlayer == null)
                return Vector3.zero;

            if (mapViewer.IsActive)
                return mapViewer.CursorPosition;
            else
                return playerController.fieldPlayer.transform.localPosition;
        }
    }
}
