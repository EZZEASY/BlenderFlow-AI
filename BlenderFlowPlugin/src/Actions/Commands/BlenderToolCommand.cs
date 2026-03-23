namespace Loupedeck.BlenderFlowPlugin
{
    using System;
    using System.Threading.Tasks;

    public abstract class BlenderToolCommandBase : PluginDynamicCommand
    {
        private readonly String _toolOp;
        private readonly VirtualKeyCode _fallbackKey;
        private readonly ModifierKey _fallbackModifier;

        protected BlenderToolCommandBase(String displayName, String toolOp,
            VirtualKeyCode fallbackKey, ModifierKey fallbackModifier = 0)
            : base(displayName, $"Blender {displayName} tool", "Tools")
        {
            _toolOp = toolOp;
            _fallbackKey = fallbackKey;
            _fallbackModifier = fallbackModifier;
        }

        protected override void RunCommand(String actionParameter)
        {
            var plugin = (BlenderFlowPlugin)this.Plugin;

            if (plugin.BlenderConnection?.IsConnected == true)
            {
                Task.Run(async () => await plugin.BlenderConnection.SendToolAsync(_toolOp));
                PluginLog.Info($"Tool via WebSocket: {_toolOp}");
                return;
            }

            if (_fallbackModifier != 0)
            {
                this.Plugin.ClientApplication.SendKeyboardShortcut(_fallbackKey, _fallbackModifier);
            }
            else
            {
                this.Plugin.ClientApplication.SendKeyboardShortcut(_fallbackKey);
            }

            PluginLog.Info($"Tool via shortcut: {_toolOp}");
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            using var builder = new BitmapBuilder(imageSize);
            builder.Clear(new BitmapColor(60, 60, 60));
            DrawToolIcon(builder);
            return builder.ToImage();
        }

        protected abstract void DrawToolIcon(BitmapBuilder builder);
    }

    public class ExtrudeCommand : BlenderToolCommandBase
    {
        public ExtrudeCommand() : base("Extrude", "extrude", VirtualKeyCode.KeyE) { }
        protected override void DrawToolIcon(BitmapBuilder builder) =>
            builder.DrawText("Extrude", color: BitmapColor.White);
    }

    public class BevelCommand : BlenderToolCommandBase
    {
        public BevelCommand() : base("Bevel", "bevel", VirtualKeyCode.KeyB, ModifierKey.Command) { }
        protected override void DrawToolIcon(BitmapBuilder builder) =>
            builder.DrawText("Bevel", color: BitmapColor.White);
    }

    public class LoopCutCommand : BlenderToolCommandBase
    {
        public LoopCutCommand() : base("Loop Cut", "loopcut", VirtualKeyCode.KeyR, ModifierKey.Command) { }
        protected override void DrawToolIcon(BitmapBuilder builder) =>
            builder.DrawText("Loop\nCut", color: BitmapColor.White);
    }
}
