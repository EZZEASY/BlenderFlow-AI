namespace Loupedeck.BlenderFlowPlugin
{
    using System;

    public class BrushSizeAdjustment : PluginDynamicAdjustment
    {
        public BrushSizeAdjustment()
            : base(
                displayName: "Brush Size",
                description: "Adjust brush size in Sculpt mode",
                groupName: "Sculpting",
                hasReset: false)
        {
        }

        protected override void ApplyAdjustment(String actionParameter, Int32 diff)
        {
            if (diff == 0)
            {
                return;
            }

            if (diff > 0)
            {
                // ] = increase brush size
                this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.Oem6); // ]
            }
            else
            {
                // [ = decrease brush size
                this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.Oem4); // [
            }

            this.AdjustmentValueChanged();
        }

        protected override void RunCommand(String actionParameter)
        {
            // No reset action for brush size
        }

        protected override String GetAdjustmentValue(String actionParameter) => "Brush";
    }
}
