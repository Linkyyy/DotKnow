using System.Net;
using System.Text;
using System.Text.Json;
using TypeLingo.Core;
using TypeLingo.Infrastructure;

namespace TypeLingo.Tests;

public sealed class MultiProviderTests
{
    public static TheoryData<string> Providers => new() { "DeepSeek", "OpenAI", "Anthropic", "Gemini", "Qwen" };
    private static string Response(string provider) => provider switch
    {
        "Anthropic" => """{"stop_reason":"end_turn","content":[{"type":"thinking","thinking":"不得显示的思考"},{"type":"text","text":"译文"}],"usage":{"input_tokens":11,"output_tokens":2}}""",
        "Gemini" => """{"candidates":[{"finishReason":"STOP","content":{"parts":[{"thought":true,"text":"不得显示的思考"},{"text":"译文"}]}}],"usageMetadata":{"promptTokenCount":11,"candidatesTokenCount":2}}""",
        _ => """{"choices":[{"finish_reason":"stop","message":{"content":"译文"}}],"usage":{"prompt_tokens":11,"completion_tokens":2}}"""
    };
    [Theory][MemberData(nameof(Providers))]
    public async Task EachProvider_UsesItsProtocolAndAuth_WithoutLeakingKeysInUrls(string provider)
    {
        var config = CoordinatorTests.Config with { AI = ProviderCatalog.Defaults(provider) };
        using var handler = new Handler(async request =>
        {
            Assert.DoesNotContain("test-key", request.RequestUri!.ToString());
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()); var body = doc.RootElement;
            if (provider == "Anthropic")
            {
                Assert.EndsWith("/v1/messages", request.RequestUri.AbsolutePath); Assert.Equal("test-key", request.Headers.GetValues("x-api-key").Single());
                Assert.Equal("2023-06-01", request.Headers.GetValues("anthropic-version").Single());
                Assert.Contains("Target language: ja", body.GetProperty("system").GetString());
                Assert.Equal("原文", body.GetProperty("messages")[0].GetProperty("content").GetString());
            }
            else if (provider == "Gemini")
            {
                Assert.EndsWith(":generateContent", request.RequestUri.AbsolutePath); Assert.Equal("test-key", request.Headers.GetValues("x-goog-api-key").Single());
                Assert.Contains("Target language: ja", body.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
                Assert.Equal("原文", body.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString());
            }
            else
            {
                Assert.EndsWith("/chat/completions", request.RequestUri.AbsolutePath); Assert.Equal("test-key", request.Headers.Authorization!.Parameter);
                Assert.False(body.GetProperty("stream").GetBoolean()); Assert.Equal("原文", body.GetProperty("messages")[1].GetProperty("content").GetString());
                Assert.Equal(provider == "DeepSeek", body.TryGetProperty("thinking", out _));
                if (provider == "Qwen") Assert.False(body.GetProperty("enable_thinking").GetBoolean());
            }
            return Json(Response(provider));
        });
        using var http = new HttpClient(handler); var statistics = new SessionStatistics();
        var api = new ProviderApi(http, () => config, ai => { Assert.Equal(provider, ai.Provider); return "test-key"; }, statistics);
        var result = await api.TranslateAsync(TranslationRequest.Create(config, "原文"), default);
        Assert.Equal("译文", result.Text); Assert.Equal(11, result.PromptTokens); Assert.Equal(2, result.CompletionTokens);
        Assert.Equal(1, statistics.ApiCalls); Assert.Equal(1, statistics.Successes);
    }
    [Theory][MemberData(nameof(Providers))]
    public async Task EachProvider_AuthFailureIsNotRetried(string provider)
    {
        var config = CoordinatorTests.Config with { AI = ProviderCatalog.Defaults(provider) };
        using var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("敏感错误正文") }));
        using var http = new HttpClient(handler); var api = new ProviderApi(http, () => config, _ => "test-key", new());
        var error = await Assert.ThrowsAsync<TranslationException>(() => api.TranslateAsync(TranslationRequest.Create(config, "原文"), default));
        Assert.Equal(TranslationError.Authentication, error.Kind); Assert.Equal(1, handler.Calls); Assert.DoesNotContain("敏感", error.Message);
    }
    [Theory][MemberData(nameof(Providers))]
    public async Task ModelLists_RespectProviderResponseShape(string provider)
    {
        var config = CoordinatorTests.Config with { AI = ProviderCatalog.Defaults(provider) };
        using var handler = new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method); Assert.EndsWith("/models", request.RequestUri!.AbsolutePath);
            return Task.FromResult(Json(provider == "Gemini"
                ? """{"models":[{"name":"models/selected-model","supportedGenerationMethods":["generateContent"]},{"name":"models/embedding","supportedGenerationMethods":["embedContent"]}]}"""
                : """{"data":[{"id":"selected-model"}]}"""));
        });
        using var http = new HttpClient(handler); var api = new ProviderApi(http, () => config, _ => "test-key", new());
        Assert.Equal(["selected-model"], await api.GetModelsAsync());
    }
    [Theory][InlineData("Anthropic")][InlineData("Gemini")]
    public async Task NativeProtocols_RejectTruncatedOrBlockedOutputs(string provider)
    {
        var config = CoordinatorTests.Config with { AI = ProviderCatalog.Defaults(provider) };
        var text = Response(provider).Replace("end_turn", "max_tokens").Replace("STOP", "SAFETY");
        using var handler = new Handler(_ => Task.FromResult(Json(text))); using var http = new HttpClient(handler);
        var api = new ProviderApi(http, () => config, _ => "test-key", new());
        var error = await Assert.ThrowsAsync<TranslationException>(() => api.TranslateAsync(TranslationRequest.Create(config, "原文"), default));
        Assert.Equal(TranslationError.InvalidResponse, error.Kind);
    }
    [Fact] public async Task SelectedModel_IsSentUnchanged()
    {
        var config = CoordinatorTests.Config with { AI = ProviderCatalog.Defaults("OpenAI") with { Model = "my-selected-model" } };
        using var handler = new Handler(async request =>
        {
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()); Assert.Equal("my-selected-model", doc.RootElement.GetProperty("model").GetString());
            return Json(Response("OpenAI"));
        });
        using var http = new HttpClient(handler); var api = new ProviderApi(http, () => config, _ => "test-key", new());
        await api.TranslateAsync(TranslationRequest.Create(config, "原文"), default);
    }
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return action(request); }
    }
}
