namespace TypeLingo.Core;

/// <summary>保留完整输入及精确范围；替换时保留范围以外的内容和换行。</summary>
public sealed record InputTextRange(string FullText, int Start, int Length)
{
    public bool IsValid => Start >= 0 && Length >= 0 && Start <= FullText.Length - Length;
    public string Text => IsValid ? FullText.Substring(Start, Length) : throw new ArgumentOutOfRangeException(nameof(Start));
    public string Replace(string translation) => IsValid ? FullText[..Start] + translation + FullText[(Start + Length)..] :
        throw new ArgumentOutOfRangeException(nameof(Start));

    public static InputTextRange LineAt(string text, int caret)
    {
        if (caret < 0 || caret > text.Length) throw new ArgumentOutOfRangeException(nameof(caret));
        // CRLF 内部的位置视为前一行行尾。
        if (caret > 0 && caret < text.Length && text[caret] == '\n' && text[caret - 1] == '\r') caret--;
        var start = caret;
        while (start > 0 && text[start - 1] is not ('\r' or '\n')) start--;
        var end = caret;
        while (end < text.Length && text[end] is not ('\r' or '\n')) end++;
        return new(text, start, end - start);
    }

    // UIA 提供程序可能把 CRLF 标准化为 CR。仅在全文和范围均可核对时映射回 ValuePattern。
    public static InputTextRange? FromAutomation(string value, string document, string prefix, string selected)
    {
        static string Normalize(string s) => s.Replace("\r\n", "\n").Replace('\r', '\n');
        var normalized = Normalize(value); var before = Normalize(prefix); var part = Normalize(selected);
        var normalizedDocument = Normalize(document);
        if ((normalizedDocument != normalized && normalizedDocument != normalized + "\n") || before.Length > normalized.Length ||
            !normalized.StartsWith(before, StringComparison.Ordinal) ||
            !normalized.AsSpan(before.Length).StartsWith(part.AsSpan(), StringComparison.Ordinal)) return null;
        int Offset(int count)
        {
            var index = 0;
            for (var n = 0; n < count; n++, index++)
                if (value[index] == '\r' && index + 1 < value.Length && value[index + 1] == '\n') index++;
            return index;
        }
        var start = Offset(before.Length); var end = Offset(before.Length + part.Length);
        return new(value, start, end - start);
    }
}
