using System.Collections.Concurrent;
using System.Diagnostics;
using System.Xml;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.FileSystem;
using Microsoft.Build.Framework;
using Microsoft.Build.Framework.Profiler;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Executed predicate origins and native-expanded Exists inputs from one private evaluation.</summary>
internal sealed class NativeConditionEvidence : ILogger, IDisposable
{
    private readonly Dictionary<ProjectUsingTaskElement, string> _taskConditions = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ProjectElement, string> _originalConditions = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<ProjectRootElement> _preparedRoots = new(ReferenceEqualityComparer.Instance);
    private ProfilerResult? _result;
    private int? _resultEvaluationId;
    private int _profileCount;
    private readonly ProbeFileSystem _fileSystem = new();
    internal sealed record ExistsProbe(string Path, bool Exists, bool ArgumentExpansion);
    internal sealed record ExistsInvocation(int Ordinal, bool Result, IReadOnlyList<ExistsProbe> Inputs, IReadOnlyList<string> Arguments);
    internal sealed record ConditionExecution(EvaluationPass Pass, IReadOnlyList<ExistsProbe> Inputs, IReadOnlyList<ExistsInvocation> Invocations);
    private readonly Dictionary<ProjectElement, List<ConditionExecution>> _conditions = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<ProjectElement> _propertyConditions = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<ProjectElement> _evaluatedItems = new(ReferenceEqualityComparer.Instance);

    private readonly NativeConditionCacheObserver _cacheObserver;
    internal ProjectCollection Collection { get; }
    internal IReadOnlyList<string> TraversalRoots => _fileSystem.TraversalRoots.Distinct(FileSystemPath.Comparer).ToArray();

    internal NativeConditionEvidence()
    {
        Collection = new ProjectCollection(null, null, null, ToolsetDefinitionLocations.Local, 1,
            onlyLogCriticalEvents: false, loadProjectsReadOnly: false,
            useAsynchronousLogging: false, reuseProjectRootElementCache: false);
        try
        {
            _cacheObserver = new NativeConditionCacheObserver(Collection, () => _fileSystem.HasActiveOperand,
                root => _preparedRoots.Contains(root), _fileSystem.ObserveCacheArgument);
            Collection.ProjectAdded += ProjectAdded;
        }
        catch (Exception failure)
        {
            OwnedResourceCleanup.DisposePreservingFailure("Native condition owner initialization cleanup failed.", failure, Collection);
            throw;
        }
    }

    private void ProjectAdded(object? sender, ProjectCollection.ProjectAddedToProjectCollectionEventArgs message) =>
        PrepareConditions(message.ProjectRootElement);

    public void Dispose()
    {
        Collection.ProjectAdded -= ProjectAdded;
        OwnedResourceCleanup.Dispose("Native condition owner cleanup failed.", null,
            OwnedResourceCleanup.FromAction(() =>
            {
                _cacheObserver.Dispose();
                Collection.UnloadAllProjects();
            }), Collection);
    }

    private void PrepareConditions(ProjectRootElement root)
    {
        if (!_preparedRoots.Add(root)) return;
        var hasPredicates = root.AllChildren.Where(IsEvaluationCondition)
            .Any(element => BuildPathRelocation.RewriteExistsCalls(element.Condition, static (_, _) => "") != element.Condition);
        if (!hasPredicates && !root.UsingTasks.Any(task => !string.IsNullOrWhiteSpace(task.Condition))) return;
        // SDK PREs can be readonly. Serialize their existing XML: DeepClone loses
        // non-text content and empty attribute metadata on reparse. Reload this same
        // cached object through the safe reader entirely in memory before evaluation.
        using var text = new StringReader(root.OuterElement);
        using var reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        root.ReloadFrom(reader, throwIfUnsavedChanges: false, preserveFormatting: true);
        foreach (var element in root.AllChildren.Where(IsEvaluationCondition))
        {
            var original = element.Condition;
            var instrumented = BuildPathRelocation.RewriteExistsCalls(original, (call, ordinal) =>
            {
                var boundaries = _fileSystem.RegisterOperand(element, ordinal);
                return GuardExpression(call, boundaries);
            });
            if (instrumented != original)
            {
                _originalConditions.Add(element, original);
                element.Condition = instrumented;
            }
        }
        foreach (var task in root.UsingTasks.Where(task => !string.IsNullOrWhiteSpace(task.Condition)))
        {
            var original = OriginalCondition(task);
            _taskConditions.Add(task, original);
            var boundaries = _fileSystem.RegisterTask(task);
            // The original expression occurs once. Native And/Or short-circuit
            // semantics select one matching end sentinel without a second eval.
            task.Condition = GuardExpression(task.Condition, boundaries);
        }

        static bool IsEvaluationCondition(ProjectElement element)
        {
            if (string.IsNullOrWhiteSpace(element.Condition)) return false;
            for (ProjectElement? ancestor = element; ancestor is not null; ancestor = ancestor.Parent)
                if (ancestor is ProjectTargetElement) return false;
            return true;
        }
    }

    private static string GuardExpression(string original, (string Begin, string True, string False) boundaries) =>
        "(Exists('" + ProjectCollection.Escape(boundaries.Begin) + "') And (((" + original
        + ") And Exists('" + ProjectCollection.Escape(boundaries.True) + "')) Or Exists('"
        + ProjectCollection.Escape(boundaries.False) + "')))";

    internal string OriginalCondition(ProjectElement element) => element is ProjectUsingTaskElement task
        && _taskConditions.TryGetValue(task, out var original) ? original
        : _originalConditions.TryGetValue(element, out var originalCondition) ? originalCondition : element.Condition;

    public LoggerVerbosity Verbosity { get; set; } = LoggerVerbosity.Quiet;
    public string? Parameters { get; set; }

    public void Initialize(IEventSource eventSource)
    {
        if (eventSource is not IEventSource3 profiles)
            throw new NativeConditionProtocolException(NativeConditionProtocolFailure.EventSource);
        profiles.IncludeEvaluationProfiles();
        eventSource.AnyEventRaised += (_, message) =>
        {
            if (message is ProjectEvaluationFinishedEventArgs finished)
            {
                _result = finished.ProfilerResult;
                _resultEvaluationId = finished.BuildEventContext?.EvaluationId;
                _profileCount++;
            }
        };
    }
    public void Shutdown() { }

    internal void VerifyProtocol(ProjectCollection owner)
    {
        if (!ReferenceEquals(owner, Collection)) throw new NativeConditionProtocolException(NativeConditionProtocolFailure.CacheObserver);
        Begin();
        var marker = Path.Combine(Path.GetTempPath(), "roslyn-condition-capability-" + Guid.NewGuid().ToString("N"));
        var root = ProjectRootElement.Create(owner);
        var projectPath = marker + ".proj";
        for (var index = 0; index != 2; index++)
            root.AddPropertyGroup().Condition = "Exists('" + ProjectCollection.Escape(marker) + "')";
        var cachedGroup = root.AddImportGroup();
        cachedGroup.Condition = "Exists('$([System.IO.Path]::Combine(`" + ProjectCollection.Escape(Path.GetDirectoryName(projectPath)!)
            + "`, `" + ProjectCollection.Escape(Path.GetFileName(projectPath)) + "`))')";
        // Register the completed XML, just as native disk loads do. Registration
        // synchronously instruments this private cache entry before evaluation.
        root.FullPath = projectPath;
        var traversal = marker + "-traversal";
        root.AddItemGroup().AddItem("Compile", ProjectCollection.Escape(traversal) + "/**/*.cs");
        _fileSystem.CanaryTraversalPath = traversal;
        _fileSystem.CanaryPath = marker;
        _fileSystem.CanaryCalls = 0;
        Project? probe = null;
        Exception? primary = null;
        try
        {
            // Repeat the same glob through distinct evaluation contexts. A cross-epoch
            // native directory cache that bypasses observation must fail qualification.
            for (var pass = 0; pass != 2; pass++)
            {
                if (probe is not null)
                {
                    owner.UnloadProject(probe);
                    probe = null;
                    Begin();
                    _fileSystem.CanaryCalls = 0;
                }
                probe = Project.FromProjectRootElement(root, Options(new Dictionary<string, string>(), owner));
                if (_fileSystem.ProtocolFailure) throw new NativeConditionProtocolException(NativeConditionProtocolFailure.Callsite);
                if (_fileSystem.CanaryCalls != 2) throw new NativeConditionProtocolException(NativeConditionProtocolFailure.ExistenceCache);
                if (_fileSystem.Probes.Count(input => input.IsExists && FileSystemPath.Comparer.Equals(input.Path, marker)) != 2)
                    throw new NativeConditionProtocolException(NativeConditionProtocolFailure.Callsite);
                Bind(probe);
                if (!TraversalRoots.Contains(traversal, FileSystemPath.Comparer))
                    throw new NativeConditionProtocolException(NativeConditionProtocolFailure.Traversal);
                var missingGroups = root.PropertyGroups.ToArray();
                cachedGroup = root.ImportGroups.Single();
                if (missingGroups.Any(group => Executions(group).Count != 1 || Executions(group)[0].Inputs.Count != 1
                        || Executions(group)[0].Invocations.Count != 1)
                    || Executions(cachedGroup).Count != 1 || Executions(cachedGroup)[0].Invocations.Count != 1
                    || Executions(cachedGroup)[0].Inputs.Count != 0
                    || !Executions(cachedGroup)[0].Invocations[0].Result
                    || !Executions(cachedGroup)[0].Invocations[0].Arguments.SequenceEqual([root.FullPath], StringComparer.Ordinal))
                    throw new NativeConditionProtocolException(NativeConditionProtocolFailure.CanaryOrigin);
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            _fileSystem.CanaryPath = null;
            _fileSystem.CanaryTraversalPath = null;
            OwnedResourceCleanup.DisposePreservingFailure("Native condition canary cleanup failed.", primary,
                OwnedResourceCleanup.FromAction(() => { if (probe is not null) owner.UnloadProject(probe); }),
                OwnedResourceCleanup.FromAction(Begin));
        }
    }

    internal Microsoft.Build.Definition.ProjectOptions Options(IDictionary<string, string> globals, ProjectCollection owner)
    {
        if (!ReferenceEquals(owner, Collection)) throw new NativeConditionProtocolException(NativeConditionProtocolFailure.CacheObserver);
        _cacheObserver.AssertCurrent();
        return new()
        {
            GlobalProperties = globals,
            ProjectCollection = owner,
            LoadSettings = ProjectLoadSettings.ProfileEvaluation | ProjectLoadSettings.DoNotEvaluateElementsWithFalseCondition,
            // A custom filesystem requires Shared. This context is NEW for each
            // individual evaluation, so no context cache crosses project boundaries.
            EvaluationContext = Microsoft.Build.Evaluation.Context.EvaluationContext.Create(
                Microsoft.Build.Evaluation.Context.EvaluationContext.SharingPolicy.Shared, _fileSystem)
        };
    }

    internal void Begin()
    {
        _cacheObserver.AssertCurrent();
        _result = null;
        _resultEvaluationId = null;
        _profileCount = 0;
        _fileSystem.Probes.Clear();
        _fileSystem.TraversalRoots.Clear();
        _fileSystem.ProtocolFailure = false;
        _fileSystem.BeginTasks();
        _conditions.Clear();
        _propertyConditions.Clear();
        _evaluatedItems.Clear();
    }

    internal void Bind(Project project)
    {
        _cacheObserver.AssertCurrent();
        if (!ReferenceEquals(project.ProjectCollection, Collection)) throw new NativeConditionProtocolException(NativeConditionProtocolFailure.CacheObserver);
        if (_fileSystem.ProtocolFailure)
            throw new NativeConditionProtocolException(NativeConditionProtocolFailure.Callsite);
        if (!_fileSystem.TasksAreBalanced)
            throw new NativeConditionProtocolException(NativeConditionProtocolFailure.TaskBoundary);
        if (_result is not { } profile)
            throw new NativeConditionProtocolException(NativeConditionProtocolFailure.Profile);
        var roots = project.Imports.Select(import => import.ImportedProject).Prepend(project.Xml)
            .Distinct<ProjectRootElement>(ReferenceEqualityComparer.Instance).ToArray();
        var xml = roots.SelectMany(root => root.AllChildren).ToArray();
        var elements = xml.ToLookup(element => (element.Location.File, element.Location.Line, element.ElementName));
        var descriptions = new Dictionary<ProjectElement, string>(ReferenceEqualityComparer.Instance);
        string Description(ProjectElement element)
        {
            if (descriptions.TryGetValue(element, out var existing)) return existing;
            var description = element.OuterElement;
            descriptions.Add(element, description);
            return description;
        }
        var predicates = xml.Where(element => !string.IsNullOrEmpty(element.Condition))
            .ToLookup(element => (element.ConditionLocation.File, element.ConditionLocation.Line, element.Condition));
        var frames = profile.ProfiledLocations.Keys.OrderBy(frame => frame.Id).ToArray();
        var byId = frames.ToDictionary(frame => frame.Id);
        // Preserve every potential ambiguity refusal as well as semantic consumers.
        // Exact candidates are a subset of the raw file/line/name bucket, so an
        // element with at most one raw candidate cannot raise ElementOrigin.
        var elementFrames = frames.Where(frame => frame.Kind == EvaluationLocationKind.Element
            && !string.IsNullOrEmpty(frame.ElementName)).ToArray();
        var peers = elementFrames.ToLookup(frame =>
            (frame.ParentId, frame.EvaluationPass, frame.File, frame.Line, frame.ElementDescription));
        var retained = new HashSet<long>();
        var pending = new Queue<EvaluationLocation>();
        void Retain(EvaluationLocation frame)
        {
            if (retained.Add(frame.Id)) pending.Enqueue(frame);
        }
        foreach (var frame in frames.Where(frame => frame.Kind == EvaluationLocationKind.Condition)) Retain(frame);
        foreach (var frame in elementFrames)
            if (frame.EvaluationPass == EvaluationPass.Items
                || frame.Line is { } line && elements[(frame.File, line, frame.ElementName)].Skip(1).Any()) Retain(frame);
        // Mapping depends on all ancestors; occurrence selection depends on every
        // peer with the original counter key. Close both dependencies transitively.
        var expandedPeerKeys = new HashSet<(long? Parent, EvaluationPass Pass, string File, int? Line, string Element)>();
        while (pending.TryDequeue(out var frame))
        {
            if (frame.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent)) Retain(parent);
            if (frame.Kind != EvaluationLocationKind.Element || string.IsNullOrEmpty(frame.ElementName)) continue;
            var key = (frame.ParentId, frame.EvaluationPass, frame.File, frame.Line, frame.ElementDescription);
            if (expandedPeerKeys.Add(key))
                foreach (var peer in peers[key]) Retain(peer);
        }
        var mapped = new Dictionary<long, ProjectElement>();
        var occurrences = new Dictionary<(long? Parent, EvaluationPass Pass, string File, int? Line, string Element), int>();
        foreach (var frame in frames)
        {
            if (frame.Kind != EvaluationLocationKind.Element || string.IsNullOrEmpty(frame.ElementName)
                || !retained.Contains(frame.Id)) continue;
            ProjectElement? parent = null;
            for (var id = frame.ParentId; id is { } value && byId.TryGetValue(value, out var ancestor); id = ancestor.ParentId)
                if (mapped.TryGetValue(value, out parent)) break;
            var candidates = frame.Line is { } line
                ? elements[(frame.File, line, frame.ElementName)]
                    .Where(element => IsWithinProfileScope(element, parent)
                        && string.Equals(Description(element), frame.ElementDescription, StringComparison.Ordinal)).ToArray() : [];
            if (candidates.Length == 0) continue; // Synthetic SDK/file/pass frames have no authored element.
            var key = (frame.ParentId, frame.EvaluationPass, frame.File, frame.Line, frame.ElementDescription);
            occurrences.TryGetValue(key, out var occurrence);
            if (candidates.Length > 1 && !ReferenceEquals(parent, candidates[0]) && occurrence >= candidates.Length)
                throw new NativeConditionProtocolException(NativeConditionProtocolFailure.ElementOrigin);
            var selected = candidates.Length == 1 || ReferenceEquals(parent, candidates[0]) ? candidates[0] : candidates[occurrence];
            occurrences[key] = occurrence + 1;
            mapped[frame.Id] = selected;
            if (frame.EvaluationPass == EvaluationPass.Items && selected is ProjectItemElement) _evaluatedItems.Add(selected);
        }
        EvaluationLocation FindCondition(long stamp, bool argumentExpansion, int projectDepth, int conditionDepth)
        {
            var low = 0;
            var high = frames.Length - 1;
            var frameIndex = -1;
            while (low <= high)
            {
                var middle = low + (high - low) / 2;
                if (frames[middle].Id < stamp) { frameIndex = middle; low = middle + 1; }
                else high = middle - 1;
            }
            if (frameIndex < 0) throw new NativeConditionProtocolException(NativeConditionProtocolFailure.FrameBeforeProfile);
            var current = frames[frameIndex];
            while (current.Kind != EvaluationLocationKind.Condition)
            {
                if (current.ParentId is not { } id)
                {
                    var failure = new NativeConditionProtocolException(argumentExpansion
                        ? NativeConditionProtocolFailure.FrameArgumentRoot : NativeConditionProtocolFailure.FrameRoot);
                    failure.Data["ConditionProfileOwnerMatches"] = _resultEvaluationId == project.LastEvaluationId;
                    failure.Data["ConditionProfileCount"] = _profileCount;
                    failure.Data["ConditionNativeProjectDepth"] = projectDepth;
                    failure.Data["ConditionNativeConditionDepth"] = conditionDepth;
                    failure.Data["ConditionLatestFrameKind"] = frames[frameIndex].Kind.ToString();
                    failure.Data["ConditionLatestFramePass"] = frames[frameIndex].EvaluationPass.ToString();
                    throw failure;
                }
                if (!byId.TryGetValue(id, out current))
                    throw new NativeConditionProtocolException(NativeConditionProtocolFailure.FrameMissingParent);
            }
            return current;
        }
        var ownedProbes = new Dictionary<long, List<ExistsProbe>>();
        foreach (var probe in _fileSystem.Probes.Where(probe => probe.IsExists && probe.Task is null).OrderBy(probe => probe.Id))
        {
            var current = FindCondition(probe.Id, probe.ArgumentExpansion, probe.ProjectDepth, probe.ConditionDepth);
            if (!ownedProbes.TryGetValue(current.Id, out var paths)) ownedProbes.Add(current.Id, paths = []);
            paths.Add(new(probe.Path, probe.Exists, probe.ArgumentExpansion));
        }
        var ownedInvocations = new Dictionary<long, List<(ProjectElement Element, ExistsInvocation Invocation)>>();
        foreach (var observed in _fileSystem.OperandExecutions)
        {
            var frame = FindCondition(observed.Stamp, argumentExpansion: false, observed.ProjectDepth, observed.ConditionDepth);
            if (!ownedInvocations.TryGetValue(frame.Id, out var invocations)) ownedInvocations.Add(frame.Id, invocations = []);
            invocations.Add((observed.Element, new(observed.Ordinal, observed.Result, observed.Inputs, observed.Arguments)));
        }
        foreach (var frame in frames.Where(frame => frame.Kind == EvaluationLocationKind.Condition))
        {
            ProjectElement? parent = null;
            for (var id = frame.ParentId; id is { } value && byId.TryGetValue(value, out var ancestor); id = ancestor.ParentId)
                if (mapped.TryGetValue(value, out parent)) break;
            var candidates = frame.Line is { } line ? predicates[(frame.File, line, frame.ElementDescription)]
                .Where(element => IsWithinProfileScope(element, parent)).ToArray() : [];
            var selected = candidates.Length == 1 ? candidates[0] : candidates.FirstOrDefault(element => ReferenceEquals(element, parent))
                ?? candidates.FirstOrDefault(element => !_conditions.ContainsKey(element));
            if (selected is null) continue;
            var invocationEvidence = ownedInvocations.Remove(frame.Id, out var observed) ? observed : [];
            if (invocationEvidence.Any(invocation => !ReferenceEquals(invocation.Element, selected))
                || invocationEvidence.Select(invocation => invocation.Invocation.Ordinal).Distinct().Count() != invocationEvidence.Count)
                throw new NativeConditionProtocolException(NativeConditionProtocolFailure.ConditionOrigin);
            if (frame.EvaluationPass == EvaluationPass.Properties) _propertyConditions.Add(selected);
            if (!_conditions.TryGetValue(selected, out var executions)) _conditions.Add(selected, executions = []);
            executions.Add(new(frame.EvaluationPass, ownedProbes.Remove(frame.Id, out var captured) ? captured : [],
                invocationEvidence.Select(invocation => invocation.Invocation).ToArray()));
        }
        if (ownedProbes.Count != 0 || ownedInvocations.Count != 0) throw new NativeConditionProtocolException(NativeConditionProtocolFailure.ConditionOrigin);
        foreach (var execution in _fileSystem.TaskExecutions)
        {
            if (!_taskConditions.ContainsKey(execution.Task) || !roots.Contains(execution.Task.ContainingProject, ReferenceEqualityComparer.Instance))
                throw new NativeConditionProtocolException(NativeConditionProtocolFailure.TaskBoundary);
            if (!_conditions.TryGetValue(execution.Task, out var executions)) _conditions.Add(execution.Task, executions = []);
            if (execution.Invocations.Select(invocation => invocation.Ordinal).Distinct().Count() != execution.Invocations.Count)
                throw new NativeConditionProtocolException(NativeConditionProtocolFailure.TaskBoundary);
            executions.Add(new(EvaluationPass.UsingTasks, execution.Inputs, execution.Invocations));
        }
    }

    internal bool WasEvaluated(ProjectElement element) => _conditions.ContainsKey(element);
    internal int CountExistsProbes(string path)
    {
        var canonical = Path.GetFullPath(path);
        return _fileSystem.Probes.Count(probe => probe.IsExists && FileSystemPath.Comparer.Equals(probe.Path, canonical));
    }
    internal bool WasPropertyConditionEvaluated(ProjectElement element) => _propertyConditions.Contains(element);
    internal bool WasItemEvaluated(ProjectElement element) => _evaluatedItems.Contains(element);
    internal IReadOnlyList<ConditionExecution> Executions(ProjectElement element) =>
        _conditions.TryGetValue(element, out var inputs) ? inputs : [];

    private static bool IsWithin(ProjectElement element, ProjectElement ancestor)
    {
        for (ProjectElement? current = element; current is not null; current = current.Parent)
            if (ReferenceEquals(current, ancestor)) return true;
        return false;
    }

    private static bool IsWithinProfileScope(ProjectElement element, ProjectElement? parent) =>
        parent is null || !FileSystemPath.Comparer.Equals(element.ContainingProject.FullPath, parent.ContainingProject.FullPath)
        // An imported file has its own XML tree. Its exact file/line/expression
        // profiler key still binds the origin beneath the caller's Import frame.
        || IsWithin(element, parent);

    private sealed class ProbeFileSystem : MSBuildFileSystemBase
    {
        private enum Boundary { Begin, True, False }
        private sealed record Sentinel(ProjectElement Element, int? Ordinal, Boundary Boundary);
        private sealed record Scope(ProjectElement Element, int? Ordinal, long Stamp, int ProjectDepth, int ConditionDepth,
            List<ExistsProbe> Inputs, List<ExistsInvocation> Invocations, List<string> Arguments);
        private readonly ConcurrentDictionary<string, Sentinel> _sentinels = new(FileSystemPath.Comparer);
        private readonly ConcurrentDictionary<int, Stack<Scope>> _scopes = new();
        internal bool HasActiveOperand => _scopes.TryGetValue(Environment.CurrentManagedThreadId, out var scopes)
            && scopes.TryPeek(out var active) && active.Ordinal is not null;
        internal ConcurrentQueue<(ProjectUsingTaskElement Task, IReadOnlyList<ExistsProbe> Inputs, IReadOnlyList<ExistsInvocation> Invocations)> TaskExecutions { get; } = new();
        internal ConcurrentQueue<(ProjectElement Element, int Ordinal, bool Result, long Stamp, int ProjectDepth, int ConditionDepth, IReadOnlyList<ExistsProbe> Inputs, IReadOnlyList<string> Arguments)> OperandExecutions { get; } = new();
        internal bool TasksAreBalanced => _scopes.Values.All(stack => stack.Count == 0);
        internal (string Begin, string True, string False) RegisterOperand(ProjectElement element, int ordinal) => Register(element, ordinal);
        internal (string Begin, string True, string False) RegisterTask(ProjectUsingTaskElement task) => Register(task, null);
        private (string Begin, string True, string False) Register(ProjectElement element, int? ordinal)
        {
            var prefix = Path.Combine(Path.GetTempPath(), "roslyn-condition-boundary-" + Guid.NewGuid().ToString("N"));
            var paths = (Begin: prefix + "-begin", True: prefix + "-true", False: prefix + "-false");
            if (!_sentinels.TryAdd(paths.Begin, new(element, ordinal, Boundary.Begin))
                || !_sentinels.TryAdd(paths.True, new(element, ordinal, Boundary.True))
                || !_sentinels.TryAdd(paths.False, new(element, ordinal, Boundary.False)))
                throw new NativeConditionProtocolException(NativeConditionProtocolFailure.TaskBoundary);
            return paths;
        }
        internal void BeginTasks()
        {
            if (!TasksAreBalanced) throw new NativeConditionProtocolException(NativeConditionProtocolFailure.TaskBoundary);
            TaskExecutions.Clear();
            OperandExecutions.Clear();
        }
        internal ConcurrentQueue<(long Id, string Path, bool IsExists, bool Exists, bool ArgumentExpansion, int ProjectDepth, int ConditionDepth, ProjectUsingTaskElement? Task)> Probes { get; } = new();
        private int _protocolFailure;
        private int _canaryCalls;
        private string? _canaryPath;
        internal bool ProtocolFailure { get => Volatile.Read(ref _protocolFailure) != 0; set => Volatile.Write(ref _protocolFailure, value ? 1 : 0); }
        internal string? CanaryPath { get => Volatile.Read(ref _canaryPath); set => Volatile.Write(ref _canaryPath, value); }
        internal int CanaryCalls { get => Volatile.Read(ref _canaryCalls); set => Volatile.Write(ref _canaryCalls, value); }
        internal void ObserveCacheArgument(string path, System.Reflection.MethodBase?[] methods)
        {
            var native = typeof(Project).Assembly;
            var existsCaller = Array.FindIndex(methods, method => method?.DeclaringType?.Assembly == native
                && method.DeclaringType.FullName == "Microsoft.Build.Evaluation.FunctionCallExpressionNode" && method.Name == "BoolEvaluate");
            if (existsCaller < 0) return;
            if (methods.Take(existsCaller).Any(method => method?.DeclaringType?.Assembly == native
                && method.DeclaringType.FullName?.StartsWith("Microsoft.Build.Evaluation.Expander", StringComparison.Ordinal) == true)) return;
            if (_sentinels.ContainsKey(Path.GetFullPath(path))) return;
            var callers = methods.Count(method => method?.DeclaringType?.Assembly == native
                && method.DeclaringType.FullName == "Microsoft.Build.Evaluation.FunctionCallExpressionNode" && method.Name == "BoolEvaluate");
            var conditionDepth = methods.Count(method => method?.DeclaringType?.Assembly == native
                && method.DeclaringType.FullName?.StartsWith("Microsoft.Build.Evaluation.ConditionEvaluator", StringComparison.Ordinal) == true
                && method.Name.StartsWith("EvaluateCondition", StringComparison.Ordinal));
            var projectDepth = methods.Count(method => method?.DeclaringType == typeof(Project) && method.IsConstructor);
            if (callers != 1 || !_scopes.TryGetValue(Environment.CurrentManagedThreadId, out var scopes)
                || !scopes.TryPeek(out var active) || active.Ordinal is null
                || active.ConditionDepth != conditionDepth || active.ProjectDepth != projectDepth)
            {
                ProtocolFailure = true;
                return;
            }
            active.Arguments.Add(path);
        }

        internal ConcurrentQueue<string> TraversalRoots { get; } = new();
        internal string? CanaryTraversalPath { get; set; }
        public override bool DirectoryExists(string path) => IsCanaryTraversal(path) || base.DirectoryExists(path);
        private bool IsCanaryTraversal(string path) => CanaryTraversalPath is { } canary
            && FileSystemPath.Comparer.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), canary);
        public override IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
            => ObserveTraversal(path, () => base.EnumerateFiles(path, searchPattern, searchOption));
        public override IEnumerable<string> EnumerateDirectories(string path, string searchPattern, SearchOption searchOption)
            => ObserveTraversal(path, () => base.EnumerateDirectories(path, searchPattern, searchOption));
        public override IEnumerable<string> EnumerateFileSystemEntries(string path, string searchPattern, SearchOption searchOption)
            => ObserveTraversal(path, () => base.EnumerateFileSystemEntries(path, searchPattern, searchOption));
        private IEnumerable<string> ObserveTraversal(string path, Func<IEnumerable<string>> enumerate)
        {
            // Iterator execution binds observation to actual native consumption.
            // Each Options call supplies a fresh native FileMatcher/cache epoch.
            if (!Path.IsPathFullyQualified(path))
                throw new NativeConditionProtocolException(NativeConditionProtocolFailure.Traversal);
            TraversalRoots.Enqueue(Path.GetFullPath(path));
            if (IsCanaryTraversal(path)) yield break;
            foreach (var entry in enumerate()) yield return entry;
        }

        public override bool FileOrDirectoryExists(string path)
        {
            // Pinned MSBuild protocol: the public profiler allocates monotonically
            // increasing IDs; only this exact native Exists callsite supplies inputs.
            // No stack values, messages, or captured properties are inspected.
            var methods = new StackTrace(fNeedFileInfo: false).GetFrames().Select(frame => frame.GetMethod()).ToArray();
            var native = typeof(Project).Assembly;
            var callers = methods.Count(method => method?.DeclaringType?.Assembly == native
                && method.DeclaringType.FullName == "Microsoft.Build.Evaluation.FunctionCallExpressionNode" && method.Name == "BoolEvaluate");
            var existsCaller = Array.FindIndex(methods, method => method?.DeclaringType?.Assembly == native
                && method.DeclaringType.FullName == "Microsoft.Build.Evaluation.FunctionCallExpressionNode" && method.Name == "BoolEvaluate");
            var argumentExpansion = existsCaller >= 0 && methods.Take(existsCaller).Any(method => method?.DeclaringType?.Assembly == native
                && method.DeclaringType.FullName?.StartsWith("Microsoft.Build.Evaluation.Expander", StringComparison.Ordinal) == true);
            var projectDepth = methods.Count(method => method?.DeclaringType == typeof(Project) && method.IsConstructor);
            var conditionDepth = methods.Count(method => method?.DeclaringType?.Assembly == native
                && method.DeclaringType.FullName?.StartsWith("Microsoft.Build.Evaluation.ConditionEvaluator", StringComparison.Ordinal) == true
                && method.Name.StartsWith("EvaluateCondition", StringComparison.Ordinal));
            var scopeStack = _scopes.GetOrAdd(Environment.CurrentManagedThreadId, _ => new());
            if (_sentinels.TryGetValue(Path.GetFullPath(path), out var sentinel))
            {
                if (callers != 1 || argumentExpansion || conditionDepth == 0)
                {
                    ProtocolFailure = true;
                    return false;
                }
                if (sentinel.Boundary == Boundary.Begin)
                {
                    var boundaryStamp = new EvaluationLocation(EvaluationPass.Properties, "Condition scope", null, 0, null, null, EvaluationLocationKind.Condition).Id;
                    scopeStack.Push(new(sentinel.Element, sentinel.Ordinal, boundaryStamp, projectDepth, conditionDepth, [], [], []));
                    return true;
                }
                if (!scopeStack.TryPop(out var active) || !ReferenceEquals(active.Element, sentinel.Element)
                    || active.Ordinal != sentinel.Ordinal || active.ConditionDepth != conditionDepth)
                {
                    ProtocolFailure = true;
                    return false;
                }
                if (active.Ordinal is { } ordinal)
                {
                    var invocation = new ExistsInvocation(ordinal, sentinel.Boundary == Boundary.True, active.Inputs.ToArray(), active.Arguments.ToArray());
                    if (active.Element is ProjectUsingTaskElement)
                    {
                        if (!scopeStack.TryPeek(out var taskScope) || taskScope.Ordinal is not null
                            || !ReferenceEquals(taskScope.Element, active.Element)) ProtocolFailure = true;
                        else taskScope.Invocations.Add(invocation);
                    }
                    else OperandExecutions.Enqueue((active.Element, ordinal, invocation.Result,
                        active.Stamp, active.ProjectDepth, active.ConditionDepth, invocation.Inputs, invocation.Arguments));
                }
                else if (active.Element is ProjectUsingTaskElement task)
                    TaskExecutions.Enqueue((task, active.Inputs.ToArray(), active.Invocations.ToArray()));
                else ProtocolFailure = true;
                return sentinel.Boundary == Boundary.True;
            }
            if (callers > 1 || callers == 0 && methods.Any(method => method?.DeclaringType?.Assembly == native
                && (method.DeclaringType.FullName == "Microsoft.Build.Evaluation.FunctionCallExpressionNode"
                    || method.DeclaringType.FullName?.StartsWith("Microsoft.Build.Evaluation.ConditionEvaluator", StringComparison.Ordinal) == true)))
                ProtocolFailure = true; // Native existence helpers swallow exceptions; refuse after evaluation instead.
            var stamp = new EvaluationLocation(EvaluationPass.Items, "Operand probe", path, 0, null, null, EvaluationLocationKind.Condition).Id;
            var exists = false;
            try
            {
                if (FileSystemPath.Comparer.Equals(path, CanaryPath)) { Interlocked.Increment(ref _canaryCalls); return false; }
                exists = base.FileOrDirectoryExists(path);
                return exists;
            }
            finally
            {
                ProjectUsingTaskElement? task = null;
                if (callers == 1)
                {
                    foreach (var active in scopeStack)
                    {
                        if (active.ConditionDepth != conditionDepth) { ProtocolFailure = true; continue; }
                        if (active.Element is ProjectUsingTaskElement ownerTask) task = ownerTask;
                        active.Inputs.Add(new(Path.GetFullPath(path), exists, argumentExpansion));
                    }
                }
                Probes.Enqueue((stamp, Path.GetFullPath(path), callers == 1, exists, argumentExpansion, projectDepth, conditionDepth, task));
            }
        }
    }
}


internal enum NativeConditionProtocolFailure
{
    EventSource, Profile, Traversal, CanaryOrigin, ElementOrigin, ConditionOrigin, OperandOrigin,
    FrameBeforeProfile, FrameRoot, FrameArgumentRoot, FrameMissingParent, Callsite, ExistenceCache, Operand, CachedOperand, TaskBoundary, CacheObserver
}

internal sealed class NativeConditionProtocolException(NativeConditionProtocolFailure failure)
    : InvalidOperationException("The loaded MSBuild runtime cannot provide exact condition input provenance (" + failure
        + "). " + (failure == NativeConditionProtocolFailure.ExistenceCache
            ? "Disable process-wide MSBuild existence caching and restart with a supported MSBuild runtime."
            : failure == NativeConditionProtocolFailure.CachedOperand
                ? "This cached import function has no observable expanded path argument; exact cached argument provenance is required."
            : failure == NativeConditionProtocolFailure.Operand
                ? "Use an explicit property or a scalar string path expression for this existence predicate."
                : "Use a supported MSBuild runtime with complete evaluation provenance.")), IPublicMessageException
{
    internal NativeConditionProtocolFailure Failure { get; } = failure;
    public string PublicMessage => Message;
}
