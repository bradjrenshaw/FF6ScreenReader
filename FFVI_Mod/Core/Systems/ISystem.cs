namespace FFVI_ScreenReader.Core.Systems
{
    /// <summary>
    /// Interface for mod systems that can be activated/deactivated based on game state.
    /// Systems are managed by SystemManager which handles lifecycle and update calls.
    /// </summary>
    public interface ISystem
    {
        /// <summary>
        /// Display name for logging and debugging.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Update priority. Lower values update first.
        /// Use for controlling order when systems depend on each other's state.
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// Whether this system should currently be active.
        /// SystemManager checks this each frame to trigger OnActivate/OnDeactivate.
        /// </summary>
        bool IsActive { get; }

        /// <summary>
        /// Called each frame while the system is active.
        /// </summary>
        void Update();

        /// <summary>
        /// Called when the system transitions from inactive to active.
        /// Use for initialization, starting audio, etc.
        /// </summary>
        void OnActivate();

        /// <summary>
        /// Called when the system transitions from active to inactive.
        /// Use for cleanup, stopping audio, releasing resources, etc.
        /// </summary>
        void OnDeactivate();

        /// <summary>
        /// Called when the Unity scene changes.
        /// </summary>
        /// <param name="sceneName">Name of the new scene</param>
        void OnSceneChanged(string sceneName);
    }
}
