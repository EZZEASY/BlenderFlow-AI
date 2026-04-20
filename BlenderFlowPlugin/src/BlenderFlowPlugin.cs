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

        /// <summary>Fires when the AI prompt dialog was dismissed in Blender.</summary>
        public event Action OnAiPromptCancelled;

        // Keep handler references so Unload can symmetrically unsubscribe.
        private Action<String> _onMessageHandler;
        private Action _onConnectedHandler;
        private Action _onDisconnectedHandler;

        public BlenderFlowPlugin()
        {
            PluginLog.Init(this.Log);
            PluginResources.Init(this.Assembly);
        }

        public override void Load()
        {
            BlenderConnection = new BlenderConnection();
            AIService = new AIService();

            // Register haptic events
            this.PluginEvents.AddEvent("aiGenerateComplete", "AI Complete", "AI model generation finished");
            this.PluginEvents.AddEvent("modeSwitch", "Mode Switch", "Blender mode changed");
            this.PluginEvents.AddEvent("aiGenerateFailed", "AI Failed", "AI generation error");

            _onMessageHandler = HandleBlenderMessage;
            _onConnectedHandler = () => PluginLog.Info("Blender connected");
            _onDisconnectedHandler = () => PluginLog.Info("Blender disconnected");

            BlenderConnection.OnMessageReceived += _onMessageHandler;
            BlenderConnection.OnConnected += _onConnectedHandler;
            BlenderConnection.OnDisconnected += _onDisconnectedHandler;

            // Start connection attempt
            Task.Run(async () => await BlenderConnection.ConnectAsync());

            PluginLog.Info("BlenderFlow AI plugin loaded");
        }

        public override void Unload()
        {
            // Symmetric cleanup — accumulated subscriptions cause duplicate
            // callbacks on Loupedeck plugin reload.
            if (BlenderConnection != null)
            {
                if (_onMessageHandler != null) BlenderConnection.OnMessageReceived -= _onMessageHandler;
                if (_onConnectedHandler != null) BlenderConnection.OnConnected -= _onConnectedHandler;
                if (_onDisconnectedHandler != null) BlenderConnection.OnDisconnected -= _onDisconnectedHandler;
            }

            try { AIService?.Dispose(); } catch { }
            try { BlenderConnection?.Dispose(); } catch { }

            PluginLog.Info("BlenderFlow AI plugin unloaded");
        }

        private void HandleBlenderMessage(String rawMessage)
        {
            try
            {
                using var doc = JsonDocument.Parse(rawMessage);
                if (!doc.RootElement.TryGetProperty("type", out var typeProp))
                {
                    return;
                }
                var type = typeProp.GetString();

                switch (type)
                {
                    case "mode_changed":
                        if (doc.RootElement.TryGetProperty("mode", out var modeProp))
                        {
                            CurrentMode = modeProp.GetString();
                        }
                        OnBlenderStateChanged?.Invoke();
                        this.PluginEvents.RaiseEvent("modeSwitch");
                        break;

                    case "state":
                        if (doc.RootElement.TryGetProperty("mode", out var stateMode))
                        {
                            CurrentMode = stateMode.GetString();
                        }
                        if (doc.RootElement.TryGetProperty("active_object", out var obj))
                        {
                            ActiveObject = obj.GetString();
                        }
                        OnBlenderStateChanged?.Invoke();
                        break;

                    case "ai_prompt_response":
                        if (doc.RootElement.TryGetProperty("prompt", out var promptProp))
                        {
                            HandleAiPrompt(promptProp.GetString());
                        }
                        break;

                    case "ai_prompt_cancelled":
                        PluginLog.Info("AI prompt cancelled by user");
                        OnAiPromptCancelled?.Invoke();
                        break;

                    case "error":
                        var code = doc.RootElement.TryGetProperty("code", out var c) ? c.GetString() : "?";
                        var message = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : "";
                        PluginLog.Warning($"Blender error: {code} - {message}");
                        break;
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "Message parse error");
            }
        }

        private void HandleAiPrompt(String prompt)
        {
            PluginLog.Info($"AI generating: {prompt}");

            // Use local handlers that remove BOTH subscriptions on either terminal event.
            // Interlocked guards against races where OnCompleted and OnFailed both fire.
            var settled = new Int32[1];
            Action<String> onCompleted = null;
            Action<String> onFailed = null;

            onCompleted = async (path) =>
            {
                if (System.Threading.Interlocked.Exchange(ref settled[0], 1) != 0) return;
                AIService.OnCompleted -= onCompleted;
                AIService.OnFailed -= onFailed;
                var format = path.EndsWith(".glb") || path.EndsWith(".gltf") ? "gltf" : "obj";
                await BlenderConnection.SendImportModelAsync(path, format);
                this.PluginEvents.RaiseEvent("aiGenerateComplete");
            };

            onFailed = (error) =>
            {
                if (System.Threading.Interlocked.Exchange(ref settled[0], 1) != 0) return;
                AIService.OnCompleted -= onCompleted;
                AIService.OnFailed -= onFailed;
                this.PluginEvents.RaiseEvent("aiGenerateFailed");
            };

            AIService.OnCompleted += onCompleted;
            AIService.OnFailed += onFailed;

            Task.Run(async () => await AIService.GenerateModelAsync(prompt));
        }
    }
}
