namespace Loupedeck.BlenderFlowPlugin
{
    using System;
    using System.Threading.Tasks;

    public class BlenderModeCommand : PluginDynamicCommand
    {
        public BlenderModeCommand()
            : base("Blender Modes", "Switch Blender mode", "Modes")
        {
            this.AddParameter("object", "Object Mode", "Modes");
            this.AddParameter("edit", "Edit Mode", "Modes");
            this.AddParameter("sculpt", "Sculpt Mode", "Modes");
        }

        protected override void RunCommand(String actionParameter)
        {
            var plugin = (BlenderFlowPlugin)this.Plugin;

            // Prefer WebSocket for precise mode switching
            if (plugin.BlenderConnection?.IsConnected == true)
            {
                var blenderMode = actionParameter switch
                {
                    "object" => "OBJECT",
                    "edit" => "EDIT",
                    "sculpt" => "SCULPT",
                    _ => "OBJECT"
                };

                Task.Run(async () => await plugin.BlenderConnection.SendSetModeAsync(blenderMode));
                PluginLog.Info($"Mode switch via WebSocket: {blenderMode}");
            }
            else
            {
                // Fallback: Cmd+Tab opens mode pie menu
                this.Plugin.ClientApplication.SendKeyboardShortcut(
                    VirtualKeyCode.Tab, ModifierKey.Command);
                PluginLog.Info($"Mode switch via pie menu: {actionParameter}");
            }
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            using var builder = new BitmapBuilder(imageSize);
            var plugin = this.Plugin as BlenderFlowPlugin;
            var currentMode = plugin?.CurrentMode ?? "OBJECT";

            // Highlight active mode
            var isActive = actionParameter switch
            {
                "object" => currentMode == "OBJECT",
                "edit" => currentMode.Contains("EDIT"),
                "sculpt" => currentMode.Contains("SCULPT"),
                _ => false
            };

            switch (actionParameter)
            {
                case "object":
                    builder.Clear(isActive ? new BitmapColor(234, 118, 0) : new BitmapColor(80, 40, 0));
                    builder.DrawText("Object\nMode", color: BitmapColor.White);
                    break;
                case "edit":
                    builder.Clear(isActive ? new BitmapColor(0, 140, 200) : new BitmapColor(0, 50, 70));
                    builder.DrawText("Edit\nMode", color: BitmapColor.White);
                    break;
                case "sculpt":
                    builder.Clear(isActive ? new BitmapColor(180, 60, 60) : new BitmapColor(60, 20, 20));
                    builder.DrawText("Sculpt\nMode", color: BitmapColor.White);
                    break;
                default:
                    builder.Clear(BitmapColor.Black);
                    builder.DrawText("Mode", color: BitmapColor.White);
                    break;
            }

            return builder.ToImage();
        }
    }
}
