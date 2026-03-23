namespace Loupedeck.BlenderFlowPlugin.Services
{
    using System;
    using System.IO;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;

    public class AIService
    {
        private readonly HttpClient _http = new HttpClient();
        private readonly String _tempDir;

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

        public async Task GenerateModelAsync(String prompt)
        {
            if (String.IsNullOrEmpty(ApiKey))
            {
                OnFailed?.Invoke("API key not set. Set TRIPO_API_KEY environment variable.");
                return;
            }

            Status = "submitting";
            Progress = 0;
            OnProgressChanged?.Invoke(Status, Progress);

            try
            {
                // Step 1: Create task
                var taskId = await CreateTaskAsync(prompt);
                if (String.IsNullOrEmpty(taskId))
                {
                    OnFailed?.Invoke("Failed to create AI generation task");
                    return;
                }

                PluginLog.Info($"AI task created: {taskId}");

                // Step 2: Poll for completion
                Status = "generating";
                var modelUrl = await PollTaskAsync(taskId);
                if (String.IsNullOrEmpty(modelUrl))
                {
                    OnFailed?.Invoke("AI generation timed out or failed");
                    return;
                }

                // Step 3: Download model
                Status = "downloading";
                Progress = 90;
                OnProgressChanged?.Invoke(Status, Progress);

                var localPath = await DownloadModelAsync(modelUrl, taskId);
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
            catch (Exception ex)
            {
                Status = "idle";
                Progress = 0;
                OnFailed?.Invoke($"AI error: {ex.Message}");
                PluginLog.Error($"AI generation error: {ex.Message}");
            }
        }

        private async Task<String> CreateTaskAsync(String prompt)
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

            var response = await _http.SendAsync(request);
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

        private async Task<String> PollTaskAsync(String taskId)
        {
            var maxAttempts = 60; // 2s * 60 = 120s timeout
            var retryCount = 0;

            for (var i = 0; i < maxAttempts; i++)
            {
                await Task.Delay(2000);

                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Get,
                        $"https://api.tripo3d.ai/v2/openapi/task/{taskId}");
                    request.Headers.Add("Authorization", $"Bearer {ApiKey}");

                    var response = await _http.SendAsync(request);

                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        retryCount++;
                        if (retryCount > 3)
                        {
                            return null;
                        }

                        await Task.Delay(5000);
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
                catch (Exception ex)
                {
                    PluginLog.Warning($"AI poll error: {ex.Message}");
                }
            }

            return null; // timeout
        }

        private async Task<String> DownloadModelAsync(String url, String taskId)
        {
            try
            {
                var response = await _http.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var ext = url.Contains(".glb") ? ".glb" : url.Contains(".obj") ? ".obj" : ".glb";
                var fileName = $"blenderflow_{taskId}{ext}";
                var filePath = Path.Combine(_tempDir, fileName);

                var data = await response.Content.ReadAsByteArrayAsync();
                await File.WriteAllBytesAsync(filePath, data);
                return filePath;
            }
            catch (Exception ex)
            {
                PluginLog.Error($"AI download error: {ex.Message}");
                return null;
            }
        }

        private void CleanOldFiles()
        {
            try
            {
                if (!Directory.Exists(_tempDir))
                {
                    return;
                }

                var cutoff = DateTime.Now.AddHours(-1);
                foreach (var file in Directory.GetFiles(_tempDir))
                {
                    if (File.GetCreationTime(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch { }
        }
    }
}
