namespace Loupedeck.BlenderFlowPlugin
{
    using System;
    using System.Threading.Tasks;
    using Loupedeck.BlenderFlowPlugin.Services;

    public class AIGenerateCommand : PluginDynamicCommand
    {
        private String _status = "idle";

        public AIGenerateCommand()
            : base(
                displayName: "AI Generate",
                description: "Generate 3D model with AI (long-press to cancel)",
                groupName: "AI")
        {
        }

        protected override void RunCommand(String actionParameter)
        {
            var plugin = (BlenderFlowPlugin)this.Plugin;

            // If a job is in flight, a tap cancels it rather than queuing another.
            if (_status == "waiting" || _status == "submitting" ||
                _status == "generating" || _status == "downloading")
            {
                plugin.AIService?.Cancel();
                _status = "idle";
                UnsubscribeEvents();
                this.ActionImageChanged();
                PluginLog.Info("AI Generate: cancelled by user");
                return;
            }

            if (!plugin.BlenderConnection.IsConnected)
            {
                PluginLog.Warning("AI Generate: Blender not connected");
                return;
            }

            // Subscribe to AI events
            plugin.AIService.OnProgressChanged += OnProgressChanged;
            plugin.AIService.OnCompleted += OnCompleted;
            plugin.AIService.OnFailed += OnFailed;

            // Listen for prompt dialog dismissal — narrower than OnBlenderStateChanged
            // so unrelated mode changes during waiting don't abort the wait.
            plugin.OnAiPromptCancelled += OnPromptCancelled;

            // Send prompt request to Blender (shows dialog)
            Task.Run(async () => await plugin.BlenderConnection.SendAiPromptRequestAsync());

            _status = "waiting";
            this.ActionImageChanged();
            PluginLog.Info("AI Generate: prompt dialog requested");
        }

        private void OnPromptCancelled()
        {
            if (_status == "waiting")
            {
                _status = "idle";
                UnsubscribeEvents();
                this.ActionImageChanged();
            }
        }

        private void OnProgressChanged(String status, Int32 percent)
        {
            _status = status;
            this.ActionImageChanged();
        }

        private void OnCompleted(String path)
        {
            _status = "idle";
            UnsubscribeEvents();
            this.ActionImageChanged();
        }

        private void OnFailed(String error)
        {
            _status = "failed";
            this.ActionImageChanged();
            PluginLog.Warning($"AI Generate failed: {error}");

            // Reset to idle after 3 seconds
            Task.Run(async () =>
            {
                await Task.Delay(3000);
                _status = "idle";
                UnsubscribeEvents();
                this.ActionImageChanged();
            });
        }

        private void UnsubscribeEvents()
        {
            var plugin = (BlenderFlowPlugin)this.Plugin;
            if (plugin == null) return;
            if (plugin.AIService != null)
            {
                plugin.AIService.OnProgressChanged -= OnProgressChanged;
                plugin.AIService.OnCompleted -= OnCompleted;
                plugin.AIService.OnFailed -= OnFailed;
            }
            plugin.OnAiPromptCancelled -= OnPromptCancelled;
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            using var builder = new BitmapBuilder(imageSize);

            switch (_status)
            {
                case "idle":
                    builder.Clear(new BitmapColor(100, 0, 180));
                    builder.DrawText("AI\nGenerate", color: BitmapColor.White);
                    break;
                case "waiting":
                    builder.Clear(new BitmapColor(80, 80, 0));
                    builder.DrawText("Waiting\n...", color: BitmapColor.White);
                    break;
                case "submitting":
                case "generating":
                    var plugin = (BlenderFlowPlugin)this.Plugin;
                    var pct = plugin?.AIService?.Progress ?? 0;
                    builder.Clear(new BitmapColor(0, 100, 180));
                    builder.DrawText($"AI\n{pct}%", color: BitmapColor.White);
                    break;
                case "downloading":
                    builder.Clear(new BitmapColor(0, 140, 80));
                    builder.DrawText("AI\n90%", color: BitmapColor.White);
                    break;
                case "failed":
                    builder.Clear(new BitmapColor(200, 0, 0));
                    builder.DrawText("Failed", color: BitmapColor.White);
                    break;
                default:
                    builder.Clear(new BitmapColor(100, 0, 180));
                    builder.DrawText("AI\nGenerate", color: BitmapColor.White);
                    break;
            }

            return builder.ToImage();
        }
    }
}
