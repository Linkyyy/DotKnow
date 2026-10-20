using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TypeLingo.Core;

public record InputRect(double X, double Y, double Width, double Height);
public sealed record InputSnapshot(string Identity, string Text, InputRect Bounds,
    bool CanWrite = true, bool IsComposing = false, bool IsSensitive = false,
    bool IsSelection = false, InputRect? ControlBounds = null, InputTextRange? EditRange = null)
{
    public bool SameContent(InputSnapshot? other) => other != null && Identity == other.Identity && Text == other.Text &&
        CanWrite == other.CanWrite && IsSelection == other.IsSelection && EditRange == other.EditRange &&
        IsComposing == other.IsComposing && IsSensitive == other.IsSensitive;
}

public sealed record TranslationRequest(string Provider, string Model, string SourceLanguage,
    string TargetLanguage, string Mode, string Text, string BaseUrl, string PromptVersion = "1")
{
    public string CacheKey => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new[] { Provider, Model, SourceLanguage, TargetLanguage, Mode, PromptVersion, Text, BaseUrl }))));

    public static TranslationRequest Create(AppSettings settings, string text) => new(settings.AI.Provider,
        settings.AI.Model, settings.Translation.SourceLanguage, settings.Translation.TargetLanguage,
        settings.Translation.Mode, text, settings.AI.BaseUrl.TrimEnd('/'));
}
public sealed record TranslationResult(string Text, int PromptTokens = 0, int CompletionTokens = 0);
public enum TranslationOrigin { Online, Cache, Vocabulary }
public sealed record Preview(InputSnapshot Snapshot, TranslationRequest Request, TranslationResult Result, long Sequence, bool FromCache,
    TranslationOrigin Origin = TranslationOrigin.Online);
public interface ITranslationMemory
{
    Task<TranslationResult?> FindAsync(TranslationRequest request, CancellationToken cancellationToken);
    Task LearnAsync(TranslationRequest request, TranslationResult result, CancellationToken cancellationToken);
}
public enum TranslationError { Authentication, Configuration, RateLimit, Server, Network, Timeout, InvalidResponse }
public sealed class TranslationException(TranslationError kind, string safeMessage) : Exception(safeMessage)
{
    public TranslationError Kind { get; } = kind;
}
public interface ITranslationProvider
{
    Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken);
}
public interface ITranslationCache
{
    Task<TranslationResult?> GetAsync(string key, CancellationToken cancellationToken);
    Task PutAsync(string key, TranslationResult result, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken = default);
}
public interface IInputTarget
{
    Task<InputSnapshot?> CaptureAsync(CancellationToken cancellationToken);
    // 实现必须在写入前重新校验控件身份、原文、焦点、隐私授权和组合输入状态。
    Task<bool> TryReplaceAsync(InputSnapshot expected, string translation, CancellationToken cancellationToken);
}

public sealed class SessionStatistics
{
    private long apiCalls, hits, vocabularyHits, successes, failures, elapsedTicks, promptTokens, completionTokens;
    public long ApiCalls => Interlocked.Read(ref apiCalls);
    public long CacheHits => Interlocked.Read(ref hits);
    public long VocabularyHits => Interlocked.Read(ref vocabularyHits);
    public long Successes => Interlocked.Read(ref successes);
    public long Failures => Interlocked.Read(ref failures);
    public long PromptTokens => Interlocked.Read(ref promptTokens);
    public long CompletionTokens => Interlocked.Read(ref completionTokens);
    public double AverageLatencyMilliseconds => Successes == 0 ? 0 : TimeSpan.FromTicks(Interlocked.Read(ref elapsedTicks) / Successes).TotalMilliseconds;
    public void ApiCall() => Interlocked.Increment(ref apiCalls);
    public void CacheHit() => Interlocked.Increment(ref hits);
    public void VocabularyHit() => Interlocked.Increment(ref vocabularyHits);
    public void Failed() => Interlocked.Increment(ref failures);
    public void Succeeded(TimeSpan elapsed, TranslationResult result)
    {
        Interlocked.Increment(ref successes); Interlocked.Add(ref elapsedTicks, elapsed.Ticks);
        Interlocked.Add(ref promptTokens, result.PromptTokens); Interlocked.Add(ref completionTokens, result.CompletionTokens);
    }
}
