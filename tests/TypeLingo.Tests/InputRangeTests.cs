using TypeLingo.Core;

namespace TypeLingo.Tests;

public sealed class InputRangeTests
{
    [Theory]
    [InlineData("first\r\nmiddle\r\nlast", 9, "middle", "first\r\n译文\r\nlast")]
    [InlineData("first\nsecond", 0, "first", "译文\nsecond")]
    [InlineData("first\nsecond", 12, "second", "first\n译文")]
    [InlineData("first\r\nsecond", 6, "first", "译文\r\nsecond")]
    [InlineData("line\n", 5, "", "line\n译文")]
    [InlineData("行一\r行二", 4, "行二", "行一\r译文")]
    public void CurrentLine_PreservesOtherLinesAndSeparators(string full, int caret, string expected, string replaced)
    {
        var range = InputTextRange.LineAt(full, caret);
        Assert.Equal(expected, range.Text); Assert.Equal(replaced, range.Replace("译文"));
    }
    [Fact] public void Selection_UsesExactOffsetEvenWithRepeatedTextAndEmoji()
    {
        var range = InputTextRange.FromAutomation("😀 same\r\nsame end", "😀 same\rsame end", "😀 same\r", "same")!;
        Assert.Equal(9, range.Start); Assert.Equal("same", range.Text);
        Assert.Equal("😀 same\r\n译文 end", range.Replace("译文"));
    }
    [Fact] public void MultilineSelection_PreservesPrefixSuffixAndNewlines()
    {
        var range = InputTextRange.FromAutomation("a\r\nb\r\nc", "a\nb\nc", "a", "\nb\n")!;
        Assert.Equal("\r\nb\r\n", range.Text); Assert.Equal("a译文c", range.Replace("译文"));
    }
    [Fact] public void AutomationMismatch_IsNeverGuessed()
    {
        Assert.Null(InputTextRange.FromAutomation("a same same", "different", "a same ", "same"));
        Assert.Null(InputTextRange.FromAutomation("a same same", "a same same", "missing", "same"));
        Assert.Null(InputTextRange.FromAutomation("a same same", "a same same", "a ", "other"));
    }
    [Fact] public async Task SameTextAtDifferentOffsets_InvalidatesPreviewAndConfirmation()
    {
        using var f = new CoordinatorTests.Fixture();
        var first = new InputSnapshot("input", "same", new(0, 0, 1, 1), IsSelection: true,
            EditRange: new("same same", 0, 4));
        var target = new RangeTarget(first); var service = new ReplacementService(target, f.Coordinator);
        var ticket = (await service.PrepareAsync())!;
        target.Current = first with { EditRange = new("same same", 5, 4) };
        Assert.False(await service.ConfirmAsync(ticket)); Assert.Equal(0, target.Writes);
        await f.Coordinator.ObserveAsync(target.Current);
        Assert.False(f.Coordinator.IsCurrent(ticket.Preview));
        Assert.Equal(5, f.Coordinator.CurrentPreview!.Snapshot.EditRange!.Start);
    }
    [Fact] public async Task ClickedPreview_RejectsOutsideEditInsteadOfTranslatingAnotherTarget()
    {
        using var f = new CoordinatorTests.Fixture();
        var first = new InputSnapshot("input", "same", new(0, 0, 1, 1), EditRange: new("prefix same", 7, 4));
        var target = new RangeTarget(first); var service = new ReplacementService(target, f.Coordinator);
        await f.Coordinator.ObserveAsync(first); var preview = f.Coordinator.CurrentPreview!;
        Assert.NotNull(await service.PreparePreviewAsync(preview));
        target.Current = first with { EditRange = new("edited same", 7, 4) };
        Assert.Null(await service.PreparePreviewAsync(preview)); Assert.Equal(1, f.Provider.Calls);
    }
    [Fact] public async Task PartialWrite_PreservesOtherTextAndSuppressesOwnFullValue()
    {
        using var f = new CoordinatorTests.Fixture();
        var first = new InputSnapshot("input", "same", new(0, 0, 1, 1), IsSelection: true, EditRange: new("prefix same suffix", 7, 4));
        var target = new RangeTarget(first); var service = new ReplacementService(target, f.Coordinator);
        var ticket = (await service.PrepareAsync())!;
        Assert.True(await service.ConfirmAsync(ticket)); Assert.Equal("prefix 訳:same suffix", target.FullValue);
        var after = first with { Text = "prefix", IsSelection = false, EditRange = new(target.FullValue!, 0, 6) };
        await f.Coordinator.ObserveAsync(after); Assert.Null(f.Coordinator.CurrentPreview); Assert.Equal(1, f.Provider.Calls);
    }
    [Fact] public async Task NewSelectionAfterReplacement_StillTranslatesWithoutEditing()
    {
        using var f = new CoordinatorTests.Fixture();
        var first = new InputSnapshot("input", "same", new(0, 0, 1, 1), IsSelection: true, EditRange: new("same suffix", 0, 4));
        var target = new RangeTarget(first); var service = new ReplacementService(target, f.Coordinator);
        var ticket = (await service.PrepareAsync())!; Assert.True(await service.ConfirmAsync(ticket));
        var value = target.FullValue!;
        var feedback = first with { Text = value, IsSelection = false, EditRange = new(value, 0, value.Length) };
        await f.Coordinator.ObserveAsync(feedback); Assert.Null(f.Coordinator.CurrentPreview);
        var selected = first with { Text = "suffix", EditRange = new(value, value.Length - 6, 6) };
        await f.Coordinator.ObserveAsync(selected);
        Assert.Equal("suffix", f.Coordinator.CurrentPreview?.Snapshot.Text); Assert.Equal(2, f.Provider.Calls);
    }
    private sealed class RangeTarget(InputSnapshot initial) : IInputTarget
    {
        public InputSnapshot Current = initial;
        public int Writes;
        public string? FullValue;
        public Task<InputSnapshot?> CaptureAsync(CancellationToken token) => Task.FromResult<InputSnapshot?>(Current);
        public Task<bool> TryReplaceAsync(InputSnapshot expected, string translation, CancellationToken token)
        {
            if (!expected.SameContent(Current)) return Task.FromResult(false);
            FullValue = expected.EditRange!.Replace(translation); Writes++; return Task.FromResult(true);
        }
    }
}
