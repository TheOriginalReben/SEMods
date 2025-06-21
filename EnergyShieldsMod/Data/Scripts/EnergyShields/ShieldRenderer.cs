using System;
using System.Collections.Generic;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using VRage.Game;
using VRageRender; // Using the same namespace as the working DebugRenderer.cs

namespace EnergyShields
{
    /// <summary>
    /// Handles rendering the shield sphere and impact effects.
    /// </summary>
    public class ShieldRenderer
    {
        private readonly IMyEntity _parentEntity;
        private readonly IShield _shield;
        private readonly MyStringId _sphereMaterial = MyStringId.GetOrCompute("Square"); // Changed for better visibility testing
        private readonly MyStringId _impactMaterial = MyStringId.GetOrCompute("ProjectileTrailLine");

        private readonly List<ImpactEffect> _impacts = new List<ImpactEffect>();

        public ShieldRenderer(IMyEntity parentEntity, IShield shield)
        {
            _parentEntity = parentEntity;
            _shield = shield;
        }

        public void Update()
        {
            MyAPIGateway.Utilities.ShowNotification("ShieldRenderer Update", 16);
            if (_parentEntity == null || _parentEntity.MarkedForClose || !_shield.IsActive)
            {
                return;
            }
            MyAPIGateway.Utilities.ShowNotification($"Shield Range: {_shield.Range}", 16);
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
            // Make the shield slightly more opaque for better visibility.
            color.A = (byte)(20 + healthPercent * 80);

            var colorV4 = color.ToVector4();

            // The player's shield sphere should not be rendered in first-person view
            // as it would obstruct the player's vision or be clipped.
            bool isLocalPlayer = _parentEntity == MyAPIGateway.Session?.Player?.Character;
            if (!isLocalPlayer || !MyAPIGateway.Session.CameraController.IsInFirstPersonView)
            {
                // Based on DebugRenderer.cs, we are targeting an older API.
                // In this API, we must construct a MyBillboard object manually by defining its corners.                
                var characterCenter = worldMatrix.Translation; // Character's position                
                var billboardSize = _shield.Range * 2; // Full size (diameter)

                // Offset the billboard slightly forward from the character's center in world space
                // to avoid Z-fighting, but keep a fixed orientation.
                var center = characterCenter + worldMatrix.Forward * 0.5f; // Offset by 0.5m

                // Create a fixed orientation matrix (facing the camera initially)
                var fixedOrientation = MatrixD.CreateBillboard(center, MyAPIGateway.Session.Camera.Position, Vector3D.Up, null);
                var right = fixedOrientation.Right * billboardSize * 0.5f;
                var up = fixedOrientation.Up * billboardSize * 0.5f;

                // Now, construct the billboard using the fixed orientation.
                // This ensures it's always facing the same way relative to the world.

                var billboard = new MyBillboard()
                {
                    Material = _sphereMaterial,
                    Position0 = center - right - up,
                    Position1 = center + right - up,
                    Position2 = center + right + up,
                    Position3 = center - right + up,
                    Color = colorV4,
                    BlendType = MyBillboard.BlendTypeEnum.Standard // Use Standard as confirmed to work.
                };
                MyTransparentGeometry.AddBillboard(billboard, true);
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
        }

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

            _impacts.Add(new ImpactEffect(hitPosition, _parentEntity.WorldMatrix.Translation, _shield.Range, _impactMaterial, impactColor));
        }

        private class ImpactEffect
        {
            private readonly Vector3D _hitPosition;
            private readonly Vector3D _shieldCenter;
            private readonly MyStringId _material;
            private float _age;
            private readonly Color _baseColor;
            private const float MaxAge = 0.5f;

            public bool IsFinished => _age >= MaxAge;

            public ImpactEffect(Vector3D hitPosition, Vector3D shieldCenter, float shieldRadius, MyStringId material, Color baseColor)
            {
                _shieldCenter = shieldCenter;
                _material = material;
                _baseColor = baseColor;
                var direction = Vector3D.Normalize(hitPosition - shieldCenter);
                _hitPosition = shieldCenter + direction * shieldRadius;
            }

            public void Update() { _age += 1f / 60f; }

            public void Draw()
            {
                float progress = _age / MaxAge;
                float scale = MathHelper.Lerp(0.1f, 5f, progress);
                float alpha = 1f - progress;
                var color = _baseColor * alpha;
                var normal = Vector3D.Normalize(_hitPosition - _shieldCenter);
                var up = Vector3D.CalculatePerpendicularVector(normal);
                var matrix = MatrixD.CreateWorld(_hitPosition, -normal, up);

                // Create billboard for impact effect by defining its corners.
                var impactRight = matrix.Right * scale;
                var impactUp = matrix.Up * scale;
                var impactCenter = matrix.Translation;

                var impactBillboard = new MyBillboard()
                {
                    Material = _material,
                    Position0 = impactCenter - impactRight - impactUp,
                    Position1 = impactCenter + impactRight - impactUp,
                    Position2 = impactCenter + impactRight + impactUp,
                    Position3 = impactCenter - impactRight + impactUp,
                    Color = color.ToVector4(),
                    BlendType = MyBillboard.BlendTypeEnum.Standard
                };
                MyAPIGateway.Utilities.ShowNotification("Drawing Billboard", 16);
                MyTransparentGeometry.AddBillboard(impactBillboard, true);
            }
        }
    }
}