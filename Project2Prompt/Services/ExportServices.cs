using System.Text;
using System.Text.RegularExpressions;
using Project2Prompt.Models;
using Project2Prompt.Profiles;

namespace Project2Prompt.Services;

/// <summary>選択ファイルを1つの文書へ変換します。</summary>
public interface IProjectExporter
{
    OutputFormat Format { get; }
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
    public Task<string> ExportAsync(IReadOnlyList<string> rootPaths, IReadOnlyList<ProjectFile> files, AppSettings settings, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var builder = new StringBuilder();

            var projectNames = files
                .Select(f => f.ProjectName)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            bool isMultiProject = projectNames.Count > 1;
            string treeRootName;

            if (isMultiProject)
            {
                builder.Append("# Workspace Projects: ").AppendLine(string.Join(", ", projectNames));
                treeRootName = "Workspace";
            }
            else if (projectNames.Count == 1)
            {
                builder.Append("# Project: ").AppendLine(projectNames[0]);
                treeRootName = projectNames[0];
            }
            else if (rootPaths.Count == 1)
            {
                var name = Path.GetFileName(rootPaths[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                builder.Append("# Project: ").AppendLine(name);
                treeRootName = name;
            }
            else
            {
                builder.AppendLine("# Workspace");
                treeRootName = "Workspace";
            }

            builder.AppendLine();
            if (settings.IncludeDirectoryTree)
            {
                builder.AppendLine("## Directory Tree");
                builder.AppendLine();
                builder.AppendLine("```text");
                builder.AppendLine(treeBuilder.BuildText(treeRootName, files));
                builder.AppendLine("```");
                builder.AppendLine();
            }

            var sortedFiles = files
                .OrderBy(f => f.ProjectName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);

            foreach (var file in sortedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var filePathDisplay = isMultiProject && !string.IsNullOrEmpty(file.ProjectName)
                    ? $"{file.ProjectName}/{file.RelativePath}"
                    : file.RelativePath;

                builder.Append("## File: ").AppendLine(filePathDisplay);
                builder.AppendLine();
                if (settings.IncludeFileInfo)
                {
                    builder.Append("<!-- lines: ").Append(file.LineCount)
                        .Append(", bytes: ").Append(file.Size).AppendLine(" -->");
                }

                var content = ExportTextProcessor.Process(file, settings, secretScanner);
                var fence = content.Contains("```", StringComparison.Ordinal) ? "````" : "```";
                builder.Append(fence).AppendLine(profileCatalog.GetFenceLanguage(file.RelativePath));
                builder.AppendLine(content);
                builder.AppendLine(fence);
                builder.AppendLine();
            }

            return builder.ToString().TrimEnd() + Environment.NewLine;
        }, cancellationToken);
}

/// <summary>プレーンテキスト形式でAI向け文書を作成します。</summary>
public sealed class TextExporter(ITreeBuilder treeBuilder, ISecretScanner secretScanner) : IProjectExporter
{
    public OutputFormat Format => OutputFormat.PlainText;

    /// <inheritdoc />
    public Task<string> ExportAsync(IReadOnlyList<string> rootPaths, IReadOnlyList<ProjectFile> files, AppSettings settings, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var builder = new StringBuilder();

            var projectNames = files
                .Select(f => f.ProjectName)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            bool isMultiProject = projectNames.Count > 1;
            string treeRootName;

            if (isMultiProject)
            {
                builder.Append("WORKSPACE PROJECTS: ").AppendLine(string.Join(", ", projectNames));
                treeRootName = "Workspace";
            }
            else if (projectNames.Count == 1)
            {
                builder.Append("PROJECT: ").AppendLine(projectNames[0]);
                treeRootName = projectNames[0];
            }
            else if (rootPaths.Count == 1)
            {
                var name = Path.GetFileName(rootPaths[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                builder.Append("PROJECT: ").AppendLine(name);
                treeRootName = name;
            }
            else
            {
                builder.AppendLine("WORKSPACE");
                treeRootName = "Workspace";
            }

            if (settings.IncludeDirectoryTree)
            {
                builder.AppendLine(new string('=', 72));
                builder.AppendLine(treeBuilder.BuildText(treeRootName, files));
            }

            var sortedFiles = files
                .OrderBy(f => f.ProjectName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);

            foreach (var file in sortedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var filePathDisplay = isMultiProject && !string.IsNullOrEmpty(file.ProjectName)
                    ? $"{file.ProjectName}/{file.RelativePath}"
                    : file.RelativePath;

                builder.AppendLine().AppendLine(new string('=', 72));
                builder.Append("FILE: ").AppendLine(filePathDisplay);
                builder.AppendLine(new string('-', 72));
                builder.AppendLine(ExportTextProcessor.Process(file, settings, secretScanner));
            }

            return builder.ToString().TrimEnd() + Environment.NewLine;
        }, cancellationToken);
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
        ".cs" => RemoveCStyle(text),
        ".vb" => RemoveLineStyle(text, "'"),
        ".py" or ".pyi" => RemovePythonComments(text),
        ".xaml" or ".xml" or ".csproj" or ".vbproj" or ".props" or ".targets" or ".resx" => XmlCommentRegex().Replace(text, string.Empty),
        _ => text,
    };

    private static string RemoveCStyle(string text)
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
            else if (!inString && !inCharacter && current == '@' && next == '"')
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