namespace Loupedeck.BlenderFlowPlugin
{
    using System;
    using System.Threading.Tasks;
    using Loupedeck.BlenderFlowPlugin.Services;

    public class AIGenerateCommand : PluginDynamicCommand
    {
        private String _status = "idle";
        private Int32 _progressPercent = 0;

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

            // Tripo uses the C#-side AIService; Python-side providers
            // (Hunyuan3D) stream progress over WS instead. Subscribe to both
            // sources so the console icon reflects whichever path runs.
            plugin.AIService.OnProgressChanged += OnProgressChanged;
            plugin.AIService.OnCompleted += OnCompleted;
            plugin.AIService.OnFailed += OnFailed;
            plugin.OnAiProgressChanged += OnPythonProgressChanged;
            plugin.OnAiCompleted += OnPythonCompleted;
            plugin.OnAiFailed += OnFailed;

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
            _progressPercent = percent;
            this.ActionImageChanged();
        }

        // Python-side providers emit a (state, 0..1) progress tuple.
        private void OnPythonProgressChanged(String state, Single progress01)
        {
            _progressPercent = (Int32)Math.Round(Math.Clamp(progress01, 0f, 1f) * 100f);
            _status = state switch
            {
                "submitted" => "submitting",
                "done"      => "downloading",
                "failed"    => "failed",
                _           => "generating",
            };
            this.ActionImageChanged();
        }

        private void OnPythonCompleted(String objectName)
        {
            _status = "idle";
            _progressPercent = 0;
            UnsubscribeEvents();
            this.ActionImageChanged();
        }

        private void OnCompleted(String path)
        {
            _status = "idle";
            _progressPercent = 0;
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
            plugin.OnAiProgressChanged -= OnPythonProgressChanged;
            plugin.OnAiCompleted -= OnPythonCompleted;
            plugin.OnAiFailed -= OnFailed;
            plugin.OnAiPromptCancelled -= OnPromptCancelled;
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            using var builder = new BitmapBuilder(imageSize);
            Int32 w = imageSize.GetWidth();
            Int32 h = imageSize.GetHeight();
            Single cx = w / 2f, cy = h / 2f;
            Single cubeR = Math.Min(w, h) * 0.22f;
            Single thick = Math.Max(1.8f, Math.Min(w, h) / 32f);

            switch (_status)
            {
                case "waiting":
                    builder.Clear(BlenderTheme.AiPurpleTint);
                    BlenderIcons.IsoCube(builder, cx, cy, cubeR, BlenderTheme.IconDim, thick);
                    BlenderIcons.Sparkle(builder, w * 0.22f, h * 0.26f,
                        Math.Min(w, h) * 0.09f, BlenderTheme.AiPurple);
                    BlenderIcons.Sparkle(builder, w * 0.80f, h * 0.78f,
                        Math.Min(w, h) * 0.07f, BlenderTheme.AiPurple);
                    break;

                case "submitting":
                case "generating":
                case "downloading":
                    {
                        builder.Clear(BlenderTheme.AiPurpleTint);
                        var plugin = (BlenderFlowPlugin)this.Plugin;
                        // Prefer the per-event _progressPercent so Python-side
                        // providers render too; fall back to AIService.Progress
                        // for the Tripo code path, and treat 'downloading' as
                        // ~90% if nothing reported.
                        var pct = _progressPercent;
                        if (pct == 0)
                        {
                            pct = _status == "downloading"
                                ? 90
                                : (plugin?.AIService?.Progress ?? 0);
                        }
                        Int32 ringR = (Int32)(Math.Min(w, h) * 0.38f);
                        BlenderIcons.ProgressRing(builder, (Int32)cx, (Int32)cy, ringR, pct,
                            BlenderTheme.PanelBgDim, BlenderTheme.AiPurple, thick * 1.2f);
                        BlenderIcons.IsoCube(builder, cx, cy, cubeR * 0.80f,
                            BlenderTheme.Icon, thick);
                        builder.DrawText($"{pct}%", 0, (Int32)(h * 0.74f), w, (Int32)(h * 0.22f),
                            BlenderTheme.IconBright, 0, 0, 0, null);
                    }
                    break;

                case "failed":
                    builder.Clear(BlenderTheme.PanelBg);
                    {
                        Single r = Math.Min(w, h) * 0.24f;
                        Single xt = Math.Max(2f, Math.Min(w, h) / 22f);
                        builder.DrawLine(cx - r, cy - r, cx + r, cy + r, BlenderTheme.Danger, xt);
                        builder.DrawLine(cx - r, cy + r, cx + r, cy - r, BlenderTheme.Danger, xt);
                    }
                    break;

                default: // idle
                    builder.Clear(BlenderTheme.AiPurpleTint);
                    BlenderIcons.IsoCube(builder, cx, cy, cubeR, BlenderTheme.Icon, thick);
                    BlenderIcons.Sparkle(builder, w * 0.22f, h * 0.26f,
                        Math.Min(w, h) * 0.10f, BlenderTheme.AiPurple);
                    BlenderIcons.Sparkle(builder, w * 0.80f, h * 0.78f,
                        Math.Min(w, h) * 0.08f, BlenderTheme.AiPurple);
                    break;
            }

            return builder.ToImage();
        }
    }
}
