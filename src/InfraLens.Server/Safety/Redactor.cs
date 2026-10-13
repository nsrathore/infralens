using System.Text.RegularExpressions;

namespace InfraLens.Server.Safety;

/// <summary>Masks secrets before any text leaves the server. Applied to every tool result.</summary>
public static partial class Redactor
{
    public const string Mask = "[REDACTED]";

    public static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        text = AwsAccessKeyId().Replace(text, Mask);
        text = BearerToken().Replace(text, "Bearer " + Mask);
        text = SecretAssignment().Replace(text, m => $"{m.Groups["key"].Value}{m.Groups["sep"].Value}{Mask}");
        text = AnthropicKey().Replace(text, Mask);
        return text;
    }

    [GeneratedRegex(@"\b(AKIA|ASIA)[0-9A-Z]{16}\b")]
    private static partial Regex AwsAccessKeyId();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.IgnoreCase)]
    private static partial Regex BearerToken();

    // password=..., "apiKey": "...", secret: ..., token=...
    [GeneratedRegex(
        @"(?<key>""?\b[\w-]*(password|passwd|secret|token|api[_-]?key|connection[_-]?string)[\w-]*""?)(?<sep>\s*[:=]\s*)(""[^""]*""|[^\s,;}]+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex SecretAssignment();

    [GeneratedRegex(@"\bsk-ant-[A-Za-z0-9\-_]{10,}")]
    private static partial Regex AnthropicKey();
}
