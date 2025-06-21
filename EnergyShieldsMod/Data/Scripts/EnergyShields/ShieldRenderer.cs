using System;
using System.Collections.Generic;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using VRage.Game;
using VRageRender; // For MySimpleObjectDraw, MyBillboard (used for BlendTypeEnum)
using Sandbox.ModAPI; // For MyAPIGateway, IMyCharacter

namespace EnergyShields
{
    /// <summary>
    /// Custom struct to represent a triangular face using vertex indices.
    /// Replaces System.Tuple<int, int, int> for compatibility.
    /// </summary>
    public struct Face
    {
        public int V1, V2, V3;

        public Face(int v1, int v2, int v3)
        {
            V1 = v1;
            V2 = v2;
            V3 = v3;
        }
    }

    /// <summary>
    /// Custom struct to represent an edge using two vertex indices.
    /// Replaces System.Tuple<int, int> for compatibility.
    /// Implements IEquatable for use in HashSet to ensure unique edges.
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
    /// Custom class to hold generated mesh data.
    /// Replaces System.Tuple<List<Vector3D>, List<Tuple<int, int>>> for compatibility.
    /// </summary>
    public class MeshData
    {
        public List<Vector3D> Vertices;
        public List<Edge> Edges;

        public MeshData(List<Vector3D> vertices, List<Edge> edges)
        {
            Vertices = vertices;
            Edges = edges;
        }
    }


    /// <summary>
    /// Handles rendering the shield sphere and impact effects.
    /// </summary>
    public class ShieldRenderer
    {
        private readonly IMyEntity _parentEntity;
        private readonly IShield _shield;
        // Using MyStringId for materials with MySimpleObjectDraw.
        private readonly MyStringId _sphereLineMaterial = MyStringId.GetOrCompute("WeaponLaser"); // A material for the shield lines.
        private readonly MyStringId _impactLineMaterial = MyStringId.GetOrCompute("ProjectileTrailLine"); // A material for impact lines.

        private readonly List<ImpactEffect> _impacts = new List<ImpactEffect>();
        private readonly List<RippleEffect> _ripples = new List<RippleEffect>(); // New list for ripple effects

        // Cache for the geodesic sphere vertices and edges to avoid recalculating every frame.
        // The key is the subdivision level.
        private static Dictionary<int, MeshData> _geodesicCache = new Dictionary<int, MeshData>();

        public ShieldRenderer(IMyEntity parentEntity, IShield shield)
        {
            _parentEntity = parentEntity;
            _shield = shield;
        }

        public void Update()
        {
            if (_parentEntity == null || _parentEntity.MarkedForClose || !_shield.IsActive)
            {
                return;
            }

            // Draw the main shield sphere
            var worldMatrix = _parentEntity.WorldMatrix;
            float healthPercent = _shield.CurrentHp / _shield.MaxHp;

            Color baseColor;
            switch (_shield.Type)
            {
                case ShieldType.Modular:
                    baseColor = Color.CornflowerBlue;
                    break;
                case ShieldType.Generic:
                default:
                    baseColor = Color.Cyan;
                    break;
            }

            var color = Color.Lerp(Color.Red, baseColor, healthPercent);
            // Adjust transparency for visibility.
            color.A = (byte)(80 + healthPercent * 100); // Increased alpha for line visibility

            var colorV4 = color.ToVector4();

            // Determine the center of the shield sphere.
            Vector3D shieldCenter = worldMatrix.Translation;
            IMyCharacter character = _parentEntity as IMyCharacter; // Attempt to cast _parentEntity to IMyCharacter
            bool isLocalPlayerCharacter = (character != null && character == MyAPIGateway.Session?.Player?.Character);

            if (isLocalPlayerCharacter)
            {
                // If it's the local player character, adjust the center to simulate the chest area.
                // Adjusted offset for character's chest position.
                shieldCenter += character.WorldMatrix.Up * 1.4; // Adjusted from 1.2 to 1.4
            }

            // Define rendering parameters based on view mode
            int currentSubdivisionLevel;
            float currentLineThickness;

            if (isLocalPlayerCharacter && MyAPIGateway.Session.CameraController.IsInFirstPersonView)
            {
                // Settings for first-person view of the local player
                currentSubdivisionLevel = 1; // Very low detail to be less distracting
                currentLineThickness = 0.0001f; // Even thinner for first-person
            }
            else
            {
                // Settings for third-person view, or for other entities (non-player)
                currentSubdivisionLevel = 1; 
                currentLineThickness = 0.001f; 
            }

            if (_shield.Type == ShieldType.Modular)
            {
                ShieldRenderer.DrawHexagonalWireframeSphere(shieldCenter, _shield.Range, colorV4, _sphereLineMaterial, currentLineThickness, currentSubdivisionLevel);
            }
            else
            {
                ShieldRenderer.DrawWireframeSphere(shieldCenter, _shield.Range, colorV4, _sphereLineMaterial, currentLineThickness);
            }

            // Update and draw impact effects
            for (int i = _impacts.Count - 1; i >= 0; i--)
            {
                var impact = _impacts[i];
                impact.Update();
                if (impact.IsFinished)
                {
                    _impacts.RemoveAt(i);
                }
                else
                {
                    impact.Draw();
                }
            }

            // Update and draw ripple effects
            for (int i = _ripples.Count - 1; i >= 0; i--)
            {
                var ripple = _ripples[i];
                ripple.Update();
                if (ripple.IsFinished)
                {
                    _ripples.RemoveAt(i);
                }
                else
                {
                    ripple.Draw();
                }
            }
        }

        /// <summary>
        /// Draws a wireframe sphere composed of three orthogonal circles.
        /// </summary>
        /// <param name="center">The center of the sphere.</param>
        /// <param name="radius">The radius of the sphere.</param>
        /// <param name="color">The color of the lines.</param>
        /// <param name="material">The material for the lines.</param>
        /// <param name="thickness">The thickness of the lines.</param>
        private static void DrawWireframeSphere(Vector3D center, float radius, Vector4 color, MyStringId material, float thickness)
        {
            // Draw 3 circles to approximate a sphere
            DrawCircle(center, radius, color, material, thickness, Vector3D.UnitX, Vector3D.UnitY);
            DrawCircle(center, radius, color, material, thickness, Vector3D.UnitX, Vector3D.UnitZ);
            DrawCircle(center, radius, color, material, thickness, Vector3D.UnitY, Vector3D.UnitZ);
        }

        /// <summary>
        /// Draws a hexagonal wireframe sphere by subdividing an icosahedron.
        /// </summary>
        /// <param name="center">The center of the sphere.</param>
        /// <param name="radius">The radius of the sphere.</param>
        /// <param name="color">The color of the lines.</param>
        /// <param name="material">The material for the lines.</param>
        /// <param name="thickness">The thickness of the lines.</param>
        /// <param name="subdivisions">The number of subdivisions to apply to the icosahedron (0 for base icosahedron).</param>
        private static void DrawHexagonalWireframeSphere(Vector3D center, float radius, Vector4 color, MyStringId material, float thickness, int subdivisions)
        {
            // Get or generate the geodesic mesh
            MeshData cachedMesh;
            if (!_geodesicCache.TryGetValue(subdivisions, out cachedMesh))
            {
                cachedMesh = GenerateHexagonalMesh(subdivisions); // Call the new hexagonal mesh generator
                _geodesicCache.Add(subdivisions, cachedMesh);
            }

            List<Vector3D> vertices = cachedMesh.Vertices;
            List<Edge> edges = cachedMesh.Edges;

            // Draw all edges
            foreach (var edge in edges)
            {
                Vector3D start = center + vertices[edge.P1] * radius;
                Vector3D end = center + vertices[edge.P2] * radius;
                MySimpleObjectDraw.DrawLine(start, end, material, ref color, thickness, MyBillboard.BlendTypeEnum.Standard);
            }
        }

        /// <summary>
        /// Generates the vertices and edges for a geodesic dome with hexagonal (and pentagonal) faces.
        /// This creates a more distinct "honeycomb" pattern.
        /// </summary>
        /// <param name="subdivisions">The number of subdivisions (higher means more faces/detail).</param>
        /// <returns>A MeshData object containing a list of normalized vertices and a list of edges.</returns>
        private static MeshData GenerateHexagonalMesh(int subdivisions)
        {
            // Step 1: Generate an initial Icosahedron (same as before)
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

            List<Face> faces = new List<Face>
            {
                new Face(0, 11, 5), new Face(0, 5, 1), new Face(0, 1, 7), new Face(0, 7, 10), new Face(0, 10, 11),
                new Face(3, 9, 4), new Face(3, 4, 2), new Face(3, 2, 6), new Face(3, 6, 8), new Face(3, 8, 9),
                new Face(1, 5, 9), new Face(5, 11, 4), new Face(11, 10, 2), new Face(10, 7, 6), new Face(7, 1, 8),
                new Face(4, 9, 5), new Face(2, 4, 11), new Face(6, 2, 10), new Face(8, 6, 7), new Face(9, 8, 1)
            };

            // Step 2: Subdivide the faces and generate new vertices for the geodesic pattern
            for (int i = 0; i < subdivisions; i++)
            {
                List<Face> newFaces = new List<Face>();
                Dictionary<Edge, int> midpointCache = new Dictionary<Edge, int>(); 

                foreach (var face in faces)
                {
                    int a = GetMidpointForHexagonalMesh(vertices, midpointCache, face.V1, face.V2);
                    int b = GetMidpointForHexagonalMesh(vertices, midpointCache, face.V2, face.V3);
                    int c = GetMidpointForHexagonalMesh(vertices, midpointCache, face.V3, face.V1);

                    newFaces.Add(new Face(face.V1, a, c));
                    newFaces.Add(new Face(face.V2, b, a));
                    newFaces.Add(new Face(face.V3, c, b));
                    newFaces.Add(new Face(a, b, c)); // The central triangle
                }
                faces = newFaces;
            }

            // Step 3: Extract the dual (vertices become face centers, face centers become vertices)
            List<Vector3D> dualVertices = new List<Vector3D>();
            List<Edge> dualEdges = new List<Edge>();
            Dictionary<Face, int> faceCenterIndices = new Dictionary<Face, int>();
            Dictionary<int, List<int>> vertexToFaceMap = new Dictionary<int, List<int>>(); // Maps original vertex to list of faces it belongs to

            // Populate vertexToFaceMap and create dual vertices (face centers)
            for (int faceIndex = 0; faceIndex < faces.Count; faceIndex++)
            {
                Face currentFace = faces[faceIndex];
                Vector3D center = (vertices[currentFace.V1] + vertices[currentFace.V2] + vertices[currentFace.V3]);
                center.Normalize();
                dualVertices.Add(center);
                faceCenterIndices.Add(currentFace, dualVertices.Count - 1);

                if (!vertexToFaceMap.ContainsKey(currentFace.V1)) vertexToFaceMap[currentFace.V1] = new List<int>();
                if (!vertexToFaceMap.ContainsKey(currentFace.V2)) vertexToFaceMap[currentFace.V2] = new List<int>();
                if (!vertexToFaceMap.ContainsKey(currentFace.V3)) vertexToFaceMap[currentFace.V3] = new List<int>();

                vertexToFaceMap[currentFace.V1].Add(faceIndex);
                vertexToFaceMap[currentFace.V2].Add(faceIndex);
                vertexToFaceMap[currentFace.V3].Add(faceIndex);
            }

            // Step 4: Create dual edges based on adjacency of original faces
            HashSet<Edge> uniqueDualEdges = new HashSet<Edge>();
            foreach (var kvp in vertexToFaceMap)
            {
                List<int> adjacentFaceIndices = kvp.Value;
                adjacentFaceIndices.Sort(); 

                for (int i = 0; i < adjacentFaceIndices.Count; i++)
                {
                    for (int j = i + 1; j < adjacentFaceIndices.Count; j++)
                    {
                        int face1Index = adjacentFaceIndices[i];
                        int face2Index = adjacentFaceIndices[j];

                        Edge newDualEdge = new Edge(face1Index, face2Index); 
                        uniqueDualEdges.Add(newDualEdge);
                    }
                }
            }
            
            List<Edge> finalEdges = new List<Edge>();
            foreach(Edge dEdge in uniqueDualEdges)
            {
                finalEdges.Add(new Edge(dEdge.P1, dEdge.P2));
            }


            return new MeshData(dualVertices, finalEdges);
        }

        /// <summary>
        /// Helper method to find or create a midpoint vertex during icosahedron subdivision.
        /// </summary>
        private static int GetMidpointForHexagonalMesh(List<Vector3D> vertices, Dictionary<Edge, int> midpointCache, int p1, int p2)
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


        /// <summary>
        /// Draws a circle using MySimpleObjectDraw.DrawLine.
        /// </summary>
        /// <param name="center">The center of the circle.</param>
        /// <param name="radius">The radius of the circle.</param>
        /// <param name="color">The color of the circle lines.</param>
        /// <param name="material">The material for the lines.</param>
        /// <param name="thickness">The thickness of the lines.</param>
        /// <param name="axis1">The first axis defining the circle's plane (e.g., Vector3D.UnitX).</param>
        /// <param name="axis2">The second axis defining the circle's plane (e.g., Vector3D.UnitY).</param>
        private static void DrawCircle(Vector3D center, float radius, Vector4 color, MyStringId material, float thickness, Vector3D axis1, Vector3D axis2)
        {
            int segments = 60; // Increased segments for smoother circles. Adjust for performance if needed.
            double angleStep = (2 * Math.PI) / segments;
            Vector3D previousPoint = center + (axis1 * radius); // Start point on the circle

            for (int i = 1; i <= segments; i++)
            {
                double angle = i * angleStep;
                // Calculate the next point on the circle using trigonometry and the two axes.
                Vector3D nextPoint = center + (axis1 * Math.Cos(angle) + axis2 * Math.Sin(angle)) * radius;

                // Draw a line segment from the previous point to the current point.
                MySimpleObjectDraw.DrawLine(previousPoint, nextPoint, material, ref color, thickness, MyBillboard.BlendTypeEnum.Standard);
                previousPoint = nextPoint;
            }
        }


        /// <summary>
        /// Creates a visual impact effect at the given position.
        /// </summary>
        /// <param name="hitPosition">The world position of the impact.</param>
        public void CreateImpactEffect(Vector3D hitPosition)
        {
            if (_parentEntity == null) return;

            Color impactColor;
            switch (_shield.Type)
            {
                case ShieldType.Modular:
                    impactColor = Color.LightSkyBlue;
                    break;
                case ShieldType.Generic:
                default:
                    impactColor = Color.LightCyan;
                    break;
            }

            // Add a new ImpactEffect (sphere) and a RippleEffect (expanding circle)
            _impacts.Add(new ImpactEffect(hitPosition, _parentEntity.WorldMatrix.Translation, _shield.Range, impactColor, _impactLineMaterial));
            _ripples.Add(new RippleEffect(hitPosition, _parentEntity.WorldMatrix.Translation, _shield.Range, impactColor, _impactLineMaterial));
        }

        /// <summary>
        /// Represents a single impact effect on the shield (expanding sphere).
        /// </summary>
        private class ImpactEffect
        {
            private readonly Vector3D _hitPosition;
            private readonly Vector3D _shieldCenter;
            private float _age;
            private readonly Color _baseColor;
            private readonly MyStringId _material; // Material for the impact lines
            private const float MaxAge = 0.5f; // Duration of the impact effect in seconds.

            /// <summary>
            /// Gets a value indicating whether the impact effect has finished its animation.
            /// </summary>
            public bool IsFinished => _age >= MaxAge;

            /// <summary>
            /// Initializes a new instance of the ImpactEffect class.
            /// </summary>
            /// <param name="hitPosition">The world position where the impact occurred.</param>
            /// <param name="shieldCenter">The center of the shield sphere.</param>
            /// <param name="shieldRadius">The radius of the shield sphere.</param>
            /// <param name="baseColor">The base color of the impact effect.</param>
            /// <param name="material">The material for the impact lines.</param>
            public ImpactEffect(Vector3D hitPosition, Vector3D shieldCenter, float shieldRadius, Color baseColor, MyStringId material)
            {
                _shieldCenter = shieldCenter;
                _baseColor = baseColor;
                _material = material;
                // Calculate the exact position on the shield's surface.
                var direction = Vector3D.Normalize(hitPosition - shieldCenter);
                _hitPosition = shieldCenter + direction * shieldRadius;
            }

            /// <summary>
            /// Updates the age of the impact effect. Should be called every game tick.
            /// </summary>
            public void Update() { _age += 1f / 60f; } // Assuming 60 FPS.

            /// <summary>
            /// Draws the impact effect.
            /// </summary>
            public void Draw()
            {
                float progress = _age / MaxAge;
                // Scale the impact effect from small to large.
                float scale = MathHelper.Lerp(0.1f, 5f, progress);
                // Fade out the impact effect.
                float alpha = 1f - progress;
                var color = _baseColor * alpha;

                // Draw the impact effect as an expanding hexagonal wireframe sphere.
                // Pass a local variable for color to satisfy 'ref' parameter requirement.
                Vector4 impactColorV4 = color.ToVector4();
                // Call the static DrawHexagonalWireframeSphere method for impact.
                // Using a reduced subdivision level and line thickness for impacts in first-person to be less distracting.
                ShieldRenderer.DrawHexagonalWireframeSphere(_hitPosition, scale, impactColorV4, _material, 0.01f, 1); // Subdivision level 1 for impacts
            }
        }

        /// <summary>
        /// Represents a single ripple effect on the shield (expanding circle on the impact plane).
        /// </summary>
        private class RippleEffect
        {
            private readonly Vector3D _hitPosition;
            private readonly Vector3D _shieldCenter;
            private float _age;
            private readonly Color _baseColor;
            private readonly MyStringId _material;
            private const float MaxAge = 0.8f; // Duration of the ripple effect in seconds.
            private const float MaxRippleRadiusMultiplier = 1.5f; // How much the ripple expands beyond shield radius.

            public bool IsFinished => _age >= MaxAge;

            public RippleEffect(Vector3D hitPosition, Vector3D shieldCenter, float shieldRadius, Color baseColor, MyStringId material)
            {
                _shieldCenter = shieldCenter;
                _baseColor = baseColor;
                _material = material;
                // Calculate the exact position on the shield's surface as the origin of the ripple.
                var direction = Vector3D.Normalize(hitPosition - shieldCenter);
                _hitPosition = shieldCenter + direction * shieldRadius;
            }

            public void Update() { _age += 1f / 60f; }

            public void Draw()
            {
                float progress = _age / MaxAge;
                if (progress > 1f) return; // Ensure it doesn't draw after finishing

                // Ripple radius expands from 0 to MaxRippleRadiusMultiplier * shield radius
                // Corrected: Use Vector3D.Distance for static method call.
                float currentRippleRadius = MathHelper.Lerp(0f, (float)Vector3D.Distance(_shieldCenter, _hitPosition) * MaxRippleRadiusMultiplier, progress);
                
                // Fade out the ripple
                float alpha = 1f - progress;
                var color = _baseColor * alpha;
                Vector4 colorV4 = color.ToVector4();

                // To draw a circle on the surface of the sphere at the hit point,
                // we need its normal (direction from center to hit)
                Vector3D normal = Vector3D.Normalize(_hitPosition - _shieldCenter);

                // Find two perpendicular vectors to the normal to define the circle's plane
                Vector3D axis1 = Vector3D.CalculatePerpendicularVector(normal);
                Vector3D axis2 = Vector3D.Cross(normal, axis1);
                axis1.Normalize(); // Ensure unit vectors
                axis2.Normalize();

                // Draw the ripple circle. The center of this circle is the hit position.
                ShieldRenderer.DrawCircle(_hitPosition, currentRippleRadius, colorV4, _material, 0.005f, axis1, axis2); // Slightly thicker for visibility
            }
        }
    }
}
