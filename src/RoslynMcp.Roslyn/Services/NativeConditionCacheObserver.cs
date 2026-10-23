using System.Diagnostics;
using System.Reflection;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Observes lookup arguments in one explicitly nonshared native XML cache without changing lookup semantics.</summary>
internal sealed class NativeConditionCacheObserver : IDisposable
{
    private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly ProjectCollection _owner;
    private readonly PropertyInfo _cacheProperty;
    private readonly object _cache;
    private readonly FieldInfo _weakField;
    private readonly FieldInfo _dictionaryField;
    private readonly object _original;
    private readonly object _observed;
    private readonly ObservingComparer _comparer;
    private bool _disposed;

    internal NativeConditionCacheObserver(ProjectCollection owner, Func<bool> hasActiveOperand,
        Func<ProjectRootElement, bool> isRetainedRoot, Action<string, MethodBase?[]> observe)
    {
        _owner = owner;
        var assembly = typeof(ProjectCollection).Assembly;
        var cacheType = assembly.GetType("Microsoft.Build.Evaluation.ProjectRootElementCache") ?? throw Unavailable();
        _cacheProperty = typeof(ProjectCollection).GetProperty("ProjectRootElementCache", InstancePrivate) ?? throw Unavailable();
        _cache = _cacheProperty.GetValue(owner) ?? throw Unavailable();
        if (_cache.GetType() != cacheType || owner.LoadedProjects.Count != 0) throw Unavailable();
        var shared = typeof(ProjectCollection).GetField("s_projectRootElementCache", BindingFlags.Static | BindingFlags.NonPublic) ?? throw Unavailable();
        if (ReferenceEquals(shared.GetValue(null), _cache)) throw Unavailable();
        if (cacheType.GetField("_autoReloadFromDisk", InstancePrivate)?.GetValue(_cache) is not false) throw Unavailable();
        _weakField = cacheType.GetField("_weakCache", InstancePrivate) ?? throw Unavailable();
        _original = _weakField.GetValue(_cache) ?? throw Unavailable();
        var weakType = assembly.GetType("Microsoft.Build.Collections.WeakValueDictionary`2")?.MakeGenericType(typeof(string), typeof(ProjectRootElement)) ?? throw Unavailable();
        if (_original.GetType() != weakType) throw Unavailable();
        _dictionaryField = weakType.GetField("_dictionary", InstancePrivate) ?? throw Unavailable();
        if (_dictionaryField.GetValue(_original) is not Dictionary<string, WeakReference<ProjectRootElement>> original || original.Count != 0) throw Unavailable();
        _comparer = new ObservingComparer(original.Comparer, hasActiveOperand, isRetainedRoot, observe);
        var constructor = weakType.GetConstructor([typeof(IEqualityComparer<string>)]) ?? throw Unavailable();
        _observed = constructor.Invoke([_comparer]);
        if (_dictionaryField.GetValue(_observed) is not Dictionary<string, WeakReference<ProjectRootElement>> observedDictionary)
            throw Unavailable();
        _comparer.Bind(observedDictionary);
        _weakField.SetValue(_cache, _observed);
    }

    internal void AssertCurrent()
    {
        if (_disposed || !ReferenceEquals(_cacheProperty.GetValue(_owner), _cache)
            || !ReferenceEquals(_weakField.GetValue(_cache), _observed)
            || _dictionaryField.GetValue(_observed) is not Dictionary<string, WeakReference<ProjectRootElement>> dictionary
            || !ReferenceEquals(dictionary.Comparer, _comparer) || !ReferenceEquals(dictionary, _comparer.Dictionary)) throw Unavailable();
        foreach (var pair in dictionary) _comparer.AssertRetained(pair.Key, pair.Value);
    }

    public void Dispose()
    {
        if (_disposed) return;
        // Do not overwrite a replacement that this observer never owned.
        AssertCurrent();
        _weakField.SetValue(_cache, _original);
        _disposed = true;
    }

    private static NativeConditionProtocolException Unavailable() => new(NativeConditionProtocolFailure.CacheObserver);

    private sealed class ObservingComparer(IEqualityComparer<string> original, Func<bool> hasActiveOperand,
        Func<ProjectRootElement, bool> isRetainedRoot, Action<string, MethodBase?[]> observe) : IEqualityComparer<string>
    {
        [ThreadStatic] private static HashSet<ObservingComparer>? _inspecting;
        private Dictionary<string, WeakReference<ProjectRootElement>>? _dictionary;
        internal Dictionary<string, WeakReference<ProjectRootElement>> Dictionary => _dictionary ?? throw Unavailable();
        internal void Bind(Dictionary<string, WeakReference<ProjectRootElement>> dictionary)
        {
            if (_dictionary is not null) throw Unavailable();
            _dictionary = dictionary;
        }
        internal void AssertRetained(string key, WeakReference<ProjectRootElement>? reference)
        {
            if (reference is null || !reference.TryGetTarget(out var target)
                || !isRetainedRoot(target) || !original.Equals(key, target.FullPath)) throw Unavailable();
        }

        public bool Equals(string? left, string? right) => original.Equals(left, right);

        public int GetHashCode(string value)
        {
            var hash = original.GetHashCode(value);
            if (_inspecting?.Contains(this) == true) return hash;
            if (!hasActiveOperand()) return hash;
            var methods = new StackTrace(fNeedFileInfo: false).GetFrames().Select(frame => frame.GetMethod()).ToArray();
            // Weak-reference scavenging can call Remove under the same native lookup.
            // Observe only the dictionary lookup, never removal or insertion hashes.
            if (methods.Any(method => method?.DeclaringType?.IsGenericType == true
                    && method.DeclaringType.GetGenericTypeDefinition() == typeof(Dictionary<,>) && method.Name is "Remove" or "TryInsert")) return hash;
            // Every native-added root is already strongly retained by the owner.
            // Check the queried entry before the native lookup: a live registered
            // target cannot enter WeakValueDictionary's dead-reference Remove path.
            // Native Exists uses a null loader/format request and autoreload is off,
            // so its lookup has no insertion, invalidation or scavenging effects.
            // GetOrLoad can be inlined into Get independently of BoolEvaluate's
            // optimization tier. The callback still requires the native Exists
            // callsite, excludes expansion, and binds the exact active operand;
            // a helper's optional stack frame is not its operation identity.
            var inspecting = _inspecting ??= new();
            inspecting.Add(this);
            try
            {
                if (Dictionary.TryGetValue(value, out var reference)) AssertRetained(value, reference);
            }
            finally { inspecting.Remove(this); }
            observe(value, methods);
            return hash;
        }
    }
}
