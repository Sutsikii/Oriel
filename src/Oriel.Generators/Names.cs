using System.Text;

namespace Oriel.Generators;

internal static class Names
{
    /// <summary>
    /// The name JavaScript code uses for a C# member: <c>Greet</c> becomes
    /// <c>greet</c>, <c>URLPath</c> becomes <c>urlPath</c>, as System.Text.Json's
    /// camel case policy does.
    /// </summary>
    public static string Camel(string name)
    {
        if (name.Length == 0 || !char.IsUpper(name[0]))
            return name;
        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (i == 1 && !char.IsUpper(chars[i]))
                break;
            var hasNext = i + 1 < chars.Length;
            // The last capital of a run starts the next word: "URLPath" keeps the P.
            if (i > 0 && hasNext && !char.IsUpper(chars[i + 1]))
            {
                if (char.IsSeparator(chars[i + 1]))
                    chars[i] = char.ToLowerInvariant(chars[i]);
                break;
            }
            chars[i] = char.ToLowerInvariant(chars[i]);
        }
        return new string(chars);
    }

    /// <summary>A C# identifier made of <paramref name="name"/>: dots and the like become underscores.</summary>
    public static string Identifier(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return sb.ToString();
    }

    /// <summary><paramref name="s"/> as a C# string literal.</summary>
    public static string Literal(string s) =>
        Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(s, quote: true);
}
