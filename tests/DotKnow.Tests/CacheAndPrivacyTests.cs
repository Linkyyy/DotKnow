using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using DotKnow.Core;
using DotKnow.Infrastructure;

namespace DotKnow.Tests;

// 在 Linux 测试中使用带认证的加密器检验存储协议；生产环境使用 Windows DPAPI。
internal sealed class TestProtector : IDataProtector
{
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);
    public byte[] Protect(byte[] value)
    {
        var nonce = RandomNumberGenerator.GetBytes(12); var cipher = new byte[value.Length]; var tag = new byte[16];
        using var aes = new AesGcm(Key, 16); aes.Encrypt(nonce, value, cipher, tag); return [..nonce, ..tag, ..cipher];
    }
    public byte[] Unprotect(byte[] value)
    {
        var plain = new byte[value.Length - 28]; using var aes = new AesGcm(Key, 16);
        aes.Decrypt(value.AsSpan(0, 12), value.AsSpan(28), value.AsSpan(12, 16), plain); return plain;
    }
}
public sealed class CacheAndPrivacyTests
{
    [Fact] public void CacheKey_PreservesExactTextAndEveryConfigurationDimension()
    {
        var r = TranslationRequest.Create(new AppSettings(), "hello\n world");
        var variants = new[] { r with { Text = "hello world" }, r with { Text = "hello\nworld" }, r with { Provider = "other" },
            r with { Model = "other" }, r with { SourceLanguage = "zh" }, r with { TargetLanguage = "en" },
            r with { Mode = "business" }, r with { PromptVersion = "2" }, r with { BaseUrl = "https://other.example" } };
        Assert.All(variants, other => Assert.NotEqual(r.CacheKey, other.CacheKey));
        Assert.Equal(r.CacheKey, (r with { }).CacheKey);
    }
    [Fact] public async Task MemoryCache_EnforcesLruAndTtl()
    {
        var config = new AppSettings { Cache = new() { MemoryMaxEntries = 2, MemoryTTLMinutes = 1 } };
        var clock = new FakeClock(); using var cache = new CacheService(() => config, "unused.db", new TestProtector(), clock);
        await cache.PutAsync("a", new("a"), default); await cache.PutAsync("b", new("b"), default);
        Assert.NotNull(await cache.GetAsync("a", default)); await cache.PutAsync("c", new("c"), default);
        Assert.Null(await cache.GetAsync("b", default)); Assert.NotNull(await cache.GetAsync("a", default));
        clock.Advance(TimeSpan.FromMinutes(2)); Assert.Null(await cache.GetAsync("a", default));
    }
    [Fact] public async Task DiskCache_DefaultOff_DoesNotCreateDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        using var cache = new CacheService(() => new(), path, new TestProtector());
        await cache.PutAsync("key", new("private text"), default); Assert.False(File.Exists(path));
    }
    [Fact] public async Task DiskCache_Encrypts_Persists_Expires_Caps_AndClearsEvenWhenDisabled()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db"); var clock = new FakeClock();
        var config = new AppSettings { Cache = new() { MemoryEnabled = false, DiskEnabled = true, DiskMaxEntries = 2, DiskTTLDays = 1 } };
        try
        {
            using (var cache = new CacheService(() => config, path, new TestProtector(), clock))
            {
                await cache.PutAsync("a", new("PRIVATE_TRANSLATION"), default);
                Assert.DoesNotContain("PRIVATE_TRANSLATION", ReadShared(path));
                await cache.PutAsync("b", new("second"), default); await cache.PutAsync("c", new("third"), default);
                Assert.Null(await cache.GetAsync("a", default));
            }
            Assert.DoesNotContain("PRIVATE_TRANSLATION", ReadShared(path));
            using (var cache = new CacheService(() => config, path, new TestProtector(), clock))
            {
                Assert.Equal("third", (await cache.GetAsync("c", default))?.Text);
                clock.Advance(TimeSpan.FromDays(2)); Assert.Null(await cache.GetAsync("c", default));
                await cache.PutAsync("private", new("sensitive"), default);
                config = config with { Cache = config.Cache with { DiskEnabled = false } };
                await cache.ClearAsync();
                config = config with { Cache = config.Cache with { DiskEnabled = true } };
                Assert.Null(await cache.GetAsync("private", default));
            }
        }
        finally { File.Delete(path); }
    }
    [Theory]
    [InlineData("notepad", false, false, true)][InlineData("NOTEPAD.exe", false, false, true)]
    [InlineData("chrome", false, false, false)][InlineData("notepad", true, false, false)]
    [InlineData("notepad", false, true, false)][InlineData("keepass", false, false, false)]
    public void PrivacyGuard_RequiresExplicitApplicationConsent_AndSensitiveControlsAreExcluded(string process, bool password, bool sensitive, bool allowed)
    {
        var settings = CoordinatorTests.Config with { AllowedApplications = ["notepad", "keepass"] };
        Assert.Equal(allowed, PrivacyGuard.Allows(settings, process, password, sensitive));
        Assert.False(PrivacyGuard.Allows(settings with { PrivacyConsent = false }, process, false, false));
    }
    [Fact] public void Settings_EncryptKey_AndNeverSerializeItWithConfiguration()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var service = new SettingsService(folder, new TestProtector()); service.Save(CoordinatorTests.Config, "test-secret-not-real");
            Assert.DoesNotContain("test-secret-not-real", File.ReadAllText(Path.Combine(folder, "settings.json")));
            Assert.DoesNotContain("test-secret-not-real", Encoding.UTF8.GetString(File.ReadAllBytes(Directory.GetFiles(folder, "api-key.*.dpapi").Single())));
            Assert.Equal("test-secret-not-real", service.ReadKey()); service.Load(); Assert.True(service.Current.PrivacyConsent);
            File.WriteAllText(Path.Combine(folder, "settings.json"), "broken"); service.Load();
            Assert.NotNull(service.LoadWarning); Assert.False(service.Current.PrivacyConsent); Assert.False(service.Current.Translation.Enabled);
        }
        finally { Directory.Delete(folder, true); }
    }
    [Fact] public void Settings_RejectPlainHttpAndInvalidLimits()
    {
        Assert.Throws<ArgumentException>(() => (new AppSettings { AI = new() { BaseUrl = "http://api.deepseek.com" } }).Validate());
        Assert.Throws<ArgumentException>(() => (new AppSettings { Translation = new() { DebounceMilliseconds = -1 } }).Validate());
        Assert.Throws<ArgumentException>(() => (new AppSettings { Overlay = new() { Opacity = double.NaN } }).Validate());
    }
    private static string ReadShared(string path)
    {
        // SQLite 在 Windows 上仍持有数据库写句柄；检查磁盘密文时必须允许共享读写。
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var copy = new MemoryStream(); stream.CopyTo(copy);
        return Encoding.UTF8.GetString(copy.ToArray());
    }
    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan delta) => now += delta;
    }
}
