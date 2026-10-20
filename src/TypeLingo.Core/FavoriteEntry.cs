namespace TypeLingo.Core;

public sealed record FavoriteEntry(Guid Id, string Original, string Translation, string SourceLanguage,
    string TargetLanguage, string Mode, DateTimeOffset CreatedAt, DateTimeOffset DueAt,
    int ReviewCount = 0, int IntervalDays = 0, string TaskId = "translate", string Question = "")
{
    public FavoriteEntry Review(bool remembered, DateTimeOffset now)
    {
        var days = remembered ? IntervalDays switch { < 1 => 1, < 3 => 3, < 7 => 7, < 14 => 14, < 30 => 30, _ => 60 } : 0;
        return this with { ReviewCount = ReviewCount + 1, IntervalDays = days,
            DueAt = remembered ? now.AddDays(days) : now.AddMinutes(10) };
    }
}
