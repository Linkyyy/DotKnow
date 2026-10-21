using System.Text;
using DotKnow.Core;
using DotKnow.Infrastructure;

namespace DotKnow.Tests;

public sealed class FavoriteStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "DotKnow-favorites-" + Guid.NewGuid().ToString("N"));
    private readonly TestProtector protector = new();
    private DateTimeOffset time = new(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
    private FavoriteStore Store() => new(directory, protector, () => time);
    private static Preview Sample(string original = "复习原文", string translation = "Review translation")
    {
        var s = new InputSnapshot("input", original, new(0, 0, 1, 1));
        return new(s, TranslationRequest.Create(new(), original), new(translation), 1, false);
    }
    [Fact] public void Add_PersistsEncryptedOriginalAndTranslation_AndDeduplicates()
    {
        var store = Store(); var first = store.Add(Sample()); var duplicate = store.Add(Sample());
        Assert.Equal(first.Id, duplicate.Id); Assert.Single(Store().GetAll());
        Assert.Equal(first, Store().GetAll()[0]); Assert.Single(store.GetDue());
        var content = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(directory, "favorites.dpapi")));
        Assert.DoesNotContain("复习原文", content); Assert.DoesNotContain("Review translation", content);
        Assert.Equal(first.Id, store.GetDue()[0].Id);
    }
    [Fact] public void LanguageAndTranslationVariants_AreSeparateFavorites()
    {
        var p = Sample(); var store = Store(); store.Add(p);
        store.Add(p with { Request = p.Request with { TargetLanguage = "en" } });
        store.Add(Sample(translation: "Another translation")); Assert.Equal(3, Store().GetAll().Count);
    }
    [Fact] public void Review_PersistsScheduleAndReturnsOnlyDueEntries()
    {
        var store = Store(); var entry = store.Add(Sample());
        var reviewed = store.Review(entry.Id, true)!;
        Assert.Equal(1, reviewed.ReviewCount); Assert.Equal(1, reviewed.IntervalDays); Assert.Empty(Store().GetDue());
        time = time.AddDays(1); Assert.Single(Store().GetDue());
        reviewed = Store().Review(entry.Id, true)!; Assert.Equal(3, reviewed.IntervalDays);
        Assert.Equal(time.AddDays(3), reviewed.DueAt);
        reviewed = Store().Review(entry.Id, false)!; Assert.Equal(0, reviewed.IntervalDays);
        Assert.Equal(time.AddMinutes(10), reviewed.DueAt); Assert.Empty(Store().GetDue());
        time = time.AddMinutes(10); Assert.Single(Store().GetDue());
        Assert.Equal(3, Store().GetAll()[0].ReviewCount);
    }
    [Fact] public void Delete_RemovesContentAndReviewProgressAcrossReloads()
    {
        var store = Store(); var entry = store.Add(Sample()); store.Review(entry.Id, true);
        Assert.True(store.Delete(entry.Id)); Assert.False(store.Delete(entry.Id));
        Assert.Empty(Store().GetAll()); Assert.Null(Store().Review(entry.Id, true));
    }
    [Fact] public void CorruptFile_IsPreservedAndNeverOverwrittenByAddOrDelete()
    {
        Directory.CreateDirectory(directory); var path = Path.Combine(directory, "favorites.dpapi");
        var invalid = protector.Protect("{invalid json"u8.ToArray()); File.WriteAllBytes(path, invalid);
        Assert.Throws<IOException>(() => Store().GetAll());
        Assert.Throws<IOException>(() => Store().Add(Sample()));
        Assert.Throws<IOException>(() => Store().Delete(Guid.NewGuid()));
        Assert.Equal(invalid, File.ReadAllBytes(path));
    }
    [Fact] public void TamperedCiphertext_IsPreserved()
    {
        Store().Add(Sample()); var path = Path.Combine(directory, "favorites.dpapi");
        var data = File.ReadAllBytes(path); data[^1] ^= 1; File.WriteAllBytes(path, data);
        Assert.Throws<IOException>(() => Store().Add(Sample())); Assert.Equal(data, File.ReadAllBytes(path));
    }
    [Fact] public void ExistingSettings_MigrateLoadingDefault_AndCanDisableIt()
    {
        Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "settings.json"), "{}");
        var settings = new SettingsService(directory, protector); settings.Load(); Assert.True(settings.Current.Overlay.ShowLoading);
        settings.Save(settings.Current with { Overlay = settings.Current.Overlay with { ShowLoading = false } });
        settings.Load(); Assert.False(settings.Current.Overlay.ShowLoading);
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
