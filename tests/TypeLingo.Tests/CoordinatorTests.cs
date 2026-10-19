using TypeLingo.Core;
using TypeLingo.Infrastructure;

namespace TypeLingo.Tests;

public sealed class CoordinatorTests
{
    internal static AppSettings Config => new()
    {
        PrivacyConsent = true, AllowedApplications = ["notepad"],
        Translation = new() { DebounceMilliseconds = 100, MinimumRequestIntervalMilliseconds = 100 }
    };
    internal static InputSnapshot Input(string text, string identity = "A") => new(identity, text, new(10, 10, 100, 30));

    [Fact] public async Task NormalPreview_DoesNotWriteOriginal()
    {
        using var fixture = new Fixture();
        var input = Input("明天我可能要晚一点到公司");
        await fixture.Coordinator.ObserveAsync(input);
        Assert.Equal("明天我可能要晚一点到公司", input.Text);
        Assert.Equal("訳:明天我可能要晚一点到公司", fixture.Coordinator.CurrentPreview?.Result.Text);
        Assert.Equal(1, fixture.Provider.Calls);
    }
    [Fact] public async Task CacheHit_AfterClearingInput_MakesNoSecondRequest()
    {
        using var fixture = new Fixture();
        await fixture.Coordinator.ObserveAsync(Input("你好"));
        await fixture.Coordinator.ObserveAsync(null);
        await fixture.Coordinator.ObserveAsync(Input("你好"));
        Assert.Equal(1, fixture.Provider.Calls);
        Assert.True(fixture.Coordinator.CurrentPreview!.FromCache);
        Assert.Equal(1, fixture.Statistics.CacheHits);
    }
    [Fact] public async Task RapidChanges_DebounceIntermediateTexts()
    {
        using var f = new Fixture();
        var a = f.Coordinator.ObserveAsync(Input("今天"));
        var b = f.Coordinator.ObserveAsync(Input("今天天气"));
        var c = f.Coordinator.ObserveAsync(Input("今天天气很好"));
        await Task.WhenAll(a, b, c);
        Assert.Equal(1, f.Provider.Calls);
        Assert.Equal("今天天气很好", f.Coordinator.CurrentPreview?.Snapshot.Text);
    }
    [Fact] public async Task DuplicateInput_SharesPendingTaskAndDoesNotRefreshPreview()
    {
        using var f = new Fixture(); var shown = 0;
        f.Coordinator.PreviewChanged += p => { if (p != null) shown++; };
        var pending = f.Coordinator.ObserveAsync(Input("same"));
        Assert.Same(pending, f.Coordinator.ObserveAsync(Input("same")));
        await pending; await f.Coordinator.ObserveAsync(Input("same"));
        Assert.Equal(1, f.Provider.Calls); Assert.Equal(1, shown);
    }
    [Fact] public async Task LateResponseFromOldControl_CannotAppearOnNewControl_AndRequestsAreSerialized()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var f = new Fixture(async (request, _) =>
        {
            if (request.Text == "old") { started.SetResult(); await release.Task; }
            return new(request.Text);
        });
        var stale = f.Coordinator.ObserveAsync(Input("old", "A"));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var latest = f.Coordinator.ObserveAsync(Input("new", "B"));
        Assert.Null(f.Coordinator.CurrentPreview);
        release.SetResult(); await Task.WhenAll(stale, latest);
        Assert.Equal("B", f.Coordinator.CurrentPreview?.Snapshot.Identity);
        Assert.Equal("new", f.Coordinator.CurrentPreview?.Result.Text);
        Assert.Equal(1, f.Provider.MaxConcurrent);
    }
    [Fact] public async Task ConfigurationChange_InvalidatesCurrentResult()
    {
        using var f = new Fixture(); await f.Coordinator.ObserveAsync(Input("你好"));
        var old = f.Coordinator.CurrentPreview!;
        f.Settings = f.Settings with { Translation = f.Settings.Translation with { TargetLanguage = "en" } };
        Assert.False(f.Coordinator.IsCurrent(old));
        await f.Coordinator.ObserveAsync(Input("你好"));
        Assert.Equal(2, f.Provider.Calls); Assert.Equal("en", f.Coordinator.CurrentPreview?.Request.TargetLanguage);
    }
    [Fact] public async Task WriteCapabilityChange_InvalidatesPreview_AndReusesCache()
    {
        using var f = new Fixture();
        await f.Coordinator.ObserveAsync(Input("你好") with { CanWrite = false });
        var before = f.Coordinator.CurrentPreview!; Assert.False(before.Snapshot.CanWrite);
        await f.Coordinator.ObserveAsync(Input("你好") with { CanWrite = true });
        Assert.False(f.Coordinator.IsCurrent(before)); Assert.True(f.Coordinator.CurrentPreview!.Snapshot.CanWrite);
        Assert.Equal(1, f.Provider.Calls);
    }
    [Theory]
    [InlineData("password")][InlineData("composition")][InlineData("paused")][InlineData("no-consent")][InlineData("oversized")]
    public async Task UnsafeInput_IsNeverSentOrCached(string scenario)
    {
        using var f = new Fixture(); var snapshot = Input("private");
        if (scenario == "password") snapshot = snapshot with { IsSensitive = true };
        if (scenario == "composition") snapshot = snapshot with { IsComposing = true };
        if (scenario == "paused") f.Settings = f.Settings with { Translation = f.Settings.Translation with { Enabled = false } };
        if (scenario == "no-consent") f.Settings = f.Settings with { PrivacyConsent = false };
        if (scenario == "oversized") snapshot = Input(new string('x', 2001));
        await f.Coordinator.ObserveAsync(snapshot);
        Assert.Equal(0, f.Provider.Calls); Assert.Null(f.Coordinator.CurrentPreview);
        Assert.Null(await f.Cache.GetAsync(TranslationRequest.Create(f.Settings, snapshot.Text).CacheKey, default));
    }
    [Fact] public async Task Pause_CancelsInflightAndHidesResult()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var f = new Fixture(async (_, token) => { started.SetResult(); await Task.Delay(5000, token); return new("late"); });
        var pending = f.Coordinator.ObserveAsync(Input("private")); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        f.Settings = f.Settings with { Translation = f.Settings.Translation with { Enabled = false } };
        await f.Coordinator.ObserveAsync(null, true); await pending;
        Assert.Null(f.Coordinator.CurrentPreview);
    }

    internal sealed class Fixture : IDisposable
    {
        public AppSettings Settings = Config;
        public SessionStatistics Statistics { get; } = new();
        public FakeProvider Provider { get; }
        public CacheService Cache { get; }
        public TranslationCoordinator Coordinator { get; }
        public Fixture(Func<TranslationRequest, CancellationToken, Task<TranslationResult>>? call = null)
        {
            Provider = new(call ?? ((r, _) => Task.FromResult(new TranslationResult("訳:" + r.Text))));
            Cache = new CacheService(() => Settings, "unused.db", new TestProtector());
            Coordinator = new(Provider, Cache, () => Settings, Statistics);
        }
        public void Dispose() { Coordinator.Dispose(); Cache.Dispose(); }
    }
    internal sealed class FakeProvider(Func<TranslationRequest, CancellationToken, Task<TranslationResult>> call) : ITranslationProvider
    {
        public int Calls, MaxConcurrent;
        private int concurrent;
        public async Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls); var active = Interlocked.Increment(ref concurrent); MaxConcurrent = Math.Max(MaxConcurrent, active);
            try { return await call(request, cancellationToken); } finally { Interlocked.Decrement(ref concurrent); }
        }
    }
}
