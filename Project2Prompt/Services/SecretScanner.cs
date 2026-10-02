using System.Text.RegularExpressions;
using Project2Prompt.Models;

namespace Project2Prompt.Services;

/// <summary>一般的なキー、トークン、パスワード表現を保守的に検出します。</summary>
public sealed partial class SecretScanner : ISecretScanner
{
    private static readonly string[] SensitiveFileNames = [".env", "secrets.json", "appsettings.production.json"];
    private static readonly string[] SensitiveExtensions = [".pfx", ".pem", ".key", ".p12"];

    /// <inheritdoc />
    public bool IsSensitiveFileName(string relativePath)
    {
        var name = Path.GetFileName(relativePath);
        return SensitiveFileNames.Contains(name, StringComparer.OrdinalIgnoreCase)
            || name.StartsWith(".env.", StringComparison.OrdinalIgnoreCase)
            || SensitiveExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public IReadOnlyList<SecretFinding> Scan(string content)
    {
        var findings = new List<SecretFinding>();
        AddMatches(findings, GitHubTokenRegex(), content, "GitHub Token");
        AddMatches(findings, AwsAccessKeyRegex(), content, "AWS Access Key");
        AddMatches(findings, GenericSecretRegex(), content, "API Key / Password");
        AddMatches(findings, ConnectionStringRegex(), content, "Connection String");
        var nonOverlapping = new List<SecretFinding>();
        foreach (var finding in findings.OrderBy(f => f.Start).ThenByDescending(f => f.Length))
        {
            if (nonOverlapping.All(existing => finding.Start >= existing.Start + existing.Length
                || existing.Start >= finding.Start + finding.Length))
            {
                nonOverlapping.Add(finding);
            }
        }

        return nonOverlapping;
    }

    /// <inheritdoc />
    public string Mask(string content, IReadOnlyList<SecretFinding> findings)
    {
        if (findings.Count == 0)
        {
            return content;
        }

        var result = content;
        foreach (var finding in findings.OrderByDescending(f => f.Start))
        {
            if (finding.Start >= 0 && finding.Start + finding.Length <= result.Length)
            {
                result = result.Remove(finding.Start, finding.Length).Insert(finding.Start, "***MASKED***");
            }
        }

        return result;
    }

    private static void AddMatches(List<SecretFinding> target, Regex regex, string content, string description)
    {
        foreach (Match match in regex.Matches(content))
        {
            var group = match.Groups["secret"];
            if (group.Success && !LooksLikePlaceholder(group.Value))
            {
                target.Add(new SecretFinding(group.Index, group.Length, description));
            }
        }
    }

    private static bool LooksLikePlaceholder(string value) =>
        value.Contains("example", StringComparison.OrdinalIgnoreCase)
        || value.Contains("your_", StringComparison.OrdinalIgnoreCase)
        || value.Contains("changeme", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("${", StringComparison.Ordinal);

    [GeneratedRegex(@"\b(?<secret>(?:ghp|gho|ghu|ghs|github_pat)_[A-Za-z0-9_]{20,})\b", RegexOptions.Compiled)]
    private static partial Regex GitHubTokenRegex();

    [GeneratedRegex(@"\b(?<secret>AKIA[0-9A-Z]{16})\b", RegexOptions.Compiled)]
    private static partial Regex AwsAccessKeyRegex();

    [GeneratedRegex("""(?im)(?:api[_-]?key|token|secret|password|passwd|pwd)\s*[:=]\s*['"]?(?<secret>[^'"\s,;]{8,})""", RegexOptions.Compiled)]
    private static partial Regex GenericSecretRegex();

    [GeneratedRegex("""(?im)(?:connectionstrings?[^\r\n:=]*|server|data source)\s*[:=]\s*['"]?(?<secret>[^'"\r\n]{12,})""", RegexOptions.Compiled)]
    private static partial Regex ConnectionStringRegex();
}
