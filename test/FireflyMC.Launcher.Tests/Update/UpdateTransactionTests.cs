using FireflyMC.Launcher.Infrastructure.Storage;
using FireflyMC.Launcher.Models;
using FireflyMC.Launcher.Models.Remote;
using FireflyMC.Launcher.Services.Update;

namespace FireflyMC.Launcher.Tests.Update;

public sealed class UpdateTransactionTests
{
    [Fact]
    public async Task ExecuteAsync_WhenCommitSaveFails_RollsBackReplacedFiles()
    {
        var root = CreateTempRoot();
        var paths = new LauncherPaths(root);
        paths.EnsureCreated();
        var target = paths.GetAbsoluteGamePath("mods/a.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target, "old");
        var staged = Path.Combine(paths.StagingDirectory, "mods", "a.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
        await File.WriteAllTextAsync(staged, "new");
        var transaction = new UpdateTransaction(paths);
        var plan = NewPlan("mods/a.jar");

        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.ExecuteAsync(
            plan,
            _ => staged,
            () => throw new InvalidOperationException("manifest save failed"),
            null,
            CancellationToken.None));

        Assert.Equal("old", await File.ReadAllTextAsync(target));
        Assert.False(File.Exists(paths.TransactionFile));
    }

    [Fact]
    public async Task ExecuteAsync_WhenSuccessful_DeletesTransactionFile()
    {
        var root = CreateTempRoot();
        var paths = new LauncherPaths(root);
        paths.EnsureCreated();
        var staged = Path.Combine(paths.StagingDirectory, "mods", "a.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
        await File.WriteAllTextAsync(staged, "new");
        var transaction = new UpdateTransaction(paths);
        var plan = NewPlan("mods/a.jar");

        await transaction.ExecuteAsync(plan, _ => staged, () => Task.CompletedTask, null, CancellationToken.None);

        Assert.Equal("new", await File.ReadAllTextAsync(paths.GetAbsoluteGamePath("mods/a.jar")));
        Assert.False(File.Exists(paths.TransactionFile));
    }

    private static UpdatePlan NewPlan(string relativePath)
    {
        var entry = new RemoteModEntry("A", "a.jar", 3, ModPlatform.Modrinth, "abc", null, true);
        var resolved = new ResolvedModFile(entry, "a.jar", 3, "", new Uri("https://example.test/a.jar"));
        return new UpdatePlan("manifest", [new FileToDownload(relativePath, resolved, true)], [], [], new FireflyModAction(false, null, null, "mods/firefly.jar"), false);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"firefly-launcher-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
