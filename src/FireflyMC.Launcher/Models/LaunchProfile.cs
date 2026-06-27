namespace FireflyMC.Launcher.Models;

public sealed record LaunchProfile(
    string JavaExecutable,
    IReadOnlyList<string> JvmArguments,
    IReadOnlyList<string> GameArguments,
    string MainClass,
    string WorkingDirectory,
    IReadOnlyList<string> ClasspathEntries,
    string NativesDirectory,
    string? LoggingArgument);

public sealed record MergedVersionMetadata(
    string Id,
    string MainClass,
    IReadOnlyList<string> Libraries,
    IReadOnlyList<string> JvmArguments,
    IReadOnlyList<string> GameArguments);
