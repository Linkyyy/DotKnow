using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TypeLingo.Core;
using TypeLingo.Infrastructure;

namespace TypeLingo.Tests;

public sealed class VocabularyTests
{
    private static TranslationRequest Request(string text = "面包", string source = "zh", string target = "en", string style = "natural") =>
        TranslationRequest.Create(new AppSettings { Translation = new() { SourceLanguage = source, TargetLanguage = target, Mode = style } }, text);
    private static VocabularyEntry Entry(string text = "面包", string answer = "bread", string source = "zh", string target = "en", string style = "natural") =>
        new() { SourceLanguage = source, TargetLanguage = target, SourceText = text, TranslatedText = answer, Style = style };
    private sealed class Folder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TypeLingo-Vocab-" + Guid.NewGuid().ToString("N"));
        public Folder() => Directory.CreateDirectory(Path);
        public VocabularyStore Store(bool bundled = false) => new(Path, new TestProtector(), bundled);
        public string Jsonl(params VocabularyEntry[] records)
        {
            var file = System.IO.Path.Combine(Path, Guid.NewGuid() + ".jsonl");
            File.WriteAllLines(file, records.Select(x => JsonSerializer.Serialize(x, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })));
            return file;
        }
        public void Dispose() => Directory.Delete(Path, true);
    }
    [Fact] public async Task ActualUserBundle_HasVerifiedCountsAndSixDirections_WithoutApi()
    {
        using var folder = new Folder(); using var store = folder.Store(true);
        var counts = await store.StatisticsAsync(); Assert.Equal(32159, counts.Available); Assert.Equal(2441, counts.Ambiguous);
        foreach (var test in new[] { ("米饭", "zh", "en", "rice"), ("rice", "en", "zh", "米饭"), ("米饭", "zh", "ja", "ご飯"),
            ("ご飯", "ja", "zh", "米饭"), ("rice", "en", "ja", "ご飯"), ("ご飯", "ja", "en", "rice") })
            Assert.Equal(test.Item4, (await store.FindAsync(Request(test.Item1, test.Item2, test.Item3), default))?.Text);
        Assert.Equal("ご飯", (await store.FindAsync(Request("米饭", "auto", "ja"), default))?.Text);
        var ambiguous = Request("粥", "zh", "ja"); Assert.Null(await store.FindAsync(ambiguous, default));
        await store.LearnAsync(ambiguous, new("随意猜测"), default); Assert.Null(await store.FindAsync(ambiguous, default));
    }
    [Fact] public async Task ExactMatching_PreservesCaseWhitespaceStyleAndPromptVersion()
    {
        using var folder = new Folder(); using var store = folder.Store();
        await store.ImportAsync(folder.Jsonl(Entry("Hello", "你好")));
        Assert.Equal("你好", (await store.FindAsync(Request("Hello"), default))?.Text);
        foreach (var query in new[] { Request("hello"), Request(" Hello"), Request("Hello "), Request("Hello", style: "business"), Request("Hello") with { PromptVersion = "2" }, Request("Hello", target: "ja") })
            Assert.Null(await store.FindAsync(query, default));
    }
    [Fact] public async Task AutoLanguage_UsesOnlyUnambiguousResults_AndExplicitDirectionsStaySeparate()
    {
        using var folder = new Folder(); using var store = folder.Store();
        await store.ImportAsync(folder.Jsonl(Entry("手紙", "paper", "zh"), Entry("手紙", "letter", "ja")));
        Assert.Null(await store.FindAsync(Request("手紙", "auto"), default));
        await store.LearnAsync(Request("手紙", "auto"), new("guessed answer"), default);
        Assert.Null(await store.FindAsync(Request("手紙", "auto"), default));
        Assert.Equal("letter", (await store.FindAsync(Request("手紙", "ja"), default))?.Text);
        await store.ImportAsync(folder.Jsonl(Entry("手", "hand", "zh"), Entry("手", "hand", "ja")));
        Assert.Equal("hand", (await store.FindAsync(Request("手", "auto"), default))?.Text);
    }
    [Fact] public async Task Conflicts_AreQuarantined_AndLearningCannotResolveThem()
    {
        using var folder = new Folder(); using var store = folder.Store();
        var report = await store.ImportAsync(folder.Jsonl(Entry("bank", "银行", "en", "zh"), Entry("bank", "河岸", "en", "zh")));
        Assert.True(report.Conflicts > 0); Assert.Null(await store.FindAsync(Request("bank", "en", "zh"), default));
        await store.LearnAsync(Request("bank", "en", "zh"), new("银行"), default);
        await store.LearnAsync(Request("bank", "auto", "zh"), new("银行"), default);
        Assert.Null(await store.FindAsync(Request("bank", "auto", "zh"), default));
        var row = Assert.Single(await store.SearchAsync("bank")); await store.EditAsync(row, "银行（人工确认）");
        Assert.Equal("银行（人工确认）", (await store.FindAsync(Request("bank", "en", "zh"), default))?.Text);
    }
    [Fact] public async Task PersonalEditsSurviveImportAndLearning_AndDeletedQueriesAreNotRelearned()
    {
        using var folder = new Folder(); using var store = folder.Store();
        await store.ImportAsync(folder.Jsonl(Entry()));
        var item = Assert.Single(await store.SearchAsync("面包")); await store.EditAsync(item, "a loaf of bread");
        await store.LearnAsync(Request(), new("online answer"), default);
        var report = await store.ImportAsync(folder.Jsonl(Entry(answer: "different imported answer")));
        Assert.Equal(1, report.Preserved); Assert.Equal("a loaf of bread", (await store.FindAsync(Request(), default))?.Text);
        item = Assert.Single(await store.SearchAsync("面包")); await store.DeleteAsync(item);
        await store.LearnAsync(Request(), new("online answer"), default); Assert.Null(await store.FindAsync(Request(), default));
        await store.LearnAsync(Request(source: "auto"), new("must not recreate deleted query"), default);
        Assert.Null(await store.FindAsync(Request(source: "auto"), default));
    }
    [Fact] public async Task LearnedOriginalAndAnswer_AreEncrypted_AndSurviveRestartAndCacheClear()
    {
        using var folder = new Folder(); var request = Request("PRIVATE-ORIGINAL-1234");
        using (var store = folder.Store()) await store.LearnAsync(request, new("PRIVATE-ANSWER-5678"), default);
        var bytes = File.ReadAllBytes(System.IO.Path.Combine(folder.Path, "vocabulary.db")); var raw = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("PRIVATE-ORIGINAL", raw); Assert.DoesNotContain("PRIVATE-ANSWER", raw);
        using var reloaded = folder.Store();
        using var cache = new CacheService(() => new(), System.IO.Path.Combine(folder.Path, "cache.db"), new TestProtector());
        await cache.ClearAsync(); Assert.Equal("PRIVATE-ANSWER-5678", (await reloaded.FindAsync(request, default))?.Text);
    }
    [Fact] public async Task ClearStaysEmptyAfterRestart_AndExplicitRestoreReenablesBuiltin()
    {
        using var folder = new Folder();
        using (var store = folder.Store(true)) { await store.ClearAsync(); Assert.Equal(0, (await store.StatisticsAsync()).Available); }
        using var reloaded = folder.Store(true); Assert.Equal(0, (await reloaded.StatisticsAsync()).Available);
        await reloaded.RestoreBuiltinAsync(); Assert.Equal(32159, (await reloaded.StatisticsAsync()).Available);
    }
    [Fact] public async Task ExportRoundTrip_PreservesAmbiguityAndAutoQueries_WithChecksumsAndAttribution()
    {
        using var folder = new Folder(); using var store = folder.Store();
        await store.LearnAsync(Request("a sentence", "auto", "ja"), new("ある文"), default);
        await store.ImportAsync(folder.Jsonl(Entry("bank", "银行", "en", "zh"), Entry("bank", "河岸", "en", "zh")));
        var file = System.IO.Path.Combine(folder.Path, "export.zip"); await store.ExportAsync(file);
        using (var zip = ZipFile.OpenRead(file)) { Assert.NotNull(zip.GetEntry("checksums.sha256")); Assert.NotNull(zip.GetEntry("licenses/LICENSE.txt")); }
        using var otherFolder = new Folder(); using var other = otherFolder.Store(); await other.ImportAsync(file);
        Assert.Equal("ある文", (await other.FindAsync(Request("a sentence", "auto", "ja"), default))?.Text);
        Assert.Null(await other.FindAsync(Request("bank", "en", "zh"), default));
    }
    [Theory][InlineData("checksum")][InlineData("path")][InlineData("version")]
    public async Task InvalidPackages_DoNotChangeExistingData(string defect)
    {
        using var folder = new Folder(); using var store = folder.Store(); await store.ImportAsync(folder.Jsonl(Entry()));
        var file = System.IO.Path.Combine(folder.Path, "bad.zip");
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
        {
            void Add(string name, string text) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(text); }
            Add("manifest.json", "{\"formatVersion\":\"" + (defect == "version" ? "99" : "1.0") + "\"}");
            Add("entries.jsonl", JsonSerializer.Serialize(Entry(answer: "bad")));
            if (defect == "checksum") Add("checksums.sha256", new string('0', 64) + "  entries.jsonl\n");
            if (defect == "path") Add("../unexpected.txt", "test");
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync(file));
        Assert.Equal("bread", (await store.FindAsync(Request(), default))?.Text);
    }
    [Fact] public async Task CancelledImport_DoesNotPersistPartialChanges()
    {
        using var folder = new Folder(); using var store = folder.Store(); await store.InitializeAsync();
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ImportAsync(folder.Jsonl(Entry()), cancel.Token));
        Assert.Equal(0, (await store.StatisticsAsync()).Available);
    }
    [Fact] public async Task CorruptEncryptedRows_ArePreserved_AndCannotBeOverwrittenByLearning()
    {
        using var folder = new Folder(); using (var store = folder.Store()) await store.LearnAsync(Request(), new("bread"), default);
        var file = System.IO.Path.Combine(folder.Path, "vocabulary.db");
        using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString()))
        { db.Open(); using var command = db.CreateCommand(); command.CommandText = "UPDATE vocabulary SET value=zeroblob(64)"; command.ExecuteNonQuery(); }
        var original = File.ReadAllBytes(file); using var damaged = folder.Store();
        await Assert.ThrowsAnyAsync<Exception>(() => damaged.LearnAsync(Request(), new("must not overwrite"), default));
        Assert.Equal(original, File.ReadAllBytes(file));
    }
    [Fact] public async Task DuplicateAndMalformedRows_AreReportedWithoutOverwritingExistingText()
    {
        using var folder = new Folder(); using var store = folder.Store(); var file = folder.Jsonl(Entry(), Entry());
        await File.AppendAllTextAsync(file, "\n{bad json}\n{\"sourceText\":\"incomplete\"}\n");
        var result = await store.ImportAsync(file); Assert.Equal(1, result.Added); Assert.Equal(1, result.Duplicates); Assert.Equal(2, result.Invalid);
        await store.ImportAsync(folder.Jsonl(Entry(answer: "other"))); Assert.Null(await store.FindAsync(Request(), default));
    }
    private sealed class FailingProtector : IDataProtector
    {
        private readonly TestProtector inner = new(); private int writes;
        public byte[] Protect(byte[] value) => ++writes == 2 ? throw new IOException("simulated disk encryption failure") : inner.Protect(value);
        public byte[] Unprotect(byte[] value) => inner.Unprotect(value);
    }
    [Fact] public async Task MidImportFailure_RollsBackDatabaseAndMemory()
    {
        using var folder = new Folder();
        using (var store = new VocabularyStore(folder.Path, new FailingProtector(), false))
        {
            await Assert.ThrowsAsync<IOException>(() => store.ImportAsync(folder.Jsonl(Entry(), Entry("水", "water"))));
            Assert.Equal(0, (await store.StatisticsAsync()).Available);
        }
        using var reopened = folder.Store(); Assert.Equal(0, (await reopened.StatisticsAsync()).Available);
    }
    [Fact] public void DefaultsAndSavedPreferences_AreIndependentOfCacheSettings()
    {
        using var folder = new Folder(); var settings = new SettingsService(folder.Path, new TestProtector());
        Assert.True(settings.Current.Vocabulary.PreferOffline); Assert.True(settings.Current.Vocabulary.AutoLearn); Assert.True(settings.Current.Vocabulary.AllowOnlineFallback);
        settings.Save(settings.Current with { Vocabulary = new() { PreferOffline = false, AutoLearn = false, AllowOnlineFallback = false } });
        settings.Load(); Assert.False(settings.Current.Vocabulary.PreferOffline); Assert.False(settings.Current.Vocabulary.AutoLearn); Assert.False(settings.Current.Vocabulary.AllowOnlineFallback);
        Assert.Contains("vocabulary", SettingsNavigation.Search("offline", "en-US"));
    }
}
