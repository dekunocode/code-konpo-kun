using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Project2Prompt.Models;
using Project2Prompt.Services;

namespace Project2Prompt.ViewModels;

/// <summary>メイン画面の状態と操作を管理します。</summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IProjectScanner projectScanner;
    private readonly ITreeBuilder treeBuilder;
    private readonly IReadOnlyDictionary<OutputFormat, IProjectExporter> exporters;
    private readonly ITokenEstimator tokenEstimator;
    private readonly ISettingsService settingsService;
    private readonly IClipboardService clipboardService;
    private readonly IFileDialogService fileDialogService;
    private CancellationTokenSource? operationCancellation;
    private AppSettings settings = new();
    private int excludedFileCount;
    private bool initialized;
    private bool hasGeneratedOutput;

    public MainViewModel(
        IProjectScanner projectScanner,
        ITreeBuilder treeBuilder,
        IEnumerable<IProjectExporter> exporters,
        ITokenEstimator tokenEstimator,
        ISettingsService settingsService,
        IClipboardService clipboardService,
        IFileDialogService fileDialogService)
    {
        this.projectScanner = projectScanner;
        this.treeBuilder = treeBuilder;
        this.exporters = exporters.ToDictionary(e => e.Format);
        this.tokenEstimator = tokenEstimator;
        this.settingsService = settingsService;
        this.clipboardService = clipboardService;
        this.fileDialogService = fileDialogService;
        ProjectPaths.CollectionChanged += (_, _) => RefreshCommand.NotifyCanExecuteChanged();
    }

    /// <summary>読み込み対象のプロジェクトフォルダパス一覧。</summary>
    public ObservableCollection<string> ProjectPaths { get; } = [];

    /// <summary>スキャン済みファイル。</summary>
    public ObservableCollection<ProjectFile> Files { get; } = [];

    /// <summary>画面表示用のディレクトリツリー。</summary>
    public ObservableCollection<DirectoryNode> DirectoryRoots { get; } = [];

    /// <summary>出力形式の選択肢。</summary>
    public IReadOnlyList<SelectionOption<OutputFormat>> OutputFormatOptions { get; } =
    [
        new("Markdown", OutputFormat.Markdown),
        new("プレーンテキスト", OutputFormat.PlainText),
    ];

    /// <summary>秘密情報処理の選択肢。</summary>
    public IReadOnlyList<SelectionOption<SecretHandling>> SecretHandlingOptions { get; } =
    [
        new("除外", SecretHandling.Exclude),
        new("マスク", SecretHandling.Mask),
        new("そのまま", SecretHandling.Include),
    ];

    [ObservableProperty]
    private string? selectedProjectPath;

    [ObservableProperty]
    private string detectionResult = "未選択";

    [ObservableProperty]
    private string previewText = "プロジェクトフォルダを選択するか、ここへドロップしてください。";

    [ObservableProperty]
    private string statusMessage = "準備完了";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool isBusy;

    [ObservableProperty]
    private ProjectStatistics statistics = ProjectStatistics.Empty;

    [ObservableProperty]
    private bool isSettingsOpen;

    [ObservableProperty]
    private OutputFormat outputFormat = OutputFormat.Markdown;

    [ObservableProperty]
    private SecretHandling secretHandling = SecretHandling.Mask;

    [ObservableProperty]
    private bool includeDirectoryTree = true;

    [ObservableProperty]
    private bool includeFileInfo = true;

    [ObservableProperty]
    private bool removeComments;

    [ObservableProperty]
    private bool trimTrailingWhitespace = true;

    [ObservableProperty]
    private bool normalizeBlankLines;

    [ObservableProperty]
    private bool removeRegions;

    [ObservableProperty]
    private bool addLineNumbers;

    [ObservableProperty]
    private string excludePatternsText = string.Empty;

    /// <summary>保存設定を読み込みます。</summary>
    public async Task InitializeAsync()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        settings = await settingsService.LoadAsync();
        ApplySettingsToViewModel();

        ProjectPaths.Clear();
        foreach (var folder in settings.LastOpenedFolders.Where(Directory.Exists))
        {
            ProjectPaths.Add(folder);
        }

        if (ProjectPaths.Count > 0)
        {
            await LoadProjectsAsync();
            StatusMessage = "前回のフォルダを表示しています";
        }
        else
        {
            StatusMessage = "準備完了";
        }
    }

    /// <summary>ドロップされた単一フォルダまたはファイルを読み込みます。</summary>
    public async Task LoadDroppedPathAsync(string path)
    {
        await LoadDroppedPathsAsync([path]);
    }

    /// <summary>ドロップされた複数のフォルダまたはファイルを読み込みます。</summary>
    public async Task LoadDroppedPathsAsync(IEnumerable<string> paths)
    {
        var validFolders = paths
            .Select(p => Directory.Exists(p) ? p : Path.GetDirectoryName(p))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.GetFullPath(p!))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        bool addedAny = false;
        foreach (var folder in validFolders)
        {
            if (!ProjectPaths.Any(p => string.Equals(p, folder, StringComparison.OrdinalIgnoreCase)))
            {
                ProjectPaths.Add(folder);
                addedAny = true;
            }
        }

        if (addedAny)
        {
            await LoadProjectsAsync();
        }
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var initialDir = ProjectPaths.LastOrDefault() ?? string.Empty;
        var selected = fileDialogService.SelectFolder(initialDir);
        if (selected is not null && !ProjectPaths.Any(p => string.Equals(p, selected, StringComparison.OrdinalIgnoreCase)))
        {
            ProjectPaths.Add(selected);
            await LoadProjectsAsync();
        }
    }

    [RelayCommand]
    private async Task RemoveProjectAsync(string? path)
    {
        var target = path ?? SelectedProjectPath;
        if (string.IsNullOrWhiteSpace(target)) return;

        var existing = ProjectPaths.FirstOrDefault(p => string.Equals(p, target, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            ProjectPaths.Remove(existing);
            if (ProjectPaths.Count > 0)
            {
                await LoadProjectsAsync();
            }
            else
            {
                await ClearProjectsAsync();
            }
        }
    }

    [RelayCommand]
    private async Task ClearProjectsAsync()
    {
        ProjectPaths.Clear();
        foreach (var oldFile in Files)
        {
            oldFile.PropertyChanged -= OnFilePropertyChanged;
        }
        Files.Clear();
        DirectoryRoots.Clear();
        DetectionResult = "未選択";
        PreviewText = "プロジェクトフォルダを選択するか、ここへドロップしてください。";
        excludedFileCount = 0;
        UpdateStatistics();
        hasGeneratedOutput = false;
        NotifyCommandStates();
        StatusMessage = "プロジェクト一覧をクリアしました";

        SyncSettingsFromViewModel();
        settings.LastOpenedFolders.Clear();
        await settingsService.SaveAsync(settings);
    }

    /// <summary>今のフォルダを読み直して、編集後のファイル内容を反映します。</summary>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        var regenerate = hasGeneratedOutput;
        if (!await LoadProjectsAsync(keepSelection: true))
        {
            return;
        }

        if (regenerate && CanGenerate())
        {
            await GenerateAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync() => await RunOperationAsync(async token =>
    {
        SyncSettingsFromViewModel();
        var selected = GetEffectiveSelection();
        PreviewText = await exporters[OutputFormat].ExportAsync(ProjectPaths.ToList(), selected, settings, token);
        hasGeneratedOutput = true;
        NotifyCommandStates();
        UpdateStatistics();
        StatusMessage = $"{selected.Length:N0} ファイルを生成しました";
        await settingsService.SaveAsync(settings, token);
    });

    [RelayCommand(CanExecute = nameof(CanUseOutput))]
    private async Task CopyAsync()
    {
        try
        {
            await clipboardService.SetTextAsync(PreviewText);
            StatusMessage = "クリップボードへコピーしました";
        }
        catch (Exception ex)
        {
            StatusMessage = $"コピーできませんでした: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseOutput))]
    private async Task SaveAsync()
    {
        SyncSettingsFromViewModel();
        var path = fileDialogService.SelectSavePath(settings.OutputFolder, OutputFormat);
        if (path is null)
        {
            return;
        }

        await RunOperationAsync(async token =>
        {
            await File.WriteAllTextAsync(path, PreviewText, new UTF8Encoding(false), token);
            settings.OutputFolder = Path.GetDirectoryName(path) ?? string.Empty;
            await settingsService.SaveAsync(settings, token);
            StatusMessage = $"保存しました: {path}";
        });
    }

    [RelayCommand]
    private void ToggleSettings() => IsSettingsOpen = !IsSettingsOpen;

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var file in Files)
        {
            file.IsSelected = true;
        }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var file in Files)
        {
            file.IsSelected = false;
        }
    }

    [RelayCommand]
    private void Cancel() => operationCancellation?.Cancel();

    private Task<bool> LoadProjectsAsync(bool keepSelection = false) => RunOperationAsync(async token =>
    {
        SyncSettingsFromViewModel();
        settings.LastOpenedFolders = ProjectPaths.ToList();
        var progress = new Progress<string>(message => StatusMessage = message);
        var result = await projectScanner.ScanAsync(ProjectPaths.ToList(), settings, progress, token);

        // 再読み込みのときは、前の選択状態を引き継ぐ（新しく増えたファイルは選択された状態）
        var previousSelection = keepSelection
            ? Files
                .GroupBy(f => f.FullPath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().IsSelected, StringComparer.OrdinalIgnoreCase)
            : null;

        foreach (var oldFile in Files)
        {
            oldFile.PropertyChanged -= OnFilePropertyChanged;
        }

        Files.Clear();
        foreach (var file in result.Files)
        {
            if (previousSelection is not null && previousSelection.TryGetValue(file.FullPath, out var wasSelected))
            {
                file.IsSelected = wasSelected;
            }

            file.PropertyChanged += OnFilePropertyChanged;
            Files.Add(file);
        }

        excludedFileCount = result.ExcludedFileCount;
        DetectionResult = FormatProjectKind(result.ProjectKind);
        RebuildTree();
        UpdateStatistics();
        PreviewText = result.Warnings.Count == 0
            ? "［生成］を押すと、選択ファイルのプレビューを作成します。"
            : string.Join(Environment.NewLine, result.Warnings.Take(20).Prepend("読み込み警告:"));
        hasGeneratedOutput = false;
        NotifyCommandStates();
        StatusMessage = $"{(keepSelection ? "再読み込み完了" : "読み込み完了")} ({ProjectPaths.Count} プロジェクト): {Files.Count:N0} 対象 / {excludedFileCount:N0} 除外";
        await settingsService.SaveAsync(settings, token);
    });

    private async Task<bool> RunOperationAsync(Func<CancellationToken, Task> operation)
    {
        if (IsBusy)
        {
            return false;
        }

        operationCancellation = new CancellationTokenSource();
        IsBusy = true;
        try
        {
            await operation(operationCancellation.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "処理をキャンセルしました";
            return false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"エラー: {ex.Message}";
            return false;
        }
        finally
        {
            IsBusy = false;
            operationCancellation.Dispose();
            operationCancellation = null;
        }
    }

    private void OnFilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectFile.IsSelected))
        {
            foreach (var root in DirectoryRoots)
            {
                SyncTreeNodes(root);
            }
            UpdateStatistics();
            NotifyCommandStates();
        }
    }

    private void RebuildTree()
    {
        DirectoryRoots.Clear();
        if (ProjectPaths.Count == 1)
        {
            var rootName = Path.GetFileName(ProjectPaths[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            DirectoryRoots.Add(treeBuilder.Build(rootName, GetSelectableFiles()));
        }
        else if (ProjectPaths.Count > 1)
        {
            DirectoryRoots.Add(treeBuilder.Build("Workspace", GetSelectableFiles()));
        }
    }

    private static void SyncTreeNodes(DirectoryNode node)
    {
        if (!node.IsDirectory)
        {
            node.SyncFromFile();
        }
        else
        {
            foreach (var child in node.Children)
            {
                SyncTreeNodes(child);
            }
        }
    }

    private void UpdateStatistics()
    {
        var selected = GetEffectiveSelection();
        var characters = selected.Sum(f => (long)f.Content.Length);
        var largest = selected.MaxBy(f => f.Size)?.DisplayRelativePath ?? "-";
        var mostLines = selected.MaxBy(f => f.LineCount)?.DisplayRelativePath ?? "-";
        Statistics = new ProjectStatistics(
            selected.Length,
            excludedFileCount + Files.Count - selected.Length,
            selected.Sum(f => (long)f.LineCount),
            characters,
            tokenEstimator.Estimate(characters),
            Encoding.UTF8.GetByteCount(PreviewText),
            largest,
            mostLines);
    }

    private bool CanGenerate() => !IsBusy && GetEffectiveSelection().Length > 0;
    private bool CanUseOutput() => !IsBusy && hasGeneratedOutput;
    private bool CanRefresh() => !IsBusy && ProjectPaths.Count > 0;

    private void NotifyCommandStates()
    {
        GenerateCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
    }

    private ProjectFile[] GetSelectableFiles() => Files
        .Where(f => SecretHandling != SecretHandling.Exclude || !f.HasSecrets)
        .ToArray();

    private ProjectFile[] GetEffectiveSelection() => Files
        .Where(f => f.IsSelected && (SecretHandling != SecretHandling.Exclude || !f.HasSecrets))
        .ToArray();

    private void MarkOutputDirty()
    {
        if (Files.Count == 0)
        {
            return;
        }

        hasGeneratedOutput = false;
        StatusMessage = "出力設定が変更されました。再生成してください";
        NotifyCommandStates();
    }

    partial void OnOutputFormatChanged(OutputFormat value) => MarkOutputDirty();

    partial void OnSecretHandlingChanged(SecretHandling value)
    {
        MarkOutputDirty();
        RebuildTree();
        UpdateStatistics();
    }

    partial void OnIncludeDirectoryTreeChanged(bool value) => MarkOutputDirty();
    partial void OnIncludeFileInfoChanged(bool value) => MarkOutputDirty();
    partial void OnRemoveCommentsChanged(bool value) => MarkOutputDirty();
    partial void OnTrimTrailingWhitespaceChanged(bool value) => MarkOutputDirty();
    partial void OnNormalizeBlankLinesChanged(bool value) => MarkOutputDirty();
    partial void OnRemoveRegionsChanged(bool value) => MarkOutputDirty();
    partial void OnAddLineNumbersChanged(bool value) => MarkOutputDirty();

    private void ApplySettingsToViewModel()
    {
        OutputFormat = settings.OutputFormat;
        SecretHandling = settings.SecretHandling;
        IncludeDirectoryTree = settings.IncludeDirectoryTree;
        IncludeFileInfo = settings.IncludeFileInfo;
        RemoveComments = settings.RemoveComments;
        TrimTrailingWhitespace = settings.TrimTrailingWhitespace;
        NormalizeBlankLines = settings.NormalizeBlankLines;
        RemoveRegions = settings.RemoveRegions;
        AddLineNumbers = settings.AddLineNumbers;
        ExcludePatternsText = string.Join(Environment.NewLine, settings.AdditionalExcludes);
    }

    private void SyncSettingsFromViewModel()
    {
        settings.OutputFormat = OutputFormat;
        settings.SecretHandling = SecretHandling;
        settings.IncludeDirectoryTree = IncludeDirectoryTree;
        settings.IncludeFileInfo = IncludeFileInfo;
        settings.RemoveComments = RemoveComments;
        settings.TrimTrailingWhitespace = TrimTrailingWhitespace;
        settings.NormalizeBlankLines = NormalizeBlankLines;
        settings.RemoveRegions = RemoveRegions;
        settings.AddLineNumbers = AddLineNumbers;
        settings.AdditionalExcludes = ExcludePatternsText
            .Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private static string FormatProjectKind(ProjectKind kind)
    {
        if (kind == ProjectKind.None)
        {
            return "汎用 / 判定なし";
        }

        var names = new List<string>();
        if (kind.HasFlag(ProjectKind.CSharp)) names.Add("C#");
        if (kind.HasFlag(ProjectKind.VisualBasic)) names.Add("VB.NET");
        if (kind.HasFlag(ProjectKind.Python)) names.Add("Python");
        return names.Count > 1 ? $"混在 ({string.Join(" + ", names)})" : names[0];
    }
}

/// <summary>ComboBox向けの表示名付き値です。</summary>
public sealed record SelectionOption<T>(string Name, T Value);