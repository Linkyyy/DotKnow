namespace DotKnow.Core;

public sealed record PromptPreset(string Id, string Name, string Instructions);

public sealed record AssistantSettings
{
    public string PresetId { get; init; } = "encyclopedia";
    public string SelectionAction { get; init; } = "ask";
    public string ResponseLanguage { get; init; } = "zh";
    public PromptPreset[] CustomPresets { get; init; } = [];

    public void Validate()
    {
        if (CustomPresets == null || CustomPresets.Length > 20 ||
            CustomPresets.Any(x => x == null || string.IsNullOrEmpty(x.Id) || !x.Id.StartsWith("custom-", StringComparison.Ordinal) || x.Id.Length > 64 ||
                x.Id.Length < 8 || string.IsNullOrWhiteSpace(x.Name) || x.Name.Length > 40 ||
                string.IsNullOrWhiteSpace(x.Instructions) || x.Instructions.Length > 6000) ||
            CustomPresets.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != CustomPresets.Length)
            throw new ArgumentException("自定义提示词最多 20 套；名称 1–40 字，提示词 1–6000 字，标识不能重复。");
        if (!AssistantPresets.All.Concat(CustomPresets).Any(x => x.Id == PresetId) ||
            SelectionAction is not ("ask" or "instant") || ResponseLanguage is not ("zh" or "en" or "ja"))
            throw new ArgumentException("助手任务、划选动作或回答语言无效。");
    }
}

public static class AssistantPresets
{
    public static IReadOnlyList<PromptPreset> All { get; } = Array.AsReadOnly(new[]
    {
        new PromptPreset("encyclopedia", "百科", "Explain the selected term or topic accurately. Start with a brief definition, then give essential context and one concrete example. Distinguish established facts from uncertainty. Do not claim to have searched the web or invent citations."),
        new PromptPreset("translate", "翻译", "Translate the selected text using the configured languages and translation style."),
        new PromptPreset("explain", "通俗解释", "Explain the selected passage in plain language for a curious beginner. Break down difficult concepts and give a short analogy or example. Point out any ambiguity instead of guessing."),
        new PromptPreset("summarize", "提炼重点", "Summarize only the selected text. Give its main point followed by up to five key points. Preserve caveats, numbers and attribution; do not add facts absent from the text."),
        new PromptPreset("polish", "写作润色", "Improve the clarity, grammar and flow of the selected text while preserving its meaning and language. Return the revised text first, followed by a short explanation of important edits. Do not invent details."),
        new PromptPreset("code", "代码解读", "Explain what the selected code does, its inputs and outputs, and any evident pitfalls. State assumptions when surrounding context is missing. Do not execute the code or claim to have tested it."),
        new PromptPreset("ask", "自由提问", "Answer the user's question about the selected material. Be concrete and concise. If context is insufficient, say what is missing. Do not invent facts, sources or browsing results.")
    });

    public static IEnumerable<PromptPreset> For(AssistantSettings settings) => All.Concat(settings.CustomPresets);

    // TranslationRequest.Create remains the explicit translation API for compatibility with old caches and dictionaries.
    public static TranslationRequest CreateRequest(AppSettings settings, string text, string? presetId = null, string? question = null)
    {
        var preset = For(settings.Assistant).FirstOrDefault(x => x.Id == (presetId ?? settings.Assistant.PresetId))
            ?? throw new ArgumentException("所选提示词已不存在，请重新选择。");
        question = question?.Trim() ?? "";
        if (question.Length > 4000) throw new ArgumentException("补充问题最多 4000 字。");
        if (preset.Id == "ask" && question.Length == 0) throw new ArgumentException("请输入你想问的问题。");
        var request = TranslationRequest.Create(settings, text);
        if (preset.Id == "translate" && question.Length == 0) return request;
        var instructions = preset.Id == "translate"
            ? $"Translate the selected text from {request.SourceLanguage} to {request.TargetLanguage}, with {request.Mode} style. Address the user's additional request."
            : preset.Instructions;
        return request with { TaskId = preset.Id == "translate" ? "translation-question" : preset.Id,
            Instructions = instructions, Question = question, ResponseLanguage = settings.Assistant.ResponseLanguage };
    }
}
