using System.Text;
using Project2Prompt.Models;

namespace Project2Prompt.Services;

/// <summary>出力文書を、指定した文字数以下の複数のパートに分けます。</summary>
/// <remarks>
/// ファイルの境目で区切ります。1ファイルだけで上限を超えるときは、そのファイルを行で区切ります。
/// ディレクトリツリーは最初のパートにだけ入れます。
/// 推定トークン数は文字数から割り出しているので、トークン数の上限は文字数に直してから渡します。
/// </remarks>
public static class OutputSplitter
{
    /// <summary>上限として指定できる最小の文字数。</summary>
    public const int MinimumMaxCharacters = 4 * 1024;

    // パート番号やファイル内の分割番号は、見積もりのときだけ3桁の仮の値で数えます。
    private const string PlaceholderNumber = "999";

    /// <summary>文書をパートに分けます。上限に収まるときは、分けずに1つだけ返します。</summary>
    /// <param name="document">分ける文書。</param>
    /// <param name="maxCharacters">1パートの上限文字数。</param>
    public static IReadOnlyList<string> Split(ExportDocument document, int maxCharacters)
    {
        maxCharacters = Math.Max(maxCharacters, MinimumMaxCharacters);
        var whole = document.ToText();
        if (whole.Length <= maxCharacters)
        {
            return [whole];
        }

        var units = CreateUnits(document, maxCharacters);
        var groups = Pack(document, units, maxCharacters);
        return groups
            .Select((group, index) => RenderPart(document, index + 1, groups.Count, group))
            .ToList();
    }

    /// <summary>保存先のパスから、パートごとのファイル名を作ります（例: project-context_part01of03.md）。</summary>
    public static string GetPartPath(string path, int partNumber, int partCount)
    {
        var width = Math.Max(2, partCount.ToString().Length);
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var fileName = $"{name}_part{partNumber.ToString().PadLeft(width, '0')}of{partCount.ToString().PadLeft(width, '0')}{extension}";
        return Path.Combine(directory, fileName);
    }

    private static List<Unit> CreateUnits(ExportDocument document, int maxCharacters)
    {
        var preambleLength = document.Preamble.Length;
        var units = new List<Unit>();
        foreach (var block in document.Blocks)
        {
            var text = document.RenderBlock(block);
            var placeholderLabel = $"{block.DisplayPath} ({Continuation(PlaceholderNumber, PlaceholderNumber)})";

            // どのパートに入っても収まるように、ツリーの分も差し引いて見積もります。
            var budget = maxCharacters - HeaderLength(document, [placeholderLabel]) - preambleLength;
            if (text.Length <= budget)
            {
                units.Add(new Unit(block.DisplayPath, text));
                continue;
            }

            var overhead = document.RenderBlock(
                block with { Content = "````" },
                Continuation(PlaceholderNumber, PlaceholderNumber)).Length;
            var contentBudget = Math.Max(1024, budget - overhead);
            var chunks = SplitContent(block.Content, contentBudget);
            for (var index = 0; index < chunks.Count; index++)
            {
                var continuation = Continuation((index + 1).ToString(), chunks.Count.ToString());
                units.Add(new Unit(
                    $"{block.DisplayPath} ({continuation})",
                    document.RenderBlock(block with { Content = chunks[index] }, continuation)));
            }
        }

        return units;
    }

    private static List<List<Unit>> Pack(ExportDocument document, List<Unit> units, int maxCharacters)
    {
        var preambleLength = document.Preamble.Length;
        var baseHeaderLength = HeaderLength(document, []);
        var groups = new List<List<Unit>>();
        var current = new List<Unit>();
        long currentLength = 0;

        foreach (var unit in units)
        {
            // 見出しのファイル一覧に1行ずつ増える分も含めて数えます。
            var size = baseHeaderLength
                + (groups.Count == 0 ? preambleLength : 0)
                + currentLength
                + unit.Length + unit.ListLineLength;
            if (current.Count > 0 && size > maxCharacters)
            {
                groups.Add(current);
                current = [];
                currentLength = 0;
            }

            current.Add(unit);
            currentLength += unit.Length + unit.ListLineLength;
        }

        groups.Add(current);
        return groups;
    }

    private static string RenderPart(ExportDocument document, int partNumber, int partCount, List<Unit> units)
    {
        var builder = new StringBuilder();
        builder.Append(Header(document, partNumber.ToString(), partCount.ToString(), units.Select(u => u.Label)));
        builder.Append(partNumber == 1 ? document.Preamble : FirstLineBreak(document));
        foreach (var unit in units)
        {
            builder.Append(unit.Text);
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    // Markdownのツリー部分は空行から始まるので、2つ目以降のパートでも同じ間を空けます。
    private static string FirstLineBreak(ExportDocument document) =>
        document.Format == OutputFormat.Markdown ? Environment.NewLine : string.Empty;

    private static string Header(ExportDocument document, string partNumber, string partCount, IEnumerable<string> labels)
    {
        var builder = new StringBuilder();
        if (document.Format == OutputFormat.Markdown)
        {
            builder.Append(document.Title).Append(" (Part ").Append(partNumber).Append('/').Append(partCount).AppendLine(")");
            builder.AppendLine();
            builder.Append("> This is part ").Append(partNumber).Append(" of ").Append(partCount)
                .AppendLine(". The project was split into several files because of its size.");
            builder.Append("> Read all ").Append(partCount).AppendLine(" parts before answering.");
            builder.AppendLine();
            builder.AppendLine("Files in this part:");
            builder.AppendLine();
        }
        else
        {
            builder.Append(document.Title).Append(" (PART ").Append(partNumber).Append('/').Append(partCount).AppendLine(")");
            builder.Append("NOTE: This is part ").Append(partNumber).Append(" of ").Append(partCount)
                .AppendLine(". The project was split into several files because of its size.");
            builder.Append("NOTE: Read all ").Append(partCount).AppendLine(" parts before answering.");
            builder.AppendLine("FILES IN THIS PART:");
        }

        foreach (var label in labels)
        {
            builder.Append("- ").AppendLine(label);
        }

        return builder.ToString();
    }

    private static int HeaderLength(ExportDocument document, IEnumerable<string> labels) =>
        Header(document, PlaceholderNumber, PlaceholderNumber, labels).Length;

    private static string Continuation(string number, string count) => $"part {number}/{count} of this file";

    /// <summary>ファイル内容を、行の境目で指定文字数以下に区切ります。</summary>
    public static List<string> SplitContent(string content, int maxCharacters)
    {
        var chunks = new List<string>();
        var current = new StringBuilder();
        var currentLength = 0;

        void Flush()
        {
            if (current.Length == 0) return;
            chunks.Add(current.ToString(0, current.Length - 1)); // 最後の改行を外す
            current.Clear();
            currentLength = 0;
        }

        foreach (var line in content.Split('\n'))
        {
            var lineLength = line.Length + 1;
            if (lineLength > maxCharacters)
            {
                // 1行だけで上限を超える（圧縮されたコードなど）ときは、行の途中で区切ります。
                Flush();
                chunks.AddRange(SplitLongLine(line, maxCharacters));
                continue;
            }

            if (currentLength + lineLength > maxCharacters)
            {
                Flush();
            }

            current.Append(line).Append('\n');
            currentLength += lineLength;
        }

        Flush();
        return chunks;
    }

    private static IEnumerable<string> SplitLongLine(string line, int maxCharacters)
    {
        var current = new StringBuilder();
        var currentLength = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            if (currentLength + rune.Utf16SequenceLength > maxCharacters)
            {
                yield return current.ToString();
                current.Clear();
                currentLength = 0;
            }

            current.Append(rune.ToString());
            currentLength += rune.Utf16SequenceLength;
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    private sealed record Unit(string Label, string Text)
    {
        public long Length { get; } = Text.Length;

        public long ListLineLength { get; } = $"- {Label}{Environment.NewLine}".Length;
    }
}
