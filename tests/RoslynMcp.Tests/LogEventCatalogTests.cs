using System.Reflection;
using System.Text.RegularExpressions;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Pins the structured-log event catalog: unique ids, stable (id, name) pairs, subsystem ranges, no
/// literal ids in production source, and equality with the published table in
/// <c>docs/stdio-client-integration.md</c>.
/// </summary>
[TestClass]
public sealed class LogEventCatalogTests
{
    private static readonly (int Id, string Name, string Subsystem)[] PinnedCatalog =
    [
        (1, "WorkspaceLoaded", "workspace"),
        (2, "ChangesApplied", "workspace"),
        (3, "ChangesApplyFailed", "workspace"),
        (4, "WorkspaceClosed", "workspace"),
        (5, "SessionNotFound", "workspace"),
        (6, "WorkspaceLoadCompleted", "workspace"),
        (1001, "UnexpectedFailure", "host"),
        (1002, "LogTransportDisconnected", "host"),
        (2101, "ToolCompleted", "tool"),
        (2102, "ToolCancelled", "tool"),
        (2103, "ToolInputRequired", "tool"),
        (2104, "ToolFailed", "tool"),
        (3001, "LogCommandExecuted", "process"),
        (3002, "LogProcessTreeKillFailed", "process"),
        (3003, "CommandProcessCompleted", "process"),
        (3004, "CommandGateCompleted", "process"),
        (4001, "LogProviderExecutionFailed", "provider"),
        (4002, "LogProviderEnumerationFailed", "provider"),
    ];

    // The literal-id shapes that bypass the catalog. ServerObservability.Write passes a non-literal
    // (diagnosticEvent.EventId) and is therefore not matched; test-only synthetic ids live under tests/.
    private static readonly Regex[] LiteralIdPatterns =
    [
        new(@"new\s+EventId\s*\(\s*\d", RegexOptions.CultureInvariant),
        new(@"EventId\s+\w+\s*=\s*new\s*\(\s*\d", RegexOptions.CultureInvariant),
        new(@"const\s+int\s+\w*EventId\w*\s*=\s*\d", RegexOptions.CultureInvariant),
    ];

    [TestMethod]
    public void Catalog_IdsAreUniqueAndPinnedWithinSubsystemRanges()
    {
        var actual = ReadCatalog();

        CollectionAssert.AreEquivalent(
            PinnedCatalog.Select(static e => (e.Id, e.Name)).ToArray(),
            actual.Select(static e => (e.Id, e.Name)).ToArray(),
            "The catalog must equal the pinned (id, name) pairs; a rename, removal, renumber or addition fails here.");
        Assert.AreEqual(actual.Count, actual.Select(static e => e.Id).Distinct().Count(), "Event ids must be unique.");
        Assert.AreEqual(actual.Count, actual.Select(static e => e.Name).Distinct(StringComparer.Ordinal).Count(), "Event names must be unique.");
        foreach (var pinned in PinnedCatalog)
        {
            Assert.AreEqual(pinned.Subsystem, LogEvents.SubsystemOf(pinned.Id), $"{pinned.Name} ({pinned.Id}) is outside its subsystem range.");
        }
    }

    [TestMethod]
    public void Catalog_UnambiguousPublishedIdsStayNumericallyStable()
    {
        Assert.AreEqual(1, LogEvents.WorkspaceLoaded);
        Assert.AreEqual(2, LogEvents.ChangesApplied);
        Assert.AreEqual(3, LogEvents.ChangesApplyFailed);
        Assert.AreEqual(4, LogEvents.WorkspaceClosed);
        Assert.AreEqual(5, LogEvents.SessionNotFound);
        Assert.AreEqual(1001, LogEvents.UnexpectedFailure);
        Assert.AreEqual(2101, LogEvents.ToolCompleted);
        Assert.AreEqual(2102, LogEvents.ToolCancelled);
        Assert.AreEqual(2103, LogEvents.ToolInputRequired);
        Assert.AreEqual(2104, LogEvents.ToolFailed);
    }

    [TestMethod]
    public void ProductionSource_NeverDefinesALiteralEventId()
    {
        var root = TestFixtureFileSystem.FindRepositoryRoot();
        var offenders = new List<string>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal))
            {
                continue;
            }

            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                if (LiteralIdPatterns.Any(pattern => pattern.IsMatch(lines[i])))
                {
                    offenders.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        Assert.AreEqual(0, offenders.Count, "Literal event ids bypass LogEvents:\n" + string.Join('\n', offenders));
    }

    [TestMethod]
    [DataRow("new EventId(1, nameof(X))")]
    [DataRow("private static readonly EventId ToolFailedEvent = new(2104, \"ToolFailed\");")]
    [DataRow("private const int UnexpectedFailureEventId = 1001;")]
    public void LiteralIdPatterns_DetectEveryLiteralShape(string line)
    {
        Assert.IsTrue(LiteralIdPatterns.Any(pattern => pattern.IsMatch(line)), line);
    }

    [TestMethod]
    [DataRow("new EventId(LogEvents.WorkspaceLoaded, LogEvents.WorkspaceLoadedName)")]
    [DataRow("new EventId(diagnosticEvent.EventId, diagnosticEvent.EventName),")]
    public void LiteralIdPatterns_AllowCatalogAndDynamicIds(string line)
    {
        Assert.IsFalse(LiteralIdPatterns.Any(pattern => pattern.IsMatch(line)), line);
    }

    [TestMethod]
    public void PublishedDocsTable_EqualsCatalog()
    {
        var root = TestFixtureFileSystem.FindRepositoryRoot();
        var lines = File.ReadAllLines(Path.Combine(root, "docs", "stdio-client-integration.md"));
        var header = Array.FindIndex(lines, static l => l.StartsWith("| eventId | eventName | subsystem |", StringComparison.Ordinal));
        Assert.IsTrue(header >= 0, "docs/stdio-client-integration.md must publish the '| eventId | eventName | subsystem |' table.");

        var documented = new List<(int Id, string Name, string Subsystem)>();
        for (var i = header + 2; i < lines.Length && lines[i].StartsWith('|'); i++)
        {
            var cells = lines[i].Trim('|').Split('|', StringSplitOptions.TrimEntries);
            Assert.AreEqual(3, cells.Length, $"Malformed catalog row: {lines[i]}");
            documented.Add((int.Parse(cells[0].Trim('`'), System.Globalization.CultureInfo.InvariantCulture),
                cells[1].Trim('`'), cells[2].Trim('`')));
        }

        var actual = ReadCatalog()
            .Select(static e => (e.Id, e.Name, Subsystem: LogEvents.SubsystemOf(e.Id) ?? "<none>"))
            .OrderBy(static e => e.Id).ToArray();
        CollectionAssert.AreEqual(actual, documented.OrderBy(static e => e.Id).ToArray(),
            "The published event-id table must equal LogEvents exactly.");
    }

    private static List<(int Id, string Name)> ReadCatalog()
    {
        var constants = typeof(LogEvents).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(static f => f.IsLiteral).ToArray();
        var result = new List<(int, string)>();
        foreach (var field in constants.Where(static f => f.FieldType == typeof(int)))
        {
            var nameField = constants.SingleOrDefault(f => f.Name == field.Name + "Name");
            Assert.IsNotNull(nameField, $"{field.Name} has no '{field.Name}Name' constant.");
            result.Add(((int)field.GetRawConstantValue()!, (string)nameField.GetRawConstantValue()!));
        }

        return result;
    }
}
