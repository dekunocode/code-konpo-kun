using System.Text;
using System.Text.RegularExpressions;
using Project2Prompt.Models;
using Project2Prompt.Profiles;

namespace Project2Prompt.Services;

/// <summary>選択ファイルを1つの文書へ変換します。</summary>
public interface IProjectExporter
{
    OutputFormat Format { get; }

    /// <summary>見出し・ツリー・ファイルごとのブロックに分けた文書を作成します。</summary>
    Task<ExportDocument> BuildAsync(IReadOnlyList<string> rootPaths, IReadOnlyList<ProjectFile> files, AppSettings settings, CancellationToken cancellationToken);

    /// <summary>1つにつなげた文書を作成します。</summary>
    Task<string> ExportAsync(IReadOnlyList<string> rootPaths, IReadOnlyList<ProjectFile> files, AppSettings settings, CancellationToken cancellationToken);
}

/// <summary>Markdown形式でAI向け文書を作成します。</summary>
public sealed class MarkdownExporter(
    ITreeBuilder treeBuilder,
    IProfileCatalog profileCatalog,
    ISecretScanner secretScanner) : IProjectExporter
{
    public OutputFormat Format => OutputFormat.Markdown;

    /// <inheritdoc />
    public async Task<string> ExportAsync(IReadOnlyList<string> rootPaths, IReadOnlyList<ProjectFile> files, AppSettings settings, CancellationToken cancellationToken) =>
        (await BuildAsync(rootPaths, files, settings, cancellationToken).ConfigureAwait(false)).ToText();

    /// <inheritdoc />
    public Task<ExportDocument> BuildAsync(IReadOnlyList<string> rootPaths, IReadOnlyList<ProjectFile> files, AppSettings settings, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var heading = ExportHeading.Create(rootPaths, files);
            var title = heading.Kind switch
            {
                ExportHeadingKind.MultiProject => $"# Workspace Projects: {heading.Name}",
                ExportHeadingKind.Project => $"# Project: {heading.Name}",
                _ => "# Workspace",
            };

            var preamble = new StringBuilder();
            preamble.AppendLine();
            if (settings.IncludeDirectoryTree)
            {
                preamble.AppendLine("## Directory Tree");
                preamble.AppendLine();
                preamble.AppendLine("```text");
                preamble.AppendLine(treeBuilder.BuildText(heading.TreeRootName, files));
                preamble.AppendLine("```");
                preamble.AppendLine();
            }

            var blocks = new List<ExportBlock>();
            foreach (var file in ExportHeading.Sort(files))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var info = settings.IncludeFileInfo ? $"<!-- lines: {file.LineCount}, bytes: {file.Size} -->" : null;
                blocks.Add(new ExportBlock(
                    heading.GetDisplayPath(file),
                    ExportTextProcessor.Process(file, settings, secretScanner),
                    profileCatalog.GetFenceLanguage(file.RelativePath),
                    info));
            }

            return new ExportDocument(OutputFormat.Markdown, title, preamble.ToString(), blocks);
        }, cancellationToken);
}

/// <summary>プレーンテキスト形式でAI向け文書を作成します。</summary>
public sealed class TextExporter(ITreeBuilder treeBuilder, ISecretScanner secretScanner) : IProjectExporter
{
    public OutputFormat Format => OutputFormat.PlainText;

    /// <inheritdoc />
    public async Task<string> ExportAsync(IReadOnlyList<string> rootPaths, IReadOnlyList<ProjectFile> files, AppSettings settings, CancellationToken cancellationToken) =>
        (await BuildAsync(rootPaths, files, settings, cancellationToken).ConfigureAwait(false)).ToText();

    /// <inheritdoc />
    public Task<ExportDocument> BuildAsync(IReadOnlyList<string> rootPaths, IReadOnlyList<ProjectFile> files, AppSettings settings, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var heading = ExportHeading.Create(rootPaths, files);
            var title = heading.Kind switch
            {
                ExportHeadingKind.MultiProject => $"WORKSPACE PROJECTS: {heading.Name}",
                ExportHeadingKind.Project => $"PROJECT: {heading.Name}",
                _ => "WORKSPACE",
            };

            var preamble = new StringBuilder();
            if (settings.IncludeDirectoryTree)
            {
                preamble.AppendLine(new string('=', 72));
                preamble.AppendLine(treeBuilder.BuildText(heading.TreeRootName, files));
            }

            var blocks = new List<ExportBlock>();
            foreach (var file in ExportHeading.Sort(files))
            {
                cancellationToken.ThrowIfCancellationRequested();
                blocks.Add(new ExportBlock(
                    heading.GetDisplayPath(file),
                    ExportTextProcessor.Process(file, settings, secretScanner),
                    string.Empty,
                    null));
            }

            return new ExportDocument(OutputFormat.PlainText, title, preamble.ToString(), blocks);
        }, cancellationToken);
}

internal enum ExportHeadingKind
{
    MultiProject,
    Project,
    Workspace,
}

/// <summary>出力の見出しとツリーの起点名を決めます。</summary>
internal sealed record ExportHeading(ExportHeadingKind Kind, string Name, string TreeRootName, bool IsMultiProject)
{
    public static ExportHeading Create(IReadOnlyList<string> rootPaths, IReadOnlyList<ProjectFile> files)
    {
        var projectNames = files
            .Select(f => f.ProjectName)
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (projectNames.Count > 1)
        {
            return new(ExportHeadingKind.MultiProject, string.Join(", ", projectNames), "Workspace", true);
        }

        if (projectNames.Count == 1)
        {
            return new(ExportHeadingKind.Project, projectNames[0], projectNames[0], false);
        }

        if (rootPaths.Count == 1)
        {
            var name = Path.GetFileName(rootPaths[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return new(ExportHeadingKind.Project, name, name, false);
        }

        return new(ExportHeadingKind.Workspace, string.Empty, "Workspace", false);
    }

    public static IEnumerable<ProjectFile> Sort(IEnumerable<ProjectFile> files) => files
        .OrderBy(f => f.ProjectName, StringComparer.OrdinalIgnoreCase)
        .ThenBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);

    public string GetDisplayPath(ProjectFile file) => IsMultiProject && !string.IsNullOrEmpty(file.ProjectName)
        ? $"{file.ProjectName}/{file.RelativePath}"
        : file.RelativePath;
}

internal static partial class ExportTextProcessor
{
    public static string Process(ProjectFile file, AppSettings settings, ISecretScanner secretScanner)
    {
        var text = settings.SecretHandling == SecretHandling.Mask
            ? secretScanner.Mask(file.Content, file.SecretFindings)
            : file.Content;
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

        if (settings.RemoveComments)
        {
            text = CommentStripper.Remove(text, Path.GetExtension(file.RelativePath));
        }

        if (settings.RemoveRegions)
        {
            text = RegionRegex().Replace(text, string.Empty);
        }

        if (settings.TrimTrailingWhitespace)
        {
            text = string.Join('\n', text.Split('\n').Select(line => line.TrimEnd()));
        }

        if (settings.NormalizeBlankLines)
        {
            text = BlankLinesRegex().Replace(text, "\n\n");
        }

        if (settings.AddLineNumbers)
        {
            var lines = text.Split('\n');
            var width = Math.Max(1, lines.Length.ToString().Length);
            text = string.Join('\n', lines.Select((line, index) => $"{(index + 1).ToString().PadLeft(width)} | {line}"));
        }

        return text.TrimEnd('\n');
    }

    [GeneratedRegex(@"(?m)^\s*#(?:end)?region.*(?:\n|$)")]
    private static partial Regex RegionRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLinesRegex();
}

internal static partial class CommentStripper
{
    public static string Remove(string text, string extension) => extension.ToLowerInvariant() switch
    {
        ".cs" => RemoveCStyle(text, allowVerbatimString: true),
        ".java" or ".gradle" or ".kts" => RemoveCStyle(text),
        ".c" or ".h" or ".cpp" or ".cc" or ".cxx" or ".hpp" or ".hh" or ".hxx" or ".inl" or ".ipp" => RemoveCStyle(text),
        ".go" => RemoveCStyle(text, allowRawBacktickString: true),
        ".vb" => RemoveLineStyle(text, "'"),
        ".py" or ".pyi" => RemovePythonComments(text),
        ".xaml" or ".xml" or ".csproj" or ".vbproj" or ".vcxproj" or ".filters" or ".props" or ".targets" or ".resx" => XmlCommentRegex().Replace(text, string.Empty),
        _ => text,
    };

    private static string RemoveCStyle(string text, bool allowVerbatimString = false, bool allowRawBacktickString = false)
    {
        var result = new StringBuilder(text.Length);
        var inString = false;
        var inCharacter = false;
        var inVerbatimString = false;
        var inBlockComment = false;

        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            var next = index + 1 < text.Length ? text[index + 1] : '\0';
            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    inBlockComment = false;
                    index++;
                }
                else if (current == '\n')
                {
                    result.Append(current);
                }

                continue;
            }

            if (!inString && !inCharacter && current == '/' && next == '/')
            {
                while (index < text.Length && text[index] != '\n') index++;
                if (index < text.Length) result.Append('\n');
                continue;
            }

            if (!inString && !inCharacter && current == '/' && next == '*')
            {
                inBlockComment = true;
                index++;
                continue;
            }

            // Goの `...` はエスケープを持たないため、閉じのバッククォートまでそのまま残します。
            if (allowRawBacktickString && !inString && !inCharacter && current == '`')
            {
                var end = text.IndexOf('`', index + 1);
                if (end < 0) end = text.Length - 1;
                result.Append(text, index, end - index + 1);
                index = end;
                continue;
            }

            result.Append(current);
            if (!inCharacter && current == '"')
            {
                if (inVerbatimString && next == '"')
                {
                    result.Append(next);
                    index++;
                    continue;
                }

                if (index == 0 || text[index - 1] != '\\' || inVerbatimString)
                {
                    inString = !inString;
                    if (!inString) inVerbatimString = false;
                }
            }
            else if (!inString && current == '\'' && (index == 0 || text[index - 1] != '\\'))
            {
                inCharacter = !inCharacter;
            }
            else if (allowVerbatimString && !inString && !inCharacter && current == '@' && next == '"')
            {
                inVerbatimString = true;
            }
        }

        return result.ToString();
    }

    private static string RemoveLineStyle(string text, string marker) => string.Join('\n', text.Split('\n').Select(line =>
    {
        var inString = false;
        for (var index = 0; index <= line.Length - marker.Length; index++)
        {
            if (line[index] == '"')
            {
                inString = !inString;
            }
            else if (!inString && line.AsSpan(index).StartsWith(marker, StringComparison.Ordinal))
            {
                return line[..index];
            }
        }

        return line;
    }));

    private static string RemovePythonComments(string text)
    {
        var result = new StringBuilder(text.Length);
        var quote = '\0';
        var tripleQuote = '\0';
        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            if (tripleQuote != '\0')
            {
                result.Append(current);
                if (current == tripleQuote && index + 2 < text.Length && text[index + 1] == current && text[index + 2] == current)
                {
                    result.Append(current).Append(current);
                    index += 2;
                    tripleQuote = '\0';
                }

                continue;
            }

            if (quote == '\0' && (current == '\'' || current == '"'))
            {
                if (index + 2 < text.Length && text[index + 1] == current && text[index + 2] == current)
                {
                    tripleQuote = current;
                    result.Append(current).Append(current).Append(current);
                    index += 2;
                    continue;
                }

                quote = current;
            }
            else if (quote != '\0' && current == quote && (index == 0 || text[index - 1] != '\\'))
            {
                quote = '\0';
            }
            else if (quote == '\0' && current == '#')
            {
                while (index < text.Length && text[index] != '\n') index++;
                if (index < text.Length) result.Append('\n');
                continue;
            }

            result.Append(current);
        }

        return result.ToString();
    }

    [GeneratedRegex(@"<!--[\s\S]*?-->")]
    private static partial Regex XmlCommentRegex();
}