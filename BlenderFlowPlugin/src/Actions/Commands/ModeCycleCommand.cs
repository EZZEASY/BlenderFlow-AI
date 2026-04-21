namespace Loupedeck.BlenderFlowPlugin
{
    using System;
    using System.Threading.Tasks;

    // Single home-page key that cycles Object → Edit → Sculpt → Texture Paint
    // → Object and reflects the current mode via color + icon. Users who
    // want direct access to a specific mode can still use the individual
    // mode commands.
    public class ModeCycleCommand : PluginDynamicCommand
    {
        public ModeCycleCommand()
            : base("Mode Cycle", "Cycle mode: Object → Edit → Sculpt → Texture Paint", "Modes")
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
            // Order of these Contains checks matters: TEXTURE_PAINT must be
            // tested before the looser "EDIT"/"PAINT" substrings. SCULPT
            // doesn't appear inside any other mode string.
            if (current.Contains("TEXTURE_PAINT")) { return "OBJECT"; }
            if (current.Contains("SCULPT"))        { return "TEXTURE_PAINT"; }
            if (current.Contains("OBJECT"))        { return "EDIT"; }
            if (current.Contains("EDIT"))          { return "SCULPT"; }
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

            // Check TEXTURE_PAINT before EDIT — "EDIT" would match as a
            // substring of "PARTICLE_EDIT" but not here; the important order
            // is: specific modes before generic fallbacks.
            if (mode.Contains("TEXTURE_PAINT"))
            {
                b.Clear(BlenderTheme.PaintTint);
                // Brush body + three palette dots suggesting color paint
                BlenderIcons.Brush(b, cx, cy + Math.Min(w, h) * 0.04f,
                    Math.Min(w, h) * 0.22f,
                    BlenderTheme.PaintGold, BlenderTheme.IconBright);
                Single dotR = Math.Max(2.5f, Math.Min(w, h) * 0.045f);
                b.FillCircle(w * 0.24f, h * 0.30f, dotR, BlenderTheme.AxisX);
                b.FillCircle(w * 0.14f, h * 0.48f, dotR, BlenderTheme.AxisY);
                b.FillCircle(w * 0.22f, h * 0.66f, dotR, BlenderTheme.AxisZ);
            }
            else if (mode.Contains("EDIT"))
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
