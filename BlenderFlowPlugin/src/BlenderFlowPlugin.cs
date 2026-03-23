namespace Loupedeck.BlenderFlowPlugin
{
    using System;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Loupedeck.BlenderFlowPlugin.Services;

    public class BlenderFlowPlugin : Plugin
    {
        public override Boolean UsesApplicationApiOnly => false;
        public override Boolean HasNoApplication => false;

        public BlenderConnection BlenderConnection { get; private set; }
        public AIService AIService { get; private set; }

        // Current Blender state (updated via WebSocket)
        public new String CurrentMode { get; private set; } = "OBJECT";
        public String ActiveObject { get; private set; }

        public event Action OnBlenderStateChanged;

        public BlenderFlowPlugin()
        {
            PluginLog.Init(this.Log);
            PluginResources.Init(this.Assembly);
        }

        public override void Load()
        {
            BlenderConnection = new BlenderConnection();
            AIService = new AIService();

            BlenderConnection.OnMessageReceived += HandleBlenderMessage;
            BlenderConnection.OnConnected += () => PluginLog.Info("Blender connected");
            BlenderConnection.OnDisconnected += () => PluginLog.Info("Blender disconnected");

            // Start connection attempt
            Task.Run(async () => await BlenderConnection.ConnectAsync());

            PluginLog.Info("BlenderFlow AI plugin loaded");
        }

        public override void Unload()
        {
            BlenderConnection?.Dispose();
            PluginLog.Info("BlenderFlow AI plugin unloaded");
        }

        private void HandleBlenderMessage(String rawMessage)
        {
            try
            {
                using var doc = JsonDocument.Parse(rawMessage);
                var type = doc.RootElement.GetProperty("type").GetString();

                switch (type)
                {
                    case "mode_changed":
                        CurrentMode = doc.RootElement.GetProperty("mode").GetString();
                        OnBlenderStateChanged?.Invoke();
                        break;

                    case "state":
                        CurrentMode = doc.RootElement.GetProperty("mode").GetString();
                        if (doc.RootElement.TryGetProperty("active_object", out var obj))
                        {
                            ActiveObject = obj.GetString();
                        }
                        OnBlenderStateChanged?.Invoke();
                        break;

                    case "ai_prompt_response":
                        var prompt = doc.RootElement.GetProperty("prompt").GetString();
                        HandleAiPrompt(prompt);
                        break;

                    case "ai_prompt_cancelled":
                        PluginLog.Info("AI prompt cancelled by user");
                        OnBlenderStateChanged?.Invoke(); // Signals AIGenerateCommand to reset
                        break;

                    case "error":
                        var code = doc.RootElement.GetProperty("code").GetString();
                        var message = doc.RootElement.GetProperty("message").GetString();
                        PluginLog.Warning($"Blender error: {code} - {message}");
                        break;
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"Message parse error: {ex.Message}");
            }
        }

        private void HandleAiPrompt(String prompt)
        {
            PluginLog.Info($"AI generating: {prompt}");

            // Use a local handler to avoid accumulating persistent event subscriptions
            Action<String> onCompleted = null;
            onCompleted = async (path) =>
            {
                AIService.OnCompleted -= onCompleted;
                var format = path.EndsWith(".glb") || path.EndsWith(".gltf") ? "gltf" : "obj";
                await BlenderConnection.SendImportModelAsync(path, format);
            };

            Action<String> onFailed = null;
            onFailed = (error) =>
            {
                AIService.OnCompleted -= onCompleted;
                AIService.OnFailed -= onFailed;
            };

            AIService.OnCompleted += onCompleted;
            AIService.OnFailed += onFailed;

            Task.Run(async () => await AIService.GenerateModelAsync(prompt));
        }
    }
}
