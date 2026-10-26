using Microsoft.Extensions.FileSystemGlobbing;
using Project2Prompt.Models;

namespace Project2Prompt.Profiles;

/// <summary>登録済み言語プロファイルを検索します。</summary>
public sealed class ProfileCatalog(IEnumerable<IProjectProfile> profiles) : IProfileCatalog
{
    public IReadOnlyList<IProjectProfile> Profiles { get; } = profiles.ToArray();

    /// <inheritdoc />
    public ProjectKind Detect(IEnumerable<string> relativePaths)
    {
        var paths = relativePaths.Select(p => p.Replace('\\', '/')).ToArray();
        var kind = ProjectKind.None;
        foreach (var profile in Profiles)
        {
            var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
            foreach (var pattern in profile.DetectionPatterns)
            {
                matcher.AddInclude($"**/{pattern}");
            }

            if (paths.Any(p => matcher.Match(p).HasMatches))
            {
                kind |= profile.Kind;
            }
        }

        return kind;
    }

    /// <inheritdoc />
    public bool IsIncluded(string relativePath, ProjectKind detectedKind)
    {
        var active = detectedKind == ProjectKind.None
            ? Profiles
            : Profiles.Where(p => detectedKind.HasFlag(p.Kind));
        return active.Any(p => p.IsMatch(relativePath));
    }

    /// <inheritdoc />
    public string GetFenceLanguage(string path) => Path.GetFileName(path).ToLowerInvariant() switch
    {
        "cmakelists.txt" => "cmake",
        "makefile" => "makefile",
        _ => GetFenceLanguageByExtension(path),
    };

    private static string GetFenceLanguageByExtension(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".cs" => "csharp",
        ".vb" => "vbnet",
        ".py" or ".pyi" => "python",
        ".java" => "java",
        ".gradle" => "groovy",
        ".kts" => "kotlin",
        ".properties" => "properties",
        ".go" => "go",
        ".c" or ".h" => "c",
        ".cpp" or ".cc" or ".cxx" or ".hpp" or ".hh" or ".hxx" or ".inl" or ".ipp" => "cpp",
        ".cmake" => "cmake",
        ".mk" => "makefile",
        ".xaml" or ".csproj" or ".vbproj" or ".vcxproj" or ".filters" or ".xml" or ".config" or ".props" or ".targets" or ".resx" => "xml",
        ".json" => "json",
        ".toml" => "toml",
        ".yaml" or ".yml" => "yaml",
        ".ini" or ".cfg" => "ini",
        ".md" => "markdown",
        _ => string.Empty,
    };
}
