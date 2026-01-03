using UnityEngine;
using Il2CppLast.Map;

namespace FFVI_ScreenReader.Utils
{
    /// <summary>
    /// Represents tile coordinates on the map grid.
    /// </summary>
    public struct TileCoordinates
    {
        public int X;
        public int Y;

        public TileCoordinates(int x, int y)
        {
            X = x;
            Y = y;
        }

        public override string ToString() => $"{X}, {Y}";

        public override bool Equals(object obj)
        {
            if (obj is TileCoordinates other)
                return X == other.X && Y == other.Y;
            return false;
        }

        public override int GetHashCode() => X.GetHashCode() ^ (Y.GetHashCode() << 16);

        public static bool operator ==(TileCoordinates a, TileCoordinates b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(TileCoordinates a, TileCoordinates b) => !(a == b);
    }

    /// <summary>
    /// Converts between world coordinates and tile coordinates.
    ///
    /// The game uses a coordinate system where:
    /// - 16 world units = 1 tile
    /// - The map origin (0,0) is at the center of the map
    /// - Y axis is inverted (positive world Y = negative tile Y)
    ///
    /// Formula (from touch controller decompilation):
    /// - tileX = Floor(mapWidth * 0.5 + worldX * 0.0625)
    /// - tileY = Floor(mapHeight * 0.5 - worldY * 0.0625)
    /// </summary>
    public static class TileCoordinateConverter
    {
        /// <summary>
        /// Units per tile (16 world units = 1 tile)
        /// </summary>
        public const float UnitsPerTile = 16f;

        /// <summary>
        /// Inverse of units per tile (1/16 = 0.0625)
        /// </summary>
        public const float TileScale = 0.0625f;

        /// <summary>
        /// Converts a world position to tile coordinates.
        /// Uses the map handle to get map dimensions.
        /// </summary>
        /// <param name="worldPos">World position (use localPosition, not position)</param>
        /// <param name="mapHandle">Map handle for dimensions</param>
        /// <returns>Tile coordinates</returns>
        public static TileCoordinates WorldToTile(Vector3 worldPos, IMapAccessor mapHandle)
        {
            int mapWidth = mapHandle.GetCollisionLayerWidth();
            int mapHeight = mapHandle.GetCollisionLayerHeight();
            return WorldToTile(worldPos, mapWidth, mapHeight);
        }

        /// <summary>
        /// Converts a world position to tile coordinates.
        /// </summary>
        /// <param name="worldPos">World position (use localPosition, not position)</param>
        /// <param name="mapWidth">Map width in tiles</param>
        /// <param name="mapHeight">Map height in tiles</param>
        /// <returns>Tile coordinates</returns>
        public static TileCoordinates WorldToTile(Vector3 worldPos, int mapWidth, int mapHeight)
        {
            // Formula from touch controller (FieldPlayerTouchRouteMoveController$WorldPositionToCellPositionXY)
            // Note: Y axis is INVERTED (minus sign)
            int tileX = Mathf.FloorToInt(mapWidth * 0.5f + worldPos.x * TileScale);
            int tileY = Mathf.FloorToInt(mapHeight * 0.5f - worldPos.y * TileScale);
            return new TileCoordinates(tileX, tileY);
        }

        /// <summary>
        /// Converts a world position to tile coordinates as a Vector3 (for pathfinding).
        /// The Z component should be set separately based on layer.
        /// </summary>
        /// <param name="worldPos">World position (use localPosition, not position)</param>
        /// <param name="mapWidth">Map width in tiles</param>
        /// <param name="mapHeight">Map height in tiles</param>
        /// <returns>Vector3 with tile X and Y, Z = 0</returns>
        public static Vector3 WorldToTileVector(Vector3 worldPos, int mapWidth, int mapHeight)
        {
            var tile = WorldToTile(worldPos, mapWidth, mapHeight);
            return new Vector3(tile.X, tile.Y, 0);
        }

        /// <summary>
        /// Converts a world position to tile coordinates as floats (for precise positioning).
        /// Useful for airship navigation where exact position matters.
        /// </summary>
        /// <param name="worldPos">World position (use localPosition, not position)</param>
        /// <param name="mapWidth">Map width in tiles</param>
        /// <param name="mapHeight">Map height in tiles</param>
        /// <returns>Tuple of (tileX, tileY) as floats</returns>
        public static (float x, float y) WorldToTileFloat(Vector3 worldPos, int mapWidth, int mapHeight)
        {
            float tileX = mapWidth * 0.5f + worldPos.x * TileScale;
            float tileY = mapHeight * 0.5f - worldPos.y * TileScale;
            return (tileX, tileY);
        }

        /// <summary>
        /// Checks if two world positions are on the same tile.
        /// </summary>
        public static bool AreOnSameTile(Vector3 posA, Vector3 posB, int mapWidth, int mapHeight)
        {
            var tileA = WorldToTile(posA, mapWidth, mapHeight);
            var tileB = WorldToTile(posB, mapWidth, mapHeight);
            return tileA == tileB;
        }

        /// <summary>
        /// Checks if two world positions are on the same tile.
        /// </summary>
        public static bool AreOnSameTile(Vector3 posA, Vector3 posB, IMapAccessor mapHandle)
        {
            int mapWidth = mapHandle.GetCollisionLayerWidth();
            int mapHeight = mapHandle.GetCollisionLayerHeight();
            return AreOnSameTile(posA, posB, mapWidth, mapHeight);
        }
    }
}
