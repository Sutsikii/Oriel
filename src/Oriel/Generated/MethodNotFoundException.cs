namespace Oriel.Generated;

/// <summary>A page called a method that the service does not expose.</summary>
public sealed class MethodNotFoundException : Exception
{
    /// <summary>Creates the exception for <paramref name="method"/> of <paramref name="service"/>.</summary>
    public MethodNotFoundException(string service, string method)
        : base($"{service} has no method {method}")
    {
    }
}
