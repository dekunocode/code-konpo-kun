using System.Text;

namespace Project2Prompt.Models;

/// <summary>出力に入れる1ファイル分の中身です。</summary>
/// <param name="DisplayPath">出力に書く相対パス。</param>
/// <param name="Content">整形済みのファイル内容（改行はLF）。</param>
/// <param name="FenceLanguage">Markdownのコードブロックに付ける言語名。</param>
/// <param name="InfoComment">Markdownでファイル見出しの下に入れる注記。不要ならnull。</param>
public sealed record ExportBlock(string DisplayPath, string Content, string FenceLanguage, string? InfoComment);

/// <summary>見出し・ツリー・ファイルごとのブロックに分けた出力文書です。</summary>
public sealed class ExportDocument(OutputFormat format, string title, string preamble, IReadOnlyList<ExportBlock> blocks)
{
    /// <summary>出力形式。</summary>
    public OutputFormat Format { get; } = format;

    /// <summary>1行目の見出し（改行なし）。</summary>
    public string Title { get; } = title;

    /// <summary>見出しのあとに続く部分（ディレクトリツリーなど）。</summary>
    public string Preamble { get; } = preamble;

    /// <summary>ファイルごとのブロック。</summary>
    public IReadOnlyList<ExportBlock> Blocks { get; } = blocks;

    /// <summary>1ファイル分のブロックを文字列にします。</summary>
    /// <param name="block">対象のブロック。</param>
    /// <param name="continuation">ファイルを途中で分けたときに見出しへ添える文言。分けていなければnull。</param>
    public string RenderBlock(ExportBlock block, string? continuation = null)
    {
        var path = continuation is null ? block.DisplayPath : $"{block.DisplayPath} ({continuation})";
        var builder = new StringBuilder();
        if (Format == OutputFormat.Markdown)
        {
            builder.Append("## File: ").AppendLine(path);
            builder.AppendLine();
            if (block.InfoComment is not null)
            {
                builder.AppendLine(block.InfoComment);
            }

            var fence = block.Content.Contains("```", StringComparison.Ordinal) ? "````" : "```";
            builder.Append(fence).AppendLine(block.FenceLanguage);
            builder.AppendLine(block.Content);
            builder.AppendLine(fence);
            builder.AppendLine();
        }
        else
        {
            builder.AppendLine().AppendLine(new string('=', 72));
            builder.Append("FILE: ").AppendLine(path);
            builder.AppendLine(new string('-', 72));
            builder.AppendLine(block.Content);
        }

        return builder.ToString();
    }

    /// <summary>すべてを1つの文字列につなげます。</summary>
    public string ToText()
    {
        var builder = new StringBuilder();
        builder.AppendLine(Title);
        builder.Append(Preamble);
        foreach (var block in Blocks)
        {
            builder.Append(RenderBlock(block));
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }
}
