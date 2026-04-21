namespace Loupedeck.BlenderFlowPlugin
{
    using System;
    using System.Threading.Tasks;

    // Rotates the 3D viewport turntable-style by directly mutating
    // region_3d.view_rotation via WebSocket — no discrete Numpad steps,
    // no keystroke injection, no 15° jumps. Each dial tick is a small
    // continuous rotation around world Z.
    public class ViewportOrbitAdjustment : PluginDynamicAdjustment
    {
        // Degrees of rotation per dial tick. ~2° feels smooth on the
        // MX Creative Console's big dial; lower values feel laggy on
        // fast spins, higher ones skip frames visibly.
        private const Single DegreesPerTick = 2.0f;

        public ViewportOrbitAdjustment()
            : base(
                displayName: "Viewport Orbit",
                description: "Smooth turntable rotation of the 3D viewport",
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

            Single delta = diff * DegreesPerTick;
            Task.Run(async () => await plugin.BlenderConnection.SendViewportOrbitAsync(delta));
            this.AdjustmentValueChanged();
        }

        protected override void RunCommand(String actionParameter)
        {
            // Numpad 5 = toggle perspective/orthographic — still via keystroke,
            // no numeric input involved so no modifier issues.
            this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.NumPad5);
            this.AdjustmentValueChanged();
        }

        protected override String GetAdjustmentValue(String actionParameter) => "Orbit";

        protected override BitmapImage GetAdjustmentImage(String actionParameter, PluginImageSize imageSize)
        {
            using var b = new BitmapBuilder(imageSize);
            b.Clear(BlenderTheme.PanelBg);
            Int32 w = imageSize.GetWidth();
            Int32 h = imageSize.GetHeight();
            Single cx = w / 2f, cy = h * 0.52f;
            Single r = Math.Min(w, h) * 0.22f;
            Single thick = Math.Max(1.8f, Math.Min(w, h) / 32f);
            BlenderIcons.IsoCube(b, cx, cy, r, BlenderTheme.IconDim, thick);
            Int32 ringR = (Int32)(r * 1.8f);
            BlenderIcons.CurvedArrow(b, (Int32)cx, (Int32)cy, ringR,
                startAngle: 200f, sweepAngle: 180f,
                color: BlenderTheme.Orange, thickness: thick);
            return b.ToImage();
        }
    }
}
