using System;
using VRageMath; // For MathHelper, you might need to add a reference to VRage.Math.dll

namespace EnergyShields
{
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
        float RechargeDelaySeconds { get; } // Added to interface for terminal display

        event Action<Vector3D> OnShieldHit; // Event for when the shield takes damage visuals

        /// <summary>
        /// Reduces shield HP by a given amount. Does NOT trigger visual effects.
        /// </summary>
        /// <param name="amount">The amount of damage to take.</param>
        void TakeDamage(float amount); // Removed hitPosition parameter

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
    }

    /// <summary>
    /// Base class for all shield types, providing common functionality.
    /// </summary>
    public abstract class ShieldBase : IShield
    {
        public abstract ShieldType Type { get; }
        public float MaxHp { get; protected set; }
        public float CurrentHp { get; protected set; }
        public float Range { get; private set; }
        public float RegenRatePerSecond { get; protected set; }
        public float PowerUsageWatts { get; protected set; }
        public bool IsBroken => CurrentHp <= 0;
        public bool IsActive { get; set; } = true; // Shield is active by default

        // RechargeDelaySeconds is now public and accessible via the interface
        public float RechargeDelaySeconds { get; protected set; }
        private float _timeSinceLastDamage;

        public event Action<Vector3D> OnShieldHit;

        public ShieldBase(float initialRange)
        {
            Range = initialRange;
            _timeSinceLastDamage = RechargeDelaySeconds; // Start with full recharge delay initially
        }

        /// <summary>
        /// Handles shield damage, reducing current HP. Does NOT trigger visual effects.
        /// </summary>
        /// <param name="amount">The amount of damage to apply.</param>
        public void TakeDamage(float amount) // Removed hitPosition parameter
        {
            if (!IsActive || IsBroken) return; // Cannot take damage if inactive or already broken

            CurrentHp -= amount;
            if (CurrentHp < 0) CurrentHp = 0; // Ensure HP doesn't go below zero

            _timeSinceLastDamage = 0; // Reset recharge delay
            // OnShieldHit is NOT invoked here. It's now handled by TriggerVisualImpact.
        }

        /// <summary>
        /// Triggers a visual impact effect on the shield without modifying HP or recharge state.
        /// </summary>
        /// <param name="hitPosition">The world position of the impact for visual effects.</param>
        public void TriggerVisualImpact(Vector3D hitPosition)
        {
            // Only trigger visual if the shield is considered active and able to show effects
            if (IsActive)
            {
                OnShieldHit?.Invoke(hitPosition);
            }
        }

        /// <summary>
        /// Updates the shield's state, including regeneration.
        /// </summary>
        /// <param name="deltaSeconds">Time elapsed since last update in seconds.</param>
        public void Update(float deltaSeconds)
        {
            if (!IsActive) return; // No update if shield is inactive

            if (IsBroken)
            {
                _timeSinceLastDamage += deltaSeconds;
                if (_timeSinceLastDamage >= RechargeDelaySeconds)
                {
                    // Start regenerating only after the delay
                    CurrentHp += RegenRatePerSecond * deltaSeconds;
                    if (CurrentHp > MaxHp) CurrentHp = MaxHp; // Cap HP at MaxHp
                }
            }
            else // Not broken, regular regeneration (if applicable, e.g., for Modular shield)
            {
                _timeSinceLastDamage = 0; // Keep delay at zero if not broken
                CurrentHp += RegenRatePerSecond * deltaSeconds;
                if (CurrentHp > MaxHp) CurrentHp = MaxHp;
            }
        }

        /// <summary>
        /// Recreates the shield with new properties based on the new range.
        /// This method needs to be implemented by derived classes to adjust MaxHp, RegenRate, etc.
        /// </summary>
        /// <param name="newRange">The new range for the shield.</param>
        public virtual void Recreate(float newRange)
        {
            float oldMaxHp = MaxHp; // Store old max HP
            Range = newRange; // Update range

            // Recalculate MaxHp for the specific shield type (will be handled by derived class's Recreate() after base.Recreate())
            // CurrentHp needs to be scaled proportionally to the new MaxHp if the shield isn't broken.
            if (!IsBroken && oldMaxHp > 0) // Only scale if it wasn't broken and had HP before
            {
                float healthRatio = CurrentHp / oldMaxHp;
                // MaxHp will be re-set by the derived class's override, CurrentHp will then be adjusted.
            }
            else if (IsBroken)
            {
                CurrentHp = 0; // If broken, remain broken and rely on regeneration.
            }

            // Reset time since last damage to enforce recharge delay if MaxHp changed significantly,
            // or if the shield became broken due to range reduction.
            _timeSinceLastDamage = 0;
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
        }

        /// <summary>
        /// Overrides Recreate to update GenericShield specific properties.
        /// </summary>
        /// <param name="newRange">The new range for the shield.</param>
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
    /// </summary>
    public class ModularShield : ShieldBase
    {
        public override ShieldType Type => ShieldType.Modular;

        public ModularShield(float range) : base(range)
        {
            // Initial setup using constants and range
            MaxHp = (float)(ShieldConstants.ShieldHpPerMeter * range * ShieldConstants.ModularShieldHpMultiplier);
            CurrentHp = MaxHp; // Start with full HP
            RechargeDelaySeconds = 3f; // Short delay when broken.
            RegenRatePerSecond = MaxHp * 0.15f; // Regenerates 15% of Max HP per second.
            // Higher power usage to support faster regeneration.
            PowerUsageWatts = (float)ShieldConstants.GenericShieldWattUsage * 1.2f;
        }

        /// <summary>
        /// Overrides Recreate to update ModularShield specific properties.
        /// </summary>
        /// <param name="newRange">The new range for the shield.</param>
        public override void Recreate(float newRange)
        {
            float oldMaxHp = MaxHp;
            base.Recreate(newRange); // Call base to update Range and possibly initial CurrentHp logic

            // Recalculate Modular Shield specific properties
            MaxHp = (float)(ShieldConstants.ShieldHpPerMeter * newRange * ShieldConstants.ModularShieldHpMultiplier);
            RegenRatePerSecond = MaxHp * 0.15f;
            PowerUsageWatts = (float)ShieldConstants.GenericShieldWattUsage * 1.2f;

            // Adjust CurrentHp proportionally if it was not broken before resizing
            if (oldMaxHp > 0 && CurrentHp > 0)
            {
                CurrentHp = (CurrentHp / oldMaxHp) * MaxHp;
            } else if (CurrentHp <= 0)
            {
                CurrentHp = 0;
            } else
            {
                CurrentHp = MaxHp;
            }
        }
    }
}
