using System.Buffers;
using System.Text;
using System.Text.Json;
using Oriel.Generated;
using Oriel.SecureStorage;
using Oriel.Sqlite;
using Oriel.Updates;

namespace Oriel.Plugins.Tests;

/// <summary>A test of Windows only: skipped elsewhere.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Windows only";
    }
}

public sealed class PluginTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("oriel-plugins-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }

    /// <summary>Calls <paramref name="method"/> of <paramref name="service"/> as a page would, through its generated dispatcher.</summary>
    private static async Task<string?> Call(object service, string method, string args)
    {
        Assert.True(ServiceRegistry.TryCreate(service, out var dispatcher));
        using var doc = JsonDocument.Parse(args);
        var result = await dispatcher.InvokeAsync(method, doc.RootElement, new CallContext(CancellationToken.None));
        if (result is null)
            return null;
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
            result(w);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    [WindowsFact]
    public async Task KeepsSecretsEncryptedForTheUser()
    {
        var file = Path.Combine(_folder, "secrets.dat");
        var storage = new SecureStorage.SecureStorage(file, "Notes");
        Assert.Null(await storage.Get("token"));
        await storage.Set("token", "s3cr3t-✓");
        await storage.Set("other", "x");
        Assert.Equal("s3cr3t-✓", await storage.Get("token"));
        Assert.Equal(["other", "token"], await storage.Keys());

        // On disk, nothing in the clear; another instance reads it back.
        var bytes = File.ReadAllBytes(file);
        Assert.DoesNotContain("s3cr3t", Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain("token", Encoding.UTF8.GetString(bytes));
        Assert.Equal("s3cr3t-✓", await new SecureStorage.SecureStorage(file, "Notes").Get("token"));

        // Another app's entropy does not open it.
        await Assert.ThrowsAnyAsync<System.Security.Cryptography.CryptographicException>(() => new SecureStorage.SecureStorage(file, "Other").Get("token"));

        Assert.True(await storage.Delete("token"));
        Assert.False(await storage.Delete("token"));
        Assert.Equal(["other"], await storage.Keys());
        await Assert.ThrowsAsync<ArgumentException>(() => storage.Set("", "x"));
    }

    [WindowsFact]
    public async Task PagesReachTheSecretsThroughTheGeneratedService()
    {
        var storage = new SecureStorage.SecureStorage(Path.Combine(_folder, "secrets.dat"), "Notes");
        Assert.Null(await Call(storage, "Set", """["k","v"]"""));
        Assert.Equal("\"v\"", await Call(storage, "Get", """["k"]"""));
        Assert.Equal("""["k"]""", await Call(storage, "Keys", "[]"));
    }

    [Fact]
    public void QueriesWithNamedParametersAndGivesRowsAsObjects()
    {
        using var db = new SqliteDatabase(Path.Combine(_folder, "data", "notes.db"));
        db.Execute("create table notes (id integer primary key, title text, score real, done integer, data blob)");
        using var values = JsonDocument.Parse("""{"title":"Groceries ✓","score":2.5,"done":true,"$extra":null}""");
        Assert.Equal(1, db.Execute("insert into notes (title, score, done, data) values (@title, @score, @done, x'0102')", values.RootElement));
        using var second = JsonDocument.Parse("""{"title":"Taxes"}""");
        db.Execute("insert into notes (title) values ($title)", Rename(second, "$title"));

        var rows = db.Query("select id, title, score, done, data from notes order by id");
        Assert.Equal(2, rows.Length);
        Assert.Equal("""{"id":1,"title":"Groceries ✓","score":2.5,"done":1,"data":"AQI="}""", Raw(rows[0]));
        Assert.Equal("""{"id":2,"title":"Taxes","score":null,"done":null,"data":null}""", Raw(rows[1]));

        using var filter = JsonDocument.Parse("""{"q":"%tax%"}""");
        Assert.Equal("Taxes", Assert.Single(db.Query("select title from notes where title like @q", filter.RootElement)).GetProperty("title").GetString());

        using var list = JsonDocument.Parse("[1]");
        Assert.Throws<ArgumentException>(() => db.Query("select 1", list.RootElement));
        using var nested = JsonDocument.Parse("""{"a":[1]}""");
        Assert.Throws<ArgumentException>(() => db.Query("select @a", nested.RootElement));

        static JsonElement Rename(JsonDocument doc, string name) =>
            JsonDocument.Parse($"{{\"{name}\":{doc.RootElement.GetProperty("title").GetRawText()}}}").RootElement;

        static string Raw(JsonElement e) => JsonSerializer.Serialize(e, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    [Fact]
    public void OpensADatabaseInMemory()
    {
        using var db = new SqliteDatabase(":memory:");
        db.Execute("create table t (n integer)");
        Assert.Equal(1, db.Execute("insert into t values (1)"));
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task PagesQueryThroughTheGeneratedService()
    {
        using var db = new SqliteDatabase(Path.Combine(_folder, "notes.db"));
        Assert.Equal("0", await Call(db, "Execute", """["create table t (n integer)"]"""));
        Assert.Equal("1", await Call(db, "Execute", """["insert into t values (@n)", {"n": 7}]"""));
        Assert.Equal("""[{"n":7}]""", await Call(db, "Query", """["select n from t"]"""));
        Assert.Equal(ServiceConcurrency.Serial, (ServiceRegistry.TryCreate(db, out var dispatcher) ? dispatcher : null)!.Concurrency);
        // Dispose is C#'s, not the pages'.
        await Assert.ThrowsAsync<MethodNotFoundException>(() => Call(db, "Dispose", "[]"));
    }

    [Theory]
    [InlineData("https://github.com/Sutsikii/Oriel", "GithubSource")]
    [InlineData("https://example.com/releases", "SimpleWebSource")]
    [InlineData(@"\server
eleases", "SimpleFileSource")]
    [InlineData("releases", "SimpleFileSource")]
    public void FindsUpdatesWhereTheSourceSays(string source, string kind) =>
        Assert.Equal(kind, Updates.Updates.Source(source).GetType().Name);

    [Fact]
    public async Task DoesNotUpdateAnAppRunFromABuild()
    {
        Updates.Updates.Run(); // as Main does first; in a test host, it finds no hook to run
        var updates = new Updates.Updates(_folder);
        Assert.Equal(new UpdateStatus(false, null, null), updates.Status());
        await Assert.ThrowsAsync<NotInstalledException>(updates.Check);
        await Assert.ThrowsAsync<NotInstalledException>(() => updates.Download());
        Assert.Throws<NotInstalledException>(updates.Restart);
        // Pages get its code: "not-installed".
        var e = await Assert.ThrowsAsync<NotInstalledException>(() => Call(updates, "Check", "[]"));
        Assert.True(ServiceRegistry.TryGetError(e, out var code, out _));
        Assert.Equal("not-installed", code);
    }

    [Fact]
    public void ShipTheirTypeScriptClients()
    {
        var sqlite = TypeScriptClient.For(typeof(SqliteDatabase).Assembly)!;
        Assert.Contains("export const Sqlite = {", sqlite);
        Assert.Contains("query(sql: string, parameters?: unknown | null): Promise<unknown[]> {", sqlite);
        Assert.DoesNotContain("dispose(", sqlite);
        var updates = TypeScriptClient.For(typeof(Updates.Updates).Assembly)!;
        Assert.Contains("check(): Promise<AvailableUpdate | null> {", updates);
        Assert.Contains("onProgress(listener: (payload: number) => void): () => void {", updates);
        Assert.DoesNotContain("run(", updates);
        var storage = TypeScriptClient.For(typeof(SecureStorage.SecureStorage).Assembly)!;
        Assert.Contains("export const SecureStorage = {", storage);
        Assert.Contains("get(key: string): Promise<string | null> {", storage);
    }
}
