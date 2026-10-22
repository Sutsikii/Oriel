using System.Buffers;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Oriel.Generated;
using Oriel.Ipc;

namespace Oriel.Benchmarks;

public enum Theme { Light, Dark, HighContrast }

public sealed record Slide(string Title, Theme Theme, int? Rating, List<string> Notes);

public sealed record Deck(string Title, List<Slide> Slides, Dictionary<string, string> Meta);

/// <summary>What the benchmarks call: a call with little to read, and one with a document.</summary>
[Service]
public sealed class Bench
{
    public int Add(int a, int b) => a + b;

    public Deck Same(Deck deck) => deck;
}

/// <summary>
/// The cost of a call in the app, without the webview: reading arguments
/// and writing results with the generated codecs, the dispatcher's switch,
/// and the whole protocol, from a page's message to the reply.
/// </summary>
[MemoryDiagnoser]
public class CallBenchmarks
{
    private ServiceDispatcher _dispatcher = null!;
    private JsonDocument _addArgs = null!;
    private JsonDocument _deckArgs = null!;
    private PageConnection _page = null!;
    private string _addCall = "";
    private string _deckCall = "";
    private readonly CallContext _context = new(CancellationToken.None);
    private readonly ArrayBufferWriter<byte> _buffer = new();

    /// <summary>A deck of 20 slides, about 3 KB of JSON.</summary>
    public static string DeckJson { get; } = JsonSerializer.Serialize(new
    {
        title = "Quarterly review",
        slides = Enumerable.Range(1, 20).Select(i => new
        {
            title = $"Slide {i}: what happened, and what comes next",
            theme = i % 3 == 0 ? "highContrast" : "dark",
            rating = i % 4 == 0 ? (int?)null : i,
            notes = new[] { "Speak slowly", "Show the chart", "Take questions" },
        }),
        meta = new Dictionary<string, string> { ["author"] = "Ada", ["version"] = "3", ["locale"] = "en-GB" },
    });

    [GlobalSetup]
    public void Setup()
    {
        if (!ServiceRegistry.TryCreate(new Bench(), out _dispatcher))
            throw new InvalidOperationException("The Oriel generator did not run on the benchmarks.");
        _addArgs = JsonDocument.Parse("[2,3]");
        _deckArgs = JsonDocument.Parse($"[{DeckJson}]");
        var services = new ServiceTable();
        services.Add(_dispatcher);
        _page = new PageConnection(services, _ => { }, limits: PageLimits.Default);
        _addCall = """{"t":"call","id":1,"k":"page","m":"Bench.Add","a":[2,3]}""";
        _deckCall = $$"""{"t":"call","id":2,"k":"page","m":"Bench.Same","a":[{{DeckJson}}]}""";
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _page.Dispose();
        _addArgs.Dispose();
        _deckArgs.Dispose();
    }

    /// <summary>A call of two ints: the dispatcher, reading them, writing the sum.</summary>
    [Benchmark]
    public async Task<int> DispatchAdd() => Write(await _dispatcher.InvokeAsync("Add", _addArgs.RootElement, _context));

    /// <summary>A call of a 3 KB record: the generated codecs read it and write it back.</summary>
    [Benchmark]
    public async Task<int> DispatchDeck() => Write(await _dispatcher.InvokeAsync("Same", _deckArgs.RootElement, _context));

    /// <summary>A page's message to the reply posted: parsing, the thread pool, the dispatcher, encoding.</summary>
    [Benchmark]
    public Task ProtocolAdd() => _page.ReceiveAsync(_addCall);

    /// <summary>The same with a 3 KB record each way.</summary>
    [Benchmark]
    public Task ProtocolDeck() => _page.ReceiveAsync(_deckCall);

    /// <summary>Encoding an event for every page, as Event.Emit does.</summary>
    [Benchmark]
    public string EncodeEvent() => Protocol.EventMessage("Clock.Tick", w => Json.Write(w, 1234567));

    private int Write(ValueWriter? writer)
    {
        _buffer.ResetWrittenCount();
        using (var w = new Utf8JsonWriter(_buffer, Protocol.WriterOptions))
            writer?.Invoke(w);
        return _buffer.WrittenCount;
    }
}
