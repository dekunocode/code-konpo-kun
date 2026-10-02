namespace Project2Prompt.Models;

/// <summary>検出できるプロジェクト種別を表します。</summary>
[Flags]
public enum ProjectKind
{
    None = 0,
    CSharp = 1,
    VisualBasic = 2,
    Python = 4,
}
