namespace FFVI_ScreenReader.Audio
{
    /// <summary>
    /// Defines how an entity is represented in the sonar system.
    /// </summary>
    public enum SonarMode
    {
        /// <summary>
        /// No audio - entity is not represented in sonar.
        /// </summary>
        Silent,

        /// <summary>
        /// Treat as blocking terrain - uses existing wall tone behavior.
        /// </summary>
        BlockingTerrain,

        /// <summary>
        /// Play associated sound file on loop within range.
        /// </summary>
        Continuous
    }
}
