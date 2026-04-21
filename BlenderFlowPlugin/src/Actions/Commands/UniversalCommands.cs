namespace Loupedeck.BlenderFlowPlugin
{
    using System;
    using System.Threading.Tasks;

    // ─── Save (via bpy.ops over WebSocket) ───────────────────────────────

    public class SaveCommand : PluginDynamicCommand
    {
        public SaveCommand() : base("Save", "Save .blend file", "File") { }

        protected override void RunCommand(String actionParameter)
        {
            var plugin = (BlenderFlowPlugin)this.Plugin;
            if (plugin?.BlenderConnection?.IsConnected != true)
            {
                PluginLog.Warning("Save: Blender not connected");
                return;
            }
            Task.Run(async () => await plugin.BlenderConnection.SendSaveAsync());
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
        public RenderImageCommand() : base("Render Image", "Render current frame", "Render") { }

        protected override void RunCommand(String actionParameter)
        {
            var plugin = (BlenderFlowPlugin)this.Plugin;
            if (plugin?.BlenderConnection?.IsConnected != true)
            {
                PluginLog.Warning("Render: Blender not connected");
                return;
            }
            Task.Run(async () => await plugin.BlenderConnection.SendRenderImageAsync());
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

    // ─── Individual view commands (Numpad 1/3/7/0) ───────────────────────
    // Blender 5.0 removed the View Pie default binding, so these
    // dedicated keys replace it. Each sends a bare Numpad keystroke —
    // no modifier, so it reaches Blender cleanly even on macOS.

    public abstract class ViewCommandBase : PluginDynamicCommand
    {
        private readonly VirtualKeyCode _key;
        private readonly String _label;
        private readonly BitmapColor _bg;
        private readonly BitmapColor _accent;

        protected ViewCommandBase(String displayName, String description,
            VirtualKeyCode key, String label, BitmapColor bg, BitmapColor accent)
            : base(displayName, description, "View")
        {
            _key = key;
            _label = label;
            _bg = bg;
            _accent = accent;
        }

        protected override void RunCommand(String actionParameter)
        {
            this.Plugin.ClientApplication.SendKeyboardShortcut(_key);
            PluginLog.Info($"View: {_label}");
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            using var b = new BitmapBuilder(imageSize);
            b.Clear(_bg);
            Int32 w = imageSize.GetWidth();
            Int32 h = imageSize.GetHeight();
            DrawIcon(b, w, h, _accent);
            b.DrawText(_label, 0, (Int32)(h * 0.68f), w, (Int32)(h * 0.28f),
                BlenderTheme.IconBright, 0, 0, 0, null);
            return b.ToImage();
        }

        protected abstract void DrawIcon(BitmapBuilder b, Int32 w, Int32 h, BitmapColor accent);
    }

    public class ViewFrontCommand : ViewCommandBase
    {
        public ViewFrontCommand()
            : base("View Front", "Front orthographic view (Numpad 1)",
                   VirtualKeyCode.NumPad1, "FRONT",
                   BlenderTheme.AxisYTint, BlenderTheme.AxisY) { }

        protected override void DrawIcon(BitmapBuilder b, Int32 w, Int32 h, BitmapColor accent)
        {
            // Y-axis arrow pointing toward viewer (down-front)
            Single cx = w / 2f, cy = h * 0.40f;
            Single s = Math.Min(w, h) * 0.22f;
            Single thick = Math.Max(2.2f, Math.Min(w, h) / 22f);
            b.DrawLine(cx, cy - s, cx, cy + s, accent, thick);
            b.DrawLine(cx - s * 0.45f, cy + s * 0.55f, cx, cy + s, accent, thick);
            b.DrawLine(cx + s * 0.45f, cy + s * 0.55f, cx, cy + s, accent, thick);
            b.FillCircle(cx, cy - s, Math.Max(3f, s * 0.18f), accent);
        }
    }

    public class ViewRightCommand : ViewCommandBase
    {
        public ViewRightCommand()
            : base("View Right", "Right orthographic view (Numpad 3)",
                   VirtualKeyCode.NumPad3, "RIGHT",
                   BlenderTheme.AxisXTint, BlenderTheme.AxisX) { }

        protected override void DrawIcon(BitmapBuilder b, Int32 w, Int32 h, BitmapColor accent)
        {
            // X-axis arrow pointing right
            Single cx = w / 2f, cy = h * 0.40f;
            Single s = Math.Min(w, h) * 0.22f;
            Single thick = Math.Max(2.2f, Math.Min(w, h) / 22f);
            b.DrawLine(cx - s, cy, cx + s, cy, accent, thick);
            b.DrawLine(cx + s * 0.55f, cy - s * 0.45f, cx + s, cy, accent, thick);
            b.DrawLine(cx + s * 0.55f, cy + s * 0.45f, cx + s, cy, accent, thick);
            b.FillCircle(cx - s, cy, Math.Max(3f, s * 0.18f), accent);
        }
    }

    public class ViewTopCommand : ViewCommandBase
    {
        public ViewTopCommand()
            : base("View Top", "Top orthographic view (Numpad 7)",
                   VirtualKeyCode.NumPad7, "TOP",
                   BlenderTheme.AxisZTint, BlenderTheme.AxisZ) { }

        protected override void DrawIcon(BitmapBuilder b, Int32 w, Int32 h, BitmapColor accent)
        {
            // Z-axis arrow pointing up
            Single cx = w / 2f, cy = h * 0.40f;
            Single s = Math.Min(w, h) * 0.22f;
            Single thick = Math.Max(2.2f, Math.Min(w, h) / 22f);
            b.DrawLine(cx, cy + s, cx, cy - s, accent, thick);
            b.DrawLine(cx - s * 0.45f, cy - s * 0.55f, cx, cy - s, accent, thick);
            b.DrawLine(cx + s * 0.45f, cy - s * 0.55f, cx, cy - s, accent, thick);
            b.FillCircle(cx, cy + s, Math.Max(3f, s * 0.18f), accent);
        }
    }

    public class ViewCameraCommand : ViewCommandBase
    {
        public ViewCameraCommand()
            : base("View Camera", "Camera view (Numpad 0)",
                   VirtualKeyCode.NumPad0, "CAM",
                   BlenderTheme.PanelBg, BlenderTheme.Orange) { }

        protected override void DrawIcon(BitmapBuilder b, Int32 w, Int32 h, BitmapColor accent)
        {
            Single cx = w / 2f, cy = h * 0.40f;
            Single size = Math.Min(w, h) * 0.42f;
            Single thick = Math.Max(2f, Math.Min(w, h) / 28f);
            BlenderIcons.Camera(b, cx, cy, size, accent, thick);
        }
    }
}
