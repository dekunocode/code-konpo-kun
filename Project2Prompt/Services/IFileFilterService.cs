using Project2Prompt.Models;

namespace Project2Prompt.Services;

/// <summary>標準およびユーザー指定の除外規則を評価します。</summary>
public interface IFileFilterService
{
    bool IsExcluded(string relativePath, AppSettings settings);
}

/// <summary>秘密情報候補を検出・マスクします。</summary>
public interface ISecretScanner
{
    bool IsSensitiveFileName(string relativePath);
    IReadOnlyList<SecretFinding> Scan(string content);
    string Mask(string content, IReadOnlyList<SecretFinding> findings);
}

/// <summary>プロジェクトフォルダを非同期で走査します。</summary>
public interface IProjectScanner
{
    Task<ScanResult> ScanAsync(IReadOnlyList<string> rootPaths, AppSettings settings, IProgress<string>? progress, CancellationToken cancellationToken);
}