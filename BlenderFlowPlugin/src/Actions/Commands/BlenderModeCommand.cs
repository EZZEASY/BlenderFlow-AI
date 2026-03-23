namespace Loupedeck.BlenderFlowPlugin
{
    using System;

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
            switch (actionParameter)
            {
                case "object":
                    // Tab cycles modes; for Object mode specifically, use the pie menu
                    this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.Tab);
                    break;
                case "edit":
                    this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.Tab);
                    break;
                case "sculpt":
                    this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.Tab);
                    break;
            }

            PluginLog.Info($"Mode switch: {actionParameter}");
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var builder = new BitmapBuilder(imageSize);

            switch (actionParameter)
            {
                case "object":
                    builder.Clear(new BitmapColor(234, 118, 0));
                    builder.DrawText("Object\nMode", color: BitmapColor.White);
                    break;
                case "edit":
                    builder.Clear(new BitmapColor(0, 140, 200));
                    builder.DrawText("Edit\nMode", color: BitmapColor.White);
                    break;
                case "sculpt":
                    builder.Clear(new BitmapColor(180, 60, 60));
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
