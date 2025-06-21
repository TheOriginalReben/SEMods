using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace EnergyShields
{
    /// <summary>
    /// A session component that intercepts damage and applies it to registered shields.
    /// This class acts as the central hub for all shield interactions.
    /// </summary>
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class ShieldDamageHandler : MySessionComponentBase
    {
        private static ShieldDamageHandler _instance;
        // We map the entity (grid or character) to its shield controller
        private static readonly Dictionary<IMyEntity, IShield> ActiveShields = new Dictionary<IMyEntity, IShield>();

        public override void LoadData()
        {
            _instance = this;
            // The DamageSystem may not be ready in LoadData(). We will register in BeforeStart().
        }

        public override void BeforeStart()
        {
            // Register the damage handler here, as all game systems are guaranteed to be initialized.
            if (MyAPIGateway.Session?.DamageSystem != null)
            {
                MyAPIGateway.Session.DamageSystem.RegisterBeforeDamageHandler(0, OnDamageTaking);
            }
        }

        protected override void UnloadData()
        {
            _instance = null;
            // The damage handler is unregistered automatically by the game.
            ActiveShields.Clear();
        }

        /// <summary>
        /// Registers a shield to an entity (grid or character).
        /// </summary>
        public static void RegisterShield(IMyEntity entity, IShield shield)
        {
            if (_instance == null || entity == null || shield == null) return;
            
            // Use the dictionary's indexer to add or update the shield for the entity.
            ActiveShields[entity] = shield;
        }

        /// <summary>
        /// Unregisters a shield from an entity.
        /// </summary>
        public static void UnregisterShield(IMyEntity entity)
        {
            if (_instance == null || entity == null) return;

            ActiveShields.Remove(entity);
        }

        /// <summary>
        /// This method is called by the game before any damage is applied.
        /// </summary>
        private void OnDamageTaking(object target, ref MyDamageInformation info)
        {
            IMyEntity shieldEntity = null;
            Vector3D? hitPosition = null;
            
            var block = target as IMySlimBlock;
            if (block != null)
            {
                shieldEntity = block.CubeGrid;
                Vector3D worldPos;
                block.ComputeWorldCenter(out worldPos);
                hitPosition = worldPos;
            }
            else
            {
                var character = target as IMyCharacter;
                if (character != null)
                {
                    shieldEntity = character;
                    // Use the character's bounding box center for a more accurate hit position.
                    hitPosition = character.WorldAABB.Center;
                }
            }

            IShield shield;
            if (shieldEntity == null || !ActiveShields.TryGetValue(shieldEntity, out shield))
            {
                return; // No shield on this entity.
            }

            // Check if the point of impact is within the shield's radius. This is crucial for explosions and large grids.
            if (hitPosition.HasValue && Vector3D.DistanceSquared(shieldEntity.WorldMatrix.Translation, hitPosition.Value) > (shield.Range * shield.Range))
            {
                return; // Damage occurred outside the shield's protection radius.
            }

            if (!shield.IsActive || shield.IsBroken)
            {
                return; // Shield is offline or broken.
            }

            float damageToAbsorb = Math.Min(shield.CurrentHp, info.Amount);
            if (damageToAbsorb <= 0) return;

            shield.TakeDamage(damageToAbsorb, hitPosition);
            info.Amount -= damageToAbsorb;

            // This is useful for debugging, but can be spammy in gameplay.
            // MyAPIGateway.Utilities.ShowNotification($"Shield absorbed {damageToAbsorb:F0} damage. HP: {shield.CurrentHp:F0}", 1000);
        }
    }
}