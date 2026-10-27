namespace Oriel.Generators;

// The generator's model of a project's services: plain values with value
// equality, so the incremental pipeline can tell when nothing changed. The
// parser builds it from Roslyn symbols; the emitters read only this.

/// <summary>How a C# type maps to JSON and TypeScript.</summary>
internal enum TypeKind
{
    /// <summary>A built-in type that <c>Oriel.Generated.Json</c> reads and writes.</summary>
    Primitive,

    /// <summary>An enum, as a union of strings.</summary>
    Enum,

    /// <summary>A class, record or struct, as an object of its properties.</summary>
    Object,

    /// <summary><c>T?</c> of a value type: <c>T | null</c>.</summary>
    NullableValue,

    /// <summary><c>T?</c> of a reference type: <c>T | null</c>.</summary>
    NullableReference,

    /// <summary><c>T[]</c>.</summary>
    Array,

    /// <summary><c>List&lt;T&gt;</c> and the interfaces it implements, read as a <c>List&lt;T&gt;</c>.</summary>
    List,

    /// <summary>A dictionary with string keys: <c>Record&lt;string, T&gt;</c>.</summary>
    Dictionary,

    /// <summary>
    /// <c>ReadOnlyMemory&lt;T&gt;</c> or <c>Memory&lt;T&gt;</c> of numbers, as the
    /// result of a method: a typed array, sent as bytes rather than JSON.
    /// <c>CSharp</c> is the element type, <c>TypeScript</c> the typed array.
    /// </summary>
    Binary,
}

/// <summary>A type as it appears in a method, a property or an event.</summary>
/// <param name="Kind">How it maps.</param>
/// <param name="CSharp">Its fully qualified C# name, as generated code writes it.</param>
/// <param name="Name">
/// For a primitive, the suffix of its <c>Json.Read*</c> method; for an enum or
/// an object, the id of its definition.
/// </param>
/// <param name="TypeScript">For a primitive, its TypeScript type.</param>
/// <param name="Element">The type a nullable, array, list or dictionary holds.</param>
internal sealed record TypeRef(TypeKind Kind, string CSharp, string Name, string TypeScript, TypeRef? Element)
{
    public static TypeRef Primitive(string csharp, string name, string typeScript) =>
        new(TypeKind.Primitive, csharp, name, typeScript, null);
}

/// <summary>Documentation from XML comments, as plain text.</summary>
internal sealed record Doc(string Summary, string Returns, EquatableArray<DocParam> Params)
{
    public static readonly Doc None = new("", "", EquatableArray<DocParam>.Empty);

    public string Param(string name)
    {
        foreach (var p in Params)
        {
            if (p.Name == name)
                return p.Text;
        }
        return "";
    }
}

internal sealed record DocParam(string Name, string Text);

/// <summary>An enum: its members by their JSON names.</summary>
internal sealed record EnumDef(string Id, string CSharp, string TypeScript, Doc Doc, EquatableArray<EnumMember> Members);

internal sealed record EnumMember(string CSharp, string Json, Doc Doc);

/// <summary>How a property gets its value when an object is read.</summary>
internal enum Assign
{
    /// <summary>It cannot: it is read-only, so it is only written.</summary>
    None,

    /// <summary>Through the constructor's parameter of the same name.</summary>
    Constructor,

    /// <summary>Through its public setter.</summary>
    Setter,

    /// <summary>
    /// Through its <c>init</c> or non-public setter, called with an
    /// <c>UnsafeAccessor</c>, which needs no reflection.
    /// </summary>
    Accessor,
}

/// <param name="Declared">The C# type it is declared with, which accessors must name exactly.</param>
internal sealed record PropertyDef(string CSharp, string Json, TypeRef Type, string Declared, Doc Doc, Assign Assign, bool IsRequired);

/// <summary>A parameter of the constructor objects are read with.</summary>
/// <param name="Property">The index of the property it takes.</param>
/// <param name="Declared">The C# type it is declared with.</param>
/// <param name="Default">The C# expression of its default value, or null when it has none.</param>
internal sealed record ConstructorParam(string Name, int Property, string Declared, string? Default);

/// <summary>A class, record or struct.</summary>
/// <param name="UnsafeConstructor">
/// Whether it is created through an <c>UnsafeAccessor</c>, which C# needs for
/// types with required members when the constructor does not set them.
/// </param>
internal sealed record ObjectDef(
    string Id,
    string CSharp,
    string TypeScript,
    Doc Doc,
    bool IsValueType,
    EquatableArray<PropertyDef> Properties,
    EquatableArray<ConstructorParam> Constructor,
    bool UnsafeConstructor);

/// <summary>A parameter of a service method.</summary>
/// <param name="Type">Its type, or null when Oriel supplies it rather than the page.</param>
/// <param name="Injected">What Oriel supplies: the C# expression, from the call's context.</param>
/// <param name="Default">
/// The C# expression of the value it takes when the page leaves it out, or
/// null when the page must pass it.
/// </param>
internal sealed record ParamDef(string Name, TypeRef? Type, string? Injected, string? Default)
{
    public bool IsOptional => Default is not null;
}

/// <summary>How a method returns.</summary>
internal enum Returns
{
    Void,
    Value,
    Task,
    TaskOfValue,

    /// <summary><c>IAsyncEnumerable&lt;T&gt;</c>: items the page iterates; the result is the item type.</summary>
    Stream,
}

internal sealed record MethodDef(string CSharp, string TypeScript, Doc Doc, EquatableArray<ParamDef> Params, Returns Returns, TypeRef? Result);

/// <summary>A <c>[Service]</c> class.</summary>
/// <param name="Name">The name pages call it by.</param>
/// <param name="Concurrency">The member of <c>ServiceConcurrency</c> its calls run by: <c>Parallel</c>, <c>Serial</c> or <c>UiThread</c>.</param>
internal sealed record ServiceDef(string CSharp, string Name, string Concurrency, Doc Doc, EquatableArray<MethodDef> Methods, EquatableArray<EventDef> Events);

/// <summary>An <c>Event&lt;T&gt;</c> property of a service.</summary>
/// <param name="CSharp">The name of the property.</param>
/// <param name="TypeScript">The name of the function that listens to it: <c>onTick</c>.</param>
internal sealed record EventDef(string CSharp, string TypeScript, Doc Doc, TypeRef Payload);

/// <summary>Everything one <c>[Service]</c> class brings: itself, the types it uses and its errors.</summary>
internal sealed record ServiceResult(
    ServiceDef? Service,
    EquatableArray<EnumDef> Enums,
    EquatableArray<ObjectDef> Objects,
    EquatableArray<DiagnosticInfo> Diagnostics);

/// <summary>A <c>[ServiceError]</c> exception: its code and the properties that make its data.</summary>
/// <param name="TypeScript">The name of the interface of its data.</param>
internal sealed record ErrorDef(string Id, string CSharp, string Code, string TypeScript, Doc Doc, EquatableArray<PropertyDef> Properties);

/// <summary>Everything one <c>[ServiceError]</c> exception brings: itself and the types of its properties.</summary>
internal sealed record ErrorResult(
    ErrorDef? Error,
    EquatableArray<EnumDef> Enums,
    EquatableArray<ObjectDef> Objects,
    EquatableArray<DiagnosticInfo> Diagnostics);
