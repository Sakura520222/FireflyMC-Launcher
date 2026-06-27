using FireflyMC.Launcher.Configuration;

namespace FireflyMC.Launcher.Infrastructure.Download;

public sealed class MirrorRouter(LauncherConfiguration configuration)
{
    public Uri Choose(Uri primary, Uri? fallback, bool useMirror)
    {
        return useMirror && fallback is not null ? fallback : primary;
    }

    public Uri RewriteModrinthFileToMirror(Uri uri)
    {
        return RewriteKnownBase(uri, configuration.Mirrors.ModrinthCdnPrimary, configuration.Mirrors.ModrinthCdnMirror);
    }

    public Uri RewriteCurseForgeFileToMirror(Uri uri)
    {
        var rewritten = RewriteKnownBase(uri, configuration.Mirrors.CurseForgeFileCdn, configuration.Mirrors.CurseForgeFileMirror);
        if (!ReferenceEquals(rewritten, uri) && rewritten != uri)
        {
            return rewritten;
        }

        return RewriteKnownBase(uri, "https://mediafilez.forgecdn.net", configuration.Mirrors.CurseForgeFileMirror);
    }

    private static Uri RewriteKnownBase(Uri uri, string primaryBase, string mirrorBase)
    {
        var primary = EnsureTrailingSlash(primaryBase);
        var mirror = EnsureTrailingSlash(mirrorBase);
        if (!uri.Host.Equals(primary.Host, StringComparison.OrdinalIgnoreCase))
        {
            return uri;
        }

        var primaryPrefix = primary.AbsolutePath.TrimEnd('/');
        var originalPath = uri.AbsolutePath;
        if (!string.IsNullOrEmpty(primaryPrefix)
            && !primaryPrefix.Equals("/", StringComparison.Ordinal)
            && originalPath.StartsWith(primaryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            originalPath = originalPath[primaryPrefix.Length..];
        }

        var mirrorPrefix = mirror.AbsolutePath.TrimEnd('/');
        var builder = new UriBuilder(uri)
        {
            Scheme = mirror.Scheme,
            Host = mirror.Host,
            Port = mirror.IsDefaultPort ? -1 : mirror.Port,
            Path = $"{mirrorPrefix}/{originalPath.TrimStart('/')}".TrimStart('/')
        };
        return builder.Uri;
    }

    private static Uri EnsureTrailingSlash(string value)
    {
        return new Uri(value.TrimEnd('/') + "/");
    }
}
