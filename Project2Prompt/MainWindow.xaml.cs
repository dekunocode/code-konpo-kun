using System.Windows;
using Project2Prompt.ViewModels;

namespace Project2Prompt;

/// <summary>メイン画面。UIイベントをViewModelへ転送します。</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;

    /// <summary>画面を初期化します。</summary>
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        FitToDesktopWorkArea();
        this.viewModel = viewModel;
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }

    /// <summary>高DPIのノートPCでもタスクバーを含む画面外へはみ出さないよう調整します。</summary>
    private void FitToDesktopWorkArea()
    {
        const double margin = 24;
        var workArea = SystemParameters.WorkArea;

        MaxWidth = workArea.Width;
        MaxHeight = workArea.Height;
        MinWidth = Math.Min(MinWidth, workArea.Width);
        MinHeight = Math.Min(MinHeight, workArea.Height);
        Width = Math.Max(MinWidth, Math.Min(Width, workArea.Width - margin));
        Height = Math.Max(MinHeight, Math.Min(Height, workArea.Height - margin));
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
        {
            await viewModel.LoadDroppedPathsAsync(paths);
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }
}