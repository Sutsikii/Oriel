# Contributing to Oriel

Thanks for helping. This guide gets you from a clone to a pull request.
[docs/architecture.md](docs/architecture.md) explains how the code fits
together; read it before larger changes.

## Set up

You need:

- Windows 10 1809 or later with the WebView2 Runtime (Windows 11 has it);
- the [.NET 10 SDK](https://dotnet.microsoft.com/download) (`global.json`
  pins the feature band);
- [Node.js](https://nodejs.org) 20 or later;
- for NativeAOT publishing only: Visual Studio or its Build Tools with
  "Desktop development with C++".

```sh
git clone <this repository>
cd oriel
dotnet build
```

Windows is the only platform for now: the Linux and macOS backends are on
the [roadmap](docs/roadmap.md).

## Run the tests

| What | Command |
|---|---|
| Library, generator and build tasks | `dotnet test` (the installer test runs when Inno Setup 6 is installed) |
| Page runtime (`bridge.js`) | `node --test "tests/bridge/*.test.cjs"` |
| Generated TypeScript (type-checked with `tsc --strict`, then run) | `cd tests/typescript && npm ci && npm test` |
| Real windows | `ORIEL_E2E=1 dotnet test tests/Oriel.EndToEnd.Tests` |
| Load and leaks, in real windows, for minutes | `ORIEL_STRESS=1 dotnet test tests/Oriel.Stress.Tests` (every night in CI) |
| What a call costs | see [benchmarks/README.md](benchmarks/README.md) (in CI too) |

All of them run in CI. The end-to-end tests open windows for a few seconds
each, so they are skipped unless `ORIEL_E2E=1`.

### Property and fuzz tests

Some tests check a property over thousands of generated inputs, with a new
seed each run: the codecs round-trip any value and read any JSON or say
why not (`CodecPropertyTests`), and both ends of the protocol take any
message without throwing, replying only to calls (`PageConnectionFuzzTests`,
`tests/bridge/bridge.fuzz.test.cjs`). A failure prints its seed and the
smallest input found; replay it, then add that input to a regular test
with the fix:

```sh
dotnet test tests/Oriel.Tests -e CsCheck_Seed=<seed>      # C#, with CsCheck
BRIDGE_FUZZ_SEED=<seed> node --test tests/bridge/bridge.fuzz.test.cjs
```

To search longer, set `CsCheck_Time=60` (seconds per property) or
`BRIDGE_FUZZ_RUNS=100000`.

### Snapshot of the TypeScript client

`tests/typescript/generated/client.ts` is the client of a sample covering
every kind of type. `TypeScriptTests.MatchesTheSnapshot` compares the
generator's output to it, and `tests/typescript` type-checks and runs it.
After changing what the generator writes, update it and review the diff:

```sh
ORIEL_UPDATE_SNAPSHOTS=1 dotnet test tests/Oriel.Generators.Tests
git diff tests/typescript/generated
```

### Stress and leak tests

`tests/Oriel.Stress.Tests` opens and closes 500 windows, makes 10 000
calls at once and sends events at 120 Hz for a minute. It checks that the
live COM callbacks of the Windows backend (`ComCallback.Live`) come back
to what they were, and that handles and private memory do not grow with
the load. The stress workflow runs it every night, and on demand from the
Actions tab. For a quick run, lower `ORIEL_STRESS_WINDOWS`,
`ORIEL_STRESS_CALLS` or `ORIEL_STRESS_EVENT_SECONDS`.

### How the generator is tested

`tests/Oriel.Generators.Tests/Harness.cs` compiles C# source with the
generator, as a build would, checks there is no warning, loads the compiled
assembly and calls the generated dispatchers with JSON. So tests check
behavior (what a page gets), not the text of the generated code:

```csharp
var echo = Harness.Compile("""
    [Service] public class Echo { public int Same(int value) => value; }
    """).Bind("Echo");
Assert.Equal("4", await echo.Call("Same", "[4]"));
```

## Debug

- `ORIEL_TRACE=1` traces what the Windows backend does: the webview's
  creation, navigations, and every message from pages.
- `WindowOptions.DevTools = true` (the default in development) allows the
  page's developer tools: right-click, Inspect, or `OrielWindow.OpenDevTools()`.
- The generated C# is in `obj/.../generated/Oriel.Generators/` when the
  project sets `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>`.

## Write code

- Match the code around you: names, comment density, idioms.
  `.editorconfig` has the formatting rules, and the build treats warnings as
  errors.
- Comments say why, and what is not obvious; public APIs have XML
  documentation, which the build requires.
- Nothing may need reflection at run time: the library is checked for
  NativeAOT (`IsAotCompatible`), and the example publishes with it.
- New behavior comes with tests at the lowest level that can show it: a
  unit test against the fake backend before an end-to-end test.
- The generator targets netstandard2.0 and runs inside compilers: no
  dependency beyond Roslyn, and no I/O.

## Change the public API

The public API of `src/Oriel` is declared in `PublicAPI.Shipped.txt` (what
a release has) and `PublicAPI.Unshipped.txt` (what was added since). The
build fails on a public member, or a change of signature, that is not
declared there, so every API change shows in review:

- add the line the error names to `PublicAPI.Unshipped.txt`, or move it
  there from `Shipped` when the change breaks a released API, and say so in
  the pull request;
- a release moves the lines of `Unshipped` to `Shipped`.

The `Oriel.Generated` namespace, hidden from completion, is what generated
code calls. It is declared too, but it is not for apps: it changes with
the generator, which ships in the same package.

An API still expected to change gets
`[Experimental(Experiments.Something, UrlFormat = Experiments.Url)]`, with
a new id in [`Experiments.cs`](src/Oriel/Experiments.cs) and a row in
[diagnostics.md](docs/diagnostics.md#experimental-apis). Its lines in the
API files start with the id, such as `[ORIEL901]`. Making it stable removes
the attribute, the id's row, and the prefix.

Before 1.0, minor versions may break APIs, each one listed in the release
notes. 1.0 requires that no API be experimental without a reason, and from
then on breaking changes wait for a major version.

## Commits and pull requests

- One change per commit, which builds and passes the tests on its own.
- Messages: a summary line in the imperative, under about 72 characters
  ("Pass the calling window to service methods"), a blank line, then what
  changed and why, wrapped at 72 columns.
- A pull request says what it changes and how it was tested. Update the
  documentation in the same pull request as the behavior it describes:
  user-visible changes in `docs/`, design changes in
  `docs/architecture.md`.

## Where to start

The [roadmap](docs/roadmap.md) lists what is planned, from small, well
scoped tasks to new platforms. Open an issue first for larger changes, so
the design can be discussed before the code.
