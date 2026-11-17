using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcp.Tests;

/// <summary>
/// Guards the docs route's test selection. A pull request that changes only docs-tier files runs
/// every release gate but only the test classes listed under <c>consumers</c> in
/// <c>eng/docs-tier-test-consumers.json</c> (<c>eng/verify-release.ps1 -DocsTierConsumerTests</c>).
/// That is sound only while the list names every class a docs-tier file can break, so this guard
/// derives the candidates from the sources and fails on any that is not classified:
/// <list type="bullet">
///   <item>a test class whose file carries a docs-tier path token;</item>
///   <item>a test class whose file names a repository script that carries such a token or walks a
///         directory tree (directly, or through a script it names);</item>
///   <item>a test class whose file resolves the repository root and walks a tree, or lists the
///         tracked files;</item>
///   <item>a test class that inherits from any of the above;</item>
///   <item>a shared helper or fixture script under <c>tests/</c> that carries one of the signals.</item>
/// </list>
/// Each candidate must be a consumer (it runs on docs pull requests) or a reviewed non-consumer
/// with the reason its signal is not a read of a docs-tier file. When in doubt, list a consumer:
/// one extra class on the docs leg is cheap, a missing one is an undetected break.
/// <para>
/// What this cannot see: a path assembled at run time from pieces that are not tokens (for
/// example a name read from another file), a tree walk in a file that never names the repository
/// root, a docs-tier read made through reflection or by product code under <c>src/</c>, and
/// scripts invoked under a computed name. A break through one of those is caught by the next
/// code-bearing pull request or the weekly schedule, which run the whole suite.
/// </para>
/// </summary>
[TestClass]
public sealed class DocsTierTestConsumerContractTests
{
    private const string ListRelativePath = "eng/docs-tier-test-consumers.json";

    // Pinned copies of the router's docs-tier definition. The tokens below are derived from it by
    // hand: widening the docs tier must be a deliberate edit to the router, the tokens and this pin.
    private const string ExpectedDocumentationPattern =
        @"$documentationPattern = '^(.*\.md|ai_docs/.*\.json|audit-reports/_latest-promotion-scorecard\.json)$'";

    private const string ExpectedSourceRootMarkdownPattern =
        "$sourceRootMarkdownPattern = '^(src|analyzers|samples|tests)/'";

    // The same rule eng/verify-release.ps1 applies before it puts a name into the test filter.
    private static readonly Regex _plainClassName = new(
        @"\A[A-Za-z_][A-Za-z0-9_.]*\z",
        RegexOptions.CultureInvariant);

    // Every docs-tier file is Markdown, lives under ai_docs, or is the promotion scorecard, so a
    // reference to one carries its extension or root even when the path is built from segments
    // (Path.Combine(root, "docs", "setup.md")). The two directory tokens catch a listing of a
    // docs-only directory that names no file.
    private static readonly Regex _docsTierToken = new(
        @"\.md\b|ai_docs|_latest-promotion-scorecard|changelog\.d|[""'/\\]docs[""'/\\]",
        RegexOptions.CultureInvariant);

    private static readonly Regex _scriptTreeWalk = new(
        @"ls-files|AllDirectories|-Recurse\b|rglob|os\.walk",
        RegexOptions.CultureInvariant);

    private static readonly Regex _trackedFileListing = new("ls-files", RegexOptions.CultureInvariant);

    private static readonly Regex _repositoryRootIdiom = new(
        "repo(sitory)?_?root",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex _recursiveWalk = new(
        @"AllDirectories|-Recurse\b",
        RegexOptions.CultureInvariant);

    private static readonly string[] _scriptExtensions = [".ps1", ".psm1", ".sh", ".py", ".mjs", ".js", ".cjs", ".cmd", ".bat"];

    private static readonly string[] _buildFileExtensions = [".csproj", ".props", ".targets"];

    // Build output, dependency caches and other checkouts are not part of this repository's sources.
    private static readonly HashSet<string> _skippedDirectoryNames = new(StringComparer.Ordinal)
    {
        ".git", ".worktrees", "worktrees", "bin", "obj", "node_modules", "artifacts", "TestResults", ".vs", ".idea",
    };

    [TestMethod]
    public void DocsTierConsumerCandidates_AreExactlyTheClassifiedSet()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var list = ConsumerList.Load(Path.Combine(repoRoot, ListRelativePath));
        var candidates = FindCandidates(repoRoot);
        var problems = new List<string>(list.ShapeProblems);

        var classified = new HashSet<string>(list.Consumers.Keys, StringComparer.Ordinal);
        classified.UnionWith(list.NonConsumers.Keys);
        foreach (var (className, signals) in candidates.Classes)
        {
            if (!classified.Contains(className))
            {
                problems.Add(
                    $"UNCLASSIFIED test class {className}: {string.Join("; ", signals)}. Add it to \"consumers\" " +
                    $"in {ListRelativePath} (it then runs on docs pull requests), or to \"nonConsumers\" with the " +
                    "reason no docs-tier file reaches it.");
            }
        }

        foreach (var className in classified.Where(name => !candidates.Classes.ContainsKey(name)))
        {
            problems.Add(
                $"STALE entry {className}: no file declares it as a test class that carries a docs-tier signal. " +
                $"Remove it from {ListRelativePath}.");
        }

        foreach (var className in list.Consumers.Keys.Where(list.NonConsumers.ContainsKey))
        {
            problems.Add($"CONFLICT {className} is listed as both a consumer and a non-consumer.");
        }

        var testClasses = GetTestClasses();
        foreach (var className in list.Consumers.Keys)
        {
            if (!testClasses.TryGetValue(className, out var type))
            {
                problems.Add(
                    $"MISSING consumer {className}: the test assembly has no concrete test class of that name, so " +
                    "the docs leg would fail on it. Remove or rename the entry.");
            }
            else if (!HasCaseTheDocsLegRuns(type))
            {
                problems.Add(
                    $"EMPTY consumer {className}: every test case is in the Benchmark or Network category, which " +
                    "the docs leg excludes, so the class would run nothing there.");
            }
        }

        foreach (var (path, signals) in candidates.Helpers)
        {
            if (signals.Any(signal => signal.StartsWith(DocsTokenSignal, StringComparison.Ordinal)))
            {
                problems.Add(
                    $"HELPER {path} carries a docs-tier path token ({string.Join("; ", signals)}). A shared helper " +
                    "that reads a docs-tier file makes every class that uses it a consumer, which this guard " +
                    "cannot follow. Move the read into the test classes that need it.");
            }
            else if (!list.ReviewedHelperFiles.ContainsKey(path))
            {
                problems.Add(
                    $"UNREVIEWED helper {path}: {string.Join("; ", signals)}. Add it to \"reviewedHelperFiles\" in " +
                    $"{ListRelativePath} with the reason no docs-tier file reaches it.");
            }
        }

        foreach (var path in list.ReviewedHelperFiles.Keys.Where(path => !candidates.Helpers.ContainsKey(path)))
        {
            problems.Add($"STALE reviewed helper {path}: it no longer carries a signal. Remove it from {ListRelativePath}.");
        }

        Assert.IsEmpty(problems, string.Join(Environment.NewLine, problems));
    }

    [TestMethod]
    public void RouterDocsTierDefinition_MatchesThisGuard()
    {
        var router = File.ReadAllText(Path.Combine(
            TestFixtureFileSystem.FindRepositoryRoot(), "eng", "resolve-ci-topology.ps1"));

        StringAssert.Contains(router, ExpectedDocumentationPattern);
        StringAssert.Contains(router, ExpectedSourceRootMarkdownPattern);
    }

    private const string DocsTokenSignal = "docs-tier token";

    private static Candidates FindCandidates(string repoRoot)
    {
        var files = EnumerateSourceFiles(repoRoot).ToArray();
        var readerScriptNames = FindDocsReaderScriptNames(repoRoot, files);

        var classes = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var helpers = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var relativePath in files.Where(IsTestSideFile))
        {
            var text = File.ReadAllText(Path.Combine(repoRoot, relativePath));
            var signals = new SortedSet<string>(StringComparer.Ordinal);
            var token = _docsTierToken.Match(text);
            if (token.Success)
            {
                signals.Add($"{DocsTokenSignal} '{token.Value}' in {relativePath}");
            }

            foreach (var (name, pattern) in readerScriptNames)
            {
                if (pattern.IsMatch(text))
                {
                    signals.Add($"names docs-reading script '{name}' in {relativePath}");
                }
            }

            if (_trackedFileListing.IsMatch(text)
                || (_repositoryRootIdiom.IsMatch(text) && _recursiveWalk.IsMatch(text)))
            {
                signals.Add($"repository tree walk in {relativePath}");
            }

            if (signals.Count == 0)
            {
                continue;
            }

            var declaredTestClasses = relativePath.EndsWith(".cs", StringComparison.Ordinal)
                ? GetDeclaredTestClassNames(text)
                : [];
            if (declaredTestClasses.Count == 0)
            {
                helpers[relativePath] = signals;
                continue;
            }

            foreach (var className in declaredTestClasses)
            {
                if (!classes.TryGetValue(className, out var existing))
                {
                    classes[className] = existing = new SortedSet<string>(StringComparer.Ordinal);
                }

                existing.UnionWith(signals);
            }
        }

        // A class that inherits a candidate's test methods runs the same reads.
        var testClasses = GetTestClasses();
        foreach (var className in classes.Keys.ToArray())
        {
            if (!testClasses.TryGetValue(className, out var baseType))
            {
                continue;
            }

            foreach (var (derivedName, derivedType) in testClasses)
            {
                if (derivedType != baseType && baseType.IsAssignableFrom(derivedType))
                {
                    if (!classes.TryGetValue(derivedName, out var existing))
                    {
                        classes[derivedName] = existing = new SortedSet<string>(StringComparer.Ordinal);
                    }

                    existing.Add($"inherits candidate {className}");
                }
            }
        }

        return new Candidates(classes, helpers);
    }

    /// <summary>
    /// A script reads docs-tier files when its own text carries a docs-tier token or walks a tree,
    /// or when it names a script that does. Returned with the pattern that finds the script's name
    /// in another file: the bare file name, so <c>publish-nuget.ps1</c> does not match the
    /// <c>publish-nuget.yml</c> workflow.
    /// </summary>
    private static List<(string Name, Regex Pattern)> FindDocsReaderScriptNames(string repoRoot, string[] files)
    {
        var scripts = files
            .Where(path => !IsTestSideFile(path)
                && !path.StartsWith("samples/", StringComparison.Ordinal)
                && _scriptExtensions.Contains(Path.GetExtension(path), StringComparer.Ordinal))
            .Select(path => (Path: path, Text: File.ReadAllText(Path.Combine(repoRoot, path))))
            .ToArray();

        static Regex NamePattern(string path)
            => new(
                @"(?<![\w-])" + Regex.Escape(Path.GetFileNameWithoutExtension(path)) + @"(?![\w-])(?!\.ya?ml)",
                RegexOptions.CultureInvariant);

        var readers = new Dictionary<string, Regex>(StringComparer.Ordinal);
        foreach (var script in scripts)
        {
            if (_docsTierToken.IsMatch(script.Text) || _scriptTreeWalk.IsMatch(script.Text))
            {
                readers[script.Path] = NamePattern(script.Path);
            }
        }

        bool grew;
        do
        {
            grew = false;
            foreach (var script in scripts)
            {
                if (!readers.ContainsKey(script.Path)
                    && readers.Any(reader => reader.Key != script.Path && reader.Value.IsMatch(script.Text)))
                {
                    readers[script.Path] = NamePattern(script.Path);
                    grew = true;
                }
            }
        }
        while (grew);

        // Two scripts can share a file name; one pattern per name is enough.
        return readers
            .GroupBy(reader => Path.GetFileNameWithoutExtension(reader.Key), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => (group.Key, group.First().Value))
            .ToList();
    }

    private static bool IsTestSideFile(string relativePath)
    {
        if (!relativePath.StartsWith("tests/", StringComparison.Ordinal))
        {
            return false;
        }

        var extension = Path.GetExtension(relativePath);
        return extension == ".cs"
            || _scriptExtensions.Contains(extension, StringComparer.Ordinal)
            || _buildFileExtensions.Contains(extension, StringComparer.Ordinal);
    }

    private static IEnumerable<string> EnumerateSourceFiles(string repoRoot)
    {
        var pending = new Stack<string>();
        pending.Push(repoRoot);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                if (!_skippedDirectoryNames.Contains(Path.GetFileName(child)))
                {
                    pending.Push(child);
                }
            }

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                var extension = Path.GetExtension(file);
                if (extension == ".cs"
                    || _scriptExtensions.Contains(extension, StringComparer.Ordinal)
                    || _buildFileExtensions.Contains(extension, StringComparer.Ordinal))
                {
                    yield return Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                }
            }
        }
    }

    /// <summary>
    /// Parsed rather than matched by pattern: many test files embed C# source in string literals,
    /// and a class declared inside one of those is not a test class of this assembly.
    /// </summary>
    private static List<string> GetDeclaredTestClassNames(string source)
    {
        var names = new List<string>();
        foreach (var declaration in CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            var isTestClass = declaration.AttributeLists
                .SelectMany(list => list.Attributes)
                .Select(attribute => attribute.Name.ToString().Split('.')[^1])
                .Any(name => name is "TestClass" or "TestClassAttribute");
            if (!isTestClass)
            {
                continue;
            }

            var name = declaration.Identifier.ValueText;
            for (var parent = declaration.Parent; parent is not null; parent = parent.Parent)
            {
                name = parent switch
                {
                    TypeDeclarationSyntax type => type.Identifier.ValueText + "+" + name,
                    BaseNamespaceDeclarationSyntax @namespace => @namespace.Name + "." + name,
                    _ => name,
                };
            }

            names.Add(name);
        }

        return names;
    }

    private static Dictionary<string, Type> GetTestClasses()
        => typeof(DocsTierTestConsumerContractTests).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, FullName: not null }
                && type.GetCustomAttribute<TestClassAttribute>(inherit: true) is not null)
            .ToDictionary(type => type.FullName!, StringComparer.Ordinal);

    private static bool HasCaseTheDocsLegRuns(Type testClass)
    {
        var classCategories = testClass.GetCustomAttributes<TestCategoryBaseAttribute>(inherit: true)
            .SelectMany(attribute => attribute.TestCategories)
            .ToArray();
        return testClass.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.GetCustomAttribute<TestMethodAttribute>(inherit: true) is not null)
            .Any(method => !classCategories
                .Concat(method.GetCustomAttributes<TestCategoryBaseAttribute>(inherit: true)
                    .SelectMany(attribute => attribute.TestCategories))
                .Any(category => category is "Benchmark" or "Network"));
    }

    private sealed record Candidates(
        SortedDictionary<string, SortedSet<string>> Classes,
        SortedDictionary<string, SortedSet<string>> Helpers);

    private sealed record ConsumerList(
        Dictionary<string, string> Consumers,
        Dictionary<string, string> NonConsumers,
        Dictionary<string, string> ReviewedHelperFiles,
        List<string> ShapeProblems)
    {
        public static ConsumerList Load(string path)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var problems = new List<string>();
            var consumers = ReadEntries(document.RootElement, "consumers", "class", "reads", requirePlainClassName: true, problems);
            var nonConsumers = ReadEntries(document.RootElement, "nonConsumers", "class", "reason", requirePlainClassName: true, problems);
            var helpers = ReadEntries(document.RootElement, "reviewedHelperFiles", "path", "reason", requirePlainClassName: false, problems);
            if (consumers.Count == 0)
            {
                problems.Add($"{ListRelativePath} names no consumer; the docs leg must never run zero tests.");
            }

            return new ConsumerList(consumers, nonConsumers, helpers, problems);
        }

        private static Dictionary<string, string> ReadEntries(
            JsonElement root,
            string listName,
            string keyName,
            string reasonName,
            bool requirePlainClassName,
            List<string> problems)
        {
            var entries = new Dictionary<string, string>(StringComparer.Ordinal);
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty(listName, out var list)
                || list.ValueKind != JsonValueKind.Array)
            {
                problems.Add($"{ListRelativePath} must be an object with a \"{listName}\" array.");
                return entries;
            }

            string? previous = null;
            foreach (var entry in list.EnumerateArray())
            {
                var key = entry.ValueKind == JsonValueKind.Object
                    && entry.TryGetProperty(keyName, out var keyElement)
                    && keyElement.ValueKind == JsonValueKind.String
                        ? keyElement.GetString()
                        : null;
                var reason = entry.ValueKind == JsonValueKind.Object
                    && entry.TryGetProperty(reasonName, out var reasonElement)
                    && reasonElement.ValueKind == JsonValueKind.String
                        ? reasonElement.GetString()
                        : null;
                if (string.IsNullOrWhiteSpace(key) || (requirePlainClassName && !_plainClassName.IsMatch(key)))
                {
                    problems.Add($"\"{listName}\" has an entry without a usable \"{keyName}\".");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(reason))
                {
                    problems.Add($"\"{listName}\" entry {key} needs a non-empty \"{reasonName}\".");
                }

                if (!entries.TryAdd(key, reason ?? string.Empty))
                {
                    problems.Add($"\"{listName}\" lists {key} more than once.");
                }

                if (previous is not null && string.CompareOrdinal(previous, key) > 0)
                {
                    problems.Add($"\"{listName}\" must stay in ordinal order: {key} follows {previous}.");
                }

                previous = key;
            }

            return entries;
        }
    }
}
