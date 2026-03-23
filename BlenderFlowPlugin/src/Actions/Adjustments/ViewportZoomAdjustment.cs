namespace Loupedeck.BlenderFlowPlugin
{
    using System;

    public class ViewportZoomAdjustment : PluginDynamicAdjustment
    {
        private Int32 _zoomLevel = 50;

        public ViewportZoomAdjustment()
            : base(
                displayName: "Viewport Zoom",
                description: "Zoom in/out Blender viewport",
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

            this._zoomLevel = Math.Clamp(this._zoomLevel + diff * 5, 0, 100);

            if (diff > 0)
            {
                this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.Add);
            }
            else
            {
                this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.Subtract);
            }

            this.AdjustmentValueChanged();
        }

        protected override void RunCommand(String actionParameter)
        {
            this._zoomLevel = 50;
            // Numpad . = focus on selected object
            this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.Decimal);
            this.AdjustmentValueChanged();
        }

        protected override String GetAdjustmentValue(String actionParameter) => "Zoom";
    }
}
