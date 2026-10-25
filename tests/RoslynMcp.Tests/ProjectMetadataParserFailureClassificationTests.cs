using System.Collections;
using System.Reflection;
using Microsoft.Extensions.Logging;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ProjectMetadataParserFailureClassificationTests
{
    private readonly List<string> _roots = [];

    [TestCleanup]
    public async Task CleanupAsync() => await CleanupFailureCollector.DeleteDirectoriesAsync(_roots);

    [TestMethod]
    [DataRow("frameworks", "null")]
    [DataRow("frameworks", "operation")]
    [DataRow("frameworks", "argument")]
    [DataRow("frameworks", "inner")]
    [DataRow("frameworks", "unsupported")]
    [DataRow("output", "null")]
    [DataRow("output", "operation")]
    [DataRow("output", "argument")]
    [DataRow("output", "inner")]
    [DataRow("output", "unsupported")]
    [DataRow("assets", "null")]
    [DataRow("assets", "operation")]
    [DataRow("assets", "argument")]
    [DataRow("assets", "inner")]
    [DataRow("assets", "unsupported")]
    public void Evaluation_UnexpectedFailure_PropagatesOriginalException(string boundary, string failure)
    {
        var projectPath = CreateProject("<Project />");
        Exception expected = failure switch
        {
            "null" => new NullReferenceException("unexpected evaluation fault"),
            "operation" => new InvalidOperationException("unexpected evaluation fault"),
            "inner" => new InvalidOperationException("unexpected evaluation fault", new FormatException()),
            "unsupported" => new NotSupportedException("unexpected evaluation fault"),
            _ => new ArgumentException("unexpected evaluation fault"),
        };
        var actual = Assert.Throws<Exception>(() => Evaluate(boundary, projectPath, new ThrowingProperties(expected), new RecordingLogger()));
        Assert.AreSame(expected, actual);
    }

    [TestMethod]
    [DataRow("<Project><PropertyGroup>")]
    [DataRow("<Project><Import Project=\"Missing.props\" /></Project>")]
    public void Evaluation_InvalidProject_RetainsFallbackAndDiagnostics(string xml)
    {
        var projectPath = CreateProject(xml);
        var logger = new RecordingLogger();
        Assert.AreEqual(0, ((IReadOnlyList<string>)Evaluate("frameworks", projectPath, MsBuildMetadataHelper.DefaultGlobalProperties, logger)!).Count);
        Assert.IsNull(Evaluate("output", projectPath, MsBuildMetadataHelper.DefaultGlobalProperties, logger));
        Assert.IsNull(Evaluate("assets", projectPath, MsBuildMetadataHelper.DefaultGlobalProperties, logger));
        Assert.AreEqual(2, logger.Entries.Count);
        StringAssert.Contains(logger.Entries[0].Message, "Failed to evaluate target frameworks");
        StringAssert.Contains(logger.Entries[1].Message, "Failed to evaluate output type");
        foreach (var entry in logger.Entries)
        {
            Assert.AreEqual(LogLevel.Debug, entry.Level);
            Assert.IsInstanceOfType<InvalidOperationException>(entry.Error);
            Assert.AreEqual("Microsoft.Build.Exceptions.InvalidProjectFileException", entry.Error?.InnerException?.GetType().FullName);
        }
    }

    [TestMethod]
    public void Evaluation_ValidProject_PreservesMetadataAndAssetsAuthority()
    {
        var projectPath = CreateProject("<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><ProjectAssetsFile>active.assets.json</ProjectAssetsFile></PropertyGroup></Project>");
        var logger = new RecordingLogger();
        CollectionAssert.AreEqual(new[] { "net10.0" }, ((IReadOnlyList<string>)Evaluate("frameworks", projectPath, MsBuildMetadataHelper.DefaultGlobalProperties, logger)!).ToArray());
        Assert.AreEqual("Exe", Evaluate("output", projectPath, MsBuildMetadataHelper.DefaultGlobalProperties, logger));
        Assert.IsNull(Evaluate("assets", projectPath, MsBuildMetadataHelper.DefaultGlobalProperties, logger));
        var assetsPath = Path.Combine(Path.GetDirectoryName(projectPath)!, "active.assets.json");
        File.WriteAllText(assetsPath, "{}");
        Assert.AreEqual(assetsPath, Evaluate("assets", projectPath, MsBuildMetadataHelper.DefaultGlobalProperties, logger));
        Assert.AreEqual(0, logger.Entries.Count);
    }

    [TestMethod]
    public void LoadProjectDocument_MalformedXml_ReturnsNullAndWarning()
    {
        var logger = new RecordingLogger();
        Assert.IsNull(ProjectMetadataParser.LoadProjectDocument(CreateProject("<Project>"), logger));
        Assert.AreEqual(1, logger.Entries.Count);
        Assert.AreEqual(LogLevel.Warning, logger.Entries[0].Level);
        StringAssert.Contains(logger.Entries[0].Message, "Failed to parse project file");
        Assert.IsInstanceOfType<System.Xml.XmlException>(logger.Entries[0].Error);
    }

    [TestMethod]
    public void LoadProjectDocument_ReadLockedFile_ReturnsNullAndWarning()
    {
        var projectPath = CreateProject("<Project />");
        using var stream = File.Open(projectPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var logger = new RecordingLogger();
        Assert.IsNull(ProjectMetadataParser.LoadProjectDocument(projectPath, logger));
        Assert.AreEqual(1, logger.Entries.Count);
        Assert.AreEqual(LogLevel.Warning, logger.Entries[0].Level);
        Assert.IsInstanceOfType<IOException>(logger.Entries[0].Error);
        StringAssert.Contains(logger.Entries[0].Message, "Failed to parse project file");
    }

    [TestMethod]
    public void FailurePolicies_ClassifyReadFailuresWithoutHidingProgrammingFailures()
    {
        foreach (var error in new Exception[] { new IOException(), new UnauthorizedAccessException(), new System.Xml.XmlException() })
        {
            Assert.IsTrue(ProjectMetadataParser.IsExpectedProjectReadFailure(error), error.GetType().Name);
        }
        foreach (var error in new Exception[] { new IOException(), new UnauthorizedAccessException() })
        {
            Assert.IsTrue(MsBuildMetadataHelper.IsExpectedEvaluationFailure(error), error.GetType().Name);
        }
        foreach (var error in new Exception[] { new NullReferenceException(), new InvalidOperationException(), new ArgumentException(), new NotSupportedException(), new OperationCanceledException() })
        {
            Assert.IsFalse(ProjectMetadataParser.IsExpectedProjectReadFailure(error), error.GetType().Name);
            Assert.IsFalse(MsBuildMetadataHelper.IsExpectedEvaluationFailure(error), error.GetType().Name);
        }
        Assert.IsFalse(MsBuildMetadataHelper.IsExpectedEvaluationFailure(new System.Xml.XmlException()));
    }

    [TestMethod]
    public void GetExistingAssetsPath_InvalidProjectAndEvaluatedPaths_ReturnUnknown()
    {
        Assert.IsNull(ProjectMetadataParser.GetExistingAssetsPath("invalid\0path", MsBuildMetadataHelper.DefaultGlobalProperties));
        var projectPath = CreateProject("<Project />");
        Assert.IsNull(ProjectMetadataParser.GetExistingAssetsPath(projectPath,
            new Dictionary<string, string> { ["ProjectAssetsFile"] = "invalid\0path" }));
    }

    private static object? Evaluate(string boundary, string projectPath, IReadOnlyDictionary<string, string> properties, ILogger logger)
    {
        if (boundary == "output") return ProjectMetadataParser.GetEvaluatedOutputType(projectPath, properties, logger);
        if (boundary == "assets") return ProjectMetadataParser.GetExistingAssetsPath(projectPath, properties);
        var method = typeof(ProjectMetadataParser).GetMethod("GetEvaluatedTargetFrameworks", BindingFlags.NonPublic | BindingFlags.Static)!;
        try
        {
            return method.Invoke(null, [projectPath, properties, logger]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private string CreateProject(string xml)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(ProjectMetadataParserFailureClassificationTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        _roots.Add(root);
        var projectPath = Path.Combine(root, "Probe.csproj");
        File.WriteAllText(projectPath, xml);
        return projectPath;
    }

    private sealed class ThrowingProperties(Exception error) : IReadOnlyDictionary<string, string>
    {
        public int Count => 1;
        public IEnumerable<string> Keys => throw error;
        public IEnumerable<string> Values => throw error;
        public string this[string key] => throw error;
        public bool ContainsKey(string key) => throw error;
        public bool TryGetValue(string key, out string value) => throw error;
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => throw error;
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, Exception? Error, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, exception, formatter(state, exception)));
    }
}
