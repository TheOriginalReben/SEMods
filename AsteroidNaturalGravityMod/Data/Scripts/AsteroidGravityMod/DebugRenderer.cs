// DebugRenderer.cs
// Handles in-game visualization for debugging purposes.

using System; // For Math
using System.Collections.Generic; // For List
using VRageMath; // For Vector3D, Vector4
using Sandbox.ModAPI; // For MyAPIGateway, IMyCharacter, IMyVoxelMap
using VRage.Utils; // For MyStringId
using VRageRender; // For MySimpleObjectDraw, MyBillboard

// All classes are in the same namespace and folder, so implicit visibility should work.

namespace AsteroidGravityMod
{
    public class DebugRenderer
    {
        private AsteroidCache _asteroidCache;
        private ModSettings _settings; // Reference to the mod's current settings
        private Logger _logger; // Reference to the custom logger

        public DebugRenderer(ModSettings settings, AsteroidCache asteroidCache, Logger logger)
        {
            this._settings = settings;
            this._asteroidCache = asteroidCache;
            this._logger = logger;
        }

        public void SetDebugMode(bool enabled)
        {
            this._settings.DebugMode = enabled;
        }

        public void DrawDebugVisuals()
        {
            if (!this._settings.DebugMode) return; // Only draw if debug mode is ON

            IMyCharacter local = MyAPIGateway.Session.LocalHumanPlayer?.Character;
            if (local == null) return;

            Vector3D localPos = local.GetPosition();

            // Conditional debug logging for drawing visuals
            if (this._logger.IsDebugEnabled)
            {
                this._logger.Debug("Drawing debug visuals. Last CoG Calc ID: " + this._asteroidCache.LastProcessedCoGAsteroidId);
            }
            // Conditional debug notification for drawing visuals (these are still helpful for visual confirmation of debug mode)
            MyAPIGateway.Utilities.ShowNotification("DEBUG: Last CoG Calc ID: " + this._asteroidCache.LastProcessedCoGAsteroidId, 1000);

            if (this._asteroidCache?.AsteroidsData != null)
            {
                foreach (AsteroidData asteroid in this._asteroidCache.AsteroidsData.Values)
                {
                    // Only draw if within gravity detection range
                    if ((asteroid.Position - localPos).LengthSquared() < this._settings.GravitationalDetectionRangeSq)
                    {
                        this.DrawAsteroidInfluence(asteroid.Position, asteroid.Radius);
                        this.DrawGravitationalCenter(asteroid.Position, asteroid.Radius, asteroid.IsCoGCalculated, asteroid.EntityId == this._asteroidCache.LastProcessedCoGAsteroidId);

                        // Draw geometric center for comparison
                        IMyVoxelMap debugVoxelMap = MyAPIGateway.Entities.GetEntityById(asteroid.EntityId) as IMyVoxelMap;
                        if (debugVoxelMap?.Storage != null)
                        {
                            Vector3D geometricCenter = debugVoxelMap.WorldAABB.Center;
                            Vector4 blueColor = Constants.DebugColorBlue;

                            MySimpleObjectDraw.DrawLine(geometricCenter - new Vector3D(1, 0, 0), geometricCenter + new Vector3D(1, 0, 0), MyStringId.GetOrCompute("Square"), ref blueColor, Constants.DebugMarkerThickness, MyBillboard.BlendTypeEnum.Standard);
                            MySimpleObjectDraw.DrawLine(geometricCenter - new Vector3D(0, 1, 0), geometricCenter + new Vector3D(0, 1, 0), MyStringId.GetOrCompute("Square"), ref blueColor, Constants.DebugMarkerThickness, MyBillboard.BlendTypeEnum.Standard);
                            MySimpleObjectDraw.DrawLine(geometricCenter - new Vector3D(0, 0, 1), geometricCenter + new Vector3D(0, 0, 1), MyStringId.GetOrCompute("Square"), ref blueColor, Constants.DebugMarkerThickness, MyBillboard.BlendTypeEnum.Standard);
                        }
                    }
                }
            }
        }

        private void DrawAsteroidInfluence(Vector3D center, double radius)
        {
            int segments = 36;
            double angleStep = (2 * Math.PI) / segments;

            Vector3D previousPoint = center + new Vector3D(radius, 0, 0);
            Vector4 colorGreen = Constants.DebugColorGreen;
            for (int i = 1; i <= segments; i++)
            {
                double angle = i * angleStep;
                Vector3D nextPoint = center + new Vector3D(Math.Cos(angle) * radius, Math.Sin(angle) * radius, 0);
                MySimpleObjectDraw.DrawLine(previousPoint, nextPoint, MyStringId.GetOrCompute("Square"), ref colorGreen, Constants.DebugLineThickness, MyBillboard.BlendTypeEnum.Standard);
                previousPoint = nextPoint;
            }
        }

        /// <summary>
        /// Draws a marker at the gravitational center.
        /// </summary>
        /// <param name="gravitationalCenter">The position to draw the marker.</param>
        /// <param name="asteroidRadius">The radius of the asteroid for scaling the marker size.</param>
        /// <param name="isCoGCalculated">True if the precise CoG has been calculated.</param>
        /// <param name="isHighlighted">True if this asteroid's marker should be highlighted.</param>
        private void DrawGravitationalCenter(Vector3D gravitationalCenter, double asteroidRadius, bool isCoGCalculated, bool isHighlighted)
        {
            double markerSize = asteroidRadius * 0.1;
            Vector3D offsetX = Vector3D.UnitX * markerSize;
            Vector3D offsetY = Vector3D.UnitY * markerSize;
            Vector3D offsetZ = Vector3D.UnitZ * markerSize;

            Vector4 color = isCoGCalculated ? Constants.DebugColorRed : new Vector4(1, 1, 0, 1); // Red for precise, Yellow for geometric/pending

            MySimpleObjectDraw.DrawLine(gravitationalCenter - offsetX, gravitationalCenter + offsetX, MyStringId.GetOrCompute("Square"), ref color, Constants.DebugMarkerThickness, MyBillboard.BlendTypeEnum.Standard);
            MySimpleObjectDraw.DrawLine(gravitationalCenter - offsetY, gravitationalCenter + offsetY, MyStringId.GetOrCompute("Square"), ref color, Constants.DebugMarkerThickness, MyBillboard.BlendTypeEnum.Standard);
            MySimpleObjectDraw.DrawLine(gravitationalCenter - offsetZ, gravitationalCenter + offsetZ, MyStringId.GetOrCompute("Square"), ref color, Constants.DebugMarkerThickness, MyBillboard.BlendTypeEnum.Standard);

            if (isHighlighted)
            {
                float highlightMarkerSize = (float)asteroidRadius * 0.5f;
                Vector4 highlightColor = Constants.DebugColorWhite;
                float highlightThickness = Constants.DebugHighlightThickness;

                MySimpleObjectDraw.DrawLine(gravitationalCenter - new Vector3D(highlightMarkerSize, 0, 0), gravitationalCenter + new Vector3D(highlightMarkerSize, 0, 0), MyStringId.GetOrCompute("Square"), ref highlightColor, highlightThickness, MyBillboard.BlendTypeEnum.Standard);
                MySimpleObjectDraw.DrawLine(gravitationalCenter - new Vector3D(0, highlightMarkerSize, 0), gravitationalCenter + new Vector3D(0, highlightMarkerSize, 0), MyStringId.GetOrCompute("Square"), ref highlightColor, highlightThickness, MyBillboard.BlendTypeEnum.Standard);
                MySimpleObjectDraw.DrawLine(gravitationalCenter - new Vector3D(0, 0, highlightMarkerSize), gravitationalCenter + new Vector3D(0, 0, highlightMarkerSize), MyStringId.GetOrCompute("Square"), ref highlightColor, highlightThickness, MyBillboard.BlendTypeEnum.Standard);
            }
        }
    }
}
