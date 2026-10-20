using TypeLingo.Core;
using TypeLingo.Infrastructure;

namespace TypeLingo.Tests;

public sealed class OfflineTranslationTests
{
    private sealed class Fixture : IDisposable
    {
        public readonly string DirectoryPath = Path.Combine(Path.GetTempPath(), "TypeLingo-Offline-" + Guid.NewGuid().ToString("N"));
        public AppSettings Settings = CoordinatorTests.Config with { Translation = new() { SourceLanguage = "zh", TargetLanguage = "en", DebounceMilliseconds = 100, MinimumRequestIntervalMilliseconds = 100 } };
        public readonly VocabularyStore Store;
        public readonly CacheService Cache;
        public readonly CoordinatorTests.FakeProvider Provider;
        public readonly SessionStatistics Statistics = new();
        public readonly TranslationCoordinator Coordinator;
        public Fixture(Func<TranslationRequest, CancellationToken, Task<TranslationResult>>? translate = null, ITranslationMemory? memory = null)
        {
            Directory.CreateDirectory(DirectoryPath); Store = new(DirectoryPath, new TestProtector(), false);
            Cache = new(() => Settings, Path.Combine(DirectoryPath, "cache.db"), new TestProtector());
            Provider = new(translate ?? ((_, _) => Task.FromResult(new TranslationResult("online answer"))));
            Coordinator = new(Provider, Cache, () => Settings, Statistics, memory ?? Store);
        }
        public TranslationRequest Request(string text = "test") => TranslationRequest.Create(Settings, text);
        public async Task Seed(string text = "test", string answer = "offline answer") => await Store.LearnAsync(Request(text), new(answer), default);
        public void Dispose() { Coordinator.Dispose(); Store.Dispose(); Cache.Dispose(); Directory.Delete(DirectoryPath, true); }
    }
    [Fact] public async Task OfflineHasPriorityOverWarmCache_AndNeverInvokesOnlineProvider()
    {
        using var f = new Fixture(); await f.Seed(); await f.Cache.PutAsync(f.Request().CacheKey, new("stale cached answer"), default);
        await f.Coordinator.ObserveAsync(CoordinatorTests.Input("test"));
        Assert.Equal("offline answer", f.Coordinator.CurrentPreview?.Result.Text); Assert.Equal(TranslationOrigin.Vocabulary, f.Coordinator.CurrentPreview?.Origin);
        Assert.Equal(0, f.Provider.Calls); Assert.Equal(1, f.Statistics.VocabularyHits); Assert.Equal(0, f.Statistics.CacheHits);
    }
    [Fact] public async Task OnlineMissIsLearnedWithoutEitherCache_AndWorksAfterRestartWithAnotherProvider()
    {
        using var f = new Fixture(); f.Settings = f.Settings with { Cache = new() { MemoryEnabled = false, DiskEnabled = false } };
        await f.Coordinator.ObserveAsync(CoordinatorTests.Input("test")); Assert.Equal(1, f.Provider.Calls);
        using var reloaded = new VocabularyStore(f.DirectoryPath, new TestProtector(), false);
        var config = f.Settings with { AI = ProviderCatalog.Defaults("OpenAI") with { Model = "different model" } };
        var forbidden = new CoordinatorTests.FakeProvider((_, _) => throw new InvalidOperationException("must stay offline"));
        using var coordinator = new TranslationCoordinator(forbidden, f.Cache, () => config, new SessionStatistics(), reloaded);
        await coordinator.ObserveAsync(CoordinatorTests.Input("test"));
        Assert.Equal("online answer", coordinator.CurrentPreview?.Result.Text); Assert.Equal(0, forbidden.Calls);
    }
    [Theory][InlineData(true)][InlineData(false)]
    public async Task OfflineOnlyMiss_DoesNotUseApiOrOnlineCache(bool preference)
    {
        using var f = new Fixture(); f.Settings = f.Settings with { Vocabulary = new() { PreferOffline = preference, AllowOnlineFallback = false } };
        await f.Cache.PutAsync(f.Request().CacheKey, new("online cached answer"), default);
        var statuses = new List<string>(); f.Coordinator.StatusChanged += statuses.Add;
        await f.Coordinator.ObserveAsync(CoordinatorTests.Input("test"));
        Assert.Equal(0, f.Provider.Calls); Assert.Null(f.Coordinator.CurrentPreview); Assert.Contains("离线题库未命中，已关闭在线翻译。", statuses);
        await f.Seed(); await f.Coordinator.ObserveAsync(CoordinatorTests.Input("test"), true);
        Assert.Equal("offline answer", f.Coordinator.CurrentPreview?.Result.Text);
    }
    [Fact] public async Task DisablingPreferenceUsesNormalOnlineFlow_AndLearningCanBeDisabledSeparately()
    {
        using var f = new Fixture(); await f.Seed();
        f.Settings = f.Settings with { Vocabulary = new() { PreferOffline = false, AutoLearn = false } };
        await f.Coordinator.ObserveAsync(CoordinatorTests.Input("test")); Assert.Equal("online answer", f.Coordinator.CurrentPreview?.Result.Text);
        Assert.Equal(1, f.Provider.Calls);
        await f.Coordinator.ObserveAsync(CoordinatorTests.Input("new uncached text"));
        Assert.Null(await f.Store.FindAsync(f.Request("new uncached text"), default));
        Assert.Equal("offline answer", (await f.Store.FindAsync(f.Request(), default))?.Text);
    }
    [Fact] public async Task EncounteredLegacyCacheIsPromotedToPermanentVocabulary()
    {
        using var f = new Fixture(); await f.Cache.PutAsync(f.Request().CacheKey, new("previous cached translation"), default);
        await f.Coordinator.ObserveAsync(CoordinatorTests.Input("test")); Assert.Equal(0, f.Provider.Calls);
        Assert.Equal(TranslationOrigin.Cache, f.Coordinator.CurrentPreview?.Origin);
        await f.Cache.ClearAsync(); await f.Coordinator.ObserveAsync(null);
        await f.Coordinator.ObserveAsync(CoordinatorTests.Input("test"));
        Assert.Equal(TranslationOrigin.Vocabulary, f.Coordinator.CurrentPreview?.Origin); Assert.Equal("previous cached translation", f.Coordinator.CurrentPreview?.Result.Text);
    }
    [Theory][InlineData("error")][InlineData("empty")]
    public async Task FailedOrEmptyOnlineResponsesDoNotEnterVocabulary(string kind)
    {
        using var f = new Fixture((_, _) => kind == "error" ? throw new TranslationException(TranslationError.Network, "network unavailable") : Task.FromResult(new TranslationResult(" ")));
        await f.Coordinator.ObserveAsync(CoordinatorTests.Input("test"));
        Assert.Null(f.Coordinator.CurrentPreview); Assert.Equal(0, (await f.Store.StatisticsAsync()).Available);
    }
    [Fact] public async Task CancelledLateResponseIsNotLearnedOrShown()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var f = new Fixture(async (_, _) => { started.SetResult(); await release.Task; return new("late answer"); });
        var task = f.Coordinator.ObserveAsync(CoordinatorTests.Input("test")); await started.Task;
        await f.Coordinator.ObserveAsync(null); release.SetResult(); await task;
        Assert.Null(f.Coordinator.CurrentPreview); Assert.Equal(0, (await f.Store.StatisticsAsync()).Available);
    }
    private sealed class FaultMemory(bool failReading) : ITranslationMemory
    {
        public int Reads, Writes;
        public Task<TranslationResult?> FindAsync(TranslationRequest request, CancellationToken token)
        { Reads++; if (failReading) throw new IOException("damaged local file"); return Task.FromResult<TranslationResult?>(null); }
        public Task LearnAsync(TranslationRequest request, TranslationResult result, CancellationToken token)
        { Writes++; throw new IOException("disk full"); }
    }
    [Fact] public async Task FailedSavingStillDisplaysSuccessfulOnlineTranslation_WithWarning()
    {
        var memory = new FaultMemory(false); using var f = new Fixture(memory: memory);
        var statuses = new List<string>(); f.Coordinator.StatusChanged += statuses.Add;
        await f.Coordinator.ObserveAsync(CoordinatorTests.Input("test"));
        Assert.Equal("online answer", f.Coordinator.CurrentPreview?.Result.Text); Assert.Equal(1, memory.Writes);
        Assert.Contains("译文就绪，但本地保存失败；请检查存储权限。", statuses);
    }
    [Fact] public async Task BrokenVocabularyDoesNotSilentlySendTextOnline()
    {
        using var f = new Fixture(memory: new FaultMemory(true));
        await f.Coordinator.ObserveAsync(CoordinatorTests.Input("test")); Assert.Equal(0, f.Provider.Calls); Assert.Null(f.Coordinator.CurrentPreview);
    }
    [Theory][InlineData("consent")][InlineData("pause")][InlineData("sensitive")][InlineData("composition")]
    public async Task OfflineLookupPreservesExistingPrivacyAndInputGates(string kind)
    {
        var memory = new FaultMemory(false); using var f = new Fixture(memory: memory);
        if (kind == "consent") f.Settings = f.Settings with { PrivacyConsent = false };
        if (kind == "pause") f.Settings = f.Settings with { Translation = f.Settings.Translation with { Enabled = false } };
        var input = CoordinatorTests.Input("test") with { IsSensitive = kind == "sensitive", IsComposing = kind == "composition" };
        await f.Coordinator.ObserveAsync(input); Assert.Equal(0, memory.Reads); Assert.Equal(0, memory.Writes); Assert.Equal(0, f.Provider.Calls);
    }
}
