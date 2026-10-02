using Project2Prompt.Models;
using Project2Prompt.Profiles;
using Project2Prompt.Services;
using Xunit;

namespace Project2Prompt.Tests;

public sealed class CoreServicesTests
{
    private static ProfileCatalog CreateCatalog() => new(
    [
        new CSharpProjectProfile(),
        new VisualBasicProjectProfile(),
        new PythonProjectProfile(),
    ]);

    [Fact]
    public void ProfileCatalog_DetectsMixedProject_AndFenceLanguages()
    {
        var catalog = CreateCatalog();

        var kind = catalog.Detect(["App/App.csproj", "tools/pyproject.toml"]);

        Assert.True(kind.HasFlag(ProjectKind.CSharp));
        Assert.True(kind.HasFlag(ProjectKind.Python));
        Assert.Equal("csharp", catalog.GetFenceLanguage("Program.cs"));
        Assert.Equal("python", catalog.GetFenceLanguage("script.py"));
    }

    [Fact]
    public void SecretScanner_MasksDetectedToken_AndIgnoresPlaceholder()
    {
        var scanner = new SecretScanner();
        const string token = "ghp_1234567890abcdefghijklmnopqrstuvwxyz";
        var content = $"token={token}\napi_key=your_example_key";

        var findings = scanner.Scan(content);
        var masked = scanner.Mask(content, findings);

        Assert.NotEmpty(findings);
        Assert.DoesNotContain(token, masked, StringComparison.Ordinal);
        Assert.Contains("***MASKED***", masked, StringComparison.Ordinal);
    }

    [Fact]
    public void TreeBuilder_CreatesStableDirectoryTree()
    {
        var files = new[]
        {
            CreateFile("Models/User.cs", "class User {}"),
            CreateFile("README.md", "readme"),
        };

        var text = new TreeBuilder().BuildText("Sample", files);

        Assert.Contains("Sample/", text, StringComparison.Ordinal);
        Assert.Contains("Models/", text, StringComparison.Ordinal);
        Assert.Contains("User.cs", text, StringComparison.Ordinal);
        Assert.Contains("README.md", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MarkdownExporter_UsesFenceAndMasksSecrets()
    {
        var scanner = new SecretScanner();
        const string content = "password=very-secret-password";
        var file = CreateFile("appsettings.json", content, scanner.Scan(content));
        var exporter = new MarkdownExporter(new TreeBuilder(), CreateCatalog(), scanner);

        var result = await exporter.ExportAsync(
            ["Sample"],
            [file],
            new AppSettings { SecretHandling = SecretHandling.Mask },
            CancellationToken.None);

        Assert.Contains("## File: appsettings.json", result, StringComparison.Ordinal);
        Assert.Contains("```json", result, StringComparison.Ordinal);
        Assert.Contains("***MASKED***", result, StringComparison.Ordinal);
        Assert.DoesNotContain("very-secret-password", result, StringComparison.Ordinal);
    }

    [Fact]
    public void TreeBuilder_WithMultipleProjects_CreatesProjectSubfolders()
    {
        var files = new[]
        {
            CreateFile("src/App.cs", "class App {}", projectName: "ProjectA"),
            CreateFile("src/main.py", "print('hello')", projectName: "ProjectB"),
        };

        var tree = new TreeBuilder().Build("Workspace", files);

        Assert.Equal("Workspace", tree.Name);
        Assert.Equal(2, tree.Children.Count);
        Assert.Contains(tree.Children, c => c.IsDirectory && c.Name == "ProjectA");
        Assert.Contains(tree.Children, c => c.IsDirectory && c.Name == "ProjectB");

        var text = new TreeBuilder().BuildText("Workspace", files);
        Assert.Contains("ProjectA/", text, StringComparison.Ordinal);
        Assert.Contains("ProjectB/", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MarkdownExporter_RemovesComments_WhenEnabled()
    {
        const string content = "var url = \"https://example.com\"; // remove me\n/* block */\nvar x = 1;";
        var exporter = new MarkdownExporter(new TreeBuilder(), CreateCatalog(), new SecretScanner());

        var result = await exporter.ExportAsync(
            ["Sample"],
            [CreateFile("Program.cs", content)],
            new AppSettings { RemoveComments = true },
            CancellationToken.None);

        Assert.Contains("https://example.com", result, StringComparison.Ordinal);
        Assert.DoesNotContain("remove me", result, StringComparison.Ordinal);
        Assert.DoesNotContain("block", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProjectScanner_ExcludesBuildArtifacts_AndDetectsCSharp()
    {
        var root = Path.Combine(Path.GetTempPath(), $"Project2PromptTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "src"));
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "Sample.csproj"), "<Project/>");
            await File.WriteAllTextAsync(Path.Combine(root, "src", "Program.cs"), "class Program { }");
            await File.WriteAllTextAsync(Path.Combine(root, "bin", "Generated.cs"), "class Generated { }");
            var scanner = new ProjectScanner(CreateCatalog(), new FileFilterService(), new SecretScanner());

            var result = await scanner.ScanAsync([root], new AppSettings(), null, CancellationToken.None);

            Assert.Equal(ProjectKind.CSharp, result.ProjectKind);
            Assert.Contains(result.Files, file => file.RelativePath == "Sample.csproj");
            Assert.Contains(result.Files, file => file.RelativePath == "src/Program.cs");
            Assert.DoesNotContain(result.Files, file => file.RelativePath.Contains("Generated.cs", StringComparison.Ordinal));
            Assert.True(result.ExcludedFileCount >= 1);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ProjectScanner_ScansMultipleProjects_AndCombinesKinds()
    {
        var root1 = Path.Combine(Path.GetTempPath(), $"Project2PromptTests-CS-{Guid.NewGuid():N}");
        var root2 = Path.Combine(Path.GetTempPath(), $"Project2PromptTests-PY-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root1);
        Directory.CreateDirectory(root2);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root1, "App.csproj"), "<Project/>");
            await File.WriteAllTextAsync(Path.Combine(root1, "Program.cs"), "class Program { }");

            await File.WriteAllTextAsync(Path.Combine(root2, "pyproject.toml"), "[tool.poetry]");
            await File.WriteAllTextAsync(Path.Combine(root2, "main.py"), "print('hello')");

            var scanner = new ProjectScanner(CreateCatalog(), new FileFilterService(), new SecretScanner());
            var result = await scanner.ScanAsync([root1, root2], new AppSettings(), null, CancellationToken.None);

            Assert.True(result.ProjectKind.HasFlag(ProjectKind.CSharp));
            Assert.True(result.ProjectKind.HasFlag(ProjectKind.Python));
            Assert.Equal(4, result.Files.Count);
            Assert.Contains(result.Files, f => f.ProjectName == Path.GetFileName(root1) && f.RelativePath == "Program.cs");
            Assert.Contains(result.Files, f => f.ProjectName == Path.GetFileName(root2) && f.RelativePath == "main.py");
        }
        finally
        {
            if (Directory.Exists(root1)) Directory.Delete(root1, true);
            if (Directory.Exists(root2)) Directory.Delete(root2, true);
        }
    }

    private static ProjectFile CreateFile(
        string relativePath,
        string content,
        IReadOnlyList<SecretFinding>? findings = null,
        string projectName = "Sample") => new()
        {
            ProjectName = projectName,
            FullPath = relativePath,
            RelativePath = relativePath,
            Content = content,
            Size = content.Length,
            LineCount = 1,
            SecretFindings = findings ?? [],
        };
}