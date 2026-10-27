using System.Text.Json;

namespace Oriel.Benchmarks;

/// <summary>The data the page of <see cref="BinaryResults"/> asks for.</summary>
[Service(Name = "Payload")]
public sealed class PayloadProbe
{
    private readonly Dictionary<int, byte[]> _data = [];

    public TaskCompletionSource<string> Reported { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary><paramref name="megabytes"/> MB as binary data: a Uint8Array.</summary>
    public ReadOnlyMemory<byte> Binary(int megabytes) => Data(megabytes);

    /// <summary>The same as JSON: a base64 string.</summary>
    public byte[] Json(int megabytes) => Data(megabytes);

    public void Report(string result) => Reported.TrySetResult(result);

    private byte[] Data(int megabytes)
    {
        lock (_data)
        {
            if (!_data.TryGetValue(megabytes, out var data))
            {
                data = new byte[megabytes * 1024 * 1024];
                new Random(megabytes).NextBytes(data);
                _data[megabytes] = data;
            }
            return data;
        }
    }
}

/// <summary>
/// How long a page waits for 1, 10 and 100 MB, from the call to a
/// Uint8Array in its hands: as binary data, and as JSON (base64, decoded).
/// </summary>
internal static class BinaryResults
{
    public sealed record Row(int Megabytes, double BinaryMs, double JsonMs);

    public static async Task<Row[]> Run(int runs = 5)
    {
        var assets = Directory.CreateTempSubdirectory("oriel-binary-").FullName;
        var name = "Oriel Binary " + Guid.NewGuid().ToString("N")[..8];
        File.WriteAllText(Path.Combine(assets, "index.html"), $$"""
            <!doctype html>
            <script type="module">
            const decode = (s) => Uint8Array.fromBase64 ? Uint8Array.fromBase64(s) : Uint8Array.from(atob(s), (c) => c.charCodeAt(0));
            const median = async (f) => {
              const times = [];
              for (let i = 0; i < {{runs}}; i++) {
                const started = performance.now();
                const bytes = await f();
                times.push(performance.now() - started);
                if (!(bytes instanceof Uint8Array)) throw new Error("not bytes");
              }
              return times.sort((a, b) => a - b)[Math.floor(times.length / 2)];
            };
            await oriel.call("Payload.Binary", [1]); // warm up
            await oriel.call("Payload.Json", [1]);
            const rows = [];
            for (const megabytes of [1, 10, 100]) {
              const binaryMs = await median(() => oriel.call("Payload.Binary", [megabytes]));
              const jsonMs = await median(async () => decode(await oriel.call("Payload.Json", [megabytes])));
              rows.push({ megabytes, binaryMs, jsonMs });
            }
            await oriel.call("Payload.Report", [JSON.stringify(rows)]);
            </script>
            """);
        try
        {
            var app = new OrielApp(new AppOptions { Name = name, AssetsDirectory = assets });
            var probe = new PayloadProbe();
            app.Bind(probe);
            var run = Task.Run(() => app.Run(() => app.OpenWindow(new WindowOptions { Width = 400, Height = 300 })));
            var report = await probe.Reported.Task.WaitAsync(TimeSpan.FromMinutes(10));
            foreach (var window in app.Windows)
                window.Close();
            await run.WaitAsync(TimeSpan.FromSeconds(30));
            return JsonSerializer.Deserialize<Row[]>(report, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        }
        finally
        {
            try
            {
                Directory.Delete(assets, recursive: true);
            }
            catch (IOException)
            {
                // the webview's processes may hold it for a moment
            }
        }
    }
}
