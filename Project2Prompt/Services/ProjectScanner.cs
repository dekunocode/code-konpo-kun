using System.Text;
using Project2Prompt.Models;
using Project2Prompt.Profiles;

namespace Project2Prompt.Services;

/// <summary>ファイルシステム上のプロジェクト群を安全に走査します。</summary>
public sealed class ProjectScanner(
    IProfileCatalog profileCatalog,
    IFileFilterService fileFilter,
    ISecretScanner secretScanner) : IProjectScanner
{
    private const long MaximumFileSize = 5 * 1024 * 1024;

    /// <inheritdoc />
    public Task<ScanResult> ScanAsync(
        IReadOnlyList<string> rootPaths,
        AppSettings settings,
        IProgress<string>? progress,
        CancellationToken cancellationToken) => Task.Run(async () =>
        {
            var distinctRoots = rootPaths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (distinctRoots.Count == 0)
            {
                return new ScanResult([], ProjectKind.None, [], 0, []);
            }

            var warnings = new List<string>();
            var files = new List<ProjectFile>();
            var totalExcluded = 0;
            var totalProjectKind = ProjectKind.None;
            var validRoots = new List<string>();

            foreach (var root in distinctRoots)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!Directory.Exists(root))
                {
                    warnings.Add($"フォルダが見つかりません: {root}");
                    continue;
                }

                validRoots.Add(root);
                var projectName = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrEmpty(projectName))
                {
                    projectName = root;
                }

                var candidates = EnumerateFiles(root, warnings, cancellationToken).ToArray();
                var relativePaths = candidates.Select(p => Path.GetRelativePath(root, p).Replace('\\', '/')).ToArray();
                var projectKind = profileCatalog.Detect(relativePaths);
                totalProjectKind |= projectKind;

                for (var index = 0; index < candidates.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var path = candidates[index];
                    var relativePath = relativePaths[index];
                    progress?.Report($"解析中 [{projectName}]: {relativePath}");

                    var sensitiveFileName = secretScanner.IsSensitiveFileName(relativePath);
                    if (fileFilter.IsExcluded(relativePath, settings)
                        || (!profileCatalog.IsIncluded(relativePath, projectKind) && !sensitiveFileName))
                    {
                        totalExcluded++;
                        continue;
                    }

                    if (sensitiveFileName && settings.SecretHandling == SecretHandling.Exclude)
                    {
                        totalExcluded++;
                        warnings.Add($"秘密情報ファイルを除外しました: [{projectName}] {relativePath}");
                        continue;
                    }

                    try
                    {
                        var info = new FileInfo(path);
                        if (info.Length > MaximumFileSize)
                        {
                            totalExcluded++;
                            warnings.Add($"巨大ファイルを除外しました: [{projectName}] {relativePath}");
                            continue;
                        }

                        if (await IsBinaryAsync(path, cancellationToken).ConfigureAwait(false))
                        {
                            totalExcluded++;
                            warnings.Add($"バイナリファイルを除外しました: [{projectName}] {relativePath}");
                            continue;
                        }

                        var content = await ReadTextAsync(path, cancellationToken).ConfigureAwait(false);
                        var findings = secretScanner.Scan(content).ToList();
                        if (sensitiveFileName)
                        {
                            findings.Add(new SecretFinding(0, 0, "秘密情報ファイル"));
                        }

                        if (settings.SecretHandling == SecretHandling.Exclude && findings.Count > 0)
                        {
                            totalExcluded++;
                            warnings.Add($"秘密情報候補を含むため除外しました: [{projectName}] {relativePath}");
                            continue;
                        }

                        files.Add(new ProjectFile
                        {
                            ProjectName = projectName,
                            FullPath = path,
                            RelativePath = relativePath,
                            Content = content,
                            Size = info.Length,
                            LineCount = CountLines(content),
                            SecretFindings = findings,
                        });
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
                    {
                        totalExcluded++;
                        warnings.Add($"読み込みをスキップしました: [{projectName}] {relativePath} ({ex.Message})");
                    }
                }
            }

            return new ScanResult(validRoots, totalProjectKind, files, totalExcluded, warnings);
        }, cancellationToken);

    private static IEnumerable<string> EnumerateFiles(string root, List<string> warnings, CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            string[] directoryFiles;
            string[] childDirectories;
            try
            {
                directoryFiles = Directory.GetFiles(directory);
                childDirectories = Directory.GetDirectories(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"フォルダをスキップしました: {Path.GetRelativePath(root, directory)} ({ex.Message})");
                continue;
            }

            foreach (var file in directoryFiles)
            {
                yield return file;
            }

            foreach (var child in childDirectories)
            {
                try
                {
                    var attributes = File.GetAttributes(child);
                    if (!attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        pending.Push(child);
                    }
                    else
                    {
                        warnings.Add($"シンボリックリンクをスキップしました: {Path.GetRelativePath(root, child)}");
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    warnings.Add($"フォルダ情報を読み取れません: {Path.GetRelativePath(root, child)} ({ex.Message})");
                }
            }
        }
    }

    private static async Task<bool> IsBinaryAsync(string path, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, buffer.Length, true);
        var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.AsSpan(0, read).Contains((byte)0);
    }

    private static async Task<string> ReadTextAsync(string path, CancellationToken cancellationToken)
    {
        // Shift-JIS (CP932) 等のコードページを利用可能にする
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        try
        {
            // 1. まずは BOM 検出付き Strict UTF-8 で読み込みを試みる
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, true);
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
            return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DecoderFallbackException)
        {
            // 2. UTF-8 でデコード失敗した場合、Shift-JIS (CP932) として再読み込み
            var sjis = Encoding.GetEncoding(932);
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, true);
            using var reader = new StreamReader(stream, sjis);
            return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static int CountLines(string content) => content.Length == 0 ? 0 : content.Count(c => c == '\n') + 1;
}