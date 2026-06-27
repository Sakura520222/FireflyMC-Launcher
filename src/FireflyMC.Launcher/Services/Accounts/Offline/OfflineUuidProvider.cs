using System.Security.Cryptography;
using System.Text;

namespace FireflyMC.Launcher.Services.Accounts.Offline;

public sealed class OfflineUuidProvider
{
    public Guid GetUuid(string username)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"OfflinePlayer:{username}"));
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x30);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes);
    }

    public string GetUuidString(string username)
    {
        return ToJavaUuidString(GetUuid(username));
    }

    private static string ToJavaUuidString(Guid guid)
    {
        var bytes = guid.ToByteArray();
        Array.Reverse(bytes, 0, 4);
        Array.Reverse(bytes, 4, 2);
        Array.Reverse(bytes, 6, 2);
        return new Guid(bytes).ToString("N");
    }
}
