using UnityEngine;

namespace FFVI_ScreenReader.Utils
{
    /// <summary>
    /// Utility class for direction calculations and formatting.
    /// </summary>
    public static class DirectionHelper
    {
        /// <summary>
        /// Gets the cardinal/intercardinal direction from one position to another.
        /// </summary>
        /// <param name="from">Starting position</param>
        /// <param name="to">Target position</param>
        /// <returns>Direction string (North, Northeast, East, etc.)</returns>
        public static string GetDirection(Vector3 from, Vector3 to)
        {
            Vector3 diff = to - from;
            float angle = Mathf.Atan2(diff.x, diff.y) * Mathf.Rad2Deg;

            // Normalize to 0-360
            if (angle < 0) angle += 360;

            // Convert to cardinal/intercardinal directions (8-way)
            if (angle >= 337.5f || angle < 22.5f) return "North";
            if (angle >= 22.5f && angle < 67.5f) return "Northeast";
            if (angle >= 67.5f && angle < 112.5f) return "East";
            if (angle >= 112.5f && angle < 157.5f) return "Southeast";
            if (angle >= 157.5f && angle < 202.5f) return "South";
            if (angle >= 202.5f && angle < 247.5f) return "Southwest";
            if (angle >= 247.5f && angle < 292.5f) return "West";
            if (angle >= 292.5f && angle < 337.5f) return "Northwest";

            return "Unknown";
        }

        /// <summary>
        /// Gets the cardinal direction name from a normalized direction vector.
        /// Supports 8 directions (cardinals + intercardinals).
        /// </summary>
        /// <param name="dir">Normalized direction vector</param>
        /// <returns>Direction name</returns>
        public static string GetCardinalDirectionName(Vector3 dir)
        {
            // Handle diagonals first (when both X and Y components are significant)
            // A normalized diagonal has components around ±0.707
            if (Mathf.Abs(dir.x) > 0.4f && Mathf.Abs(dir.y) > 0.4f)
            {
                if (dir.y > 0 && dir.x > 0) return "Northeast";
                if (dir.y > 0 && dir.x < 0) return "Northwest";
                if (dir.y < 0 && dir.x > 0) return "Southeast";
                if (dir.y < 0 && dir.x < 0) return "Southwest";
            }

            // Cardinal directions (when primarily on one axis)
            if (Mathf.Abs(dir.y) > Mathf.Abs(dir.x))
            {
                return dir.y > 0 ? "North" : "South";
            }
            else if (Mathf.Abs(dir.x) > 0.1f)
            {
                return dir.x > 0 ? "East" : "West";
            }

            return "Unknown";
        }

        /// <summary>
        /// Formats a distance in world units as steps.
        /// 16 world units = 1 step.
        /// </summary>
        /// <param name="distance">Distance in world units</param>
        /// <returns>Formatted string like "2.5 steps"</returns>
        public static string FormatSteps(float distance)
        {
            float steps = distance / 16f;
            string stepLabel = System.Math.Abs(steps - 1f) < 0.1f ? "step" : "steps";
            return $"{steps:F1} {stepLabel}";
        }
    }
}
