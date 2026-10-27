using System.Buffers;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Oriel;

/// <summary>A second instance of the app started: see <see cref="OrielApp.SecondInstanceStarted"/>.</summary>
public sealed class SecondInstanceEventArgs(IReadOnlyList<string> arguments, string workingDirectory) : EventArgs
{
    /// <summary>Its command-line arguments, without the executable.</summary>
    public IReadOnlyList<string> Arguments { get; } = arguments;

    /// <summary>The folder it started in, which relative paths in <see cref="Arguments"/> are relative to.</summary>
    public string WorkingDirectory { get; } = workingDirectory;
}

/// <summary>
/// The app was asked to open a link of its scheme, or a file: see
/// <see cref="OrielApp.OpenRequested"/>.
/// </summary>
public sealed class OpenRequestedEventArgs : EventArgs
{
    internal OpenRequestedEventArgs(Uri? url, string? path)
    {
        Url = url;
        Path = path;
    }

    /// <summary>The link, such as <c>myapp://open?id=4</c>, or null for a file.</summary>
    public Uri? Url { get; }

    /// <summary>The full path of the file, or null for a link.</summary>
    public string? Path { get; }

    /// <summary>
    /// What the command line of a launch asks to open: each argument that is
    /// a link (an absolute URL of a scheme other than <c>file</c>), or a file
    /// that exists, relative to <paramref name="workingDirectory"/>.
    /// </summary>
    internal static List<OpenRequestedEventArgs> From(IReadOnlyList<string> arguments, string workingDirectory)
    {
        var requests = new List<OpenRequestedEventArgs>();
        foreach (var argument in arguments)
        {
            if (argument.Length == 0 || argument.StartsWith('-'))
                continue;
            if (Uri.TryCreate(argument, UriKind.Absolute, out var uri) && uri.Scheme.Length > 1)
            {
                if (uri.IsFile)
                {
                    if (File.Exists(uri.LocalPath))
                        requests.Add(new OpenRequestedEventArgs(null, uri.LocalPath));
                }
                else
                {
                    requests.Add(new OpenRequestedEventArgs(uri, null));
                }
                continue;
            }
            try
            {
                var path = System.IO.Path.GetFullPath(argument, workingDirectory);
                if (File.Exists(path))
                    requests.Add(new OpenRequestedEventArgs(null, path));
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // not a path
            }
        }
        return requests;
    }
}

/// <summary>
/// The first instance of an app, for <see cref="AppOptions.SingleInstance"/>:
/// it holds a named mutex, and takes the arguments of the instances that
/// start after it on a named pipe of the user's, which they send and exit.
/// </summary>
/// <remarks>
/// The mutex decides who is first, even when two start at once; the pipe is
/// only the way in. Both are named after the app and the user, so another
/// user's instance is apart. The pipe takes no connection of another user.
/// </remarks>
internal sealed class SingleInstance : IDisposable
{
    private const int MaxMessage = 1024 * 1024;

    private readonly Mutex _mutex;
    private readonly string _pipe;
    private readonly Action<SecondInstanceEventArgs> _received;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _listening;

    private SingleInstance(Mutex mutex, string pipe, Action<SecondInstanceEventArgs> received)
    {
        _mutex = mutex;
        _pipe = pipe;
        _received = received;
        _listening = Task.Run(ListenAsync);
    }

    /// <summary>The name of the mutex and the pipe of <paramref name="appName"/>, for this user.</summary>
    internal static string Key(string appName) =>
        "oriel-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(appName + "\0" + Environment.UserName)))[..32];

    /// <summary>
    /// Makes this process the app's first instance, and returns it; or,
    /// when another one runs, sends it <paramref name="arguments"/> and
    /// returns null. Disposing the instance gives the place up, on the
    /// thread that claimed it.
    /// </summary>
    /// <param name="appName">The app's name, which the mutex and the pipe are named after.</param>
    /// <param name="arguments">This launch's arguments, sent when another instance runs.</param>
    /// <param name="workingDirectory">This launch's folder, sent with them.</param>
    /// <param name="received">Gets what later instances send, on a thread of the pool.</param>
    public static SingleInstance? Claim(string appName, IReadOnlyList<string> arguments, string workingDirectory, Action<SecondInstanceEventArgs> received)
    {
        var key = Key(appName);
        var mutex = new Mutex(false, (OperatingSystem.IsWindows() ? @"Local\" : "") + key);
        bool first;
        try
        {
            first = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            first = true; // the first instance ended without letting go
        }
        if (first)
            return new SingleInstance(mutex, key, received);
        mutex.Dispose();
        Send(key, arguments, workingDirectory);
        return null;
    }

    /// <summary>Sends the arguments to the first instance, which may still be starting: for 5 seconds.</summary>
    private static void Send(string pipe, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var message = Message(arguments, workingDirectory);
        var until = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", pipe, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                client.Connect(500);
                client.Write(message);
                client.Flush();
                return;
            }
            catch (Exception e) when (e is TimeoutException or IOException && DateTime.UtcNow < until)
            {
                Thread.Sleep(50);
            }
            catch (Exception e) when (e is TimeoutException or IOException)
            {
                Tracing.Write($"the first instance took no arguments: {e.Message}");
                return;
            }
        }
    }

    internal static byte[] Message(IReadOnlyList<string> arguments, string workingDirectory)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("cwd", workingDirectory);
            w.WriteStartArray("args");
            foreach (var argument in arguments)
                w.WriteStringValue(argument);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>What <see cref="Message"/> wrote, or null for anything else.</summary>
    internal static SecondInstanceEventArgs? Read(ReadOnlySpan<byte> message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message.ToArray());
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("cwd", out var cwd) || cwd.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("args", out var args) || args.ValueKind != JsonValueKind.Array)
                return null;
            var list = new List<string>();
            foreach (var arg in args.EnumerateArray())
            {
                if (arg.ValueKind != JsonValueKind.String)
                    return null;
                list.Add(arg.GetString()!);
            }
            return new SecondInstanceEventArgs(list, cwd.GetString()!);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(_pipe, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                using var message = new MemoryStream();
                var buffer = new byte[16 * 1024];
                int read;
                while ((read = await server.ReadAsync(buffer, _stop.Token).ConfigureAwait(false)) > 0 && message.Length <= MaxMessage)
                    message.Write(buffer, 0, read);
                if (message.Length <= MaxMessage && Read(message.GetBuffer().AsSpan(0, (int)message.Length)) is { } started)
                    _received(started);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch (IOException e)
            {
                Tracing.Write($"a second instance's message broke: {e.Message}");
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _listening.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // it stopped
        }
        _mutex.ReleaseMutex();
        _mutex.Dispose();
        _stop.Dispose();
    }
}
