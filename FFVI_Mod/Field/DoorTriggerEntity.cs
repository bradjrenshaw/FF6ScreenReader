using FFVI_ScreenReader.Core;

namespace FFVI_ScreenReader.Field
{
    /// <summary>
    /// Represents a door or trigger
    /// </summary>
    public class DoorTriggerEntity : NavigableEntity
    {
        public override EntityCategory Category => EntityCategory.Events;

        public override int Priority => 6;

        public override bool BlocksPathing => false;

        protected override string GetDisplayName()
        {
            return Name;
        }

        protected override string GetEntityTypeName()
        {
            return "Door/Trigger";
        }
    }
}
