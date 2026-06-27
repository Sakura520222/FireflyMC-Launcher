using System.Text.Json.Serialization;

namespace FireflyMC.Launcher.Contracts.FireflyApi;

public sealed record ModEntryResponse(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("fileName")] string? FileName,
    [property: JsonPropertyName("filename")] string? LegacyFileName,
    [property: JsonPropertyName("fileSize")] long? FileSize,
    [property: JsonPropertyName("size")] long? Size,
    [property: JsonPropertyName("platformId")] string? PlatformId,
    [property: JsonPropertyName("projectId")] string? ProjectId,
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("required")] bool? Required);

public sealed record VersionInfoResponse(
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("packVersion")] string? PackVersion,
    [property: JsonPropertyName("modVersion")] string? ModVersion,
    [property: JsonPropertyName("modUrl")] string? ModUrl,
    [property: JsonPropertyName("sha256")] string? Sha256);
