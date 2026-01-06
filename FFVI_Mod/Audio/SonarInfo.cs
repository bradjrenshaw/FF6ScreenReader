namespace FFVI_ScreenReader.Audio
{
    /// <summary>
    /// Configuration for how an entity is represented in the sonar system.
    /// </summary>
    public class SonarInfo
    {
        /// <summary>
        /// The sonar mode determining how this entity is represented.
        /// </summary>
        public SonarMode Mode { get; set; }

        /// <summary>
        /// File path to the sound file (relative to sounds directory).
        /// Only used when Mode is Continuous.
        /// </summary>
        public string Sound { get; set; }

        /// <summary>
        /// Maximum range in tiles at which the sound will play.
        /// Only used when Mode is Continuous.
        /// </summary>
        public float MaxRange { get; set; }

        /// <summary>
        /// Creates a SonarInfo with Silent mode (no audio).
        /// </summary>
        public static SonarInfo Silent() => new SonarInfo { Mode = SonarMode.Silent };

        /// <summary>
        /// Creates a SonarInfo with BlockingTerrain mode (uses wall tones).
        /// </summary>
        public static SonarInfo Blocking() => new SonarInfo { Mode = SonarMode.BlockingTerrain };

        /// <summary>
        /// Creates a SonarInfo with Continuous mode (plays sound file on loop).
        /// </summary>
        /// <param name="soundFileName">Sound file name (e.g., "chest.wav")</param>
        /// <param name="maxRangeTiles">Maximum range in tiles</param>
        public static SonarInfo Continuous(string soundFileName, float maxRangeTiles) =>
            new SonarInfo
            {
                Mode = SonarMode.Continuous,
                Sound = soundFileName,
                MaxRange = maxRangeTiles
            };
    }
}
