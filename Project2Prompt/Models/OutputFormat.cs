namespace Project2Prompt.Models;

/// <summary>梱包結果の形式を表します。</summary>
public enum OutputFormat
{
    Markdown,
    PlainText,
}

/// <summary>秘密情報を含むファイルの処理方法です。</summary>
public enum SecretHandling
{
    Exclude,
    Mask,
    Include,
}
