using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.GameSystems;
using Sandbox.ModAPI;
using VRage.Game; // Required for MyDefinitionId and MyObjectBuilder_GasProperties
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using System.Text;
using VRage.Game.Entity;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Utils;
using Sandbox.Common.ObjectBuilders.Definitions; // This directive is required for MyObjectBuilder_GasProperties

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
        private MyIni _ini = new MyIni();

        // Properties for terminal controls
        public float RangeSetting
        {
            get { return _ini.Get("Shield", "Range").ToSingle(ShieldConstants.MaxRange); }
            set
            {
                _ini.Set("Shield", "Range", value);
                _block.CustomData = _ini.ToString();
                RecreateShield();
            }
        }

        public ShieldType ShieldTypeSetting
        {
            get { return (ShieldType)_ini.Get("Shield", "Type").ToInt32((int)ShieldType.Generic); }
            set
            {
                _ini.Set("Shield", "Type", (int)value);
                _block.CustomData = _ini.ToString();
                RecreateShield();
            }
        }

        private void RecreateShield()
        {
            // Clean up old shield if it exists
            if (_shield != null)
            {
                _shield.OnShieldHit -= OnShieldHit;
            }

            // Create new shield based on settings
            float range = RangeSetting;
            // Explicitly cast to the interface to help the C# 6 compiler.
            _shield = ShieldTypeSetting == ShieldType.Generic ? (IShield)new GenericShield(range) : new ModularShield(range);
            _shield.OnShieldHit += OnShieldHit;

            ShieldDamageHandler.RegisterShield(_grid, _shield);
            _renderer = new ShieldRenderer(_grid, _shield);
            UpdatePowerUsage();
        }

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            _block = Entity as IMyTerminalBlock;
            if (_block == null) return;

            _grid = _block.CubeGrid;
            _powerConsumer = Entity.Components.Get<MyResourceSinkComponent>();

            // Load settings from CustomData
            MyIniParseResult result;
            if (!_ini.TryParse(_block.CustomData, out result) || string.IsNullOrWhiteSpace(_block.CustomData))
            {
                // If parsing fails or data is empty, initialize with defaults and save them.
                _ini.Set("Shield", "Range", 25f); // A sensible default
                _ini.Set("Shield", "Type", (int)ShieldType.Generic);
                _block.CustomData = _ini.ToString();
                // Re-parse to ensure the _ini object is in a consistent state
                _ini.TryParse(_block.CustomData, out result);
            }

            TerminalControls.CreateControls();
            RecreateShield();
            _block.AppendingCustomInfo += UpdateDetailedInfo;

            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;
        }

        public override void UpdateAfterSimulation()
        {
            if (_block == null || _block.MarkedForClose) return;

            // Renderer must be updated even if shield is inactive to fade out effects
            _renderer?.Update();

            if (_shield == null) return;

            bool isPoweredAndWorking = _powerConsumer != null && _powerConsumer.IsPowered && _block.IsWorking;
            _shield.IsActive = isPoweredAndWorking;

            if (isPoweredAndWorking)
            {
                // Update shield regeneration (1/60th of a second).
                _shield.Update(1f / 60f);
            }
        }

        private void OnShieldHit(VRageMath.Vector3D position)
        {
            _renderer?.CreateImpactEffect(position);
        }

        // This is the correct way to update the terminal info panel.
        private void UpdateDetailedInfo(IMyTerminalBlock block, StringBuilder sb)
        {
            if (_shield == null) return;
            sb.AppendLine("Shield Status: " + (_shield.IsActive ? "Online" : "Offline"));
            sb.AppendLine("Shield Type: " + _shield.Type);
            sb.AppendLine("Shield HP: " + $"{_shield.CurrentHp:F0} / {_shield.MaxHp:F0}");
            sb.AppendLine("Range: " + $"{_shield.Range}m");
            sb.AppendLine($"Regeneration: {_shield.RegenRatePerSecond:F0} HP/s");

            // The PowerUsageWatts property holds the value in MW. We format it for readability.
            float powerInMw = _shield.PowerUsageWatts;
            if (powerInMw < 1)
            {
                sb.AppendLine($"Power Usage: {powerInMw * 1000:F2} kW");
            }
            else
            {
                sb.AppendLine($"Power Usage: {powerInMw:F2} MW");
            }
        }

        private void UpdatePowerUsage()
        {
            if (_powerConsumer != null && _shield != null)
           {
                _powerConsumer.SetRequiredInputByType(MyResourceDistributorComponent.ElectricityId, _shield.PowerUsageWatts);
            }
        }

        public override void OnRemovedFromScene()
        {
            // Unregister the shield when the block is removed.
            if (_grid != null && _shield != null)
            {
                ShieldDamageHandler.UnregisterShield(_grid);
                if (_block != null) _block.AppendingCustomInfo -= UpdateDetailedInfo;
                _shield.OnShieldHit -= OnShieldHit;
            }
            base.OnRemovedFromScene();
        }

        public override void Close()
        {
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