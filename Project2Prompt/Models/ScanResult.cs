namespace Project2Prompt.Models;

/// <summary>プロジェクトスキャンの結果です。</summary>
public sealed record ScanResult(
    IReadOnlyList<string> RootPaths,
    ProjectKind ProjectKind,
    IReadOnlyList<ProjectFile> Files,
    int ExcludedFileCount,
    IReadOnlyList<string> Warnings);

/// <summary>画面に表示する集計値です。</summary>
public sealed record ProjectStatistics(
    int IncludedFiles,
    int ExcludedFiles,
    long TotalLines,
    long TotalCharacters,
    long EstimatedTokens,
    long OutputBytes,
    string LargestFile,
    string MostLinesFile)
{
    /// <summary>空の統計値。</summary>
    public static ProjectStatistics Empty { get; } = new(0, 0, 0, 0, 0, 0, "-", "-");
}