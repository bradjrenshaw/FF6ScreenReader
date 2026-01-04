using System.Collections.Generic;
using System.Linq;

namespace FFVI_ScreenReader.Core
{
    /// <summary>
    /// Entity category for filtering navigation targets.
    /// Categories 0-9 are interactive (can be navigated to).
    /// Categories 10+ are non-interactive (spatial awareness only).
    /// </summary>
    public enum EntityCategory
    {
        // Interactive categories (can navigate to with J/K/L)
        All = 0,
        Chests = 1,
        NPCs = 2,
        MapExits = 3,
        Events = 4,
        Vehicles = 5,

        // Non-interactive categories (spatial awareness only)
        Barriers = 10,
        Hazards = 11,
    }

    /// <summary>
    /// Metadata about an entity category.
    /// </summary>
    public class CategoryInfo
    {
        /// <summary>
        /// Display name for the category (shown when cycling)
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Whether entities in this category can be navigated to (J/K/L cycling)
        /// </summary>
        public bool IsInteractive { get; }

        /// <summary>
        /// Whether this category appears when cycling through categories
        /// </summary>
        public bool ShowInCycling { get; }

        public CategoryInfo(string displayName, bool isInteractive, bool showInCycling)
        {
            DisplayName = displayName;
            IsInteractive = isInteractive;
            ShowInCycling = showInCycling;
        }
    }

    /// <summary>
    /// Registry that provides metadata about entity categories.
    /// </summary>
    public static class CategoryRegistry
    {
        private static readonly Dictionary<EntityCategory, CategoryInfo> Categories = new()
        {
            // Interactive categories
            { EntityCategory.All, new CategoryInfo("All", isInteractive: true, showInCycling: true) },
            { EntityCategory.Chests, new CategoryInfo("Chests", isInteractive: true, showInCycling: true) },
            { EntityCategory.NPCs, new CategoryInfo("NPCs", isInteractive: true, showInCycling: true) },
            { EntityCategory.MapExits, new CategoryInfo("Map Exits", isInteractive: true, showInCycling: true) },
            { EntityCategory.Events, new CategoryInfo("Events", isInteractive: true, showInCycling: true) },
            { EntityCategory.Vehicles, new CategoryInfo("Vehicles", isInteractive: true, showInCycling: true) },

            // Non-interactive categories (spatial awareness only)
            { EntityCategory.Barriers, new CategoryInfo("Barriers", isInteractive: false, showInCycling: false) },
            { EntityCategory.Hazards, new CategoryInfo("Hazards", isInteractive: false, showInCycling: false) },
        };

        /// <summary>
        /// Gets metadata for a category.
        /// </summary>
        public static CategoryInfo GetInfo(EntityCategory category)
        {
            return Categories.TryGetValue(category, out var info)
                ? info
                : new CategoryInfo(category.ToString(), isInteractive: false, showInCycling: false);
        }

        /// <summary>
        /// Gets the display name for a category.
        /// </summary>
        public static string GetDisplayName(EntityCategory category)
        {
            return GetInfo(category).DisplayName;
        }

        /// <summary>
        /// Checks if a category is interactive (can be navigated to).
        /// </summary>
        public static bool IsInteractive(EntityCategory category)
        {
            return GetInfo(category).IsInteractive;
        }

        /// <summary>
        /// Checks if a category should appear when cycling through categories.
        /// </summary>
        public static bool ShowInCycling(EntityCategory category)
        {
            return GetInfo(category).ShowInCycling;
        }

        /// <summary>
        /// Gets all categories that can be cycled through.
        /// </summary>
        public static IEnumerable<EntityCategory> GetCycleableCategories()
        {
            return Categories
                .Where(kvp => kvp.Value.ShowInCycling)
                .Select(kvp => kvp.Key)
                .OrderBy(c => (int)c);
        }

        /// <summary>
        /// Gets all interactive categories.
        /// </summary>
        public static IEnumerable<EntityCategory> GetInteractiveCategories()
        {
            return Categories
                .Where(kvp => kvp.Value.IsInteractive)
                .Select(kvp => kvp.Key)
                .OrderBy(c => (int)c);
        }

        /// <summary>
        /// Gets the number of cycleable categories.
        /// </summary>
        public static int CycleableCategoryCount => Categories.Count(kvp => kvp.Value.ShowInCycling);
    }
}
