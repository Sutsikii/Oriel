using Microsoft.CodeAnalysis;

namespace Oriel.Generators;

/// <summary>Reads a <c>[Service]</c> class into a <see cref="ServiceResult"/>.</summary>
internal static class ServiceParser
{
    public const string ServiceAttribute = "Oriel.ServiceAttribute";
    private const string IgnoreAttribute = "Oriel.ServiceIgnoreAttribute";
    private const string EventType = "Oriel.Event<T>";

    public static ServiceResult Parse(GeneratorAttributeSyntaxContext context, CancellationToken cancellation)
    {
        var type = (INamedTypeSymbol)context.TargetSymbol;
        var diagnostics = new List<DiagnosticInfo>();
        var mapper = new TypeMapper(diagnostics, cancellation);
        var service = ParseService(type, context.Attributes[0], mapper, diagnostics, cancellation);
        return new ServiceResult(
            service,
            mapper.Enums.ToEquatableArray(),
            mapper.Objects.ToEquatableArray(),
            diagnostics.ToEquatableArray());
    }

    private static ServiceDef? ParseService(INamedTypeSymbol type, AttributeData attribute, TypeMapper mapper, List<DiagnosticInfo> diagnostics, CancellationToken cancellation)
    {
        var problem = type switch
        {
            { IsStatic: true } => "it is static; services are instances, bound with OrielApp.Bind",
            { IsAbstract: true } => "it is abstract",
            { IsGenericType: true } => "it is generic",
            _ when !IsAccessible(type) => "it is not accessible to generated code; make it public or internal",
            _ => null,
        };
        var name = attribute.NamedArguments.FirstOrDefault(a => a.Key == "Name").Value.Value as string ?? type.Name;
        // The members of ServiceConcurrency, in order: the attribute holds the value.
        var concurrency = attribute.NamedArguments.FirstOrDefault(a => a.Key == "Concurrency").Value.Value switch
        {
            1 => "Serial",
            2 => "UiThread",
            _ => "Parallel",
        };
        if (problem is null && !IsName(name))
            problem = $"its name '{name}' is not an identifier";
        if (problem is not null)
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.InvalidService, type, type.Name, problem));
            return null;
        }

        var methods = new List<MethodDef>();
        var candidates = PublicMethods(type).ToList();
        foreach (var overloaded in candidates.GroupBy(m => m.Name).Where(g => g.Count() > 1))
        {
            foreach (var method in overloaded)
                diagnostics.Add(DiagnosticInfo.Create(Diagnostics.Overload, method, name, method.Name));
        }
        foreach (var method in candidates.GroupBy(m => m.Name).Where(g => g.Count() == 1).Select(g => g.First()))
        {
            var parsed = ParseMethod(name, method, mapper, diagnostics, cancellation);
            if (parsed is not null)
                methods.Add(parsed);
        }
        var events = new List<EventDef>();
        foreach (var property in Events(type))
        {
            var payload = mapper.Map(((INamedTypeSymbol)property.Type).TypeArguments[0], property, $"the payload of {name}.{property.Name}");
            if (payload is not null)
                events.Add(new EventDef(property.Name, "on" + property.Name, DocComments.Of(property, cancellation), payload));
        }

        return new ServiceDef(TypeMapper.CSharpName(type), name, concurrency, DocComments.Of(type, cancellation), methods.ToEquatableArray(), events.ToEquatableArray());
    }

    /// <summary>The public <c>Event&lt;T&gt;</c> properties of a service and its bases.</summary>
    private static IEnumerable<IPropertySymbol> Events(INamedTypeSymbol type)
    {
        var taken = new HashSet<string>();
        for (var t = type; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
        {
            foreach (var property in t.GetMembers().OfType<IPropertySymbol>())
            {
                if (!property.IsStatic && property.DeclaredAccessibility == Accessibility.Public
                    && property.GetMethod is not null
                    && property.Type.OriginalDefinition.ToDisplayString() == EventType
                    && taken.Add(property.Name))
                {
                    yield return property;
                }
            }
        }
    }

    /// <summary>
    /// The methods pages can call: public, of instances, declared by the class
    /// or its bases. A method of a derived class hides those of its name in
    /// the bases, overridden or not.
    /// </summary>
    private static IEnumerable<IMethodSymbol> PublicMethods(INamedTypeSymbol type)
    {
        var taken = new HashSet<string>();
        for (var t = type; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
        {
            var level = t.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(m => m.MethodKind == MethodKind.Ordinary && !m.IsStatic
                    && m.DeclaredAccessibility == Accessibility.Public
                    && !taken.Contains(m.Name))
                .ToList();
            foreach (var method in level)
            {
                taken.Add(method.Name);
                if (!method.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == IgnoreAttribute))
                    yield return method;
            }
        }
    }

    private static MethodDef? ParseMethod(string service, IMethodSymbol method, TypeMapper mapper, List<DiagnosticInfo> diagnostics, CancellationToken cancellation)
    {
        string? problem = null;
        if (method.IsGenericMethod)
            problem = "it is generic";
        else if (method.Parameters.FirstOrDefault(p => p.RefKind != RefKind.None) is { } byRef)
            problem = $"parameter '{byRef.Name}' is passed by reference";
        if (problem is not null)
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.InvalidMethod, method, service, method.Name, problem));
            return null;
        }

        var ok = true;
        var parameters = new List<ParamDef>();
        foreach (var parameter in method.Parameters)
        {
            var injected = Injected(parameter.Type);
            if (injected is not null)
            {
                parameters.Add(new ParamDef(parameter.Name, null, injected, null));
                continue;
            }
            var type = mapper.Map(parameter.Type, parameter, $"parameter '{parameter.Name}' of {service}.{method.Name}");
            if (type is null)
            {
                ok = false;
                continue;
            }
            var defaultValue = parameter.HasExplicitDefaultValue ? TypeMapper.DefaultValue(parameter)
                : parameter.IsParams ? "[]"
                : null;
            parameters.Add(new ParamDef(parameter.Name, type, null, defaultValue));
        }

        var (returns, result) = Result(method, mapper, $"{service}.{method.Name}", ref ok);
        if (!ok)
            return null;
        return new MethodDef(method.Name, Names.Camel(method.Name), DocComments.Of(method, cancellation), parameters.ToEquatableArray(), returns, result);
    }

    /// <summary>What a method returns once awaited.</summary>
    /// <param name="name">The method, as <c>Service.Method</c>.</param>
    private static (Returns, TypeRef?) Result(IMethodSymbol method, TypeMapper mapper, string name, ref bool ok)
    {
        if (method.ReturnsVoid)
            return (Returns.Void, null);
        var type = method.ReturnType;
        var definition = type.OriginalDefinition.ToDisplayString();
        if (definition is "System.Threading.Tasks.Task" or "System.Threading.Tasks.ValueTask")
            return (Returns.Task, null);
        var returns = Returns.Value;
        if (definition == "System.Collections.Generic.IAsyncEnumerable<T>")
        {
            type = ((INamedTypeSymbol)type).TypeArguments[0];
            returns = Returns.Stream;
        }
        else if (definition is "System.Threading.Tasks.Task<TResult>" or "System.Threading.Tasks.ValueTask<TResult>")
        {
            type = ((INamedTypeSymbol)type).TypeArguments[0];
            returns = Returns.TaskOfValue;
        }
        var mapped = returns == Returns.Stream
            ? mapper.Map(type, method, $"the items of {name}")
            : mapper.MapResult(type, method, $"the result of {name}");
        if (mapped is null)
            ok = false;
        return (returns, mapped);
    }

    /// <summary>The C# expression of what Oriel passes for a parameter of this type, or null.</summary>
    private static string? Injected(ITypeSymbol type) => type.ToDisplayString() switch
    {
        "System.Threading.CancellationToken" => "context.Cancellation",
        "Oriel.OrielWindow" => "context.Window!",
        _ => null,
    };

    internal static bool IsAccessible(INamedTypeSymbol type)
    {
        for (ISymbol? s = type; s is INamedTypeSymbol; s = s.ContainingType)
        {
            if (s.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal))
                return false;
        }
        return true;
    }

    private static bool IsName(string name) =>
        name.Length > 0 && (char.IsLetter(name[0]) || name[0] == '_') && name.All(c => char.IsLetterOrDigit(c) || c == '_');
}
