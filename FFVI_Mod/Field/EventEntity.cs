using Il2Cpp;
using FFVI_ScreenReader.Audio;
using FFVI_ScreenReader.Core;

namespace FFVI_ScreenReader.Field
{
    /// <summary>
    /// Represents a generic event (teleport, switch event, random event, etc.)
    /// </summary>
    public class EventEntity : NavigableEntity
    {
        /// <summary>
        /// Specific event type
        /// </summary>
        public MapConstants.ObjectType EventType =>
            GameEntity?.Property != null
                ? (MapConstants.ObjectType)GameEntity.Property.ObjectType
                : MapConstants.ObjectType.PointIn;

        public override EntityCategory Category => EntityCategory.Events;

        public override int Priority => 8;

        public override bool BlocksPathing => false;

        public override SonarInfo SonarInfo => SonarInfo.Continuous("event.wav", 5f);

        protected override string GetDisplayName()
        {
            return Name;
        }

        protected override string GetEntityTypeName()
        {
            return GetEventTypeNameStatic(EventType);
        }

        public static string GetEventTypeNameStatic(MapConstants.ObjectType type)
        {
            switch (type)
            {
                case MapConstants.ObjectType.TelepoPoint:
                    return "Teleport";
                case MapConstants.ObjectType.Event:
                case MapConstants.ObjectType.SwitchEvent:
                case MapConstants.ObjectType.RandomEvent:
                    return "Event";
                default:
                    return type.ToString();
            }
        }
    }
}
