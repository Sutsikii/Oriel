namespace Oriel;

/// <summary>
/// Makes an exception an error pages can tell apart: a call that throws it
/// rejects with a <c>CallError</c> whose <c>code</c> is <see cref="Code"/> and
/// whose <c>data</c> holds the exception's public properties.
/// </summary>
/// <example>
/// <code>
/// [ServiceError("conflict")]
/// public class ConflictException(string id) : Exception($"{id} was changed by someone else")
/// {
///     public string Id { get; } = id;
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ServiceErrorAttribute(string code) : Attribute
{
    /// <summary>The code pages tell the error by.</summary>
    public string Code { get; } = code;
}
