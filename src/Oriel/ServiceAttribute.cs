namespace Oriel;

/// <summary>
/// Exposes the public instance methods of a class to the pages of the app.
/// The source generator writes the class's dispatcher and its part of the
/// TypeScript client; bind an instance with <c>OrielApp.Bind</c>.
/// </summary>
/// <example>
/// <code>
/// [Service]
/// public class Greeter
/// {
///     /// &lt;summary&gt;Returns a greeting.&lt;/summary&gt;
///     public string Greet(string name) =&gt; $"Hello, {name}";
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ServiceAttribute : Attribute
{
    /// <summary>
    /// The name pages call the service by. Defaults to the name of the class.
    /// </summary>
    public string? Name { get; set; }
}

/// <summary>Keeps a public method of a <see cref="ServiceAttribute"/> class from pages.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class ServiceIgnoreAttribute : Attribute
{
}
