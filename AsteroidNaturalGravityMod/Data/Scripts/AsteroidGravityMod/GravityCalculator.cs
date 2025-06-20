// GravityCalculator.cs
// Handles the calculation of gravitational force exerted by asteroids.

using System; // For Math
using VRageMath; // For Vector3D, MathHelper
using Sandbox.ModAPI; // For IMyVoxelMap (indirectly via AsteroidCache)

// All classes are in the same namespace and folder, so implicit visibility should work.

namespace AsteroidGravityMod
{
    public class GravityCalculator
    {
        private AsteroidCache _asteroidCache;
        private ModSettings _settings; // Reference to the mod's current settings
        private Logger _logger; // Reference to the custom logger

        // Ensure these are defined as class fields
        private Vector3D _lastComputedPosition = Vector3D.Zero;
        private Vector3D _lastComputedGravity = Vector3D.Zero;

        public GravityCalculator(ModSettings settings, AsteroidCache asteroidCache, Logger logger)
        {
            this._settings = settings;
            this._asteroidCache = asteroidCache;
            this._logger = logger;
        }

        public Vector3D ComputeTotalGravity(Vector3D currentPosition)
        {
            if ((currentPosition - this._lastComputedPosition).LengthSquared() < Constants.PositionThresholdSq)
            {
                return this._lastComputedGravity; // Return cached value if position hasn't changed significantly
            }

            this._lastComputedPosition = currentPosition;
            Vector3D strongestGravityVector = Vector3D.Zero;
            double maxStrengthMagnitudeSq = 0;
            double strongestAsteroidRadius = 0;

            if (this._asteroidCache?.AsteroidsData == null) return Vector3D.Zero;

            foreach (AsteroidData asteroid in this._asteroidCache.AsteroidsData.Values)
            {
                if (asteroid == null) continue;

                // Influence range based on asteroid size, capped by global detection range
                double asteroidInfluenceRange = asteroid.Radius * 1.25;
                asteroidInfluenceRange = Math.Min(asteroidInfluenceRange, this._settings.GravitationalDetectionRange);
                double asteroidInfluenceRangeSq = asteroidInfluenceRange * asteroidInfluenceRange;

                Vector3D directionVector = asteroid.Position - currentPosition;
                double distanceSq = directionVector.LengthSquared();

                if (distanceSq < asteroidInfluenceRangeSq)
                {
                    double distance = Math.Sqrt(distanceSq);
                    float currentScaledGravity;

                    if (distance < 0.01) // Prevent division by zero or extreme gravity at center
                        currentScaledGravity = (float)this._settings.MaxAccel_ms2;
                    else
                    {
                        float rawGravityStrength = this._settings.BaseGravityStrength * ((float)asteroid.Radius / this._settings.SizeDivisor);
                        float falloffFactor = MathHelper.Clamp(1.0f - (float)(distance / asteroidInfluenceRange), 0f, 1f);
                        currentScaledGravity = rawGravityStrength * falloffFactor;
                        currentScaledGravity = Math.Min(currentScaledGravity, (float)this._settings.MaxAccel_ms2);
                    }

                    Vector3D currentGravityVector = Vector3D.Normalize(directionVector) * currentScaledGravity;
                    double currentStrengthMagnitudeSq = currentGravityVector.LengthSquared();

                    if (currentStrengthMagnitudeSq > maxStrengthMagnitudeSq)
                    {
                        maxStrengthMagnitudeSq = currentStrengthMagnitudeSq;
                        strongestGravityVector = currentGravityVector;
                        strongestAsteroidRadius = asteroid.Radius;
                    }
                }
            }

            if (strongestGravityVector.LengthSquared() == 0)
            {
                this._lastComputedGravity = Vector3D.Zero;
                return Vector3D.Zero;
            }

            // Apply size-based acceleration clamping for final gravity vector
            double computedAccel = strongestGravityVector.Length();
            if (computedAccel > 0)
            {
                float sizeRatio = MathHelper.Clamp((float)((strongestAsteroidRadius - 5) / (2000.0 - 5)), 0f, 1f);
                float effectiveMaxAccel = MathHelper.Lerp((float)this._settings.MinAccel_ms2, (float)this._settings.MaxAccel_ms2, sizeRatio);

                float clampedAccel = MathHelper.Clamp((float)computedAccel, (float)this._settings.MinAccel_ms2, effectiveMaxAccel);
                strongestGravityVector = Vector3D.Normalize(strongestGravityVector) * clampedAccel;
            }

            this._lastComputedGravity = strongestGravityVector;
            return strongestGravityVector;
        }

        public AsteroidData GetStrongestInfluencingAsteroid(Vector3D currentPosition)
        {
            AsteroidData strongestAsteroid = null;
            double maxStrengthMagnitudeSq = 0;

            if (this._asteroidCache?.AsteroidsData == null) return null;

            foreach (AsteroidData asteroid in this._asteroidCache.AsteroidsData.Values)
            {
                if (asteroid == null) continue;

                double asteroidInfluenceRange = asteroid.Radius * 1.25;
                asteroidInfluenceRange = Math.Min(asteroidInfluenceRange, this._settings.GravitationalDetectionRange);
                double asteroidInfluenceRangeSq = asteroidInfluenceRange * asteroidInfluenceRange;

                Vector3D directionVector = asteroid.Position - currentPosition;
                double distanceSq = directionVector.LengthSquared();

                if (distanceSq < asteroidInfluenceRangeSq)
                {
                    double distance = Math.Sqrt(distanceSq);
                    float currentScaledGravity;

                    if (distance < 0.01)
                        currentScaledGravity = (float)this._settings.MaxAccel_ms2;
                    else
                    {
                        float rawGravityStrength = this._settings.BaseGravityStrength * ((float)asteroid.Radius / this._settings.SizeDivisor);
                        float falloffFactor = MathHelper.Clamp(1.0f - (float)(distance / asteroidInfluenceRange), 0f, 1f);
                        currentScaledGravity = rawGravityStrength * falloffFactor;
                        currentScaledGravity = Math.Min(currentScaledGravity, (float)this._settings.MaxAccel_ms2);
                    }

                    Vector3D currentGravityVector = Vector3D.Normalize(directionVector) * currentScaledGravity;
                    double currentStrengthMagnitudeSq = currentGravityVector.LengthSquared();

                    if (currentStrengthMagnitudeSq > maxStrengthMagnitudeSq)
                    {
                        maxStrengthMagnitudeSq = currentStrengthMagnitudeSq;
                        strongestAsteroid = asteroid;
                    }
                }
            }
            return strongestAsteroid;
        }
    }
}
