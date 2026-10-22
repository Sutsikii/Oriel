# Roadmap

What Oriel has, and what is planned. Items are roughly in order; each is a
self-contained piece of work.

## Done

- Services: typed calls from pages, run on the thread pool, with
  cancellation when the page goes away and the calling window.
- Events from services to pages, to every window or to one; windows have
  ids, and names to find them by.
- The source generator: dispatchers, JSON codecs and the TypeScript client,
  without reflection; diagnostics for what it cannot generate.
- Windows backend: Win32 windows with WebView2 through COM, no loader DLL;
  dark title bar, per-monitor DPI, app files served at `https://app.oriel/`;
  pages recover from failures of the webview's processes; windows stay on the
  app, open other sites in the browser, and grant no permission by default.
- Build integration: the client written on build, the frontend copied on
  publish, NativeAOT.
- Installers: `OrielInstaller` builds a per-user Inno Setup installer on
  publish, with shortcuts, an uninstaller and a WebView2 check.
- Native dialogs, modal to a window: open one or several files, save,
  pick a folder, message boxes.
- Printing: print dialogs, and pages to PDF files with their layout.
- `Oriel.Hosting`: apps on the .NET Generic Host, with services from
  dependency injection (for the app, per window, per call), `ILogger`,
  configuration and hosted services.

## Next

### Calls and types

- **Streams**: `IAsyncEnumerable<T>` results as async iterables in pages, with
  back-pressure.
- **Polymorphism**: `[JsonDerivedType]` hierarchies as TypeScript unions.
- **Generic records** of the app's own.

### Windows and desktop

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
- **Signing** of the app and its installer, from a certificate or a
  signing service.
- **MSIX** packages, for the Microsoft Store.
- **Auto-updates**, signed.

### Platforms

- **Linux**: GTK and WebKitGTK, through `NativeLibrary` and function
  pointers, as the Windows backend does with COM.
- **macOS**: AppKit and WKWebView, through the Objective-C runtime.

See [CONTRIBUTING.md](../CONTRIBUTING.md) to pick one up.
