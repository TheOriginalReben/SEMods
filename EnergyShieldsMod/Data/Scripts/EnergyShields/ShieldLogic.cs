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
        public const double ShieldHpPerMeter = 100;
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
        bool IsActive { get; set; }

        event Action<Vector3D> OnShieldHit;

        /// <summary>
        /// Reduces shield HP by a given amount.
        /// </summary>
        /// <param name="hitPosition">The world position of the impact, if available.</param>
        void TakeDamage(float amount, Vector3D? hitPosition = null);

        /// <summary>
        /// Updates the shield's state, primarily for regeneration.
        /// </summary>
        /// <param name="deltaTime">Time elapsed since the last update in seconds.</param>
        void Update(float deltaTime);
    }

    /// <summary>
    /// An abstract base class providing common shield functionality,
    /// including regeneration logic and a recharge delay when broken.
    /// </summary>
    public abstract class ShieldBase : IShield
    {
        public abstract ShieldType Type { get; }
        public float MaxHp { get; protected set; }
        public float CurrentHp { get; protected set; }
        public float Range { get; protected set; }
        public float RegenRatePerSecond { get; protected set; }
        public float PowerUsageWatts { get; protected set; }
        public bool IsActive { get; set; } = true;

        public bool IsBroken => CurrentHp <= 0;
        protected float TimeSinceBroken = 0f;
        protected float RechargeDelaySeconds = 0f;

        public event Action<Vector3D> OnShieldHit;

        protected ShieldBase(float range)
        {
            // Ensure the range is within the defined min/max values.
            this.Range = MathHelper.Clamp(range, ShieldConstants.MinRange, ShieldConstants.MaxRange);
        }

        public virtual void TakeDamage(float amount, Vector3D? hitPosition = null)
        {
            if (!IsActive || amount <= 0) return;

            CurrentHp -= amount;
            if (CurrentHp <= 0)
            {
                CurrentHp = 0;
                TimeSinceBroken = 0f; // Reset the broken timer
            }

            if (hitPosition.HasValue)
            {
                OnShieldHit?.Invoke(hitPosition.Value);
            }
        }

        public virtual void Update(float deltaTime)
        {
            if (!IsActive) return;

            // If the shield is broken, wait for the recharge delay.
            if (IsBroken)
            {
                TimeSinceBroken += deltaTime;
                if (TimeSinceBroken < RechargeDelaySeconds)
                {
                    return; // Still waiting for recharge delay.
                }
            }
            
            // Regenerate shield if it's not at full health.
            if (CurrentHp < MaxHp)
            {
                CurrentHp = Math.Min(MaxHp, CurrentHp + RegenRatePerSecond * deltaTime);
            }
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
            MaxHp = (float)(ShieldConstants.ShieldHpPerMeter * range * ShieldConstants.GenericShieldHpMultiplier);
            CurrentHp = MaxHp;
            RechargeDelaySeconds = 15f; // Long delay when broken.
            RegenRatePerSecond = MaxHp * 0.02f; // Regenerates 2% of Max HP per second.
            PowerUsageWatts = (float)ShieldConstants.GenericShieldWattUsage;
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
            MaxHp = (float)(ShieldConstants.ShieldHpPerMeter * range * ShieldConstants.ModularShieldHpMultiplier);
            CurrentHp = MaxHp;
            RechargeDelaySeconds = 3f; // Short delay when broken.
            RegenRatePerSecond = MaxHp * 0.15f; // Regenerates 15% of Max HP per second.
            // Higher power usage to support faster regeneration.
            PowerUsageWatts = (float)ShieldConstants.GenericShieldWattUsage * 1.2f;
        }
    }
}