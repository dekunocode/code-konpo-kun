using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Project2Prompt.Profiles;
using Project2Prompt.Services;
using Project2Prompt.ViewModels;

namespace Project2Prompt;

/// <summary>アプリケーションの起動と依存関係を管理します。</summary>
public partial class App : Application
{
    private ServiceProvider? serviceProvider;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var services = new ServiceCollection();
        ConfigureServices(services);
        serviceProvider = services.BuildServiceProvider();
        serviceProvider.GetRequiredService<MainWindow>().Show();
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IProjectProfile, CSharpProjectProfile>();
        services.AddSingleton<IProjectProfile, VisualBasicProjectProfile>();
        services.AddSingleton<IProjectProfile, PythonProjectProfile>();
        services.AddSingleton<IProfileCatalog, ProfileCatalog>();
        services.AddSingleton<IFileFilterService, FileFilterService>();
        services.AddSingleton<ISecretScanner, SecretScanner>();
        services.AddSingleton<IProjectScanner, ProjectScanner>();
        services.AddSingleton<ITreeBuilder, TreeBuilder>();
        services.AddSingleton<ITokenEstimator, TokenEstimator>();
        services.AddSingleton<IProjectExporter, MarkdownExporter>();
        services.AddSingleton<IProjectExporter, TextExporter>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }
}
