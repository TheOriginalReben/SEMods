using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using Sandbox.Game.Entities.Character.Components;

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
            }

            if (_playerCharacter == null || _playerCharacter.MarkedForClose)
            {
                // If character is gone, ensure everything is torn down and exit.
                if (_isShieldManaged) TeardownShield();
                return;
            }

            // Disable shield when jetpack is active.
            var jetpack = _playerCharacter.Components.Get<MyCharacterJetpackComponent>();
            if (jetpack != null && jetpack.IsFlying)
            {
                if (_isShieldManaged)
                {
                    TeardownShield();
                    MyAPIGateway.Utilities.ShowNotification("Personal shield offline: Jetpack active.", 1500, "Red");
                }
                // No need to update renderer if shield is torn down.
                return;
            }

            // If the shield should be active but isn't, create it.
            if (!_isShieldManaged)
            {
                _playerShield = new ModularShield(2f); // Set a small, visible range for the player shield
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
                _playerShield.IsActive = _playerCharacter.SuitEnergyLevel > 0;
                if (_playerShield.IsActive)
                    _playerShield.Update(1f / 60f);
            }

            // Always update the renderer to handle lingering effects
            _renderer?.Update();
        }

        private void OnShieldHit(VRageMath.Vector3D position)
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