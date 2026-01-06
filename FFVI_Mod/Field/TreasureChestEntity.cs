using Il2Cpp;
using Il2CppLast.Entity.Field;
using FFVI_ScreenReader.Audio;
using FFVI_ScreenReader.Core;

namespace FFVI_ScreenReader.Field
{
    /// <summary>
    /// Represents a treasure chest entity
    /// </summary>
    public class TreasureChestEntity : NavigableEntity
    {
        /// <summary>
        /// Whether this treasure chest has been opened
        /// </summary>
        public bool IsOpened => GameEntity?.TryCast<FieldTresureBox>()?.isOpen ?? false;

        public override EntityCategory Category => EntityCategory.Chests;

        public override int Priority => 3;

        public override bool BlocksPathing => true;

        /// <summary>
        /// Opened chests are not interactive
        /// </summary>
        public override bool IsInteractive => !IsOpened;

        /// <summary>
        /// Opened chests are silent, unopened chests play sound
        /// </summary>
        public override SonarInfo SonarInfo => IsOpened
            ? SonarInfo.Silent()
            : SonarInfo.Continuous("chest.wav", 5f);

        protected override string GetDisplayName()
        {
            string status = IsOpened ? "Opened" : "Unopened";
            return $"{status} {Name}";
        }

        protected override string GetEntityTypeName()
        {
            return "Treasure Chest";
        }
    }
}
