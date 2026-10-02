using System.Text;
using Project2Prompt.Models;

namespace Project2Prompt.Services;

/// <summary>ディレクトリ構造を画面・出力向けに構築します。</summary>
public interface ITreeBuilder
{
    DirectoryNode Build(string rootName, IEnumerable<ProjectFile> files);
    string BuildText(string rootName, IEnumerable<ProjectFile> files);
}

/// <summary>ツリー表示用ノードおよびテキストツリーを構築します。</summary>
public sealed class TreeBuilder : ITreeBuilder
{
    /// <inheritdoc />
    public DirectoryNode Build(string rootName, IEnumerable<ProjectFile> files)
    {
        var fileList = files.ToList();
        var root = new DirectoryNode
        {
            Name = rootName,
            IsDirectory = true,
        };

        var distinctProjects = fileList
            .Select(f => f.ProjectName)
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        bool isMultiProject = distinctProjects.Count > 1;

        var sortedFiles = fileList
            .OrderBy(f => f.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);

        foreach (var file in sortedFiles)
        {
            var relative = (isMultiProject && !string.IsNullOrEmpty(file.ProjectName))
                ? $"{file.ProjectName}/{file.RelativePath}"
                : file.RelativePath;

            var parts = relative.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            var current = root;

            // フォルダ階層の追跡および作成
            foreach (var part in parts.SkipLast(1))
            {
                var child = current.Children.FirstOrDefault(c => c.IsDirectory && string.Equals(c.Name, part, StringComparison.OrdinalIgnoreCase));
                if (child is null)
                {
                    child = new DirectoryNode
                    {
                        Name = part,
                        IsDirectory = true,
                        Parent = current,
                    };
                    current.Children.Add(child);
                }

                current = child;
            }

            // ファイルノードの追加
            if (parts.Length > 0)
            {
                var fileNode = new DirectoryNode
                {
                    Name = parts[^1],
                    IsDirectory = false,
                    File = file,
                    Parent = current,
                };
                fileNode.SyncFromFile();
                current.Children.Add(fileNode);
            }
        }

        // 全体の選択状態（3値）を配下からルートまで再計算
        RecalculateTreeSelection(root);

        return root;
    }

    /// <inheritdoc />
    public string BuildText(string rootName, IEnumerable<ProjectFile> files)
    {
        var root = Build(rootName, files);
        var builder = new StringBuilder().AppendLine(root.Name + "/");
        AppendChildren(builder, root, string.Empty);
        return builder.ToString().TrimEnd();
    }

    private static void RecalculateTreeSelection(DirectoryNode node)
    {
        foreach (var child in node.Children.Where(c => c.IsDirectory))
        {
            RecalculateTreeSelection(child);
        }

        node.RecalculateSelection();
    }

    private static void AppendChildren(StringBuilder builder, DirectoryNode node, string prefix)
    {
        var entries = node.Children
            .OrderByDescending(c => c.IsDirectory)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            var last = index == entries.Length - 1;
            var displayName = entry.IsDirectory ? entry.Name + "/" : entry.Name;

            builder.Append(prefix).Append(last ? "└─ " : "├─ ").AppendLine(displayName);
            if (entry.IsDirectory)
            {
                AppendChildren(builder, entry, prefix + (last ? "    " : "│  "));
            }
        }
    }
}