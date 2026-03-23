namespace Loupedeck.BlenderFlowPlugin
{
    using System;

    public class UndoRedoCommand : PluginDynamicCommand
    {
        public UndoRedoCommand()
            : base("Undo / Redo", "Undo or redo last action", "Edit")
        {
            this.AddParameter("undo", "Undo", "Edit");
            this.AddParameter("redo", "Redo", "Edit");
        }

        protected override void RunCommand(String actionParameter)
        {
            switch (actionParameter)
            {
                case "undo":
                    this.Plugin.ClientApplication.SendKeyboardShortcut(
                        VirtualKeyCode.KeyZ, ModifierKey.Command);
                    break;
                case "redo":
                    this.Plugin.ClientApplication.SendKeyboardShortcut(
                        VirtualKeyCode.KeyZ, ModifierKey.Command | ModifierKey.Shift);
                    break;
            }

            PluginLog.Info($"Edit action: {actionParameter}");
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var builder = new BitmapBuilder(imageSize);
            builder.Clear(new BitmapColor(50, 50, 50));

            switch (actionParameter)
            {
                case "undo":
                    builder.DrawText("Undo", color: BitmapColor.White);
                    break;
                case "redo":
                    builder.DrawText("Redo", color: BitmapColor.White);
                    break;
            }

            return builder.ToImage();
        }
    }
}
