namespace Project2Prompt.Models;

/// <summary>永続化するアプリ設定です。</summary>
public sealed class AppSettings
{
    public List<string> LastOpenedFolders { get; set; } = [];
    public string OutputFolder { get; set; } = string.Empty;
    public OutputFormat OutputFormat { get; set; } = OutputFormat.Markdown;
    public SecretHandling SecretHandling { get; set; } = SecretHandling.Mask;
    public bool IncludeDirectoryTree { get; set; } = true;
    public bool IncludeFileInfo { get; set; } = true;
    public bool RemoveComments { get; set; }
    public bool TrimTrailingWhitespace { get; set; } = true;
    public bool NormalizeBlankLines { get; set; }
    public bool RemoveRegions { get; set; }
    public bool AddLineNumbers { get; set; }

    /// <summary>保存するとき、推定トークン数の上限ごとにファイルを分けるかどうか。</summary>
    public bool SplitOutput { get; set; }

    /// <summary>分けるときの1ファイルの上限（推定トークン数）。</summary>
    public int SplitTokens { get; set; } = 100_000;
    public List<string> AdditionalExcludes { get; set; } = [];
}