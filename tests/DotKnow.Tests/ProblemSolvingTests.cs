using System.Net;
using System.Text;
using System.Text.Json;
using DotKnow.Core;
using DotKnow.Infrastructure;

namespace DotKnow.Tests;

public sealed class ProblemSolvingTests
{
    private const string White = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGP4//8/AAX+Av4N70a4AAAAAElFTkSuQmCC";
    private const string Black = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGNgYGAAAAAEAAH2FzhVAAAAAElFTkSuQmCC";
    private static ImageAttachment Image(string data = White) => new(Convert.FromBase64String(data), 1, 1);
    private static AppSettings Config(string provider = "OpenAI") => CoordinatorTests.Config with
    { AI = ProviderCatalog.Defaults(provider) with { ImageModel = provider == "Qwen" ? "qwen-vl-plus" : "" } };
    private static InputSnapshot Snapshot(ImageAttachment image) => new("screenshot", image.Description, new(20, 30, 100, 80), CanWrite: false, IsSelection: true);

    [Fact] public void TextSolvingAndImages_AreSeparateFromTranslations_AndFromEachOther()
    {
        var settings = Config();
        Assert.Contains(AssistantPresets.All, x => x.Id == "solve" && x.Name == "解题");
        var text = AssistantPresets.CreateRequest(settings, "2 + 2 = ?", "solve");
        Assert.False(text.IsTranslation); Assert.Contains("final answer", text.Instructions);
        var image = Image();
        var requests = new[] { ProblemSolving.CreateRequest(settings, image), ProblemSolving.CreateRequest(settings, Image(Black)),
            ProblemSolving.CreateRequest(settings, image, "why?"),
            ProblemSolving.CreateRequest(settings with { AI = settings.AI with { ImageModel = "gpt-4.1" } }, image),
            AssistantPresets.CreateRequest(settings, image.Description, "solve") };
        Assert.Equal(requests.Length, requests.Select(x => x.CacheKey).Distinct().Count());
        Assert.All(requests, r => Assert.False(r.IsTranslation));
        Assert.Equal("solve", requests[0].TaskId); Assert.Same(image, requests[0].Image);
        Assert.DoesNotContain(White, requests[0].ToString());
        Assert.False((TranslationRequest.Create(settings, image.Description) with { Image = image }).IsTranslation);
    }
    [Theory]
    [InlineData("DeepSeek", "deepseek-flash")][InlineData("Qwen", "qwen-plus")][InlineData("OpenAI", "gpt-3.5-turbo")]
    public void KnownTextOnlyConfigurations_AreRejectedBeforeScreenshotSubmission(string provider, string model)
    {
        var config = Config(provider) with { AI = ProviderCatalog.Defaults(provider) with { Model = model } };
        Assert.NotNull(ProblemSolving.ConfigurationError(config.AI));
        Assert.Throws<ArgumentException>(() => ProblemSolving.CreateRequest(config, Image()));
        // Text solving still works with a text-only provider.
        Assert.Equal("solve", AssistantPresets.CreateRequest(config, "x + 1 = 2", "solve").TaskId);
    }
    [Fact] public void ImagesAreBounded_AndLegacySettingsAcquireNewDefaults()
    {
        Assert.Throws<ArgumentException>(() => new ImageAttachment(new byte[40], 1, 1));
        Assert.Throws<ArgumentException>(() => new ImageAttachment(Convert.FromBase64String(White), 2, 1));
        Assert.Throws<ArgumentException>(() => new ImageAttachment(new byte[ImageAttachment.MaxBytes + 1], 1, 1));
        var legacy = JsonSerializer.Deserialize<AppSettings>("""{"AI":{"Provider":"OpenAI","Model":"gpt-4.1-mini","BaseUrl":"https://api.openai.com/v1"},"Hotkeys":{"Preview":"Ctrl+Alt+T"}}""")!;
        legacy.Validate(); Assert.Equal("", legacy.AI.ImageModel); Assert.Equal("Ctrl+Alt+S", legacy.Hotkeys.Screenshot);
        Assert.Equal("gpt-4.1-mini", ProblemSolving.ImageModel(legacy.AI));
        Assert.Throws<ArgumentException>(() => (legacy with { AI = legacy.AI with { ImageModel = new string('x', 201) } }).Validate());
    }
    [Theory]
    [InlineData(-100, 200, -800, 50, -800, 50, 700, 150)]
    [InlineData(-3000, -500, 4000, 4000, -1920, -200, 3840, 1280)]
    [InlineData(100, 100, 300, 250, 100, 100, 200, 150)]
    public void PhysicalSelection_ClipsToVirtualDesktop_AndSupportsReversedDrags(double x1, double y1, double x2, double y2, double x, double y, double w, double h) =>
        Assert.Equal(new InputRect(x, y, w, h), ProblemSolving.Selection(x1, y1, x2, y2, new(-1920, -200, 3840, 1280)));
    [Fact] public void ClicksAndInvalidCoordinates_DoNotProduceACapture()
    {
        Assert.Null(ProblemSolving.Selection(20, 20, 22, 100, new(0, 0, 1920, 1080)));
        Assert.Null(ProblemSolving.Selection(double.NaN, 20, 100, 100, new(0, 0, 1920, 1080)));
    }
    [Theory][InlineData("OpenAI")][InlineData("Qwen")][InlineData("Anthropic")][InlineData("Gemini")]
    public async Task Screenshot_UsesEachProvidersRealImageProtocol(string provider)
    {
        var config = Config(provider); var image = Image();
        using var handler = new Handler(async message =>
        {
            var json = await message.Content!.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json); var body = doc.RootElement;
            Assert.DoesNotContain(White, message.RequestUri!.ToString());
            JsonElement parts;
            if (provider == "Gemini")
            {
                parts = body.GetProperty("contents")[0].GetProperty("parts");
                Assert.Equal(White, parts[1].GetProperty("inlineData").GetProperty("data").GetString());
                Assert.Equal("image/png", parts[1].GetProperty("inlineData").GetProperty("mimeType").GetString());
            }
            else if (provider == "Anthropic")
            {
                parts = body.GetProperty("messages")[0].GetProperty("content");
                Assert.Equal("image", parts[0].GetProperty("type").GetString());
                Assert.Equal(White, parts[0].GetProperty("source").GetProperty("data").GetString());
                Assert.Equal("image/png", parts[0].GetProperty("source").GetProperty("media_type").GetString());
            }
            else
            {
                parts = body.GetProperty("messages")[1].GetProperty("content");
                Assert.Equal("image_url", parts[1].GetProperty("type").GetString());
                Assert.Equal("data:image/png;base64," + White, parts[1].GetProperty("image_url").GetProperty("url").GetString());
            }
            Assert.Equal(2, parts.GetArrayLength());
            Assert.Contains("solve", body.ToString(), StringComparison.OrdinalIgnoreCase);
            return Success(provider);
        });
        using var client = new HttpClient(handler); var api = new ProviderApi(client, () => config, _ => "fixture-key", new());
        var result = await api.TranslateAsync(ProblemSolving.CreateRequest(config, image), default);
        Assert.Equal("x = 1", result.Text); Assert.Equal(1, handler.Calls);
    }
    private sealed class NoCache : ITranslationCache, ITranslationMemory
    {
        public int Calls;
        public Task ClearAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task<TranslationResult?> GetAsync(string key, CancellationToken t) => throw new Exception("Screenshot must not read cache");
        public Task PutAsync(string key, TranslationResult value, CancellationToken t) { Calls++; throw new Exception("Screenshot must not write cache"); }
        public Task<TranslationResult?> FindAsync(TranslationRequest r, CancellationToken t) => throw new Exception("Screenshot must not read vocabulary");
        public Task LearnAsync(TranslationRequest r, TranslationResult value, CancellationToken t) => throw new Exception("Screenshot must not learn vocabulary");
    }
    [Fact] public async Task Screenshot_BypassesAllAutomaticStorage_AndCannotBeReplaced()
    {
        var config = Config() with { Vocabulary = new() { AllowOnlineFallback = false } };
        var provider = new CoordinatorTests.FakeProvider((_, _) => Task.FromResult(new TranslationResult("x = 1")));
        var storage = new NoCache();
        using var coordinator = new TranslationCoordinator(provider, storage, () => config, new(), storage);
        var image = Image(); var snapshot = Snapshot(image);
        await coordinator.ObserveAsync(snapshot, taskRequest: ProblemSolving.CreateRequest(config, image));
        Assert.Equal("x = 1", coordinator.CurrentPreview!.Result.Text); Assert.Equal(0, storage.Calls);
        var replacement = new ReplacementService(new NeverCapture(), coordinator);
        Assert.Null(await replacement.PrepareAsync());
        Assert.Null(await replacement.PreparePreviewAsync(coordinator.CurrentPreview));
        Assert.False(await replacement.ConfirmAsync(new(coordinator.CurrentPreview)));
        await coordinator.ObserveAsync(null, true);
        Assert.Null(coordinator.CurrentPreview);
    }
    [Theory][InlineData(false, true)][InlineData(true, false)]
    public async Task PausedOrUnconsentedScreenshot_DoesNotCallProvider(bool consent, bool enabled)
    {
        var config = Config() with { PrivacyConsent = consent, Translation = new() { Enabled = enabled } };
        var provider = new CoordinatorTests.FakeProvider((_, _) => throw new Exception("No request expected"));
        using var coordinator = new TranslationCoordinator(provider, new NoCache(), () => config, new());
        var image = Image(); await coordinator.ObserveAsync(Snapshot(image), taskRequest: ProblemSolving.CreateRequest(config, image));
        Assert.Equal(0, provider.Calls); Assert.Null(coordinator.CurrentPreview);
    }
    [Fact] public async Task ClosingScreenshot_DiscardsALateProviderResult()
    {
        var config = Config(); var entered = new TaskCompletionSource(); var answer = new TaskCompletionSource<TranslationResult>();
        var provider = new CoordinatorTests.FakeProvider((_, _) => { entered.SetResult(); return answer.Task; });
        using var coordinator = new TranslationCoordinator(provider, new NoCache(), () => config, new());
        var image = Image(); var task = coordinator.ObserveAsync(Snapshot(image), taskRequest: ProblemSolving.CreateRequest(config, image));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await coordinator.ObserveAsync(null, true); answer.SetResult(new("late result")); await task;
        Assert.Null(coordinator.CurrentPreview);
    }
    private sealed class NeverCapture : IInputTarget
    {
        public Task<InputSnapshot?> CaptureAsync(CancellationToken t) => throw new Exception("Images must never target an editor");
        public Task<bool> TryReplaceAsync(InputSnapshot e, string text, CancellationToken t) => throw new Exception("Images must never replace");
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken token) { Calls++; return respond(message); }
    }
    private static HttpResponseMessage Success(string provider) => new(HttpStatusCode.OK) { Content = new StringContent(provider switch
    {
        "Anthropic" => """{"stop_reason":"end_turn","content":[{"type":"text","text":"x = 1"}]}""",
        "Gemini" => """{"candidates":[{"finishReason":"STOP","content":{"parts":[{"text":"x = 1"}]}}]}""",
        _ => """{"choices":[{"finish_reason":"stop","message":{"content":"x = 1"}}]}"""
    }, Encoding.UTF8, "application/json") };
}
