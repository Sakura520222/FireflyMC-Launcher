using System.Text.RegularExpressions;

namespace FireflyMC.Launcher.Infrastructure.Crypto;

public static partial class SecretRedactor
{
    public static string Redact(string? input, bool redactIpAddresses = true)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var text = SensitiveKeyPattern().Replace(input, "$1=<redacted>");
        text = AuthorizationPattern().Replace(text, "$1 <redacted>");
        text = AccessTokenArgumentPattern().Replace(text, "$1 <redacted>");
        text = TokenLikePattern().Replace(text, "$1<redacted>");
        if (redactIpAddresses)
        {
            text = Ipv4Pattern().Replace(text, "<ip>");
        }

        return text;
    }

    [GeneratedRegex("(?i)(access_token|refresh_token|device_code|xbl token|xsts token|mc access token|dpapi)(\\s*[=:]\\s*)[^\\s,&]+")]
    private static partial Regex SensitiveKeyPattern();

    [GeneratedRegex("(?i)(Authorization:)\\s*(Bearer\\s+)?[^\\s]+")]
    private static partial Regex AuthorizationPattern();

    [GeneratedRegex("(?i)(--accessToken)\\s+[^\\s]+")]
    private static partial Regex AccessTokenArgumentPattern();

    [GeneratedRegex("(?i)(\"(?:accessToken|access_token|refreshToken|refresh_token|deviceCode|device_code)\"\\s*:\\s*\")[^\"]+")]
    private static partial Regex TokenLikePattern();

    [GeneratedRegex("\\b(?:(?:25[0-5]|2[0-4]\\d|1?\\d?\\d)\\.){3}(?:25[0-5]|2[0-4]\\d|1?\\d?\\d)\\b")]
    private static partial Regex Ipv4Pattern();
}
