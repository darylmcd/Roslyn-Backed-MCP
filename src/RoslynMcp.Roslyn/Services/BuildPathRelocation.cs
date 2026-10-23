using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Identifies physical path slots; arbitrary MSBuild property values retain their identity.</summary>
internal static partial class BuildPathRelocation
{
    internal static IReadOnlySet<string> KnownPathProperties => BuildPathRoles.PathProperties;
    private static readonly HashSet<string> PackageProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "RestorePackagesPath", "NuGetPackageRoot", "NuGetPackageFolders", "RestoreFallbackFolders",
        "RestoreAdditionalProjectFallbackFolders", "RestoreSources", "RestoreAdditionalProjectSources"
    };
    private static readonly HashSet<string> PathItems = new(StringComparer.OrdinalIgnoreCase)
    {
        "ProjectReference", "Compile", "AdditionalFiles", "EditorConfigFiles", "Analyzer", "Reference",
        "Content", "None", "EmbeddedResource", "Resource", "Page", "ApplicationDefinition"
    };

    internal static bool IsKnownProperty(string name) => KnownPathProperties.Contains(name);
    internal static bool IsPackageProperty(string name) => PackageProperties.Contains(name);

    internal static string RelocateGlobal(string name, string value, Func<string, string> mapPath, IReadOnlySet<string> properties)
    {
        if (name.Equals("CurrentSolutionConfigurationContents", StringComparison.OrdinalIgnoreCase))
            return RelocateSolutionConfiguration(value, mapPath);
        if (IsPackageProperty(name) || !properties.Contains(name) && !KnownPathProperties.Contains(name)) return value;
        return RelocateMsBuildValue(value, path =>
        {
            var mapped = mapPath(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)));
            return Path.EndsInDirectorySeparator(path) && !Path.EndsInDirectorySeparator(mapped) ? mapped + path[^1] : mapped;
        });
    }

    internal static string RelocateSolutionConfiguration(string value, Func<string, string> mapPath)
    {
        var decoded = Microsoft.Build.Evaluation.ProjectCollection.Unescape(value);
        try
        {
            using var reader = XmlReader.Create(new StringReader(decoded), new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
            if (document.Root?.Name != "SolutionConfiguration")
                return value;
            // This is MSBuild's structured solution-configuration protocol, not an
            // arbitrary global string. Preserve every field except its physical path slot.
            var changed = false;
            foreach (var project in document.Root.Elements("ProjectConfiguration"))
                if (project.Attribute("AbsolutePath") is { } path && Path.IsPathFullyQualified(path.Value))
                {
                    var mapped = mapPath(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Value)));
                    if (mapped == path.Value) continue;
                    path.Value = mapped;
                    changed = true;
                }
            if (!changed) return value;
            return Microsoft.Build.Evaluation.ProjectCollection.Escape(document.ToString(SaveOptions.DisableFormatting));
        }
        catch (XmlException)
        {
            // An authored malformed/opaque value still participates in exact comparison;
            // it is never replaced by an empty context or printed in diagnostics.
            return value;
        }
    }

    internal static string RelocateMsBuildValue(string value, Func<string, string> mapPath)
    {
        return string.Join(";", value.Split(';').Select(token => RelocateMsBuildPath(token, mapPath)));
    }

    internal static string RelocateMsBuildPath(string token, Func<string, string> mapPath)
    {
        // Keep the authored suffix byte spelling, including property expressions, globs,
        // and escaped literal delimiters. Only the relocated physical prefix is escaped.
        var trimmed = token.Trim();
        var decoded = Microsoft.Build.Evaluation.ProjectCollection.Unescape(trimmed);
        if (!Path.IsPathFullyQualified(decoded)) return token;
        var mapped = mapPath(decoded);
        if (mapped == decoded) return token;
        var suffix = 0;
        while (suffix < decoded.Length && suffix < mapped.Length && SameChar(decoded[^(suffix + 1)], mapped[^(suffix + 1)])) suffix++;
        var decodedPrefix = decoded.Length - suffix;
        var rawPrefix = 0;
        for (var consumed = 0; consumed < decodedPrefix; consumed++)
            rawPrefix += rawPrefix + 2 < trimmed.Length && trimmed[rawPrefix] == '%'
                && Uri.IsHexDigit(trimmed[rawPrefix + 1]) && Uri.IsHexDigit(trimmed[rawPrefix + 2]) ? 3 : 1;
        var leading = token.Length - token.TrimStart().Length;
        var trailing = token.Length - token.TrimEnd().Length;
        return token[..leading] + Microsoft.Build.Evaluation.ProjectCollection.Escape(mapped[..(mapped.Length - suffix)])
            + trimmed[rawPrefix..] + (trailing == 0 ? "" : token[^trailing..]);

        static bool SameChar(char left, char right) => (left is '/' or '\\') && (right is '/' or '\\')
            || (OperatingSystem.IsWindows() ? char.ToUpperInvariant(left) == char.ToUpperInvariant(right) : left == right);
    }

    internal static string RewriteExistsOperands(string value, Func<string, string> transform)
    {
        var result = value;
        var calls = ExistsCalls(value);
        var replacements = calls.Select(call => transform(value.Substring(call.OperandStart, call.OperandLength))).ToArray();
        for (var index = calls.Count - 1; index >= 0; index--)
        {
            var call = calls[index];
            var original = value.Substring(call.OperandStart, call.OperandLength);
            var transformed = replacements[index];
            if (transformed == original) continue;
            // An expanded literal may contain spaces although its original
            // property token was unquoted. Quote the new native argument only.
            if (!call.Quoted) transformed = "'" + transformed.Replace("'", "%27", StringComparison.Ordinal) + "'";
            result = result.Remove(call.OperandStart, call.OperandLength).Insert(call.OperandStart, transformed);
        }
        return result;
    }

    internal static string RewriteExistsCalls(string value, Func<string, int, string> transform)
    {
        var calls = ExistsCalls(value);
        var replacements = calls.Select((call, ordinal) => transform(value.Substring(call.Start, call.Length), ordinal)).ToArray();
        var result = value;
        for (var index = calls.Count - 1; index >= 0; index--)
            result = result.Remove(calls[index].Start, calls[index].Length).Insert(calls[index].Start, replacements[index]);
        return result;
    }

    private readonly record struct ExistsCall(int Start, int Length, int OperandStart, int OperandLength, bool Quoted);

    // Locate source spans only. Native MSBuild still parses/evaluates the
    // condition; this scanner never supplies boolean or function semantics.
    private static List<ExistsCall> ExistsCalls(string value)
    {
        var calls = new List<ExistsCall>();
        for (var index = 0; index < value.Length;)
        {
            if (value[index] is '\u0027' or '\u0022') { index = QuotedEnd(index); continue; }
            if (ReferenceStart(index)) { index = ParenthesizedEnd(value, index + 1); continue; }
            if (index + 6 > value.Length || !value.AsSpan(index, 6).Equals("Exists", StringComparison.OrdinalIgnoreCase)
                || index > 0 && Identifier(value[index - 1]) || index + 6 < value.Length && Identifier(value[index + 6]))
            { index++; continue; }
            var open = index + 6;
            while (open < value.Length && char.IsWhiteSpace(value[open])) open++;
            if (open == value.Length || value[open] != '(') { index++; continue; }
            var end = ParenthesizedEnd(value, open);
            if (end > value.Length || end == 0 || value[end - 1] != ')') break;
            var operandStart = open + 1;
            var operandEnd = end - 1;
            while (operandStart < operandEnd && char.IsWhiteSpace(value[operandStart])) operandStart++;
            while (operandEnd > operandStart && char.IsWhiteSpace(value[operandEnd - 1])) operandEnd--;
            var quoted = operandEnd - operandStart >= 2 && value[operandStart] is '\u0027' or '\u0022'
                && value[operandEnd - 1] == value[operandStart];
            if (quoted) { operandStart++; operandEnd--; }
            calls.Add(new(index, end - index, operandStart, operandEnd - operandStart, quoted));
            index = end;
        }
        return calls;

        static bool Identifier(char character) => char.IsLetterOrDigit(character) || character is '_' or '.';
        bool ReferenceStart(int index) => index + 1 < value.Length && value[index] is '$' or '@' or '%' && value[index + 1] == '(';
        int QuotedEnd(int start)
        {
            var quote = value[start];
            for (var index = start + 1; index < value.Length;)
            {
                if (ReferenceStart(index)) { index = ParenthesizedEnd(value, index + 1); continue; }
                if (value[index++] == quote) return index;
            }
            return value.Length;
        }
    }

    // Return the exclusive end of a native parenthesized expression. Quoted
    // argument contents are data, including unmatched filename parentheses.
    internal static int ParenthesizedEnd(string value, int start)
    {
        var depth = 1;
        for (var index = start + 1; index < value.Length; index++)
        {
            if (value[index] is '\u0027' or '\u0022' or '`')
            {
                var quote = value[index++];
                while (index < value.Length && value[index] != quote) index++;
            }
            else if (value[index] == '(') depth++;
            else if (value[index] == ')' && --depth == 0) return index + 1;
        }
        return value.Length + 1; // Leave malformed source for the native parser.
    }

    internal static string RelocateCondition(string value, Func<string, string> mapPath, IReadOnlySet<string> properties)
        => RelocateConditionCore(value, mapPath, properties, static _ => true);

    internal static string RelocateCapturedCondition(string value, Func<string, string> mapPath,
        IReadOnlySet<string> properties, IReadOnlyList<string> capturedOperands)
        => RelocateConditionCore(value, mapPath, properties, operand => capturedOperands.Contains(operand, StringComparer.Ordinal));

    private static string RelocateConditionCore(string value, Func<string, string> mapPath,
        IReadOnlySet<string> properties, Func<string, bool> captured)
    {
        var exists = RewriteExistsOperands(value, operand => captured(operand) ? RelocateMsBuildValue(operand, mapPath) : operand);
        return ComparisonOperands().Replace(exists, match =>
        {
            if (!new[] { "left", "right" }.Any(name => PropertyReference().Matches(match.Groups[name].Value)
                .Any(reference => properties.Contains(reference.Groups[1].Value)))) return match.Value;
            // Quoting and operators retain their spelling. Only a comparison explicitly
            // involving a proven path property supplies path semantics to its operands.
            var result = match.Value;
            foreach (var name in new[] { "left", "right" }.OrderByDescending(name => match.Groups[name].Index))
            {
                var group = match.Groups[name];
                var raw = group.Value;
                var quoted = raw.Length >= 2 && (raw[0] is '\u0027' or '\u0022') && raw[^1] == raw[0];
                var content = quoted ? raw[1..^1] : raw;
                var relocated = RelocateMsBuildPath(content, mapPath);
                var replacement = quoted ? raw[0] + relocated + raw[^1] : relocated;
                result = result.Remove(group.Index - match.Index, group.Length).Insert(group.Index - match.Index, replacement);
            }
            return result;
        });

    }

    internal static bool IsPathItem(string name) => PathItems.Contains(name);

    internal static bool IsPathAttribute(XAttribute attribute)
    {
        var name = attribute.Name.LocalName;
        var owner = attribute.Parent?.Name.LocalName;
        return (owner == "Import" && name == "Project")
            || (owner == "Project" && name == "Path")
            || (owner is not null && PathItems.Contains(owner) && name is "Include" or "Remove" or "Update" or "Exclude")
            || (owner != "Exec" && name is "Projects" or "SourceFiles" or "DestinationFiles" or "DestinationFolder" or "Files" or "Directories");
    }

    internal static bool IsPathCondition(XAttribute attribute, IReadOnlySet<string> properties) =>
        attribute.Name.LocalName == "Condition" && (attribute.Value.Contains("Exists(", StringComparison.OrdinalIgnoreCase)
            || PropertyReference().Matches(attribute.Value).Any(match => properties.Contains(match.Groups[1].Value)));

    internal static bool IsPathText(XElement element, IReadOnlySet<string> properties) =>
        (element.Parent?.Name.LocalName == "PropertyGroup" && properties.Contains(element.Name.LocalName))
        || (element.Name.LocalName == "HintPath" && element.Parent?.Name.LocalName == "Reference");

    [GeneratedRegex(@"(?<left>'[^']*'|""[^""]*""|\$\([A-Za-z_][A-Za-z0-9_.-]*\)[^\s<>=!]*|[^\s'""<>=!()]+)\s*(?:==|!=|<=|>=|<|>)\s*(?<right>'[^']*'|""[^""]*""|\$\([A-Za-z_][A-Za-z0-9_.-]*\)[^\s<>=!]*|[^\s'""<>=!()]+)", RegexOptions.CultureInvariant)]
    private static partial Regex ComparisonOperands();

    [GeneratedRegex(@"\$\(([A-Za-z_][A-Za-z0-9_.-]*)\)", RegexOptions.CultureInvariant)]
    private static partial Regex PropertyReference();
}
