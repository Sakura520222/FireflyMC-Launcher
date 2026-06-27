namespace FireflyMC.Launcher.Infrastructure.Download;

public sealed class MirrorRouter
{
    public Uri Choose(Uri primary, Uri? fallback, bool useMirror)
    {
        return useMirror && fallback is not null ? fallback : primary;
    }
}
