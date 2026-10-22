using Microsoft.CodeAnalysis;

namespace Oriel.Generators;

/// <summary>Reads a <c>[ServiceError]</c> exception into an <see cref="ErrorResult"/>.</summary>
internal static class ErrorParser
{
    public const string ServiceErrorAttribute = "Oriel.ServiceErrorAttribute";

    public static ErrorResult Parse(GeneratorAttributeSyntaxContext context, CancellationToken cancellation)
    {
        var type = (INamedTypeSymbol)context.TargetSymbol;
        var diagnostics = new List<DiagnosticInfo>();
        var mapper = new TypeMapper(diagnostics, cancellation);
        var error = ParseError(type, context.Attributes[0], mapper, diagnostics, cancellation);
        return new ErrorResult(
            error,
            mapper.Enums.ToEquatableArray(),
            mapper.Objects.ToEquatableArray(),
            diagnostics.ToEquatableArray());
    }

    private static ErrorDef? ParseError(INamedTypeSymbol type, AttributeData attribute, TypeMapper mapper, List<DiagnosticInfo> diagnostics, CancellationToken cancellation)
    {
        var code = attribute.ConstructorArguments.Length == 1 ? attribute.ConstructorArguments[0].Value as string : null;
        var problem = type switch
        {
            _ when !IsException(type) => "it does not derive from System.Exception",
            { IsGenericType: true } => "it is generic",
            _ when !ServiceParser.IsAccessible(type) => "it is not accessible to generated code; make it public or internal",
            _ when string.IsNullOrWhiteSpace(code) => "its code is empty",
            _ => null,
        };
        if (problem is not null)
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.InvalidError, type, type.Name, problem));
            return null;
        }

        var properties = mapper.ErrorProperties(type);
        if (properties is null)
            return null;
        return new ErrorDef(
            Names.Identifier(type.ToDisplayString()),
            TypeMapper.CSharpName(type),
            code!,
            type.Name,
            DocComments.Of(type, cancellation),
            properties.ToEquatableArray());
    }

    private static bool IsException(INamedTypeSymbol type)
    {
        for (var t = type.BaseType; t is not null; t = t.BaseType)
        {
            if (t.ToDisplayString() == "System.Exception")
                return true;
        }
        return false;
    }
}
