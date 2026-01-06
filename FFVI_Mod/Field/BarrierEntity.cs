using FFVI_ScreenReader.Core;

namespace FFVI_ScreenReader.Field
{
    /// <summary>
    /// Represents a blocking barrier entity (ObjectType.Entity)
    /// These are invisible walls that block player movement.
    /// </summary>
    public class BarrierEntity : NavigableEntity
    {
        public override EntityCategory Category => EntityCategory.Barriers;

        public override int Priority => 9;

        public override bool BlocksPathing => true;

        protected override string GetDisplayName()
        {
            return Name;
        }

        protected override string GetEntityTypeName()
        {
            return "Barrier";
        }
    }
}
