namespace Loupedeck.BlenderFlowPlugin
{
    using System;
    using System.Threading.Tasks;

    // Single home-page key that cycles Object → Edit → Sculpt → Object
    // and reflects the current mode via color + icon. Users who want direct
    // access to one specific mode can still use the individual mode commands.
    public class ModeCycleCommand : PluginDynamicCommand
    {
        public ModeCycleCommand()
            : base("Mode Cycle", "Cycle Blender mode (Object → Edit → Sculpt)", "Modes")
        {
        }

        protected override Boolean OnLoad()
        {
            var plugin = this.Plugin as BlenderFlowPlugin;
            if (plugin != null)
            {
                plugin.OnBlenderStateChanged += this.ActionImageChanged;
            }
            return true;
        }

        protected override Boolean OnUnload()
        {
            var plugin = this.Plugin as BlenderFlowPlugin;
            if (plugin != null)
            {
                plugin.OnBlenderStateChanged -= this.ActionImageChanged;
            }
            return true;
        }

        protected override void RunCommand(String actionParameter)
        {
            var plugin = (BlenderFlowPlugin)this.Plugin;
            var next = NextMode(plugin?.CurrentMode ?? "OBJECT");

            if (plugin?.BlenderConnection?.IsConnected == true)
            {
                Task.Run(async () => await plugin.BlenderConnection.SendSetModeAsync(next));
                PluginLog.Info($"Mode cycle → {next}");
            }
            else
            {
                this.Plugin.ClientApplication.SendKeyboardShortcut(
                    VirtualKeyCode.Tab, ModifierKey.Control);
                PluginLog.Info("Mode cycle via pie menu (disconnected)");
            }
        }

        private static String NextMode(String current)
        {
            if (current.Contains("OBJECT")) { return "EDIT"; }
            if (current.Contains("EDIT"))   { return "SCULPT"; }
            if (current.Contains("SCULPT")) { return "OBJECT"; }
            return "OBJECT";
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            using var b = new BitmapBuilder(imageSize);
            Int32 w = imageSize.GetWidth();
            Int32 h = imageSize.GetHeight();
            Single cx = w / 2f, cy = h / 2f;
            Single r = Math.Min(w, h) * 0.28f;
            Single thick = Math.Max(1.8f, Math.Min(w, h) / 32f);

            var plugin = this.Plugin as BlenderFlowPlugin;
            var mode = plugin?.CurrentMode ?? "OBJECT";

            if (mode.Contains("EDIT"))
            {
                b.Clear(BlenderTheme.EditBlueTint);
                BlenderIcons.IsoCube(b, cx, cy, r, BlenderTheme.IconBright, thick);
                BlenderIcons.IsoCubeVertices(b, cx, cy, r, BlenderTheme.Orange,
                    Math.Max(2.5f, r * 0.14f));
            }
            else if (mode.Contains("SCULPT"))
            {
                b.Clear(BlenderTheme.SculptTint);
                BlenderIcons.Brush(b, cx, cy + Math.Min(w, h) * 0.04f,
                    Math.Min(w, h) * 0.22f,
                    BlenderTheme.SculptRed, BlenderTheme.IconBright);
            }
            else // OBJECT (default)
            {
                b.Clear(BlenderTheme.OrangeTint);
                BlenderIcons.IsoCube(b, cx, cy, r, BlenderTheme.Orange, thick);
            }

            // Cycle hint (↻) in the corner
            Int32 hx = (Int32)(w * 0.80f);
            Int32 hy = (Int32)(h * 0.22f);
            Int32 hr = (Int32)(Math.Min(w, h) * 0.10f);
            BlenderIcons.CurvedArrow(b, hx, hy, hr,
                startAngle: 40f, sweepAngle: 270f,
                color: BlenderTheme.Icon, thickness: Math.Max(1.2f, thick * 0.6f));

            return b.ToImage();
        }
    }
}
