# Architecture

How Oriel is put together, for people changing it. Read it before working on
the generator or a platform backend.

## Goals

- **Typed calls without ceremony.** A `[Service]` class is all it takes; the
  TypeScript client follows the C# code, documentation included.
- **No reflection, so NativeAOT.** Everything that depends on the app's types
  is generated at compile time. A published app is a native executable of
  about 2 MB.
- **No native dependency of its own.** The system webview is driven directly
  (through COM on Windows): no C++ or Rust library, no WebView2 SDK, no
  loader DLL to ship.
- **The logic is tested without windows.** Backends are thin; everything
  else runs, and is tested, against a fake backend.

## Repository layout

```
src/
  Oriel/                    the library apps reference
    OrielApp.cs             the app: services, windows, event loop, events
    OrielWindow.cs          a window and its page connection
    Event.cs                Event<T>, what services emit
    Frontend.cs             where pages come from, and which origins are trusted
    TypeScriptClient.cs     the clients the generator registered
    Bridge/bridge.js        the page runtime, injected into every page
    Ipc/                    the protocol: PageConnection, Protocol, ServiceTable
    Generated/              what generated code builds on: dispatchers, JSON codecs
    Platform/
      IPlatform.cs          the contract of a backend
      Windows/              Win32 and WebView2
    build/                  MSBuild props and targets, shipped in the package
  Oriel.Generators/         the source generator (netstandard2.0)
  Oriel.Build/              the MSBuild tasks of apps: the installer script (netstandard2.0)
tests/
  Oriel.Tests/              the library, with a fake backend
  Oriel.Generators.Tests/   the generator: compiles code, calls what it generated
  Oriel.Build.Tests/        the build tasks, and an installer compiled when Inno Setup is there
  Oriel.EndToEnd.Tests/     real windows (ORIEL_E2E=1)
  bridge/                   bridge.js, with node --test
  typescript/               the generated client, type-checked and run
examples/hello/             an app, built from source
examples/chess-next-move/   an app that runs a process: Stockfish, spoken to in UCI
docs/                       this documentation
```

## How a call travels

```
page                         app (thread pool)                      service
 │ Greeter.greet("Ada")       │                                      │
 │ ─ call ─────────────────▶  PageConnection.ReceiveAsync            │
 │   {"t":"call","m":         │  ServiceTable["Greeter"]             │
 │    "Greeter.Greet",...}    │  Greeter_Dispatcher.InvokeAsync ───▶ Greet("Ada")
 │                            │  ◀──────────────────────────────── "Hello, Ada"
 │ ◀─ result ──────────────── Protocol.ResultMessage                 │
 │   promise resolves         │  (posted on the UI thread)           │
```

1. The generated client calls `window.oriel.call("Greeter.Greet", ["Ada"])`.
2. `bridge.js` posts a `call` message with the page's token ([protocol.md](protocol.md)).
3. The backend hands the message to the window on the UI thread;
   `OrielWindow` drops it unless it comes from a trusted origin.
4. `PageConnection` parses it and runs the call **on the thread pool**,
   through the service's generated dispatcher.
5. The dispatcher reads the arguments with the generated codecs, calls the
   method, and returns a writer of the result.
6. The reply is posted back to the page on the UI thread, unless the page went
   away meanwhile.

## Threads

- **The UI thread** is Oriel's own (`OrielApp.Run` starts it, in a
  single-threaded COM apartment as WebView2 requires). Backends call into
  Oriel on it, and every `IPlatform`/`IPlatformWindow` member but `Post` is
  only called on it.
- **Calls** run on the thread pool, so services never block the UI.
- **`OrielApp.Dispatch`** runs an action on the UI thread; `OrielWindow`'s
  members use it, so they can be called from any thread.

## The source generator

[`OrielGenerator`](../src/Oriel.Generators/OrielGenerator.cs) is an
incremental generator:

```
[Service] classes ──ServiceParser──▶ ServiceResult (model + diagnostics)
                                         │  one per class, cached
                         Collect + Merge ▼
                                      Project ──▶ TypeScriptEmitter ──▶ oriel.ts (as a string)
                                         └──────▶ CSharpEmitter ─────▶ Oriel.Services.g.cs
```

- **ServiceParser** reads a class with Roslyn's symbols. **TypeMapper** maps
  each type to a `TypeRef` (primitive, enum, object, nullable, array, list,
  dictionary) and defines the enums and objects it meets, recursively.
  Anything it cannot map becomes a diagnostic, with the reason.
- The **model** ([`Model.cs`](../src/Oriel.Generators/Model.cs)) is made of
  records and `EquatableArray`s: values compared by content, so the pipeline
  skips the emitters when an edit changed no service. A test checks it.
- **CSharpEmitter** writes, in one file:
  - `OrielModule`, a module initializer that registers each dispatcher with
    `ServiceRegistry` and the TypeScript client with `TypeScriptClient`;
  - a `file`-scoped dispatcher per service, a `switch` on method names;
  - `Codecs`, a reader and a writer per enum and object. `init` and
    `required` members are set through `UnsafeAccessor`, which needs no
    reflection.
- **TypeScriptEmitter** writes the client from the same model, so the two
  sides cannot disagree.

Documentation comments are read from the syntax when the project does not
generate XML documentation ([`DocComments`](../src/Oriel.Generators/DocComments.cs)).

### Adding a type mapping

1. Map it in `TypeMapper` (a primitive goes in `Primitive`, with the name of
   its `Json.Read*` method and its TypeScript type).
2. Add its `Json.Read*` method and `Json.Write` overload in
   [`Generated/Json.cs`](../src/Oriel/Generated/Json.cs), with tests in
   `tests/Oriel.Tests/Generated`.
3. Add a round trip to `CodecTests.RoundTripsBuiltInTypes`, and the row to
   the table of [bindings.md](bindings.md#types).

## The Windows backend

[`Platform/Windows`](../src/Oriel/Platform/Windows) implements `IPlatform`
with Win32 and WebView2:

- **`WebView2Runtime`** finds the runtime as `WebView2Loader.dll` does:
  EdgeUpdate's registry records, most stable channel first, or the fixed
  version runtime named by `WEBVIEW2_BROWSER_EXECUTABLE_FOLDER`. The
  environment is created by the runtime's own client library
  (`CreateWebViewEnvironmentWithOptionsInternal`), so no loader is shipped.
- **`Com`** calls COM objects through their vtables, by slot number, with
  function pointers. **`ComCallback`** makes the COM objects WebView2 calls
  back (completed handlers, event handlers) in native memory.
- **`WebView2`** names the slots Oriel uses. Each slot is the index of the
  method in its interface's vtable in `WebView2.h`, IUnknown's three methods
  first; the comment above each wrapper names the method.
- **`WindowsPlatform`** runs the message loop and dispatches `Post`ed actions
  through a message-only window. **`WindowsWebWindow`** is a window and its
  WebView2 controller: it injects `bridge.js` at document start, maps
  `https://app.oriel/` to the app's files, and forwards messages and title
  changes.

### When the webview fails

WebView2 runs pages in processes of its own, which can fail: a renderer
runs out of memory, a GPU driver crashes, the browser process is killed.
The backend reports each `ProcessFailed` as a `WebViewFailure`, and
`OrielWindow` decides with its `RecoveryPolicy`, in shared code:

| Failure | Recovery |
|---|---|
| The page's renderer exited or hangs | Reload the page |
| The browser process exited | Recreate the webview (and the environment, which died with it) |
| A frame's renderer, the GPU process, another helper | Nothing: the page goes on, or WebView2 restarts the process |
| Three recoveries within a minute | Show an error page with a link to try again, instead of looping |

`OrielWindow.WebViewFailed` is raised first, so apps can log the failure
or choose another recovery; failures that needed one are also reported to
`OrielApp.Error`. The end-to-end tests kill a real renderer to check the
page comes back.

### Calling another WebView2 method

1. Find the interface's vtable in `WebView2.h` (from the
   `Microsoft.Web.WebView2` package) and count the method's slot, from 0 at
   `QueryInterface`. Interfaces like `ICoreWebView2_3` extend the previous
   ones: their slots continue after the base interface's last one.
2. Add a wrapper to `WebView2.cs` with a comment naming the slot and method,
   and its signature in the function pointer type: `HRESULT` is `int`, `BOOL`
   is `int`, strings are `char*`, interfaces are `nint`.
3. Strings WebView2 returns are freed with `Com.TakeString`; interfaces it
   returns are released with `Com.Release`; objects it passes to callbacks
   are borrowed for the call (`Com.AddRef` to keep one).

## Adding a platform

A backend implements `IPlatform` and `IPlatformWindow`
([`IPlatform.cs`](../src/Oriel/Platform/IPlatform.cs)) in a folder of its
own, and `Platforms.Current` picks it. It must:

- run its event loop on the thread that calls `Run`, and run `Post`ed actions
  on it;
- open a window showing `PlatformWindowOptions.Url`, with
  `BridgeScript` injected at document start in the top frame, and
  `window.chrome.webview`'s `postMessage`/`message` replaced by the
  system's message channel (bridge.js looks for `chrome.webview`: give it
  that shape);
- serve `AssetsFolder` at `https://app.oriel/` when it is set;
- report each message with the URL of the document that sent it, title
  changes, and the window's end through `IPlatformWindowEvents`.

Everything else (services, the protocol, origins, events, quitting) is
shared, and covered by the tests of `OrielApp` against the fake backend.
