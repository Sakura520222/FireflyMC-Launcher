using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace FireflyMC.Launcher.Infrastructure.Storage;

public static class JsonFile
{
    public static async Task<T?> ReadAsync<T>(string path, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken);
    }

    public static async Task WriteAtomicAsync<T>(string path, T value, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = $"{path}.tmp";
        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, value, typeInfo, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        if (File.Exists(path))
        {
            File.Replace(tempPath, path, null);
        }
        else
        {
            File.Move(tempPath, path);
        }
    }
}
