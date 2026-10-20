namespace TypeLingo.Core;

public sealed class ReplacementTicket(Preview preview)
{
    public Preview Preview { get; } = preview;
    private int consumed;
    public bool Consume() => Interlocked.Exchange(ref consumed, 1) == 0;
}

public sealed class ReplacementService(IInputTarget target, TranslationCoordinator coordinator)
{
    public async Task<ReplacementTicket?> PrepareAsync(CancellationToken cancellationToken = default)
    {
        if (coordinator.CurrentPreview is { Request.IsTranslation: false }) return null;
        var before = await target.CaptureAsync(cancellationToken);
        if (before is not { CanWrite: true, IsComposing: false, IsSensitive: false } || string.IsNullOrWhiteSpace(before.Text)) return null;
        await coordinator.ObserveAsync(before);
        cancellationToken.ThrowIfCancellationRequested();
        var preview = coordinator.CurrentPreview;
        var after = await target.CaptureAsync(cancellationToken);
        return preview != null && coordinator.IsCurrent(preview) && Same(before, after) &&
               Same(before, preview.Snapshot) ? new ReplacementTicket(preview) : null;
    }

    public async Task<bool> ConfirmAsync(ReplacementTicket ticket, CancellationToken cancellationToken = default)
    {
        if (!ticket.Consume() || !ticket.Preview.Request.IsTranslation || !coordinator.IsCurrent(ticket.Preview)) return false;
        var live = await target.CaptureAsync(cancellationToken);
        if (!Same(ticket.Preview.Snapshot, live) || !coordinator.IsCurrent(ticket.Preview)) return false;
        var success = await target.TryReplaceAsync(ticket.Preview.Snapshot, ticket.Preview.Result.Text, cancellationToken);
        if (success) coordinator.SuppressOwnReplacement(ticket.Preview.Snapshot, ticket.Preview.Result.Text);
        return success;
    }
    public async Task<ReplacementTicket?> PreparePreviewAsync(Preview preview, CancellationToken cancellationToken = default)
    {
        if (!preview.Request.IsTranslation || !coordinator.IsCurrent(preview) || preview.Snapshot is not { CanWrite: true, IsComposing: false, IsSensitive: false }) return null;
        var live = await target.CaptureAsync(cancellationToken);
        return Same(preview.Snapshot, live) && coordinator.IsCurrent(preview) ? new(preview) : null;
    }
    public void Cancel(ReplacementTicket ticket) => ticket.Consume();
    private static bool Same(InputSnapshot expected, InputSnapshot? actual) => actual is
        { CanWrite: true, IsSensitive: false, IsComposing: false } && expected.SameContent(actual);
}
