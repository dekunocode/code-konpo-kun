using Project2Prompt.Models;

namespace Project2Prompt.Profiles;

/// <summary>言語ごとの検出・収集規則を提供します。</summary>
public interface IProjectProfile
{
    ProjectKind Kind { get; }
    string DisplayName { get; }
    IReadOnlyList<string> DetectionPatterns { get; }
    IReadOnlyList<string> IncludePatterns { get; }
    bool IsMatch(string relativePath);
}

/// <summary>利用可能なプロファイルを集約します。</summary>
public interface IProfileCatalog
{
    IReadOnlyList<IProjectProfile> Profiles { get; }
    ProjectKind Detect(IEnumerable<string> relativePaths);
    bool IsIncluded(string relativePath, ProjectKind detectedKind);
    string GetFenceLanguage(string path);
}
