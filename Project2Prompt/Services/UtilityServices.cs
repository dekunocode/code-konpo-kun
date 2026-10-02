using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using Project2Prompt.Models;

namespace Project2Prompt.Services;

/// <summary>文字数から概算トークン数を算出します。</summary>
public interface ITokenEstimator
{
    long Estimate(long characterCount);
}

public sealed class TokenEstimator : ITokenEstimator
{
    public long Estimate(long characterCount) => (long)Math.Ceiling(characterCount / 3.5d);
}

/// <summary>JSON設定をユーザー領域へ保存します。</summary>
public interface ISettingsService
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

public sealed class SettingsService : ISettingsService
{
    private readonly string settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Project2Prompt",
        "settings.json");

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(settingsPath))
        {
            return new AppSettings();
        }

        try
        {
            await using var stream = File.OpenRead(settingsPath);
            return await JsonSerializer.DeserializeAsync(stream, AppJsonContext.Default.AppSettings, cancellationToken).ConfigureAwait(false)
                ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(settingsPath)!;
        Directory.CreateDirectory(directory);
        await using var stream = new FileStream(settingsPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        await JsonSerializer.SerializeAsync(stream, settings, AppJsonContext.Default.AppSettings, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>OSクリップボードへの書き込みを抽象化します。</summary>
public interface IClipboardService
{
    Task SetTextAsync(string text);
}

public sealed class ClipboardService : IClipboardService
{
    public Task SetTextAsync(string text)
    {
        Clipboard.SetText(text);
        return Task.CompletedTask;
    }
}

/// <summary>フォルダ選択と保存ダイアログを抽象化します。</summary>
public interface IFileDialogService
{
    string? SelectFolder(string initialDirectory);
    string? SelectSavePath(string initialDirectory, OutputFormat format);
}

public sealed class FileDialogService : IFileDialogService
{
    public string? SelectFolder(string initialDirectory)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "プロジェクトフォルダを選択",
            InitialDirectory = Directory.Exists(initialDirectory) ? initialDirectory : string.Empty,
            Multiselect = false,
        };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string? SelectSavePath(string initialDirectory, OutputFormat format)
    {
        var isMarkdown = format == OutputFormat.Markdown;
        var dialog = new SaveFileDialog
        {
            Title = "梱包結果を保存",
            InitialDirectory = Directory.Exists(initialDirectory) ? initialDirectory : string.Empty,
            FileName = isMarkdown ? "project-context.md" : "project-context.txt",
            DefaultExt = isMarkdown ? ".md" : ".txt",
            Filter = isMarkdown ? "Markdown (*.md)|*.md|すべてのファイル (*.*)|*.*" : "テキスト (*.txt)|*.txt|すべてのファイル (*.*)|*.*",
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
