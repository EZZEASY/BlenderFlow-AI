namespace Loupedeck.BlenderFlowPlugin.Services
{
    using System;
    using System.IO;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    public class AIService : IDisposable
    {
        // Shared across all plugin instances: HttpClient is documented to be reused.
        // A per-call timeout lives on the request's CancellationToken, not here.
        private static readonly HttpClient _http = new HttpClient();

        private readonly String _tempDir;
        private readonly Object _jobLock = new Object();
        private CancellationTokenSource _currentJob;
        private Boolean _disposed;

        // TODO: Replace with actual API key management
        // For hackathon: set TRIPO_API_KEY environment variable
        private String ApiKey => Environment.GetEnvironmentVariable("TRIPO_API_KEY") ?? "";

        public String Status { get; private set; } = "idle";
        public Int32 Progress { get; private set; } = 0;

        public event Action<String, Int32> OnProgressChanged; // status, percent
        public event Action<String> OnCompleted; // model file path
        public event Action<String> OnFailed; // error message

        public AIService()
        {
            _tempDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "BlenderFlow", "temp");

            Directory.CreateDirectory(_tempDir);
            CleanOldFiles();
        }

        /// <summary>Cancel any in-flight generation. Safe to call repeatedly.</summary>
        public void Cancel()
        {
            CancellationTokenSource cts;
            lock (_jobLock)
            {
                cts = _currentJob;
                _currentJob = null;
            }
            if (cts != null)
            {
                try { cts.Cancel(); } catch { }
                try { cts.Dispose(); } catch { }
            }
        }

        public async Task GenerateModelAsync(String prompt)
        {
            if (_disposed)
            {
                return;
            }

            if (String.IsNullOrEmpty(ApiKey))
            {
                OnFailed?.Invoke("API key not set. Set TRIPO_API_KEY environment variable.");
                return;
            }

            // Replace any in-flight job so we never have two concurrent generations.
            CancellationTokenSource cts;
            lock (_jobLock)
            {
                _currentJob?.Cancel();
                _currentJob?.Dispose();
                cts = _currentJob = new CancellationTokenSource();
            }
            var token = cts.Token;

            Status = "submitting";
            Progress = 0;
            OnProgressChanged?.Invoke(Status, Progress);

            try
            {
                // Step 1: Create task
                var taskId = await CreateTaskAsync(prompt, token);
                if (String.IsNullOrEmpty(taskId))
                {
                    OnFailed?.Invoke("Failed to create AI generation task");
                    return;
                }

                PluginLog.Info($"AI task created: {taskId}");

                // Step 2: Poll for completion
                Status = "generating";
                var modelUrl = await PollTaskAsync(taskId, token);
                if (String.IsNullOrEmpty(modelUrl))
                {
                    OnFailed?.Invoke("AI generation timed out or failed");
                    return;
                }

                // Step 3: Download model
                Status = "downloading";
                Progress = 90;
                OnProgressChanged?.Invoke(Status, Progress);

                var localPath = await DownloadModelAsync(modelUrl, taskId, token);
                if (String.IsNullOrEmpty(localPath))
                {
                    OnFailed?.Invoke("Failed to download generated model");
                    return;
                }

                Status = "idle";
                Progress = 100;
                OnProgressChanged?.Invoke(Status, Progress);
                OnCompleted?.Invoke(localPath);
                PluginLog.Info($"AI model downloaded: {localPath}");
            }
            catch (OperationCanceledException)
            {
                Status = "idle";
                Progress = 0;
                OnProgressChanged?.Invoke(Status, Progress);
                PluginLog.Info("AI generation cancelled");
            }
            catch (Exception ex)
            {
                Status = "idle";
                Progress = 0;
                OnFailed?.Invoke($"AI error: {ex.Message}");
                PluginLog.Error(ex, "AI generation error");
            }
            finally
            {
                lock (_jobLock)
                {
                    if (_currentJob == cts)
                    {
                        _currentJob = null;
                    }
                }
                try { cts.Dispose(); } catch { }
            }
        }

        private async Task<String> CreateTaskAsync(String prompt, CancellationToken token)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tripo3d.ai/v2/openapi/task");
            request.Headers.Add("Authorization", $"Bearer {ApiKey}");
            request.Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    type = "text_to_model",
                    prompt = prompt
                }),
                Encoding.UTF8,
                "application/json");

            var response = await _http.SendAsync(request, token);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                PluginLog.Error($"AI API error: {response.StatusCode} - {body}");
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            return doc.RootElement
                .GetProperty("data")
                .GetProperty("task_id")
                .GetString();
        }

        private async Task<String> PollTaskAsync(String taskId, CancellationToken token)
        {
            var maxAttempts = 60; // 2s * 60 = 120s timeout
            var retryCount = 0;

            for (var i = 0; i < maxAttempts; i++)
            {
                await Task.Delay(2000, token);

                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Get,
                        $"https://api.tripo3d.ai/v2/openapi/task/{taskId}");
                    request.Headers.Add("Authorization", $"Bearer {ApiKey}");

                    var response = await _http.SendAsync(request, token);

                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        retryCount++;
                        if (retryCount > 3)
                        {
                            return null;
                        }

                        await Task.Delay(5000, token);
                        continue;
                    }

                    var body = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(body);
                    var data = doc.RootElement.GetProperty("data");
                    var status = data.GetProperty("status").GetString();
                    var progress = data.TryGetProperty("progress", out var p) ? p.GetInt32() : 0;

                    Progress = Math.Clamp(progress, 0, 90);
                    OnProgressChanged?.Invoke("generating", Progress);

                    if (status == "success")
                    {
                        var output = data.GetProperty("output");
                        if (output.TryGetProperty("model", out var model))
                        {
                            return model.GetString();
                        }
                        // Try pbr_model for textured output
                        if (output.TryGetProperty("pbr_model", out var pbrModel))
                        {
                            return pbrModel.GetString();
                        }
                        return null;
                    }

                    if (status == "failed")
                    {
                        return null;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    PluginLog.Warning(ex, $"AI poll error (task {taskId})");
                }
            }

            return null; // timeout
        }

        private async Task<String> DownloadModelAsync(String url, String taskId, CancellationToken token)
        {
            String filePath = null;
            try
            {
                var response = await _http.GetAsync(url, token);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var ext = url.Contains(".glb") ? ".glb" : url.Contains(".obj") ? ".obj" : ".glb";
                var fileName = $"blenderflow_{taskId}{ext}";
                filePath = Path.Combine(_tempDir, fileName);

                // Stream directly to disk so partial bodies don't sit in memory, and so
                // we can delete the half-written file on cancel/failure.
                using (var source = await response.Content.ReadAsStreamAsync())
                using (var dest = File.Create(filePath))
                {
                    await source.CopyToAsync(dest, 81920, token);
                }
                return filePath;
            }
            catch (OperationCanceledException)
            {
                TryDelete(filePath);
                throw;
            }
            catch (Exception ex)
            {
                TryDelete(filePath);
                PluginLog.Error(ex, $"AI download error (task {taskId})");
                return null;
            }
        }

        private static void TryDelete(String path)
        {
            if (String.IsNullOrEmpty(path)) return;
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private void CleanOldFiles()
        {
            try
            {
                if (!Directory.Exists(_tempDir))
                {
                    return;
                }

                // LastWriteTime is populated by the OS on every platform we target;
                // CreationTime is unreliable on APFS/ext4 and can return epoch.
                var cutoff = DateTime.Now.AddHours(-1);
                foreach (var file in Directory.GetFiles(_tempDir))
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "AI temp cleanup failed");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Cancel();
            // HttpClient is static — do NOT dispose it.
        }
    }
}
