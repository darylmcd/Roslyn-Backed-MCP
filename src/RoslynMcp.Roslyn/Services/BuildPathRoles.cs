using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Physical output roles and active declaration evidence from one native evaluation.</summary>
internal static partial class BuildPathRoles
{
    internal static readonly IReadOnlySet<string> OutputDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "MSBuildProjectExtensionsPath", "BaseIntermediateOutputPath", "IntermediateOutputPath",
        "OutputPath", "BaseOutputPath", "OutDir", "PublishDir", "PublishUrl", "ContentPreprocessorOutputDirectory"
    };
    internal static readonly IReadOnlySet<string> OutputFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "DocumentationFile", "GeneratedMSBuildEditorConfigFile", "ProjectAssetsFile", "TargetPath"
    };
    internal static readonly IReadOnlySet<string> NativeInputDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "MSBuildExtensionsPath", "MSBuildExtensionsPath32", "MSBuildExtensionsPath64", "MSBuildUserExtensionsPath"
    };
    internal static readonly IReadOnlySet<string> PathProperties = OutputDirectories.Concat(OutputFiles).Concat(NativeInputDirectories).Concat(new[]
    {
        "AssemblyOriginatorKeyFile", "CodeAnalysisRuleSet", "ProjectDir", "MSBuildProjectDirectory", "MSBuildProjectFullPath",
        "MSBuildThisFileDirectory", "MSBuildThisFileFullPath", "MSBuildStartupDirectory", "SolutionDir", "SolutionPath"
    }).ToHashSet(StringComparer.OrdinalIgnoreCase);

    internal sealed record Declaration(string FilePath, int Ordinal, string Name, string? Attribute, string Expression, string Expanded,
        ImmutableArray<string> ExistsOperands)
    {
        public bool Equals(Declaration? other) => other is not null
            && FilePath == other.FilePath && Ordinal == other.Ordinal && Name == other.Name
            && Attribute == other.Attribute && Expression == other.Expression && Expanded == other.Expanded
            && ExistsOperands.IsDefault == other.ExistsOperands.IsDefault
            && (ExistsOperands.IsDefault || ExistsOperands.AsSpan().SequenceEqual(other.ExistsOperands.AsSpan()));

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(FilePath, StringComparer.Ordinal);
            hash.Add(Ordinal);
            hash.Add(Name, StringComparer.Ordinal);
            hash.Add(Attribute, StringComparer.Ordinal);
            hash.Add(Expression, StringComparer.Ordinal);
            hash.Add(Expanded, StringComparer.Ordinal);
            hash.Add(ExistsOperands.IsDefault);
            if (!ExistsOperands.IsDefault)
                foreach (var operand in ExistsOperands) hash.Add(operand, StringComparer.Ordinal);
            return hash.ToHashCode();
        }

        public override string ToString() => "ActivePathDeclaration";
    }
    internal sealed record InputCoordinate(string Origin, string Directory, string Target)
    {
        public override string ToString() => "ActiveInputCoordinate";
    }
    internal sealed record ConditionProbe(string Origin, string Path, string QueryPath, bool? ObservedExists)
    {
        public override string ToString() => "ActiveConditionProbe";
    }
    internal sealed record Evidence(ImmutableArray<string> Directories, ImmutableArray<string> OutputFiles, ImmutableArray<string> OutputCoordinates, ImmutableArray<string> NativeInputDirectories,
        ImmutableArray<InputCoordinate> InputCoordinates, ImmutableArray<string> InputFiles, ImmutableArray<ConditionProbe> ConditionProbes, ImmutableArray<Declaration> Declarations, ImmutableArray<string> TraversalRoots)
    {
        internal bool EquivalentTo(Evidence other) =>
            Directories.ToHashSet(FileSystemPath.Comparer).SetEquals(other.Directories)
            && OutputFiles.ToHashSet(FileSystemPath.Comparer).SetEquals(other.OutputFiles)
            && OutputCoordinates.ToHashSet(FileSystemPath.Comparer).SetEquals(other.OutputCoordinates)
            && NativeInputDirectories.ToHashSet(FileSystemPath.Comparer).SetEquals(other.NativeInputDirectories)
            && InputCoordinates.ToHashSet().SetEquals(other.InputCoordinates)
            && InputFiles.ToHashSet(FileSystemPath.Comparer).SetEquals(other.InputFiles)
            && ConditionProbes.ToHashSet().SetEquals(other.ConditionProbes)
            && Declarations.ToHashSet().SetEquals(other.Declarations)
            && TraversalRoots.ToHashSet(FileSystemPath.Comparer).SetEquals(other.TraversalRoots);

        public override string ToString() => $"ActivePathEvidence {{ DirectoryCount = {Directories.Length}, DeclarationCount = {Declarations.Length} }}";
    }
    private sealed record Binding(string Value, string Escaped, string Expanded, bool IsGlobal, ImmutableArray<string> Prefixes);

    internal static Evidence Capture(Project project, NativeConditionEvidence nativeConditions)
    {
        var directory = Path.GetDirectoryName(project.FullPath)!;
        var directories = new HashSet<string>(FileSystemPath.Comparer);
        var outputCoordinates = new HashSet<string>(FileSystemPath.Comparer);
        var outputFiles = new HashSet<string>(FileSystemPath.Comparer);
        var nativeInputDirectories = new HashSet<string>(FileSystemPath.Comparer);
        var inputCoordinates = new HashSet<InputCoordinate>();
        var traversalRoots = nativeConditions.TraversalRoots.ToImmutableArray();
        var inputFiles = new HashSet<string>(FileSystemPath.Comparer);
        var conditionProbes = new HashSet<ConditionProbe>();
        var declarations = ImmutableArray.CreateBuilder<Declaration>();
        var declarationOrdinals = new Dictionary<ProjectRootElement, Dictionary<ProjectElement, int>>(ReferenceEqualityComparer.Instance);
        var bindings = project.Properties.Where(property => property.Xml is null).ToDictionary(property => property.Name,
            property => new Binding(property.EvaluatedValue, Project.GetPropertyValueEscaped(property), Project.GetPropertyValueEscaped(property), project.GlobalProperties.ContainsKey(property.Name), []),
            StringComparer.OrdinalIgnoreCase);
        var active = new Dictionary<ProjectPropertyElement, Queue<ProjectProperty>>(ReferenceEqualityComparer.Instance);
        foreach (var property in project.AllEvaluatedProperties.Where(property => property.Xml is not null))
        {
            if (!active.TryGetValue(property.Xml, out var queue)) active.Add(property.Xml, queue = new());
            queue.Enqueue(property);
        }
        var importsByElement = new Dictionary<ProjectImportElement, List<ResolvedImport>>(ReferenceEqualityComparer.Instance);
        var topImports = new Dictionary<ProjectRootElement, List<ProjectImportElement>>(ReferenceEqualityComparer.Instance);
        var bottomImports = new Dictionary<ProjectRootElement, List<ProjectImportElement>>(ReferenceEqualityComparer.Instance);
        foreach (var import in project.Imports.Where(import => import.ImportingElement is not null))
        {
            var element = import.ImportingElement;
            if (!importsByElement.TryGetValue(element, out var resolved))
            {
                importsByElement.Add(element, resolved = []);
                var implicitImports = element.ImplicitImportLocation switch
                {
                    ImplicitImportLocation.Top => topImports,
                    ImplicitImportLocation.Bottom => bottomImports,
                    _ => null
                };
                if (implicitImports is not null)
                {
                    if (!implicitImports.TryGetValue(element.ContainingProject, out var elements))
                        implicitImports.Add(element.ContainingProject, elements = []);
                    elements.Add(element);
                }
            }
            // Preserve native resolution order, including multiple resolutions of
            // one wildcard import. The index contains only this evaluation's objects.
            resolved.Add(import);
        }
        var visited = new HashSet<ProjectRootElement>(ReferenceEqualityComparer.Instance);
        var capturedConditions = new HashSet<(ProjectElement Element, bool PropertyPass)>();
        // This completed native evaluation's import inventory cannot change during
        // capture. Materialize its PRE-cache lookup only when an import predicate
        // needs it, and share that exact lookup across executed conditions.
        HashSet<string>? cachedImportPaths = null;
        Walk(project.Xml);
        if (active.Values.Any(queue => queue.Count != 0))
            throw InvalidOperationErrors.Internal("Active property origins were not present in the evaluated import tree.");

        foreach (var name in OutputDirectories)
        {
            AddDirectory(project.GetPropertyValue(name));
            AddConsumedOutputCoordinates(name, file: false);
        }
        foreach (var name in OutputFiles)
        {
            var value = project.GetPropertyValue(name);
            if (TryEvaluatedPath(value, out var path)) { AddDirectory(Path.GetDirectoryName(path)!); outputFiles.Add(path); }
            AddConsumedOutputCoordinates(name, file: true);
        }
        foreach (var name in new[] { "AssemblyOriginatorKeyFile", "CodeAnalysisRuleSet" })
            if (TryEvaluatedPath(project.GetPropertyValue(name), out var file)) inputFiles.Add(file);
        // Other predicate passes observe the completed property pass. Only native
        // executed predicates contribute; inactive descendants supply no authority.
        foreach (var root in visited)
            foreach (var element in root.AllChildren.Where(nativeConditions.WasEvaluated)) CaptureCondition(element, propertyPass: false);
        foreach (var root in visited)
            foreach (var item in root.AllChildren.OfType<ProjectItemElement>().Where(item => BuildPathRelocation.IsPathItem(item.ItemType)))
            {
                if (!nativeConditions.WasItemEvaluated(item)) continue;
                foreach (var attribute in new[] { ("Include", item.Include), ("Update", item.Update), ("Remove", item.Remove), ("Exclude", item.Exclude) })
                {
                    if (string.IsNullOrWhiteSpace(attribute.Item2)) continue;
                    var (expanded, prefixes) = Expand(attribute.Item2, root.FullPath, item.ItemType.Equals("Reference", StringComparison.OrdinalIgnoreCase) ? static path => path : NativePathSeparators);
                    Add(item, attribute.Item1, attribute.Item2, expanded, []);
                    foreach (var token in expanded.Split(';'))
                    {
                        var decoded = ProjectCollection.Unescape(token);
                        if (!item.ItemType.Equals("Reference", StringComparison.OrdinalIgnoreCase))
                            decoded = NativePathSeparators(decoded);
                        if (!Path.IsPathFullyQualified(decoded)) continue;
                        // Escaped metacharacters denote literal filename text. Keep
                        // their relocation anchor without inventing a directory query.
                        // Property-produced unescaped wildcards remain native globs.
                        var variable = token.IndexOfAny(['*', '?']);
                        var literal = variable < 0 ? decoded : ProjectCollection.Unescape(token[..variable]);
                        if (!item.ItemType.Equals("Reference", StringComparison.OrdinalIgnoreCase))
                            literal = NativePathSeparators(literal);
                        var coordinate = Path.EndsInDirectorySeparator(literal) ? Path.TrimEndingDirectorySeparator(literal) : Path.GetDirectoryName(literal);
                        if (coordinate is null || token.Contains("$(", StringComparison.Ordinal)) continue;
                        coordinate = CanonicalCoordinate(coordinate);
                        inputCoordinates.Add(new(root.FullPath, coordinate, coordinate));
                        if (variable >= 0) conditionProbes.Add(new(root.FullPath, coordinate, coordinate, null));
                    }
                }
            }
        foreach (var item in project.Items.Where(item => BuildPathRelocation.IsPathItem(item.ItemType)))
        {
            var xml = item.Xml;
            var isReference = item.ItemType.Equals("Reference", StringComparison.OrdinalIgnoreCase);
            var includePath = isReference ? item.EvaluatedInclude : item.GetMetadataValue("FullPath");
            Func<string, string> interpret = isReference ? static path => path : NativePathSeparators;
            if (!isReference && TryEvaluatedPath(includePath, out var itemPath))
                inputFiles.Add(itemPath);
            if (!string.IsNullOrWhiteSpace(xml.Include))
            {
                var (expanded, prefixes) = Expand(xml.Include, xml.ContainingProject.FullPath, interpret);
                if (TryEvaluatedPath(includePath, out var target))
                {
                    AddInputCoordinates(expanded, prefixes, target, xml.ContainingProject.FullPath, interpret);
                    CaptureNativeInputDirectories(xml.Include, xml.ContainingProject.FullPath, target, interpret);
                }
                Add(xml, "Include", xml.Include, expanded, []);
            }
            if (item.ItemType.Equals("Reference", StringComparison.OrdinalIgnoreCase) && item.GetMetadata("HintPath")?.Xml is { } hint)
            {
                var (expanded, prefixes) = Expand(hint.Value, hint.ContainingProject.FullPath, static path => path);
                if (TryEvaluatedPath(item.GetMetadataValue("HintPath"), out var target)) AddInputCoordinates(expanded, prefixes, target, xml.ContainingProject.FullPath, static path => path);
                Add(hint, null, hint.Value, expanded, []);
            }
        }
        return new(directories.ToImmutableArray(), outputFiles.ToImmutableArray(), outputCoordinates.ToImmutableArray(), nativeInputDirectories.ToImmutableArray(), inputCoordinates.ToImmutableArray(), inputFiles.ToImmutableArray(), conditionProbes.ToImmutableArray(), declarations.Distinct().ToImmutableArray(), traversalRoots);

        void Walk(ProjectRootElement root)
        {
            if (!visited.Add(root)) return;
            foreach (var element in topImports.GetValueOrDefault(root) ?? [])
                VisitImport(element);
            foreach (var element in root.AllChildren)
            {
                if (nativeConditions.WasPropertyConditionEvaluated(element))
                    CaptureCondition(element, propertyPass: true);
                if (element is ProjectPropertyElement propertyElement && active.TryGetValue(propertyElement, out var queue) && queue.TryDequeue(out var property))
                {
                    var (expanded, prefixes) = Expand(propertyElement.Value, root.FullPath, static path => path);
                    if (expanded.Contains("$([", StringComparison.Ordinal)) expanded = Project.GetPropertyValueEscaped(property);
                    if (PathProperties.Contains(property.Name)) Add(propertyElement, null, propertyElement.Value, expanded, []);
                    bindings[property.Name] = new(property.EvaluatedValue, Project.GetPropertyValueEscaped(property), expanded, project.GlobalProperties.ContainsKey(property.Name), prefixes.ToImmutableArray());
                }
                else if (element is ProjectImportElement import) VisitImport(import);
            }
            foreach (var element in bottomImports.GetValueOrDefault(root) ?? [])
                VisitImport(element);
        }
        void VisitImport(ProjectImportElement xml)
        {
            if (!importsByElement.TryGetValue(xml, out var resolved)) return;
            CaptureCondition(xml, propertyPass: true);
            var (expanded, prefixes) = Expand(xml.Project, xml.ContainingProject.FullPath, static path => path);
            if (expanded.Contains("$([", StringComparison.Ordinal))
            {
                if (resolved.Count != 1)
                    throw new NativeConditionProtocolException(NativeConditionProtocolFailure.Operand);
                expanded = ProjectCollection.Escape(resolved[0].ImportedProject.FullPath);
            }
            foreach (var import in resolved)
            {
                AddInputCoordinates(expanded, prefixes, import.ImportedProject.FullPath, xml.ContainingProject.FullPath, static path => path);
                CaptureNativeInputDirectories(xml.Project, xml.ContainingProject.FullPath, import.ImportedProject.FullPath, static path => path);
            }
            Add(xml, "Project", xml.Project, expanded, []);
            foreach (var import in resolved) Walk(import.ImportedProject);
        }
        void AddConsumedOutputCoordinates(string name, bool file)
        {
            if (!bindings.TryGetValue(name, out var binding) || !TryEvaluatedPath(project.GetPropertyValue(name), out var target)) return;
            if (StaticPrefix(binding.Expanded, static path => path) is { } coordinate && Contains(coordinate, target)) outputCoordinates.Add(CanonicalCoordinate(coordinate));
            foreach (var prefix in binding.Prefixes)
            {
                var canonicalPrefix = CanonicalCoordinate(prefix);
                var coordinatePrefix = file && FileSystemPath.Comparer.Equals(canonicalPrefix, target) ? Path.GetDirectoryName(canonicalPrefix)! : canonicalPrefix;
                if (Contains(coordinatePrefix, target)) outputCoordinates.Add(CanonicalCoordinate(coordinatePrefix));
            }
        }
        void CaptureCondition(ProjectElement xml, bool propertyPass)
        {
            var condition = nativeConditions.OriginalCondition(xml);
            if (string.IsNullOrWhiteSpace(condition) || !nativeConditions.WasEvaluated(xml) || !capturedConditions.Add((xml, propertyPass))) return;
            foreach (var execution in nativeConditions.Executions(xml)
                         .Where(execution => (execution.Pass == Microsoft.Build.Framework.Profiler.EvaluationPass.Properties) == propertyPass))
            {
                // An expansion query computes an argument; it is not the path
                // queried by the original Exists call. Never substitute it for
                // an empty argument, a cache hit, or an ordinary filesystem query.
                if (execution.Inputs.Any(input => input.ArgumentExpansion)
                    || execution.Invocations.Any(invocation => invocation.Inputs.Any(input => input.ArgumentExpansion)))
                    throw new NativeConditionProtocolException(NativeConditionProtocolFailure.OperandOrigin);
                var nativeInputs = execution.Inputs;
                var capturedOperands = ImmutableArray.CreateBuilder<string>();
                var operandIndex = 0;
                var invocationOrdinal = 0;
                var importCondition = xml is ProjectImportElement or ProjectImportGroupElement;
                var evaluationDirectory = xml is ProjectPropertyGroupElement or ProjectImportElement or ProjectImportGroupElement or ProjectUsingTaskElement
                    ? Path.GetDirectoryName(xml.ContainingProject.FullPath)! : directory;
                var expanded = BuildPathRelocation.RewriteExistsOperands(condition, operand =>
                {
                    var ordinal = invocationOrdinal++;
                    var invocation = execution.Invocations.SingleOrDefault(candidate => candidate.Ordinal == ordinal);
                    if (invocation is null) return operand; // Native short-circuit did not execute this exact call.
                    var inputs = invocation.Inputs;
                    var inputIndex = 0;
                    // Native expansion first fixes the operand's separators, then
                    // constructs escaped task items. The filesystem query also fixes
                    // separators after unescape; the import cache sees the item first.
                    if (Path.DirectorySeparatorChar != '\\') operand = operand.Replace('\\', '/');
                    var (expression, prefixes) = Expand(operand, xml.ContainingProject.FullPath, NativePathSeparators);
                    var actual = Reference().Replace(operand, match =>
                    {
                        var name = match.Groups[1].Value;
                        return ContextualProperty(name, xml.ContainingProject.FullPath)
                            ?? (bindings.TryGetValue(name, out var binding) ? binding.Escaped : string.Empty);
                    });
                    string? nativeArgument = null;
                    if (operand.Contains("$([", StringComparison.Ordinal))
                    {
                        if (invocation.Arguments.Count == 1)
                            nativeArgument = invocation.Arguments[0];
                        else if (invocation.Arguments.Count == 0 && !importCondition && inputs.Count == 1)
                            nativeArgument = inputs[0].Path;
                        else
                        {
                            if (invocation.Arguments.Count == 0 && importCondition)
                                throw new NativeConditionProtocolException(NativeConditionProtocolFailure.CachedOperand);
                            throw new NativeConditionProtocolException(NativeConditionProtocolFailure.OperandOrigin);
                        }
                        if (!IsScalarStringFunction(operand))
                            throw new NativeConditionProtocolException(NativeConditionProtocolFailure.Operand);
                        expression = ProjectCollection.Escape(nativeArgument);
                        actual = expression;
                    }
                    if (actual.Contains("@(", StringComparison.Ordinal) || actual.Contains("%(", StringComparison.Ordinal)
                        || actual.Contains("$(", StringComparison.Ordinal))
                        throw new NativeConditionProtocolException(NativeConditionProtocolFailure.Operand);
                    var members = actual.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var targets = new List<(string PhysicalPath, string QueryPath, string CacheQueryPath, bool Cached)>();
                    foreach (var member in members)
                    {
                        // Captured native arguments have already passed task-item
                        // decoding and base resolution. Never interpret them twice.
                        var decoded = nativeArgument ?? new Microsoft.Build.Utilities.TaskItem(member).ItemSpec;
                        var cacheQuery = nativeArgument ?? (Path.IsPathRooted(decoded) ? decoded : Path.GetFullPath(decoded, evaluationDirectory));
                        var query = NativePathSeparators(cacheQuery);
                        // Exists consumes one exact path, not a glob. The raw
                        // expression was checked before decoding; escaped syntax
                        // and wildcard characters are now literal query text.
                        if (!TryExactPhysicalAt(query, evaluationDirectory, out var target))
                            throw new NativeConditionProtocolException(NativeConditionProtocolFailure.Operand);
                        // Physical inventory uses a canonical identity. Native Exists
                        // still distinguishes a file from that same file with a
                        // directory separator suffix, so retain its query spelling.
                        targets.Add((target, query, cacheQuery, false));
                    }
                    if (targets.Count == 0)
                    {
                        if (inputs.Count != 0 || invocation.Result)
                            throw new NativeConditionProtocolException(NativeConditionProtocolFailure.OperandOrigin);
                        return operand;
                    }
                    var observedResult = true;
                    var observations = new bool?[targets.Count];
                    for (var targetIndex = 0; targetIndex < targets.Count; targetIndex++)
                    {
                        var target = targets[targetIndex];
                        // A real query takes precedence over the post-evaluation cache.
                        // Only an executed import call can have native PRE-cache evidence.
                        if (inputIndex < inputs.Count && SameQuery(inputs[inputIndex].Path, target.QueryPath))
                        {
                            observations[targetIndex] = inputs[inputIndex++].Exists;
                            if (observations[targetIndex] == false) { observedResult = false; break; }
                            continue;
                        }
                        // Native PRE cache keys are ordinal-ignore-case on every OS.
                        // Retain the stored object's spelling for physical authority.
                        if (importCondition && (cachedImportPaths ??= project.Imports.Select(import => import.ImportedProject.FullPath)
                                .Append(project.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase)).TryGetValue(target.CacheQueryPath, out var cachedPath))
                        {
                            // Native cache success bypasses the filesystem query entirely.
                            // Bind the actual loaded project coordinate, not its normalized alternative.
                            targets[targetIndex] = (Path.TrimEndingDirectorySeparator(Path.GetFullPath(cachedPath)),
                                cachedPath, target.CacheQueryPath, true);
                            observations[targetIndex] = true;
                            continue;
                        }
                        throw new NativeConditionProtocolException(NativeConditionProtocolFailure.OperandOrigin);
                    }
                    if (inputIndex != inputs.Count || observedResult != invocation.Result)
                        throw new NativeConditionProtocolException(NativeConditionProtocolFailure.OperandOrigin);
                    operandIndex += inputIndex;
                    // Freeze the COMPLETE expanded list, including members after the
                    // first missing entry: a private tree may change that first result.
                    for (var targetIndex = 0; targetIndex < targets.Count; targetIndex++)
                    {
                        var target = targets[targetIndex];
                        Func<string, string> interpret = target.Cached ? static path => path : NativePathSeparators;
                        CaptureNativeInputDirectories(operand, xml.ContainingProject.FullPath, target.PhysicalPath, interpret);
                        conditionProbes.Add(new(xml.ContainingProject.FullPath, target.PhysicalPath, target.QueryPath, observations[targetIndex]));
                        AddInputCoordinates(expression, prefixes, target.PhysicalPath, xml.ContainingProject.FullPath, interpret);
                    }
                    capturedOperands.Add(expression);
                    return expression;
                });
                if (operandIndex != nativeInputs.Count)
                    throw new NativeConditionProtocolException(NativeConditionProtocolFailure.OperandOrigin);
                Add(xml, "Condition", condition, expanded, capturedOperands.ToImmutable());
            }
        }

        static string NativePathSeparators(string path) => OperatingSystem.IsWindows() ? path : path.Replace('\\', '/');

        static bool SameQuery(string observed, string expected) =>
            Path.EndsInDirectorySeparator(observed) == Path.EndsInDirectorySeparator(expected)
            && FileSystemPath.Comparer.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(observed)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(expected)));

        void AddInputCoordinates(string expanded, IEnumerable<string> prefixes, string target, string origin, Func<string, string> interpret)
        {
            foreach (var prefix in prefixes.Concat(StaticPrefix(expanded, interpret) is { } coordinate ? [coordinate] : Array.Empty<string>()))
                if (Contains(prefix, target)) inputCoordinates.Add(new(origin, CanonicalCoordinate(prefix), CanonicalCoordinate(target)));
        }
        void CaptureNativeInputDirectories(string expression, string origin, string target, Func<string, string> interpret)
        {
            foreach (Match reference in Reference().Matches(expression))
            {
                var name = reference.Groups[1].Value;
                if (NativeInputDirectories.Contains(name) && bindings.TryGetValue(name, out var binding)
                    && TryExactPhysicalAt(interpret(binding.Value), Path.GetDirectoryName(origin)!, out var root) && Contains(root, target))
                    nativeInputDirectories.Add(root);
            }
        }
        static string CanonicalCoordinate(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

        static bool Contains(string root, string path)
        {
            root = CanonicalCoordinate(root);
            path = CanonicalCoordinate(path);
            return FileSystemPath.Comparer.Equals(root, path) || FileSystemPath.IsStrictDescendant(root, path);
        }

        void Add(Microsoft.Build.Construction.ProjectElement xml, string? attribute, string expression, string expanded, ImmutableArray<string> existsOperands)
        {
            var definition = xml.ContainingProject;
            if (!declarationOrdinals.TryGetValue(definition, out var ordinals))
            {
                ordinals = new Dictionary<ProjectElement, int>(ReferenceEqualityComparer.Instance);
                var position = 0;
                foreach (var element in definition.AllChildren) ordinals.Add(element, ++position);
                declarationOrdinals.Add(definition, ordinals);
            }
            if (!ordinals.TryGetValue(xml, out var ordinal)) return; // Implicit SDK imports have no authored element.
            declarations.Add(new(definition.FullPath, ordinal, xml.ElementName, attribute, expression, expanded, existsOperands));
        }
        (string Expanded, HashSet<string> Prefixes) Expand(string expression, string origin, Func<string, string> interpret)
        {
            var prefixes = new HashSet<string>(FileSystemPath.Comparer);
            var expanded = Reference().Replace(expression, match =>
            {
                var name = match.Groups[1].Value;
                if (ContextualProperty(name, origin) is { } contextual) return contextual;
                if (!bindings.TryGetValue(name, out var binding)) return string.Empty;
                var coordinate = interpret(binding.Value);
                if (Path.IsPathFullyQualified(coordinate))
                {
                    prefixes.Add(coordinate);
                    prefixes.UnionWith(binding.Prefixes.Select(interpret));
                    return binding.Expanded;
                }
                return binding.IsGlobal ? match.Value : binding.Expanded;
            });
            return (expanded, prefixes);
        }

        string? StaticPrefix(string expression, Func<string, string> interpret)
        {
            var decoded = interpret(ProjectCollection.Unescape(expression));
            var variable = expression.IndexOf("$(", StringComparison.Ordinal);
            if (variable <= 0 || !Path.IsPathFullyQualified(decoded)) return null;
            var prefix = interpret(ProjectCollection.Unescape(expression[..variable]));
            return Path.EndsInDirectorySeparator(prefix) ? Path.TrimEndingDirectorySeparator(prefix) : Path.GetDirectoryName(prefix);
        }
        bool TryEvaluatedPath(string value, out string path)
            => TryExactPhysicalAt(value, directory, out path);
        static bool TryExactPhysicalAt(string value, string baseDirectory, out string path)
        {
            // Native evaluated properties, items and metadata are exact paths. Characters
            // decoded from escapes are filename text, never new MSBuild syntax.
            path = "";
            if (string.IsNullOrWhiteSpace(value) || Uri.TryCreate(value, UriKind.Absolute, out var uri) && !uri.IsFile) return false;
            path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value, baseDirectory));
            return true;
        }
        void AddDirectory(string value)
        {
            if (TryEvaluatedPath(value, out var path)) directories.Add(path);
        }
    }

    internal static bool IsNativeInputPath(Evidence evidence, string path) => evidence.NativeInputDirectories.Any(root =>
        FileSystemPath.Comparer.Equals(root, Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)))
        || FileSystemPath.IsStrictDescendant(root, Path.GetFullPath(path)));

    private static string? ContextualProperty(string name, string origin)
    {
        if (!name.StartsWith("MSBuildThisFile", StringComparison.OrdinalIgnoreCase)) return null;
        var directory = Path.GetDirectoryName(origin)! + Path.DirectorySeparatorChar;
        var value = name["MSBuildThisFile".Length..].ToUpperInvariant() switch
        {
            "DIRECTORY" => directory,
            "FULLPATH" => origin,
            "NAME" => Path.GetFileNameWithoutExtension(origin),
            "EXTENSION" => Path.GetExtension(origin),
            "FILE" => Path.GetFileName(origin),
            "DIRECTORYNOROOT" => directory[Path.GetPathRoot(directory)!.Length..],
            _ => null
        };
        return value is null ? null : ProjectCollection.Escape(value);
    }

    private static bool IsScalarStringFunction(string operand)
    {
        // Inspect public return metadata only; never execute a function again.
        // Native MSBuild escapes string results before file-list expansion.
        var match = ScalarFunction().Match(operand);
        if (!match.Success || match.Index != 0) return false;
        var type = match.Groups["type"].Value.ToUpperInvariant() switch
        {
            "SYSTEM.IO.PATH" => typeof(Path),
            "SYSTEM.IO.FILE" => typeof(File),
            "SYSTEM.STRING" => typeof(string),
            "SYSTEM.ENVIRONMENT" => typeof(Environment),
            _ => null
        };
        if (type is null) return false;
        var name = match.Groups["method"].Value;
        if (name is "Escape" or "Unescape" or "ConvertFromBase64") return false;
        var methods = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(method => method.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                && !method.ContainsGenericParameters && !method.ReturnType.IsByRefLike
                && !method.GetParameters().Any(parameter => parameter.ParameterType.IsByRefLike)).ToArray();
        if (methods.Length == 0 || methods.Any(method => method.ReturnType != typeof(string))) return false;
        return BuildPathRelocation.ParenthesizedEnd(operand, match.Length - 1) == operand.Length - 1
            && operand[^1] == ')';
    }

    [GeneratedRegex(@"^\$\(\[(?<type>[A-Za-z0-9_.]+)\]::(?<method>[A-Za-z0-9_]+)\(", RegexOptions.CultureInvariant)]
    private static partial Regex ScalarFunction();

    [GeneratedRegex(@"\$\(([A-Za-z_][A-Za-z0-9_.-]*)\)", RegexOptions.CultureInvariant)]
    private static partial Regex Reference();
}
