using System.Diagnostics;
using Oriel;

namespace ChessNextMove;

/// <summary>A line the engine found: its score and the moves that follow.</summary>
/// <param name="Rank">1 for the best line, then 2, 3.</param>
/// <param name="Cp">The score in centipawns, from White's side; null when the line mates.</param>
/// <param name="Mate">Moves to mate, positive when White mates, 0 when the side to move is mated; null when the line does not mate.</param>
/// <param name="Moves">The moves, in UCI notation: e2e4, e7e8q.</param>
public record Line(int Rank, int? Cp, int? Mate, List<string> Moves);

/// <summary>Where a search is.</summary>
/// <param name="Search">The number the page gave the search.</param>
/// <param name="Fen">The position searched.</param>
/// <param name="Depth">How many moves deep the engine looked.</param>
/// <param name="Nodes">Positions looked at.</param>
/// <param name="Nps">Positions looked at per second.</param>
/// <param name="Lines">The best lines, best first.</param>
/// <param name="BestMove">The move the engine plays, once it is done; null when there is none, as in a mate.</param>
/// <param name="Done">Whether the search is over.</param>
public record Analysis(int Search, string Fen, int Depth, long Nodes, long Nps, List<Line> Lines, string? BestMove, bool Done);

/// <summary>The engine, as the page shows it.</summary>
/// <param name="Name">Its name, as it gives it: "Stockfish 17.1".</param>
/// <param name="Found">Whether its executable is there.</param>
/// <param name="Path">Where its executable is looked for.</param>
public record EngineInfo(string Name, bool Found, string Path);

/// <summary>Stockfish, run as a process of the app and spoken to in UCI.</summary>
[Service]
public sealed class Engine : IDisposable
{
    private static readonly TimeSpan s_throttle = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan s_answer = TimeSpan.FromSeconds(15);

    private readonly string _path;
    private readonly SemaphoreSlim _turn = new(1, 1); // one search at a time
    private readonly Lock _lock = new(); // the process, and what waits on its output
    private Process? _process;
    private string _name = "Stockfish";
    private string? _expected;
    private TaskCompletionSource? _answered;
    private TaskCompletionSource<string?>? _bestMove;
    private Action<string>? _info;
    private int _tickets;

    public Engine(string path) => _path = path;

    /// <summary>Where Stockfish is: CHESS_ENGINE when it is set, else engine/stockfish.exe next to the app.</summary>
    [ServiceIgnore]
    public static string Locate()
    {
        var custom = Environment.GetEnvironmentVariable("CHESS_ENGINE");
        return string.IsNullOrEmpty(custom) ? Path.Combine(AppContext.BaseDirectory, "engine", "stockfish.exe") : custom;
    }

    /// <summary>Fires as a search goes deeper, a few times a second, and once more when it is done.</summary>
    public Event<Analysis> Progress { get; } = new();

    /// <summary>The engine: its name, and whether it was found.</summary>
    public async Task<EngineInfo> Info(CancellationToken cancellation)
    {
        if (File.Exists(_path))
        {
            await _turn.WaitAsync(cancellation);
            try
            {
                await StartAsync(cancellation);
            }
            finally
            {
                _turn.Release();
            }
        }
        return new EngineInfo(_name, File.Exists(_path), _path);
    }

    /// <summary>Searches <paramref name="fen"/> for the best moves. A new search stops the one before it.</summary>
    /// <param name="search">A number the page gives the search, to tell its progress apart.</param>
    /// <param name="fen">The position, in FEN.</param>
    /// <param name="depth">How many moves deep to look.</param>
    /// <param name="milliseconds">How long to look at most; 0 for as long as the depth takes.</param>
    /// <param name="lines">How many of the best lines to find.</param>
    /// <returns>The search, done.</returns>
    public async Task<Analysis> Analyse(int search, string fen, int depth, int milliseconds = 0, int lines = 1, CancellationToken cancellation = default)
    {
        Check(fen);
        depth = Math.Clamp(depth, 1, 60);
        lines = Math.Clamp(lines, 1, 5);
        var whiteToMove = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1] == "w";

        var ticket = Interlocked.Increment(ref _tickets);
        Send("stop"); // the search before this one ends, and returns what it found
        await _turn.WaitAsync(cancellation);
        try
        {
            if (ticket != Volatile.Read(ref _tickets))
                throw new OperationCanceledException("A newer search replaced this one.");
            await StartAsync(cancellation);
            Send($"setoption name MultiPV value {lines}");
            Send($"position fen {fen}");
            await ExpectAsync("isready", "readyok", cancellation);
            if (ticket != Volatile.Read(ref _tickets))
                throw new OperationCanceledException("A newer search replaced this one.");

            // Written by the thread that reads the engine, read once the search is over.
            var found = new Line?[lines];
            int reached = 0;
            long nodes = 0, nps = 0;
            var emitted = 0L;
            Analysis Snap(bool done, string? best) => new(search, fen, reached, nodes, nps, [.. found.OfType<Line>()], best, done);

            var bestMove = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_lock)
            {
                _bestMove = bestMove;
                _info = text =>
                {
                    if (Parse(text, whiteToMove) is not { } info)
                        return;
                    reached = Math.Max(reached, info.Depth);
                    nodes = info.Nodes;
                    nps = info.Nps;
                    if (info.Line.Rank <= lines)
                        found[info.Line.Rank - 1] = info.Line;
                    if (info.Line.Rank == lines && Stopwatch.GetElapsedTime(emitted) >= s_throttle)
                    {
                        emitted = Stopwatch.GetTimestamp();
                        Progress.Emit(Snap(false, null));
                    }
                };
            }

            Send(milliseconds > 0 ? $"go depth {depth} movetime {milliseconds}" : $"go depth {depth}");
            string? best;
            using (cancellation.Register(() => Send("stop")))
                best = await bestMove.Task;

            var result = Snap(true, best);
            Progress.Emit(result);
            return result;
        }
        finally
        {
            lock (_lock)
            {
                _bestMove = null;
                _info = null;
            }
            _turn.Release();
        }
    }

    /// <summary>Stops the search, which then returns what it found.</summary>
    public void Stop() => Send("stop");

    [ServiceIgnore]
    public void Dispose()
    {
        Process? process;
        lock (_lock)
            process = _process;
        if (process is null)
            return;
        Send("quit");
        if (!process.WaitForExit(1000))
            process.Kill();
        process.Dispose();
    }

    private async Task StartAsync(CancellationToken cancellation)
    {
        lock (_lock)
        {
            if (_process is { HasExited: false })
                return;
        }
        if (!File.Exists(_path))
            throw new FileNotFoundException($"Stockfish is not at {_path}. Run setup.ps1 to download it.");

        var process = new Process
        {
            StartInfo = new ProcessStartInfo(_path)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(_path),
            },
        };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is { } text)
                Read(text);
            else
                Stopped(process);
        };
        process.Start();
        process.BeginOutputReadLine();
        lock (_lock)
            _process = process;

        await ExpectAsync("uci", "uciok", cancellation);
        Send($"setoption name Threads value {Math.Max(1, Environment.ProcessorCount / 2)}");
        Send("setoption name Hash value 256");
        await ExpectAsync("isready", "readyok", cancellation);
    }

    /// <summary>Sends <paramref name="command"/> and waits for the engine to answer <paramref name="answer"/>.</summary>
    private async Task ExpectAsync(string command, string answer, CancellationToken cancellation)
    {
        var answered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            _expected = answer;
            _answered = answered;
        }
        Send(command);
        await answered.Task.WaitAsync(s_answer, cancellation);
    }

    private void Send(string command)
    {
        lock (_lock)
        {
            if (_process is not { HasExited: false } process)
                return;
            try
            {
                process.StandardInput.WriteLine(command);
            }
            catch (IOException)
            {
                // The engine is gone: Stopped fails what waited on it.
            }
        }
    }

    private void Read(string text)
    {
        Action<string>? info;
        lock (_lock)
        {
            if (text.StartsWith("id name ", StringComparison.Ordinal))
                _name = text[8..];
            else if (text == _expected)
                _answered?.TrySetResult();
            else if (text.StartsWith("bestmove", StringComparison.Ordinal))
            {
                var move = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1);
                _bestMove?.TrySetResult(move is null or "(none)" ? null : move);
            }
            if (!text.StartsWith("info ", StringComparison.Ordinal))
                return;
            info = _info;
        }
        info?.Invoke(text);
    }

    private void Stopped(Process process)
    {
        var error = new InvalidOperationException("The engine stopped. The position may be one it cannot search.");
        lock (_lock)
        {
            if (_process == process)
                _process = null;
            _answered?.TrySetException(error);
            _bestMove?.TrySetException(error);
        }
    }

    private sealed record Report(int Depth, long Nodes, long Nps, Line Line);

    /// <summary>Reads an info line of a search: "info depth 12 multipv 1 score cp 35 nodes 9000 nps 1000 pv e2e4 e7e5".</summary>
    private static Report? Parse(string text, bool whiteToMove)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int depth = 0, rank = 1;
        int? cp = null, mate = null;
        long nodes = 0, nps = 0;
        var moves = new List<string>();
        for (var i = 1; i < words.Length; i++)
        {
            switch (words[i])
            {
                case "string" or "currmove":
                    return null;
                case "lowerbound" or "upperbound":
                    return null; // a score the engine is not sure of yet
                case "depth" when i + 1 < words.Length:
                    depth = int.Parse(words[++i]);
                    break;
                case "multipv" when i + 1 < words.Length:
                    rank = int.Parse(words[++i]);
                    break;
                case "nodes" when i + 1 < words.Length:
                    nodes = long.Parse(words[++i]);
                    break;
                case "nps" when i + 1 < words.Length:
                    nps = long.Parse(words[++i]);
                    break;
                case "score" when i + 2 < words.Length:
                    var value = int.Parse(words[i + 2]);
                    // UCI scores are from the side to move; pages read them from White's.
                    if (!whiteToMove)
                        value = -value;
                    if (words[i + 1] == "mate")
                        mate = value;
                    else
                        cp = value;
                    i += 2;
                    break;
                case "pv":
                    moves.AddRange(words.AsSpan(i + 1));
                    i = words.Length;
                    break;
            }
        }
        if (cp is null && mate is null)
            return null;
        return new Report(depth, nodes, nps, new Line(rank, cp, mate, moves));
    }

    /// <summary>Refuses what would make Stockfish stop: a position with no king, or more than one.</summary>
    private static void Check(string fen)
    {
        var fields = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 4 || fields[0].Split('/').Length != 8 || fields[1] is not ("w" or "b"))
            throw new ArgumentException("The FEN is not valid.");
        if (fields[0].Count(c => c == 'K') != 1 || fields[0].Count(c => c == 'k') != 1)
            throw new ArgumentException("Each side needs exactly one king.");
    }
}
