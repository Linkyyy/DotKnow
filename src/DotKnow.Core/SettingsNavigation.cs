namespace DotKnow.Core;

public sealed record SettingsSection(string Id, string Title, string Description, string[] SearchTerms);

public static class SettingsNavigation
{
    // 搜索只索引设置名称，不触及密钥、原文或译文。
    public static IReadOnlyList<SettingsSection> Sections { get; } = Array.AsReadOnly(new[]
    {
        new SettingsSection("home", "主页", "划选助手状态与常用设置一览", ["状态概览", "实时翻译", "缓存统计", "点解"]),
        new SettingsSection("prompts", "任务与提示词", "百科、翻译和你的专属提问方式", ["预设", "默认任务", "自定义提示词", "划选后输入问题", "百科", "解释", "摘要", "代码解读", "回答语言", "立即执行"]),
        new SettingsSection("general", "常规设置", "选择语言、翻译方向与划词偏好", ["鼠标划选后出现（默认）", "划词翻译", "微信", "微信划选兼容", "WeChat", "Weixin", "网页", "文档", "阅读", "自动查询窗口显示时机", "查询中显示 Loading", "查询完成后显示", "翻译窗口显示时机", "翻译完成后显示", "悬浮窗出现方式", "按快捷键出现", "选中文字后出现", "界面语言", "翻译方向", "源语言", "目标语言", "翻译风格", "防抖时间（毫秒）", "最大输入字符数"]),
        new SettingsSection("vocabulary", "离线题库", "优先查询本地词库，积累常用表达", ["优先使用离线题库", "自动积累在线译文", "未命中时允许在线翻译", "导入题库", "导出题库", "清空题库", "恢复内置词库", "搜索原文或译文"]),
        new SettingsSection("ai", "AI 服务与模型", "管理模型服务、账户密钥与连接", ["API 服务商", "API 密钥", "API 地址", "模型", "购买平台", "测试翻译", "超时", "重试"]),
        new SettingsSection("cache", "结果缓存", "减少重复请求，管理本地存储", ["内存缓存", "磁盘缓存", "有效期", "容量", "清除缓存", "请求统计"]),
        new SettingsSection("appearance", "外观与悬浮窗", "让助手窗口融入你的桌面", ["自动隐藏（秒）", "半透明", "深色模式", "字体大小", "不透明度", "最大宽度", "位置偏好", "主题", "选择你的配色", "浅纸", "纯白", "深林", "雾蓝", "暮紫", "明暗模式", "浅色", "深色", "跟随系统"]),
        new SettingsSection("hotkeys", "快捷键", "使用键盘快速预览、复制与替换", ["显示查询面板", "Ctrl+Alt+T", "准备替换 / 显示确认", "开启 / 暂停全部读取", "复制当前结果"]),
        new SettingsSection("privacy", "隐私与应用", "控制监控范围与数据访问", ["监控所有应用", "仅允许指定应用", "排除的进程名", "密码框", "隐私授权", "暂停所有读取"]),
        new SettingsSection("favorites", "本地收藏与复习", "保存知识与表达，用卡片巩固记忆", ["收藏译文", "搜索本地收藏", "开始到期复习", "复习", "记住了"])
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
