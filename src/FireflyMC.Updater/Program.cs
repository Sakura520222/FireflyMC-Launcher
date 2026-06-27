using System.Diagnostics;
using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;

var options = UpdaterOptions.Parse(args);
await Updater.RunAsync(options, CancellationToken.None);

internal sealed record UpdaterOptions(
    string PackagePath,
    string SignaturePath,
    string TargetDirectory,
    string PublicKey,
    int LauncherPid,
    string Nonce)
{
    public static UpdaterOptions Parse(string[] args)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length - 1; i += 2)
        {
            map[args[i]] = args[i + 1];
        }

        return new UpdaterOptions(
            Required(map, "--package"),
            Required(map, "--signature"),
            Required(map, "--target"),
            Required(map, "--public-key"),
            int.Parse(Required(map, "--launcher-pid")),
            Required(map, "--nonce"));
    }

    private static string Required(IReadOnlyDictionary<string, string> map, string key)
    {
        return map.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Missing {key}");
    }
}

internal static class Updater
{
    public static async Task RunAsync(UpdaterOptions options, CancellationToken cancellationToken)
    {
        var target = Path.GetFullPath(options.TargetDirectory);
        var updateRoot = Path.Combine(target, "update");
        Directory.CreateDirectory(updateRoot);
        var extract = Path.Combine(updateRoot, $"extract-{Guid.NewGuid():N}");
        var backup = Path.Combine(updateRoot, $"launcher-backup-{Guid.NewGuid():N}");

        try
        {
            await WaitForLauncherExitAsync(options.LauncherPid, cancellationToken);
            VerifyPackageSignature(options.PackagePath, options.SignaturePath, options.PublicKey);
            ZipFile.ExtractToDirectory(options.PackagePath, extract, overwriteFiles: true);
            VerifyPackageManifest(extract);
            Directory.CreateDirectory(backup);
            BackupTarget(target, backup);
            ReplaceTarget(extract, target);
            await LaunchAndConfirmAsync(target, options.Nonce, cancellationToken);
            Directory.Delete(backup, recursive: true);
        }
        catch
        {
            Rollback(target, backup);
            TryLaunch(Path.Combine(target, "FireflyMC.Launcher.exe"), $"--update-failed {options.Nonce}");
            throw;
        }
        finally
        {
            DeleteIfExists(extract);
        }
    }

    private static async Task WaitForLauncherExitAsync(int pid, CancellationToken cancellationToken)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (ArgumentException)
        {
        }
    }

    private static void VerifyPackageSignature(string packagePath, string signaturePath, string publicKey)
    {
        if (!File.Exists(packagePath) || !File.Exists(signaturePath) || string.IsNullOrWhiteSpace(publicKey))
        {
            throw new InvalidOperationException("Update package, signature, or public key is missing.");
        }

        var package = File.ReadAllBytes(packagePath);
        var signature = File.ReadAllBytes(signaturePath);
        var key = Convert.FromBase64String(publicKey);
        if (!Ed25519Verifier.Verify(package, signature, key))
        {
            throw new CryptographicException("Invalid Ed25519 update package signature.");
        }
    }

    private static void VerifyPackageManifest(string extractedDirectory)
    {
        var manifestPath = Path.Combine(extractedDirectory, "package-manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("package-manifest.json is missing from update package.", manifestPath);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        foreach (var file in document.RootElement.GetProperty("files").EnumerateArray())
        {
            var relative = file.GetProperty("path").GetString() ?? throw new InvalidDataException("Manifest file path is empty.");
            var expected = file.GetProperty("sha256").GetString() ?? throw new InvalidDataException("Manifest SHA-256 is empty.");
            var fullPath = Path.GetFullPath(Path.Combine(extractedDirectory, relative));
            if (!fullPath.StartsWith(extractedDirectory, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Manifest path escapes package root: {relative}");
            }

            using var stream = File.OpenRead(fullPath);
            var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new CryptographicException($"Package manifest hash mismatch: {relative}");
            }
        }
    }

    private static void BackupTarget(string target, string backup)
    {
        foreach (var file in Directory.EnumerateFiles(target, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            if (name.Equals("FireflyMC.Updater.exe", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            File.Copy(file, Path.Combine(backup, name), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(target, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(directory);
            if (name is ".minecraft" or "runtime" or "update" or "logs" or "secrets")
            {
                continue;
            }

            CopyDirectory(directory, Path.Combine(backup, name));
        }
    }

    private static void ReplaceTarget(string extracted, string target)
    {
        foreach (var file in Directory.EnumerateFiles(extracted, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(extracted, file);
            if (relative.Equals("package-manifest.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static async Task LaunchAndConfirmAsync(string target, string nonce, CancellationToken cancellationToken)
    {
        var launcher = Path.Combine(target, "FireflyMC.Launcher.exe");
        TryLaunch(launcher, $"--update-success {nonce}");
        var successFile = Path.Combine(target, "update", $"success-{nonce}");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (File.Exists(successFile))
            {
                return;
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new TimeoutException("Updated launcher did not confirm healthy startup.");
    }

    private static void Rollback(string target, string backup)
    {
        if (Directory.Exists(backup))
        {
            CopyDirectory(backup, target);
        }
    }

    private static void TryLaunch(string fileName, string arguments)
    {
        if (File.Exists(fileName))
        {
            Process.Start(new ProcessStartInfo(fileName, arguments) { UseShellExecute = true });
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}

internal static class Ed25519Verifier
{
    private static readonly BigInteger Q = (BigInteger.One << 255) - 19;
    private static readonly BigInteger L = (BigInteger.One << 252) + BigInteger.Parse("27742317777372353535851937790883648493");
    private static readonly BigInteger D = Mod(-121665 * ModInverse(121666));
    private static readonly BigInteger I = BigInteger.ModPow(2, (Q - 1) / 4, Q);
    private static readonly Point Identity = new(BigInteger.Zero, BigInteger.One);
    private static readonly Point BasePoint = DecodePoint(Convert.FromHexString("5866666666666666666666666666666666666666666666666666666666666666"));

    public static bool Verify(byte[] message, byte[] signature, byte[] publicKey)
    {
        if (signature.Length != 64 || publicKey.Length != 32)
        {
            return false;
        }

        var rBytes = signature[..32];
        var sBytes = signature[32..];
        var s = FromLittleEndian(sBytes);
        if (s >= L)
        {
            return false;
        }

        Point r;
        Point a;
        try
        {
            r = DecodePoint(rBytes);
            a = DecodePoint(publicKey);
        }
        catch
        {
            return false;
        }

        var hInput = new byte[32 + 32 + message.Length];
        Buffer.BlockCopy(rBytes, 0, hInput, 0, 32);
        Buffer.BlockCopy(publicKey, 0, hInput, 32, 32);
        Buffer.BlockCopy(message, 0, hInput, 64, message.Length);
        var h = FromLittleEndian(SHA512.HashData(hInput)) % L;
        var left = ScalarMultiply(BasePoint, s);
        var right = Add(r, ScalarMultiply(a, h));
        return left.Equals(right);
    }

    private static Point DecodePoint(byte[] encoded)
    {
        if (encoded.Length != 32)
        {
            throw new CryptographicException("Invalid Ed25519 point length.");
        }

        var bytes = encoded.ToArray();
        var sign = (bytes[31] & 0x80) != 0;
        bytes[31] &= 0x7f;
        var y = FromLittleEndian(bytes);
        if (y >= Q)
        {
            throw new CryptographicException("Invalid Ed25519 point.");
        }

        var y2 = Mod(y * y);
        var x2 = Mod((y2 - 1) * ModInverse(D * y2 + 1));
        var x = BigInteger.ModPow(x2, (Q + 3) / 8, Q);
        if (Mod(x * x - x2) != 0)
        {
            x = Mod(x * I);
        }

        if (Mod(x * x - x2) != 0)
        {
            throw new CryptographicException("Invalid Ed25519 point.");
        }

        if ((x.IsEven && sign) || (!x.IsEven && !sign))
        {
            x = Mod(-x);
        }

        return new Point(x, y);
    }

    private static Point ScalarMultiply(Point point, BigInteger scalar)
    {
        var result = Identity;
        var addend = point;
        while (scalar > 0)
        {
            if (!scalar.IsEven)
            {
                result = Add(result, addend);
            }

            addend = Add(addend, addend);
            scalar >>= 1;
        }

        return result;
    }

    private static Point Add(Point p, Point q)
    {
        var x1x2 = Mod(p.X * q.X);
        var y1y2 = Mod(p.Y * q.Y);
        var dxxyy = Mod(D * x1x2 * y1y2);
        var x = Mod((p.X * q.Y + q.X * p.Y) * ModInverse(1 + dxxyy));
        var y = Mod((y1y2 + x1x2) * ModInverse(1 - dxxyy));
        return new Point(x, y);
    }

    private static BigInteger ModInverse(BigInteger value)
    {
        return BigInteger.ModPow(Mod(value), Q - 2, Q);
    }

    private static BigInteger Mod(BigInteger value)
    {
        var result = value % Q;
        return result.Sign < 0 ? result + Q : result;
    }

    private static BigInteger FromLittleEndian(byte[] bytes)
    {
        return new BigInteger(bytes, isUnsigned: true, isBigEndian: false);
    }

    private readonly record struct Point(BigInteger X, BigInteger Y);
}
