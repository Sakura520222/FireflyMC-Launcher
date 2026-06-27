using FireflyMC.Launcher.Configuration;
using FireflyMC.Launcher.Infrastructure.Diagnostics;
using FireflyMC.Launcher.Infrastructure.Download;
using FireflyMC.Launcher.Infrastructure.Platforms;
using FireflyMC.Launcher.Models.Remote;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace FireflyMC.Launcher.Tests.Infrastructure.Platforms;

public sealed class ModDownloadSourceTests
{
    [Fact]
    public async Task ModrinthClient_WhenOfficialFails_UsesMcimMirrorAndRewritesCdn()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/official/v2/project/mod-project/version").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));
        server.Given(Request.Create().WithPath("/modrinth/v2/project/mod-project/version").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("sync_at", DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"))
                .WithBody("""
                [
                  {
                    "version_number": "1.0.0",
                    "name": "1.0.0",
                    "files": [
                      {
                        "filename": "example.jar",
                        "primary": true,
                        "url": "https://cdn.modrinth.com/data/project/versions/file/example.jar",
                        "hashes": { "sha1": "abc123" },
                        "size": 123
                      }
                    ]
                  }
                ]
                """));
        var config = CreateConfiguration(server);
        var client = new ModrinthClient(new HttpClient(), config, new MirrorRouter(config), LauncherUserAgent.Create(config), new NullDiagnosticLogger());

        var result = await client.ResolveAsync(
            new RemoteModEntry("Example", "example.jar", 0, ModPlatform.Modrinth, "mod-project", "1.0.0"),
            "1.21.1",
            "neoforge",
            CancellationToken.None);

        result.FileName.Should().Be("example.jar");
        result.Sha1.Should().Be("abc123");
        result.DownloadUri.Should().Be(new Uri("https://mod.mcimirror.top/data/project/versions/file/example.jar"));
    }

    [Fact]
    public async Task CurseForgeClient_UsesMcimWithoutLoaderParameter_FiltersGameVersionsAndRewritesFileUrl()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/curseforge/v1/mods/386134/files").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("sync_at", DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"))
                .WithBody("""
                {
                  "data": [
                    {
                      "id": 1,
                      "fileName": "ftb-ultimine-fabric.jar",
                      "displayName": "fabric",
                      "fileDate": "2026-01-01T00:00:00Z",
                      "gameVersions": [ "1.21.1", "Fabric" ],
                      "downloadUrl": "https://edge.forgecdn.net/files/1/1/ftb-ultimine-fabric.jar",
                      "hashes": [ { "algo": 1, "value": "bad" } ],
                      "fileLength": 10
                    },
                    {
                      "id": 2,
                      "fileName": "ftb-ultimine-neoforge-2101.1.15.jar",
                      "displayName": "2101.1.15",
                      "fileDate": "2026-02-01T00:00:00Z",
                      "gameVersions": [ "1.21.1", "NeoForge" ],
                      "downloadUrl": "https://edge.forgecdn.net/files/8231/400/ftb-ultimine-neoforge-2101.1.15.jar",
                      "hashes": [ { "algo": 1, "value": "sha1" } ],
                      "fileLength": 20
                    }
                  ]
                }
                """));
        var config = CreateConfiguration(server);
        var client = new CurseForgeClient(new HttpClient(), config, new MirrorRouter(config), LauncherUserAgent.Create(config), new NullDiagnosticLogger());

        var result = await client.ResolveAsync(
            new RemoteModEntry("FTB Ultimine", "ftb-ultimine-neoforge-2101.1.15.jar", 0, ModPlatform.CurseForge, "386134", null),
            "1.21.1",
            "neoforge",
            CancellationToken.None);

        result.FileName.Should().Be("ftb-ultimine-neoforge-2101.1.15.jar");
        result.Sha1.Should().Be("sha1");
        result.DownloadUri.Should().Be(new Uri("https://mod.mcimirror.top/files/8231/400/ftb-ultimine-neoforge-2101.1.15.jar"));
        server.LogEntries.Should().ContainSingle();
        var requestUrl = server.LogEntries[0].RequestMessage?.Url;
        requestUrl.Should().NotBeNull();
        requestUrl!.Should().NotContain("modLoaderType");
    }

    [Fact]
    public void McimCachePolicy_WhenSyncAtIsOlderThanThreshold_RejectsResponse()
    {
        using var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        response.Headers.Add("sync_at", DateTimeOffset.UtcNow.AddDays(-8).ToString("yyyy-MM-ddTHH:mm:ssZ"));

        var act = () => McimCachePolicy.EnsureFresh(response, new UpdateOptions { McimStaleThresholdDays = 7 });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*stale*");
    }

    private static LauncherConfiguration CreateConfiguration(WireMockServer server)
    {
        return new LauncherConfiguration
        {
            UserAgent = "FireflyMC-Launcher/1.0.0 (https://github.com/Sakura520222/FireflyMC-Launcher)",
            Mirrors = new MirrorOptions
            {
                ModrinthApiPrimary = $"{server.Url}/official",
                ModrinthApiMirror = $"{server.Url}/modrinth",
                ModrinthCdnPrimary = "https://cdn.modrinth.com",
                ModrinthCdnMirror = "https://mod.mcimirror.top",
                CurseForgeApiMirror = $"{server.Url}/curseforge",
                CurseForgeFileCdn = "https://edge.forgecdn.net",
                CurseForgeFileMirror = "https://mod.mcimirror.top"
            },
            Update = new UpdateOptions
            {
                McimStaleThresholdDays = 7
            }
        };
    }
}
