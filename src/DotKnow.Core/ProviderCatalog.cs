namespace DotKnow.Core;

public sealed record ProviderDefinition(string Id, string Label, string BaseUrl, string DefaultModel, string PlatformUrl);

public static class ProviderCatalog
{
    // 开通入口固定指向官方平台，不使用配置中的自定义地址拼接购买链接。
    public static IReadOnlyList<ProviderDefinition> All { get; } = Array.AsReadOnly(new[]
    {
        new ProviderDefinition("DeepSeek", "DeepSeek", "https://api.deepseek.com", "deepseek-flash", "https://platform.deepseek.com/"),
        new ProviderDefinition("OpenAI", "OpenAI", "https://api.openai.com/v1", "gpt-4.1-mini", "https://platform.openai.com/"),
        new ProviderDefinition("Anthropic", "Claude（Anthropic）", "https://api.anthropic.com/v1", "claude-sonnet-4-5", "https://console.anthropic.com/"),
        new ProviderDefinition("Gemini", "Gemini（Google）", "https://generativelanguage.googleapis.com/v1beta", "gemini-2.5-flash", "https://aistudio.google.com/apikey"),
        new ProviderDefinition("Qwen", "通义千问（阿里云百炼）", "https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen-plus", "https://bailian.console.aliyun.com/")
    });
    public static ProviderDefinition Get(string id) => All.FirstOrDefault(x => x.Id == id)
        ?? throw new ArgumentException("不支持所选 API 服务商。");
    public static AiSettings Defaults(string id) => new()
    { Provider = id, BaseUrl = Get(id).BaseUrl, Model = Get(id).DefaultModel };
}
