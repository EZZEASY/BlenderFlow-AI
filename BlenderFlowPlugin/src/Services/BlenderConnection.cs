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
        private CancellationTokenSource _lifetimeCts = new CancellationTokenSource();

        // ClientWebSocket does not allow concurrent SendAsync calls.
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

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

            // 5s connect timeout, linked to lifetime token so Dispose cancels in-flight connects
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
            connectCts.CancelAfter(TimeSpan.FromSeconds(5));

            try
            {
                _ws?.Dispose();
                _ws = new ClientWebSocket();
                await _ws.ConnectAsync(new Uri(_uri), connectCts.Token);
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
            var token = _lifetimeCts.Token;
            try
            {
                while (_ws?.State == WebSocketState.Open && !_disposed)
                {
                    var result = await _ws.ReceiveAsync(
                        new ArraySegment<Byte>(buffer), token);

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
                // Connection lost or cancelled
            }

            _isConnected = false;
            OnDisconnected?.Invoke();
            PluginLog.Info("WebSocket connection lost");

            // Persistent reconnect loop — keep trying every 5s until we
            // succeed or the plugin is unloaded. Without the loop, a single
            // failed retry (e.g. Blender still booting when we retry) would
            // leave us permanently stranded.
            while (!_disposed)
            {
                try
                {
                    await Task.Delay(5000, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                await ConnectAsync();
                if (_isConnected)
                {
                    return;
                }
            }
        }

        public async Task SendAsync(String type, Object payload = null)
        {
            if (_disposed || !_isConnected || _ws?.State != WebSocketState.Open)
            {
                return;
            }

            // Serialize outside the lock so the critical section is just the socket write.
            String json;
            try
            {
                if (payload != null)
                {
                    var dict = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<String, Object>>(
                        JsonSerializer.Serialize(payload));
                    dict["type"] = type;
                    json = JsonSerializer.Serialize(dict);
                }
                else
                {
                    json = JsonSerializer.Serialize(new { type = type });
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "WebSocket payload serialize error");
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(json);
            var token = _lifetimeCts.Token;

            if (!await _sendLock.WaitAsync(TimeSpan.FromSeconds(3), token).ConfigureAwait(false))
            {
                PluginLog.Warning("WebSocket send skipped: lock contention timeout");
                return;
            }
            try
            {
                // Re-check state after acquiring the lock — the socket may have closed while we waited.
                if (_ws?.State != WebSocketState.Open)
                {
                    return;
                }
                await _ws.SendAsync(
                    new ArraySegment<Byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    token);
            }
            catch (OperationCanceledException)
            {
                // Disposed / reconnecting
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "WebSocket send error");
            }
            finally
            {
                try { _sendLock.Release(); } catch { }
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

        public async Task SendBrushStrengthDeltaAsync(Single delta)
        {
            await SendAsync("brush_strength", new { delta });
        }

        public async Task SendBrushStrengthSetAsync(Single value)
        {
            await SendAsync("brush_strength", new { value });
        }

        // Menu-style shortcuts delivered as bpy.ops calls, because the
        // Logi SDK's keyboard injection drops modifier keys on macOS
        // (Blender's GHOST layer filters synthetic modifier events).
        public async Task SendUndoAsync() => await SendAsync("undo");
        public async Task SendRedoAsync() => await SendAsync("redo");
        public async Task SendSaveAsync() => await SendAsync("save");
        public async Task SendRenderImageAsync() => await SendAsync("render_image");

        public async Task SendViewportOrbitAsync(Single deltaDeg)
        {
            await SendAsync("viewport_orbit", new { delta_deg = deltaDeg });
        }

        public async Task SendViewportZoomAsync(Single factor)
        {
            await SendAsync("viewport_zoom", new { factor });
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _isConnected = false;

            // Cancel any in-flight connect / receive / reconnect-delay.
            try { _lifetimeCts.Cancel(); } catch { }

            // Best-effort graceful close with a hard timeout; fall back to Abort so
            // we never block the plugin host (Loupedeck) on a wedged socket.
            var ws = _ws;
            if (ws != null)
            {
                try
                {
                    using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    var closeTask = ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", closeCts.Token);
                    // Fire-and-forget; log if it fails later but don't block Dispose.
                    _ = closeTask.ContinueWith(
                        t => PluginLog.Warning($"WebSocket close error: {t.Exception?.GetBaseException().Message}"),
                        TaskContinuationOptions.OnlyOnFaulted);
                }
                catch { }

                try { ws.Abort(); } catch { }
                try { ws.Dispose(); } catch { }
            }

            try { _lifetimeCts.Dispose(); } catch { }
            try { _sendLock.Dispose(); } catch { }
        }
    }
}
