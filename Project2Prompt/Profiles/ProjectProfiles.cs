using Microsoft.Extensions.FileSystemGlobbing;
using Project2Prompt.Models;

namespace Project2Prompt.Profiles;

/// <summary>プロファイルの共通実装です。</summary>
public abstract class ProjectProfileBase : IProjectProfile
{
    private readonly Lazy<Matcher> includeMatcher;

    protected ProjectProfileBase()
    {
        // 派生型のパターン初期化完了後に評価する必要があるため遅延生成します。
        includeMatcher = new Lazy<Matcher>(() =>
        {
            var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
            matcher.AddIncludePatterns(IncludePatterns);
            return matcher;
        });
    }

    public abstract ProjectKind Kind { get; }
    public abstract string DisplayName { get; }
    public abstract IReadOnlyList<string> DetectionPatterns { get; }
    public abstract IReadOnlyList<string> IncludePatterns { get; }

    /// <inheritdoc />
    public bool IsMatch(string relativePath) => includeMatcher.Value.Match(relativePath.Replace('\\', '/')).HasMatches;
}

/// <summary>C#/.NET用プロファイル。</summary>
public sealed class CSharpProjectProfile : ProjectProfileBase
{
    public override ProjectKind Kind => ProjectKind.CSharp;
    public override string DisplayName => "C# (.NET)";
    public override IReadOnlyList<string> DetectionPatterns { get; } = ["*.csproj", "*.sln", "*.slnx"];
    public override IReadOnlyList<string> IncludePatterns { get; } =
    [
        "**/*.cs", "**/*.xaml", "**/*.csproj", "**/*.sln", "**/*.slnx", "**/*.props",
        "**/*.targets", "**/*.json", "**/*.xml", "**/*.config", "**/*.resx", "**/.editorconfig",
        "**/global.json", "**/NuGet.config", "**/*.md", "**/LICENSE*",
    ];
}

/// <summary>VB.NET用プロファイル。</summary>
public sealed class VisualBasicProjectProfile : ProjectProfileBase
{
    public override ProjectKind Kind => ProjectKind.VisualBasic;
    public override string DisplayName => "VB.NET";
    public override IReadOnlyList<string> DetectionPatterns { get; } = ["*.vbproj"];
    public override IReadOnlyList<string> IncludePatterns { get; } =
    [
        "**/*.vb", "**/*.vbproj", "**/*.xaml", "**/*.config", "**/*.json", "**/*.xml",
        "**/*.resx", "**/*.settings", "**/*.myapp", "**/README.md", "**/LICENSE*",
    ];
}

/// <summary>Python用プロファイル。</summary>
public sealed class PythonProjectProfile : ProjectProfileBase
{
    public override ProjectKind Kind => ProjectKind.Python;
    public override string DisplayName => "Python";
    public override IReadOnlyList<string> DetectionPatterns { get; } = ["pyproject.toml", "requirements.txt", "setup.py", "Pipfile"];
    public override IReadOnlyList<string> IncludePatterns { get; } =
    [
        "**/*.py", "**/*.pyi", "**/pyproject.toml", "**/uv.lock", "**/requirements*.txt",
        "**/setup.py", "**/setup.cfg", "**/Pipfile", "**/Pipfile.lock", "**/poetry.lock",
        "**/*.toml", "**/*.yaml", "**/*.yml", "**/*.json", "**/*.ini", "**/*.cfg",
        "**/README.md", "**/LICENSE*",
    ];
}
