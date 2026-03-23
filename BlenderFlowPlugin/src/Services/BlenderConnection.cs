namespace Loupedeck.BlenderFlowPlugin.Services
{
    using System;
    using System.Net.WebSockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    public class BlenderConnection : IDisposable
    {
        private ClientWebSocket _ws;
        private readonly String _uri = "ws://localhost:9876";
        private Boolean _isConnected;
        private Boolean _disposed;
        private Int32 _failCount;
        private CancellationTokenSource _cts;

        public Boolean IsConnected => _isConnected;
        public Boolean IsDisconnected => _failCount >= 3;

        public event Action<String> OnMessageReceived;
        public event Action OnConnected;
        public event Action OnDisconnected;

        public async Task ConnectAsync()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                _ws?.Dispose();
                _ws = new ClientWebSocket();
                _cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _ws.ConnectAsync(new Uri(_uri), _cts.Token);
                _isConnected = true;
                _failCount = 0;
                OnConnected?.Invoke();
                PluginLog.Info("Connected to Blender WebSocket");

                // Start listening
                _ = Task.Run(() => ListenAsync());
            }
            catch (Exception ex)
            {
                _isConnected = false;
                _failCount++;
                if (_failCount >= 3)
                {
                    OnDisconnected?.Invoke();
                }

                PluginLog.Warning($"WebSocket connect failed (attempt {_failCount}): {ex.Message}");
            }
        }

        private async Task ListenAsync()
        {
            var buffer = new Byte[8192];
            try
            {
                while (_ws?.State == WebSocketState.Open && !_disposed)
                {
                    var result = await _ws.ReceiveAsync(
                        new ArraySegment<Byte>(buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    OnMessageReceived?.Invoke(message);
                }
            }
            catch (Exception)
            {
                // Connection lost
            }

            _isConnected = false;
            OnDisconnected?.Invoke();
            PluginLog.Info("WebSocket connection lost");

            // Auto-reconnect after 5 seconds
            if (!_disposed)
            {
                await Task.Delay(5000);
                await ConnectAsync();
            }
        }

        public async Task SendAsync(String type, Object payload = null)
        {
            if (!_isConnected || _ws?.State != WebSocketState.Open)
            {
                return;
            }

            try
            {
                var msg = new { type = type };
                String json;

                if (payload != null)
                {
                    // Merge type with payload properties
                    var dict = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<String, Object>>(
                        JsonSerializer.Serialize(payload));
                    dict["type"] = type;
                    json = JsonSerializer.Serialize(dict);
                }
                else
                {
                    json = JsonSerializer.Serialize(msg);
                }

                var bytes = Encoding.UTF8.GetBytes(json);
                await _ws.SendAsync(
                    new ArraySegment<Byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"WebSocket send error: {ex.Message}");
            }
        }

        public async Task SendSetModeAsync(String mode)
        {
            await SendAsync("set_mode", new { mode });
        }

        public async Task SendToolAsync(String op)
        {
            await SendAsync("tool", new { op });
        }

        public async Task SendGetStateAsync()
        {
            await SendAsync("get_state");
        }

        public async Task SendAiPromptRequestAsync()
        {
            await SendAsync("ai_prompt_request");
        }

        public async Task SendImportModelAsync(String path, String format)
        {
            await SendAsync("import_model", new { path, format });
        }

        public void Dispose()
        {
            _disposed = true;
            _cts?.Cancel();
            try
            {
                _ws?.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
            catch { }
            _ws?.Dispose();
            _isConnected = false;
        }
    }
}
