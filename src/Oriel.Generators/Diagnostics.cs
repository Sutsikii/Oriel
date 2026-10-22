using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Oriel.Generators;

/// <summary>
/// The errors the generator reports. Each id is documented in
/// docs/diagnostics.md; keep the two in step.
/// </summary>
internal static class Diagnostics
{
    private const string Category = "Oriel";

    public static readonly DiagnosticDescriptor UnsupportedType = new(
        "ORIEL001",
        "Type not supported by Oriel",
        "{0} cannot be sent between C# and pages: {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidService = new(
        "ORIEL002",
        "Class cannot be a service",
        "{0} cannot be a service: {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateName = new(
        "ORIEL003",
        "Two definitions share a name in TypeScript",
        "{0}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor Overload = new(
        "ORIEL004",
        "Overloaded service method",
        "{0}.{1} is overloaded, which TypeScript calls cannot tell apart: rename one, or hide it with [ServiceIgnore]",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidMethod = new(
        "ORIEL005",
        "Method cannot be called by pages",
        "{0}.{1} cannot be called by pages: {2}; hide it with [ServiceIgnore]",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidError = new(
        "ORIEL006",
        "Class cannot be a service error",
        "{0} cannot be a service error: {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}

/// <summary>A diagnostic held by value, for the incremental pipeline.</summary>
internal sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo? Location, EquatableArray<string> Arguments)
{
    public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, ISymbol? at, params string[] arguments) =>
        new(descriptor, LocationInfo.From(at), arguments.ToEquatableArray());

    public Diagnostic ToDiagnostic() =>
        Diagnostic.Create(Descriptor, Location?.ToLocation(), Arguments.Cast<object>().ToArray());
}

internal sealed record LocationInfo(string Path, TextSpan Span, LinePositionSpan Lines)
{
    public static LocationInfo? From(ISymbol? symbol)
    {
        var location = symbol?.Locations.FirstOrDefault(l => l.IsInSource);
        if (location?.SourceTree is null)
            return null;
        return new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
    }

    public Location ToLocation() => Location.Create(Path, Span, Lines);
}
