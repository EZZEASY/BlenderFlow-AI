namespace Loupedeck.BlenderFlowPlugin
{
    using System;
    using System.Threading.Tasks;

    // Zooms the 3D viewport by scaling region_3d.view_distance via
    // WebSocket — fully continuous, no keystroke steps. Button press
    // frames the selected object (Numpad . bare key, works reliably).
    public class ViewportZoomAdjustment : PluginDynamicAdjustment
    {
        // 5% per tick: positive diff zooms in (divide distance by 1.05),
        // negative diff zooms out.
        private const Single FactorPerTick = 1.05f;

        public ViewportZoomAdjustment()
            : base(
                displayName: "Viewport Zoom",
                description: "Smooth zoom of the 3D viewport; press to frame selected",
                groupName: "Viewport",
                hasReset: true)
        {
        }

        protected override void ApplyAdjustment(String actionParameter, Int32 diff)
        {
            if (diff == 0) { return; }

            var plugin = (BlenderFlowPlugin)this.Plugin;
            if (plugin?.BlenderConnection?.IsConnected != true)
            {
                return;
            }

            // Compound the factor so |diff| > 1 scales correctly: 2 ticks
            // out zooms 1.05² = 1.1025×, not 2.1×.
            Single factor = (Single)Math.Pow(FactorPerTick, diff);
            Task.Run(async () => await plugin.BlenderConnection.SendViewportZoomAsync(factor));
            this.AdjustmentValueChanged();
        }

        protected override void RunCommand(String actionParameter)
        {
            this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.Decimal);
            this.AdjustmentValueChanged();
        }

        protected override String GetAdjustmentValue(String actionParameter) => "Zoom";

        protected override BitmapImage GetAdjustmentImage(String actionParameter, PluginImageSize imageSize)
        {
            using var b = new BitmapBuilder(imageSize);
            b.Clear(BlenderTheme.PanelBg);
            Int32 w = imageSize.GetWidth();
            Int32 h = imageSize.GetHeight();
            Single cx = w * 0.44f, cy = h * 0.44f;
            Single lensR = Math.Min(w, h) * 0.24f;
            Single thick = Math.Max(2f, Math.Min(w, h) / 26f);
            BlenderIcons.Magnifier(b, cx, cy, lensR, "+", BlenderTheme.Orange, thick);
            return b.ToImage();
        }
    }
}
