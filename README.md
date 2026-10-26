# Oriel

**Desktop apps in C#, with a web frontend.**

An oriel is a window that juts out of a wall. Oriel opens native windows
that show your web frontend in the webview the system already has, and lets
the page call your C# code through a **TypeScript client generated from it**.

```csharp
/// <summary>Greets people.</summary>
[Service]
public class Greeter
{
    /// <summary>Returns a greeting for <paramref name="name"/>.</summary>
    public Greeting Greet(string name, Tone tone = Tone.Friendly) => new($"Hello, {name}!", tone);

    /// <summary>Fires when someone was greeted.</summary>
    public Event<string> Greeted { get; } = new();
}

var app = new OrielApp(new AppOptions { Name = "Hello" });
app.Bind(new Greeter());
return app.Run(() => app.OpenWindow(new WindowOptions { Width = 800, Height = 600 }));
```

```ts
import { CallError, Greeter } from "./oriel"; // generated from the C# above

const greeting = await Greeter.greet("Ada", "pirate"); // { text: string; tone: Tone }
Greeter.onGreeted((name) => console.log(`greeted ${name}`));
```

## Why Oriel

- **Typed calls, no glue code.** Mark a class `[Service]`: its methods,
  records, enums, nullable types and events show up in TypeScript, with their
  documentation comments. Change the C#, and the page's compiler tells you
  what broke.
- **Any frontend.** Vite, React, Vue, Svelte, or plain HTML: the client is a
  dependency-free module.
- **Small and native.** No reflection anywhere, so apps publish with
  NativeAOT: the example is a **1.9 MB** executable. No browser engine is
  bundled, and no native library of Oriel's own: WebView2 is driven directly
  through COM.
- **Safe by default.** Only the app's own pages can call services; calls of a
  page that navigated away are canceled, and their results never reach the
  next page.
- **A real install.** `dotnet publish` can end with a setup wizard: per-user
  install, Start menu shortcut, uninstaller, and a check for WebView2.
- **Errors at compile time.** A type that cannot cross to pages is reported
  where it is used, with the reason, instead of failing at run time.

The .NET options for a web UI each leave something out: Blazor Hybrid needs
Razor rather than a JavaScript frontend, Electron.NET bundles Chromium and
Node, and lighter webview wrappers leave the calls untyped. Oriel is built
around the typed bridge, the way [Wails](https://wails.io), Tauri and
[MyGo](https://github.com/egoist/mygo) are in Go and Rust.

## Try it

On Windows 10 or 11, with the .NET 10 SDK and Node.js:

```sh
dotnet new install Oriel.Templates
dotnet new oriel -o MyApp
cd MyApp/frontend
npm install
npm run dev
```

[Getting started](docs/getting-started.md) goes on from there.

## Measured

The [hello example](examples/hello), published with NativeAOT, on Windows 11:

| | |
|---|---|
| Executable | 1.9 MB, plus 5 KB of frontend |
| Window shown | at once; page loaded and calling C# after about 0.4 s |
| Memory, the app's process | 19 MB working set, 5 MB private |
| Memory, with the 6 WebView2 processes | 318 MB working set, 150 MB private |

The second memory line is the one to compare: the webview's processes are
most of an app's footprint, as in every webview-based framework.

## Status

Oriel is young. It runs on **Windows 10 1809 and later**, with the WebView2
Runtime, on **Linux** with GTK 3 and WebKitGTK ([linux.md](docs/linux.md)),
and on **macOS** with AppKit and WKWebView ([macos.md](docs/macos.md)).
The Linux and macOS backends have the core, and not yet dialogs, menus or
printing. The macOS one is tested in CI only, since the maintainers have no
Mac yet: if you have one, your help is welcome. The calls, events, generator, Windows
backend, window control, dialogs, menus and installers are done and tested;
tray icons, single instance, signing and a CLI are next. See the [roadmap](docs/roadmap.md).

## Documentation

- [Getting started](docs/getting-started.md): a first app, from service to
  native executable.
- [Services and the TypeScript client](docs/bindings.md): what pages can
  call, and how types map.
- [Hosting](docs/hosting.md): dependency injection, services per call or
  per window, `ILogger` and hosted services, with `Oriel.Hosting`.
- [Dialogs](docs/dialogs.md): files to open, where to save, folders and
  messages, modal to a window.
- [Windows](docs/windows.md): position, size, states, full screen,
  frameless windows, and placements remembered across runs.
- [Menus](docs/menus.md): the menu bar, context menus, and accelerators
  the page gets first.
- [Printing](docs/printing.md): print dialogs, and pages to PDF files.
- [Building and shipping](docs/build.md): build properties, development
  loop, publishing.
- [WebView2](docs/webview2.md): the runtime apps need, its updates, and
  machines without it.
- [Linux](docs/linux.md): what the Linux backend has, and how it differs.
- [macOS](docs/macos.md): what the macOS backend has, how it differs, and
  where help is wanted.
- [Generator diagnostics](docs/diagnostics.md): ORIEL001 to ORIEL006, and
  the experimental APIs.
- [The page protocol](docs/protocol.md): the messages under the client.
- [Architecture](docs/architecture.md): how it works, for contributors.

## Contributing

Contributions are welcome: [CONTRIBUTING.md](CONTRIBUTING.md) covers the
setup, the tests and the conventions, and the [roadmap](docs/roadmap.md)
lists what to pick up.

## License

[MIT](LICENSE).
