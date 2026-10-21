namespace Oriel.Generated;

/// <summary>
/// A JSON value does not fit the C# type it is read into. The message says
/// where, as a path from the argument:
/// <c>argument 'deck': $.slides[2].title: expected a string, got a number</c>.
/// </summary>
public sealed class CodecException : Exception
{
    private readonly string _reason;
    private readonly string _path;

    /// <summary>Creates the exception for a value at the root of what is being read.</summary>
    public CodecException(string reason)
        : this(reason, "")
    {
    }

    private CodecException(string reason, string path)
        : base(path.Length == 0 ? reason : $"${path}: {reason}")
    {
        _reason = reason;
        _path = path;
    }

    /// <summary>The same error, for a value one level deeper: under <paramref name="segment"/>.</summary>
    /// <param name="segment">A property (<c>.name</c>) or an index (<c>[2]</c>).</param>
    public CodecException Under(string segment) => new(_reason, segment + _path);
}
