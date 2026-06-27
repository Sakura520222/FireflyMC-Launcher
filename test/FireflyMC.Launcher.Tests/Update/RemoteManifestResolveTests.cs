using FireflyMC.Launcher.Configuration;
using FireflyMC.Launcher.Infrastructure.Diagnostics;
using FireflyMC.Launcher.Services.Update;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace FireflyMC.Launcher.Tests.Update;

public sealed class RemoteManifestResolveTests
{
    [Fact]
    public async Task ResolveRemoteManifestAsync_ReadsPackModsObjectShape()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/api/version").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""
            {
              "modVersion": "v2.5.6",
              "packVersion": "v2.5",
              "modUrl": "https://example.test/FireflyMC-2.5.6.jar"
            }
            """));
        server.Given(Request.Create().WithPath("/api/pack/mods").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""
            {
              "importedAt": 1782461277,
              "count": 1,
              "mods": [
                {
                  "name": "FTB Ultimine",
                  "version": "2101.1.13",
                  "fileName": "ftb-ultimine-neoforge-2101.1.13.jar",
                  "fileSize": 176935,
                  "platformId": "386134"
                }
              ]
            }
            """));
        var configuration = new LauncherConfiguration
        {
            FireflyApi = new FireflyApiOptions
            {
                Version = $"{server.Url}/api/version",
                PackMods = $"{server.Url}/api/pack/mods"
            }
        };
        var service = new ModPackUpdateService(
            new HttpClient(),
            configuration,
            paths: null!,
            installedManifestStore: null!,
            downloader: null!,
            hashVerifier: null!,
            platformResolver: null!,
            updateTransaction: null!,
            LauncherUserAgent.Create(configuration),
            new NullDiagnosticLogger(),
            concurrentDownloader: null!);

        var manifest = await service.ResolveRemoteManifestAsync(CancellationToken.None);

        manifest.PackVersion.Should().Be("v2.5");
        manifest.Mods.Should().ContainSingle();
        manifest.Mods[0].ProjectId.Should().Be("386134");
        manifest.Mods[0].Platform.Should().Be(FireflyMC.Launcher.Models.Remote.ModPlatform.CurseForge);
    }
}
