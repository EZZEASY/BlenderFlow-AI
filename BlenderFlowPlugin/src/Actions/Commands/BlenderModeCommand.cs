namespace Loupedeck.BlenderFlowPlugin
{
    using System;
    using System.Threading.Tasks;

    public abstract class BlenderModeCommandBase : PluginDynamicCommand
    {
        private readonly String _blenderMode;

        protected BlenderModeCommandBase(String displayName, String blenderMode)
            : base(displayName, $"Switch to {displayName}", "Modes")
        {
            _blenderMode = blenderMode;
        }

        protected override void RunCommand(String actionParameter)
        {
            var plugin = (BlenderFlowPlugin)this.Plugin;

            if (plugin.BlenderConnection?.IsConnected == true)
            {
                Task.Run(async () => await plugin.BlenderConnection.SendSetModeAsync(_blenderMode));
                PluginLog.Info($"Mode switch via WebSocket: {_blenderMode}");
            }
            else
            {
                this.Plugin.ClientApplication.SendKeyboardShortcut(
                    VirtualKeyCode.Tab, ModifierKey.Control);
                PluginLog.Info($"Mode switch via pie menu: {_blenderMode}");
            }
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            using var builder = new BitmapBuilder(imageSize);
            var plugin = this.Plugin as BlenderFlowPlugin;
            var currentMode = plugin?.CurrentMode ?? "OBJECT";
            var isActive = currentMode.Contains(_blenderMode);

            Int32 w = imageSize.GetWidth();
            Int32 h = imageSize.GetHeight();
            DrawModeIcon(builder, w, h, isActive);
            return builder.ToImage();
        }

        protected abstract void DrawModeIcon(BitmapBuilder b, Int32 w, Int32 h, Boolean isActive);
    }

    public class ObjectModeCommand : BlenderModeCommandBase
    {
        public ObjectModeCommand() : base("Object Mode", "OBJECT") { }

        protected override void DrawModeIcon(BitmapBuilder b, Int32 w, Int32 h, Boolean isActive)
        {
            b.Clear(isActive ? BlenderTheme.OrangeTint : BlenderTheme.PanelBg);
            Single cx = w / 2f, cy = h / 2f;
            Single r = Math.Min(w, h) * 0.34f;
            Single thick = Math.Max(2f, Math.Min(w, h) / 28f);
            var outline = isActive ? BlenderTheme.Orange : BlenderTheme.Icon;
            BlenderIcons.IsoCube(b, cx, cy, r, outline, thick);
        }
    }

    public class EditModeCommand : BlenderModeCommandBase
    {
        public EditModeCommand() : base("Edit Mode", "EDIT") { }

        protected override void DrawModeIcon(BitmapBuilder b, Int32 w, Int32 h, Boolean isActive)
        {
            b.Clear(isActive ? BlenderTheme.EditBlueTint : BlenderTheme.PanelBg);
            Single cx = w / 2f, cy = h / 2f;
            Single r = Math.Min(w, h) * 0.32f;
            Single thick = Math.Max(1.5f, Math.Min(w, h) / 32f);
            var outline = isActive ? BlenderTheme.IconBright : BlenderTheme.IconDim;
            var dot = isActive ? BlenderTheme.Orange : BlenderTheme.EditBlue;
            BlenderIcons.IsoCube(b, cx, cy, r, outline, thick);
            BlenderIcons.IsoCubeVertices(b, cx, cy, r, dot, Math.Max(2.5f, r * 0.14f));
        }
    }

    public class SculptModeCommand : BlenderModeCommandBase
    {
        public SculptModeCommand() : base("Sculpt Mode", "SCULPT") { }

        protected override void DrawModeIcon(BitmapBuilder b, Int32 w, Int32 h, Boolean isActive)
        {
            b.Clear(isActive ? BlenderTheme.SculptTint : BlenderTheme.PanelBg);
            Single cx = w / 2f, cy = h / 2f + Math.Min(w, h) * 0.04f;
            Single r = Math.Min(w, h) * 0.26f;
            var body = isActive ? BlenderTheme.SculptRed : BlenderTheme.IconDim;
            var hi = isActive ? BlenderTheme.IconBright : BlenderTheme.Icon;
            BlenderIcons.Brush(b, cx, cy, r, body, hi);
        }
    }

    public class TexturePaintModeCommand : BlenderModeCommandBase
    {
        public TexturePaintModeCommand() : base("Texture Paint Mode", "TEXTURE_PAINT") { }

        protected override void DrawModeIcon(BitmapBuilder b, Int32 w, Int32 h, Boolean isActive)
        {
            b.Clear(isActive ? BlenderTheme.PaintTint : BlenderTheme.PanelBg);
            Single cx = w / 2f, cy = h / 2f + Math.Min(w, h) * 0.04f;
            Single r = Math.Min(w, h) * 0.26f;
            var body = isActive ? BlenderTheme.PaintGold : BlenderTheme.IconDim;
            var hi = isActive ? BlenderTheme.IconBright : BlenderTheme.Icon;
            BlenderIcons.Brush(b, cx, cy, r, body, hi);
            // Palette dots (axis RGB) to differentiate from Sculpt's plain brush
            Single dotR = Math.Max(2.5f, Math.Min(w, h) * 0.045f);
            b.FillCircle(w * 0.24f, h * 0.30f, dotR, BlenderTheme.AxisX);
            b.FillCircle(w * 0.14f, h * 0.48f, dotR, BlenderTheme.AxisY);
            b.FillCircle(w * 0.22f, h * 0.66f, dotR, BlenderTheme.AxisZ);
        }
    }
}
