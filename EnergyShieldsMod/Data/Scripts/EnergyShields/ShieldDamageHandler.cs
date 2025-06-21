using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath; // Added for Vector3D

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
        // Changed to internal for accessibility from other mod classes within the same assembly.
        internal static readonly Dictionary<IMyEntity, IShield> ActiveShields = new Dictionary<IMyEntity, IShield>();

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
        /// Checks if an entity currently has an active shield registered.
        /// </summary>
        /// <param name="entity">The entity to check.</param>
        /// <returns>True if a shield is registered for the entity, false otherwise.</returns>
        public static bool IsShieldRegistered(IMyEntity entity)
        {
            return ActiveShields.ContainsKey(entity);
        }

        /// <summary>
        /// This method is called by the game before any damage is applied.
        /// It intercepts damage and redirects it to the shield if one is active.
        /// </summary>
        private void OnDamageTaking(object target, ref MyDamageInformation info)
        {
            IMyEntity shieldCandidateEntity = null;
            Vector3D? hitPosition = null;
            
            // Determine the actual entity that owns the shield
            var block = target as IMySlimBlock;
            if (block != null)
            {
                shieldCandidateEntity = block.CubeGrid;
                // For blocks, calculate the world center as the approximate hit position
                Vector3D worldPos;
                block.ComputeWorldCenter(out worldPos);
                hitPosition = worldPos;
            }
            else
            {
                var character = target as IMyCharacter;
                if (character != null)
                {
                    shieldCandidateEntity = character;
                    // For characters, use their bounding box center as the hit position
                    hitPosition = character.WorldAABB.Center;
                }
            }

            // If no valid shield candidate entity, or no shield registered for it, return.
            IShield shield;
            if (shieldCandidateEntity == null || !ActiveShields.TryGetValue(shieldCandidateEntity, out shield))
            {
                return; // No shield on this entity, allow damage to pass through.
            }

            // Important: Check if the point of impact is within the shield's radius.
            // This is crucial for explosions and large grids.
            if (hitPosition.HasValue && Vector3D.DistanceSquared(shieldCandidateEntity.WorldMatrix.Translation, hitPosition.Value) > (shield.Range * shield.Range))
            {
                return; // Damage occurred outside the shield's protection radius, allow damage to pass through.
            }

            // --- Core Logic for Damage vs. Visuals in Creative Mode ---

            // Always trigger visual impact if the shield is active and a hit position is valid.
            // This ensures visual feedback even if shield is full or damage is zero (e.g., creative mode).
            if (hitPosition.HasValue && shield.IsActive)
            {
                shield.TriggerVisualImpact(hitPosition.Value);
            }

            // Only proceed with actual damage absorption if the shield is active AND not broken AND there's actual damage.
            if (shield.IsActive && !shield.IsBroken && info.Amount > 0)
            {
                float damageToAbsorb = Math.Min(shield.CurrentHp, info.Amount);
                
                if (damageToAbsorb > 0)
                {
                    // Call TakeDamage which only handles HP reduction and resets recharge delay.
                    shield.TakeDamage(damageToAbsorb); 
                    info.Amount -= damageToAbsorb; // Reduce original damage
                }
            }
            // If shield is inactive, broken, or info.Amount is 0 (and not a creative mode-only visual trigger),
            // damage passes through without affecting the shield's HP.
        }
    }
}
