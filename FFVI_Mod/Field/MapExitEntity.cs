using FFVI_ScreenReader.Audio;
using FFVI_ScreenReader.Core;

namespace FFVI_ScreenReader.Field
{
    /// <summary>
    /// Represents a map exit/transition
    /// </summary>
    public class MapExitEntity : NavigableEntity
    {
        /// <summary>
        /// Destination map ID
        /// </summary>
        public int DestinationMapId => GameEntity?.Property?.TryCast<Il2CppLast.Map.PropertyGotoMap>()?.MapId ?? -1;

        /// <summary>
        /// Friendly name of destination map
        /// </summary>
        public string DestinationName => MapNameResolver.GetMapExitName(GameEntity?.Property?.TryCast<Il2CppLast.Map.PropertyGotoMap>());

        public override EntityCategory Category => EntityCategory.MapExits;

        public override int Priority => 1;

        public override bool BlocksPathing => true;

        public override SonarInfo SonarInfo => SonarInfo.Continuous("exit.wav", 5f);

        protected override string GetDisplayName()
        {
            // Build enhanced name with destination
            return !string.IsNullOrEmpty(DestinationName)
                ? $"{Name} → {DestinationName}"
                : Name;
        }

        protected override string GetEntityTypeName()
        {
            return "Map Exit";
        }
    }
}
