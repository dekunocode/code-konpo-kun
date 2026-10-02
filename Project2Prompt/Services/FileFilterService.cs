using Microsoft.Extensions.FileSystemGlobbing;
using Project2Prompt.Models;

namespace Project2Prompt.Services;

/// <summary>不要な生成物やキャッシュを除外します。</summary>
public sealed class FileFilterService : IFileFilterService
{
    private static readonly string[] StandardExcludes =
    [
        "**/bin/**", "**/obj/**", "**/.git/**", "**/.vs/**", "**/.idea/**", "**/node_modules/**",
        "**/packages/**", "**/dist/**", "**/build/**", "**/publish/**", "**/TestResults/**",
        "**/__pycache__/**", "**/.pytest_cache/**", "**/.mypy_cache/**", "**/.ruff_cache/**",
        "**/.venv/**", "**/venv/**", "**/env/**", "**/*.g.cs", "**/*.g.i.cs", "**/*.Designer.cs",
        "**/*.Designer.vb", "**/*.g.vb", "**/*.g.i.vb", "**/*.user", "**/*.suo",
    ];

    /// <inheritdoc />
    public bool IsExcluded(string relativePath, AppSettings settings)
    {
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddIncludePatterns(StandardExcludes);
        matcher.AddIncludePatterns(settings.AdditionalExcludes.Where(p => !string.IsNullOrWhiteSpace(p)));
        return matcher.Match(relativePath.Replace('\\', '/')).HasMatches;
    }
}
