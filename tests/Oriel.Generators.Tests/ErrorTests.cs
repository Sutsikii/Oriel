using System.Buffers;
using System.Text;
using System.Text.Json;
using Oriel.Generated;

namespace Oriel.Generators.Tests;

/// <summary>How [ServiceError] exceptions reach pages.</summary>
public class ErrorTests
{
    private const string Source = """
        namespace App;

        public enum Reason { Edited, Deleted }

        /// <summary>Someone else changed the deck.</summary>
        [ServiceError("conflict")]
        public class ConflictException(string id, Reason reason, int? version) : Exception($"{id} was changed")
        {
            /// <summary>The deck that changed.</summary>
            public string Id { get; } = id;
            public Reason Reason { get; } = reason;
            public int? Version { get; } = version;
            public override string Message => "changed: " + Id;
        }

        [ServiceError("not-found")]
        public class NotFoundException : Exception
        {
        }

        public class StaleException(string id) : ConflictException(id, Reason.Edited, null);

        [Service]
        public class Decks
        {
            public void Save(string id) => throw new ConflictException(id, Reason.Deleted, 3);
            public void Refresh(string id) => throw new StaleException(id);
        }
        """;

    private static (string Code, string Data)? Error(Exception e)
    {
        if (!Oriel.Generated.ServiceRegistry.TryGetError(e, out var code, out var data))
            return null;
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
            data(w);
        return (code, Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    [Fact]
    public async Task GivesTheCodeAndThePublicPropertiesOfTheException()
    {
        var decks = Harness.Compile(Source).Bind("App.Decks");
        var e = await Assert.ThrowsAnyAsync<Exception>(() => decks.Call("Save", """["d1"]"""));

        Assert.Equal(("conflict", """{"id":"d1","reason":"deleted","version":3}"""), Error(e));
    }

    [Fact]
    public async Task GivesTheErrorOfTheNearestBaseTypeToDerivedExceptions()
    {
        var decks = Harness.Compile(Source).Bind("App.Decks");
        var e = await Assert.ThrowsAnyAsync<Exception>(() => decks.Call("Refresh", """["d2"]"""));

        Assert.Equal(("conflict", """{"id":"d2","reason":"edited","version":null}"""), Error(e));
    }

    [Fact]
    public void LeavesOtherExceptionsWithoutACode()
    {
        Harness.Compile(Source).Bind("App.Decks");
        Assert.Null(Error(new InvalidOperationException("no")));
    }

    [Fact]
    public void WritesAUnionOfTheErrorsInTypeScript()
    {
        var ts = Harness.Compile(Source).TypeScript;
        Assert.Contains(
            "export type CallError = CallErrorBase & ({ readonly code: \"conflict\"; readonly data: ConflictException } | { readonly code: \"not-found\"; readonly data: NotFoundException } | { readonly code: null; readonly data: null });",
            ts);
        Assert.Contains("""
            /** Someone else changed the deck. */
            export interface ConflictException {
              /** The deck that changed. */
              readonly id: string;
              readonly reason: Reason;
              readonly version: number | null;
            }
            """.ReplaceLineEndings("\n"), ts.ReplaceLineEndings("\n"));
        Assert.Contains("export interface NotFoundException {\n}", ts.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void WritesErrorsOfProjectsWithoutServices()
    {
        var harness = Harness.Compile("""
            [ServiceError("busy")]
            public class BusyException : Exception { public int Seconds { get; init; } }
            """);
        Assert.Empty(harness.GeneratorDiagnostics);
        Assert.Contains("readonly code: \"busy\"", harness.TypeScript);
    }
}
