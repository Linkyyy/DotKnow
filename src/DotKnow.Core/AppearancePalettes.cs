using System.Collections.ObjectModel;

namespace DotKnow.Core;

/// <summary>配色与明暗模式独立；旧配置自动使用官网浅纸配色。</summary>
public sealed record AppearancePalette(string Id, string Label, string Description, PaletteColors Light, PaletteColors Dark);

public sealed record PaletteColors(string Canvas, string Surface, string Card, string Sidebar, string Border,
    string Hover, string Selection, string Text, string Muted, string Accent, string AccentText,
    string AccentSurface, string Highlight, string HighlightText, string Hero, string HeroText,
    string HeroMuted, string Input, string Focus, string? HeroHighlight = null)
{
    public IReadOnlyDictionary<string, string> Resources => new ReadOnlyDictionary<string, string>(new Dictionary<string, string>
    {
        ["CanvasBrush"] = Canvas, ["SurfaceBrush"] = Surface, ["CardBrush"] = Card, ["SidebarBrush"] = Sidebar,
        ["BorderBrush"] = Border, ["HoverBrush"] = Hover, ["SelectionBrush"] = Selection,
        ["TextBrush"] = Text, ["MutedBrush"] = Muted, ["AccentBrush"] = Accent, ["AccentTextBrush"] = AccentText,
        ["AccentSurfaceBrush"] = AccentSurface, ["HighlightBrush"] = Highlight, ["HighlightTextBrush"] = HighlightText,
        ["HeroBrush"] = Hero, ["HeroTextBrush"] = HeroText, ["HeroMutedBrush"] = HeroMuted,
        ["InputBrush"] = Input, ["FocusBrush"] = Focus, ["HeroHighlightBrush"] = HeroHighlight ?? Highlight
    });
}

public static class AppearancePalettes
{
    public const string DefaultId = "paper";
    public static IReadOnlyList<AppearancePalette> All { get; } = Array.AsReadOnly(new[]
    {
        new AppearancePalette("paper", "浅纸", "米白与荧光绿，延续官网风格",
            new("#F7F8F2", "#FBFCF7", "#FFFFFF", "#EFF2E7", "#DCE0D5", "#EDF1E4", "#E5EECF",
                "#202722", "#5B6851", "#253329", "#F7F8F2", "#E7EBDF", "#D9FA82", "#26321C",
                "#253329", "#F5F8EE", "#BCCCAF", "#F3F5EC", "#5C7E34"),
            new("#1D261F", "#242F26", "#29352B", "#18221B", "#43533D", "#344331", "#3F5232",
                "#F1F4E8", "#BAC8AE", "#D9FA82", "#24311B", "#34442E", "#D9FA82", "#25321D",
                "#273B29", "#F5F9EC", "#BDD2AE", "#202C22", "#D9FA82")),
        new AppearancePalette("white", "纯白", "纯白背景与中性灰，清晰简洁",
            new("#FFFFFF", "#FFFFFF", "#FFFFFF", "#F5F5F5", "#DEDEDE", "#F0F0F0", "#E5E7EB",
                "#202124", "#60636A", "#30343B", "#FFFFFF", "#F4F4F5", "#E5E7EB", "#202124",
                "#F4F4F5", "#202124", "#60636A", "#FFFFFF", "#555B66", "#555B66"),
            new("#202124", "#27282C", "#2D2F33", "#191A1D", "#505259", "#393C42", "#41454D",
                "#F5F5F5", "#C3C5CA", "#E5E7EB", "#202124", "#35373C", "#E5E7EB", "#202124",
                "#292B30", "#F5F5F5", "#C3C5CA", "#25262A", "#D4D7DE")),
        new AppearancePalette("forest", "深林", "低饱和草木绿，让阅读安静下来",
            new("#F0F5EE", "#F7FAF4", "#FCFEFA", "#E5EEDF", "#D3DFCB", "#E2ECD9", "#D5E7C5",
                "#24392E", "#576E54", "#294B3A", "#F6FAF2", "#E0ECD7", "#C9ED9D", "#263D25",
                "#244432", "#F4F9EE", "#BDCFAD", "#EAF1E3", "#456F37"),
            new("#16291F", "#1D3326", "#243D2E", "#122219", "#3F5A42", "#2D4832", "#37553A",
                "#F2F8EE", "#B5CBAE", "#BEE78F", "#21351F", "#29472D", "#BEE78F", "#21351F",
                "#213D2A", "#F4FAEF", "#B9D3AE", "#1A3022", "#BEE78F")),
        new AppearancePalette("ocean", "雾蓝", "清透的蓝灰，为专注留一点空间",
            new("#F2F6F9", "#F9FBFD", "#FFFFFF", "#E8EFF4", "#D4E0E8", "#E7F0F5", "#DCEBF2",
                "#233443", "#516879", "#294D67", "#F7FBFD", "#E1EDF4", "#C5E7F4", "#253E51",
                "#253E51", "#F4F9FC", "#BED2DF", "#EBF2F7", "#426F8A"),
            new("#192832", "#20323F", "#263C49", "#15232C", "#405968", "#2D4655", "#345466",
                "#F0F6F9", "#B4CAD8", "#A8D7EB", "#203743", "#294654", "#A8D7EB", "#203743",
                "#203746", "#F2F8FB", "#BCD5E2", "#1C2E3A", "#A8D7EB")),
        new AppearancePalette("lavender", "暮紫", "柔和的丁香紫，给表达一点灵感",
            new("#F7F4FA", "#FCFAFE", "#FFFFFF", "#EFE8F5", "#E0D7E9", "#F0E9F7", "#E8DDF4",
                "#342B40", "#72617F", "#514166", "#FBF8FE", "#ECE4F3", "#E3D4FB", "#49325C",
                "#42334F", "#FCF7FF", "#D7C7E3", "#F1EBF7", "#7D559D"),
            new("#292232", "#332B3E", "#3B3147", "#231D2B", "#5D496B", "#463651", "#513D60",
                "#F6F1FA", "#D0BDDC", "#D9BDF4", "#352640", "#463251", "#D9BDF4", "#352640",
                "#382946", "#FCF6FF", "#DAC5E9", "#2E2538", "#D9BDF4"))
    });

    public static bool Contains(string id) => All.Any(x => x.Id == id);
    public static AppearancePalette Get(string id) => All.FirstOrDefault(x => x.Id == id) ?? All[0];
    public static PaletteColors Resolve(string palette, string theme, bool systemDark) =>
        AppearanceTheme.IsDark(theme, systemDark) ? Get(palette).Dark : Get(palette).Light;
}
