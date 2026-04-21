namespace Loupedeck.BlenderFlowPlugin
{
    using System;

    public class UndoCommand : PluginDynamicCommand
    {
        public UndoCommand()
            : base("Undo", "Undo last action", "Edit")
        {
        }

        protected override void RunCommand(String actionParameter)
        {
            // Blender uses Ctrl (not Cmd) on macOS too — its keymap is
            // identical across platforms, unlike most Mac apps.
            this.Plugin.ClientApplication.SendKeyboardShortcut(
                VirtualKeyCode.KeyZ, ModifierKey.Control);
            PluginLog.Info("Undo");
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            using var builder = new BitmapBuilder(imageSize);
            builder.Clear(BlenderTheme.PanelBg);
            Int32 w = imageSize.GetWidth();
            Int32 h = imageSize.GetHeight();
            Int32 cx = w / 2, cy = h / 2;
            Int32 radius = (Int32)(Math.Min(w, h) * 0.32f);
            Single thick = Math.Max(2f, Math.Min(w, h) / 24f);
            // Counter-clockwise arc: sweep from ~-45° upward and around
            BlenderIcons.CurvedArrow(builder, cx, cy, radius,
                startAngle: -40f, sweepAngle: -270f,
                color: BlenderTheme.Icon, thickness: thick);
            return builder.ToImage();
        }
    }

    public class RedoCommand : PluginDynamicCommand
    {
        public RedoCommand()
            : base("Redo", "Redo last action", "Edit")
        {
        }

        protected override void RunCommand(String actionParameter)
        {
            this.Plugin.ClientApplication.SendKeyboardShortcut(
                VirtualKeyCode.KeyZ, ModifierKey.Control | ModifierKey.Shift);
            PluginLog.Info("Redo");
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            using var builder = new BitmapBuilder(imageSize);
            builder.Clear(BlenderTheme.PanelBg);
            Int32 w = imageSize.GetWidth();
            Int32 h = imageSize.GetHeight();
            Int32 cx = w / 2, cy = h / 2;
            Int32 radius = (Int32)(Math.Min(w, h) * 0.32f);
            Single thick = Math.Max(2f, Math.Min(w, h) / 24f);
            // Clockwise arc: mirror of undo
            BlenderIcons.CurvedArrow(builder, cx, cy, radius,
                startAngle: 220f, sweepAngle: 270f,
                color: BlenderTheme.Icon, thickness: thick);
            return builder.ToImage();
        }
    }
}
