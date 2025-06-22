using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath; // Added for Vector3D
using VRage.Utils; // Required for MyLog, MyStringId
using VRage.Game; // Required for MyDefinitionId

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
                MyLog.Default.WriteLine($"[EnergyShields] Damage handler registered successfully.");
            }
            else
            {
                MyLog.Default.WriteLine($"[EnergyShields] ERROR: DamageSystem not available when registering handler.");
            }
        }

        protected override void UnloadData()
        {
            _instance = null;
            // The damage handler is unregistered automatically by the game.
            ActiveShields.Clear();
            MyLog.Default.WriteLine($"[EnergyShields] ShieldDamageHandler unloaded and ActiveShields cleared.");
        }

        /// <summary>
        /// Registers a shield to an entity (grid or character).
        /// </summary>
        public static void RegisterShield(IMyEntity entity, IShield shield)
        {
            if (_instance == null || entity == null || shield == null)
            {
                MyLog.Default.WriteLine($"[EnergyShields] ERROR: Attempted to register shield with null instance, entity, or shield object. Entity: {(entity != null ? entity.DisplayName : "NULL")}, Shield: {(shield != null ? "Valid" : "NULL")}");
                return;
            }
            
            // Use the dictionary's indexer to add or update the shield for the entity.
            ActiveShields[entity] = shield;
            MyLog.Default.WriteLine($"[EnergyShields] Registered shield for entity: {entity.DisplayName} (ID: {entity.EntityId}). Total active shields: {ActiveShields.Count}");
        }

        /// <summary>
        /// Unregisters a shield from an entity.
        /// </summary>
        public static void UnregisterShield(IMyEntity entity)
        {
            if (_instance == null || entity == null)
            {
                MyLog.Default.WriteLine($"[EnergyShields] ERROR: Attempted to unregister shield with null instance or entity.");
                return;
            }

            if (ActiveShields.Remove(entity))
            {
                MyLog.Default.WriteLine($"[EnergyShields] Unregistered shield for entity: {entity.DisplayName} (ID: {entity.EntityId}). Total active shields: {ActiveShields.Count}");
            }
            else
            {
                MyLog.Default.WriteLine($"[EnergyShields] Attempted to unregister shield for entity {entity.DisplayName} (ID: {entity.EntityId}), but no shield was found.");
            }
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
        /// Attempts to find a more precise hit position based on attacker.
        /// </summary>
        private void OnDamageTaking(object target, ref MyDamageInformation info)
        {
            IMyEntity shieldCandidateEntity = null;
            IMySlimBlock block = null;
            IMyCharacter character = null;
            
            block = target as IMySlimBlock;
            if (block != null)
            {
                shieldCandidateEntity = block.CubeGrid;
                MyLog.Default.WriteLine($"[EnergyShields] OnDamageTaking: Target is block: {block.BlockDefinition.DisplayNameText} ({block.BlockDefinition.Id.SubtypeName}). Shield candidate: {shieldCandidateEntity?.DisplayName}");
            }
            else
            {
                character = target as IMyCharacter;
                if (character != null)
                {
                    shieldCandidateEntity = character;
                    MyLog.Default.WriteLine($"[EnergyShields] OnDamageTaking: Target is character: {character.DisplayName}. Shield candidate: {shieldCandidateEntity?.DisplayName}");
                }
                else
                {
                    MyLog.Default.WriteLine($"[EnergyShields] OnDamageTaking: Target is neither block nor character. Type: {target?.GetType().Name}. Damage passes through.");
                    return;
                }
            }

            IShield shield;
            if (shieldCandidateEntity == null)
            {
                MyLog.Default.WriteLine($"[EnergyShields] OnDamageTaking: No valid shield candidate entity. Damage passes through.");
                return;
            }

            if (!ActiveShields.TryGetValue(shieldCandidateEntity, out shield))
            {
                MyLog.Default.WriteLine($"[EnergyShields] OnDamageTaking: No shield registered for entity: {shieldCandidateEntity.DisplayName} (ID: {shieldCandidateEntity.EntityId}). Damage passes through.");
                return; 
            }

            MyLog.Default.WriteLine($"[EnergyShields] OnDamageTaking: Shield found for {shieldCandidateEntity.DisplayName}. Incoming Damage: {info.Amount}. Damage Type: {info.Type.String}. Attacker ID: {info.AttackerId}");

            IMyEntity attackerEntity = null;
            Vector3D? attackerPosition = null;
            float shieldRangeSquared = shield.Range * shield.Range;
            Vector3D shieldCenter = shieldCandidateEntity.WorldMatrix.Translation;

            if (info.AttackerId != 0) 
            {
                attackerEntity = MyAPIGateway.Entities.GetEntityById(info.AttackerId);
                if (attackerEntity != null && !attackerEntity.MarkedForClose)
                {
                    attackerPosition = attackerEntity.WorldMatrix.Translation;
                    MyLog.Default.WriteLine($"[EnergyShields] OnDamageTaking: Attacker position: {attackerPosition.Value}");
                }
                else
                {
                    MyLog.Default.WriteLine($"[EnergyShields] OnDamageTaking: Attacker entity not found or marked for close. Cannot determine attacker position.");
                }
            }
            else
            {
                MyLog.Default.WriteLine($"[EnergyShields] OnDamageTaking: No Attacker ID provided. Cannot determine attacker position.");
            }

            Vector3D? hitPosition = null; // This is primarily for visual effects and as a fallback for unknown attacker spatial checks.

            // Derive hitPosition for visuals/fallback logic
            if (attackerPosition.HasValue) 
            {
                Vector3D directionFromShieldToAttacker = Vector3D.Normalize(attackerPosition.Value - shieldCenter);
                // Hit position is calculated to be on the surface of the shield for visual consistency
                hitPosition = shieldCenter + directionFromShieldToAttacker * shield.Range;
                MyLog.Default.WriteLine($"[EnergyShields] OnDamageTaking: Hit position (for visuals/fallback) derived from attacker: {hitPosition.Value}");
            }
            else // Fallback for no attackerPosition (info.AttackerId == 0 or attacker not found)
            {
                if (block != null)
                {
                    Vector3D worldPos;
                    block.ComputeWorldCenter(out worldPos);
                    hitPosition = worldPos;
                    MyLog.Default.WriteLine($"[EnergyShields] OnDamageTaking: Hit position fallback to block center: {hitPosition.Value}");
                }
                else if (character != null)
                {
                    hitPosition = character.WorldAABB.Center;
                    MyLog.Default.WriteLine($"[EnergyShields] OnDamageTaking: Hit position fallback to character AABB center: {hitPosition.Value}");
                }
                else
                {
                    hitPosition = shieldCenter;
                    MyLog.Default.WriteLine($"[EnergyShields] OnDamageTaking: Hit position fallback to shield entity translation: {hitPosition.Value}");
                }
            }

            // --- Core Damage Mitigation Logic based on new requirement ---
            bool shouldDamagePassThrough = false;

            if (attackerPosition.HasValue)
            {
                double attackerDistanceSquared = Vector3D.DistanceSquared(shieldCenter, attackerPosition.Value);
                MyLog.Default.WriteLine($"[EnergyShields] OnDamageTaking: Attacker Distance Squared: {attackerDistanceSquared}, Shield Range Squared: {shieldRangeSquared}");

                // New logic: If attacker is INSIDE the shield, damage passes through.
                if (attackerDistanceSquared < shieldRangeSquared)
                {
                    shouldDamagePassThrough = true;
                    MyLog.Default.WriteLine($"[EnergyShields] OnDamageTaking: Attacker is definitively inside shield range. Damage will pass through.");
                }
                else
                {
                    // Attacker is outside or on the boundary, damage should be absorbed.
                    MyLog.Default.WriteLine($"[EnergyShields] OnDamageTaking: Attacker is outside or at shield range. Damage will be absorbed.");
                }
            }
            else // Attacker position is unknown (info.AttackerId was 0 or entity not found)
            {
                // In this case, we fall back to a geometric check based on the target and derived hitPosition.
                // If hitPosition is strictly outside the shield, damage passes through.
                if (hitPosition.HasValue && Vector3D.DistanceSquared(shieldCenter, hitPosition.Value) > shieldRangeSquared)
                {
                    shouldDamagePassThrough = true;
                    MyLog.Default.WriteLine($"[EnergyShields] OnDamageTaking: Unknown attacker, derived hit position strictly outside. Damage will pass through.");
                }
                else if (!hitPosition.HasValue) 
                {
                    // This scenario should be rare with current fallbacks, but added for robustness.
                    shouldDamagePassThrough = true;
                    MyLog.Default.WriteLine($"[EnergyShields] OnDamageTaking: Unknown attacker, no valid hit position. Damage will pass through.");
                }
                else
                {
                    MyLog.Default.WriteLine($"[EnergyShields] OnDamageTaking: Unknown attacker, derived hit position within or at shield range. Damage will be absorbed.");
                }
            }

            if (shouldDamagePassThrough)
            {
                MyLog.Default.WriteLine($"[EnergyShields] OnDamageTaking: Final decision: Damage passes through.");
                return;
            }

            MyLog.Default.WriteLine($"[EnergyShields] OnDamageTaking: Final decision: Damage will be absorbed by shield.");

            // Always trigger visual impact if the shield is active (or if it's a zero-damage hit in creative-like scenarios)
            // and a valid hit position exists. This ensures visual feedback even if shield is full or damage is zero.
            if (shield.IsActive && hitPosition.HasValue) // Only trigger if shield is active and we have a valid hit position
            {
                MyLog.Default.WriteLine($"[EnergyShields] OnDamageTaking: Shield is active. Triggering visual impact.");
                shield.TriggerVisualImpact(hitPosition.Value);
            }
            else if (info.Amount == 0 && hitPosition.HasValue) // Creative mode scenario, still trigger visual
            {
                MyLog.Default.WriteLine($"[EnergyShields] OnDamageTaking: Info.Amount is 0. Triggering visual impact (creative mode likely).");
                shield.TriggerVisualImpact(hitPosition.Value);
            }

            MyLog.Default.WriteLine($"[EnergyShields] OnDamageTaking: Before absorption check. Active: {shield.IsActive}, Broken: {shield.IsBroken}, Incoming: {info.Amount}, Current HP: {shield.CurrentHp}");

            if (shield.IsActive && !shield.IsBroken && info.Amount > 0)
            {
                float originalIncomingDamage = info.Amount; 

                MyLog.Default.WriteLine($"[EnergyShields] OnDamageTaking: Shield attempting to absorb damage. Original Damage: {originalIncomingDamage}, Shield HP before: {shield.CurrentHp}");

                if (hitPosition.HasValue)
                {
                    shield.TakeDamageAtPosition(originalIncomingDamage, hitPosition.Value);
                }
                else
                {
                    shield.TakeDamage(originalIncomingDamage);
                }
                
                MyLog.Default.WriteLine($"[EnergyShields] OnDamageTaking: Damage absorbed by shield. Shield HP after: {shield.CurrentHp}. Original Damage: {originalIncomingDamage}");

                info.Amount = 0f;
                MyLog.Default.WriteLine($"[EnergyShields] OnDamageTaking: info.Amount set to 0. Damage fully handled by shield. Returning.");
                return; 
            }
            else
            {
                MyLog.Default.WriteLine($"[EnergyShields] OnDamageTaking: Shield not absorbing damage. Conditions not met. Active: {shield.IsActive}, Broken: {shield.IsBroken}, Incoming > 0: {info.Amount > 0}. Damage passes through.");
            }
        }
    }
}