using System;
using System.Collections.Generic; // Added for List<T>
using System.Linq; // Added for Sum(), All()
using VRageMath;

namespace EnergyShields
{
    /// <summary>
    /// Custom struct to represent an edge using two vertex indices.
    /// Implements IEquatable for use in HashSet to ensure unique edges.
    /// Moved here from GeodesicGenerator for broader accessibility within EnergyShields namespace.
    /// </summary>
    public struct Edge : IEquatable<Edge>
    {
        public int P1, P2;

        public Edge(int p1, int p2)
        {
            // Ensure consistent order for hashing and equality comparison
            P1 = Math.Min(p1, p2);
            P2 = Math.Max(p1, p2);
        }

        public bool Equals(Edge other)
        {
            return (P1 == other.P1 && P2 == other.P2);
        }

        public override bool Equals(object obj)
        {
            if (obj is Edge)
                return Equals((Edge)obj);
            return false;
        }

        public override int GetHashCode()
        {
            // Simple hash combining two integers
            return P1.GetHashCode() ^ P2.GetHashCode();
        }
    }

    /// <summary>
    /// Represents a polygonal face with references to its vertices.
    /// Moved here from GeodesicGenerator for broader accessibility within EnergyShields namespace.
    /// </summary>
    public class PolygonFace
    {
        public int[] VertexIndices; // Indices into the main vertices list

        public PolygonFace(params int[] indices)
        {
            VertexIndices = indices;
        }
    }


    /// <summary>
    /// Contains the constants and configuration for the energy shields.
    /// These values are derived from your EnergyShieldsModPlan.cs file.
    /// </summary>
    public static class ShieldConstants
    {
        // Changed ShieldHpPerMeter to 500 to achieve 1000 HP for a 2-meter player shield.
        public const double ShieldHpPerMeter = 500;
        public const double GenericShieldHpMultiplier = 5;
        public const double ModularShieldHpMultiplier = 1;
        public const double GenericShieldWattUsage = 500;
        public const int MaxRange = 50; // meters
        public const int MinRange = 1; // meters
    }

    /// <summary>
    /// Defines the types of shields available.
    /// </summary>
    public enum ShieldType
    {
        None,
        Generic,
        Modular
    }

    /// <summary>
    /// Interface defining the core properties and actions of any shield.
    /// </summary>
    public interface IShield
    {
        ShieldType Type { get; }
        float MaxHp { get; }
        float CurrentHp { get; }
        float Range { get; }
        float RegenRatePerSecond { get; }
        float PowerUsageWatts { get; }
        bool IsBroken { get; }
        bool IsActive { get; set; } // Settable to enable/disable shield
        float RechargeDelaySeconds { get; } // For display in terminal info

        event Action<Vector3D> OnShieldHit; // Event for when the shield takes damage visuals

        /// <summary>
        /// Reduces shield HP by a given amount, distributed evenly or generally. Does NOT trigger visual effects.
        /// </summary>
        /// <param name="amount">The amount of damage to take.</param>
        void TakeDamage(float amount);

        /// <summary>
        /// Reduces shield HP at a specific world position, finding the relevant zone if applicable.
        /// Does NOT trigger visual effects directly (TriggerVisualImpact should be called separately).
        /// </summary>
        /// <param name="amount">The amount of damage to take.</param>
        /// <param name="hitPosition">The world position of the hit.</param>
        void TakeDamageAtPosition(float amount, Vector3D hitPosition);

        /// <summary>
        /// Triggers a visual impact effect on the shield without modifying HP or recharge state.
        /// Used for cases like creative mode where damage is 0, but impact visuals are desired.
        /// </summary>
        /// <param name="hitPosition">The world position of the impact for visual effects.</param>
        void TriggerVisualImpact(Vector3D hitPosition);

        /// <summary>
        /// Updates the shield's state, including regeneration.
        /// </summary>
        /// <param name="deltaSeconds">Time elapsed since last update in seconds.</param>
        void Update(float deltaSeconds);

        /// <summary>
        /// Recreates the shield with a new range. This is called when the range setting changes.
        /// </summary>
        /// <param name="newRange">The new range for the shield.</param>
        void Recreate(float newRange);

        // For Modular Shield rendering: Expose individual zone data
        IEnumerable<ShieldZoneRenderData> GetZoneRenderData(Vector3D shieldCenter);
    }

    /// <summary>
    /// Represents a single hexagonal (or pentagonal) zone of a modular shield.
    /// Each zone has its own HP and regeneration logic.
    /// </summary>
    public class ShieldZone
    {
        public float MaxHp { get; private set; }
        public float CurrentHp { get; private set; }
        public float RegenRatePerSecond { get; private set; }
        public float RechargeDelaySeconds { get; private set; } // Delay before regeneration starts after being broken
        private float _timeSinceLastDamage;

        public bool IsBroken => CurrentHp <= 0;

        public ShieldZone(float maxHp, float regenRate, float rechargeDelay)
        {
            MaxHp = maxHp;
            CurrentHp = maxHp; // Start with full HP
            RegenRatePerSecond = regenRate;
            RechargeDelaySeconds = rechargeDelay;
            _timeSinceLastDamage = rechargeDelay; // Start ready to regen if needed
        }

        /// <summary>
        /// Applies damage to this specific shield zone.
        /// </summary>
        /// <param name="amount">The amount of damage to take.</param>
        public void TakeDamage(float amount)
        {
            CurrentHp -= amount;
            if (CurrentHp < 0) CurrentHp = 0; // Cap at zero
            _timeSinceLastDamage = 0; // Reset delay after taking damage
        }

        /// <summary>
        /// Updates the state of this shield zone, including regeneration.
        /// </summary>
        /// <param name="deltaSeconds">Time elapsed since last update in seconds.</param>
        public void Update(float deltaSeconds)
        {
            if (IsBroken)
            {
                _timeSinceLastDamage += deltaSeconds;
                // Only start regenerating after the RechargeDelaySeconds has passed
                if (_timeSinceLastDamage >= RechargeDelaySeconds)
                {
                    CurrentHp += RegenRatePerSecond * deltaSeconds;
                    if (CurrentHp > MaxHp) CurrentHp = MaxHp; // Cap HP at MaxHp
                }
            }
            else // Not broken, continues regeneration (if any, typically 0 for Generic, positive for Modular)
            {
                _timeSinceLastDamage = 0; // Keep delay at zero if not broken (as it means it's still being hit or full)
                CurrentHp += RegenRatePerSecond * deltaSeconds;
                if (CurrentHp > MaxHp) CurrentHp = MaxHp;
            }
        }

        /// <summary>
        /// Updates the parameters of the shield zone (e.g., when range changes).
        /// Current HP is scaled proportionally if not broken.
        /// </summary>
        public void SetNewParameters(float newMaxHp, float newRegenRate, float newRechargeDelay)
        {
            if (MaxHp > 0 && CurrentHp > 0) // If it had health before and is not already broken
            {
                CurrentHp = (CurrentHp / MaxHp) * newMaxHp; // Scale current HP proportionally
            }
            // If current HP was 0 or negative, it remains 0.

            MaxHp = newMaxHp;
            RegenRatePerSecond = newRegenRate;
            RechargeDelaySeconds = newRechargeDelay;
            // If it became broken due to scale down, or was already broken, ensure regen delay resets.
            if (CurrentHp <= 0) _timeSinceLastDamage = 0;
        }
    }

    /// <summary>
    /// Data passed to the renderer for each shield zone.
    /// </summary>
    public struct ShieldZoneRenderData
    {
        public Vector3D Center; // World position of the zone's center
        public Vector3D[] Vertices; // World positions of the zone's hexagonal vertices
        public float HealthRatio; // Current health / Max health (0.0 to 1.0)
    }

    /// <summary>
    /// Base class for all shield types, providing common functionality.
    /// </summary>
    public abstract class ShieldBase : IShield
    {
        public abstract ShieldType Type { get; }
        public virtual float MaxHp { get; protected set; } // Virtual to be overridden by ModularShield
        public virtual float CurrentHp { get; protected set; } // Virtual to be overridden by ModularShield
        public float Range { get; private set; }
        public virtual float RegenRatePerSecond { get; protected set; } // Virtual to be overridden by ModularShield
        public virtual float PowerUsageWatts { get; protected set; } // Virtual to be overridden by ModularShield
        public virtual bool IsBroken => CurrentHp <= 0; // Virtual to be overridden by ModularShield
        public bool IsActive { get; set; } = true;

        public float RechargeDelaySeconds { get; protected set; } // Accessible via interface
        protected float _timeSinceLastDamage; // Protected for base class internal use

        public event Action<Vector3D> OnShieldHit;

        public ShieldBase(float initialRange)
        {
            Range = initialRange;
            // Initial _timeSinceLastDamage is set in derived constructors after RechargeDelaySeconds is known.
        }

        /// <summary>
        /// Reduces shield HP by a given amount, distributed evenly or generally. Does NOT trigger visual effects.
        /// This base implementation is for non-modular shields.
        /// </summary>
        /// <param name="amount">The amount of damage to apply.</param>
        public virtual void TakeDamage(float amount)
        {
            if (!IsActive || IsBroken) return;

            CurrentHp -= amount;
            if (CurrentHp < 0) CurrentHp = 0;
            _timeSinceLastDamage = 0; // Reset recharge delay
        }

        /// <summary>
        /// Applies damage at a specific position. For generic shields, this is the same as general TakeDamage.
        /// Modular shields will override this to find a specific zone.
        /// </summary>
        /// <param name="amount">The amount of damage to take.</param>
        /// <param name="hitPosition">The world position of the hit (ignored for generic shields).</param>
        public virtual void TakeDamageAtPosition(float amount, Vector3D hitPosition)
        {
            TakeDamage(amount); // Default behavior is to apply damage generally
        }

        /// <summary>
        /// Triggers a visual impact effect on the shield without modifying HP or recharge state.
        /// </summary>
        /// <param name="hitPosition">The world position of the impact for visual effects.</param>
        public void TriggerVisualImpact(Vector3D hitPosition)
        {
            if (IsActive) // Only trigger visual if the shield is currently active
            {
                OnShieldHit?.Invoke(hitPosition);
            }
        }

        /// <summary>
        /// Updates the shield's state, including regeneration.
        /// This base implementation is for non-modular shields. ModularShield overrides this.
        /// </summary>
        /// <param name="deltaSeconds">Time elapsed since last update in seconds.</param>
        public virtual void Update(float deltaSeconds)
        {
            if (!IsActive) return;

            if (IsBroken)
            {
                _timeSinceLastDamage += deltaSeconds;
                if (_timeSinceLastDamage >= RechargeDelaySeconds)
                {
                    CurrentHp += RegenRatePerSecond * deltaSeconds;
                    if (CurrentHp > MaxHp) CurrentHp = MaxHp;
                }
            }
            else
            {
                _timeSinceLastDamage = 0;
                CurrentHp += RegenRatePerSecond * deltaSeconds;
                if (CurrentHp > MaxHp) CurrentHp = MaxHp;
            }
        }

        /// <summary>
        /// Recreates the shield with new properties based on the new range.
        /// This base method updates the Range. Derived classes handle specific HP/Regen adjustments.
        /// </summary>
        /// <param name="newRange">The new range for the shield.</param>
        public virtual void Recreate(float newRange)
        {
            float oldMaxHp = MaxHp; 
            Range = newRange; // Update range

            // CurrentHp scaling based on old MaxHp will happen in derived classes after their MaxHp is updated.
            // Reset delay if needed. The newMaxHp check was removed here as it's not available in base.
            // If the shield was broken, or if its scaled HP became zero, reset the timer.
            if (CurrentHp <= 0 || (oldMaxHp > 0 && MaxHp > 0 && CurrentHp / oldMaxHp > MaxHp / oldMaxHp)) // Adjusted condition for resetting delay
            {
                _timeSinceLastDamage = 0;
            }
        }

        public virtual IEnumerable<ShieldZoneRenderData> GetZoneRenderData(Vector3D shieldCenter)
        {
            // Base shields don't have zones, return empty.
            return Enumerable.Empty<ShieldZoneRenderData>();
        }
    }

    /// <summary>
    /// Implements the "Generic" shield type. High HP, but slow to recover once broken.
    /// </summary>
    public class GenericShield : ShieldBase
    {
        public override ShieldType Type => ShieldType.Generic;

        public GenericShield(float range) : base(range)
        {
            // Initial setup using constants and range
            MaxHp = (float)(ShieldConstants.ShieldHpPerMeter * range * ShieldConstants.GenericShieldHpMultiplier);
            CurrentHp = MaxHp; // Start with full HP
            RechargeDelaySeconds = 15f; // Long delay when broken.
            RegenRatePerSecond = MaxHp * 0.02f; // Regenerates 2% of Max HP per second.
            PowerUsageWatts = (float)ShieldConstants.GenericShieldWattUsage;
            _timeSinceLastDamage = RechargeDelaySeconds; // Initialize base delay
        }

        public override void Recreate(float newRange)
        {
            float oldMaxHp = MaxHp;
            base.Recreate(newRange); // Call base to update Range and possibly initial CurrentHp logic

            // Recalculate Generic Shield specific properties
            MaxHp = (float)(ShieldConstants.ShieldHpPerMeter * newRange * ShieldConstants.GenericShieldHpMultiplier);
            RegenRatePerSecond = MaxHp * 0.02f;
            PowerUsageWatts = (float)ShieldConstants.GenericShieldWattUsage;

            // Adjust CurrentHp proportionally if it was not broken before resizing
            if (oldMaxHp > 0 && CurrentHp > 0) // Only adjust if it had health before and is not broken now by base.Recreate()
            {
                CurrentHp = (CurrentHp / oldMaxHp) * MaxHp;
            } else if (CurrentHp <= 0) // If it was broken or became broken due to range reduction
            {
                CurrentHp = 0; // Keep it broken
            } else // If it was full and now MaxHp increased
            {
                CurrentHp = MaxHp; // Set to new MaxHp
            }
        }
    }

    /// <summary>
    /// Implements the "Modular" shield type. Lower HP, but regenerates very quickly.
    /// Composed of individual ShieldZone objects.
    /// </summary>
    public class ModularShield : ShieldBase
    {
        public override ShieldType Type => ShieldType.Modular;
        private List<ShieldZone> _zones;
        private IMyEntity _parentEntity; // Added to get world matrix for zone hit detection

        // Number of zones is based on a fixed subdivision level for the geodesic sphere.
        // Subdivision 0 (icosahedron) has 20 faces. Subdivision 1 has 80 faces, etc.
        private const int GeodesicSubdivisionLevel = 1; // Controls the density of hexagons
        // Cached geodesic data for zone geometry. This will be generated once and shared.
        private static GeodesicGenerator.GeodesicMeshData _cachedGeodesicData;

        public override float MaxHp => _zones.Sum(z => z.MaxHp);
        public override float CurrentHp => _zones.Sum(z => z.CurrentHp);
        // Shield is broken if all its zones are broken.
        public override bool IsBroken => _zones.All(z => z.IsBroken); 
        public override float RegenRatePerSecond => _zones.Sum(z => z.RegenRatePerSecond);
        // Power usage for modular shield is higher.
        public override float PowerUsageWatts { get; protected set; } = (float)ShieldConstants.GenericShieldWattUsage * 1.2f;

        // Modular shield has a short overall recharge delay when it breaks, but zones regen independently.
        private const float ModularShieldOverallRechargeDelaySeconds = 3f;

        public ModularShield(float range, IMyEntity parentEntity) : base(range) // Added parentEntity parameter
        {
            _parentEntity = parentEntity; // Store parent entity
            RechargeDelaySeconds = ModularShieldOverallRechargeDelaySeconds; // Overall shield delay
            _timeSinceLastDamage = RechargeDelaySeconds; // Initialize base delay for overall shield state

            // Ensure geodesic data is generated once
            if (_cachedGeodesicData == null)
            {
                _cachedGeodesicData = GeodesicGenerator.GenerateHexagonalMesh(GeodesicSubdivisionLevel);
            }
            
            InitializeZones(range);
        }

        private void InitializeZones(float currentRange)
        {
            _zones = new List<ShieldZone>();
            
            // Calculate the total HP this modular shield instance should have.
            float totalCalculatedModularHp = (float)(ShieldConstants.ShieldHpPerMeter * currentRange * ShieldConstants.ModularShieldHpMultiplier);

            // Each zone's MaxHp is now equal to the total calculated modular shield HP.
            float zoneMaxHp = totalCalculatedModularHp; 
            // Set zone regeneration rate to 30% of zone's max HP as requested.
            float zoneRegenRate = zoneMaxHp * 0.30f; 
            // Set the individual zone recharge delay to 10 seconds.
            float zoneRechargeDelay = 10f; 

            foreach (var face in _cachedGeodesicData.Faces)
            {
                _zones.Add(new ShieldZone(zoneMaxHp, zoneRegenRate, zoneRechargeDelay));
            }
        }

        /// <summary>
        /// Applies damage to the modular shield by distributing it among all active zones.
        /// This method is now used for non-localized damage, if any such scenario exists.
        /// </summary>
        /// <param name="amount">The total amount of damage to apply.</param>
        public override void TakeDamage(float amount)
        {
            if (!IsActive || IsBroken) return; 

            // Distribute damage evenly across all zones.
            float damagePerZone = amount / _zones.Count;
            foreach (var zone in _zones)
            {
                zone.TakeDamage(damagePerZone);
            }
            
            _timeSinceLastDamage = 0; // Reset overall shield recharge delay
        }

        /// <summary>
        /// Applies damage to the specific zone closest to the hit position.
        /// </summary>
        /// <param name="amount">The amount of damage to apply.</param>
        /// <param name="hitPosition">The world position of the hit.</param>
        public override void TakeDamageAtPosition(float amount, Vector3D hitPosition)
        {
            if (!IsActive || IsBroken) return;

            // Find the closest zone to the hit position
            ShieldZone closestZone = null;
            double minDistanceSq = double.MaxValue;

            Vector3D shieldCenter = _parentEntity.WorldMatrix.Translation;

            for (int i = 0; i < _zones.Count; i++)
            {
                ShieldZone currentZone = _zones[i];
                PolygonFace face = _cachedGeodesicData.Faces[i];

                // Calculate the world center of this zone
                Vector3D zoneWorldCenter = Vector3D.Zero;
                foreach (int vertexIndex in face.VertexIndices)
                {
                    zoneWorldCenter += shieldCenter + _cachedGeodesicData.Vertices[vertexIndex] * Range;
                }
                zoneWorldCenter /= face.VertexIndices.Length;

                double distanceSq = Vector3D.DistanceSquared(hitPosition, zoneWorldCenter);
                if (distanceSq < minDistanceSq)
                {
                    minDistanceSq = distanceSq;
                    closestZone = currentZone;
                }
            }

            if (closestZone != null)
            {
                closestZone.TakeDamage(amount);
            }
            
            _timeSinceLastDamage = 0; // Reset overall shield recharge delay
        }

        /// <summary>
        /// Updates all individual shield zones and the overall shield state.
        /// </summary>
        public override void Update(float deltaSeconds)
        {
            if (!IsActive) return;

            foreach (var zone in _zones)
            {
                zone.Update(deltaSeconds);
            }

            // Update overall shield broken state and regeneration if all zones were broken.
            // The IsBroken getter handles the logic for checking all zones.
            if (IsBroken)
            {
                _timeSinceLastDamage += deltaSeconds;
                if (_timeSinceLastDamage >= RechargeDelaySeconds)
                {
                    // Once overall shield can start regenerating, zones handle their own regen.
                    // This is more for visual/terminal feedback if the whole shield is down.
                    // Individual zones doing their own regen is the main mechanism.
                }
            } else {
                _timeSinceLastDamage = 0; // If not broken, ensure overall delay is reset
            }
        }

        public override void Recreate(float newRange)
        {
            base.Recreate(newRange); // Updates Range

            // Recalculate parameters for each zone based on new range and update them.
            float totalCalculatedModularHp = (float)(ShieldConstants.ShieldHpPerMeter * newRange * ShieldConstants.ModularShieldHpMultiplier);
            
            // Each zone's MaxHp is now equal to the total calculated modular shield HP.
            float zoneMaxHp = totalCalculatedModularHp;
            // Set zone regeneration rate to 30% of zone's max HP as requested.
            float zoneRegenRate = zoneMaxHp * 0.30f; 
            // Set the individual zone recharge delay to 10 seconds.
            float zoneRechargeDelay = 10f;

            foreach (var zone in _zones)
            {
                zone.SetNewParameters(zoneMaxHp, zoneRegenRate, zoneRechargeDelay);
            }
            // Overall MaxHp, CurrentHp, etc. will update via their getters.
        }

        /// <summary>
        /// Provides render data for each individual shield zone.
        /// </summary>
        /// <param name="shieldCenter">The current world center of the shield.</param>
        public override IEnumerable<ShieldZoneRenderData> GetZoneRenderData(Vector3D shieldCenter)
        {
            if (_cachedGeodesicData == null)
            {
                yield break; // Should not happen if initialized correctly
            }

            for (int i = 0; i < _zones.Count; i++)
            {
                ShieldZone zone = _zones[i];
                PolygonFace face = _cachedGeodesicData.Faces[i]; // Correctly referencing PolygonFace

                Vector3D[] worldVertices = new Vector3D[face.VertexIndices.Length];
                for (int v = 0; v < face.VertexIndices.Length; v++)
                {
                    // Scale local vertex by shield range and translate to world center
                    worldVertices[v] = shieldCenter + _cachedGeodesicData.Vertices[face.VertexIndices[v]] * Range;
                }

                Vector3D zoneCenter = Vector3D.Zero;
                foreach (var vert in worldVertices)
                {
                    zoneCenter += vert;
                }
                zoneCenter /= worldVertices.Length;

                yield return new ShieldZoneRenderData
                {
                    Center = zoneCenter,
                    Vertices = worldVertices,
                    HealthRatio = zone.MaxHp > 0 ? zone.CurrentHp / zone.MaxHp : 0f
                };
            }
        }
    }

    /// <summary>
    /// Static class for generating geodesic sphere mesh data.
    /// </summary>
    public static class GeodesicGenerator
    {
        /// <summary>
        /// Custom class to hold generated geodesic mesh data including faces.
        /// </summary>
        public class GeodesicMeshData
        {
            public List<Vector3D> Vertices;
            public List<Edge> Edges; // Original edges, might not be directly used for rendering faces
            public List<PolygonFace> Faces; // The polygonal faces (hexagons/pentagons)

            public GeodesicMeshData(List<Vector3D> vertices, List<Edge> edges, List<PolygonFace> faces)
            {
                Vertices = vertices;
                Edges = edges;
                Faces = faces;
            }
        }

        // Cache for geodesic mesh data
        private static Dictionary<int, GeodesicMeshData> _geodesicCache = new Dictionary<int, GeodesicMeshData>();

        /// <summary>
        /// Generates the vertices, edges, and hexagonal/pentagonal faces for a geodesic dome.
        /// The 'faces' of the returned mesh data correspond to the zones.
        /// </summary>
        /// <param name="subdivisions">The number of subdivisions (0 for base icosahedron dual).</param>
        /// <returns>A GeodesicMeshData object containing normalized vertices, edges, and polygonal faces.</returns>
        public static GeodesicMeshData GenerateHexagonalMesh(int subdivisions)
        {
            GeodesicMeshData cachedData; // Declare out variable before TryGetValue for C# 6 compatibility
            if (_geodesicCache.TryGetValue(subdivisions, out cachedData))
            {
                return cachedData;
            }

            // Step 1: Generate an initial Icosahedron
            double t = (1.0 + Math.Sqrt(5.0)) / 2.0; // Golden ratio
            List<Vector3D> vertices = new List<Vector3D>();
            vertices.Add(Vector3D.Normalize(new Vector3D(-1.0, t, 0.0)));
            vertices.Add(Vector3D.Normalize(new Vector3D(1.0, t, 0.0)));
            vertices.Add(Vector3D.Normalize(new Vector3D(-1.0, -t, 0.0)));
            vertices.Add(Vector3D.Normalize(new Vector3D(1.0, -t, 0.0)));
            vertices.Add(Vector3D.Normalize(new Vector3D(0.0, -1.0, t)));
            vertices.Add(Vector3D.Normalize(new Vector3D(0.0, 1.0, t)));
            vertices.Add(Vector3D.Normalize(new Vector3D(0.0, -1.0, -t)));
            vertices.Add(Vector3D.Normalize(new Vector3D(0.0, 1.0, -t)));
            vertices.Add(Vector3D.Normalize(new Vector3D(t, 0.0, -1.0)));
            vertices.Add(Vector3D.Normalize(new Vector3D(t, 0.0, 1.0)));
            vertices.Add(Vector3D.Normalize(new Vector3D(-t, 0.0, -1.0)));
            vertices.Add(Vector3D.Normalize(new Vector3D(-t, 0.0, 1.0)));

            List<PolygonFace> icosahedronFaces = new List<PolygonFace> // Changed to PolygonFace
            {
                new PolygonFace(0, 11, 5), new PolygonFace(0, 5, 1), new PolygonFace(0, 1, 7), new PolygonFace(0, 7, 10), new PolygonFace(0, 10, 11),
                new PolygonFace(3, 9, 4), new PolygonFace(3, 4, 2), new PolygonFace(3, 2, 6), new PolygonFace(3, 6, 8), new PolygonFace(3, 8, 9),
                new PolygonFace(1, 5, 9), new PolygonFace(5, 11, 4), new PolygonFace(11, 10, 2), new PolygonFace(10, 7, 6), new PolygonFace(7, 1, 8),
                new PolygonFace(4, 9, 5), new PolygonFace(2, 4, 11), new PolygonFace(6, 2, 10), new PolygonFace(8, 6, 7), new PolygonFace(9, 8, 1)
            };

            // Map to store midpoints for efficient subdivision
            Dictionary<Edge, int> midpointCache = new Dictionary<Edge, int>();

            // Step 2: Subdivide the faces (to get a finer triangulation for the dual)
            List<PolygonFace> currentFaces = new List<PolygonFace>(icosahedronFaces);
            for (int i = 0; i < subdivisions; i++)
            {
                List<PolygonFace> newFaces = new List<PolygonFace>();
                foreach (var face in currentFaces)
                {
                    int a = GetMidpointIndex(vertices, midpointCache, face.VertexIndices[0], face.VertexIndices[1]); // Accessing indices correctly
                    int b = GetMidpointIndex(vertices, midpointCache, face.VertexIndices[1], face.VertexIndices[2]);
                    int c = GetMidpointIndex(vertices, midpointCache, face.VertexIndices[2], face.VertexIndices[0]);

                    newFaces.Add(new PolygonFace(face.VertexIndices[0], a, c));
                    newFaces.Add(new PolygonFace(face.VertexIndices[1], b, a));
                    newFaces.Add(new PolygonFace(face.VertexIndices[2], c, b));
                    newFaces.Add(new PolygonFace(a, b, c)); // The central triangle
                }
                currentFaces = newFaces;
            }

            // Step 3: Extract the Dual - Vertices of the original become faces of the dual.
            // Faces of the original become vertices of the dual.
            List<Vector3D> dualVertices = new List<Vector3D>();
            List<PolygonFace> dualFaces = new List<PolygonFace>();
            HashSet<Edge> dualEdges = new HashSet<Edge>(); // To avoid duplicate edges for the dual mesh

            // Map original face index to dual vertex index
            Dictionary<int, int> originalFaceToDualVertexIndex = new Dictionary<int, int>();
            for (int i = 0; i < currentFaces.Count; i++)
            {
                PolygonFace originalFace = currentFaces[i];
                Vector3D center = (vertices[originalFace.VertexIndices[0]] + vertices[originalFace.VertexIndices[1]] + vertices[originalFace.VertexIndices[2]]);
                center.Normalize(); // Normalize to keep it on the unit sphere
                dualVertices.Add(center);
                originalFaceToDualVertexIndex[i] = dualVertices.Count - 1;
            }

            // Group original faces by their shared original vertices to form dual faces
            Dictionary<int, List<int>> originalVertexToFaceIndices = new Dictionary<int, List<int>>();
            for (int i = 0; i < currentFaces.Count; i++)
            {
                PolygonFace face = currentFaces[i];
                // Ensure all vertices are accounted for
                foreach (int vertexIndex in face.VertexIndices)
                {
                    if (!originalVertexToFaceIndices.ContainsKey(vertexIndex))
                    {
                        originalVertexToFaceIndices[vertexIndex] = new List<int>();
                    }
                    originalVertexToFaceIndices[vertexIndex].Add(i);
                }
            }

            // Now, for each original vertex, create a dual face (hexagon or pentagon)
            foreach (var kvp in originalVertexToFaceIndices)
            {
                List<int> adjacentFaceIndices = kvp.Value;
                // Sort the indices to ensure consistent ordering when creating polygons.
                adjacentFaceIndices.Sort((idx1, idx2) =>
                {
                    Vector3D v1 = dualVertices[originalFaceToDualVertexIndex[idx1]];
                    Vector3D v2 = dualVertices[originalFaceToDualVertexIndex[idx2]];
                    Vector3D center = vertices[kvp.Key]; 
                    
                    Vector3D axis1 = Vector3D.CalculatePerpendicularVector(center);
                    Vector3D axis2 = Vector3D.Cross(center, axis1);

                    double angle1 = Math.Atan2(Vector3D.Dot(v1, axis2), Vector3D.Dot(v1, axis1));
                    double angle2 = Math.Atan2(Vector3D.Dot(v2, axis2), Vector3D.Dot(v2, axis1));
                    return angle1.CompareTo(angle2);
                });


                List<int> dualFaceVertexIndices = new List<int>();
                for (int i = 0; i < adjacentFaceIndices.Count; i++)
                {
                    dualFaceVertexIndices.Add(originalFaceToDualVertexIndex[adjacentFaceIndices[i]]);
                }
                
                if (dualFaceVertexIndices.Count > 2) // Polygons must have at least 3 vertices
                {
                    dualFaces.Add(new PolygonFace(dualFaceVertexIndices.ToArray()));
                }

                // Also generate edges for the dual graph if needed for wireframe.
                for (int i = 0; i < adjacentFaceIndices.Count; i++)
                {
                    int idx1 = originalFaceToDualVertexIndex[adjacentFaceIndices[i]];
                    int idx2 = originalFaceToDualVertexIndex[adjacentFaceIndices[(i + 1) % adjacentFaceIndices.Count]];
                    dualEdges.Add(new Edge(idx1, idx2));
                }
            }

            GeodesicMeshData newMeshData = new GeodesicMeshData(dualVertices, dualEdges.ToList(), dualFaces);
            _geodesicCache.Add(subdivisions, newMeshData);
            return newMeshData;
        }

        /// <summary>
        /// Helper method to find or create a midpoint vertex during icosahedron subdivision.
        /// </summary>
        private static int GetMidpointIndex(List<Vector3D> vertices, Dictionary<Edge, int> midpointCache, int p1, int p2)
        {
            Edge edgeKey = new Edge(p1, p2);

            int midpointIndex;
            if (midpointCache.TryGetValue(edgeKey, out midpointIndex))
            {
                return midpointIndex;
            }

            Vector3D mid = (vertices[p1] + vertices[p2]);
            mid.Normalize(); 
            vertices.Add(mid);
            midpointIndex = vertices.Count - 1;
            midpointCache.Add(edgeKey, midpointIndex);
            return midpointIndex;
        }
    }
}