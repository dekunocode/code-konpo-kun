using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Project2Prompt.Models;

/// <summary>
/// フォルダおよびファイルをツリー表示し、3値（トリステート）チェックボックスで選択状態を制御するノードです。
/// </summary>
public partial class DirectoryNode : ObservableObject
{
    private bool? isSelected = true;
    private bool isUpdatingSelection;

    /// <summary>表示名。</summary>
    public required string Name { get; init; }

    /// <summary>フォルダかどうか（false の場合はファイル）。</summary>
    public bool IsDirectory { get; init; } = true;

    /// <summary>ファイルノードの場合に対応する ProjectFile インスタンス。</summary>
    public ProjectFile? File { get; init; }

    /// <summary>親ノード。</summary>
    public DirectoryNode? Parent { get; set; }

    /// <summary>子ノード（フォルダおよびファイル）。</summary>
    public ObservableCollection<DirectoryNode> Children { get; } = [];

    /// <summary>
    /// 選択状態を表します。
    /// true: 全選択 / false: 未選択 / null: 一部選択（中間状態）
    /// </summary>
    public bool? IsSelected
    {
        get => isSelected;
        set
        {
            if (SetProperty(ref isSelected, value))
            {
                OnIsSelectedChanged(value);
            }
        }
    }

    private void OnIsSelectedChanged(bool? value)
    {
        if (isUpdatingSelection) return;

        isUpdatingSelection = true;
        try
        {
            // 1. 自身がファイルを持っている場合は ProjectFile の選択状態を更新
            if (value.HasValue)
            {
                if (File is not null)
                {
                    File.IsSelected = value.Value;
                }

                // 2. 自身がフォルダの場合は子ノードへ全選択/全解除を再帰伝播
                foreach (var child in Children)
                {
                    child.IsSelected = value;
                }
            }

            // 3. 親フォルダへ選択状態の再計算を依頼
            Parent?.RecalculateSelection();
        }
        finally
        {
            isUpdatingSelection = false;
        }
    }

    /// <summary>
    /// 配下要素の選択状況に基づいて自身の 3値（true / false / null）を再計算します。
    /// </summary>
    public void RecalculateSelection()
    {
        if (isUpdatingSelection) return;

        if (!IsDirectory && File is not null)
        {
            SetIsSelectedWithoutPropagation(File.IsSelected);
            Parent?.RecalculateSelection();
            return;
        }

        if (Children.Count == 0) return;

        bool allSelected = true;
        bool noneSelected = true;

        foreach (var child in Children)
        {
            if (child.IsSelected == true)
            {
                noneSelected = false;
            }
            else if (child.IsSelected == false)
            {
                allSelected = false;
            }
            else // null (一部選択)
            {
                allSelected = false;
                noneSelected = false;
            }
        }

        bool? newSelected = allSelected ? true : (noneSelected ? false : null);
        if (isSelected != newSelected)
        {
            SetIsSelectedWithoutPropagation(newSelected);
            Parent?.RecalculateSelection();
        }
    }

    /// <summary>
    /// DataGrid 側などで ProjectFile.IsSelected が変更された際にツリー側へ同期させます。
    /// </summary>
    public void SyncFromFile()
    {
        if (File is not null && isSelected != File.IsSelected)
        {
            SetIsSelectedWithoutPropagation(File.IsSelected);
            Parent?.RecalculateSelection();
        }
    }

    private void SetIsSelectedWithoutPropagation(bool? value)
    {
        isUpdatingSelection = true;
        SetProperty(ref isSelected, value, nameof(IsSelected));
        isUpdatingSelection = false;
    }
}