using TypeLingo.Core;

namespace TypeLingo.App;

internal static class UiText
{
    public static string Language { get; private set; } = "zh-CN";
    public static event Action? Changed;
    public static string Get(string text) => UiCatalog.Get(Language, text);
    public static string Format(string key, params object[] values) => UiCatalog.Format(Language, key, values);
    public static void Apply(string language)
    {
        Language = UiCatalog.SupportedLanguages.Contains(language) ? language : "zh-CN";
        // 动态资源同步刷新已打开的窗口，不必重建窗口或重启应用。
        foreach (var key in UiCatalog.Keys) System.Windows.Application.Current.Resources[key] = Get(key);
        Changed?.Invoke();
    }
}
