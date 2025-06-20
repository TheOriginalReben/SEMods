// Constants.cs
// Defines truly constant values used across the mod.

using VRageMath; // For Vector4

// All classes are in the same namespace and folder, so implicit visibility should work.

namespace AsteroidGravityMod
{
    public static class Constants
    {
        public const double G = 9.81; // Earth's gravity in m/s²
        public const double TickSeconds = 1.0 / 60.0; // Approx 1/60 sec, using double for better precision

        // Debug Colors (remain constant)
        public static readonly Vector4 DebugColorGreen = new Vector4(0, 1, 0, 1);
        public static readonly Vector4 DebugColorRed = new Vector4(1, 0, 0, 1);
        public static readonly Vector4 DebugColorBlue = new Vector4(0, 0, 1, 1);
        public static readonly Vector4 DebugColorWhite = new Vector4(1, 1, 1, 1);
        public static readonly Vector4 DebugColorCyan = new Vector4(0, 1, 1, 1);

        public const float DebugLineThickness = 0.1f;
        public const float DebugMarkerThickness = 0.15f;
        public const float DebugHighlightThickness = 1.0f; // VERY THICK for debugging visibility

        // Asteroid data caching range (around player for geometric CoG fallback)
        public const double CacheRange = 5000.0; // 5 km
        public const double CacheRangeSq = CacheRange * CacheRange;

        // Combined range for precise CoG calculation AND saving
        public const double CoGAndSaveRange = 2000.0; // 2 km
        public const double CoGAndSaveRangeSq = CoGAndSaveRange * CoGAndSaveRange;

        // Other internal constants
        public const double PositionThresholdSq = 1.0; // 1 m² for gravity caching
        public const int VoxelReadChunkSize = 32; // Size of chunks to read voxel data (e.g., 32x32x32 voxels) for CoG calculation

        // Configuration and Log File Names for MyAPIGateway.Utilities.WriteFileInWorldStorage
        public const string ConfigFileName = "AsteroidGravityMod.cfg";
        public const string LogFileName = "AsteroidGravityMod.log";
        public const string ModName = "AsteroidNaturalGravityMod"; // The logical name of your mod.
    }
}
