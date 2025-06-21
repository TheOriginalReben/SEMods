using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.GameSystems;
using Sandbox.ModAPI;
using VRage.Game; // Required for MyDefinitionId and MyObjectBuilder_GasProperties
using VRage.Game.Components; // For MyResourceSinkComponent, MyEntityUpdateEnum
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using System.Text;
using VRage.Game.ModAPI.Ingame.Utilities; // Required for MyIni
using VRage.Utils; // For MyStringHash, MyStringId
using VRageMath; // For Vector3D, MathHelper, needed for RangeSetting clamp and OnShieldHit
using Sandbox.Common.ObjectBuilders.Definitions; // This directive is required for MyObjectBuilder_GasProperties
using Sandbox.Game.Entities; // Required for MyCubeBlock

namespace EnergyShields
{
    /// <summary>
    /// Game logic component for the shield generator block.
    /// This must be attached to a block in your SBC file.
    /// </summary>
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_UpgradeModule), false, "LargeShieldGenerator", "SmallShieldGenerator")]
    public class ShieldGeneratorLogic : MyGameLogicComponent
    {
        private IShield _shield;
        private MyResourceSinkComponent _powerConsumer;
        private IMyTerminalBlock _block;
        private IMyCubeGrid _grid;
        private ShieldRenderer _renderer;
        private MyIni _ini = new MyIni(); // Used to save and load settings from CustomData

        // Properties for terminal controls
        public float RangeSetting
        {
            get { return _ini.Get("Shield", "Range").ToSingle(ShieldConstants.MaxRange); }
            set
            {
                // Ensure value is within defined min/max range
                value = MathHelper.Clamp(value, ShieldConstants.MinRange, ShieldConstants.MaxRange);
                _ini.Set("Shield", "Range", value);
                _block.CustomData = _ini.ToString(); // Save changes to CustomData

                // Recreate shield with new range if shield object exists
                if (_shield != null)
                {
                    _shield.Recreate(value);
                }
                UpdateDetailedInfo(_block, new StringBuilder()); // Force update terminal display
                UpdatePowerUsage(); // Update power consumption based on new shield properties
            }
        }

        public ShieldType ShieldTypeSetting
        {
            get { return (ShieldType)_ini.Get("Shield", "Type").ToInt32((int)ShieldType.Generic); }
            set
            {
                _ini.Set("Shield", "Type", (int)value);
                _block.CustomData = _ini.ToString(); // Save changes to CustomData
                RecreateShield(); // Recreate shield with new type
                UpdateDetailedInfo(_block, new StringBuilder()); // Force update terminal display
                UpdatePowerUsage(); // Update power consumption based on new shield properties
            }
        }

        public bool IsShieldActive
        {
            get { return _ini.Get("Shield", "IsActive").ToBoolean(true); }
            set
            {
                _ini.Set("Shield", "IsActive", value);
                _block.CustomData = _ini.ToString(); // Save changes to CustomData
                if (_shield != null)
                {
                    _shield.IsActive = value; // Directly set shield's active state
                }
                UpdateDetailedInfo(_block, new StringBuilder()); // Force update terminal display
                UpdatePowerUsage(); // Update power consumption based on new shield properties
            }
        }

        /// <summary>
        /// Called when the entity is inited. Setup components and load settings here.
        /// </summary>
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            _block = Entity as IMyTerminalBlock;
            if (_block == null) return;

            _grid = _block.CubeGrid;

            // Load settings from CustomData first. Use TryParse for MyIni.
            if (!string.IsNullOrEmpty(_block.CustomData))
            {
                _ini.TryParse(_block.CustomData);
            }

            // Ensure default values are set if not present in CustomData.
            // Accessing properties directly here will also trigger their setters if values change.
            float initialRange = RangeSetting; 
            ShieldType initialType = ShieldTypeSetting; 
            bool initialIsActive = IsShieldActive;

            // Setup resource sink for power consumption
            _powerConsumer = new MyResourceSinkComponent();
            // MyResourceSinkInfo defines the sink type, default input, and a callback.
            // Use the correct MyDefinitionId for electricity.
            _powerConsumer.Init(
                MyStringHash.GetOrCompute("ElectricPower"), // Resource type, now MyStringHash
                new MyResourceSinkInfo // Sink info
                {
                    ResourceTypeId = MyDefinitionId.Parse("MyObjectBuilder_GasProperties/Electricity"),
                    MaxRequiredInput = (float)ShieldConstants.GenericShieldWattUsage, // Max power it can draw
                    RequiredInputFunc = UpdatePowerUsage // Callback for current required input
                },
                (MyCubeBlock)_block // The block associated with this sink, cast to MyCubeBlock
            );
            
            // After adding, ensure the sink is registered with the entity's components
            Entity.Components.Add(_powerConsumer);


            RecreateShield(); // Create the initial shield object
            _powerConsumer.Update(); // Force initial power update

            // Register for block's custom info update to display shield status
            _block.AppendingCustomInfo += UpdateDetailedInfo;
            // Register to receive shield hit events for visual effects
            _shield.OnShieldHit += OnShieldHit; // Ensure correct delegate matches Action<Vector3D>

            // Ensure block receives updates by setting NeedsUpdate.
            // Corrected: Using MyEntityUpdateEnum.BeforeSimulation (PascalCase)
            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME; 
        }

        /// <summary>
        /// Called every simulation tick. Update shield logic here.
        /// </summary>
        public override void UpdateBeforeSimulation()
        {
            base.UpdateBeforeSimulation();

            if (_shield == null || _block.Closed || !_block.IsFunctional)
            {
                // If shield is null or block is closed/non-functional, ensure it's unregistered
                if (_grid != null && _shield != null)
                {
                    ShieldDamageHandler.UnregisterShield(_grid);
                }
                // Also ensure power consumer is off
                if (_powerConsumer != null)
                {
                    _powerConsumer.SetRequiredInputByType(MyResourceDistributorComponent.ElectricityId, 0f);
                }
                return;
            }

            // Ensure shield is registered if it's functional and active state might have changed externally
            if (_block.IsFunctional && IsShieldActive && !ShieldDamageHandler.IsShieldRegistered(_grid)) // Using new helper method
            {
                ShieldDamageHandler.RegisterShield(_grid, _shield);
            }
            else if ((!_block.IsFunctional || !IsShieldActive) && ShieldDamageHandler.IsShieldRegistered(_grid)) // Using new helper method
            {
                ShieldDamageHandler.UnregisterShield(_grid);
            }

            // Update shield's internal state (regeneration, etc.)
            _shield.Update(1f / 60f); // Assuming 60 FPS

            // Update renderer for visual effects
            _renderer?.Update();

            // Force update power consumption
            UpdatePowerUsage();
        }

        /// <summary>
        /// Recreates the IShield instance based on current settings (range, type).
        /// </summary>
        private void RecreateShield()
        {
            // Unregister old shield if it exists
            if (_shield != null && _grid != null)
            {
                ShieldDamageHandler.UnregisterShield(_grid);
                _shield.OnShieldHit -= OnShieldHit; // Unsubscribe from old shield's event
            }

            // Create new shield instance based on selected type
            switch (ShieldTypeSetting)
            {
                case ShieldType.Modular:
                    _shield = new ModularShield(RangeSetting);
                    break;
                case ShieldType.Generic:
                default:
                    _shield = new GenericShield(RangeSetting);
                    break;
            }

            // Register new shield and renderer
            if (_grid != null && _shield != null)
            {
                ShieldDamageHandler.RegisterShield(_grid, _shield);
                _shield.OnShieldHit += OnShieldHit; // Subscribe to new shield's event
                _renderer = new ShieldRenderer(_grid, _shield);
                _shield.IsActive = IsShieldActive; // Set active state based on terminal setting
            }
        }

        /// <summary>
        /// Handles shield hit events for visual effects.
        /// </summary>
        /// <param name="position">The world position of the impact.</param>
        private void OnShieldHit(Vector3D position) // Signature matches Action<Vector3D>
        {
            _renderer?.CreateImpactEffect(position);
        }

        /// <summary>
        /// Updates the custom info displayed in the terminal.
        /// </summary>
        private void UpdateDetailedInfo(IMyTerminalBlock block, StringBuilder sb)
        {
            if (_shield == null)
            {
                sb.AppendLine("Shield: Offline");
                return;
            }

            sb.AppendLine($"Shield Type: {_shield.Type}");
            sb.AppendLine($"Status: {(_shield.IsActive ? (_shield.IsBroken ? "Broken" : "Online") : "Disabled")}");
            sb.AppendLine($"HP: {_shield.CurrentHp:F0}/{_shield.MaxHp:F0}");
            sb.AppendLine($"Range: {_shield.Range:F1} m");
            // RechargeDelaySeconds is part of ShieldBase, accessible via IShield
            sb.AppendLine($"Recharge Delay: {_shield.RechargeDelaySeconds:F1} s"); // Corrected: Access through _shield

            // The PowerUsageWatts property holds the value in Watts. Convert to kW or MW.
            float powerInWatts = _shield.PowerUsageWatts;
            if (powerInWatts < 1000)
            {
                sb.AppendLine($"Power Usage: {powerInWatts:F2} W");
            }
            else if (powerInWatts < 1000000)
            {
                sb.AppendLine($"Power Usage: {powerInWatts / 1000:F2} kW");
            }
            else
            {
                sb.AppendLine($"Power Usage: {powerInWatts / 1000000:F2} MW");
            }
        }

        /// <summary>
        /// Updates the power consumption of the shield generator.
        /// This method is called by the MyResourceSinkComponent as a callback.
        /// </summary>
        private float UpdatePowerUsage() // Return float as required by MyResourceSinkInfo.RequiredInputFunc
        {
            if (_powerConsumer != null && _shield != null)
            {
                float requiredPower = _shield.IsActive && !_shield.IsBroken ? _shield.PowerUsageWatts : 0f;
                // For RequiredInputFunc, you return the value, not set it directly via SetRequiredInputByType.
                return requiredPower;
            }
            return 0f; // Return 0 if shield or power consumer is not ready
        }

        public override void OnRemovedFromScene()
        {
            // Unregister the shield when the block is removed.
            if (_grid != null && _shield != null)
            {
                ShieldDamageHandler.UnregisterShield(_grid);
            }
            if (_block != null) _block.AppendingCustomInfo -= UpdateDetailedInfo;
            if (_shield != null) _shield.OnShieldHit -= OnShieldHit;
            base.OnRemovedFromScene();
        }

        public override void Close()
        {
            // Clean up resources and unregister events
            if (_grid != null && _shield != null)
            {
                ShieldDamageHandler.UnregisterShield(_grid);
            }
            if (_block != null) _block.AppendingCustomInfo -= UpdateDetailedInfo;
            if (_shield != null) _shield.OnShieldHit -= OnShieldHit;
            base.Close();
        }
    }
}
