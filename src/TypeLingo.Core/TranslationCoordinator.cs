namespace TypeLingo.Core;

/// <summary>同一时间只执行一个主翻译请求，焦点、文本或配置变化时取消旧请求。</summary>
public sealed class TranslationCoordinator(ITranslationProvider provider, ITranslationCache cache,
    Func<AppSettings> settings, SessionStatistics statistics) : IDisposable
{
    private readonly object gate = new();
    private readonly SemaphoreSlim singleRequest = new(1, 1);
    private CancellationTokenSource? pending;
    private InputSnapshot? current;
    private string? currentKey;
    private long sequence;
    private long lastRequestTimestamp;
    private Preview? preview;
    private Task active = Task.CompletedTask;
    private bool disposed;
    private (string Identity, string Text, string? FullText, InputSnapshot? Observed)? suppression;
    public event Action<Preview?>? PreviewChanged;
    public event Action<string>? StatusChanged;
    public Preview? CurrentPreview { get { lock (gate) return preview; } }

    public Task ObserveAsync(InputSnapshot? snapshot, bool force = false)
    {
        lock (gate)
        {
            if (disposed) return Task.CompletedTask;
            var config = settings();
            if (!config.PrivacyConsent || !config.Translation.Enabled || snapshot is { IsSensitive: true } ||
                snapshot is { IsComposing: true } || string.IsNullOrWhiteSpace(snapshot?.Text) ||
                snapshot.Text.Length > config.Translation.MaxInputCharacters) snapshot = null;
            if (suppression is { } own && snapshot?.Identity == own.Identity &&
                (own.FullText != null ? snapshot.EditRange?.FullText == own.FullText : snapshot.Text == own.Text) &&
                (!snapshot.IsSelection || snapshot.Text == own.Text) && (own.Observed == null || own.Observed.SameContent(snapshot)))
            {
                suppression = (own.Identity, own.Text, own.FullText, snapshot); snapshot = null;
            }
            else if (snapshot != null) suppression = null;
            var request = snapshot == null ? null : TranslationRequest.Create(config, snapshot.Text);
            if (!force && (current == null ? snapshot == null : current.SameContent(snapshot)) && currentKey == request?.CacheKey) return active;
            pending?.Cancel(); pending?.Dispose(); pending = null;
            current = snapshot; currentKey = request?.CacheKey; preview = null; sequence++;
            PreviewChanged?.Invoke(null);
            if (snapshot == null) return active = Task.CompletedTask;
            var source = pending = new CancellationTokenSource();
            return active = RunAsync(snapshot, request!, config, sequence, source.Token);
        }
    }

    private async Task RunAsync(InputSnapshot snapshot, TranslationRequest request, AppSettings config, long id, CancellationToken token)
    {
        // 先让出执行权，避免在更新观察状态时同步触发缓存或事件回调。
        await Task.Yield();
        try
        {
            var result = await cache.GetAsync(request.CacheKey, token);
            var cached = result != null;
            if (result == null)
            {
                PublishStatus(id, "等待输入稳定…");
                await Task.Delay(config.Translation.DebounceMilliseconds, token);
                await singleRequest.WaitAsync(token);
                try
                {
                    var wait = config.Translation.MinimumRequestIntervalMilliseconds -
                        (Environment.TickCount64 - lastRequestTimestamp);
                    if (wait > 0) await Task.Delay(TimeSpan.FromMilliseconds(wait), token);
                    token.ThrowIfCancellationRequested();
                    lastRequestTimestamp = Environment.TickCount64;
                    PublishStatus(id, "正在翻译…");
                    result = await provider.TranslateAsync(request, token);
                    token.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(result.Text)) throw new TranslationException(TranslationError.InvalidResponse, "API 未返回有效译文。");
                    await cache.PutAsync(request.CacheKey, result, token);
                }
                finally { singleRequest.Release(); }
            }
            lock (gate)
            {
                if (disposed || token.IsCancellationRequested || id != sequence || current?.Identity != snapshot.Identity || currentKey != request.CacheKey) return;
                if (cached) statistics.CacheHit();
                preview = new Preview(snapshot, request, result, id, cached);
                PreviewChanged?.Invoke(preview);
                StatusChanged?.Invoke(cached ? "本地缓存 · 未调用 API" : "译文就绪 · 原文保持不变");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (TranslationException e) { PublishStatus(id, e.Message); }
        catch (Exception) { PublishStatus(id, "翻译或缓存暂时不可用，请检查配置后重试。"); }
    }

    private void PublishStatus(long id, string status) { lock (gate) { if (!disposed && id == sequence) StatusChanged?.Invoke(status); } }
    public bool IsCurrent(Preview value)
    {
        lock (gate) return !disposed && value.Sequence == sequence && preview == value &&
            settings().PrivacyConsent && settings().Translation.Enabled &&
            TranslationRequest.Create(settings(), value.Snapshot.Text).CacheKey == value.Request.CacheKey;
    }
    public void SuppressOwnReplacement(InputSnapshot original, string translated)
    {
        lock (gate) { suppression = (original.Identity, translated, original.EditRange?.Replace(translated), null); }
        _ = ObserveAsync(null, true);
    }
    public void Dispose()
    {
        lock (gate) { disposed = true; pending?.Cancel(); pending?.Dispose(); preview = null; current = null; }
        // 忽略取消的服务商可能仍占用信号量，此时不能提前释放信号量对象。
    }
}
