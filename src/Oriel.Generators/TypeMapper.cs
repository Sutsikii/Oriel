using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Oriel.Generators;

/// <summary>
/// Maps the C# types of a service's methods to <see cref="TypeRef"/>s, and
/// defines the enums and objects they use along the way. A type it cannot
/// map is reported, with the reason, where it is used.
/// </summary>
internal sealed class TypeMapper
{
    private static readonly SymbolDisplayFormat s_csharp = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    private readonly Dictionary<string, EnumDef> _enums = new();
    private readonly Dictionary<string, ObjectDef> _objects = new();
    private readonly HashSet<string> _mapping = new();
    private readonly HashSet<string> _failed = new();
    private readonly List<DiagnosticInfo> _diagnostics;
    private readonly CancellationToken _cancellation;

    public TypeMapper(List<DiagnosticInfo> diagnostics, CancellationToken cancellation)
    {
        _diagnostics = diagnostics;
        _cancellation = cancellation;
    }

    public IEnumerable<EnumDef> Enums => _enums.Values;

    public IEnumerable<ObjectDef> Objects => _objects.Values;

    /// <summary>
    /// Maps <paramref name="type"/>, or reports why it cannot be and returns null.
    /// </summary>
    /// <param name="at">Where to report errors when the type itself is not in source.</param>
    /// <param name="what">What has the type, for messages: <c>parameter 'deck' of Decks.Save</c>.</param>
    public TypeRef? Map(ITypeSymbol type, ISymbol at, string what)
    {
        _cancellation.ThrowIfCancellationRequested();
        var reason = "";
        var mapped = TryMap(type, at, what, ref reason);
        if (mapped is null && reason.Length > 0)
            _diagnostics.Add(DiagnosticInfo.Create(Diagnostics.UnsupportedType, at, what, reason));
        return mapped;
    }

    public static string CSharpName(ITypeSymbol type) =>
        type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(s_csharp);

    /// <summary>Maps a type; on failure sets <paramref name="reason"/>, or leaves it empty when an error was reported deeper.</summary>
    private TypeRef? TryMap(ITypeSymbol type, ISymbol at, string what, ref string reason)
    {
        if (type is IArrayTypeSymbol array)
        {
            if (array.Rank != 1)
            {
                reason = "multidimensional arrays are not supported; use an array of arrays";
                return null;
            }
            if (array.ElementType.SpecialType == SpecialType.System_Byte)
                return TypeRef.Primitive("byte[]", "Bytes", "string");
            var item = Map(array.ElementType, at, what);
            return item is null ? null : new TypeRef(TypeKind.Array, item.CSharp + "[]", "", "", item);
        }

        if (!type.IsValueType && type.NullableAnnotation == NullableAnnotation.Annotated)
        {
            var inner = TryMap(type.WithNullableAnnotation(NullableAnnotation.NotAnnotated), at, what, ref reason);
            return inner is null ? null : new TypeRef(TypeKind.NullableReference, inner.CSharp + "?", "", "", inner);
        }

        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            var inner = TryMap(nullable.TypeArguments[0], at, what, ref reason);
            return inner is null ? null : new TypeRef(TypeKind.NullableValue, inner.CSharp + "?", "", "", inner);
        }

        var primitive = Primitive(type);
        if (primitive is not null)
            return primitive;

        if (type is not INamedTypeSymbol named)
        {
            reason = type.TypeKind == Microsoft.CodeAnalysis.TypeKind.TypeParameter
                ? "type parameters are not supported"
                : $"{type.ToDisplayString()} is not a type Oriel maps";
            return null;
        }

        if (TryCollection(named, at, what, ref reason, out var collection))
            return collection;

        if (named.TypeKind == Microsoft.CodeAnalysis.TypeKind.Enum)
            return MapEnum(named, ref reason);

        return MapObject(named, at, ref reason);
    }

    private static TypeRef? Primitive(ITypeSymbol type)
    {
        var csharp = CSharpName(type);
        switch (type.SpecialType)
        {
            case SpecialType.System_String: return TypeRef.Primitive(csharp, "String", "string");
            case SpecialType.System_Boolean: return TypeRef.Primitive(csharp, "Boolean", "boolean");
            case SpecialType.System_Char: return TypeRef.Primitive(csharp, "Char", "string");
            case SpecialType.System_Byte: return TypeRef.Primitive(csharp, "Byte", "number");
            case SpecialType.System_SByte: return TypeRef.Primitive(csharp, "SByte", "number");
            case SpecialType.System_Int16: return TypeRef.Primitive(csharp, "Int16", "number");
            case SpecialType.System_UInt16: return TypeRef.Primitive(csharp, "UInt16", "number");
            case SpecialType.System_Int32: return TypeRef.Primitive(csharp, "Int32", "number");
            case SpecialType.System_UInt32: return TypeRef.Primitive(csharp, "UInt32", "number");
            case SpecialType.System_Int64: return TypeRef.Primitive(csharp, "Int64", "number");
            case SpecialType.System_UInt64: return TypeRef.Primitive(csharp, "UInt64", "number");
            case SpecialType.System_Single: return TypeRef.Primitive(csharp, "Single", "number");
            case SpecialType.System_Double: return TypeRef.Primitive(csharp, "Double", "number");
            case SpecialType.System_Decimal: return TypeRef.Primitive(csharp, "Decimal", "number");
            case SpecialType.System_DateTime: return TypeRef.Primitive(csharp, "DateTime", "string");
        }
        return csharp switch
        {
            "global::System.Guid" => TypeRef.Primitive(csharp, "Guid", "string"),
            "global::System.DateTimeOffset" => TypeRef.Primitive(csharp, "DateTimeOffset", "string"),
            "global::System.Text.Json.JsonElement" => TypeRef.Primitive(csharp, "Element", "unknown"),
            _ => null,
        };
    }

    /// <summary>
    /// Maps lists and dictionaries, and refuses the generic types of the base
    /// library Oriel does not map; returns false for other types.
    /// </summary>
    private bool TryCollection(INamedTypeSymbol type, ISymbol at, string what, ref string reason, out TypeRef? result)
    {
        result = null;
        if (type.IsTupleType)
        {
            reason = "tuples have no property names; use a record";
            return true;
        }
        if (!type.IsGenericType)
            return false;
        var definition = type.OriginalDefinition.ToDisplayString();
        switch (definition)
        {
            case "System.Collections.Generic.List<T>":
            case "System.Collections.Generic.IList<T>":
            case "System.Collections.Generic.ICollection<T>":
            case "System.Collections.Generic.IEnumerable<T>":
            case "System.Collections.Generic.IReadOnlyList<T>":
            case "System.Collections.Generic.IReadOnlyCollection<T>":
            {
                var item = Map(type.TypeArguments[0], at, what);
                if (item is not null)
                    result = new TypeRef(TypeKind.List, $"global::System.Collections.Generic.List<{item.CSharp}>", "", "", item);
                return true;
            }
            case "System.Collections.Generic.Dictionary<TKey, TValue>":
            case "System.Collections.Generic.IDictionary<TKey, TValue>":
            case "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>":
            {
                if (type.TypeArguments[0].SpecialType != SpecialType.System_String)
                {
                    reason = "dictionaries need string keys, as JSON objects have";
                    return true;
                }
                var value = Map(type.TypeArguments[1], at, what);
                if (value is not null)
                    result = new TypeRef(TypeKind.Dictionary, $"global::System.Collections.Generic.Dictionary<string, {value.CSharp}>", "", "", value);
                return true;
            }
        }
        if (definition.StartsWith("System.Collections.Generic.IAsyncEnumerable<", StringComparison.Ordinal))
            reason = "streams (IAsyncEnumerable) can only be returned, not nested";
        else if (definition.StartsWith("System.Tuple<", StringComparison.Ordinal))
            reason = "tuples have no property names; use a record";
        else if (definition.StartsWith("System.Threading.Tasks.", StringComparison.Ordinal))
            reason = "tasks can only be returned, not nested";
        return reason.Length > 0;
    }

    private TypeRef? MapEnum(INamedTypeSymbol type, ref string reason)
    {
        var csharp = CSharpName(type);
        var id = Names.Identifier(type.ToDisplayString());
        var reference = new TypeRef(TypeKind.Enum, csharp, id, "", null);
        if (_enums.ContainsKey(id))
            return reference;
        if (type.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.FlagsAttribute"))
        {
            reason = $"{type.Name} is a [Flags] enum, which has no single name per value";
            return null;
        }
        if (!IsAccessible(type))
        {
            reason = $"{type.Name} is not accessible to generated code; make it public or internal";
            return null;
        }

        // Aliases (two names of one value) keep the first name.
        var values = new HashSet<object?>();
        var members = type.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(f => f.HasConstantValue && values.Add(f.ConstantValue))
            .Select(f => new EnumMember(
                f.Name,
                StringArgument(f, "System.Text.Json.Serialization.JsonStringEnumMemberNameAttribute") ?? Names.Camel(f.Name),
                DocComments.Of(f, _cancellation)))
            .ToEquatableArray();
        _enums[id] = new EnumDef(id, csharp, type.Name, DocComments.Of(type, _cancellation), members);
        return reference;
    }

    private TypeRef? MapObject(INamedTypeSymbol type, ISymbol at, ref string reason)
    {
        var csharp = CSharpName(type);
        var id = Names.Identifier(type.ToDisplayString());
        var reference = new TypeRef(TypeKind.Object, csharp, id, "", null);
        if (_objects.ContainsKey(id) || _mapping.Contains(id))
            return reference; // defined, or being defined: a type that refers to itself
        if (_failed.Contains(id))
            return null; // reported where it was first used

        reason = Unmappable(type);
        if (reason.Length > 0)
            return null;

        _mapping.Add(id);
        try
        {
            if (DefineObject(type, at, id, csharp, ref reason))
                return reference;
            _failed.Add(id);
            return null;
        }
        finally
        {
            _mapping.Remove(id);
        }
    }

    /// <summary>Why a named type cannot be an object, or "".</summary>
    private static string Unmappable(INamedTypeSymbol type)
    {
        switch (type.TypeKind)
        {
            case Microsoft.CodeAnalysis.TypeKind.Interface:
                return $"{type.Name} is an interface; Oriel sends concrete classes, records and structs";
            case Microsoft.CodeAnalysis.TypeKind.Delegate:
                return "delegates cannot be sent";
            case Microsoft.CodeAnalysis.TypeKind.Class or Microsoft.CodeAnalysis.TypeKind.Struct:
                break;
            default:
                return $"{type.ToDisplayString()} is not a type Oriel maps";
        }
        var ns = type.ContainingNamespace?.ToDisplayString() ?? "";
        if (ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal) || ns.StartsWith("Microsoft.", StringComparison.Ordinal))
            return $"{type.ToDisplayString()} is not a type Oriel maps";
        if (type.IsGenericType)
            return $"{type.Name} is generic, which is not supported yet";
        if (type.IsAbstract)
            return $"{type.Name} is abstract; Oriel sends concrete classes, records and structs";
        if (type.IsRefLikeType)
            return $"{type.Name} is a ref struct";
        if (!IsAccessible(type))
            return $"{type.Name} is not accessible to generated code; make it public or internal";
        return "";
    }

    /// <summary>
    /// Maps the public properties of a <c>[ServiceError]</c> exception, those
    /// it adds to <see cref="Exception"/>, or reports why one cannot be and
    /// returns null.
    /// </summary>
    public List<PropertyDef>? ErrorProperties(INamedTypeSymbol type)
    {
        var properties = new List<PropertyDef>();
        return Properties(type, type, properties, [], stopAt: "System.Exception") ? properties : null;
    }

    private bool DefineObject(INamedTypeSymbol type, ISymbol at, string id, string csharp, ref string reason)
    {
        var properties = new List<PropertyDef>();
        var symbols = new List<IPropertySymbol>();
        if (!Properties(type, at, properties, symbols, stopAt: null))
            return false;

        var constructor = ChooseConstructor(type, ref reason);
        if (constructor is null)
            return false;

        var parameters = new List<ConstructorParam>();
        foreach (var parameter in constructor.Parameters)
        {
            var index = symbols.FindIndex(s => string.Equals(s.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                reason = $"parameter '{parameter.Name}' of the constructor of {type.Name} matches none of its public properties";
                return false;
            }
            properties[index] = properties[index] with { Assign = Assign.Constructor };
            parameters.Add(new ConstructorParam(parameter.Name, index, parameter.Type.ToDisplayString(s_csharp), parameter.HasExplicitDefaultValue ? DefaultValue(parameter) : null));
        }

        var unsafeConstructor = HasRequiredMembers(type) && !HasAttribute(constructor, "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute");
        _objects[id] = new ObjectDef(
            id,
            csharp,
            type.Name,
            DocComments.Of(type, _cancellation),
            type.IsValueType,
            properties.ToEquatableArray(),
            parameters.ToEquatableArray(),
            unsafeConstructor);
        return true;
    }

    /// <summary>
    /// Maps the public properties of <paramref name="type"/>, the most derived
    /// first, up to <paramref name="stopAt"/> (a base type whose own are left
    /// out) or to <see cref="object"/>. False when one cannot be mapped.
    /// </summary>
    private bool Properties(INamedTypeSymbol type, ISymbol at, List<PropertyDef> properties, List<IPropertySymbol> symbols, string? stopAt)
    {
        var ok = true;
        for (var t = type;
             t is not null && t.SpecialType is not (SpecialType.System_Object or SpecialType.System_ValueType) && t.ToDisplayString() != stopAt;
             t = t.BaseType)
        {
            foreach (var property in t.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.IsStatic || property.IsIndexer
                    || property.DeclaredAccessibility != Accessibility.Public
                    || property.GetMethod?.DeclaredAccessibility != Accessibility.Public
                    || symbols.Any(s => s.Name == property.Name)
                    || (stopAt is not null && property.IsOverride) // Message and the like, sent already
                    || HasAttribute(property, "System.Text.Json.Serialization.JsonIgnoreAttribute"))
                {
                    continue;
                }
                var mapped = Map(property.Type, property.Locations.Any(l => l.IsInSource) ? property : at, $"property {type.Name}.{property.Name}");
                if (mapped is null)
                {
                    ok = false;
                    continue;
                }
                var assign = property.SetMethod switch
                {
                    null => Assign.None,
                    { IsInitOnly: false, DeclaredAccessibility: Accessibility.Public } => Assign.Setter,
                    _ => Assign.Accessor,
                };
                symbols.Add(property);
                properties.Add(new PropertyDef(
                    property.Name,
                    StringArgument(property, "System.Text.Json.Serialization.JsonPropertyNameAttribute") ?? Names.Camel(property.Name),
                    mapped,
                    property.Type.ToDisplayString(s_csharp),
                    DocComments.Of(property, _cancellation),
                    assign,
                    property.IsRequired));
            }
        }
        return ok;
    }

    /// <summary>
    /// The constructor objects are read with: the one marked [JsonConstructor],
    /// else the parameterless one, else the only public one (a record's).
    /// </summary>
    private static IMethodSymbol? ChooseConstructor(INamedTypeSymbol type, ref string reason)
    {
        var constructors = type.InstanceConstructors.Where(c => c.DeclaredAccessibility == Accessibility.Public).ToList();
        var chosen = constructors.FirstOrDefault(c => HasAttribute(c, "System.Text.Json.Serialization.JsonConstructorAttribute"))
            ?? constructors.FirstOrDefault(c => c.Parameters.Length == 0)
            ?? (constructors.Count == 1 ? constructors[0] : null);
        if (chosen is null)
        {
            reason = constructors.Count == 0
                ? $"{type.Name} has no public constructor"
                : $"{type.Name} has several constructors; mark the one to read it with [JsonConstructor]";
        }
        return chosen;
    }

    private static bool HasRequiredMembers(INamedTypeSymbol type)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (t.GetMembers().Any(m => m is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true }))
                return true;
        }
        return false;
    }

    /// <summary>The C# expression of a parameter's default value.</summary>
    internal static string DefaultValue(IParameterSymbol parameter)
    {
        var value = parameter.ExplicitDefaultValue;
        if (value is null)
            return "default";
        var type = parameter.Type;
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            type = nullable.TypeArguments[0];
        return $"({CSharpName(type)}){SymbolDisplay.FormatPrimitive(value, quoteStrings: true, useHexadecimalNumbers: false)}";
    }

    private static bool IsAccessible(INamedTypeSymbol type)
    {
        for (ISymbol? s = type; s is INamedTypeSymbol; s = s.ContainingType)
        {
            if (s.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal))
                return false;
        }
        return true;
    }

    private static bool HasAttribute(ISymbol symbol, string name) =>
        symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == name);

    private static string? StringArgument(ISymbol symbol, string attribute) =>
        symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == attribute)
            ?.ConstructorArguments.FirstOrDefault().Value as string;
}
