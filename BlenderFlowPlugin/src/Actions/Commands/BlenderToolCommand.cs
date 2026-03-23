namespace Loupedeck.BlenderFlowPlugin
{
    using System;

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
            switch (actionParameter)
            {
                case "extrude":
                    this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.KeyE);
                    break;
                case "bevel":
                    // macOS: Blender uses Cmd as primary modifier
                    // TODO Layer 2: WebSocket fallback if modifier keys don't work
                    this.Plugin.ClientApplication.SendKeyboardShortcut(
                        VirtualKeyCode.KeyB, ModifierKey.Command);
                    break;
                case "loopcut":
                    this.Plugin.ClientApplication.SendKeyboardShortcut(
                        VirtualKeyCode.KeyR, ModifierKey.Command);
                    break;
            }

            PluginLog.Info($"Tool executed: {actionParameter}");
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var builder = new BitmapBuilder(imageSize);
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
