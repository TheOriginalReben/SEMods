// AsteroidCache.cs
// Manages the caching and processing of asteroid data, including Center of Gravity calculations.

using System; // For Exception, Math
using System.Collections.Generic; // For Dictionary, List, HashSet
using System.Collections.Concurrent; // For ConcurrentQueue, ConcurrentHashSet
using VRage.Game.ModAPI; // For IMyVoxelMap
using VRageMath; // For Vector3D, Vector3I, BoundingBoxD
using Sandbox.ModAPI; // For MyAPIGateway.Session.VoxelMaps
using VRage.Utils; // For MyLog, MyStringId, MyFontEnum
using VRage.Voxels; // For MyStorageData, MyStorageDataTypeFlags

// All classes are in the same namespace and folder, so implicit visibility should work.

namespace AsteroidGravityMod
{
    public class AsteroidCache
    {
        private Dictionary<long, AsteroidData> _asteroidsData = new Dictionary<long, AsteroidData>();
        public IReadOnlyDictionary<long, AsteroidData> AsteroidsData => _asteroidsData;

        // Tracks the EntityId of the last asteroid whose CoG was precisely calculated for debug
        public long LastProcessedCoGAsteroidId { get; private set; } = -1;

        // Reusable MyStorageData for reading voxel chunks during CoG calculation
        private MyStorageData _reusableStorageData = new MyStorageData();

        // Queue for asteroids that need CoG calculation (to be picked up by main mod for job submission)
        private ConcurrentQueue<long> _asteroidsNeedingCoGQueue;
        private HashSet<long> _asteroidsInCoGProcess = new HashSet<long>(); // Tracked on main thread to prevent duplicate jobs

        // Dependencies
        private ModSettings _settings; // Reference to the mod's current settings
        private DebugRenderer _debugRenderer; // Reference for debug notifications
        private Logger _logger; // Reference to the custom logger

        private List<IMyVoxelBase> _rawVoxelsCache = new List<IMyVoxelBase>();
        private HashSet<long> _currentAsteroidIdsCache = new HashSet<long>();
        private List<long> _asteroidsToRemoveCache = new List<long>(); // For cleaning up invalid entries

        public AsteroidCache(ModSettings settings, DebugRenderer debugRenderer, ConcurrentQueue<long> asteroidsNeedingCoGQueue, Logger logger)
        {
            this._settings = settings;
            this._debugRenderer = debugRenderer;
            this._asteroidsNeedingCoGQueue = asteroidsNeedingCoGQueue;
            this._logger = logger;
        }

        public void LoadCachedData(List<AsteroidData> loadedData)
        {
            // If we decide to save/load asteroid data (not just settings), this method would populate _asteroidsData
            // For now, asteroids are discovered and processed during gameplay.
            foreach (var data in loadedData)
            {
                if (!this._asteroidsData.ContainsKey(data.EntityId))
                {
                    this._asteroidsData.Add(data.EntityId, data);
                    // If loaded, and was not calculated, queue it for calculation if enabled
                    if (!data.IsCoGCalculated && this._settings.EnablePreciseCoG)
                    {
                        if (!this._asteroidsInCoGProcess.Contains(data.EntityId)) // Ensure not already queued
                        {
                            this._asteroidsNeedingCoGQueue.Enqueue(data.EntityId);
                            this._asteroidsInCoGProcess.Add(data.EntityId);
                            if (this._logger.IsDebugEnabled) // Conditional debug log
                                this._logger.Debug("Loaded asteroid " + data.EntityId + " queued for CoG calc.");
                        }
                    }
                }
            }
        }

        public void CacheAsteroids(Vector3D playerPos)
        {
            this._currentAsteroidIdsCache.Clear();
            this._rawVoxelsCache.Clear();
            MyAPIGateway.Session.VoxelMaps.GetInstances(this._rawVoxelsCache);

            int newAsteroidsCount = 0;
            int removedAsteroidsCount = 0;

            foreach (IMyVoxelBase voxel in this._rawVoxelsCache)
            {
                IMyVoxelMap voxelMap = voxel as IMyVoxelMap;
                if (voxelMap?.Storage == null || voxelMap.Storage.Size == Vector3I.Zero) continue;

                long entityId = voxelMap.EntityId;
                this._currentAsteroidIdsCache.Add(entityId);

                BoundingBoxD worldAABB = voxelMap.WorldAABB;
                Vector3D currentAsteroidGeometricCenterWorld = worldAABB.Center;

                if ((currentAsteroidGeometricCenterWorld - playerPos).LengthSquared() < Constants.CacheRangeSq)
                {
                    // C# 6 compatible out variable declaration
                    AsteroidData data;
                    if (!this._asteroidsData.TryGetValue(entityId, out data))
                    {
                        // Add new asteroid with geometric center initially
                        data = new AsteroidData(entityId, currentAsteroidGeometricCenterWorld, worldAABB.HalfExtents.Length(), string.Empty);
                        this._asteroidsData.Add(entityId, data);
                        newAsteroidsCount++;

                        // Queue for CoG calculation if enabled
                        if (this._settings.EnablePreciseCoG)
                        {
                            if (!this._asteroidsInCoGProcess.Contains(entityId)) // Avoid duplicate queuing
                            {
                                this._asteroidsNeedingCoGQueue.Enqueue(entityId);
                                this._asteroidsInCoGProcess.Add(entityId);
                                if (this._logger.IsDebugEnabled) // Conditional debug log
                                    this._logger.Debug("New asteroid " + entityId + " queued for CoG calc.");
                            }
                        }
                    }
                    else
                    {
                        // If an existing asteroid, ensure it's queued if it needs calculation and is in range
                        if (!data.IsCoGCalculated && this._settings.EnablePreciseCoG)
                        {
                             if (!this._asteroidsInCoGProcess.Contains(entityId))
                             {
                                 this._asteroidsNeedingCoGQueue.Enqueue(entityId);
                                 this._asteroidsInCoGProcess.Add(entityId);
                                 if (this._logger.IsDebugEnabled) // Conditional debug log
                                    this._logger.Debug("Existing asteroid " + entityId + " re-queued for CoG calc.");
                             }
                        }
                        // Update position if it moved significantly and CoG is not calculated
                        if (!data.IsCoGCalculated && (data.Position - currentAsteroidGeometricCenterWorld).LengthSquared() > 100.0) // 10m threshold
                        {
                             data.Position = currentAsteroidGeometricCenterWorld;
                             this._asteroidsData[entityId] = data; // Update struct in dictionary
                             if (this._logger.IsDebugEnabled) // Conditional debug log
                                this._logger.Debug("Asteroid " + entityId + " geometric center updated.");
                        }
                    }
                }
            }

            this._asteroidsToRemoveCache.Clear();
            foreach (var kvp in this._asteroidsData)
            {
                // Remove if no longer present or out of cache range from current player position
                if (!this._currentAsteroidIdsCache.Contains(kvp.Key) || (kvp.Value.Position - playerPos).LengthSquared() >= Constants.CacheRangeSq)
                {
                    this._asteroidsToRemoveCache.Add(kvp.Key);
                }
            }

            foreach (long id in this._asteroidsToRemoveCache)
            {
                if (id == this.LastProcessedCoGAsteroidId)
                {
                    this.LastProcessedCoGAsteroidId = -1;
                }
                this._asteroidsData.Remove(id);
                this._asteroidsInCoGProcess.Remove(id); // Also remove from processing queue tracker
                // Need to ensure it's removed from _asteroidsNeedingCoGQueue if possible (ConcurrentQueue lacks direct remove)
                // This means items might stay in queue but will be skipped when processed.
                removedAsteroidsCount++;
            }

            if (this._settings.DebugMode && (newAsteroidsCount > 0 || removedAsteroidsCount > 0))
            {
                this._logger.Info("Cache updated: +" + newAsteroidsCount + " new, -" + removedAsteroidsCount + " removed. Total cached: " + this._asteroidsData.Count);
            }
        }

        /// <summary>
        /// Updates the CoG of an asteroid in the cache after a job has completed.
        /// This method must be called on the main game thread.
        /// </summary>
        /// <param name="result">The CoGResult containing the asteroid ID and its calculated CoG.</param>
        public void UpdateAsteroidCoGResult(CoGResult result)
        {
            // C# 6 compatible out variable declaration
            AsteroidData data;
            if (this._asteroidsData.TryGetValue(result.EntityId, out data))
            {
                data.IsCoGCalculated = result.Success;
                if (result.Success)
                {
                    data.Position = result.CoGPosition;
                    this.LastProcessedCoGAsteroidId = result.EntityId;
                    if (this._logger.IsDebugEnabled) // Conditional debug log
                        this._logger.Debug("CoG for " + result.EntityId + " updated.");
                }
                else
                {
                    // If calculation failed, fall back to geometric center
                    IMyVoxelMap voxelMap = MyAPIGateway.Entities.GetEntityById(result.EntityId) as IMyVoxelMap;
                    if (voxelMap != null)
                    {
                        data.Position = voxelMap.WorldAABB.Center;
                        if (this._logger.IsDebugEnabled) // Conditional debug log
                            this._logger.Debug("CoG for " + result.EntityId + " defaulted to geometric (calc failed).");
                    }
                }
                this._asteroidsData[result.EntityId] = data; // Update struct in dictionary
                this._asteroidsInCoGProcess.Remove(result.EntityId); // Mark as no longer being processed
            }
            else
            {
                if (this._logger.IsDebugEnabled) // Conditional debug log
                    this._logger.Debug("CoG result for unknown or removed asteroid " + result.EntityId + " received.");
            }
        }

        /// <summary>
        /// Calculates the precise Center of Gravity for a voxel map by iterating through its content.
        /// This is a BLOCKING operation, intended to be called on a separate thread (e.g., via Parallel.Start).
        /// </summary>
        /// <param name="voxelMap">The voxel map (asteroid) to calculate CoG for.</param>
        /// <returns>The calculated world position of the Center of Gravity.</returns>
        public static Vector3D CalculatePreciseCoGStatic(IMyVoxelMap voxelMap) // Made public static
        {
            Vector3D sumOfSolidPoints = Vector3D.Zero;
            int solidPointCount = 0;

            Vector3I minVoxel = Vector3I.Zero;
            Vector3I maxVoxel = voxelMap.Storage.Size - Vector3I.One; // Adjusted to be inclusive max

            // Reusable MyStorageData for reading voxel chunks (defined as a member, or local if static).
            // Since this is a static method, we need a local instance.
            MyStorageData reusableStorageData = new MyStorageData();
            reusableStorageData.Resize(new Vector3I(Constants.VoxelReadChunkSize));

            for (int z = 0; z <= maxVoxel.Z; z += Constants.VoxelReadChunkSize)
            {
                for (int y = 0; y <= maxVoxel.Y; y += Constants.VoxelReadChunkSize)
                {
                    for (int x = 0; x <= maxVoxel.X; x += Constants.VoxelReadChunkSize)
                    {
                        Vector3I currentMin = new Vector3I(x, y, z);
                        Vector3I currentMax = currentMin + new Vector3I(Constants.VoxelReadChunkSize) - Vector3I.One;
                        currentMax = Vector3I.Min(maxVoxel, currentMax); // Ensure we don't read beyond actual storage size

                        Vector3I readDimensions = currentMax - currentMin + Vector3I.One;

                        // Only read if the chunk is valid (non-zero dimensions)
                        if (readDimensions.X > 0 && readDimensions.Y > 0 && readDimensions.Z > 0)
                        {
                            reusableStorageData.Resize(readDimensions); // Resize to actual read size
                            try
                            {
                                voxelMap.Storage.ReadRange(reusableStorageData, MyStorageDataTypeFlags.Content, 0, currentMin, currentMax);

                                for (int cz = 0; cz < readDimensions.Z; cz++)
                                {
                                    for (int cy = 0; cy < readDimensions.Y; cy++)
                                    {
                                        for (int cx = 0; cx < readDimensions.X; cx++)
                                        {
                                            byte content = reusableStorageData.Content(cx, cy, cz);
                                            if (content > 0) // If voxel is solid
                                            {
                                                Vector3I currentVoxelLocalCoord = currentMin + new Vector3I(cx, cy, cz);
                                                Vector3D localVoxelCenterRaw = (Vector3D)currentVoxelLocalCoord + new Vector3D(0.5);

                                                // Calculate world position of the voxel center
                                                Vector3D centeredLocalVoxelPos = localVoxelCenterRaw - (Vector3D)voxelMap.Storage.Size / 2.0;
                                                Vector3D solidVoxelWorldPos = Vector3D.Transform(centeredLocalVoxelPos, voxelMap.PositionComp.WorldMatrix);

                                                sumOfSolidPoints += solidVoxelWorldPos;
                                                solidPointCount++;
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                // In static methods, we can't directly use _logger.
                                MyLog.Default.WriteLine("AsteroidGravityMod: Error reading voxel data chunk for CoG (chunk " + currentMin + ", Asteroid " + voxelMap.EntityId + "): " + ex.Message);
                                // Continue processing other chunks even if one fails
                            }
                        }
                    }
                }
            }

            if (solidPointCount > 0)
            {
                return sumOfSolidPoints / solidPointCount;
            }
            else
            {
                // Fallback to geometric center if no solid voxels are found (e.g., empty asteroid)
                return voxelMap.WorldAABB.Center;
            }
        }
    }
}
