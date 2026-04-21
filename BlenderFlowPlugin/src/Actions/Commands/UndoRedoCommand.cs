namespace Loupedeck.BlenderFlowPlugin
{
    using System;
    using System.Threading.Tasks;

    public class UndoCommand : PluginDynamicCommand
    {
        public UndoCommand()
            : base("Undo", "Undo last action", "Edit")
        {
        }

        protected override void RunCommand(String actionParameter)
        {
            // bpy.ops.ed.undo() via WebSocket — bypasses the Mac keystroke
            // path entirely. The SDK's SendKeyboardShortcut drops modifier
            // keys on macOS, so Cmd+Z never reaches Blender as a real
            // shortcut. Calling the operator directly is deterministic
            // and doesn't require Blender to be the focused app.
            var plugin = (BlenderFlowPlugin)this.Plugin;
            if (plugin?.BlenderConnection?.IsConnected != true)
            {
                PluginLog.Warning("Undo: Blender not connected");
                return;
            }
            Task.Run(async () => await plugin.BlenderConnection.SendUndoAsync());
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
            var plugin = (BlenderFlowPlugin)this.Plugin;
            if (plugin?.BlenderConnection?.IsConnected != true)
            {
                PluginLog.Warning("Redo: Blender not connected");
                return;
            }
            Task.Run(async () => await plugin.BlenderConnection.SendRedoAsync());
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
