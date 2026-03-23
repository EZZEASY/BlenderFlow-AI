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
            this.Plugin.ClientApplication.SendKeyboardShortcut(
                VirtualKeyCode.KeyZ, ModifierKey.Command);
            PluginLog.Info("Undo");
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            using var builder = new BitmapBuilder(imageSize);
            builder.Clear(new BitmapColor(50, 50, 50));
            builder.DrawText("Undo", color: BitmapColor.White);
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
                VirtualKeyCode.KeyZ, ModifierKey.Command | ModifierKey.Shift);
            PluginLog.Info("Redo");
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            using var builder = new BitmapBuilder(imageSize);
            builder.Clear(new BitmapColor(50, 50, 50));
            builder.DrawText("Redo", color: BitmapColor.White);
            return builder.ToImage();
        }
    }
}
