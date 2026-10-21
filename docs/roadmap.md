# Roadmap

What Oriel has, and what is planned. Items are roughly in order; each is a
self-contained piece of work.

## Done

- Services: typed calls from pages, run on the thread pool, with
  cancellation when the page goes away and the calling window.
- Events from services to pages.
- The source generator: dispatchers, JSON codecs and the TypeScript client,
  without reflection; diagnostics for what it cannot generate.
- Windows backend: Win32 windows with WebView2 through COM, no loader DLL;
  dark title bar, per-monitor DPI, app files served at `https://app.oriel/`.
- Build integration: the client written on build, the frontend copied on
  publish, NativeAOT.

## Next

### Calls and types

- **Streams**: `IAsyncEnumerable<T>` results as async iterables in pages, with
  back-pressure.
- **Events to one window**: `Event<T>.EmitTo(OrielWindow)`.
- **Polymorphism**: `[JsonDerivedType]` hierarchies as TypeScript unions.
- **Generic records** of the app's own.

### Windows and desktop

- **Native dialogs**: open, save, message boxes.
- **Menus**: the window's menu bar and context menus, with accelerators
  that reach the page when it handles them.
- **Window control**: position, size, state, full screen, frameless windows
  with drag regions, and remembering their placement.
- **Tray icon, notifications, single instance, deep links.**
- **Embedded files**: the frontend inside the executable, served through
  `WebResourceRequested`, for a single-file app.

### Tooling

- **`oriel` CLI**: `oriel new` from templates (vanilla, React, Vue,
  Svelte), `oriel dev` and `oriel build` without per-project scripts.
- **Installers**: MSIX and an Inno Setup or WiX recipe, with code signing.
- **Auto-updates**, signed.

### Platforms

- **Linux**: GTK and WebKitGTK, through `NativeLibrary` and function
  pointers, as the Windows backend does with COM.
- **macOS**: AppKit and WKWebView, through the Objective-C runtime.

See [CONTRIBUTING.md](../CONTRIBUTING.md) to pick one up.
