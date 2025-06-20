// PreciseCoGCalculationJob.cs
// Defines an asynchronous work item for calculating the precise Center of Gravity of an asteroid.

using System;
using System.Collections.Concurrent; // For ConcurrentQueue
using VRage.Game.ModAPI;
using VRageMath;
using Sandbox.ModAPI;
using VRage.Utils;
using VRage.Voxels; // Crucial for MyStorageData, MyStorageDataTypeFlags

// All classes are in the same namespace and folder, so implicit visibility should work.

namespace AsteroidGravityMod
{
    // A simple struct to hold the result of the CoG calculation
    public struct CoGResult
    {
        public long EntityId;
        public Vector3D CoGPosition;
        public bool Success;
    }

    /// <summary>
    /// Represents a work item for calculating the precise Center of Gravity of an asteroid.
    /// This class provides the 'DoWork' method to be executed as an Action via MyAPIGateway.Parallel.Start.
    /// </summary>
    public class PreciseCoGCalculationJob
    {
        private long _asteroidId;
        private IMyVoxelMap _voxelMap;
        private ConcurrentQueue<CoGResult> _resultsQueue;
        private Logger _logger; // Reference to the custom logger

        // Constructor
        public PreciseCoGCalculationJob(long asteroidId, IMyVoxelMap voxelMap, ConcurrentQueue<CoGResult> resultsQueue, Logger logger)
        {
            this._asteroidId = asteroidId;
            this._voxelMap = voxelMap;
            this._resultsQueue = resultsQueue;
            this._logger = logger;
        }

        /// <summary>
        /// Performs the actual CoG calculation. This method runs on a parallel thread.
        /// It will enqueue its result into the shared results queue.
        /// </summary>
        public void DoWork()
        {
            Vector3D calculatedCoG = Vector3D.Zero;
            bool success = false;

            try
            {
                if (this._voxelMap?.Storage != null)
                {
                    // This call relies on MyStorageData and MyStorageDataTypeFlags being available via VRage.Voxels
                    calculatedCoG = AsteroidCache.CalculatePreciseCoGStatic(this._voxelMap);
                    success = true;
                    if (this._logger.IsDebugEnabled) // Conditional debug log
                        this._logger.Debug("CoG calculation successful for asteroid " + this._asteroidId + ".");
                }
                else
                {
                    this._logger.Warning("Voxel map for asteroid " + this._asteroidId + " was null or had no storage during CoG job. Skipping calculation.");
                }
            }
            catch (Exception ex)
            {
                // Log error without blocking the main thread
                this._logger.Error("Error calculating CoG for asteroid " + this._asteroidId + " in job: " + ex.Message + "\n" + ex.StackTrace);
                // The result will indicate failure
            }
            finally
            {
                // Enqueue the result back to the main thread
                _resultsQueue.Enqueue(new CoGResult { EntityId = this._asteroidId, CoGPosition = calculatedCoG, Success = success });
            }
        }
    }
}
