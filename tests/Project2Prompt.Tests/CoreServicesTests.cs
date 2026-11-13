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
        new JavaProjectProfile(),
        new GoProjectProfile(),
        new CppProjectProfile(),
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

    [Theory]
    [InlineData("pom.xml", ProjectKind.Java)]
    [InlineData("app/build.gradle.kts", ProjectKind.Java)]
    [InlineData("go.mod", ProjectKind.Go)]
    [InlineData("CMakeLists.txt", ProjectKind.Cpp)]
    [InlineData("Engine/Engine.vcxproj", ProjectKind.Cpp)]
    public void ProfileCatalog_DetectsJavaGoAndCpp(string marker, ProjectKind expected)
    {
        var catalog = CreateCatalog();

        Assert.Equal(expected, catalog.Detect([marker]));
    }

    [Fact]
    public void ProfileCatalog_IncludesOnlyDetectedLanguageSources()
    {
        var catalog = CreateCatalog();

        Assert.True(catalog.IsIncluded("src/main/java/App.java", ProjectKind.Java));
        Assert.True(catalog.IsIncluded("cmd/server/main.go", ProjectKind.Go));
        Assert.True(catalog.IsIncluded("src/engine.cpp", ProjectKind.Cpp));
        Assert.True(catalog.IsIncluded("include/engine.h", ProjectKind.Cpp));
        Assert.False(catalog.IsIncluded("go.sum", ProjectKind.Go));
        Assert.False(catalog.IsIncluded("main.go", ProjectKind.Java));
    }

    [Theory]
    [InlineData("App.java", "java")]
    [InlineData("build.gradle", "groovy")]
    [InlineData("main.go", "go")]
    [InlineData("util.c", "c")]
    [InlineData("util.h", "c")]
    [InlineData("engine.cpp", "cpp")]
    [InlineData("engine.hpp", "cpp")]
    [InlineData("CMakeLists.txt", "cmake")]
    [InlineData("sub/Makefile", "makefile")]
    [InlineData("Engine.vcxproj", "xml")]
    [InlineData("notes.txt", "")]
    public void ProfileCatalog_ReturnsFenceLanguage(string path, string expected)
    {
        Assert.Equal(expected, CreateCatalog().GetFenceLanguage(path));
    }

    [Theory]
    [InlineData("target/classes/App.class")]
    [InlineData(".gradle/8.0/file.lock")]
    [InlineData("vendor/github.com/pkg/errors/errors.go")]
    [InlineData("x64/Debug/Engine.obj")]
    [InlineData("Engine/Release/Engine.pdb")]
    [InlineData("cmake-build-debug/CMakeCache.txt")]
    public void FileFilterService_ExcludesJavaGoAndCppBuildOutputs(string path)
    {
        Assert.True(new FileFilterService().IsExcluded(path, new AppSettings()));
    }

    [Fact]
    public async Task MarkdownExporter_RemovesComments_ForJavaAndCpp()
    {
        const string content = "String url = \"https://example.com\"; // remove me\n/* block */\nchar c = '/';\nint x = 1;";
        var exporter = new MarkdownExporter(new TreeBuilder(), CreateCatalog(), new SecretScanner());

        foreach (var path in new[] { "App.java", "engine.cpp", "util.h" })
        {
            var result = await exporter.ExportAsync(
                ["Sample"],
                [CreateFile(path, content)],
                new AppSettings { RemoveComments = true },
                CancellationToken.None);

            Assert.Contains("https://example.com", result, StringComparison.Ordinal);
            Assert.Contains("char c = '/';", result, StringComparison.Ordinal);
            Assert.Contains("int x = 1;", result, StringComparison.Ordinal);
            Assert.DoesNotContain("remove me", result, StringComparison.Ordinal);
            Assert.DoesNotContain("block", result, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task MarkdownExporter_RemovesGoComments_AndKeepsRawStrings()
    {
        const string content = "var pattern = `// not a comment \"quoted`\nvar url = \"https://example.com\" // remove me\n/* block */\nvar x = 1";
        var exporter = new MarkdownExporter(new TreeBuilder(), CreateCatalog(), new SecretScanner());

        var result = await exporter.ExportAsync(
            ["Sample"],
            [CreateFile("main.go", content)],
            new AppSettings { RemoveComments = true },
            CancellationToken.None);

        Assert.Contains("```go", result, StringComparison.Ordinal);
        Assert.Contains("`// not a comment \"quoted`", result, StringComparison.Ordinal);
        Assert.Contains("https://example.com", result, StringComparison.Ordinal);
        Assert.Contains("var x = 1", result, StringComparison.Ordinal);
        Assert.DoesNotContain("remove me", result, StringComparison.Ordinal);
        Assert.DoesNotContain("block", result, StringComparison.Ordinal);
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

    [Theory]
    [InlineData(OutputFormat.Markdown)]
    [InlineData(OutputFormat.PlainText)]
    public async Task Exporter_ExportAsync_MatchesDocumentText(OutputFormat format)
    {
        var files = new[] { CreateFile("A.cs", "class A {}"), CreateFile("B.cs", "class B {}") };
        IProjectExporter exporter = format == OutputFormat.Markdown
            ? new MarkdownExporter(new TreeBuilder(), CreateCatalog(), new SecretScanner())
            : new TextExporter(new TreeBuilder(), new SecretScanner());

        var text = await exporter.ExportAsync(["Sample"], files, new AppSettings(), CancellationToken.None);
        var document = await exporter.BuildAsync(["Sample"], files, new AppSettings(), CancellationToken.None);

        Assert.Equal(text, document.ToText());
        Assert.Equal(2, document.Blocks.Count);
    }

    [Fact]
    public async Task OutputSplitter_ReturnsSinglePart_WhenWithinLimit()
    {
        var exporter = new MarkdownExporter(new TreeBuilder(), CreateCatalog(), new SecretScanner());
        var document = await exporter.BuildAsync(["Sample"], [CreateFile("A.cs", "class A {}")], new AppSettings(), CancellationToken.None);

        var parts = OutputSplitter.Split(document, 200 * 1024);

        Assert.Single(parts);
        Assert.Equal(document.ToText(), parts[0]);
    }

    [Theory]
    [InlineData(OutputFormat.Markdown)]
    [InlineData(OutputFormat.PlainText)]
    public async Task OutputSplitter_SplitsAtFileBoundaries_WithinLimit(OutputFormat format)
    {
        const int limit = 8 * 1024;
        var files = Enumerable.Range(0, 12)
            .Select(i => CreateFile($"src/File{i:D2}.cs", string.Join('\n', Enumerable.Range(0, 40).Select(n => $"// 日本語のコメント {i}-{n}"))))
            .ToArray();
        IProjectExporter exporter = format == OutputFormat.Markdown
            ? new MarkdownExporter(new TreeBuilder(), CreateCatalog(), new SecretScanner())
            : new TextExporter(new TreeBuilder(), new SecretScanner());
        var document = await exporter.BuildAsync(["Sample"], files, new AppSettings(), CancellationToken.None);

        var parts = OutputSplitter.Split(document, limit);

        Assert.True(parts.Count > 1);
        Assert.All(parts, part => Assert.True(System.Text.Encoding.UTF8.GetByteCount(part) <= limit));
        var fileHeader = format == OutputFormat.Markdown ? "## File: " : "FILE: ";
        var treeHeader = format == OutputFormat.Markdown ? "## Directory Tree" : "File00.cs";
        Assert.Contains(treeHeader, parts[0], StringComparison.Ordinal);
        for (var i = 0; i < parts.Count; i++)
        {
            Assert.Contains($"{i + 1}/{parts.Count}", parts[i], StringComparison.Ordinal);
        }

        // どのファイルも、ちょうど1回だけ、どこかのパートに入っている
        foreach (var file in files)
        {
            var header = $"{fileHeader}{file.RelativePath}";
            Assert.Equal(1, parts.Sum(p => CountOccurrences(p, header + System.Environment.NewLine)));
        }

        Assert.DoesNotContain("## Directory Tree", parts[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task OutputSplitter_SplitsLargeFileByLines_AndKeepsAllContent()
    {
        const int limit = 8 * 1024;
        var lines = Enumerable.Range(0, 2000).Select(n => $"var line{n} = \"値{n}\";").ToArray();
        var content = string.Join('\n', lines);
        var exporter = new MarkdownExporter(new TreeBuilder(), CreateCatalog(), new SecretScanner());
        var document = await exporter.BuildAsync(["Sample"], [CreateFile("Big.cs", content)], new AppSettings(), CancellationToken.None);

        var parts = OutputSplitter.Split(document, limit);

        Assert.True(parts.Count > 2);
        Assert.All(parts, part => Assert.True(System.Text.Encoding.UTF8.GetByteCount(part) <= limit));
        Assert.Contains("## File: Big.cs (part 1/", parts[0], StringComparison.Ordinal);
        var joined = string.Concat(parts);
        Assert.All(lines, line => Assert.Contains(line + "\n", joined.Replace("\r\n", "\n"), StringComparison.Ordinal));
    }

    [Fact]
    public void OutputSplitter_SplitContent_HandlesVeryLongLine()
    {
        var line = new string('あ', 3000);

        var chunks = OutputSplitter.SplitContent(line, 1024);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.True(System.Text.Encoding.UTF8.GetByteCount(chunk) <= 1024));
        Assert.Equal(line, string.Concat(chunks));
    }

    [Fact]
    public void OutputSplitter_GetPartPath_AddsPartNumbers()
    {
        var path = Path.Combine("out", "project-context.md");

        Assert.Equal(Path.Combine("out", "project-context_part01of03.md"), OutputSplitter.GetPartPath(path, 1, 3));
        Assert.Equal(Path.Combine("out", "project-context_part007of120.md"), OutputSplitter.GetPartPath(path, 7, 120));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
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