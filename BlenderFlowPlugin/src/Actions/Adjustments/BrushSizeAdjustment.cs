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

        protected override BitmapImage GetAdjustmentImage(String actionParameter, PluginImageSize imageSize)
        {
            using var b = new BitmapBuilder(imageSize);
            b.Clear(BlenderTheme.PanelBg);
            Int32 w = imageSize.GetWidth();
            Int32 h = imageSize.GetHeight();
            Single cx = w / 2f, cy = h * 0.46f;
            Single maxR = Math.Min(w, h) * 0.32f;
            BlenderIcons.BrushRings(b, cx, cy, maxR, BlenderTheme.SculptRed);
            b.DrawText("Brush", 0, (Int32)(h * 0.78f), w, (Int32)(h * 0.20f),
                BlenderTheme.Icon, 0, 0, 0, null);
            return b.ToImage();
        }
    }
}
