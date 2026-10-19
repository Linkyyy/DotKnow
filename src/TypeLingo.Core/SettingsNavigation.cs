namespace TypeLingo.Core;

public sealed record SettingsSection(string Id, string Title, string Description, string[] SearchTerms);

public static class SettingsNavigation
{
    // 搜索只索引设置名称，不触及密钥、原文或译文。
    public static IReadOnlyList<SettingsSection> Sections { get; } = Array.AsReadOnly(new[]
    {
        new SettingsSection("home", "主页", "翻译状态与常用设置一览", ["状态概览", "实时翻译", "缓存统计"]),
        new SettingsSection("general", "常规设置", "选择语言、翻译方向与输入偏好", ["界面语言", "翻译方向", "源语言", "目标语言", "翻译风格", "防抖时间（毫秒）", "最大输入字符数"]),
        new SettingsSection("ai", "AI 服务与模型", "管理模型服务、账户密钥与连接", ["API 服务商", "API 密钥", "API 地址", "模型", "购买平台", "测试翻译", "超时", "重试"]),
        new SettingsSection("cache", "翻译缓存", "减少重复请求，管理本地存储", ["内存缓存", "磁盘缓存", "有效期", "容量", "清除缓存", "请求统计"]),
        new SettingsSection("appearance", "外观与悬浮窗", "让翻译窗口融入你的桌面", ["深色模式", "字体大小", "不透明度", "最大宽度", "位置偏好", "主题"]),
        new SettingsSection("hotkeys", "快捷键", "使用键盘快速预览、复制与替换", ["准备替换 / 显示确认", "开启 / 暂停全部读取", "复制当前译文"]),
        new SettingsSection("privacy", "隐私与应用", "控制监控范围与数据访问", ["监控所有应用", "仅允许指定应用", "排除的进程名", "密码框", "隐私授权", "暂停所有读取"])
    });

    public static IReadOnlyList<string> Search(string query, string language)
    {
        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Sections.Where(section =>
        {
            var original = new[] { section.Title, section.Description }.Concat(section.SearchTerms).ToArray();
            var text = string.Join(' ', original.Concat(original.Select(x => UiCatalog.Get(language, x))).Append(section.Id));
            return words.All(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
        }).Select(x => x.Id).ToArray();
    }
}
