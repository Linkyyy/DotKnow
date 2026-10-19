using TypeLingo.Core;

namespace TypeLingo.Infrastructure;

// 保留 DeepSeek 专用入口，统一传输、原生协议与错误处理由 ProviderApi 实现。
public sealed class DeepSeekTranslationProvider(HttpClient client, Func<AppSettings> settings,
    Func<string> getApiKey, SessionStatistics statistics) : ITranslationProvider
{
    public const string SystemPrompt = TranslationPrompt.SystemPrompt;
    private readonly ProviderApi api = new(client, settings, _ => getApiKey(), statistics);
    public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken) =>
        api.TranslateAsync(request, cancellationToken);
    public Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken cancellationToken = default) => api.GetModelsAsync(cancellationToken);
}
