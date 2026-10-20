using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TypeLingo.Core;

namespace TypeLingo.Infrastructure;

// 五家服务共用取消、重试与统计；Claude 和 Gemini 使用各自的原生请求及响应协议。
public sealed class ProviderApi(HttpClient client, Func<AppSettings> settings,
    Func<AiSettings, string> getApiKey, SessionStatistics statistics) : ITranslationProvider
{
    public async Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        var config = settings() with { AI = settings().AI with { Provider = request.Provider, Model = request.Model, BaseUrl = request.BaseUrl } };
        config.Validate(); var watch = Stopwatch.StartNew();
        var body = CreateBody(request);
        var path = request.Provider switch
        {
            "Anthropic" => "messages",
            "Gemini" => "models/" + Uri.EscapeDataString(GeminiId(request.Model)) + ":generateContent",
            _ => "chat/completions"
        };
        try
        {
            using var response = await SendAsync(path, () => JsonContent.Create(body), config.AI, cancellationToken);
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var result = ParseResult(document.RootElement, request.Provider);
            if (string.IsNullOrWhiteSpace(result.Text)) throw new JsonException();
            statistics.Succeeded(watch.Elapsed, result); return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException)
        { statistics.Failed(); throw new TranslationException(TranslationError.InvalidResponse, "API 返回格式无效，未显示不完整译文。"); }
        catch { statistics.Failed(); throw; }
    }

    private static object CreateBody(TranslationRequest request)
    {
        var prompt = TranslationPrompt.Build(request);
        if (request.Provider == "Anthropic") return new
        {
            model = request.Model, max_tokens = 4096, stream = false, system = prompt,
            messages = new[] { new { role = "user", content = TranslationPrompt.UserContent(request) } }
        };
        if (request.Provider == "Gemini")
        {
            var generation = new Dictionary<string, object> { ["maxOutputTokens"] = 8192 };
            // 仅对明确支持关闭思考的 Flash 2.5 模型设置思考预算，避免向其他模型发送不支持的参数。
            if (GeminiId(request.Model).StartsWith("gemini-2.5-flash", StringComparison.Ordinal)) generation["thinkingConfig"] = new { thinkingBudget = 0 };
            return new
            {
                systemInstruction = new { parts = new[] { new { text = prompt } } },
                contents = new[] { new { role = "user", parts = new[] { new { text = TranslationPrompt.UserContent(request) } } } },
                generationConfig = generation
            };
        }
        var body = new Dictionary<string, object>
        {
            ["model"] = request.Model, ["stream"] = false,
            ["messages"] = new[] { new { role = "system", content = prompt }, new { role = "user", content = TranslationPrompt.UserContent(request) } }
        };
        if (request.Provider == "DeepSeek") body["thinking"] = new { type = "disabled" };
        if (request.Provider == "Qwen") body["enable_thinking"] = false;
        return body;
    }

    private static TranslationResult ParseResult(JsonElement root, string provider)
    {
        static int Read(JsonElement obj, string name) => obj.TryGetProperty(name, out var item) && item.TryGetInt32(out var value) ? value : 0;
        static void Complete(string? reason, string expected)
        { if (reason != expected) throw new TranslationException(TranslationError.InvalidResponse, "译文不完整或被服务拒绝，未用于替换。"); }
        if (provider == "Anthropic")
        {
            Complete(root.GetProperty("stop_reason").GetString(), "end_turn");
            var text = string.Concat(root.GetProperty("content").EnumerateArray().Where(x => x.GetProperty("type").GetString() == "text").Select(x => x.GetProperty("text").GetString()));
            root.TryGetProperty("usage", out var usage);
            return new(text, usage.ValueKind == JsonValueKind.Object ? Read(usage, "input_tokens") + Read(usage, "cache_read_input_tokens") + Read(usage, "cache_creation_input_tokens") : 0,
                usage.ValueKind == JsonValueKind.Object ? Read(usage, "output_tokens") : 0);
        }
        if (provider == "Gemini")
        {
            var candidate = root.GetProperty("candidates")[0]; Complete(candidate.GetProperty("finishReason").GetString(), "STOP");
            var text = string.Concat(candidate.GetProperty("content").GetProperty("parts").EnumerateArray()
                .Where(x => x.TryGetProperty("text", out _) && (!x.TryGetProperty("thought", out var thought) || !thought.GetBoolean()))
                .Select(x => x.GetProperty("text").GetString()));
            root.TryGetProperty("usageMetadata", out var usage);
            return new(text, usage.ValueKind == JsonValueKind.Object ? Read(usage, "promptTokenCount") : 0,
                usage.ValueKind == JsonValueKind.Object ? Read(usage, "candidatesTokenCount") : 0);
        }
        var choice = root.GetProperty("choices")[0]; Complete(choice.GetProperty("finish_reason").GetString(), "stop");
        root.TryGetProperty("usage", out var standardUsage);
        return new(choice.GetProperty("message").GetProperty("content").GetString()!,
            standardUsage.ValueKind == JsonValueKind.Object ? Read(standardUsage, "prompt_tokens") : 0,
            standardUsage.ValueKind == JsonValueKind.Object ? Read(standardUsage, "completion_tokens") : 0);
    }

    public async Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        var config = settings(); config.Validate(); var models = new List<string>(); string? next = "models";
        for (var page = 0; next != null && page < 10; page++)
        {
            using var response = await SendAsync(next, null, config.AI, cancellationToken);
            try
            {
                using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                var root = document.RootElement; next = null;
                if (config.AI.Provider == "Gemini")
                {
                    models.AddRange(root.GetProperty("models").EnumerateArray()
                        .Where(x => x.TryGetProperty("supportedGenerationMethods", out var methods) && methods.EnumerateArray().Any(m => m.GetString() == "generateContent"))
                        .Select(x => GeminiId(x.GetProperty("name").GetString()!)));
                    if (root.TryGetProperty("nextPageToken", out var token) && !string.IsNullOrWhiteSpace(token.GetString())) next = "models?pageToken=" + Uri.EscapeDataString(token.GetString()!);
                }
                else
                {
                    models.AddRange(root.GetProperty("data").EnumerateArray().Select(x => x.GetProperty("id").GetString()!));
                    if (config.AI.Provider == "Anthropic" && root.TryGetProperty("has_more", out var more) && more.GetBoolean() &&
                        root.TryGetProperty("last_id", out var cursor)) next = "models?after_id=" + Uri.EscapeDataString(cursor.GetString()!);
                }
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
            { throw new TranslationException(TranslationError.InvalidResponse, "模型列表格式无效。"); }
        }
        return models.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToArray();
    }

    private async Task<HttpResponseMessage> SendAsync(string path, Func<HttpContent>? body, AiSettings config, CancellationToken token)
    {
        var key = getApiKey(config);
        if (string.IsNullOrWhiteSpace(key)) throw new TranslationException(TranslationError.Authentication, "请为当前服务商和 API 地址保存密钥。");
        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds));
            using var message = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, config.BaseUrl.TrimEnd('/') + "/" + path);
            switch (config.Provider)
            {
                case "Anthropic": message.Headers.Add("x-api-key", key); message.Headers.Add("anthropic-version", "2023-06-01"); break;
                case "Gemini": message.Headers.Add("x-goog-api-key", key); break;
                default: message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key); break;
            }
            message.Content = body?.Invoke();
            try
            {
                statistics.ApiCall();
                // 响应体的缓冲读取也受本次尝试的超时控制，避免服务器只返回响应头后一直挂起。
                var response = await client.SendAsync(message, HttpCompletionOption.ResponseContentRead, timeout.Token);
                if (response.IsSuccessStatusCode) return response;
                var status = response.StatusCode;
                var transient = status == HttpStatusCode.TooManyRequests || (int)status >= 500;
                var retryAfter = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.Zero);
                response.Dispose();
                if (transient && attempt < config.MaxRetries)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Clamp(Math.Max(retryAfter.TotalMilliseconds, 400 * Math.Pow(2, attempt) + Random.Shared.Next(100)), 100, 10000)), token);
                    continue;
                }
                throw status switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new TranslationException(TranslationError.Authentication, "API 认证失败，请检查 Key 与账户权限。"),
                    HttpStatusCode.TooManyRequests => new TranslationException(TranslationError.RateLimit, "API 请求受限，请稍后重试。"),
                    _ when (int)status >= 500 => new TranslationException(TranslationError.Server, "翻译服务暂时不可用。"),
                    _ => new TranslationException(TranslationError.Configuration, $"API 拒绝请求（HTTP {(int)status}），请检查模型、地址和账户余额。")
                };
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            { throw new TranslationException(TranslationError.Timeout, "翻译请求超时，请稍后重试。"); }
            catch (HttpRequestException)
            { throw new TranslationException(TranslationError.Network, "无法连接 API，请检查网络与 HTTPS 证书。"); }
        }
    }
    private static string GeminiId(string name) => name.StartsWith("models/", StringComparison.Ordinal) ? name[7..] : name;
}
