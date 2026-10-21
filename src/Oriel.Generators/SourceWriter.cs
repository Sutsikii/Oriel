using System.Text;

namespace Oriel.Generators;

/// <summary>Builds indented source text, C# or TypeScript.</summary>
internal sealed class SourceWriter
{
    private readonly StringBuilder _sb = new();
    private readonly string _indentUnit;
    private int _depth;

    public SourceWriter(string indentUnit = "    ")
    {
        _indentUnit = indentUnit;
    }

    public SourceWriter Line(string text = "")
    {
        if (text.Length > 0)
        {
            for (var i = 0; i < _depth; i++)
                _sb.Append(_indentUnit);
            _sb.Append(text);
        }
        _sb.Append('\n');
        return this;
    }

    /// <summary>Writes <paramref name="header"/> and an opening brace, and indents until the block is disposed.</summary>
    public IDisposable Block(string header, string close = "}")
    {
        Line(header);
        Line("{");
        _depth++;
        return new Closer(this, close);
    }

    public IDisposable Indent()
    {
        _depth++;
        return new Closer(this, null);
    }

    public override string ToString() => _sb.ToString();

    private sealed class Closer : IDisposable
    {
        private readonly SourceWriter _writer;
        private readonly string? _close;

        public Closer(SourceWriter writer, string? close)
        {
            _writer = writer;
            _close = close;
        }

        public void Dispose()
        {
            _writer._depth--;
            if (_close is not null)
                _writer.Line(_close);
        }
    }
}
