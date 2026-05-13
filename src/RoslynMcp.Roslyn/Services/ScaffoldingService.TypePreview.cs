using System.Collections.Immutable;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

public sealed partial class ScaffoldingService
{
    public async Task<RefactoringPreviewDto> PreviewScaffoldTypeAsync(string workspaceId, ScaffoldTypeDto request, CancellationToken ct)
    {
        IdentifierValidation.ThrowIfInvalidIdentifier(request.TypeName, "type name");
        var project = ResolveProject(workspaceId, request.ProjectName);
        var projectDirectory = Path.GetDirectoryName(project.FilePath)
            ?? throw new InvalidOperationException($"Project directory could not be resolved for '{project.FilePath}'.");
        var typeNamespace = string.IsNullOrWhiteSpace(request.Namespace) ? project.Name : request.Namespace!;
        var folderSegments = ResolveFolderSegmentsForNamespace(typeNamespace, project.Name);
        var filePath = Path.Combine([projectDirectory, .. folderSegments, $"{request.TypeName}.cs"]);

        var interfaceResolution = await ResolveInterfaceMembersAsync(workspaceId, request, ct).ConfigureAwait(false);
        var content = BuildTypeContent(typeNamespace, request, interfaceResolution);

        var preview = await _fileOperationService
            .PreviewCreateFileAsync(workspaceId, new CreateFileDto(project.Name, filePath, content), ct)
            .ConfigureAwait(false);

        if (interfaceResolution.Warnings.Count > 0)
        {
            return preview with { Warnings = interfaceResolution.Warnings };
        }
        return preview;
    }

    /// <summary>
    /// Item 2: when <see cref="ScaffoldTypeDto.ImplementInterface"/> is true and the scaffolded
    /// type is a class (not an interface/record/enum), walk any interface candidates in
    /// <c>BaseType</c> and <c>Interfaces</c>, resolve each to an <see cref="INamedTypeSymbol"/>,
    /// and build textual stub declarations for all interface members. Falls back to
    /// <see cref="InterfaceResolutionResult.Empty"/> (with a warning) when the interface cannot
    /// be resolved so scaffold still succeeds.
    /// </summary>
    private async Task<InterfaceResolutionResult> ResolveInterfaceMembersAsync(
        string workspaceId, ScaffoldTypeDto request, CancellationToken ct)
    {
        if (!request.ImplementInterface)
            return InterfaceResolutionResult.Empty;

        var normalizedKind = request.TypeKind.ToLowerInvariant();
        if (normalizedKind is "interface" or "enum")
            return InterfaceResolutionResult.Empty;

        var candidates = CollectInterfaceCandidates(request);
        if (candidates.Count == 0)
            return InterfaceResolutionResult.Empty;

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var stubs = new System.Text.StringBuilder();
        var requiredUsings = new HashSet<string>(StringComparer.Ordinal);
        var warnings = new List<string>();
        var emittedSignatures = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in candidates)
        {
            var resolved = await ResolveTypeSymbolAsync(solution, candidate, ct).ConfigureAwait(false);
            if (resolved is null)
            {
                warnings.Add(
                    $"Could not resolve '{candidate}' to a type symbol in workspace '{workspaceId}'. " +
                    "Scaffolded class will have an empty body — add interface members manually.");
                continue;
            }

            if (resolved.TypeKind != TypeKind.Interface)
                continue; // concrete base class — no stubs needed.

            EmitMembersForInterface(resolved, stubs, requiredUsings, emittedSignatures);
        }

        return new InterfaceResolutionResult(stubs.ToString(), requiredUsings, warnings);
    }

    /// <summary>
    /// Collect candidate type names to resolve as interfaces: the optional <see cref="ScaffoldTypeDto.BaseType"/>
    /// plus every non-blank entry in <see cref="ScaffoldTypeDto.Interfaces"/>. Base-type entries that turn out
    /// to be concrete classes are filtered later in the resolution loop.
    /// </summary>
    private static List<string> CollectInterfaceCandidates(ScaffoldTypeDto request)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.BaseType))
            candidates.Add(request.BaseType!);
        if (request.Interfaces is not null)
            candidates.AddRange(request.Interfaces.Where(n => !string.IsNullOrWhiteSpace(n)));
        return candidates;
    }

    /// <summary>
    /// Resolve a candidate type name across every project's compilation, preferring an exact metadata-name
    /// match and falling back to <c>GetSymbolsWithName</c> with a display-name compare so short names
    /// (e.g. <c>IDisposable</c>) still match without a leading namespace.
    /// </summary>
    private static async Task<INamedTypeSymbol?> ResolveTypeSymbolAsync(
        Solution solution, string candidate, CancellationToken ct)
    {
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;
            var resolved = compilation.GetTypeByMetadataName(candidate)
                ?? compilation.GetSymbolsWithName(
                        StripGenericArity(candidate), Microsoft.CodeAnalysis.SymbolFilter.Type, ct)
                    .OfType<INamedTypeSymbol>()
                    .FirstOrDefault(t => t.ToDisplayString().Equals(candidate, StringComparison.Ordinal) ||
                                         t.Name.Equals(candidate, StringComparison.Ordinal));
            if (resolved is not null) return resolved;
        }
        return null;
    }

    /// <summary>
    /// Emit stub text for every required-to-implement member of the resolved interface and all its
    /// inherited interfaces (<see cref="INamedTypeSymbol.AllInterfaces"/>), deduped by signature so the
    /// same member inherited via two paths is only emitted once.
    /// </summary>
    private static void EmitMembersForInterface(
        INamedTypeSymbol resolved,
        System.Text.StringBuilder stubs,
        HashSet<string> requiredUsings,
        HashSet<string> emittedSignatures)
    {
        // AllInterfaces includes inherited interfaces (IFoo : IBar implements IBar's members too).
        var interfacesToEmit = new List<INamedTypeSymbol> { resolved };
        interfacesToEmit.AddRange(resolved.AllInterfaces);

        foreach (var iface in interfacesToEmit)
        {
            foreach (var member in iface.GetMembers())
            {
                if (ShouldSkipInterfaceMember(member)) continue;

                var signature = BuildMemberSignatureKey(member);
                if (!emittedSignatures.Add(signature)) continue;

                var stub = BuildInterfaceMemberStub(member, requiredUsings);
                if (stub is null) continue;
                stubs.AppendLine(stub);
            }
        }
    }

    /// <summary>
    /// Skip static interface members (DIM entry points), property/event accessors (handled via the
    /// owning property/event itself), and members with a default implementation (C# 8 DIM — not
    /// required of implementors).
    /// </summary>
    private static bool ShouldSkipInterfaceMember(ISymbol member)
    {
        if (member.IsStatic) return true;
        if (member is IMethodSymbol methodSym &&
            methodSym.AssociatedSymbol is IPropertySymbol or IEventSymbol) return true;
        if (!member.IsAbstract && HasDefaultInterfaceImplementation(member)) return true;
        return false;
    }

    /// <summary>Rope generic arity (<c>`1</c>) from a display name like <c>IEnumerable`1</c>.</summary>
    private static string StripGenericArity(string name)
    {
        var tick = name.IndexOf('`');
        return tick < 0 ? name : name[..tick];
    }

    /// <summary>True when an interface method/property has a concrete default body (C# 8 DIM).</summary>
    private static bool HasDefaultInterfaceImplementation(ISymbol member)
    {
        foreach (var syntaxRef in member.DeclaringSyntaxReferences)
        {
            var node = syntaxRef.GetSyntax();
            if (node is Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax m && m.Body is not null)
                return true;
            if (node is Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax p &&
                p.AccessorList is not null &&
                p.AccessorList.Accessors.Any(a => a.Body is not null || a.ExpressionBody is not null))
                return true;
        }
        return false;
    }

    private static string BuildMemberSignatureKey(ISymbol member)
    {
        if (member is IMethodSymbol method)
        {
            var parms = string.Join(",", method.Parameters.Select(p => p.Type.ToDisplayString()));
            return $"M:{method.Name}({parms})<{method.TypeParameters.Length}>";
        }
        if (member is IPropertySymbol prop)
        {
            var parms = string.Join(",", prop.Parameters.Select(p => p.Type.ToDisplayString()));
            return $"P:{prop.Name}[{parms}]";
        }
        return $"{member.Kind}:{member.Name}";
    }

    /// <summary>
    /// Emit a textual method/property/event stub that throws <c>NotImplementedException</c>.
    /// Uses <c>MinimallyQualifiedFormat</c> so callers get readable type names plus recorded
    /// required <c>using</c> namespaces for later header assembly.
    /// </summary>
    private static string? BuildInterfaceMemberStub(ISymbol member, HashSet<string> requiredUsings)
    {
        requiredUsings.Add("System"); // NotImplementedException

        if (member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary)
        {
            CollectNamespaces(method.ReturnType, requiredUsings);
            foreach (var p in method.Parameters)
                CollectNamespaces(p.Type, requiredUsings);

            var typeParams = method.TypeParameters.Length == 0
                ? string.Empty
                : "<" + string.Join(", ", method.TypeParameters.Select(tp => tp.Name)) + ">";
            var parameters = string.Join(", ", method.Parameters.Select(FormatParameter));
            var constraints = BuildTypeParameterConstraints(method.TypeParameters);
            var returnType = method.ReturnsVoid ? "void" : method.ReturnType.ToMinimalDisplay();
            var body = method.ReturnsVoid
                ? "throw new NotImplementedException();"
                : "throw new NotImplementedException();";

            return
                $"    public {returnType} {method.Name}{typeParams}({parameters}){constraints}\n" +
                "    {\n" +
                $"        {body}\n" +
                "    }\n";
        }

        if (member is IPropertySymbol property && !property.IsIndexer)
        {
            CollectNamespaces(property.Type, requiredUsings);
            var type = property.Type.ToMinimalDisplay();
            var accessors = new List<string>();
            if (property.GetMethod is not null) accessors.Add("get => throw new NotImplementedException();");
            if (property.SetMethod is not null)
            {
                // Use set or init based on the interface declaration.
                var keyword = property.SetMethod.IsInitOnly ? "init" : "set";
                accessors.Add($"{keyword} => throw new NotImplementedException();");
            }
            var accessorBlock = string.Join(" ", accessors);
            return $"    public {type} {property.Name} {{ {accessorBlock} }}\n";
        }

        if (member is IEventSymbol evt)
        {
            CollectNamespaces(evt.Type, requiredUsings);
            var type = evt.Type.ToMinimalDisplay();
            return
                $"    public event {type}? {evt.Name}\n" +
                "    {\n" +
                "        add => throw new NotImplementedException();\n" +
                "        remove => throw new NotImplementedException();\n" +
                "    }\n";
        }

        return null;
    }

    private static string FormatParameter(IParameterSymbol parameter)
    {
        var modifier = parameter.RefKind switch
        {
            RefKind.Ref => "ref ",
            RefKind.Out => "out ",
            RefKind.In => "in ",
            _ => string.Empty
        };
        var typeText = parameter.Type.ToMinimalDisplay();
        return parameter.IsParams
            ? $"params {typeText} {parameter.Name}"
            : $"{modifier}{typeText} {parameter.Name}";
    }

    private static string BuildTypeParameterConstraints(ImmutableArray<ITypeParameterSymbol> typeParameters)
    {
        if (typeParameters.Length == 0) return string.Empty;
        var parts = new List<string>();
        foreach (var tp in typeParameters)
        {
            var clauses = new List<string>();
            if (tp.HasReferenceTypeConstraint) clauses.Add("class");
            if (tp.HasValueTypeConstraint) clauses.Add("struct");
            if (tp.HasUnmanagedTypeConstraint) clauses.Add("unmanaged");
            if (tp.HasNotNullConstraint) clauses.Add("notnull");
            foreach (var ct in tp.ConstraintTypes)
                clauses.Add(ct.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
            if (tp.HasConstructorConstraint) clauses.Add("new()");
            if (clauses.Count == 0) continue;
            parts.Add($" where {tp.Name} : {string.Join(", ", clauses)}");
        }
        return string.Concat(parts);
    }

    private static string BuildTypeContent(string typeNamespace, ScaffoldTypeDto request, InterfaceResolutionResult interfaceResolution)
    {
        var inheritance = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.BaseType))
        {
            inheritance.Add(request.BaseType);
        }

        if (request.Interfaces is not null)
        {
            inheritance.AddRange(request.Interfaces.Where(@interface => !string.IsNullOrWhiteSpace(@interface)));
        }

        var inheritanceClause = inheritance.Count > 0 ? $" : {string.Join(", ", inheritance)}" : string.Empty;
        var normalizedKind = request.TypeKind.ToLowerInvariant();
        var typeKeyword = normalizedKind switch
        {
            "interface" => "interface",
            "record" => "record",
            "enum" => "enum",
            _ => "class"
        };

        // Modern .NET convention: default scaffolded classes to `internal sealed class` so
        // they don't expand the public API surface and aren't subclassable by accident.
        // Records/interfaces/enums stay `public` (interface and enum cannot be sealed; records
        // are typically intended as DTOs that get used widely).
        var modifier = normalizedKind == "interface" || normalizedKind == "record" || normalizedKind == "enum"
            ? "public"
            : "internal sealed";

        // Item 2: deduplicate usings against the implied namespace. Skip any using equal to the
        // scaffolded type's own namespace — we're already in it.
        var usingsBlock = new System.Text.StringBuilder();
        foreach (var ns in interfaceResolution.RequiredUsings
            .Where(n => !string.Equals(n, typeNamespace, StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal))
        {
            usingsBlock.Append("using ").Append(ns).Append(";\n");
        }
        if (usingsBlock.Length > 0) usingsBlock.Append('\n');

        var body = string.IsNullOrEmpty(interfaceResolution.MemberStubs)
            ? string.Empty
            : interfaceResolution.MemberStubs;

        return $"{usingsBlock}namespace {typeNamespace};\n\n{modifier} {typeKeyword} {request.TypeName}{inheritanceClause}\n{{\n{body}}}\n";
    }
}
