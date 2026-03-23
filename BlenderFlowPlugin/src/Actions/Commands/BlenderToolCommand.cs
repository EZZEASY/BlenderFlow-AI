namespace Loupedeck.BlenderFlowPlugin
{
    using System;
    using System.Threading.Tasks;

    public class BlenderToolCommand : PluginDynamicCommand
    {
        public BlenderToolCommand()
            : base("Blender Tools", "Mesh editing tools", "Tools")
        {
            this.AddParameter("extrude", "Extrude", "Tools");
            this.AddParameter("bevel", "Bevel", "Tools");
            this.AddParameter("loopcut", "Loop Cut", "Tools");
        }

        protected override void RunCommand(String actionParameter)
        {
            var plugin = (BlenderFlowPlugin)this.Plugin;

            // Prefer WebSocket — avoids macOS modifier key issues
            if (plugin.BlenderConnection?.IsConnected == true)
            {
                Task.Run(async () => await plugin.BlenderConnection.SendToolAsync(actionParameter));
                PluginLog.Info($"Tool via WebSocket: {actionParameter}");
                return;
            }

            // Fallback: keyboard shortcuts
            switch (actionParameter)
            {
                case "extrude":
                    this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.KeyE);
                    break;
                case "bevel":
                    this.Plugin.ClientApplication.SendKeyboardShortcut(
                        VirtualKeyCode.KeyB, ModifierKey.Command);
                    break;
                case "loopcut":
                    this.Plugin.ClientApplication.SendKeyboardShortcut(
                        VirtualKeyCode.KeyR, ModifierKey.Command);
                    break;
            }

            PluginLog.Info($"Tool via shortcut: {actionParameter}");
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            using var builder = new BitmapBuilder(imageSize);
            builder.Clear(new BitmapColor(60, 60, 60));

            switch (actionParameter)
            {
                case "extrude":
                    builder.DrawText("Extrude", color: BitmapColor.White);
                    break;
                case "bevel":
                    builder.DrawText("Bevel", color: BitmapColor.White);
                    break;
                case "loopcut":
                    builder.DrawText("Loop\nCut", color: BitmapColor.White);
                    break;
                default:
                    builder.DrawText("Tool", color: BitmapColor.White);
                    break;
            }

            return builder.ToImage();
        }
    }
}
