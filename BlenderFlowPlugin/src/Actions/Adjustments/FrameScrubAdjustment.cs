namespace Loupedeck.BlenderFlowPlugin
{
    using System;

    // Dial ticks step through the timeline one frame at a time (Blender's
    // native Left/Right arrow shortcuts). Button press toggles play/pause
    // via Spacebar.
    public class FrameScrubAdjustment : PluginDynamicAdjustment
    {
        public FrameScrubAdjustment()
            : base(
                displayName: "Frame Scrub",
                description: "Step through timeline one frame per tick; press to play/pause",
                groupName: "Animation",
                hasReset: true)
        {
        }

        protected override void ApplyAdjustment(String actionParameter, Int32 diff)
        {
            if (diff == 0) { return; }

            var key = diff > 0 ? VirtualKeyCode.ArrowRight : VirtualKeyCode.ArrowLeft;
            Int32 steps = Math.Abs(diff);
            for (Int32 i = 0; i < steps; i++)
            {
                this.Plugin.ClientApplication.SendKeyboardShortcut(key);
            }
            this.AdjustmentValueChanged();
        }

        protected override void RunCommand(String actionParameter)
        {
            this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.Space);
            this.AdjustmentValueChanged();
            PluginLog.Info("Frame Scrub: play/pause");
        }

        protected override String GetAdjustmentValue(String actionParameter) => "Frame";

        protected override BitmapImage GetAdjustmentImage(String actionParameter, PluginImageSize imageSize)
        {
            using var b = new BitmapBuilder(imageSize);
            b.Clear(BlenderTheme.PanelBg);
            Int32 w = imageSize.GetWidth();
            Int32 h = imageSize.GetHeight();
            Single cx = w / 2f, cy = h * 0.56f;
            Single stripW = Math.Min(w, h) * 0.70f;
            Single stripH = Math.Min(w, h) * 0.32f;
            Single thick = Math.Max(1.5f, Math.Min(w, h) / 36f);
            BlenderIcons.Filmstrip(b, cx, cy, stripW, stripH, BlenderTheme.Icon, thick);
            return b.ToImage();
        }
    }
}
