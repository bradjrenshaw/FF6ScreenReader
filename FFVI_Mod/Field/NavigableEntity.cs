using Il2CppLast.Entity.Field;
using UnityEngine;
using FFVI_ScreenReader.Core;
using static FFVI_ScreenReader.Utils.DirectionHelper;

namespace FFVI_ScreenReader.Field
{
    /// <summary>
    /// Base class for all navigable entities on the field map.
    /// Provides common properties and behavior for entity navigation and pathfinding.
    /// </summary>
    public abstract class NavigableEntity
    {
        /// <summary>
        /// Reference to the underlying game entity
        /// </summary>
        public virtual FieldEntity GameEntity { get; set; }

        /// <summary>
        /// Current position in world coordinates
        /// </summary>
        public virtual Vector3 Position => GameEntity?.transform?.position ?? Vector3.zero;

        /// <summary>
        /// Entity name (localized if available)
        /// </summary>
        public virtual string Name => GameEntity?.Property?.Name ?? "Unknown";

        /// <summary>
        /// Category for filtering purposes
        /// </summary>
        public abstract EntityCategory Category { get; }

        /// <summary>
        /// Priority for deduplication (lower = more important)
        /// </summary>
        public abstract int Priority { get; }

        /// <summary>
        /// Whether this entity blocks pathfinding movement
        /// </summary>
        public abstract bool BlocksPathing { get; }

        /// <summary>
        /// Whether this entity is currently interactive
        /// </summary>
        public virtual bool IsInteractive => true;

        /// <summary>
        /// Gets the display name for this entity (without distance/direction)
        /// </summary>
        protected abstract string GetDisplayName();

        /// <summary>
        /// Gets the entity type name for this entity (e.g., "Treasure Chest", "NPC")
        /// </summary>
        protected abstract string GetEntityTypeName();

        /// <summary>
        /// Formats this entity for screen reader announcement
        /// </summary>
        public virtual string FormatDescription(Vector3 playerPos)
        {
            float distance = Vector3.Distance(playerPos, Position);
            string direction = GetDirection(playerPos, Position);
            return $"{GetDisplayName()} ({FormatSteps(distance)} {direction}) - {GetEntityTypeName()}";
        }
    }
}
