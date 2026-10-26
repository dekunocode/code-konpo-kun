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

/// <summary>Java用プロファイル。</summary>
public sealed class JavaProjectProfile : ProjectProfileBase
{
    public override ProjectKind Kind => ProjectKind.Java;
    public override string DisplayName => "Java";
    public override IReadOnlyList<string> DetectionPatterns { get; } =
        ["pom.xml", "build.gradle", "build.gradle.kts", "settings.gradle", "settings.gradle.kts"];
    public override IReadOnlyList<string> IncludePatterns { get; } =
    [
        "**/*.java", "**/pom.xml", "**/*.gradle", "**/*.gradle.kts", "**/gradle.properties",
        "**/gradle-wrapper.properties", "**/*.properties", "**/*.xml", "**/*.yaml", "**/*.yml",
        "**/*.json", "**/README.md", "**/LICENSE*",
    ];
}

/// <summary>Go用プロファイル。</summary>
public sealed class GoProjectProfile : ProjectProfileBase
{
    public override ProjectKind Kind => ProjectKind.Go;
    public override string DisplayName => "Go";
    public override IReadOnlyList<string> DetectionPatterns { get; } = ["go.mod", "go.work"];
    public override IReadOnlyList<string> IncludePatterns { get; } =
    [
        "**/*.go", "**/go.mod", "**/go.work", "**/Makefile", "**/*.toml", "**/*.yaml", "**/*.yml",
        "**/*.json", "**/README.md", "**/LICENSE*",
    ];
}

/// <summary>C/C++用プロファイル。</summary>
public sealed class CppProjectProfile : ProjectProfileBase
{
    public override ProjectKind Kind => ProjectKind.Cpp;
    public override string DisplayName => "C/C++";
    public override IReadOnlyList<string> DetectionPatterns { get; } = ["CMakeLists.txt", "*.vcxproj", "meson.build"];
    public override IReadOnlyList<string> IncludePatterns { get; } =
    [
        "**/*.c", "**/*.h", "**/*.cpp", "**/*.cc", "**/*.cxx", "**/*.hpp", "**/*.hh", "**/*.hxx",
        "**/*.inl", "**/*.ipp", "**/CMakeLists.txt", "**/*.cmake", "**/CMakePresets.json",
        "**/*.vcxproj", "**/*.vcxproj.filters", "**/*.props", "**/*.sln", "**/*.slnx",
        "**/meson.build", "**/Makefile", "**/*.mk", "**/vcpkg.json", "**/conanfile.txt",
        "**/.clang-format", "**/.editorconfig", "**/README.md", "**/LICENSE*",
    ];
}
