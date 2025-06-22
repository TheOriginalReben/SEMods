using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using Sandbox.Game.Entities.Character.Components; // Ensure this is present for SuitEnergyLevel
using VRageMath; // Added for Vector3D

namespace EnergyShields
{
    /// <summary>
    /// Manages the personal shield for the player character.
    /// </summary>
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class PlayerShieldLogic : MySessionComponentBase
    {
        private IShield _playerShield;
        private IMyCharacter _playerCharacter;
        private bool _isShieldManaged = false;
        private ShieldRenderer _renderer;

        // Adjustable constant for the player shield's default range
        // Changed back to 2 meters as requested.
        private const float DefaultPlayerShieldRange = 2f; 

        /// <summary>
        /// Tears down the shield and unregisters it.
        /// </summary>
        private void TeardownShield()
        {
            if (!_isShieldManaged) return;

            if (_playerShield != null)
            {
                _playerShield.OnShieldHit -= OnShieldHit;
                _playerShield = null;
            }
            if (_playerCharacter != null)
            {
                ShieldDamageHandler.UnregisterShield(_playerCharacter);
            }
            _renderer = null;
            _isShieldManaged = false;
        }

        public override void UpdateBeforeSimulation()
        {
            var currentCharacter = MyAPIGateway.Session?.Player?.Character;

            // Handle character changes (spawn, despawn, death)
            if (currentCharacter != _playerCharacter)
            {
                TeardownShield();
                _playerCharacter = currentCharacter;

                if (_playerCharacter != null)
                {
                    // Optionally show a notification when character changes and shield is being set up
                    // MyAPIGateway.Utilities.ShowNotification("Character changed. Setting up shield.", 1500, "Blue");
                }
            }

            // If the character exists and the shield isn't managed yet, create it.
            if (!_isShieldManaged && _playerCharacter != null)
            {
                // The player shield will always be a ModularShield for fast recovery.
                _playerShield = new ModularShield(DefaultPlayerShieldRange, _playerCharacter); 
                _playerShield.OnShieldHit += OnShieldHit;
                ShieldDamageHandler.RegisterShield(_playerCharacter, _playerShield);
                _renderer = new ShieldRenderer(_playerCharacter, _playerShield);
                _isShieldManaged = true;
                MyAPIGateway.Utilities.ShowNotification("Personal shield online.", 1500, "Green");
            }
            
            // Update shield logic and consume suit energy.
            if (_isShieldManaged && _playerShield != null)
            {
                // The ModAPI does not provide a way to directly drain suit energy.
                // Instead, we just check if the suit has any power and deactivate the shield if it runs out.
                // It seems SuitEnergyLevel is part of Sandbox.Game.Entities.Character.Components, ensure it's accessible.
                _playerShield.IsActive = _playerCharacter.SuitEnergyLevel > 0;
                
                if (_playerShield.IsActive)
                {
                    _playerShield.Update(1f / 60f); // Update shield state (recharge, etc.) assuming 60 FPS
                }
            }

            // Always update the renderer to handle lingering effects (impacts, ripples)
            _renderer?.Update();
        }

        /// <summary>
        /// Event handler for when the shield takes a hit.
        /// </summary>
        /// <param name="position">The world position of the impact.</param>
        private void OnShieldHit(Vector3D position) // Ensure correct delegate matches Action<Vector3D>
        {
            _renderer?.CreateImpactEffect(position);
        }

        protected override void UnloadData()
        {
            TeardownShield();
            _playerCharacter = null;
            base.UnloadData();
        }
    }
}
