namespace Loupedeck.BlenderFlowPlugin
{
    using System;
    using System.Threading.Tasks;

    // Dial ticks adjust brush.strength via WebSocket (no OS-level shortcut
    // exists for this — Shift+F is a modal drag, not step-based). Button
    // press resets to 0.5 (Blender's default mid-strength).
    public class BrushStrengthAdjustment : PluginDynamicAdjustment
    {
        private const Single StepPerTick = 0.05f;
        private const Single ResetValue = 0.5f;

        public BrushStrengthAdjustment()
            : base(
                displayName: "Brush Strength",
                description: "Adjust brush strength (sculpt/paint)",
                groupName: "Sculpting",
                hasReset: true)
        {
        }

        protected override void ApplyAdjustment(String actionParameter, Int32 diff)
        {
            if (diff == 0) { return; }

            var plugin = (BlenderFlowPlugin)this.Plugin;
            if (plugin?.BlenderConnection?.IsConnected != true)
            {
                PluginLog.Warning("Brush Strength: Blender not connected");
                return;
            }

            Single delta = diff * StepPerTick;
            Task.Run(async () => await plugin.BlenderConnection.SendBrushStrengthDeltaAsync(delta));
            this.AdjustmentValueChanged();
        }

        protected override void RunCommand(String actionParameter)
        {
            var plugin = (BlenderFlowPlugin)this.Plugin;
            if (plugin?.BlenderConnection?.IsConnected != true) { return; }

            Task.Run(async () => await plugin.BlenderConnection.SendBrushStrengthSetAsync(ResetValue));
            this.AdjustmentValueChanged();
            PluginLog.Info("Brush Strength reset to 0.5");
        }

        protected override String GetAdjustmentValue(String actionParameter) => "Str";

        protected override BitmapImage GetAdjustmentImage(String actionParameter, PluginImageSize imageSize)
        {
            using var b = new BitmapBuilder(imageSize);
            b.Clear(BlenderTheme.PanelBg);
            Int32 w = imageSize.GetWidth();
            Int32 h = imageSize.GetHeight();

            Single brushCx = w * 0.32f, brushCy = h * 0.48f;
            Single brushR = Math.Min(w, h) * 0.22f;
            BlenderIcons.Brush(b, brushCx, brushCy, brushR,
                BlenderTheme.SculptRed, BlenderTheme.IconBright);

            // Strength bars on the right
            BlenderIcons.StrengthBars(b,
                w * 0.72f, h * 0.50f, Math.Min(w, h) * 0.16f,
                BlenderTheme.Orange);
            return b.ToImage();
        }
    }
}
