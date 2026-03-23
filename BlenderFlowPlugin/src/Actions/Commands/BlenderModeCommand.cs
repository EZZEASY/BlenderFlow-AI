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
                    VirtualKeyCode.Tab, ModifierKey.Command);
                PluginLog.Info($"Mode switch via pie menu: {_blenderMode}");
            }
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            using var builder = new BitmapBuilder(imageSize);
            var plugin = this.Plugin as BlenderFlowPlugin;
            var currentMode = plugin?.CurrentMode ?? "OBJECT";
            var isActive = currentMode.Contains(_blenderMode);

            DrawModeIcon(builder, isActive);
            return builder.ToImage();
        }

        protected abstract void DrawModeIcon(BitmapBuilder builder, Boolean isActive);
    }

    public class ObjectModeCommand : BlenderModeCommandBase
    {
        public ObjectModeCommand() : base("Object Mode", "OBJECT") { }

        protected override void DrawModeIcon(BitmapBuilder builder, Boolean isActive)
        {
            builder.Clear(isActive ? new BitmapColor(234, 118, 0) : new BitmapColor(80, 40, 0));
            builder.DrawText("Object\nMode", color: BitmapColor.White);
        }
    }

    public class EditModeCommand : BlenderModeCommandBase
    {
        public EditModeCommand() : base("Edit Mode", "EDIT") { }

        protected override void DrawModeIcon(BitmapBuilder builder, Boolean isActive)
        {
            builder.Clear(isActive ? new BitmapColor(0, 140, 200) : new BitmapColor(0, 50, 70));
            builder.DrawText("Edit\nMode", color: BitmapColor.White);
        }
    }

    public class SculptModeCommand : BlenderModeCommandBase
    {
        public SculptModeCommand() : base("Sculpt Mode", "SCULPT") { }

        protected override void DrawModeIcon(BitmapBuilder builder, Boolean isActive)
        {
            builder.Clear(isActive ? new BitmapColor(180, 60, 60) : new BitmapColor(60, 20, 20));
            builder.DrawText("Sculpt\nMode", color: BitmapColor.White);
        }
    }
}
