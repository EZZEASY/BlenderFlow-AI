namespace Loupedeck.BlenderFlowPlugin
{
    using System;

    // ─── Save (Ctrl+S) ───────────────────────────────────────────────────

    public class SaveCommand : PluginDynamicCommand
    {
        public SaveCommand() : base("Save", "Save .blend file", "File") { }

        protected override void RunCommand(String actionParameter)
        {
            this.Plugin.ClientApplication.SendKeyboardShortcut(
                VirtualKeyCode.KeyS, ModifierKey.Control);
            PluginLog.Info("Save");
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            using var b = new BitmapBuilder(imageSize);
            b.Clear(BlenderTheme.PanelBg);
            Int32 w = imageSize.GetWidth();
            Int32 h = imageSize.GetHeight();
            Single cx = w / 2f, cy = h / 2f;
            Single size = Math.Min(w, h) * 0.58f;
            Single thick = Math.Max(2f, Math.Min(w, h) / 28f);
            BlenderIcons.FloppyDisk(b, cx, cy, size, BlenderTheme.Icon, thick);
            return b.ToImage();
        }
    }

    // ─── Render image (F12) ──────────────────────────────────────────────

    public class RenderImageCommand : PluginDynamicCommand
    {
        public RenderImageCommand() : base("Render Image", "Render current frame (F12)", "Render") { }

        protected override void RunCommand(String actionParameter)
        {
            this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.F12);
            PluginLog.Info("Render image");
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            using var b = new BitmapBuilder(imageSize);
            b.Clear(BlenderTheme.PanelBg);
            Int32 w = imageSize.GetWidth();
            Int32 h = imageSize.GetHeight();
            Single cx = w / 2f, cy = h / 2f;
            Single size = Math.Min(w, h) * 0.56f;
            Single thick = Math.Max(2f, Math.Min(w, h) / 28f);
            BlenderIcons.Camera(b, cx, cy, size, BlenderTheme.Orange, thick);
            return b.ToImage();
        }
    }

    // ─── Shading toggle (Z pie) ──────────────────────────────────────────

    public class ShadingPieCommand : PluginDynamicCommand
    {
        public ShadingPieCommand()
            : base("Shading Pie", "Open shading mode pie menu (Z)", "View") { }

        protected override void RunCommand(String actionParameter)
        {
            this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.KeyZ);
            PluginLog.Info("Shading pie");
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            using var b = new BitmapBuilder(imageSize);
            b.Clear(BlenderTheme.PanelBg);
            Int32 w = imageSize.GetWidth();
            Int32 h = imageSize.GetHeight();
            Single cx = w / 2f, cy = h / 2f;
            Single r = Math.Min(w, h) * 0.30f;
            Single thick = Math.Max(2f, Math.Min(w, h) / 28f);
            BlenderIcons.ShaderSphere(b, cx, cy, r, BlenderTheme.Icon, BlenderTheme.Icon, thick);
            return b.ToImage();
        }
    }

    // ─── Operator search (F3) ────────────────────────────────────────────

    public class OperatorSearchCommand : PluginDynamicCommand
    {
        public OperatorSearchCommand()
            : base("Search", "Operator search (F3)", "View") { }

        protected override void RunCommand(String actionParameter)
        {
            this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.F3);
            PluginLog.Info("Operator search");
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            using var b = new BitmapBuilder(imageSize);
            b.Clear(BlenderTheme.PanelBg);
            Int32 w = imageSize.GetWidth();
            Int32 h = imageSize.GetHeight();
            Single cx = w * 0.44f, cy = h * 0.44f;
            Single lensR = Math.Min(w, h) * 0.24f;
            Single thick = Math.Max(2f, Math.Min(w, h) / 26f);
            BlenderIcons.Magnifier(b, cx, cy, lensR, null, BlenderTheme.Icon, thick);
            return b.ToImage();
        }
    }

    // ─── Quick Favorites (Q) ─────────────────────────────────────────────

    public class QuickFavoritesCommand : PluginDynamicCommand
    {
        public QuickFavoritesCommand()
            : base("Quick Favorites", "Open Blender's Quick Favorites menu (Q)", "View") { }

        protected override void RunCommand(String actionParameter)
        {
            this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.KeyQ);
            PluginLog.Info("Quick favorites");
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            using var b = new BitmapBuilder(imageSize);
            b.Clear(BlenderTheme.PanelBg);
            Int32 w = imageSize.GetWidth();
            Int32 h = imageSize.GetHeight();
            Single cx = w / 2f, cy = h / 2f;
            Single size = Math.Min(w, h) * 0.30f;
            Single thick = Math.Max(2.2f, Math.Min(w, h) / 22f);
            BlenderIcons.Star4(b, cx, cy, size, BlenderTheme.Warning, thick);
            return b.ToImage();
        }
    }

    // ─── View pie (`) ────────────────────────────────────────────────────

    public class ViewPieCommand : PluginDynamicCommand
    {
        public ViewPieCommand()
            : base("View Pie", "Open viewpoint pie menu (`)", "View") { }

        protected override void RunCommand(String actionParameter)
        {
            // Oem3 = backtick (`) on US layout — opens View pie in Blender
            this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.Oem3);
            PluginLog.Info("View pie");
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            using var b = new BitmapBuilder(imageSize);
            b.Clear(BlenderTheme.PanelBg);
            Int32 w = imageSize.GetWidth();
            Int32 h = imageSize.GetHeight();
            Single cx = w / 2f, cy = h * 0.56f;
            Single size = Math.Min(w, h) * 0.28f;
            Single thick = Math.Max(2.2f, Math.Min(w, h) / 24f);
            BlenderIcons.AxisGizmo(b, cx, cy, size,
                BlenderTheme.AxisX, BlenderTheme.AxisY, BlenderTheme.AxisZ, thick);
            return b.ToImage();
        }
    }
}
