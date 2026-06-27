using System.Text.Json;
using FireflyMC.Launcher.Contracts.FireflyApi;
using FluentAssertions;

namespace FireflyMC.Launcher.Tests.Contracts;

public sealed class FireflyApiContractsTests
{
    [Fact]
    public void PackModsResponse_DeserializesCurrentApiShape()
    {
        const string json = """
        {
          "importedAt": 1782461277,
          "count": 1,
          "mods": [
            {
              "name": "FTB Ultimine",
              "version": "2101.1.13",
              "fileName": "ftb-ultimine-neoforge-2101.1.13.jar",
              "fileSize": 176935,
              "platformId": "386134",
              "modId": "",
              "updatedAt": "2026/2/3 20:09:33"
            }
          ]
        }
        """;

        var response = JsonSerializer.Deserialize<PackModsResponse>(json);

        response.Should().NotBeNull();
        response!.Count.Should().Be(1);
        response.Mods.Should().ContainSingle();
        response.Mods![0].PlatformId.Should().Be("386134");
    }
}
