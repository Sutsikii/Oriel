using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Oriel.Generators;

/// <summary>
/// Reads the XML documentation of symbols as plain text for TSDoc. Projects
/// rarely turn on GenerateDocumentationFile, which is what makes Roslyn parse
/// doc comments, so for source symbols the comment is read from the syntax.
/// </summary>
internal static class DocComments
{
    public static Doc Of(ISymbol symbol, CancellationToken cancellation)
    {
        var xml = FromSyntax(symbol, cancellation) ?? symbol.GetDocumentationCommentXml(cancellationToken: cancellation);
        return string.IsNullOrWhiteSpace(xml) ? Doc.None : Parse(xml!);
    }

    /// <summary>Parses the XML of a doc comment, with or without its root element.</summary>
    public static Doc Parse(string xml)
    {
        XElement root;
        try
        {
            root = XElement.Parse("<doc>" + xml + "</doc>", LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return Doc.None;
        }
        if (root.Elements().Count() == 1 && root.Elements("member").Any())
            root = root.Element("member")!;

        var parameters = root.Elements("param")
            .Select(p => new DocParam((string?)p.Attribute("name") ?? "", Text(p)))
            .Where(p => p.Name.Length > 0 && p.Text.Length > 0)
            .ToEquatableArray();
        return new Doc(Text(root.Element("summary")), Text(root.Element("returns")), parameters);
    }

    private static string? FromSyntax(ISymbol symbol, CancellationToken cancellation)
    {
        var reference = symbol.DeclaringSyntaxReferences.FirstOrDefault();
        if (reference is null)
            return null;
        var node = reference.GetSyntax(cancellation);
        // Fields of enums and variables are declared in a node of their own;
        // their comment belongs to it.
        if (node is Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax)
            node = node.Parent?.Parent ?? node;

        var sb = new StringBuilder();
        foreach (var trivia in node.GetLeadingTrivia())
        {
            if (trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia))
            {
                foreach (var line in trivia.ToFullString().Split('\n'))
                    AppendLine(sb, line);
            }
            else if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia))
            {
                AppendLine(sb, trivia.ToString());
            }
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    private static void AppendLine(StringBuilder sb, string line)
    {
        var text = line.Trim();
        if (!text.StartsWith("///", StringComparison.Ordinal))
            return;
        text = text.Substring(3);
        if (text.StartsWith(" ", StringComparison.Ordinal))
            text = text.Substring(1);
        sb.Append(text).Append('\n');
    }

    /// <summary>The text of an element: references become their names, paragraphs blank lines.</summary>
    private static string Text(XElement? element)
    {
        if (element is null)
            return "";
        var sb = new StringBuilder();
        Append(sb, element);
        return Normalize(sb.ToString());
    }

    private static void Append(StringBuilder sb, XElement element)
    {
        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XText text:
                    sb.Append(text.Value);
                    break;
                case XElement e when e.Name == "see" || e.Name == "seealso":
                    var target = (string?)e.Attribute("langword") ?? CrefName((string?)e.Attribute("cref") ?? (string?)e.Attribute("href") ?? "");
                    sb.Append(e.IsEmpty ? target : e.Value);
                    break;
                case XElement e when e.Name == "paramref" || e.Name == "typeparamref":
                    sb.Append((string?)e.Attribute("name"));
                    break;
                case XElement e when e.Name == "c":
                    sb.Append('`').Append(e.Value).Append('`');
                    break;
                case XElement e when e.Name == "para":
                    sb.Append("\n\n");
                    Append(sb, e);
                    sb.Append("\n\n");
                    break;
                case XElement e:
                    Append(sb, e);
                    break;
            }
        }
    }

    /// <summary><c>T:App.Deck</c> and <c>App.Deck.Title</c> both name <c>Title</c>'s last part.</summary>
    private static string CrefName(string cref)
    {
        if (cref.Length > 1 && cref[1] == ':')
            cref = cref.Substring(2);
        var paren = cref.IndexOf('(');
        if (paren >= 0)
            cref = cref.Substring(0, paren);
        var dot = cref.LastIndexOf('.');
        return dot >= 0 ? cref.Substring(dot + 1) : cref;
    }

    /// <summary>Joins wrapped lines, keeping blank lines between paragraphs.</summary>
    private static string Normalize(string text)
    {
        var paragraphs = text.Replace("\r", "")
            .Split(new[] { "\n\n" }, StringSplitOptions.None)
            .Select(p => string.Join(" ", p.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)))
            .Where(p => p.Length > 0);
        return string.Join("\n\n", paragraphs);
    }
}
