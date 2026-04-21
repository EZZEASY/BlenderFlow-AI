namespace Loupedeck.BlenderFlowPlugin
{
    using System;

    public abstract class BlenderToolCommandBase : PluginDynamicCommand
    {
        private readonly String _displayName;
        private readonly VirtualKeyCode _key;
        private readonly ModifierKey _modifier;

        protected BlenderToolCommandBase(String displayName,
            VirtualKeyCode key, ModifierKey modifier = 0)
            : base(displayName, $"Blender {displayName} tool", "Tools")
        {
            _displayName = displayName;
            _key = key;
            _modifier = modifier;
        }

        // Mesh edit-mode tools are modal operators that need the user's mouse
        // over the 3D viewport. Driving them from a background timer via
        // bpy.ops fails the poll/context check. Sending the native keystroke
        // lets Blender's own keymap handle it — the user's mouse position
        // drives the modal interaction as it normally would.
        protected override void RunCommand(String actionParameter)
        {
            if (_modifier != 0)
            {
                this.Plugin.ClientApplication.SendKeyboardShortcut(_key, _modifier);
            }
            else
            {
                this.Plugin.ClientApplication.SendKeyboardShortcut(_key);
            }
            PluginLog.Info($"Tool keystroke: {_displayName}");
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            using var builder = new BitmapBuilder(imageSize);
            builder.Clear(BlenderTheme.PanelBg);
            Int32 w = imageSize.GetWidth();
            Int32 h = imageSize.GetHeight();
            DrawToolIcon(builder, w, h);
            return builder.ToImage();
        }

        protected abstract void DrawToolIcon(BitmapBuilder b, Int32 w, Int32 h);
    }

    public class ExtrudeCommand : BlenderToolCommandBase
    {
        public ExtrudeCommand() : base("Extrude", VirtualKeyCode.KeyE) { }

        protected override void DrawToolIcon(BitmapBuilder b, Int32 w, Int32 h)
        {
            Single cx = w / 2f, cy = h / 2f;
            Single size = Math.Min(w, h) * 0.60f;
            Single thick = Math.Max(2f, Math.Min(w, h) / 28f);
            BlenderIcons.ExtrudeArrow(b, cx, cy, size, BlenderTheme.Orange, thick);
        }
    }

    public class BevelCommand : BlenderToolCommandBase
    {
        public BevelCommand() : base("Bevel", VirtualKeyCode.KeyB, ModifierKey.Control) { }

        protected override void DrawToolIcon(BitmapBuilder b, Int32 w, Int32 h)
        {
            Single cx = w / 2f, cy = h / 2f;
            Single size = Math.Min(w, h) * 0.62f;
            Single thick = Math.Max(2f, Math.Min(w, h) / 28f);
            BlenderIcons.BeveledCube(b, cx, cy, size,
                BlenderTheme.Icon, BlenderTheme.Orange, thick);
        }
    }

    public class LoopCutCommand : BlenderToolCommandBase
    {
        public LoopCutCommand() : base("Loop Cut", VirtualKeyCode.KeyR, ModifierKey.Control) { }

        protected override void DrawToolIcon(BitmapBuilder b, Int32 w, Int32 h)
        {
            Single cx = w / 2f, cy = h / 2f;
            Single r = Math.Min(w, h) * 0.34f;
            Single thick = Math.Max(1.8f, Math.Min(w, h) / 32f);
            BlenderIcons.LoopCutCube(b, cx, cy, r,
                BlenderTheme.IconDim, BlenderTheme.Orange, thick);
        }
    }
}
