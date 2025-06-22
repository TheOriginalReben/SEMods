using System;
using System.Collections.Generic;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath; 
using VRage.Game; // Required for MySimpleObjectRasterizer
using VRageRender; // For MySimpleObjectDraw, MyBillboard (used for BlendTypeEnum)
using Sandbox.ModAPI; // For MyAPIGateway, IMyCharacter

namespace EnergyShields
{
    /// <summary>
    /// Handles rendering the shield sphere and impact effects.
    /// </summary>
    public class ShieldRenderer
    {
        private readonly IMyEntity _parentEntity;
        private readonly IShield _shield;
        private readonly MyStringId _sphereLineMaterial = MyStringId.GetOrCompute("WeaponLaser"); 
        private readonly MyStringId _impactLineMaterial = MyStringId.GetOrCompute("ProjectileTrailLine"); 
        private readonly MyStringId _hexSolidMaterial = MyStringId.GetOrCompute("Square"); 

        private readonly List<ImpactEffect> _impacts = new List<ImpactEffect>();
        private readonly List<RippleEffect> _ripples = new List<RippleEffect>(); 

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

            var worldMatrix = _parentEntity.WorldMatrix;
            Vector3D shieldCenter = worldMatrix.Translation; 
            IMyCharacter character = _parentEntity as IMyCharacter; 
            bool isLocalPlayerCharacter = (character != null && character == MyAPIGateway.Session?.Player?.Character);

            if (isLocalPlayerCharacter)
            {
                shieldCenter = character.WorldAABB.Center;
            }

            // Determine rendering parameters based on view mode
            int currentSubdivisionLevel; // This is not used in DrawWireframeSphere directly
            float currentLineThickness; 

            if (isLocalPlayerCharacter && MyAPIGateway.Session.CameraController.IsInFirstPersonView)
            {
                currentSubdivisionLevel = 0; // Not used for DrawWireframeSphere
                currentLineThickness = 0.0005f; 
            }
            else
            {
                currentSubdivisionLevel = 2; // Not used for DrawWireframeSphere
                currentLineThickness = 0.002f; 
            }

            if (_shield.Type == ShieldType.Modular)
            {
                // For Modular Shields, render individual zones
                foreach (var zoneRenderData in _shield.GetZoneRenderData(shieldCenter))
                {
                    Color baseColor = Color.CornflowerBlue; 
                    var zoneColor = Color.Lerp(Color.Red, baseColor, zoneRenderData.HealthRatio);
                    zoneColor.A = (byte)(80 + zoneColor.A); // Corrected alpha calculation for better blending

                    Vector4 zoneColorV4 = zoneColor.ToVector4();

                    // Multi-layer effect: Draw a slightly smaller, more solid hexagonal face first,
                    // then an outer wireframe.

                    // Layer 1: Slightly smaller, semi-solid face (inner layer)
                    Vector3D[] innerVertices = new Vector3D[zoneRenderData.Vertices.Length];
                    for(int i = 0; i < zoneRenderData.Vertices.Length; i++)
                    {
                        innerVertices[i] = Vector3D.Lerp(shieldCenter, zoneRenderData.Vertices[i], 0.95); 
                    }
                    DrawPolygonOutline(innerVertices, zoneColorV4, _hexSolidMaterial, currentLineThickness * 2.0f);


                    // Layer 2: Outer wireframe
                    DrawPolygonOutline(zoneRenderData.Vertices, zoneColorV4, _sphereLineMaterial, currentLineThickness);
                }
            }
            else // Generic Shield
            {
                // --- Draw the shield sphere itself ---
                // Replaced MySimpleObjectDraw.DrawTransparentSphere with DrawWireframeSphere helper

                // For the "filled" sphere (using wireframe with a denser, more transparent color)
                Color filledSphereColor = Color.CornflowerBlue * 0.1f;
                Vector4 filledSphereColorV4 = filledSphereColor.ToVector4(); // Convert to Vector4
                ShieldRenderer.DrawWireframeSphere(shieldCenter, _shield.Range, filledSphereColorV4, _hexSolidMaterial, currentLineThickness * 0.5f);

                // For the "wireframe" sphere (using a more distinct color and thickness)
                float healthPercent = _shield.CurrentHp / _shield.MaxHp;
                Color baseColor = Color.Cyan;
                var wireframeSphereColor = Color.Lerp(Color.Red, baseColor, healthPercent);
                wireframeSphereColor.A = (byte)(80 + wireframeSphereColor.A); // Corrected alpha calculation
                Vector4 wireframeSphereColorV4 = wireframeSphereColor.ToVector4(); // Convert to Vector4
                ShieldRenderer.DrawWireframeSphere(shieldCenter, _shield.Range, wireframeSphereColorV4, _sphereLineMaterial, currentLineThickness);
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
        /// Used for Generic shields.
        /// </summary>
        private static void DrawWireframeSphere(Vector3D center, float radius, Vector4 color, MyStringId material, float thickness)
        {
            DrawCircle(center, radius, color, material, thickness, Vector3D.UnitX, Vector3D.UnitY);
            DrawCircle(center, radius, color, material, thickness, Vector3D.UnitX, Vector3D.UnitZ);
            DrawCircle(center, radius, color, material, thickness, Vector3D.UnitY, Vector3D.UnitZ);
        }

        /// <summary>
        /// Draws a hexagonal wireframe sphere by subdividing an icosahedron.
        /// This method is now explicitly defined in ShieldRenderer to resolve compilation issues.
        /// For simplicity, it currently defers to DrawWireframeSphere, but can be expanded
        /// in the future to draw more specific hexagonal geometry.
        /// </summary>
        /// <param name="center">The center of the sphere.</param>
        /// <param name="radius">The radius of the sphere.</param>
        /// <param name="color">The color of the lines.</param>
        /// <param name="material">The material for the lines.</param>
        /// <param name="thickness">The thickness of the lines.</param>
        /// <param name="subdivisions">The number of subdivisions (ignored for now, kept for signature match).</param>
        public static void DrawHexagonalWireframeSphere(Vector3D center, float radius, Vector4 color, MyStringId material, float thickness, int subdivisions)
        {
            // Currently, this is a placeholder that simply draws a regular wireframe sphere.
            // In a more advanced implementation, this would use the geodesic data to draw actual hexagons.
            DrawWireframeSphere(center, radius, color, material, thickness);
        }

        /// <summary>
        /// Draws a wireframe outline of a polygon given its world vertices.
        /// Used for rendering individual hexagonal zones.
        /// </summary>
        /// <param name="vertices">Array of world coordinates defining the polygon's vertices.</param>
        /// <param name="color">The color of the lines.</param>
        /// <param name="material">The material for the lines.</param>
        /// <param name="thickness">The thickness of the lines.</param>
        private static void DrawPolygonOutline(Vector3D[] vertices, Vector4 color, MyStringId material, float thickness)
        {
            if (vertices == null || vertices.Length < 2) return;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3D start = vertices[i];
                Vector3D end = vertices[(i + 1) % vertices.Length]; 
                // Corrected MySimpleObjectDraw.DrawLine call for compatibility
                MySimpleObjectDraw.DrawLine(start, end, material, ref color, thickness, MyBillboard.BlendTypeEnum.Standard);
            }
        }

        /// <summary>
        /// Draws a circle using MySimpleObjectDraw.DrawLine.
        /// </summary>
        private static void DrawCircle(Vector3D center, float radius, Vector4 color, MyStringId material, float thickness, Vector3D axis1, Vector3D axis2)
        {
            int segments = 60; 
            double angleStep = (2 * Math.PI) / segments;
            Vector3D previousPoint = center + (axis1 * radius);

            for (int i = 1; i <= segments; i++)
            {
                double angle = i * angleStep;
                Vector3D nextPoint = center + (axis1 * Math.Cos(angle) + axis2 * Math.Sin(angle)) * radius;

                // Corrected MySimpleObjectDraw.DrawLine call for compatibility
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
            private readonly MyStringId _material;
            private const float MaxAge = 0.5f;

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
                var direction = Vector3D.Normalize(hitPosition - shieldCenter);
                _hitPosition = shieldCenter + direction * shieldRadius;
            }

            public void Update() { _age += 1f / 60f; }

            public void Draw()
            {
                float progress = _age / MaxAge;
                // Scale the impact effect from small to large.
                float scale = MathHelper.Lerp(0.1f, 5f, progress);
                // Fade out the impact effect.
                float alpha = 1f - progress;
                var color = _baseColor * alpha;
                Vector4 impactColorV4 = color.ToVector4();
                
                // Call the static DrawHexagonalWireframeSphere method for impact.
                // Note: The thickness parameter here for DrawHexagonalWireframeSphere will also be affected by the DrawLine changes.
                ShieldRenderer.DrawHexagonalWireframeSphere(_hitPosition, scale, impactColorV4, _material, 0.002f, 1); 
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
            private const float MaxAge = 0.8f;
            private const float MaxRippleRadiusMultiplier = 1.5f;

            public bool IsFinished => _age >= MaxAge;

            public RippleEffect(Vector3D hitPosition, Vector3D shieldCenter, float shieldRadius, Color baseColor, MyStringId material)
            {
                _shieldCenter = shieldCenter;
                _baseColor = baseColor;
                _material = material;
                var direction = Vector3D.Normalize(hitPosition - shieldCenter);
                _hitPosition = shieldCenter + direction * shieldRadius;
            }

            public void Update() { _age += 1f / 60f; }

            public void Draw()
            {
                float progress = _age / MaxAge;
                if (progress > 1f) return;

                float currentRippleRadius = MathHelper.Lerp(0f, (float)Vector3D.Distance(_shieldCenter, _hitPosition) * MaxRippleRadiusMultiplier, progress);
                
                float alpha = 1f - progress;
                var color = _baseColor * alpha;
                Vector4 colorV4 = color.ToVector4();

                Vector3D normal = Vector3D.Normalize(_hitPosition - _shieldCenter);
                Vector3D axis1 = Vector3D.CalculatePerpendicularVector(normal);
                Vector3D axis2 = Vector3D.Cross(normal, axis1);
                axis1.Normalize();
                axis2.Normalize();

                ShieldRenderer.DrawCircle(_hitPosition, currentRippleRadius, colorV4, _material, 0.005f, axis1, axis2); 
            }
        }
    }
}