using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace EnergyShields
{
    /// <summary>
    /// Handles creation of terminal controls for the shield generator block.
    /// </summary>
    public static class TerminalControls
    {
        private static bool _controlsCreated = false;

        public static void CreateControls()
        {
            if (_controlsCreated) return;
            _controlsCreated = true;

            // Slider for Shield Range
            var rangeSlider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyUpgradeModule>("ShieldRange");
            rangeSlider.Title = MyStringId.GetOrCompute("Shield Range");
            rangeSlider.Tooltip = MyStringId.GetOrCompute("Sets the maximum radius of the shield sphere.");
            rangeSlider.SetLimits(ShieldConstants.MinRange, ShieldConstants.MaxRange);
            rangeSlider.Getter = (block) => GetLogic(block)?.RangeSetting ?? 0;
            rangeSlider.Setter = (block, value) =>
            {
                var logic = GetLogic(block);
                if (logic != null) logic.RangeSetting = value;
            };
            rangeSlider.Writer = (block, sb) => sb.Append($"{GetLogic(block)?.RangeSetting ?? 0:F1} m");
            rangeSlider.Visible = (block) => GetLogic(block) != null;
            MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(rangeSlider);

            // Listbox for Shield Type
            var typeList = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlListbox, IMyUpgradeModule>("ShieldType");
            typeList.Title = MyStringId.GetOrCompute("Shield Type");
            typeList.Tooltip = MyStringId.GetOrCompute("Selects the type of shield to project.");
            typeList.ListContent = (block, list, selected) =>
            {
                var generic = new MyTerminalControlListBoxItem(MyStringId.GetOrCompute("Generic"), MyStringId.GetOrCompute("High HP, slow recovery."), (int)ShieldType.Generic);
                var modular = new MyTerminalControlListBoxItem(MyStringId.GetOrCompute("Modular"), MyStringId.GetOrCompute("Low HP, fast recovery."), (int)ShieldType.Modular);
                list.Add(generic);
                list.Add(modular);

                var logic = GetLogic(block);
                if (logic != null)
                {
                    switch (logic.ShieldTypeSetting)
                    {
                        case ShieldType.Generic:
                            selected.Add(generic);
                            break;
                        case ShieldType.Modular:
                            selected.Add(modular);
                            break;
                    }
                }
            };
            typeList.ItemSelected = (block, selected) =>
            {
                var logic = GetLogic(block);
                if (logic != null && selected.Count > 0)
                {
                    logic.ShieldTypeSetting = (ShieldType)(int)selected[0].UserData;
                }
            };
            typeList.Visible = (block) => GetLogic(block) != null;
            MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(typeList);
        }

        private static ShieldGeneratorLogic GetLogic(IMyTerminalBlock block)
        {
            if (block == null) return null;
            return block.GameLogic?.GetAs<ShieldGeneratorLogic>();
        }
    }
}