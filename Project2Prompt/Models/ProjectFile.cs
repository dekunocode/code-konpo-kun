using CommunityToolkit.Mvvm.ComponentModel;

namespace Project2Prompt.Models;

/// <summary>スキャンされた1ファイルと選択状態を保持します。</summary>
public partial class ProjectFile : ObservableObject
{
    /// <summary>所属するプロジェクト名。</summary>
    public required string ProjectName { get; init; }

    /// <summary>ファイルの絶対パス。</summary>
    public required string FullPath { get; init; }

    /// <summary>プロジェクトルートからの相対パス。</summary>
    public required string RelativePath { get; init; }

    /// <summary>画面表示・識別用のプロジェクト名付き相対パス。</summary>
    public string DisplayRelativePath => string.IsNullOrEmpty(ProjectName) ? RelativePath : $"[{ProjectName}] {RelativePath}";

    /// <summary>読み込んだテキスト。</summary>
    public required string Content { get; init; }

    /// <summary>ファイルサイズ。</summary>
    public long Size { get; init; }

    /// <summary>行数。</summary>
    public int LineCount { get; init; }

    /// <summary>秘密情報の候補。</summary>
    public IReadOnlyList<SecretFinding> SecretFindings { get; init; } = [];

    /// <summary>出力対象かどうか。</summary>
    [ObservableProperty]
    private bool isSelected = true;

    /// <summary>秘密情報候補を含むかどうか。</summary>
    public bool HasSecrets => SecretFindings.Count > 0;

    /// <summary>画面表示用の秘密情報概要。</summary>
    public string SecretSummary => string.Join("、", SecretFindings.Select(f => f.Description).Distinct());
}

/// <summary>秘密情報候補の位置と種類を表します。</summary>
public sealed record SecretFinding(int Start, int Length, string Description);