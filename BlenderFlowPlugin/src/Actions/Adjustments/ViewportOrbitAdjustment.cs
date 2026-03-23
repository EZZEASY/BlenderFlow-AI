namespace Loupedeck.BlenderFlowPlugin
{
    using System;

    public class ViewportOrbitAdjustment : PluginDynamicAdjustment
    {
        public ViewportOrbitAdjustment()
            : base(
                displayName: "Viewport Orbit",
                description: "Rotate viewport left/right (turntable)",
                groupName: "Viewport",
                hasReset: true)
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
                // Numpad 6 = rotate right
                this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.NumPad6);
            }
            else
            {
                // Numpad 4 = rotate left
                this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.NumPad4);
            }

            this.AdjustmentValueChanged();
        }

        protected override void RunCommand(String actionParameter)
        {
            // Numpad 5 = toggle perspective/orthographic
            this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.NumPad5);
            this.AdjustmentValueChanged();
        }

        protected override String GetAdjustmentValue(String actionParameter) => "Orbit";
    }
}
