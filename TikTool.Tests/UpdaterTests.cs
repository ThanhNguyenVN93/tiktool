using System.IO.Compression;
using TikTool.Services;

namespace TikTool.Tests;

public class SemVerTests
{
    [Theory]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.10")]
    [InlineData("1.0.0-beta.10", "1.0.0")]
    [InlineData("1.0.0", "1.0.1")]
    [InlineData("1.0.0-alpha", "1.0.0-beta")]
    [InlineData("1.0.0-beta", "1.0.0-beta.1")]
    [InlineData("0.9.9", "1.0.0-beta.1")]
    public void Orders_versions_like_semver(string older, string newer)
    {
        Assert.True(SemVer.TryParse(older, out var a));
        Assert.True(SemVer.TryParse(newer, out var b));
        Assert.True(a < b);
        Assert.True(b > a);
    }

    [Theory]
    [InlineData("v1.0.0-beta.3", "1.0.0-beta.3")]
    [InlineData("1.2.3+abc123", "1.2.3")]
    [InlineData("1.0.0-beta.3+8f1c2d", "1.0.0-beta.3")]
    public void Parses_tags_and_build_metadata(string input, string expected)
    {
        Assert.True(SemVer.TryParse(input, out var v));
        Assert.Equal(expected, v.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1.0")]
    [InlineData("1.0.0-")]
    [InlineData("a.b.c")]
    public void Rejects_what_is_not_a_version(string input) => Assert.False(SemVer.TryParse(input, out _));
}

public class AppUpdaterTests
{
    private const string Prefix = "https://github.com/ThanhNguyenVN93/tiktool/releases/download/";

    private static string Release(string tag, bool prerelease, bool draft = false, bool withSha = true, string host = Prefix) =>
        $$"""
        {"tag_name":"{{tag}}","prerelease":{{prerelease.ToString().ToLower()}},"draft":{{draft.ToString().ToLower()}},"html_url":"https://github.com/x/y/releases/tag/{{tag}}","body":"notes",
         "assets":[{"name":"SocialToolkit-{{tag}}-win-x64.zip","size":1234,"browser_download_url":"{{host}}{{tag}}/SocialToolkit-{{tag}}-win-x64.zip"}
         {{(withSha ? $$""",{"name":"SocialToolkit-{{tag}}-win-x64.zip.sha256","size":90,"browser_download_url":"{{host}}{{tag}}/SocialToolkit-{{tag}}-win-x64.zip.sha256"}""" : "")}}]}
        """;

    private static string Releases(params string[] items) => "[" + string.Join(",", items) + "]";

    private static SemVer V(string s)
    {
        SemVer.TryParse(s, out var v);
        return v;
    }

    [Fact]
    public void Picks_the_highest_newer_release_for_a_beta_install()
    {
        var json = Releases(Release("v1.0.0-beta.2", true), Release("v1.0.0-beta.4", true), Release("v1.0.0-beta.3", true));
        Assert.Equal("v1.0.0-beta.4", AppUpdater.PickUpdate(json, V("1.0.0-beta.1"))!.Tag);
    }

    [Fact]
    public void Offers_nothing_when_already_on_the_newest()
    {
        Assert.Null(AppUpdater.PickUpdate(Releases(Release("v1.0.0-beta.2", true)), V("1.0.0-beta.2")));
        Assert.Null(AppUpdater.PickUpdate(Releases(Release("v1.0.0-beta.2", true)), V("1.0.0")));
    }

    [Fact]
    public void A_stable_install_is_not_moved_to_a_prerelease()
    {
        var json = Releases(Release("v1.1.0-beta.1", true), Release("v1.0.5", false));
        Assert.Equal("v1.0.5", AppUpdater.PickUpdate(json, V("1.0.0"))!.Tag);
        Assert.Null(AppUpdater.PickUpdate(Releases(Release("v1.1.0-beta.1", true)), V("1.0.0")));
    }

    [Fact]
    public void Ignores_drafts_releases_without_a_checksum_and_files_from_other_hosts()
    {
        Assert.Null(AppUpdater.PickUpdate(Releases(Release("v2.0.0", false, draft: true)), V("1.0.0")));
        Assert.Null(AppUpdater.PickUpdate(Releases(Release("v2.0.0", false, withSha: false)), V("1.0.0")));
        Assert.Null(AppUpdater.PickUpdate(Releases(Release("v2.0.0", false, host: "https://evil.example/")), V("1.0.0")));
    }
}

public class UpdateApplyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tiktool_updtest_" + Guid.NewGuid().ToString("N"));
    private string Staged => Path.Combine(_root, "staged");
    private string Target => Path.Combine(_root, "app");

    public UpdateApplyTests()
    {
        Directory.CreateDirectory(Staged);
        Directory.CreateDirectory(Target);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static void Put(string dir, string rel, string text)
    {
        var path = Path.Combine(dir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void Replaces_changed_files_keeps_the_old_ones_aside_and_adds_new_ones()
    {
        Put(Target, "a.dll", "old a");
        Put(Target, "same.dll", "same");
        Put(Staged, "a.dll", "new a");
        Put(Staged, "same.dll", "same");
        Put(Staged, @"sub\c.txt", "new c");

        var result = AppUpdater.ApplyFiles(Staged, Target);

        Assert.True(result.Ok);
        Assert.Equal(2, result.Replaced);
        Assert.Equal("new a", File.ReadAllText(Path.Combine(Target, "a.dll")));
        Assert.Equal("old a", File.ReadAllText(Path.Combine(Target, "a.dll.old")));
        Assert.False(File.Exists(Path.Combine(Target, "same.dll.old")));
        Assert.Equal("new c", File.ReadAllText(Path.Combine(Target, @"sub\c.txt")));
    }

    [Fact]
    public void Never_replaces_yt_dlp_or_ffmpeg()
    {
        Put(Target, @"Resources\yt-dlp.exe", "users own, newer");
        Put(Staged, @"Resources\yt-dlp.exe", "bundled");
        Put(Staged, @"Resources\ffmpeg.exe", "bundled ffmpeg");

        Assert.True(AppUpdater.ApplyFiles(Staged, Target).Ok);
        Assert.Equal("users own, newer", File.ReadAllText(Path.Combine(Target, @"Resources\yt-dlp.exe")));
        Assert.False(File.Exists(Path.Combine(Target, @"Resources\ffmpeg.exe")));
    }

    [Fact]
    public void Undoes_everything_when_one_file_cannot_be_replaced()
    {
        Put(Target, "a.dll", "old a");
        Put(Target, "z.dll", "old z");
        Put(Staged, "a.dll", "new a");
        Put(Staged, "z.dll", "new z");

        // z.dll is held open without delete-sharing, so it cannot be renamed
        using var hold = new FileStream(Path.Combine(Target, "z.dll"), FileMode.Open, FileAccess.Read, FileShare.None);
        var result = AppUpdater.ApplyFiles(Staged, Target);

        Assert.False(result.Ok);
        Assert.Equal("old a", File.ReadAllText(Path.Combine(Target, "a.dll")));
        Assert.False(File.Exists(Path.Combine(Target, "a.dll.old")));
    }

    [Fact]
    public void Cleanup_removes_only_the_old_files()
    {
        Put(Target, "a.dll", "keep");
        Put(Target, "a.dll.old", "gone");
        Put(Target, "b.dll.old2", "gone");
        Put(Target, "notes.old.txt", "keep");

        AppUpdater.CleanupOldFiles(Target);

        Assert.True(File.Exists(Path.Combine(Target, "a.dll")));
        Assert.False(File.Exists(Path.Combine(Target, "a.dll.old")));
        Assert.False(File.Exists(Path.Combine(Target, "b.dll.old2")));
        Assert.True(File.Exists(Path.Combine(Target, "notes.old.txt")));
    }

    [Fact]
    public void Unpacks_a_zip_without_its_top_folder()
    {
        var zip = Path.Combine(_root, "ok.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            foreach (var name in new[] { "SocialToolkit/TikTool.exe", "SocialToolkit/Resources/x.txt" })
            {
                using var w = new StreamWriter(z.CreateEntry(name).Open());
                w.Write("data");
            }
        }

        AppUpdater.ExtractZipSafely(zip, Path.Combine(_root, "out"));

        Assert.True(File.Exists(Path.Combine(_root, @"out\TikTool.exe")));
        Assert.True(File.Exists(Path.Combine(_root, @"out\Resources\x.txt")));
    }

    [Fact]
    public void Refuses_a_zip_entry_that_escapes_the_folder()
    {
        var zip = Path.Combine(_root, "evil.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            foreach (var name in new[] { "SocialToolkit/ok.txt", "SocialToolkit/../../evil.txt" })
            {
                using var w = new StreamWriter(z.CreateEntry(name).Open());
                w.Write("data");
            }
        }

        Assert.Throws<InvalidOperationException>(() => AppUpdater.ExtractZipSafely(zip, Path.Combine(_root, "out2")));
        Assert.False(File.Exists(Path.Combine(_root, "evil.txt")));
    }
}