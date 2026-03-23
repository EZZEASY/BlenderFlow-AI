namespace Loupedeck.BlenderFlowPlugin
{
    using System;

    public class AIGenerateCommand : PluginDynamicCommand
    {
        public AIGenerateCommand()
            : base(
                displayName: "AI Generate",
                description: "Generate 3D model with AI (Layer 2)",
                groupName: "AI")
        {
        }

        protected override void RunCommand(String actionParameter)
        {
            // TODO: Layer 2 — WebSocket + AI API integration
            PluginLog.Info("AI Generate pressed — not yet implemented (Layer 2)");
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var builder = new BitmapBuilder(imageSize);
            builder.Clear(new BitmapColor(100, 0, 180));
            builder.DrawText("AI\nGenerate", color: BitmapColor.White);
            return builder.ToImage();
        }
    }
}
