using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Serializes Roslyn <see cref="ISymbol"/> instances to portable base-64 handles and resolves
/// them back to symbols in a given <see cref="Solution"/>.
/// </summary>
/// <remarks>
/// A handle encodes the symbol's metadata name, display name, source location and, for
/// metadata members or constructed named types, exact assembly and structural identity as a
/// base-64-encoded JSON payload. The encoding is reversible and may contain a source path; it is
/// an API locator, not a confidential or non-disclosing token. Resolution first attempts lookup
/// by exact member or constructed type identity when present, otherwise by metadata name and source position.
/// </remarks>
public static class SymbolHandleSerializer
{
    private const int MaxConstructedTypeDepth = 8;

    /// <summary>
    /// Creates a portable encoded handle for the given symbol that can be passed back to
    /// <see cref="ResolveHandleAsync"/> to recover the symbol.
    /// </summary>
    /// <param name="symbol">The symbol to serialize.</param>
    /// <returns>A base-64-encoded handle string.</returns>
    public static string CreateHandle(ISymbol symbol)
    {
        var sourceLocation = symbol.Locations.FirstOrDefault(location => location.IsInSource);
        var lineSpan = sourceLocation?.GetLineSpan();
        MemberIdentity? memberIdentity = null;
        if (sourceLocation is null && symbol.ContainingType is not null && symbol is not INamedTypeSymbol)
        {
            var declarationId = DocumentationCommentId.CreateDeclarationId(symbol);
            if (string.IsNullOrEmpty(declarationId))
            {
                throw new NotSupportedException($"The metadata member kind {symbol.Kind} has no stable declaration identity.");
            }

            memberIdentity = new MemberIdentity(
                symbol.ContainingAssembly.Identity.ToString(),
                GetTypeMetadataName(symbol.ContainingType),
                declarationId);
        }
        var constructedType = symbol is INamedTypeSymbol { IsUnboundGenericType: false } named
            && (named.IsTupleType || HasConstructedLevel(named))
            ? EncodeType(named, named.NullableAnnotation, depth: 1, signatureOwner: null)
            : null;
        // A legacy location can select the open type when its parameter and the method
        // parameter have the same name. Older readers must have no locator to fall back to.
        if (symbol is INamedTypeSymbol candidate && ContainsMethodOwnedParameter(candidate))
        {
            var methodPayload = new SymbolHandlePayload(null, symbol.ToDisplayString(), null, null, null,
                MethodConstructedType: constructedType);
            return Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(methodPayload));
        }

        var payload = new SymbolHandlePayload(
            MetadataName: symbol is INamedTypeSymbol namedType ? namedType.ToDisplayString() : null,
            DisplayName: symbol.ToDisplayString(),
            FilePath: lineSpan?.Path,
            Line: lineSpan?.StartLinePosition.Line + 1,
            Column: lineSpan?.StartLinePosition.Character + 1,
            MemberIdentity: memberIdentity,
            ConstructedType: constructedType);

        var json = JsonSerializer.Serialize(payload);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>
    /// Attempts to resolve a symbol handle previously created by <see cref="CreateHandle"/> within
    /// the given solution.
    /// </summary>
    /// <param name="solution">The solution to search.</param>
    /// <param name="symbolHandle">The base-64-encoded handle string.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resolved symbol, or <see langword="null"/> if the handle decodes correctly but the symbol cannot be found in the solution.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="symbolHandle"/> is empty, not valid base64, not valid JSON, or not a well-formed handle payload.</exception>
    public static async Task<ISymbol?> ResolveHandleAsync(Solution solution, string symbolHandle, CancellationToken ct)
    {
        var payload = ParseHandlePayload(symbolHandle);

        if (payload.MemberIdentity is not null)
        {
            return await ResolveMetadataMemberAsync(solution, payload.MemberIdentity, ct).ConfigureAwait(false);
        }

        if ((payload.MethodConstructedType ?? payload.ConstructedType) is { } constructedType)
        {
            return await ResolveConstructedTypeAsync(solution, constructedType, ct).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(payload.MetadataName))
        {
            var symbolByMetadataName = await SymbolResolver.ResolveByMetadataNameAsync(solution, payload.MetadataName, ct).ConfigureAwait(false);
            if (symbolByMetadataName is not null)
            {
                return symbolByMetadataName;
            }
        }

        if (!string.IsNullOrWhiteSpace(payload.FilePath) && payload.Line.HasValue && payload.Column.HasValue)
        {
            var symbolBySource = await SymbolResolver.ResolveAtPositionAsync(
                solution,
                payload.FilePath,
                payload.Line.Value,
                payload.Column.Value,
                ct).ConfigureAwait(false);

            if (symbolBySource is not null)
            {
                if (string.IsNullOrWhiteSpace(payload.DisplayName) ||
                    string.Equals(symbolBySource.ToDisplayString(), payload.DisplayName, StringComparison.Ordinal))
                {
                    return symbolBySource;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Decodes a symbol handle string. Throws <see cref="ArgumentException"/> if the string is not a valid handle
    /// (distinguishes malformed handles from "valid handle, symbol not found" at resolution time).
    /// </summary>
    internal static SymbolHandlePayload ParseHandlePayload(string symbolHandle)
    {
        if (string.IsNullOrWhiteSpace(symbolHandle))
        {
            throw new PublicArgumentException("symbolHandle is empty.", nameof(symbolHandle));
        }

        string json;
        try
        {
            json = Encoding.UTF8.GetString(Convert.FromBase64String(symbolHandle.Trim()));
        }
        catch (FormatException ex)
        {
            throw new PublicArgumentException("symbolHandle is not valid base64.", nameof(symbolHandle), ex);
        }

        SymbolHandlePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<SymbolHandlePayload>(json);
        }
        catch (JsonException ex)
        {
            throw new PublicArgumentException("symbolHandle is not valid JSON.", nameof(symbolHandle), ex);
        }

        if (payload is null)
        {
            throw new PublicArgumentException("symbolHandle JSON deserialized to null.", nameof(symbolHandle));
        }

        if (payload.MemberIdentity is { } memberIdentity &&
            (string.IsNullOrWhiteSpace(memberIdentity.AssemblyIdentity) ||
             string.IsNullOrWhiteSpace(memberIdentity.ContainingTypeMetadataName) ||
             string.IsNullOrWhiteSpace(memberIdentity.DeclarationId)))
        {
            throw new PublicArgumentException("symbolHandle member identity is incomplete.", nameof(symbolHandle));
        }

        if (payload.ConstructedType is { } constructedType &&
            (payload.MemberIdentity is not null || constructedType.Kind != RecipeKind.Named ||
             !IsValidRecipe(constructedType, depth: 1, allowMethodOwner: false, selfArity: null)))
        {
            throw new PublicArgumentException("symbolHandle constructed type identity is invalid.", nameof(symbolHandle));
        }

        if (payload.MethodConstructedType is { } methodType &&
            (payload.ConstructedType is not null || payload.MemberIdentity is not null ||
             payload.MetadataName is not null || payload.FilePath is not null ||
             payload.Line is not null || payload.Column is not null || string.IsNullOrWhiteSpace(payload.DisplayName) ||
             methodType.Kind != RecipeKind.Named ||
             !IsValidRecipe(methodType, depth: 1, allowMethodOwner: true, selfArity: null) ||
             !ContainsMethodOwner(methodType)))
        {
            throw new PublicArgumentException("symbolHandle constructed type identity is invalid.", nameof(symbolHandle));
        }

        var hasMetadata = !string.IsNullOrWhiteSpace(payload.MetadataName);
        var hasSource = !string.IsNullOrWhiteSpace(payload.FilePath) && payload.Line.HasValue && payload.Column.HasValue;
        var hasDisplay = !string.IsNullOrWhiteSpace(payload.DisplayName);
        if (!hasMetadata && !hasSource && !hasDisplay && payload.MemberIdentity is null && payload.ConstructedType is null)
        {
            throw new PublicArgumentException(
                "symbolHandle payload must include MetadataName, source location (filePath, line, column), or DisplayName.",
                nameof(symbolHandle));
        }

        return payload;
    }

    internal sealed record SymbolHandlePayload(
        string? MetadataName,
        string? DisplayName,
        string? FilePath,
        int? Line,
        int? Column,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MemberIdentity? MemberIdentity = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ConstructedTypeIdentity? ConstructedType = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ConstructedTypeIdentity? MethodConstructedType = null);

    internal sealed record MemberIdentity(
        string AssemblyIdentity,
        string ContainingTypeMetadataName,
        string DeclarationId);

    internal enum RecipeKind { Named = 1, Array, Dynamic, TypeParameter, MethodTypeParameter, MethodSignatureParameter }

    internal sealed record MethodOwnerIdentity(string MetadataName, int Arity, bool IsStatic,
        SignatureCallingConvention CallingConvention, MethodKind MethodKind, MethodSignaturePart[] Signature,
        InterfaceMethodIdentity[] ExplicitInterfaces);

    internal sealed record MethodSignaturePart(RefKind RefKind, ConstructedTypeIdentity Type);

    internal sealed record InterfaceMethodIdentity(ConstructedTypeIdentity InterfaceType, string MetadataName);

    internal sealed record ConstructedTypeIdentity(RecipeKind Kind, NullableAnnotation Annotation)
    {
        public string? Assembly { get; init; }
        public TypeLevel[]? Chain { get; init; }
        public string?[]? TupleNames { get; init; }
        public bool NativeInteger { get; init; }
        public ConstructedTypeIdentity? Element { get; init; }
        public int? Rank { get; init; }
        public int? Ordinal { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public MethodOwnerIdentity? Method { get; init; }
    }

    internal sealed record TypeLevel(string Name, ConstructedTypeIdentity[]? Arguments);

    private static bool HasConstructedLevel(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? level = type; level is not null; level = level.ContainingType)
        {
            if (level.Arity > 0 && !SymbolEqualityComparer.IncludeNullability.Equals(level, level.OriginalDefinition))
            {
                return true;
            }
        }
        return false;
    }

    private static bool ContainsMethodOwnedParameter(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol parameter => parameter.TypeParameterKind == TypeParameterKind.Method,
        IArrayTypeSymbol array => ContainsMethodOwnedParameter(array.ElementType),
        IPointerTypeSymbol pointer => ContainsMethodOwnedParameter(pointer.PointedAtType),
        IFunctionPointerTypeSymbol pointer => ContainsMethodOwnedParameter(pointer.Signature.ReturnType) ||
            pointer.Signature.Parameters.Any(parameter => ContainsMethodOwnedParameter(parameter.Type)),
        INamedTypeSymbol named => named.TypeArguments.Any(ContainsMethodOwnedParameter) ||
            (named.ContainingType is { } containing && ContainsMethodOwnedParameter(containing)),
        _ => false,
    };

    private static bool ContainsMethodOwner(ConstructedTypeIdentity recipe) =>
        recipe.Kind == RecipeKind.MethodTypeParameter ||
        (recipe.Element is { } element && ContainsMethodOwner(element)) ||
        (recipe.Chain is { } chain && chain.Any(level => level.Arguments?.Any(ContainsMethodOwner) == true));

    private static ConstructedTypeIdentity? EncodeType(
        ITypeSymbol type, NullableAnnotation annotation, int depth, IMethodSymbol? signatureOwner)
    {
        if (depth > MaxConstructedTypeDepth || type.TypeKind == TypeKind.Error)
        {
            return null;
        }

        switch (type)
        {
            case INamedTypeSymbol { IsAnonymousType: false, IsUnboundGenericType: false } named:
                var chain = EncodeTypeChain(named, depth, includeArguments: true, signatureOwner);
                return chain is null ? null : new ConstructedTypeIdentity(RecipeKind.Named, annotation)
                {
                    Assembly = named.ContainingAssembly.Identity.ToString(),
                    Chain = chain,
                    TupleNames = named.IsTupleType
                        ? named.TupleElements.Select(element => element.IsExplicitlyNamedTupleElement ? element.Name : null).ToArray()
                        : null,
                    NativeInteger = named.IsNativeIntegerType,
                };
            case IArrayTypeSymbol array when (array.IsSZArray || array.Rank >= 2) && array.CustomModifiers.IsEmpty &&
                    array.Sizes.IsEmpty && array.LowerBounds.IsDefault:
                var element = EncodeType(array.ElementType, array.ElementNullableAnnotation, depth + 1, signatureOwner);
                return element is null ? null : new ConstructedTypeIdentity(RecipeKind.Array, annotation)
                {
                    Element = element,
                    Rank = array.Rank,
                };
            case IDynamicTypeSymbol:
                return new(RecipeKind.Dynamic, annotation);
            case ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Type, ContainingType: { } owner } parameter:
                return new(RecipeKind.TypeParameter, annotation)
                {
                    Assembly = owner.ContainingAssembly.Identity.ToString(),
                    Chain = EncodeTypeChain(owner.OriginalDefinition, depth, includeArguments: false, signatureOwner),
                    Ordinal = parameter.Ordinal,
                };
            case ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method, DeclaringMethod: { } method } methodParameter:
                if (signatureOwner is not null)
                {
                    return SymbolEqualityComparer.Default.Equals(signatureOwner, method)
                        ? new(RecipeKind.MethodSignatureParameter, annotation) { Ordinal = methodParameter.Ordinal }
                        : null;
                }

                return EncodeMethodOwner(method, depth) is { } methodOwner
                    ? new ConstructedTypeIdentity(RecipeKind.MethodTypeParameter, annotation)
                    {
                        Assembly = method.ContainingAssembly.Identity.ToString(),
                        Chain = EncodeTypeChain(method.ContainingType, depth, includeArguments: false, signatureOwner: null),
                        Method = methodOwner,
                        Ordinal = methodParameter.Ordinal,
                    }
                    : null;
            default:
                return null;
        }
    }

    private static MethodOwnerIdentity? EncodeMethodOwner(IMethodSymbol method, int depth)
    {
        if (method.ContainingSymbol is not INamedTypeSymbol containing ||
            method.MethodKind is MethodKind.LocalFunction or MethodKind.AnonymousFunction or MethodKind.ReducedExtension ||
            !SymbolEqualityComparer.Default.Equals(method, method.OriginalDefinition) ||
            !SymbolEqualityComparer.Default.Equals(containing, containing.OriginalDefinition) ||
            !method.ReturnTypeCustomModifiers.IsEmpty || !method.RefCustomModifiers.IsEmpty ||
            !method.UnmanagedCallingConventionTypes.IsEmpty)
        {
            return null;
        }

        var signature = new MethodSignaturePart[method.Parameters.Length + 1];
        foreach (var parameter in method.Parameters)
        {
            if (!parameter.CustomModifiers.IsEmpty || !parameter.RefCustomModifiers.IsEmpty ||
                EncodeType(parameter.Type, parameter.NullableAnnotation, depth + 1, method) is not { } type)
            {
                return null;
            }
            signature[parameter.Ordinal] = new(parameter.RefKind, type);
        }
        if (EncodeType(method.ReturnType, method.ReturnNullableAnnotation, depth + 1, method) is not { } returnType)
        {
            return null;
        }
        signature[^1] = new(method.RefKind, returnType);

        var explicitInterfaces = new InterfaceMethodIdentity[method.ExplicitInterfaceImplementations.Length];
        for (var index = 0; index < explicitInterfaces.Length; index++)
        {
            var implemented = method.ExplicitInterfaceImplementations[index];
            if (ContainsMethodOwnedParameter(implemented.ContainingType) ||
                EncodeType(implemented.ContainingType, implemented.ContainingType.NullableAnnotation, depth + 1,
                    signatureOwner: null) is not { } interfaceType)
            {
                return null;
            }
            explicitInterfaces[index] = new(interfaceType, implemented.MetadataName);
        }
        return new(method.MetadataName, method.Arity, method.IsStatic, method.CallingConvention, method.MethodKind, signature,
            explicitInterfaces.OrderBy(identity => JsonSerializer.Serialize(identity), StringComparer.Ordinal).ToArray());
    }

    private static TypeLevel[]? EncodeTypeChain(
        INamedTypeSymbol type, int depth, bool includeArguments, IMethodSymbol? signatureOwner)
    {
        var levels = new Stack<TypeLevel>();
        for (INamedTypeSymbol? level = type; level is not null; level = level.ContainingType)
        {
            ConstructedTypeIdentity[]? arguments = null;
            if (includeArguments && level.Arity > 0 &&
                !SymbolEqualityComparer.IncludeNullability.Equals(level, level.ConstructedFrom))
            {
                arguments = new ConstructedTypeIdentity[level.Arity];
                for (var index = 0; index < arguments.Length; index++)
                {
                    if (!level.GetTypeArgumentCustomModifiers(index).IsEmpty ||
                        EncodeType(level.TypeArguments[index], level.TypeArgumentNullableAnnotations[index], depth + 1, signatureOwner) is not { } argument)
                    {
                        return null;
                    }
                    arguments[index] = argument;
                }
            }
            levels.Push(new(level.ContainingType is null ? GetTypeMetadataName(level) : level.MetadataName, arguments));
        }
        return levels.ToArray();
    }

    private static bool IsValidRecipe(ConstructedTypeIdentity? recipe, int depth, bool allowMethodOwner, int? selfArity)
    {
        if (recipe is null || depth > MaxConstructedTypeDepth || !Enum.IsDefined(recipe.Annotation) ||
            (recipe.Method is not null && recipe.Kind != RecipeKind.MethodTypeParameter))
        {
            return false;
        }

        var hasType = !string.IsNullOrWhiteSpace(recipe.Assembly) && recipe.Chain is { Length: > 0 };
        var noType = recipe.Assembly is null && recipe.Chain is null && recipe.TupleNames is null && !recipe.NativeInteger;
        var noArray = recipe.Element is null && recipe.Rank is null;
        return recipe.Kind switch
        {
            RecipeKind.Named => hasType && noArray && recipe.Ordinal is null &&
                (recipe.TupleNames is null || (recipe.TupleNames.Length > 0 && !recipe.NativeInteger &&
                    recipe.TupleNames.All(name => name is null || !string.IsNullOrWhiteSpace(name)))) &&
                recipe.Chain is { } levels && levels.All(level =>
                    !string.IsNullOrWhiteSpace(level?.Name) &&
                    (level.Arguments is null || (level.Arguments.Length > 0 &&
                        level.Arguments.All(argument => IsValidRecipe(argument, depth + 1, allowMethodOwner, selfArity))))),
            RecipeKind.Array => noType && recipe.Ordinal is null && recipe.Rank is >= 1 && IsValidRecipe(recipe.Element, depth + 1, allowMethodOwner, selfArity),
            RecipeKind.Dynamic => noType && noArray && recipe.Ordinal is null,
            RecipeKind.TypeParameter => hasType && noArray && recipe.Ordinal is >= 0 &&
                recipe.TupleNames is null && !recipe.NativeInteger && recipe.Chain is { } owner &&
                owner.All(level => !string.IsNullOrWhiteSpace(level?.Name) && level.Arguments is null),
            RecipeKind.MethodTypeParameter => allowMethodOwner && selfArity is null && hasType && noArray &&
                recipe.TupleNames is null && !recipe.NativeInteger && recipe.Ordinal is >= 0 &&
                recipe.Chain is { } methodOwner && methodOwner.All(level => !string.IsNullOrWhiteSpace(level?.Name) && level.Arguments is null) &&
                recipe.Method is { } method && recipe.Ordinal < method.Arity && IsValidMethodOwner(method, depth),
            RecipeKind.MethodSignatureParameter => noType && noArray && selfArity is { } arity &&
                recipe.Ordinal is >= 0 && recipe.Ordinal < arity,
            _ => false,
        };
    }

    private static bool IsValidMethodOwner(MethodOwnerIdentity method, int depth) =>
        !string.IsNullOrWhiteSpace(method.MetadataName) && method.Arity > 0 &&
        Enum.IsDefined(method.MethodKind) && Enum.IsDefined(method.CallingConvention) &&
        method.Signature is { Length: > 0 } && method.Signature.All(part =>
            part is not null && Enum.IsDefined(part.RefKind) &&
            IsValidRecipe(part.Type, depth + 1, allowMethodOwner: false, selfArity: method.Arity)) &&
        method.ExplicitInterfaces is not null && method.ExplicitInterfaces.All(member =>
            member is not null && !string.IsNullOrWhiteSpace(member.MetadataName) &&
            member.InterfaceType is { Kind: RecipeKind.Named } &&
            IsValidRecipe(member.InterfaceType, depth + 1, allowMethodOwner: false, selfArity: null));

    private static async Task<ISymbol?> ResolveConstructedTypeAsync(
        Solution solution, ConstructedTypeIdentity recipe, CancellationToken ct)
    {
        var canonicalRecipe = JsonSerializer.Serialize(recipe);
        (INamedTypeSymbol Symbol, Compilation Compilation)? result = null;
        foreach (var project in solution.Projects)
        {
            ct.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null)
            {
                continue;
            }
            var assemblies = new Dictionary<string, IAssemblySymbol?>(StringComparer.Ordinal);
            foreach (var assembly in compilation.References.Select(compilation.GetAssemblyOrModuleSymbol)
                .OfType<IAssemblySymbol>().Prepend(compilation.Assembly))
            {
                ct.ThrowIfCancellationRequested();
                var identity = assembly.Identity.ToString();
                if (!assemblies.TryAdd(identity, assembly) && !SymbolEqualityComparer.Default.Equals(assemblies[identity], assembly))
                {
                    assemblies[identity] = null;
                }
            }

            if (ResolveType(compilation, assemblies, recipe, ct) is not INamedTypeSymbol resolved)
            {
                continue;
            }
            // Compare the structural identity, including labels and annotations that display or symbol
            // equality alone can overlook. Never publish an approximate reconstruction.
            var encoded = EncodeType(resolved, resolved.NullableAnnotation, depth: 1, signatureOwner: null);
            if (encoded is null || !string.Equals(JsonSerializer.Serialize(encoded), canonicalRecipe, StringComparison.Ordinal))
            {
                return null;
            }
            if (result is { } previous && !HaveEquivalentDefinitions(previous.Symbol, previous.Compilation, resolved, compilation, ct))
            {
                return null;
            }
            result ??= (resolved, compilation);
        }
        return result?.Symbol;
    }

    private static bool HaveEquivalentDefinitions(ITypeSymbol left, Compilation leftCompilation,
        ITypeSymbol right, Compilation rightCompilation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        switch (left, right)
        {
            case (IArrayTypeSymbol leftArray, IArrayTypeSymbol rightArray):
                return HaveEquivalentDefinitions(leftArray.ElementType, leftCompilation, rightArray.ElementType, rightCompilation, ct);
            case (ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method, DeclaringMethod: { } leftMethod } leftParameter,
                  ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method, DeclaringMethod: { } rightMethod } rightParameter):
                return leftParameter.Ordinal == rightParameter.Ordinal &&
                    SameDefinition(leftMethod.ContainingType.OriginalDefinition, leftCompilation,
                        rightMethod.ContainingType.OriginalDefinition, rightCompilation, ct) &&
                    EncodeMethodOwner(leftMethod, depth: 1) is { } leftIdentity &&
                    EncodeMethodOwner(rightMethod, depth: 1) is { } rightIdentity &&
                    string.Equals(JsonSerializer.Serialize(leftIdentity), JsonSerializer.Serialize(rightIdentity), StringComparison.Ordinal);
            case (ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Type, ContainingType: { } leftOwner },
                  ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Type, ContainingType: { } rightOwner }):
                return SameDefinition(leftOwner.OriginalDefinition, leftCompilation, rightOwner.OriginalDefinition, rightCompilation, ct);
            case (INamedTypeSymbol leftNamed, INamedTypeSymbol rightNamed):
                if (!SameDefinition(leftNamed.OriginalDefinition, leftCompilation, rightNamed.OriginalDefinition, rightCompilation, ct))
                {
                    return false;
                }
                if (leftNamed.ContainingType is { } leftContaining && rightNamed.ContainingType is { } rightContaining &&
                    !HaveEquivalentDefinitions(leftContaining, leftCompilation, rightContaining, rightCompilation, ct))
                {
                    return false;
                }
                for (var index = 0; index < leftNamed.TypeArguments.Length; index++)
                {
                    if (!HaveEquivalentDefinitions(leftNamed.TypeArguments[index], leftCompilation,
                            rightNamed.TypeArguments[index], rightCompilation, ct))
                    {
                        return false;
                    }
                }
                return true;
            case (IDynamicTypeSymbol, IDynamicTypeSymbol):
                return true;
            default:
                return false;
        }
    }

    private static bool SameDefinition(INamedTypeSymbol left, Compilation leftCompilation,
        INamedTypeSymbol right, Compilation rightCompilation, CancellationToken ct)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        var leftSource = left.DeclaringSyntaxReferences;
        var rightSource = right.DeclaringSyntaxReferences;
        if (!leftSource.IsEmpty || !rightSource.IsEmpty)
        {
            // Ordinary project references and target frameworks may expose different Roslyn
            // symbols for the same source declaration. Independent declarations are ambiguous.
            return leftSource.Length == rightSource.Length &&
                OrderDeclarations(leftSource).Zip(OrderDeclarations(rightSource)).All(pair =>
                    !string.IsNullOrEmpty(pair.First.SyntaxTree.FilePath) &&
                    pair.First.SyntaxTree.FilePath == pair.Second.SyntaxTree.FilePath &&
                    pair.First.Span == pair.Second.Span &&
                    (ReferenceEquals(pair.First.SyntaxTree, pair.Second.SyntaxTree) ||
                        pair.First.GetSyntax(ct).IsEquivalentTo(pair.Second.GetSyntax(ct))));
        }

        if (leftCompilation.GetMetadataReference(left.ContainingAssembly) is not PortableExecutableReference leftReference ||
            rightCompilation.GetMetadataReference(right.ContainingAssembly) is not PortableExecutableReference rightReference)
        {
            return false;
        }
        if (ReferenceEquals(leftReference, rightReference))
        {
            return true;
        }
        // Equal CLR assembly names can describe distinct images in different compilations.
        using var leftMetadata = leftReference.GetMetadata();
        using var rightMetadata = rightReference.GetMetadata();
        return leftMetadata is AssemblyMetadata leftAssembly && rightMetadata is AssemblyMetadata rightAssembly
            && leftAssembly.GetModules().Select(module => module.GetModuleVersionId())
                .SequenceEqual(rightAssembly.GetModules().Select(module => module.GetModuleVersionId()));
    }

    private static IEnumerable<SyntaxReference> OrderDeclarations(
        ImmutableArray<SyntaxReference> declarations) =>
        declarations.OrderBy(reference => reference.SyntaxTree.FilePath, StringComparer.Ordinal)
            .ThenBy(reference => reference.Span.Start)
            .ThenBy(reference => reference.Span.Length);

    private static ITypeSymbol? ResolveType(Compilation compilation, Dictionary<string, IAssemblySymbol?> assemblies,
        ConstructedTypeIdentity recipe, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        switch (recipe.Kind)
        {
            case RecipeKind.Dynamic:
                return compilation.DynamicType.WithNullableAnnotation(recipe.Annotation);
            case RecipeKind.Array when recipe.Element is { } element && recipe.Rank is { } rank:
                return ResolveType(compilation, assemblies, element, ct) is { } elementType
                    ? compilation.CreateArrayTypeSymbol(elementType, rank, element.Annotation).WithNullableAnnotation(recipe.Annotation)
                    : null;
            case RecipeKind.Named or RecipeKind.TypeParameter or RecipeKind.MethodTypeParameter when recipe.Assembly is { } assemblyName &&
                assemblies.TryGetValue(assemblyName, out var assembly) && assembly is not null && recipe.Chain is { } chain:
                var named = ResolveTypeChain(compilation, assemblies, assembly, chain, ct);
                if (named is null)
                {
                    return null;
                }
                if (recipe.Kind == RecipeKind.TypeParameter)
                {
                    return recipe.Ordinal is { } ordinal && ordinal < named.TypeParameters.Length
                        ? named.TypeParameters[ordinal].WithNullableAnnotation(recipe.Annotation)
                        : null;
                }
                if (recipe.Kind == RecipeKind.MethodTypeParameter)
                {
                    return ResolveMethodParameter(named, recipe, ct);
                }
                if (recipe.NativeInteger)
                {
                    if (named.SpecialType is not (SpecialType.System_IntPtr or SpecialType.System_UIntPtr))
                    {
                        return null;
                    }
                    named = compilation.CreateNativeIntegerTypeSymbol(signed: named.SpecialType == SpecialType.System_IntPtr);
                }
                if (recipe.TupleNames is { } names)
                {
                    if (!named.IsTupleType || names.Length != named.TupleElements.Length)
                    {
                        return null;
                    }
                    named = compilation.CreateTupleTypeSymbol(named, names.ToImmutableArray(),
                        elementLocations: default,
                        elementNullableAnnotations: named.TupleElements.Select(element => element.NullableAnnotation).ToImmutableArray());
                }
                return named.WithNullableAnnotation(recipe.Annotation);
            default:
                return null;
        }
    }

    private static ITypeSymbol? ResolveMethodParameter(INamedTypeSymbol containingType, ConstructedTypeIdentity recipe, CancellationToken ct)
    {
        var owner = recipe.Method!;
        var identity = JsonSerializer.Serialize(owner);
        IMethodSymbol? match = null;
        foreach (var candidate in containingType.GetMembers().OfType<IMethodSymbol>())
        {
            ct.ThrowIfCancellationRequested();
            if (candidate.MetadataName != owner.MetadataName || candidate.Arity != owner.Arity ||
                candidate.IsStatic != owner.IsStatic || candidate.CallingConvention != owner.CallingConvention ||
                candidate.MethodKind != owner.MethodKind || candidate.Parameters.Length != owner.Signature.Length - 1 ||
                EncodeMethodOwner(candidate, depth: 1) is not { } candidateIdentity ||
                !string.Equals(JsonSerializer.Serialize(candidateIdentity), identity, StringComparison.Ordinal))
            {
                continue;
            }
            if (match is not null)
            {
                return null;
            }
            match = candidate;
        }
        return match?.TypeParameters[recipe.Ordinal!.Value].WithNullableAnnotation(recipe.Annotation);
    }

    private static INamedTypeSymbol? ResolveTypeChain(Compilation compilation, Dictionary<string, IAssemblySymbol?> assemblies,
        IAssemblySymbol assembly, TypeLevel[] chain, CancellationToken ct)
    {
        INamedTypeSymbol? current = null;
        for (var index = 0; index < chain.Length; index++)
        {
            ct.ThrowIfCancellationRequested();
            var level = chain[index];
            if (current is null)
            {
                current = assembly.GetTypeByMetadataName(level.Name);
            }
            else
            {
                var matches = current.GetTypeMembers().Where(type => type.MetadataName == level.Name).Take(2).ToArray();
                current = matches.Length == 1 ? matches[0] : null;
            }
            if (current is null)
            {
                return null;
            }
            if (level.Arguments is not { } arguments)
            {
                continue;
            }
            if (arguments.Length != current.Arity)
            {
                return null;
            }
            var types = ImmutableArray.CreateBuilder<ITypeSymbol>(arguments.Length);
            foreach (var argument in arguments)
            {
                if (ResolveType(compilation, assemblies, argument, ct) is not { } type)
                {
                    return null;
                }
                types.Add(type);
            }
            current = current.Construct(types.MoveToImmutable(), arguments.Select(argument => argument.Annotation).ToImmutableArray());
        }
        return current;
    }

    private static string GetTypeMetadataName(INamedTypeSymbol type)
    {
        var prefix = type.ContainingType is not null
            ? GetTypeMetadataName(type.ContainingType) + "+"
            : type.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : GetNamespaceMetadataName(type.ContainingNamespace) + ".";
        return prefix + type.MetadataName;
    }

    private static string GetNamespaceMetadataName(INamespaceSymbol ns) =>
        ns.ContainingNamespace.IsGlobalNamespace
            ? ns.MetadataName
            : GetNamespaceMetadataName(ns.ContainingNamespace) + "." + ns.MetadataName;

    private static async Task<ISymbol?> ResolveMetadataMemberAsync(
        Solution solution, MemberIdentity identity, CancellationToken ct)
    {
        ISymbol? result = null;
        foreach (var project in solution.Projects)
        {
            ct.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;

            foreach (var assembly in compilation.References
                .Select(compilation.GetAssemblyOrModuleSymbol)
                .OfType<IAssemblySymbol>()
                .Prepend(compilation.Assembly))
            {
                ct.ThrowIfCancellationRequested();
                if (!string.Equals(assembly.Identity.ToString(), identity.AssemblyIdentity, StringComparison.Ordinal))
                    continue;

                var type = assembly.GetTypeByMetadataName(identity.ContainingTypeMetadataName);
                if (type is null) continue;

                var matches = type.GetMembers()
                    .Where(member => string.Equals(
                        DocumentationCommentId.CreateDeclarationId(member),
                        identity.DeclarationId,
                        StringComparison.Ordinal))
                    .ToArray();
                if (matches.Length > 1) return null;
                if (matches.Length == 1)
                {
                    if (result is not null &&
                        !string.Equals(result.ToDisplayString(), matches[0].ToDisplayString(), StringComparison.Ordinal))
                        return null;
                    result ??= matches[0];
                }
            }
        }

        return result;
    }

    /// <summary>
    /// elicit-disambiguation-on-multi-symbol-resolve: builds a short, descriptive label that
    /// disambiguates one candidate from N siblings in a select-from-N elicitation prompt
    /// (or the additive disambiguation-list response when elicitation is unavailable).
    /// Format: <c>"&lt;Kind&gt; &lt;DisplayString&gt; — &lt;file&gt;:&lt;line&gt;"</c> when a source location
    /// exists; the file path is normalized to <c>Path.GetFileName</c> so we never leak
    /// absolute paths from internal-visibility code into the prompt the user sees.
    /// </summary>
    /// <remarks>
    /// The type-and-member display string mirrors candidate metadata already returned to the
    /// same caller; Roslyn display formatting is not an accessibility boundary. The absolute
    /// source path is the additional locally identifying value, so only its basename is emitted.
    /// </remarks>
    public static string BuildDisplayLabel(ISymbol symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);

        var kind = symbol.Kind.ToString();
        var display = symbol.ToDisplayString();
        var loc = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (loc is null)
        {
            return $"{kind} {display}";
        }

        var lineSpan = loc.GetLineSpan();
        var fileBase = string.IsNullOrEmpty(lineSpan.Path)
            ? null
            : Path.GetFileName(lineSpan.Path);
        var lineNumber = lineSpan.StartLinePosition.Line + 1;
        return string.IsNullOrEmpty(fileBase)
            ? $"{kind} {display}"
            : $"{kind} {display} — {fileBase}:{lineNumber}";
    }

    /// <summary>
    /// elicit-disambiguation-on-multi-symbol-resolve: returns ALL symbols across the solution
    /// that match the given <paramref name="metadataName"/> — types via
    /// <see cref="Compilation.GetTypeByMetadataName"/> AND members via the trailing-dot split
    /// (each <c>GetMembers(name)</c> overload contributes every overload). Results are
    /// deduped by the stable <c>symbolHandle</c> clients re-submit so the same symbol seen
    /// through multiple <c>Compilation</c>s or assemblies does not produce an ambiguity the
    /// caller cannot resolve.
    /// </summary>
    /// <remarks>
    /// This is the multi-result counterpart of
    /// <see cref="SymbolResolver.ResolveByMetadataNameAsync"/>. Callers who need exactly one
    /// symbol should keep using the single-result variant; callers that want to detect
    /// ambiguity (overload set, partial-class siblings, member-vs-type collisions) ahead of
    /// elicitation should call this and inspect <c>Count &gt; 1</c>.
    /// </remarks>
    /// <param name="compilationCache">
    /// compilation-cache-adoption-read-side: optional shared compilation cache. When supplied together
    /// with <paramref name="workspaceId"/> AND <paramref name="solution"/> is the live workspace
    /// solution, the per-project compilation fetch is routed through it. Omitted (the default) or a
    /// forked solution takes the raw fetch — see
    /// <see cref="SymbolResolver.CanUseCompilationCache"/>.
    /// </param>
    /// <param name="workspaceId">
    /// The workspace session identifier, required to key the cache. Mandatory whenever
    /// <paramref name="compilationCache"/> is supplied — the half-configured combination throws
    /// <see cref="System.ArgumentException"/> instead of silently taking the uncached path.
    /// </param>
    /// <exception cref="System.ArgumentException">
    /// Thrown when <paramref name="compilationCache"/> is supplied without a <paramref name="workspaceId"/>.
    /// </exception>
    public static async Task<IReadOnlyList<ISymbol>> FindAllByMetadataNameAsync(
        Solution solution,
        string metadataName,
        CancellationToken ct,
        ICompilationCache? compilationCache = null,
        string? workspaceId = null)
    {
        ArgumentNullException.ThrowIfNull(solution);
        SymbolResolver.ValidateCacheParameters(compilationCache, workspaceId);
        if (string.IsNullOrWhiteSpace(metadataName)) return Array.Empty<ISymbol>();

        var matches = new List<ISymbol>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var project in solution.Projects)
        {
            ct.ThrowIfCancellationRequested();
            var compilation = await SymbolResolver.GetProjectCompilationAsync(
                solution, project, compilationCache, workspaceId, ct).ConfigureAwait(false);
            if (compilation is null) continue;

            // Type-by-full-name fast path — collect, do not return early.
            var typeSymbol = compilation.GetTypeByMetadataName(metadataName);
            if (typeSymbol is not null)
            {
                AddCandidateIfNew(matches, seen, typeSymbol);
            }

            // Member fallback: split at the LAST dot. All overloads of the named member
            // contribute to the candidate set — that's exactly the disambiguation case the
            // initiative targets (e.g. method overloads of a single name).
            var lastDot = metadataName.LastIndexOf('.');
            if (lastDot <= 0 || lastDot == metadataName.Length - 1) continue;

            var containingTypeName = metadataName[..lastDot];
            var memberName = metadataName[(lastDot + 1)..];
            var containingType = compilation.GetTypeByMetadataName(containingTypeName);
            if (containingType is null) continue;

            foreach (var member in containingType.GetMembers(memberName))
            {
                AddCandidateIfNew(matches, seen, member);
            }
        }

        return matches;
    }

    private static void AddCandidateIfNew(List<ISymbol> matches, HashSet<string> seen, ISymbol symbol)
    {
        if (seen.Add(CreateHandle(symbol)))
        {
            matches.Add(symbol);
        }
    }
}
