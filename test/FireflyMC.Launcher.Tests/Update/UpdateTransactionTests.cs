using FireflyMC.Launcher.Infrastructure.Diagnostics;
using FireflyMC.Launcher.Infrastructure.Storage;
using FireflyMC.Launcher.Models;
using FireflyMC.Launcher.Models.Remote;
using FireflyMC.Launcher.Services.Update;
using FluentAssertions;

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
        var transaction = new UpdateTransaction(paths, new NullDiagnosticLogger());
        var plan = NewPlan("mods/a.jar");

        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.ExecuteAsync(
            plan,
            _ => staged,
            () => throw new InvalidOperationException("manifest save failed"),
            null,
            CancellationToken.None));

        (await File.ReadAllTextAsync(target)).Should().Be("old");
        File.Exists(paths.TransactionFile).Should().BeFalse();
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
        var transaction = new UpdateTransaction(paths, new NullDiagnosticLogger());
        var plan = NewPlan("mods/a.jar");

        await transaction.ExecuteAsync(plan, _ => staged, () => Task.CompletedTask, null, CancellationToken.None);

        (await File.ReadAllTextAsync(paths.GetAbsoluteGamePath("mods/a.jar"))).Should().Be("new");
        File.Exists(paths.TransactionFile).Should().BeFalse();
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
