using TypeLingo.Core;
using static TypeLingo.Tests.CoordinatorTests;

namespace TypeLingo.Tests;

public sealed class ReplacementTests
{
    [Fact] public async Task PrepareAndCancel_NeverWrite()
    {
        using var f = new Fixture(); var target = new Target(Input("你好，辛苦了")); var service = new ReplacementService(target, f.Coordinator);
        var ticket = await service.PrepareAsync(); Assert.NotNull(ticket); Assert.Equal(0, target.Writes);
        service.Cancel(ticket); Assert.False(await service.ConfirmAsync(ticket)); Assert.Equal(0, target.Writes);
    }
    [Fact] public async Task Confirmation_WritesOnce_AndSuppressesFeedbackTranslation()
    {
        using var f = new Fixture(); var target = new Target(Input("你好，辛苦了")); var service = new ReplacementService(target, f.Coordinator);
        var ticket = (await service.PrepareAsync())!;
        Assert.True(await service.ConfirmAsync(ticket)); Assert.False(await service.ConfirmAsync(ticket));
        Assert.Equal(1, target.Writes); Assert.Equal("訳:你好，辛苦了", target.Current?.Text);
        await f.Coordinator.ObserveAsync(target.Current);
        Assert.Equal(1, f.Provider.Calls); Assert.Null(f.Coordinator.CurrentPreview);
    }
    [Theory][InlineData("text")][InlineData("focus")][InlineData("ime")][InlineData("readonly")][InlineData("privacy")][InlineData("settings")]
    public async Task ChangedStateAfterConfirmation_CancelsWrite(string change)
    {
        using var f = new Fixture(); var target = new Target(Input("before")); var service = new ReplacementService(target, f.Coordinator);
        var ticket = (await service.PrepareAsync())!;
        if (change == "text") target.Current = Input("after");
        if (change == "focus") target.Current = Input("before", "B");
        if (change == "ime") target.Current = target.Current! with { IsComposing = true };
        if (change == "readonly") target.Current = target.Current! with { CanWrite = false };
        if (change == "privacy") target.Current = target.Current! with { IsSensitive = true };
        if (change == "settings") f.Settings = f.Settings with { Translation = f.Settings.Translation with { Mode = "literal" } };
        Assert.False(await service.ConfirmAsync(ticket)); Assert.Equal(0, target.Writes);
    }
    [Fact] public async Task HotkeyWithStalePreview_RetranslatesBeforeConfirmation()
    {
        using var f = new Fixture(); await f.Coordinator.ObserveAsync(Input("old"));
        var target = new Target(Input("new")); var service = new ReplacementService(target, f.Coordinator);
        var ticket = await service.PrepareAsync();
        Assert.Equal("new", ticket?.Preview.Snapshot.Text); Assert.Equal("訳:new", ticket?.Preview.Result.Text);
        Assert.Equal(2, f.Provider.Calls); Assert.Equal(0, target.Writes);
    }
    [Fact] public async Task TargetRechecksAtWriteBoundary_RejectsConcurrentEdit()
    {
        using var f = new Fixture(); var target = new Target(Input("original")); var service = new ReplacementService(target, f.Coordinator);
        var ticket = (await service.PrepareAsync())!;
        target.BeforeWrite = () => target.Current = Input("concurrent edit");
        Assert.False(await service.ConfirmAsync(ticket)); Assert.Equal(0, target.Writes);
    }
    private sealed class Target(InputSnapshot snapshot) : IInputTarget
    {
        public InputSnapshot? Current = snapshot;
        public Action? BeforeWrite;
        public int Writes;
        public Task<InputSnapshot?> CaptureAsync(CancellationToken cancellationToken) => Task.FromResult(Current);
        public Task<bool> TryReplaceAsync(InputSnapshot expected, string translation, CancellationToken cancellationToken)
        {
            BeforeWrite?.Invoke();
            if (Current?.Identity != expected.Identity || Current?.Text != expected.Text || Current is not { CanWrite: true, IsComposing: false, IsSensitive: false }) return Task.FromResult(false);
            Writes++; Current = Current with { Text = translation }; return Task.FromResult(true);
        }
    }
}
