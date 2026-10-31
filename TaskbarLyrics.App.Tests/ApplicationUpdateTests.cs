using System.IO;
using System.IO.Compression;
using TaskbarLyrics.Updates;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class ApplicationUpdateTests
{
    [Fact]
    public void MarkdownFallbackPreservesImagesAndEscapesTheirAttributes()
    {
        var html = UpdateMarkdown.ToHtml("![**preview** & 'quote'](https://github.com/user-attachments/assets/example) **after**");
        Assert.Contains("<img src='https://github.com/user-attachments/assets/example'", html);
        Assert.Contains("alt='**preview** &amp; &#39;quote&#39;'", html);
        Assert.Contains("<strong>after</strong>", html);
        Assert.DoesNotContain("!<a", html);
    }
    [Fact]
    public void NotesMergeOnlyUpgradeIntervalInDescendingOrderAndKeepHtml()
    {
        var target = Release(6, "v1.7.6", "**latest**");
        var releases = new[] { Release(4, "1.7.4", "four"), target, Release(3, "1.7.3", "old"), Release(5, "1.7.5", "five"),
            Release(8, "1.8.0", "future"), Release(7, "1.7.7-preview", "preview"), target };
        var notes = UpdateReleaseNotes.Merge(releases, target, "1.7.3", true);
        Assert.True(notes.Complete);
        Assert.True(notes.Markdown.IndexOf("1.7.6", StringComparison.Ordinal) < notes.Markdown.IndexOf("1.7.5", StringComparison.Ordinal));
        Assert.True(notes.Markdown.IndexOf("1.7.5", StringComparison.Ordinal) < notes.Markdown.IndexOf("1.7.4", StringComparison.Ordinal));
        Assert.DoesNotContain("old", notes.Markdown);
        Assert.DoesNotContain("future", notes.Markdown);
        Assert.DoesNotContain("preview", notes.Markdown);
        Assert.Contains("<strong>latest</strong>", notes.Html);
        var same = UpdateReleaseNotes.Merge(releases, target, "1.7.6", true);
        Assert.DoesNotContain("five", same.Markdown);
        Assert.Contains("latest", same.Markdown);
        target.BodyHtml = "<p>github rendered</p>";
        Assert.Contains("github rendered", UpdateReleaseNotes.Merge([], target, "1.7.6", true).Html);
    }
    [Fact]
    public void NotesMarkDuplicateVersionsAndOversizedBodiesIncomplete()
    {
        var target = Release(3, "1.0.3", "target");
        Assert.False(UpdateReleaseNotes.Merge([Release(1, "1.0.2", "a"), Release(2, "v1.0.2", "b")], target, "1.0.0", true).Complete);
        target.Body = new string('x', UpdateReleaseNotes.MaximumLength + 1);
        Assert.False(UpdateReleaseNotes.Merge([], target, "1.0.0", true).Complete);
    }
    [Theory]
    [InlineData("../escape")]
    [InlineData("C:/escape")]
    [InlineData("a:stream")]
    [InlineData("folder/CON.txt")]
    [InlineData("a/../b")]
    [InlineData("file.")]
    [InlineData("file ")]
    [InlineData("a\\b")]
    public void RejectsUnsafePaths(string path) => Assert.Throws<InvalidDataException>(() => UpdatePackage.Contained(Path.GetTempPath(), path));

    [Fact]
    public async Task DigestMismatchStopsBeforeExtraction()
    {
        using var stream = new MemoryStream([1, 2, 3]);
        await Assert.ThrowsAsync<InvalidDataException>(() => UpdatePackage.VerifyDigestAsync(stream, "sha256:" + new string('0', 64)));
        Assert.False(UpdatePackage.IsDigest("sha256:abc"));
    }
    [Fact]
    public void ExtractChecksVersionRootRequiredFilesAndConflictingPaths()
    {
        using var fixture = new Fixture();
        var stage = Path.Combine(fixture.Directory, "stage");
        Fixture.WriteVersion(stage, "new");
        var wrapper = Path.Combine(fixture.Directory, "wrapper");
        System.IO.Directory.CreateDirectory(wrapper);
        System.IO.Directory.Move(stage, Path.Combine(wrapper, "TaskbarLyrics-1.0.1"));
        var zip = Path.Combine(fixture.Directory, "package.zip");
        ZipFile.CreateFromDirectory(wrapper, zip);
        var extracted = Path.Combine(fixture.Directory, "extracted");
        var files = UpdatePackage.Extract(zip, "1.0.1", extracted);
        Assert.Contains("TaskbarLyrics.exe", files);
        Assert.Contains("Web/Settings/settings.html", files);
        Assert.True(File.Exists(Path.Combine(extracted, "Web/Settings/settings.html")));
        Assert.Throws<InvalidDataException>(() => UpdatePackage.Extract(zip, "1.0.2", Path.Combine(fixture.Directory, "wrong")));
        // Any file in an existing ZIP is installed as-is; no extra manifest or file list is required.
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
        using (var writer = new StreamWriter(archive.CreateEntry("TaskbarLyrics-1.0.1/extra.txt").Open())) writer.Write("unmanaged");
        Assert.Contains("extra.txt", UpdatePackage.Extract(zip, "1.0.1", Path.Combine(fixture.Directory, "extra")));
        // A path that is both a file and a directory cannot be installed.
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
        using (var writer = new StreamWriter(archive.CreateEntry("TaskbarLyrics-1.0.1/TaskbarLyrics.exe/nested.txt").Open())) writer.Write("conflict");
        Assert.Throws<InvalidDataException>(() => UpdatePackage.Extract(zip, "1.0.1", Path.Combine(fixture.Directory, "conflict")));
    }
    [Fact]
    public void ExtractRequiresEntryPointAndSettingsPage()
    {
        using var fixture = new Fixture();
        var source = Path.Combine(fixture.Directory, "partial");
        var root = Path.Combine(source, "TaskbarLyrics-1.0.1");
        System.IO.Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "TaskbarLyrics.exe"), "exe");
        var zip = Path.Combine(fixture.Directory, "partial.zip");
        ZipFile.CreateFromDirectory(source, zip);
        Assert.Throws<InvalidDataException>(() => UpdatePackage.Extract(zip, "1.0.1", Path.Combine(fixture.Directory, "partial-output")));
    }
    [Fact]
    public void TransactionCacheIsOutsideAndScopedToTheInstallationDirectory()
    {
        using var fixture = new Fixture();
        var first = Path.Combine(fixture.Directory, "first");
        var second = Path.Combine(fixture.Directory, "second");
        var firstCache = UpdatePackage.GetTransactionRoot(first);
        var secondCache = UpdatePackage.GetTransactionRoot(second);
        Assert.StartsWith(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskbarLyrics", "Updates", "Transactions"), firstCache, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(firstCache, secondCache);
        Assert.False(firstCache.StartsWith(Path.GetFullPath(first), StringComparison.OrdinalIgnoreCase));
    }
    [Fact]
    public void WindowsPowerShellArchiveSeparatorsAreNormalizedBeforeValidation()
    {
        using var fixture = new Fixture();
        var source = Path.Combine(fixture.Directory, "source");
        Fixture.WriteVersion(source, "new");
        var zip = Path.Combine(fixture.Directory, "windows.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            foreach (var file in System.IO.Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                archive.CreateEntryFromFile(file, "TaskbarLyrics-1.0.1\\" + Path.GetRelativePath(source, file).Replace('/', '\\'));
        }
        var output = Path.Combine(fixture.Directory, "output");
        Assert.Contains("TaskbarLyrics.exe", UpdatePackage.Extract(zip, "1.0.1", output));
        Assert.True(File.Exists(Path.Combine(output, "Web/Settings/settings.html")));
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
            archive.CreateEntry("TaskbarLyrics-1.0.1\\..\\escape.txt");
        Assert.Throws<InvalidDataException>(() => UpdatePackage.Extract(zip, "1.0.1", Path.Combine(fixture.Directory, "escape")));
    }
    [Theory]
    [InlineData("backup:TaskbarLyrics.exe")]
    [InlineData("backup:Web/Settings/settings.html")]
    [InlineData("install:TaskbarLyrics.exe")]
    [InlineData("install:Web/Settings/settings.html")]
    [InlineData("install:Web/Lyrics/added.js")]
    public void FailureAtEachFileBoundaryRestoresOldFilesAndPreservesProtectedLogs(string checkpoint)
    {
        using var fixture = new Fixture();
        var root = Path.Combine(fixture.Directory, "app");
        var staging = Path.Combine(fixture.Directory, "stage");
        Fixture.WriteVersion(root, "old");
        File.WriteAllText(Path.Combine(root, "stale.txt"), "stale");
        var logs = Path.Combine(root, "Logs");
        System.IO.Directory.CreateDirectory(logs);
        File.WriteAllText(Path.Combine(logs, "app_debug.log"), "log");
        var files = Fixture.WriteVersion(staging, "new", "Web/Lyrics/added.js");
        var transaction = Path.Combine(fixture.Directory, "transaction");
        Assert.Throws<IOException>(() => UpdateTransaction.Apply(root, transaction, staging, files, "1.0.0", "1.0.1", "upgrade",
            point => { if (point == checkpoint) throw new IOException("Injected failure."); }));
        Assert.Equal("old", File.ReadAllText(Path.Combine(root, "TaskbarLyrics.exe")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(root, "Web/Settings/settings.html")));
        Assert.Equal("stale", File.ReadAllText(Path.Combine(root, "stale.txt")));
        Assert.Equal("log", File.ReadAllText(Path.Combine(logs, "app_debug.log")));
        Assert.False(File.Exists(Path.Combine(root, "Web/Lyrics/added.js")));
        UpdateTransaction.Recover(root, transaction);
        Assert.Equal("old", File.ReadAllText(Path.Combine(root, "TaskbarLyrics.exe")));
    }
    [Fact]
    public void SuccessfulUpgradeReplacesPackageFilesAndPreservesUnrelatedFilesAndLogs()
    {
        using var fixture = new Fixture();
        var root = Path.Combine(fixture.Directory, "app");
        var staging = Path.Combine(fixture.Directory, "stage");
        Fixture.WriteVersion(root, "old");
        System.IO.Directory.CreateDirectory(Path.Combine(root, "Web/Lyrics"));
        File.WriteAllText(Path.Combine(root, "Web/Lyrics/removed.js"), "old");
        var logs = Path.Combine(root, "Logs");
        System.IO.Directory.CreateDirectory(logs);
        File.WriteAllText(Path.Combine(logs, "app_debug.log"), "log");
        var files = Fixture.WriteVersion(staging, "new", "Web/Lyrics/added.js");
        var transaction = Path.Combine(fixture.Directory, "txn");
        UpdateTransaction.Apply(root, transaction, staging, files, "1.0.0", "1.0.1", "upgrade");
        Assert.Equal("new", File.ReadAllText(Path.Combine(root, "TaskbarLyrics.exe")));
        Assert.True(File.Exists(Path.Combine(root, "Web/Lyrics/added.js")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(root, "Web/Lyrics/removed.js")));
        Assert.Equal("log", File.ReadAllText(Path.Combine(logs, "app_debug.log")));
        UpdateTransaction.Commit(transaction);
        UpdateTransaction.Recover(root, transaction);
        Assert.Equal("new", File.ReadAllText(Path.Combine(root, "TaskbarLyrics.exe")));
    }
    [Fact]
    public void InstalledButUnconfirmedVersionRecoversAndCommittedVersionDoesNotRollback()
    {
        using var fixture = new Fixture();
        var root = Path.Combine(fixture.Directory, "app");
        var stage = Path.Combine(fixture.Directory, "stage");
        Fixture.WriteVersion(root, "old");
        var files = Fixture.WriteVersion(stage, "new");
        var transaction = Path.Combine(fixture.Directory, "txn");
        UpdateTransaction.Apply(root, transaction, stage, files, "1.0.0", "1.0.1", "upgrade");
        Assert.Equal("new", File.ReadAllText(Path.Combine(root, "TaskbarLyrics.exe")));
        UpdateTransaction.Recover(root, transaction);
        Assert.Equal("old", File.ReadAllText(Path.Combine(root, "TaskbarLyrics.exe")));
        var next = Path.Combine(fixture.Directory, "next");
        files = Fixture.WriteVersion(next, "new");
        transaction = Path.Combine(fixture.Directory, "txn2");
        UpdateTransaction.Apply(root, transaction, next, files, "1.0.0", "1.0.1", "upgrade");
        UpdateTransaction.Commit(transaction);
        UpdateTransaction.Recover(root, transaction);
        Assert.Equal("new", File.ReadAllText(Path.Combine(root, "TaskbarLyrics.exe")));
    }
    [Fact]
    public void SameVersionReinstallReallyReplacesFiles()
    {
        using var fixture = new Fixture();
        var root = Path.Combine(fixture.Directory, "app"); var stage = Path.Combine(fixture.Directory, "stage");
        Fixture.WriteVersion(root, "damaged");
        var files = Fixture.WriteVersion(stage, "restored");
        UpdateTransaction.Apply(root, Path.Combine(fixture.Directory, "txn"), stage, files, "1.0.0", "1.0.0", "reinstall");
        Assert.Equal("restored", File.ReadAllText(Path.Combine(root, "TaskbarLyrics.exe")));
    }
    [Fact]
    public void AssetSelectionRejectsMissingDigestAmbiguityAndDowngrade()
    {
        var asset = new UpdateChecker.GitHubAsset
        {
            Id = 1,
            Name = "TaskbarLyrics-1.0.0.zip",
            State = "uploaded",
            Size = 100,
            Digest = "sha256:" + new string('a', 64),
            Url = "https://github.com/ANYNC/TaskbarLyrics/releases/download/v1.0.0/TaskbarLyrics-1.0.0.zip"
        };
        var release = Release(1, "v1.0.0", ""); release.Assets.Add(asset);
        var candidate = new UpdateCheckResult(UpdateCheckState.Latest, "v1.0.0", "1.0.0", "", false) { Release = release };
        Assert.NotNull(AppUpdateService.SelectAsset(candidate));
        Assert.Null(AppUpdateService.SelectAsset(candidate with { CurrentVersion = "2.0.0" }));
        release.Assets.Add(asset);
        Assert.Null(AppUpdateService.SelectAsset(candidate));
        release.Assets = [asset with { Digest = null }];
        Assert.Null(AppUpdateService.SelectAsset(candidate));
        Assert.False(AppUpdateService.IsDownloadUrl("https://github.com.evil.test/a", false));
        Assert.False(AppUpdateService.IsDownloadUrl("http://github.com/a", false));
    }
    [Theory]
    [InlineData("{}", true)]
    [InlineData("null", true)]
    [InlineData("{\"url\":\"https://evil.test\"}", false)]
    [InlineData("\"arbitrary\"", false)]
    public void UpdateCommandsCannotSupplyTheirOwnTargets(string payload, bool valid)
    {
        var message = SettingsWebMessageRouter.Parse("{\"version\":1,\"type\":\"reinstallUpdate\",\"payload\":" + payload + "}");
        Assert.NotNull(message);
        Assert.Equal(valid, SettingsWebMessageRouter.IsEmptyCommand(message));
    }
    [Fact]
    public async Task PipeHandoffVerifiesBothProcessIdentities()
    {
        var name = "TaskbarLyrics.Tests." + Guid.NewGuid().ToString("N");
        using var server = new System.IO.Pipes.NamedPipeServerStream(name, System.IO.Pipes.PipeDirection.InOut, 1,
            System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);
        using var client = new System.IO.Pipes.NamedPipeClientStream(".", name, System.IO.Pipes.PipeDirection.InOut,
            System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Task.WhenAll(server.WaitForConnectionAsync(timeout.Token), client.ConnectAsync(timeout.Token));
        UpdatePipeIdentity.RequireClient(server, Environment.ProcessId);
        UpdatePipeIdentity.RequireServer(client, Environment.ProcessId);
        Assert.Throws<IOException>(() => UpdatePipeIdentity.RequireClient(server, 0));
        Assert.Throws<IOException>(() => UpdatePipeIdentity.RequireServer(client, 0));
    }
    private static UpdateChecker.GitHubRelease Release(long id, string version, string body) => new() { Id = id, TagName = version, Name = version, Body = body };
    private sealed class Fixture : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "TaskbarLyrics-update-tests", Guid.NewGuid().ToString("N"));
        public static List<string> WriteVersion(string root, string content, params string[] extra)
        {
            var files = new List<string> { "TaskbarLyrics.exe", "Web/Settings/settings.html" };
            files.AddRange(extra);
            foreach (var relative in files)
            {
                var path = UpdatePackage.Contained(root, relative);
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, content);
            }
            return files;
        }
        public void Dispose() { if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true); }
    }
}
