using System.Text;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>Exact original-coordinate masks for caller-supplied test filters.</summary>
internal sealed class TestFilterRedaction
{
    private readonly string[] _literals;
    private readonly int _maximumLength;

    internal TestFilterRedaction(IReadOnlyList<string> arguments)
    {
        var literals = new List<string>();
        for (var index = 0; index < arguments.Count; index++)
        {
            if (!TryGetOption(arguments[index], out var inlineValue)) continue;
            var value = inlineValue ?? (index + 1 < arguments.Count ? arguments[++index] : "");
            if (value.Length > 0) literals.Add(value);
        }
        _literals = literals.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _maximumLength = _literals.Length == 0 ? 0 : _literals.Max(value => value.Length);
    }

    internal bool HasFilters => _literals.Length > 0;

    internal static bool TryGetOption(string argument, out string? inlineValue)
    {
        foreach (var option in new[] { "--filter", "--treenode-filter" })
        {
            if (string.Equals(argument, option, StringComparison.OrdinalIgnoreCase))
            {
                inlineValue = null;
                return true;
            }
            if (argument.StartsWith(option + "=", StringComparison.OrdinalIgnoreCase))
            {
                inlineValue = argument[(option.Length + 1)..];
                return true;
            }
        }
        inlineValue = null;
        return false;
    }

    internal string Mask(string source)
    {
        if (!HasFilters || source.Length == 0) return source;
        var mask = new bool[source.Length];
        Mark(source, mask);
        return Apply(source, mask, 0, source.Length);
    }

    internal string MaskHead(string source, int length)
    {
        var contextLength = (int)Math.Min(source.Length, (long)length + _maximumLength - 1);
        return Mask(source[..Math.Max(length, contextLength)])[..length];
    }

    private void Mark(string source, bool[] mask)
    {
        foreach (var literal in _literals)
        {
            var start = 0;
            while (start <= source.Length - literal.Length)
            {
                var match = source.IndexOf(literal, start, StringComparison.OrdinalIgnoreCase);
                if (match < 0) break;
                Array.Fill(mask, true, match, literal.Length);
                start = match + 1;
            }
        }
    }

    private static string Apply(string source, bool[] mask, int start, int length)
    {
        var chars = source.AsSpan(start, length).ToArray();
        for (var index = 0; index < length; index++)
            if (mask[start + index]) chars[index] = '*';
        return new string(chars);
    }

    internal Collector CreateCollector(int limit) => new(this, limit);

    internal sealed class Collector
    {
        private readonly TestFilterRedaction _owner;
        private readonly int _limit;
        private readonly StringBuilder _safe;
        private string _pending = "";
        private bool[] _pendingMask = [];
        private bool _completed;

        internal Collector(TestFilterRedaction owner, int limit)
        {
            _owner = owner;
            _limit = limit;
            _safe = new StringBuilder(Math.Min(limit, 4096));
        }

        internal void Append(string chunk)
        {
            if (_completed) throw InvalidOperationErrors.Internal("The filter collector is complete.");
            if (chunk.Length == 0) return;
            var bound = (int)Math.Min((long)_owner._maximumLength - 1 + chunk.Length, int.MaxValue);
            var consumed = 0;
            while (consumed < chunk.Length)
            {
                var count = Math.Min(bound - _pending.Length, chunk.Length - consumed);
                var window = string.Concat(_pending, chunk.AsSpan(consumed, count));
                var mask = new bool[window.Length];
                _pendingMask.CopyTo(mask, 0);
                _owner.Mark(window, mask);
                var commit = Math.Max(0, window.Length - (_owner._maximumLength - 1));
                AppendSafe(Apply(window, mask, 0, commit));
                _pending = window[commit..];
                _pendingMask = mask[commit..];
                consumed += count;
            }
        }

        internal string Complete()
        {
            if (_completed) throw InvalidOperationErrors.Internal("The filter collector is complete.");
            _completed = true;
            AppendSafe(Apply(_pending, _pendingMask, 0, _pending.Length));
            _pending = "";
            _pendingMask = [];
            return _safe.ToString();
        }

        private void AppendSafe(string piece)
        {
            if (piece.Length >= _limit)
            {
                _safe.Clear();
                _safe.Append(piece.AsSpan(piece.Length - _limit));
                return;
            }
            var keep = _limit - piece.Length;
            if (_safe.Length > keep) _safe.Remove(0, _safe.Length - keep);
            _safe.Append(piece);
        }
    }
}
