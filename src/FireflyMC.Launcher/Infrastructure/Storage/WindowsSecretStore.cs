using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FireflyMC.Launcher.Configuration;
using FireflyMC.Launcher.Infrastructure.Diagnostics;
using FireflyMC.Launcher.Models.Accounts;

namespace FireflyMC.Launcher.Infrastructure.Storage;

public sealed class WindowsSecretStore(ILauncherPaths paths, IDiagnosticLogger logger) : ISecretStore
{
    public async Task<MicrosoftCredential?> LoadMicrosoftCredentialAsync(string accountId, CancellationToken cancellationToken)
    {
        var path = GetCredentialPath(accountId);
        if (!File.Exists(path))
        {
            logger.LogDebug($"未找到凭据文件 ({accountId})");
            return null;
        }

        logger.LogDebug($"加载凭据 ({accountId})");
        var protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var jsonBytes = ProtectedData.Unprotect(protectedBytes, GetEntropy(accountId), DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize(jsonBytes, JsonContext.Default.MicrosoftCredential);
    }

    public async Task SaveMicrosoftCredentialAsync(MicrosoftCredential credential, CancellationToken cancellationToken)
    {
        logger.LogInformation($"保存 Microsoft 凭据 ({credential.AccountId})");
        Directory.CreateDirectory(paths.SecretsDirectory);
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(credential, JsonContext.Default.MicrosoftCredential);
        var protectedBytes = ProtectedData.Protect(jsonBytes, GetEntropy(credential.AccountId), DataProtectionScope.CurrentUser);
        var path = GetCredentialPath(credential.AccountId);
        var tmp = $"{path}.tmp";
        await File.WriteAllBytesAsync(tmp, protectedBytes, cancellationToken);
        if (File.Exists(path))
        {
            File.Replace(tmp, path, null);
        }
        else
        {
            File.Move(tmp, path);
        }
    }

    public Task DeleteMicrosoftCredentialAsync(string accountId, CancellationToken cancellationToken)
    {
        logger.LogInformation($"删除 Microsoft 凭据 ({accountId})");
        var path = GetCredentialPath(accountId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string GetCredentialPath(string accountId)
    {
        var safeName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accountId)));
        return Path.Combine(paths.SecretsDirectory, $"{safeName}.dpapi");
    }

    private static byte[] GetEntropy(string accountId)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes($"FireflyMC.Launcher:{accountId}"));
    }
}
